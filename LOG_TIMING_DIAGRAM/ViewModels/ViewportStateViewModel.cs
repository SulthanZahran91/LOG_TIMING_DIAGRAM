using System;

namespace LOG_TIMING_DIAGRAM.ViewModels
{
    public sealed class ViewportStateViewModel : ViewModelBase
    {
        private DateTime _fullStart;
        private DateTime _fullEnd;
        private DateTime _visibleStart;
        private DateTime _visibleEnd;
        private double _zoomLevel = 1.0;

        public event EventHandler TimeRangeChanged;
        public event EventHandler ZoomLevelChanged;

        public DateTime FullStart
        {
            get => _fullStart;
            private set => SetProperty(ref _fullStart, value);
        }

        public DateTime FullEnd
        {
            get => _fullEnd;
            private set => SetProperty(ref _fullEnd, value);
        }

        public DateTime VisibleStart
        {
            get => _visibleStart;
            private set
            {
                if (SetProperty(ref _visibleStart, value))
                {
                    TimeRangeChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public DateTime VisibleEnd
        {
            get => _visibleEnd;
            private set
            {
                if (SetProperty(ref _visibleEnd, value))
                {
                    TimeRangeChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public double ZoomLevel
        {
            get => _zoomLevel;
            private set
            {
                var clamped = Math.Max(1.0, Math.Min(1000.0, value));
                if (SetProperty(ref _zoomLevel, clamped))
                {
                    ZoomLevelChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void SetFullTimeRange(DateTime start, DateTime end)
        {
            if (end < start)
            {
                throw new ArgumentException("End must be after start.", nameof(end));
            }

            FullStart = start;
            FullEnd = end;
            SetTimeRange(start, end);
            ZoomLevel = 1.0;
        }

        public void SetTimeRange(DateTime start, DateTime end)
        {
            if (end < start)
            {
                throw new ArgumentException("Visible end must be after visible start.", nameof(end));
            }

            start = ClampToBounds(start);
            end = ClampToBounds(end);

            if (end < start)
            {
                end = start;
            }

            VisibleStart = start;
            VisibleEnd = end;
        }

        public void ZoomIn(double factor)
        {
            ZoomLevel = ZoomLevel * Math.Max(1.0, factor);
        }

        public void ZoomOut(double factor)
        {
            if (factor <= 0)
            {
                factor = 1.0;
            }

            ZoomLevel = ZoomLevel / factor;
        }

        public void SetZoomLevel(double level)
        {
            ZoomLevel = level;
        }

        public void ResetZoom()
        {
            ZoomLevel = 1.0;
            SetTimeRange(FullStart, FullEnd);
        }

        public void Pan(TimeSpan delta)
        {
            var newStart = VisibleStart + delta;
            var newEnd = VisibleEnd + delta;
            SetTimeRange(newStart, newEnd);
        }

        public void JumpToTime(DateTime center, TimeSpan? window = null)
        {
            var span = window ?? (VisibleEnd - VisibleStart);
            var half = TimeSpan.FromTicks(span.Ticks / 2);
            var start = center - half;
            var end = center + half;
            SetTimeRange(start, end);
        }

        private DateTime ClampToBounds(DateTime value)
        {
            if (value < FullStart)
            {
                return FullStart;
            }

            if (value > FullEnd)
            {
                return FullEnd;
            }

            return value;
        }
    }
}
