using System;
using System.Collections.ObjectModel;
using System.Globalization;
using LOG_TIMING_DIAGRAM.Models;

namespace LOG_TIMING_DIAGRAM.ViewModels
{
    public sealed class StatsViewModel : ViewModelBase
    {
        private int _entryCount;
        private int _deviceCount;
        private int _signalCount;
        private string _timeRangeText;
        private int _errorCount;

        public StatsViewModel()
        {
            Errors = new ObservableCollection<string>();
        }

        public int EntryCount
        {
            get => _entryCount;
            private set => SetProperty(ref _entryCount, value);
        }

        public int DeviceCount
        {
            get => _deviceCount;
            private set => SetProperty(ref _deviceCount, value);
        }

        public int SignalCount
        {
            get => _signalCount;
            private set => SetProperty(ref _signalCount, value);
        }

        public string TimeRangeText
        {
            get => _timeRangeText;
            private set => SetProperty(ref _timeRangeText, value);
        }

        public int ErrorCount
        {
            get => _errorCount;
            private set => SetProperty(ref _errorCount, value);
        }

        public ObservableCollection<string> Errors { get; }

        public void Update(ParseResult result)
        {
            if (result == null || result.Data == null)
            {
                Clear();
                return;
            }

            var log = result.Data;
            EntryCount = log.EntryCount;
            DeviceCount = log.DeviceCount;
            SignalCount = log.SignalCount;

            var range = log.TimeRange;
            if (range != null)
            {
                TimeRangeText = $"{range.Item1:yyyy-MM-dd HH:mm:ss.fff} - {range.Item2:yyyy-MM-dd HH:mm:ss.fff}";
            }
            else
            {
                TimeRangeText = "N/A";
            }

            Errors.Clear();
            if (result.Errors != null)
            {
                foreach (var error in result.Errors)
                {
                    Errors.Add(error.ToString());
                }
            }

            ErrorCount = Errors.Count;
        }

        public void Clear()
        {
            EntryCount = 0;
            DeviceCount = 0;
            SignalCount = 0;
            TimeRangeText = string.Empty;
            ErrorCount = 0;
            Errors.Clear();
        }
    }
}
