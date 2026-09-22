using System.Globalization;
using System.Numerics;
using System.Text;

namespace FFPorter.Core.Common.Python;

public static class PyInt
{
    public enum Status
    {
        Ok,
        Invalid,
        TooManyDigits,
    }

    public const int MaxStrDigits = 4300;

    private static readonly int[] DigitRuns =
    [
        0x30, 0x660, 0x6F0, 0x7C0, 0x966, 0x9E6, 0xA66, 0xAE6, 0xB66, 0xBE6, 0xC66, 0xCE6, 0xD66, 0xDE6,
        0xE50, 0xED0, 0xF20, 0x1040, 0x1090, 0x17E0, 0x1810, 0x1946, 0x19D0, 0x1A80, 0x1A90, 0x1B50, 0x1BB0,
        0x1C40, 0x1C50, 0xA620, 0xA8D0, 0xA900, 0xA9D0, 0xA9F0, 0xAA50, 0xABF0, 0xFF10, 0x104A0, 0x10D30,
        0x11066, 0x110F0, 0x11136, 0x111D0, 0x112F0, 0x11450, 0x114D0, 0x11650, 0x116C0, 0x11730, 0x118E0,
        0x11950, 0x11C50, 0x11D50, 0x11DA0, 0x11F50, 0x16A60, 0x16AC0, 0x16B50, 0x1D7CE, 0x1D7D8, 0x1D7E2,
        0x1D7EC, 0x1D7F6, 0x1E140, 0x1E2F0, 0x1E4F0, 0x1E950, 0x1FBF0,
    ];

    private static readonly int[] UnicodeSpaces =
    [
        0x85, 0xA0, 0x1680, 0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009,
        0x200A, 0x2028, 0x2029, 0x202F, 0x205F, 0x3000,
    ];

    public static Status Parse(string text, out BigInteger value, out int digitCount)
    {
        value = BigInteger.Zero;
        digitCount = 0;

        var ascii = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            int codePoint = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                codePoint = char.ConvertToUtf32(text[i], text[++i]);
            if (codePoint < 127)
                ascii.Append((char)codePoint);
            else if (Array.BinarySearch(UnicodeSpaces, codePoint) >= 0)
                ascii.Append(' ');
            else if (DecimalValue(codePoint) is int digit and >= 0)
                ascii.Append((char)('0' + digit));
            else
                return Status.Invalid;
        }

        string s = ascii.ToString();
        int pos = 0;
        while (pos < s.Length && IsAsciiSpace(s[pos]))
            pos++;
        bool negative = false;
        if (pos < s.Length && (s[pos] == '+' || s[pos] == '-'))
        {
            negative = s[pos] == '-';
            pos++;
        }
        if (pos < s.Length && s[pos] == '_')
            return Status.Invalid;
        var digits = new StringBuilder();
        char previous = '\0';
        for (; pos < s.Length && (char.IsAsciiDigit(s[pos]) || s[pos] == '_'); pos++)
        {
            if (s[pos] == '_')
            {
                if (previous == '_')
                    return Status.Invalid;
            }
            else
            {
                digits.Append(s[pos]);
            }
            previous = s[pos];
        }
        if (previous == '_' || digits.Length == 0)
            return Status.Invalid;
        while (pos < s.Length && IsAsciiSpace(s[pos]))
            pos++;
        if (pos != s.Length)
            return Status.Invalid;

        digitCount = digits.Length;
        if (digitCount > MaxStrDigits)
            return Status.TooManyDigits;
        value = BigInteger.Parse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture);
        if (negative)
            value = -value;
        return Status.Ok;
    }

    public static bool TryParse(string text, out BigInteger value) => Parse(text, out value, out _) == Status.Ok;

    public static int DecimalValue(int codePoint)
    {
        int index = Array.BinarySearch(DigitRuns, codePoint);
        if (index < 0)
            index = ~index - 1;
        if (index < 0)
            return -1;
        int value = codePoint - DigitRuns[index];
        return value < 10 ? value : -1;
    }

    private static bool IsAsciiSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';
}
