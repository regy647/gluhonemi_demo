using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NAudio.Wave;

namespace soniox_test_2.Services
{
    public interface IAudioRecordingService
    {
        Task<string> StartRecordingAsync();
        Task<byte[]> StopRecordingAsync();
        bool IsRecording { get; }
    }

    public class AudioRecordingService : IAudioRecordingService
    {
        private WaveInEvent _waveIn;
        private WaveFileWriter _writer;
        private string _tempFilePath;
        private bool _isRecording;

        public bool IsRecording => _isRecording;

        public AudioRecordingService()
        {
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(44100, 16, 1),
                BufferMilliseconds = 50
            };
        }

        public async Task<string> StartRecordingAsync()
        {
            if (_isRecording)
                throw new InvalidOperationException("Recording is already in progress.");

            _tempFilePath = Path.GetTempFileName();
            _writer = new WaveFileWriter(_tempFilePath, _waveIn.WaveFormat);
            
            _waveIn.DataAvailable += (sender, e) =>
            {
                if (_writer != null)
                {
                    _writer.Write(e.Buffer, 0, e.BytesRecorded);
                }
            };

            _waveIn.StartRecording();
            _isRecording = true;

            return _tempFilePath;
        }

        public async Task<byte[]> StopRecordingAsync()
        {
            if (!_isRecording)
                throw new InvalidOperationException("No recording in progress.");

            _waveIn.StopRecording();
            _isRecording = false;

            if (_writer != null)
            {
                _writer.Dispose();
                _writer = null;
            }

            if (File.Exists(_tempFilePath))
            {
                var audioData = await File.ReadAllBytesAsync(_tempFilePath);
                File.Delete(_tempFilePath);
                return audioData;
            }

            return Array.Empty<byte>();
        }
    }
} 