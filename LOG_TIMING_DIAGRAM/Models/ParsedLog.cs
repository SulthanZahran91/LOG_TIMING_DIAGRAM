using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LOG_TIMING_DIAGRAM.Models
{
    public sealed class ParsedLog
    {
        private ParsedLog(
            IReadOnlyList<LogEntry> entries,
            IReadOnlyCollection<string> devices,
            IReadOnlyCollection<string> signals,
            DateTime start,
            DateTime end)
        {
            Entries = entries;
            Devices = devices;
            Signals = signals;
            TimeRange = Tuple.Create(start, end);
        }

        public IReadOnlyList<LogEntry> Entries { get; }

        public IReadOnlyCollection<string> Signals { get; }

        public IReadOnlyCollection<string> Devices { get; }

        public Tuple<DateTime, DateTime> TimeRange { get; }

        public int EntryCount => Entries.Count;

        public int SignalCount => Signals.Count;

        public int DeviceCount => Devices.Count;

        public static ParsedLog FromEntries(IEnumerable<LogEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var entryList = entries.OrderBy(e => e.Timestamp).ToList();
            if (entryList.Count == 0)
            {
                throw new ArgumentException("Parsed logs require at least one entry.", nameof(entries));
            }

            var devices = new ReadOnlyCollection<string>(
                entryList.Select(e => e.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList());
            var signals = new ReadOnlyCollection<string>(
                entryList.Select(e => e.SignalKey).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList());

            return new ParsedLog(
                new ReadOnlyCollection<LogEntry>(entryList),
                devices,
                signals,
                entryList.First().Timestamp,
                entryList.Last().Timestamp);
        }

        public static ParsedLog FromEntries(
            IEnumerable<LogEntry> entries,
            IEnumerable<string> devices,
            IEnumerable<string> signals,
            Tuple<DateTime, DateTime> timeRange)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (devices == null)
            {
                throw new ArgumentNullException(nameof(devices));
            }

            if (signals == null)
            {
                throw new ArgumentNullException(nameof(signals));
            }

            if (timeRange == null)
            {
                throw new ArgumentNullException(nameof(timeRange));
            }

            var entryList = entries.OrderBy(e => e.Timestamp).ToList();

            return new ParsedLog(
                new ReadOnlyCollection<LogEntry>(entryList),
                new ReadOnlyCollection<string>(devices.ToList()),
                new ReadOnlyCollection<string>(signals.ToList()),
                timeRange.Item1,
                timeRange.Item2);
        }
    }
}
