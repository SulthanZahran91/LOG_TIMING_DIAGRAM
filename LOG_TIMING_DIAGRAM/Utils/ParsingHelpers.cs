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
            if (string.IsNullOrWhiteSpace(input))
            {
                value = 0;
                return false;
            }

            return int.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
