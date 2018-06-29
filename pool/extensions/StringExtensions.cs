

using System;
using System.Globalization;

namespace XPool.extensions
{
    public static class StringExtensions
    {
                                                public static byte[] HexToByteArray(this string str)
        {
            if (str.StartsWith("0x"))
                str = str.Substring(2);

            var arr = new byte[str.Length >> 1];

            for(var i = 0; i < str.Length >> 1; ++i)
                arr[i] = (byte) ((GetHexVal(str[i << 1]) << 4) + GetHexVal(str[(i << 1) + 1]));

            return arr;
        }

        private static int GetHexVal(char hex)
        {
            var val = (int) hex;
            return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
        }

        public static string ToStringHex8(this uint value)
        {
            return value.ToString("x8", CultureInfo.InvariantCulture);
        }

        public static string ToStringHex8(this int value)
        {
            return value.ToString("x8", CultureInfo.InvariantCulture);
        }

        public static string ToStringHexWithPrefix(this ulong value)
        {
            if (value == 0)
                return "0x0";

            return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
        }

        public static string ToStringHexWithPrefix(this long value)
        {
            if (value == 0)
                return "0x0";

            return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
        }

        public static string ToStringHexWithPrefix(this uint value)
        {
            if (value == 0)
                return "0x0";

            return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
        }

        public static string ToStringHexWithPrefix(this int value)
        {
            if (value == 0)
                return "0x0";

            return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
        }

        public static T IntegralFromHex<T>(this string value)
        {
            var underlyingType = Nullable.GetUnderlyingType(typeof(T));

            if (value.StartsWith("0x"))
                value = value.Substring(2);

            if (!ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var val))
                throw new FormatException();

            return (T) Convert.ChangeType(val, underlyingType ?? typeof(T));
        }

        public static string ToLowerCamelCase(this string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            return char.ToLowerInvariant(str[0]) + str.Substring(1);
        }
    }
}
