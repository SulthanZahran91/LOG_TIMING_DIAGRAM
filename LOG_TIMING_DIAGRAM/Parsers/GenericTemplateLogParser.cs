using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LOG_TIMING_DIAGRAM.Models;
using LOG_TIMING_DIAGRAM.Utils;

namespace LOG_TIMING_DIAGRAM.Parsers
{
    public abstract class GenericTemplateLogParser
    {
        protected struct ParsedLine
        {
            public string DeviceId { get; set; }

            public string SignalName { get; set; }

            public DateTime Timestamp { get; set; }

            public object Value { get; set; }

            public SignalType SignalType { get; set; }
        }

        protected GenericTemplateLogParser()
        {
            LineRegex = BuildLineRegex() ?? throw new InvalidOperationException("Parser must provide a line regular expression.");
        }

        public abstract string Name { get; }

        protected abstract Regex BuildLineRegex();

        protected Regex LineRegex { get; }

        protected virtual string TimestampFormat => "yyyy-MM-dd HH:mm:ss.fff";

        protected virtual Regex DeviceIdRegex => new Regex(@"[A-Za-z0-9_-]+-\d+", RegexOptions.Compiled);

        protected virtual int ProgressReportInterval => 1000;

        public virtual bool CanParse(IEnumerable<string> sampleLines)
        {
            if (sampleLines == null)
            {
                return false;
            }

            var inspected = 0;
            var matches = 0;

            foreach (var line in sampleLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                inspected++;
                if (LineRegex.IsMatch(line))
                {
                    matches++;
                }

                if (inspected >= 5)
                {
                    break;
                }
            }

            if (inspected == 0)
            {
                return false;
            }

            var threshold = Math.Max(1, (int)Math.Ceiling(inspected * 0.6));
            return matches >= threshold;
        }

        public virtual Task<ParseResult> ParseAsync(
            string filePath,
            CancellationToken cancellationToken,
            IProgress<ParseProgress> progress = null)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            return Task.Run(() => ParseInternal(filePath, cancellationToken, progress), cancellationToken);
        }

        private ParseResult ParseInternal(
            string filePath,
            CancellationToken cancellationToken,
            IProgress<ParseProgress> progress)
        {
            Debug.WriteLine($"[Parser:{Name}] Starting parse of '{filePath}'.");
            long fileSize = -1;
            try
            {
                fileSize = new FileInfo(filePath).Length;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Parser:{Name}] Unable to determine file size: {ex.Message}");
            }

            if (fileSize >= 0)
            {
                Debug.WriteLine($"[Parser:{Name}] File size: {fileSize:N0} bytes.");
            }

            var stopwatch = Stopwatch.StartNew();
            var estimatedEntryCapacity = 1024;
            if (fileSize > 0)
            {
                var approxLong = Math.Max(1L, fileSize / 48);
                var approx = (int)Math.Min(int.MaxValue, approxLong);
                estimatedEntryCapacity = Math.Max(estimatedEntryCapacity, approx);
            }
            var entries = new List<LogEntry>(estimatedEntryCapacity);
            var errors = new List<ParseError>();
            var devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var signals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DateTime? start = null;
            DateTime? end = null;

            var processedLines = 0;
            const int logInterval = 5000;

