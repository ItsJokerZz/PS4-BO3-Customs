using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace FFPorter.Core.Common.Python;

public sealed class PyJsonDecodeException(string message) : PyValueError(message);

internal static class PyJsonDecoder
{
    private const int MaxIntDigits = 4300;

    private sealed class StopIteration(int index) : Exception
    {
        public int Index { get; } = index;
    }

    public static object? Loads(string s)
    {
        if (s.Length > 0 && s[0] == (char)0xFEFF)
            throw Error("Unexpected UTF-8 BOM (decode using utf-8-sig)", s, 0);

        int idx = SkipWhitespace(s, 0);
        object? value;
        int end;
        try
        {
            value = ScanOnce(s, idx, out end);
        }
        catch (StopIteration stop)
        {
            throw Error("Expecting value", s, stop.Index);
        }
        end = SkipWhitespace(s, end);
        if (end != s.Length)
            throw Error("Extra data", s, end);
        return value;
    }

    private static int SkipWhitespace(string s, int idx)
    {
        while (idx < s.Length && s[idx] is ' ' or '\t' or '\n' or '\r')
            idx++;
        return idx;
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static object? ScanOnce(string s, int idx, out int next)
    {
        int length = s.Length;
        if (idx >= length)
            throw new StopIteration(idx);
        switch (s[idx])
        {
            case '"':
                return ScanString(s, idx + 1, out next);
            case '{':
                RuntimeHelpers.EnsureSufficientExecutionStack();
                return ParseObject(s, idx + 1, out next);
            case '[':
                RuntimeHelpers.EnsureSufficientExecutionStack();
                return ParseArray(s, idx + 1, out next);
            case 'n':
                if (idx + 3 < length && s[idx + 1] == 'u' && s[idx + 2] == 'l' && s[idx + 3] == 'l')
                {
                    next = idx + 4;
                    return null;
                }
                break;
            case 't':
                if (idx + 3 < length && s[idx + 1] == 'r' && s[idx + 2] == 'u' && s[idx + 3] == 'e')
                {
                    next = idx + 4;
                    return true;
                }
                break;
            case 'f':
                if (idx + 4 < length && s[idx + 1] == 'a' && s[idx + 2] == 'l' && s[idx + 3] == 's' && s[idx + 4] == 'e')
                {
                    next = idx + 5;
                    return false;
                }
                break;
            case 'N':
                if (idx + 2 < length && s[idx + 1] == 'a' && s[idx + 2] == 'N')
                {
                    next = idx + 3;
                    return double.NaN;
                }
                break;
            case 'I':
                if (idx + 7 < length && string.CompareOrdinal(s, idx + 1, "nfinity", 0, 7) == 0)
                {
                    next = idx + 8;
                    return double.PositiveInfinity;
                }
                break;
            case '-':
                if (idx + 8 < length && string.CompareOrdinal(s, idx + 1, "Infinity", 0, 8) == 0)
                {
                    next = idx + 9;
                    return double.NegativeInfinity;
                }
                break;
        }
        return MatchNumber(s, idx, out next);
    }

    private static JsonMap ParseObject(string s, int idx, out int next)
    {
        int endIdx = s.Length - 1;
        var result = new JsonMap();
        idx = SkipWhitespace(s, idx);
        if (idx > endIdx || s[idx] != '}')
        {
            while (true)
            {
                if (idx > endIdx || s[idx] != '"')
                    throw Error("Expecting property name enclosed in double quotes", s, idx);
                string key = ScanString(s, idx + 1, out idx);
                idx = SkipWhitespace(s, idx);
                if (idx > endIdx || s[idx] != ':')
                    throw Error("Expecting ':' delimiter", s, idx);
                idx = SkipWhitespace(s, idx + 1);
                result[key] = ScanOnce(s, idx, out idx);
                idx = SkipWhitespace(s, idx);
                if (idx <= endIdx && s[idx] == '}')
                    break;
                if (idx > endIdx || s[idx] != ',')
                    throw Error("Expecting ',' delimiter", s, idx);
                idx = SkipWhitespace(s, idx + 1);
            }
        }
        next = idx + 1;
        return result;
    }

    private static List<object?> ParseArray(string s, int idx, out int next)
    {
        int endIdx = s.Length - 1;
        var result = new List<object?>();
        idx = SkipWhitespace(s, idx);
        if (idx > endIdx || s[idx] != ']')
        {
            while (true)
            {
                result.Add(ScanOnce(s, idx, out idx));
                idx = SkipWhitespace(s, idx);
                if (idx <= endIdx && s[idx] == ']')
                    break;
                if (idx > endIdx || s[idx] != ',')
                    throw Error("Expecting ',' delimiter", s, idx);
                idx = SkipWhitespace(s, idx + 1);
            }
        }
        next = idx + 1;
        return result;
    }

    private static string ScanString(string s, int end, out int next)
    {
        int len = s.Length;
        int begin = end - 1;
        StringBuilder? builder = null;
        while (true)
        {
            int n;
            char c = '\0';
            for (n = end; n < len; n++)
            {
                c = s[n];
                if (c is '"' or '\\')
                    break;
                if (c <= 0x1F)
                    throw Error("Invalid control character at", s, n);
            }
            if (n >= len)
                throw Error("Unterminated string starting at", s, begin);

            if (c == '"' && builder is null)
            {
                next = n + 1;
                return s.Substring(end, n - end);
            }
            builder ??= new StringBuilder();
            builder.Append(s, end, n - end);
            n++;
            if (c == '"')
            {
                next = n;
                return builder.ToString();
            }
            if (n == len)
                throw Error("Unterminated string starting at", s, begin);

            c = s[n];
            if (c != 'u')
            {
                end = n + 1;
                char decoded = c switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => '\0',
                };
                if (decoded == '\0')
                    throw Error("Invalid \\escape", s, end - 2);
                builder.Append(decoded);
                continue;
            }

            n++;
            end = n + 4;
            if (end >= len)
                throw Error("Invalid \\uXXXX escape", s, n - 1);
            int unit = Hex4(s, n, end);
            n = end;
            if (unit is >= 0xD800 and <= 0xDBFF && end + 6 < len && s[n] == '\\' && s[n + 1] == 'u')
            {
                int low = Hex4(s, n + 2, end + 6);
                if (low is >= 0xDC00 and <= 0xDFFF)
                {
                    builder.Append((char)unit).Append((char)low);
                    end += 6;
                    continue;
                }
            }
            builder.Append((char)unit);
        }
    }

