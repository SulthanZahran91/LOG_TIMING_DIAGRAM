using System;

namespace LOG_TIMING_DIAGRAM.Models
{
    public sealed class LogEntry
    {
        public LogEntry(string deviceId, string signalKey, string signalName, DateTime timestamp, object value, SignalType signalType)
        {
            DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
            SignalKey = signalKey ?? throw new ArgumentNullException(nameof(signalKey));
            SignalName = signalName ?? throw new ArgumentNullException(nameof(signalName));
            Timestamp = timestamp;
            Value = value ?? throw new ArgumentNullException(nameof(value));
            SignalType = signalType;
        }

        public string DeviceId { get; }

        public string SignalKey { get; }

        public string SignalName { get; }

        public DateTime Timestamp { get; }

        public object Value { get; }

        public SignalType SignalType { get; }

        public override string ToString()
        {
            return $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {SignalKey} = {Value}";
        }
    }
}
