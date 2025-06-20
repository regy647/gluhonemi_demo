using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

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

                // Send initial config to Soniox as the first message
                var config = new
                {
                    api_key = SonioxApiKey,
                    audio_format = "auto",
                    model = "stt-rt-preview",
                    language_hints = new[] { "sl" }
                };
                var configJson = JsonSerializer.Serialize(config);
                var configBytes = Encoding.UTF8.GetBytes(configJson);
                await sonioxWebSocket.SendAsync(new ArraySegment<byte>(configBytes), WebSocketMessageType.Text, true, context.RequestAborted);
                Console.WriteLine("[DEBUG] Sent config to Soniox");

                var receiveBuffer = new byte[8192];

                var forwardAudioTask = Task.Run(async () =>
                {
                    while (clientWebSocket.State == WebSocketState.Open)
                    {
                        var result = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), context.RequestAborted);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Console.WriteLine("[DEBUG] Client closed WebSocket");
                            await sonioxWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed", context.RequestAborted);
                            break;
                        }
                        else if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            Console.WriteLine($"[DEBUG] Received audio chunk from client: {result.Count} bytes");
                            await sonioxWebSocket.SendAsync(new ArraySegment<byte>(receiveBuffer, 0, result.Count), WebSocketMessageType.Binary, result.EndOfMessage, context.RequestAborted);
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