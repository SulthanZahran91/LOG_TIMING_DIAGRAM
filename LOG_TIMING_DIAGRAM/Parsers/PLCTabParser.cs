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
            return new Regex(@"^\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3}.*$", RegexOptions.Compiled);
        }

        protected override ParsedLine MapMatchToParsedLine(Match match)
        {
            var line = match.Value;
            var tokens = SplitColumns(line);
            if (tokens.Length < 5)
            {
                throw new FormatException("Expected at least five columns in tab log format.");
            }

            var timestampToken = tokens[0];
            var pathToken = tokens.Length > 2 ? tokens[2] : tokens[1];
            var signalToken = tokens.Length > 3 ? tokens[3] : throw new FormatException("Missing signal column.");

            string dtypeToken = null;
            string valueToken = null;

            if (tokens.Length >= 6)
            {
                dtypeToken = tokens[4];
                valueToken = tokens[5];
            }
            else if (tokens.Length == 5)
            {
                valueToken = tokens[4];
            }

            if (string.IsNullOrWhiteSpace(valueToken) && tokens.Length > 6)
            {
                valueToken = tokens[6];
            }

            var timestamp = ParseTimestamp(timestampToken);
            var (deviceId, unit) = ParseDeviceAndUnit(pathToken);
            var signalKey = ComposeSignalKey(deviceId, unit, signalToken);

            var signalType = DetermineSignalType(dtypeToken, valueToken);
            var value = ConvertValue(valueToken, signalType);

            return new ParsedLine
            {
                DeviceId = deviceId,
                SignalName = signalKey,
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
