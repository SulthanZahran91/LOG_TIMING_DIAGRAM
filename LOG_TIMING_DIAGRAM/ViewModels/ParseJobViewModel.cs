using System;
using System.Threading;
using LOG_TIMING_DIAGRAM.Models;

namespace LOG_TIMING_DIAGRAM.ViewModels
{
    public sealed class ParseJobViewModel : ViewModelBase
    {
        private bool _isRunning;
        private string _currentFile;
        private double _progressPercent;
        private string _statusMessage;
        private CancellationTokenSource _cancellationTokenSource;

        public bool IsRunning
        {
            get => _isRunning;
            private set => SetProperty(ref _isRunning, value);
        }

        public string CurrentFile
        {
            get => _currentFile;
            private set => SetProperty(ref _currentFile, value);
        }

        public double ProgressPercent
        {
            get => _progressPercent;
            private set => SetProperty(ref _progressPercent, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public CancellationToken Token => _cancellationTokenSource?.Token ?? CancellationToken.None;

        public bool IsCancellable => IsRunning;

        public event EventHandler CancelRequested;

        public void Start(string filePath)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            IsRunning = true;
            CurrentFile = filePath;
            ProgressPercent = 0;
            StatusMessage = "Parsing...";
        }

        public void Update(ParseProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            var filePath = progress.FilePath;
            CurrentFile = filePath;
            var linesRead = progress.LinesRead;
            var totalLines = progress.TotalLines ?? 0;

            if (linesRead <= 0 || totalLines <= 0)
            {
                ProgressPercent = 0;
            }
            else
            {
                var percent = (double)linesRead / totalLines * 100;
                ProgressPercent = Math.Max(0, Math.Min(100, percent));
            }

            StatusMessage = $"Parsing {PathTrim(filePath)} ({linesRead:N0} lines) ...";
        }

        public void Complete(string message = null)
        {
            IsRunning = false;
            StatusMessage = message ?? "Completed";
            ProgressPercent = 100;
            CurrentFile = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        public void Fail(string message)
        {
            IsRunning = false;
            StatusMessage = message;
            ProgressPercent = 0;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        public void Cancel()
        {
            if (!IsRunning)
            {
                return;
            }

            _cancellationTokenSource?.Cancel();
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private static string PathTrim(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            try
            {
                return System.IO.Path.GetFileName(path);
            }
            catch
            {
                return path;
            }
        }
    }
}