            using (var stream = new FileStream(
                       filePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite,
                       bufferSize: 65536,
                       FileOptions.SequentialScan))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 65536))
            {
                Debug.WriteLine($"[Parser:{Name}] File stream opened. Beginning read loop.");
                var lineNumber = 0;
                var previewBudget = 3;
                if (!Debugger.IsAttached)
                {
                    previewBudget = 0;
                }
                else
                {
                    Debug.WriteLine($"[Parser:{Name}] Preview budget for line logging: {previewBudget}.");
                }
                string rawLine;
                while ((rawLine = reader.ReadLine()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lineNumber++;
                    processedLines = lineNumber;
                    if (previewBudget > 0)
                    {
                        var previewLength = Math.Min(rawLine.Length, 200);
                        var snippet = rawLine.Substring(0, previewLength);
                        if (previewLength < rawLine.Length)
                        {
                            snippet += "...";
                        }

                        Debug.WriteLine($"[Parser:{Name}] Line {lineNumber} snippet: {snippet}");
                        previewBudget--;
                    }

                    if (string.IsNullOrWhiteSpace(rawLine))
                    {
                        continue;
                    }

                    try
                    {
                        var parsed = ParseLine(rawLine);
                        var entry = new LogEntry(
                            parsed.DeviceId,
                            parsed.SignalName,
                            parsed.Timestamp,
                            parsed.Value,
                            parsed.SignalType);

                        entries.Add(entry);
                        devices.Add(parsed.DeviceId);
                        signals.Add(parsed.SignalName);

                        start = start == null || entry.Timestamp < start ? entry.Timestamp : start;
                        end = end == null || entry.Timestamp > end ? entry.Timestamp : end;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Parser:{Name}] Error parsing line {lineNumber}: {ex.Message}");
                        errors.Add(new ParseError(lineNumber, rawLine, ex.Message, filePath));
                    }

                    if (progress != null && lineNumber % ProgressReportInterval == 0)
                    {
                        progress.Report(new ParseProgress(filePath, lineNumber));
                    }

                    if (Debugger.IsAttached && lineNumber % logInterval == 0)
                    {
                        Debug.WriteLine($"[Parser:{Name}] Processed {lineNumber:N0} line(s). Entries={entries.Count}, Errors={errors.Count}.");
                    }
                }

                if (progress != null)
                {
                    progress.Report(new ParseProgress(filePath, processedLines, processedLines));
                }
            }

            stopwatch.Stop();
            Debug.WriteLine($"[Parser:{Name}] Read loop completed. TotalLines={processedLines}, Entries={entries.Count}, Errors={errors.Count}, Duration={stopwatch.Elapsed}.");

            if (entries.Count == 0)
            {
                Debug.WriteLine($"[Parser:{Name}] No entries parsed. Returning failure.");
                return ParseResult.Failed(errors);
            }

            var parsedLog = ParsedLog.FromEntries(
                entries,
                devices,
                signals,
                Tuple.Create(start.Value, end.Value));

            var rangeStart = start?.ToString("o") ?? "null";
            var rangeEnd = end?.ToString("o") ?? "null";
            Debug.WriteLine($"[Parser:{Name}] Parse successful. DeviceCount={devices.Count}, SignalCount={signals.Count}, TimeRange={rangeStart} - {rangeEnd}.");
            return new ParseResult(parsedLog, errors);
        }

        protected virtual ParsedLine ParseLine(string line)
        {
            var match = LineRegex.Match(line);
            if (!match.Success)
            {
                throw new FormatException("Line does not match expected format.");
            }

            return MapMatchToParsedLine(match);
        }

        protected abstract ParsedLine MapMatchToParsedLine(Match match);

        protected virtual DateTime ParseTimestamp(string value)
        {
            return DateTime.ParseExact(value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        protected virtual string ExtractDeviceId(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                throw new FormatException("Device path segment was empty.");
            }

            var regex = DeviceIdRegex;
            if (regex == null)
            {
                return input;
            }

            var match = regex.Match(input);
            if (!match.Success)
            {
                throw new FormatException("Unable to extract device id.");
            }

            return match.Value;
        }

        protected static string ComposeSignalKey(string deviceId, string unit, string signal)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new ArgumentNullException(nameof(deviceId));
            }

            if (string.IsNullOrWhiteSpace(signal))
            {
                throw new ArgumentNullException(nameof(signal));
            }

            var prefix = string.IsNullOrEmpty(unit) ? deviceId : $"{deviceId}@{unit}";
            return $"{prefix}::{signal}";
        }
    }
}
