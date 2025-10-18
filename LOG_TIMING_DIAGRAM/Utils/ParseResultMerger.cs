using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using LOG_TIMING_DIAGRAM.Models;

namespace LOG_TIMING_DIAGRAM.Utils
{
    public static class ParseResultMerger
    {
        public static ParseResult Merge(IDictionary<string, ParseResult> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            var allEntries = new List<LogEntry>();
            var errors = new List<ParseError>();
            var devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var signals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DateTime? start = null;
            DateTime? end = null;

            foreach (var kvp in results)
            {
                var filePath = kvp.Key;
                var result = kvp.Value;
                if (result == null)
                {
                    continue;
                }

                if (result.Data != null)
                {
                    foreach (var entry in result.Data.Entries)
                    {
                        allEntries.Add(entry);
                        devices.Add(entry.DeviceId);
                        signals.Add(entry.SignalName);
                        start = start == null || entry.Timestamp < start ? entry.Timestamp : start;
                        end = end == null || entry.Timestamp > end ? entry.Timestamp : end;
                    }
                }

                if (result.Errors != null)
                {
                    foreach (var error in result.Errors)
                    {
                        var updatedFile = error.FilePath ?? filePath;
                        errors.Add(new ParseError(
                            error.Line,
                            error.Content,
                            error.Reason,
                            updatedFile));
                    }
                }
            }

            if (allEntries.Count == 0)
            {
                return ParseResult.Failed(errors);
            }

            var parsedLog = ParsedLog.FromEntries(
                allEntries,
                devices,
                signals,
                Tuple.Create(start.Value, end.Value));

            return new ParseResult(parsedLog, errors);
        }
    }
}
