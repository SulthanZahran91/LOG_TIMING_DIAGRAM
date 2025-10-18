using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LOG_TIMING_DIAGRAM.Models
{
    public sealed class SignalData
    {
        public SignalData(string key, string deviceId, string signalName, SignalType signalType, IEnumerable<LogEntry> entries, IEnumerable<SignalState> states)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
            SignalName = signalName ?? throw new ArgumentNullException(nameof(signalName));
            SignalType = signalType;
            Entries = new ReadOnlyCollection<LogEntry>((entries ?? Enumerable.Empty<LogEntry>()).OrderBy(e => e.Timestamp).ToList());
            States = new ReadOnlyCollection<SignalState>((states ?? Enumerable.Empty<SignalState>()).OrderBy(s => s.StartTimestamp).ToList());
        }

        public string Key { get; }

        public string DeviceId { get; }

        public string SignalName { get; }

        public SignalType SignalType { get; }

        public IReadOnlyList<LogEntry> Entries { get; }

        public IReadOnlyList<SignalState> States { get; }
    }
}
