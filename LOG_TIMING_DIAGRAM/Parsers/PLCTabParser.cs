using System;
using System.Linq;
using System.Text.RegularExpressions;
using LOG_TIMING_DIAGRAM.Models;
using LOG_TIMING_DIAGRAM.Utils;

namespace LOG_TIMING_DIAGRAM.Parsers
{
    public sealed class PLCTabParser : GenericTemplateLogParser
    {
        private static readonly Regex _deviceRegex = new Regex(@"([A-Za-z0-9_-]+)(?:@[^\]]+)?$", RegexOptions.Compiled);

        public override string Name => "plc_tab";

        protected override Regex DeviceIdRegex => _deviceRegex;

        protected override Regex BuildLineRegex()
        {
            // Flexible matcher that ensures we can detect candidate lines before detailed splitting.
            return new Regex(@"^\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3,6}.*$", RegexOptions.Compiled);
        }

        protected override ParsedLine MapMatchToParsedLine(Match match)
        {
            var line = match.Value;

            var levelStart = line.IndexOf('[', 0);
            if (levelStart <= 0)
            {
                throw new FormatException("Unable to locate level delimiter.");
            }

            var timestampToken = line.Substring(0, levelStart).TrimEnd();

            var levelEnd = line.IndexOf(']', levelStart + 1);
            if (levelEnd < 0)
            {
                throw new FormatException("Unable to locate level closing bracket.");
            }

            var pathStart = levelEnd + 1;
            while (pathStart < line.Length && char.IsWhiteSpace(line[pathStart]))
            {
                pathStart++;
            }

            var tabIndex = line.IndexOf('\t', pathStart);
            if (tabIndex < 0)
            {
                throw new FormatException("Unable to locate column separator.");
            }

            var pathToken = line.Substring(pathStart, tabIndex - pathStart).Trim();
            var remainder = line.Substring(tabIndex + 1).Split('\t');
            if (remainder.Length == 0)
            {
                throw new FormatException("Missing signal column.");
            }

            var signalToken = remainder[0].Trim();
            string dtypeToken = null;
            string valueToken = remainder.Length > 2 ? remainder[2].Trim() : null;
            if (string.IsNullOrWhiteSpace(valueToken) && remainder.Length > 3)
            {
                valueToken = remainder[3].Trim();
            }

            var timestamp = ParseTimestamp(timestampToken);
            var (deviceId, unit) = ParseDeviceAndUnit(pathToken);
            var trimmedSignal = signalToken?.Trim();
            var signalKey = ComposeSignalKey(deviceId, unit, trimmedSignal);

            var signalType = DetermineSignalType(dtypeToken, valueToken);
            var value = ConvertValue(valueToken, signalType);

            return new ParsedLine
            {
                DeviceId = deviceId,
                SignalKey = signalKey,
                SignalName = trimmedSignal,
                Timestamp = timestamp,
                SignalType = signalType,
                Value = value
            };
        }

        private static string[] SplitColumns(string line)
        {
            var columns = line.Split('\t');
            if (columns.Length >= 5)
            {
                return columns.Select(c => c.Trim()).ToArray();
            }

            // Fallback: split on two or more spaces.
            var spaceCols = Regex.Split(line, @"\s{2,}");
            return spaceCols.Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
        }

        private (string Device, string Unit) ParseDeviceAndUnit(string pathToken)
        {
            if (string.IsNullOrWhiteSpace(pathToken))
            {
                throw new FormatException("Path column is empty.");
            }

            var trimmed = pathToken.Trim().Trim('/');
            var segments = trimmed.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                throw new FormatException("Unable to split path column.");
            }

            var deviceSegment = segments[segments.Length - 1];
            string unit = null;
            string deviceIdSegment = deviceSegment;
            var atIndex = deviceSegment.IndexOf('@');
            if (atIndex >= 0 && atIndex < deviceSegment.Length - 1)
            {
                deviceIdSegment = deviceSegment.Substring(0, atIndex);
                unit = deviceSegment.Substring(atIndex + 1);
            }

            var deviceId = ExtractDeviceId(deviceIdSegment);
            return (deviceId, unit);
        }

        private static SignalType DetermineSignalType(string dtypeToken, string valueToken)
        {
            if (!string.IsNullOrWhiteSpace(dtypeToken))
            {
                var normalized = dtypeToken.Trim().ToUpperInvariant();
                switch (normalized)
                {
                    case "BOOL":
                    case "BOOLEAN":
                    case "DIGITAL":
                    case "OUT":
                    case "IN":
                        if (ParsingHelpers.TryParseBoolean(valueToken, out _))
                        {
                            return SignalType.Boolean;
                        }

                        break;
                    case "INT":
                    case "INTEGER":
                    case "DINT":
                    case "WORD":
                    case "DWORD":
                        return SignalType.Integer;
                }
            }

            if (ParsingHelpers.TryParseBoolean(valueToken, out _))
            {
                return SignalType.Boolean;
            }

            if (ParsingHelpers.TryParseInteger(valueToken, out _))
            {
                return SignalType.Integer;
            }

            return SignalType.String;
        }

        private static object ConvertValue(string valueToken, SignalType type)
        {
            switch (type)
            {
                case SignalType.Boolean:
                    if (ParsingHelpers.TryParseBoolean(valueToken, out var boolValue))
                    {
                        return boolValue;
                    }

                    throw new FormatException($"Unable to parse boolean value '{valueToken}'.");
                case SignalType.Integer:
                    if (ParsingHelpers.TryParseInteger(valueToken, out var intValue))
                    {
                        return intValue;
                    }

                    throw new FormatException($"Unable to parse integer value '{valueToken}'.");
                default:
                    return valueToken?.Trim();
            }
        }
    }
}
