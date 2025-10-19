using System;
using System.Globalization;

namespace LOG_TIMING_DIAGRAM.Utils
{
    internal static class ParsingHelpers
    {
        public static bool TryParseBoolean(string input, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            switch (input.Trim().ToUpperInvariant())
            {
                case "1":
                case "TRUE":
                case "ON":
                case "HIGH":
                case "SET":
                    value = true;
                    return true;
                case "0":
                case "FALSE":
                case "OFF":
                case "LOW":
                case "RESET":
                    value = false;
                    return true;
                default:
                    return bool.TryParse(input, out value);
            }
        }

        public static bool TryParseInteger(string input, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var trimmed = input.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            var sanitizedChars = new char[trimmed.Length];
            var sanitizedLength = 0;
            foreach (var ch in trimmed)
            {
                if (ch == '_' || ch == ',')
                {
                    continue;
                }

                sanitizedChars[sanitizedLength++] = ch;
            }

            if (sanitizedLength == 0)
            {
                return false;
            }

            var sanitized = new string(sanitizedChars, 0, sanitizedLength);
            var index = 0;
            var sign = 1;

            if (sanitized[index] == '+')
            {
                index++;
            }
            else if (sanitized[index] == '-')
            {
                sign = -1;
                index++;
            }

            if (index >= sanitized.Length)
            {
                return false;
            }

            var span = sanitized.AsSpan(index);
            long magnitude;
            var limit = sign < 0 ? (long)int.MaxValue + 1 : int.MaxValue;

            if (span.Length >= 2 && span[0] == '0')
            {
                var prefix = span[1];
                if (prefix == 'x' || prefix == 'X')
                {
                    span = span.Slice(2);
                    if (span.Length == 0
                        || !long.TryParse(span, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out magnitude)
                        || magnitude > limit)
                    {
                        return false;
                    }
                }
                else if (prefix == 'b' || prefix == 'B')
                {
                    span = span.Slice(2);
                    if (span.Length == 0)
                    {
                        return false;
                    }

                    magnitude = 0;
                    foreach (var c in span)
                    {
                        if (c != '0' && c != '1')
                        {
                            return false;
                        }

                        if (magnitude > limit / 2)
                        {
                            return false;
                        }

                        magnitude = (magnitude << 1) + (c - '0');
                        if (magnitude > limit)
                        {
                            return false;
                        }
                    }
                }
                else if (prefix == 'o' || prefix == 'O')
                {
                    span = span.Slice(2);
                    if (span.Length == 0)
                    {
                        return false;
                    }

                    magnitude = 0;
                    foreach (var c in span)
                    {
                        if (c < '0' || c > '7')
                        {
                            return false;
                        }

                        if (magnitude > limit / 8)
                        {
                            return false;
                        }

                        magnitude = (magnitude * 8) + (c - '0');
                        if (magnitude > limit)
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    if (!long.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out magnitude)
                        || magnitude > limit)
                    {
                        return false;
                    }
                }
            }
            else
            {
                if (!long.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out magnitude)
                    || magnitude > limit)
                {
                    return false;
                }
            }

            var signedValue = magnitude * sign;
            if (signedValue < int.MinValue || signedValue > int.MaxValue)
            {
                return false;
            }

            value = (int)signedValue;
            return true;
        }
    }
}
