using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Collections.Generic;

namespace soniox_test_2.Services
{
    public class SonioxWebSocketHandler
    {
        private const string SonioxApiKey = "ca5264a8fdf73eacc2bf3f9729f5892acbe13ad37322664748da4e296cbc75f3";
        private const string SonioxWsUrl = "wss://stt-rt.soniox.com/transcribe-websocket";

        public async Task HandleAsync(HttpContext context, WebSocket clientWebSocket)
        {
            Console.WriteLine($"[DEBUG] Client connected: {context.Connection.RemoteIpAddress}");
            try
            {
                using var sonioxWebSocket = new ClientWebSocket();
                await sonioxWebSocket.ConnectAsync(new Uri(SonioxWsUrl), context.RequestAborted);
                Console.WriteLine("[DEBUG] Connected to Soniox WebSocket API");

                // 1. Receive the first message from the client (should be JSON config)
                var buffer = new byte[8192];
                var result = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                if (result.MessageType != WebSocketMessageType.Text)
                    throw new Exception("Expected JSON config as first message");

                var clientConfigJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var clientConfig = JsonSerializer.Deserialize<Dictionary<string, object>>(clientConfigJson);

                // 2. Merge with your required fields
                clientConfig["api_key"] = SonioxApiKey;
                clientConfig["audio_format"] = "auto";
                clientConfig["model"] = "stt-rt-preview";
                clientConfig["language_hints"] = new[] { "sl" };

                // 3. Send merged config to Soniox
                var mergedConfigJson = JsonSerializer.Serialize(clientConfig);
                var mergedConfigBytes = Encoding.UTF8.GetBytes(mergedConfigJson);
                await sonioxWebSocket.SendAsync(new ArraySegment<byte>(mergedConfigBytes), WebSocketMessageType.Text, true, context.RequestAborted);
                Console.WriteLine("[DEBUG] Sent merged config to Soniox");

                var receiveBuffer = new byte[8192];

                var forwardAudioTask = Task.Run(async () =>
                {
                    while (clientWebSocket.State == WebSocketState.Open)
                    {
                        var audioResult = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), context.RequestAborted);
                        if (audioResult.MessageType == WebSocketMessageType.Close)
                        {
                            Console.WriteLine("[DEBUG] Client closed WebSocket");
                            await sonioxWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed", context.RequestAborted);
                            break;
                        }
                        else if (audioResult.MessageType == WebSocketMessageType.Binary)
                        {
                            Console.WriteLine($"[DEBUG] Received audio chunk from client: {audioResult.Count} bytes");
                            await sonioxWebSocket.SendAsync(new ArraySegment<byte>(receiveBuffer, 0, audioResult.Count), WebSocketMessageType.Binary, audioResult.EndOfMessage, context.RequestAborted);
                        }
                    }
                });

                var relayTranscriptionTask = Task.Run(async () =>
                {
                    var buffer = new byte[8192];
                    while (sonioxWebSocket.State == WebSocketState.Open)
                    {
                        var result = await sonioxWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Console.WriteLine("[DEBUG] Soniox closed WebSocket");
                            await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Soniox closed", context.RequestAborted);
                            break;
                        }
                        else if (result.MessageType == WebSocketMessageType.Text)
                        {
                            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            Console.WriteLine($"[DEBUG] Received transcription from Soniox: {json}");
                            await clientWebSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, context.RequestAborted);
                        }
                    }
                });

                await Task.WhenAny(forwardAudioTask, relayTranscriptionTask);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception in SonioxWebSocketHandler: {ex}");
            }
            finally
            {
                Console.WriteLine($"[DEBUG] Client disconnected: {context.Connection.RemoteIpAddress}");
            }
        }
    }
} 