using Microsoft.AspNetCore.Mvc;
using soniox_test_2.Services;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;

namespace soniox_test_2.Controllers
{
    public class TranscriptionController : Controller
    {
        private readonly IAudioRecordingService _audioRecordingService;

        public TranscriptionController(IAudioRecordingService audioRecordingService)
        {
            _audioRecordingService = audioRecordingService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> StartRecording()
        {
            try
            {
                var tempFilePath = await _audioRecordingService.StartRecordingAsync();
                return Json(new { success = true, message = "Recording started" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> StopRecording()
        {
            try
            {
                var audioData = await _audioRecordingService.StopRecordingAsync();
                // TODO: Send audio data to transcription API
                return Json(new { success = true, message = "Recording stopped" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult VideoUpload()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(100_000_000)] // 100 MB limit
        public async Task<IActionResult> VideoUpload(IFormFile? videoFile, string? videoUrl)
        {
            const string apiKey = "ca5264a8fdf73eacc2bf3f9729f5892acbe13ad37322664748da4e296cbc75f3";
            const string apiBase = "https://api.soniox.com";
            string? fileId = null;
            string? transcriptionId = null;
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            string? tempAudioPath = null;
            try
            {
                // 1. Upload file to Soniox (if file provided)
                if (videoFile != null && videoFile.Length > 0)
                {
                    var ext = Path.GetExtension(videoFile.FileName).ToLowerInvariant();
                    var isVideo = ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".mkv" || ext == ".webm";
                    Stream audioStream;
                    string audioFileName;
                    if (isVideo)
                    {
                        // Save video to temp file
                        var tempVideoPath = Path.GetTempFileName() + ext;
                        using (var fs = new FileStream(tempVideoPath, FileMode.Create, FileAccess.Write))
                        {
                            await videoFile.CopyToAsync(fs);
                        }
                        // Extract audio to temp file (mp3)
                        tempAudioPath = Path.GetTempFileName() + ".mp3";
                        var ffmpeg = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-y -i \"{tempVideoPath}\" -vn -acodec libmp3lame -ar 44100 -ac 2 -b:a 192k \"{tempAudioPath}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        var proc = Process.Start(ffmpeg);
                        await proc.WaitForExitAsync();
                        if (proc.ExitCode != 0)
                        {
                            var error = await proc.StandardError.ReadToEndAsync();
                            return BadRequest($"FFmpeg error: {error}");
                        }
                        audioStream = new FileStream(tempAudioPath, FileMode.Open, FileAccess.Read);
                        audioFileName = Path.GetFileNameWithoutExtension(videoFile.FileName) + ".mp3";
                        // Clean up temp video file
                        System.IO.File.Delete(tempVideoPath);
                    }
                    else
                    {
                        audioStream = videoFile.OpenReadStream();
                        audioFileName = videoFile.FileName;
                    }
                    var fileContent = new MultipartFormDataContent();
                    fileContent.Add(new StreamContent(audioStream), "file", audioFileName);
                    var res = await client.PostAsync($"{apiBase}/v1/files", fileContent);
                    res.EnsureSuccessStatusCode();
                    var resJson = await res.Content.ReadAsStringAsync();
                    var file = JsonNode.Parse(resJson);
                    fileId = file?["id"]?.ToString();
                    if (string.IsNullOrEmpty(fileId))
                        return BadRequest("File ID is missing after upload.");
                    audioStream.Dispose();
                    if (tempAudioPath != null && System.IO.File.Exists(tempAudioPath))
                        System.IO.File.Delete(tempAudioPath);
                }
                // 2. If URL provided, use it directly (Soniox supports public URLs)
                string? fileRef = fileId;
                if (!string.IsNullOrWhiteSpace(videoUrl))
                {
                    fileRef = videoUrl;
                }
                if (string.IsNullOrEmpty(fileRef))
                    return BadRequest("No file or URL provided.");
                // 3. Start transcription
                var request = new StringContent(
                    JsonSerializer.Serialize(new {
                        file_id = fileId,
                        url = !string.IsNullOrWhiteSpace(videoUrl) ? videoUrl : null,
                        model = "stt-async-preview",
                        language_hints = new []{"sl"}
                    }),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );
                var transRes = await client.PostAsync($"{apiBase}/v1/transcriptions", request);
                transRes.EnsureSuccessStatusCode();
                var transJson = await transRes.Content.ReadAsStringAsync();
                var transcription = JsonNode.Parse(transJson);
                transcriptionId = transcription?["id"]?.ToString();
                if (string.IsNullOrEmpty(transcriptionId))
                    return BadRequest("Transcription ID is missing.");
                // 4. Poll for completion
                while (true)
                {
                    var pollRes = await client.GetAsync($"{apiBase}/v1/transcriptions/{transcriptionId}");
                    pollRes.EnsureSuccessStatusCode();
                    var pollJson = await pollRes.Content.ReadAsStringAsync();
                    var poll = JsonNode.Parse(pollJson);
                    var status = poll?["status"]?.ToString();
                    if (status == "completed")
                        break;
                    if (status == "error")
                        return BadRequest($"Transcription error: {poll?["error_message"]?.ToString() ?? "Unknown error"}");
                    await Task.Delay(1000);
                }
                // 5. Get the transcript
                var transcriptRes = await client.GetAsync($"{apiBase}/v1/transcriptions/{transcriptionId}/transcript");
                transcriptRes.EnsureSuccessStatusCode();
                var transcriptJson = await transcriptRes.Content.ReadAsStringAsync();
                var transcript = JsonNode.Parse(transcriptJson);
                var transcriptText = transcript?["text"]?.ToString();
                if (string.IsNullOrEmpty(transcriptText))
                    return BadRequest("Transcript text is missing.");
                // 6. Clean up
                await client.DeleteAsync($"{apiBase}/v1/transcriptions/{transcriptionId}");
                if (!string.IsNullOrEmpty(fileId))
                    await client.DeleteAsync($"{apiBase}/v1/files/{fileId}");
                return Json(new { success = true, transcript = transcriptText });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, error = ex.Message });
            }
        }
    }
} 