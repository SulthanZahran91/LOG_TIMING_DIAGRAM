using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LOG_TIMING_DIAGRAM.Models;

namespace LOG_TIMING_DIAGRAM.Utils
{
    public static class SignalProcessing
    {
        public static IReadOnlyDictionary<string, List<LogEntry>> GroupBySignal(ParsedLog parsedLog)
        {
            if (parsedLog == null)
            {
                throw new ArgumentNullException(nameof(parsedLog));
            }

            var map = new Dictionary<string, List<LogEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in parsedLog.Entries)
            {
                if (!map.TryGetValue(entry.SignalName, out var list))
                {
                    list = new List<LogEntry>();
                    map.Add(entry.SignalName, list);
                }

                list.Add(entry);
            }

            foreach (var list in map.Values)
            {
                list.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            }

            return new ReadOnlyDictionary<string, List<LogEntry>>(map);
        }

        public static IReadOnlyList<SignalState> CalculateSignalStates(
            IReadOnlyList<LogEntry> entries,
            Tuple<DateTime, DateTime> timeRange)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (entries.Count == 0)
            {
                return Array.Empty<SignalState>();
            }

            var orderedEntries = entries.OrderBy(e => e.Timestamp).ToList();
            var states = new List<SignalState>(orderedEntries.Count);

            for (var i = 0; i < orderedEntries.Count; i++)
            {
                var current = orderedEntries[i];
                var nextTimestamp = i < orderedEntries.Count - 1
                    ? orderedEntries[i + 1].Timestamp
                    : timeRange?.Item2 ?? current.Timestamp;

                if (timeRange != null && i == orderedEntries.Count - 1 && nextTimestamp < timeRange.Item2)
                {
                    nextTimestamp = timeRange.Item2;
                }

                var state = new SignalState(current.Timestamp, nextTimestamp, current.Value, current.SignalType);
                states.Add(state);
            }

            return states;
        }

        public static IReadOnlyList<SignalData> ProcessSignalsForWaveform(ParsedLog parsedLog)
        {
            if (parsedLog == null)
            {
                throw new ArgumentNullException(nameof(parsedLog));
            }

            var grouped = GroupBySignal(parsedLog);
            var results = new List<SignalData>(grouped.Count);

            foreach (var kvp in grouped.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var firstEntry = kvp.Value[0];
                var states = CalculateSignalStates(kvp.Value, parsedLog.TimeRange);
                results.Add(new SignalData(
                    key: kvp.Key,
                    deviceId: firstEntry.DeviceId,
                    signalName: firstEntry.SignalName,
                    signalType: firstEntry.SignalType,
                    entries: kvp.Value,
                    states: states));
            }

            return results;
        }
    }
}
