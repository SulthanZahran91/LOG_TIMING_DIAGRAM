using System;

namespace LOG_TIMING_DIAGRAM.Models
{
    public sealed class SignalState
    {
        public SignalState(DateTime start, DateTime end, object value, SignalType signalType)
        {
            if (end < start)
            {
                throw new ArgumentException("End timestamp must be greater than or equal to start timestamp.", nameof(end));
            }

            StartTimestamp = start;
            EndTimestamp = end;
            Value = value;
            SignalType = signalType;
        }

        public DateTime StartTimestamp { get; }

        public DateTime EndTimestamp { get; }

        public object Value { get; }

        public SignalType SignalType { get; }
    }
}
