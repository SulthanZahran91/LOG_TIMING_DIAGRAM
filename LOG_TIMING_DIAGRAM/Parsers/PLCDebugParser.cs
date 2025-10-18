using System;
using System.Text.RegularExpressions;
using LOG_TIMING_DIAGRAM.Models;
using LOG_TIMING_DIAGRAM.Utils;

namespace LOG_TIMING_DIAGRAM.Parsers
{
    public sealed class PLCDebugParser : GenericTemplateLogParser
    {
        private static readonly Regex _lineRegex = new Regex(
            @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\s+\[(?<level>[^\]]+)\]\s+\[(?<path>[^\]]+)\]\s+\[(?<signal>[^\]]+)\]\s+\((?<dtype>[^)]+)\)\s*:\s*(?<value>.+?)\s*$",
            RegexOptions.Compiled);

        public override string Name => "plc_debug";

        protected override Regex BuildLineRegex() => _lineRegex;

        private const int TimestampTokenLength = 23;

        protected override ParsedLine ParseLine(string line)
        {
            if (TryParseLineFast(line, out var parsed))
            {
                return parsed;
            }

            return base.ParseLine(line);
        }

        protected override ParsedLine MapMatchToParsedLine(Match match)
        {
            var ts = match.Groups["ts"].Value;
            var path = match.Groups["path"].Value;
            var signalGroup = match.Groups["signal"].Value;
            var dtype = match.Groups["dtype"].Value;
            var valueToken = match.Groups["value"].Value;

            var timestamp = ParseTimestamp(ts);
            var (deviceId, unit) = ParsePath(path);
            var signalKey = ComposeSignalKey(deviceId, unit, signalGroup);
            var signalType = InferSignalType(dtype, valueToken);
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

        private bool TryParseLineFast(string line, out ParsedLine parsedLine)
        {
            parsedLine = default;
            if (string.IsNullOrEmpty(line) || line.Length <= TimestampTokenLength)
            {
                return false;
            }

            try
            {
                var levelStart = line.IndexOf('[', TimestampTokenLength);
                if (levelStart < 0)
                {
                    return false;
                }

                var levelEnd = line.IndexOf(']', levelStart + 1);
                if (levelEnd < 0)
                {
                    return false;
                }

                var pathStart = line.IndexOf('[', levelEnd + 1);
                if (pathStart < 0)
                {
                    return false;
                }

                var pathEnd = line.IndexOf(']', pathStart + 1);
                if (pathEnd < 0)
                {
                    return false;
                }

                var signalStart = line.IndexOf('[', pathEnd + 1);
                if (signalStart < 0)
                {
                    return false;
                }

                var signalEnd = line.IndexOf(']', signalStart + 1);
                if (signalEnd < 0)
                {
                    return false;
                }

                var dtypeStart = line.IndexOf('(', signalEnd + 1);
                if (dtypeStart < 0)
                {
                    return false;
                }

                var dtypeEnd = line.IndexOf(')', dtypeStart + 1);
                if (dtypeEnd < 0)
                {
                    return false;
                }

                var colonIndex = line.IndexOf(':', dtypeEnd + 1);
                if (colonIndex < 0)
                {
                    return false;
                }

                var valueStart = colonIndex + 1;
                while (valueStart < line.Length && char.IsWhiteSpace(line[valueStart]))
                {
                    valueStart++;
                }

                var valueEnd = line.Length - 1;
                while (valueEnd >= valueStart && char.IsWhiteSpace(line[valueEnd]))
                {
                    valueEnd--;
                }

                var valueToken = valueStart <= valueEnd
                    ? line.Substring(valueStart, valueEnd - valueStart + 1)
                    : string.Empty;

                var timestampToken = line.Substring(0, TimestampTokenLength);
                var pathToken = line.Substring(pathStart + 1, pathEnd - pathStart - 1);
                var signalToken = line.Substring(signalStart + 1, signalEnd - signalStart - 1);
                var dtypeToken = line.Substring(dtypeStart + 1, dtypeEnd - dtypeStart - 1);

                var timestamp = ParseTimestamp(timestampToken);
                var (deviceId, unit) = ParsePath(pathToken);
                var signalKey = ComposeSignalKey(deviceId, unit, signalToken);
                var signalType = InferSignalType(dtypeToken, valueToken);
                var value = ConvertValue(valueToken, signalType);

                parsedLine = new ParsedLine
                {
                    DeviceId = deviceId,
                    SignalName = signalKey,
                    Timestamp = timestamp,
                    SignalType = signalType,
                    Value = value
                };

                return true;
            }
            catch
            {
                return false;
            }
        }

        private (string Device, string Unit) ParsePath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                throw new FormatException("Path segment missing.");
            }

            var path = rawPath;
            var length = path.Length;
            var start = 0;
            var end = length - 1;

            while (start <= end && char.IsWhiteSpace(path[start]))
            {
                start++;
            }

            while (end >= start && char.IsWhiteSpace(path[end]))
            {
                end--;
            }

            if (start > end)
            {
                throw new FormatException("Path segment missing.");
            }

            while (start <= end && path[start] == '/')
            {
                start++;
            }

            while (end >= start && path[end] == '/')
            {
                end--;
            }

            if (start > end)
            {
                throw new FormatException("Unable to split path segments.");
            }

            var searchLength = end - start + 1;
            var lastSlash = path.LastIndexOf('/', start + searchLength - 1, searchLength);

            string deviceSegment;
            if (lastSlash >= start)
            {
                var segmentLength = end - lastSlash;
                if (segmentLength <= 0)
                {
                    throw new FormatException("Unable to split path segments.");
                }

                deviceSegment = path.Substring(lastSlash + 1, segmentLength);
            }
            else
            {
                deviceSegment = path.Substring(start, searchLength);
            }

            string unit = null;
            string deviceIdSegment = deviceSegment;
            var atIndex = deviceSegment.IndexOf('@');
            if (atIndex >= 0)
            {
                if (atIndex > 0)
                {
                    deviceIdSegment = deviceSegment.Substring(0, atIndex);
                }
                else
                {
                    deviceIdSegment = string.Empty;
                }

                if (atIndex < deviceSegment.Length - 1)
                {
                    unit = deviceSegment.Substring(atIndex + 1);
                }
            }

            if (string.IsNullOrWhiteSpace(deviceIdSegment))
            {
                throw new FormatException("Device id segment missing.");
            }

            var deviceId = ExtractDeviceId(deviceIdSegment);
            return (deviceId, unit);
        }

        protected override string ExtractDeviceId(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                throw new FormatException("Device path segment was empty.");
            }

            var start = 0;
            var end = input.Length - 1;

            while (start <= end && char.IsWhiteSpace(input[start]))
            {
                start++;
            }

            while (end >= start && char.IsWhiteSpace(input[end]))
            {
                end--;
            }

            if (start > end)
            {
                throw new FormatException("Device path segment was empty.");
            }

            var length = end - start + 1;
            var trimmed = length == input.Length ? input : input.Substring(start, length);

            var dashIndex = trimmed.LastIndexOf('-');
            if (dashIndex > 0 && dashIndex < trimmed.Length - 1)
            {
                var suffixDigits = true;
                for (var i = dashIndex + 1; i < trimmed.Length; i++)
                {
                    if (!char.IsDigit(trimmed[i]))
                    {
                        suffixDigits = false;
                        break;
                    }
                }

                if (suffixDigits)
                {
                    return trimmed;
                }
            }

            return base.ExtractDeviceId(trimmed);
        }

        private static SignalType InferSignalType(string dtype, string valueToken)
        {
            if (!string.IsNullOrWhiteSpace(dtype))
            {
                var normalized = dtype.Trim().ToUpperInvariant();
                switch (normalized)
                {
                    case "BOOLEAN":
                    case "BOOL":
                        return SignalType.Boolean;
                    case "INT":
                    case "INTEGER":
                    case "DINT":
                    case "UINT16":
                    case "UINT32":
                        return SignalType.Integer;
                    default:
                        return SignalType.String;
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

        private static object ConvertValue(string valueToken, SignalType signalType)
        {
            switch (signalType)
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