    private static int Hex4(string s, int from, int to)
    {
        int value = 0;
        for (int i = from; i < to; i++)
        {
            char digit = s[i];
            value <<= 4;
            if (digit is >= '0' and <= '9')
                value |= digit - '0';
            else if (digit is >= 'a' and <= 'f')
                value |= digit - 'a' + 10;
            else if (digit is >= 'A' and <= 'F')
                value |= digit - 'A' + 10;
            else
                throw Error("Invalid \\uXXXX escape", s, to - 5);
        }
        return value;
    }

    private static object MatchNumber(string s, int start, out int next)
    {
        int endIdx = s.Length - 1;
        int idx = start;
        bool isFloat = false;

        if (s[idx] == '-')
        {
            idx++;
            if (idx > endIdx)
                throw new StopIteration(start);
        }
        if (s[idx] is >= '1' and <= '9')
        {
            idx++;
            while (idx <= endIdx && IsDigit(s[idx]))
                idx++;
        }
        else if (s[idx] == '0')
        {
            idx++;
        }
        else
        {
            throw new StopIteration(start);
        }

        if (idx < endIdx && s[idx] == '.' && IsDigit(s[idx + 1]))
        {
            isFloat = true;
            idx += 2;
            while (idx <= endIdx && IsDigit(s[idx]))
                idx++;
        }

        if (idx < endIdx && s[idx] is 'e' or 'E')
        {
            int eStart = idx;
            idx++;
            if (idx < endIdx && s[idx] is '-' or '+')
                idx++;
            while (idx <= endIdx && IsDigit(s[idx]))
                idx++;
            if (IsDigit(s[idx - 1]))
                isFloat = true;
            else
                idx = eStart;
        }

        string text = s.Substring(start, idx - start);
        next = idx;
        if (isFloat)
        {
            return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        int digits = text.Length - (text[0] == '-' ? 1 : 0);
        if (digits > MaxIntDigits)
        {
            throw new PyValueError($"Exceeds the limit ({MaxIntDigits} digits) for integer string conversion: value has {digits} digits;"
                + " use sys.set_int_max_str_digits() to increase the limit");
        }
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long small))
            return small;
        return BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static PyJsonDecodeException Error(string message, string s, int pos)
    {
        int point = CodePointIndex(s, pos);
        int line = 1;
        foreach (char c in s.AsSpan(0, pos))
        {
            if (c == '\n')
                line++;
        }
        int lastNewline = pos == 0 ? -1 : s.LastIndexOf('\n', pos - 1);
        int column = lastNewline < 0 ? point + 1 : point - CodePointIndex(s, lastNewline);
        return new PyJsonDecodeException($"{message}: line {line} column {column} (char {point})");
    }

    private static int CodePointIndex(string s, int utf16Index)
    {
        int pairs = 0;
        for (int i = 0; i + 1 < utf16Index; i++)
        {
            if (char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
            {
                pairs++;
                i++;
            }
        }
        return utf16Index - pairs;
    }
}
