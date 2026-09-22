using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace FFPorter.Core.Common.Python;

public static class PyJson
{
    public static string Dumps(object? value, int? indent = 2)
    {
        var builder = new StringBuilder();
        Write(builder, value, indent, 0);
        return builder.ToString();
    }

    public static byte[] DumpsUtf8(object? value, int? indent = 2) => Encoding.UTF8.GetBytes(Dumps(value, indent));

    public static void DumpsTo(StringBuilder builder, object? value, int? indent = 2)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Write(builder, value, indent, 0);
    }

    private static void Write(StringBuilder builder, object? value, int? indent, int level)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                break;
            case bool flag:
                builder.Append(flag ? "true" : "false");
                break;
            case string text:
                WriteString(builder, text);
                break;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case BigInteger big:
                builder.Append(big.ToString(CultureInfo.InvariantCulture));
                break;
            case double real:
                builder.Append(FloatRepr(real));
                break;
            case float single:
                builder.Append(FloatRepr(single));
                break;
            case IEnumerable<KeyValuePair<string, object?>> map:
                WriteObject(builder, map, indent, level);
                break;
            case byte[]:
                throw new NotSupportedException("Object of type bytes is not JSON serializable");
            case IEnumerable sequence:
                WriteArray(builder, sequence, indent, level);
                break;
            default:
                throw new NotSupportedException($"Object of type {value.GetType().Name} is not JSON serializable");
        }
    }

    private static void WriteObject(StringBuilder builder, IEnumerable<KeyValuePair<string, object?>> map, int? indent, int level)
    {
        using var entries = map.GetEnumerator();
        if (!entries.MoveNext())
        {
            builder.Append("{}");
            return;
        }
        builder.Append('{');
        string separator = OpenLevel(builder, indent, level + 1);
        bool first = true;
        do
        {
            if (!first)
                builder.Append(separator);
            first = false;
            WriteString(builder, entries.Current.Key);
            builder.Append(": ");
            Write(builder, entries.Current.Value, indent, level + 1);
        } while (entries.MoveNext());
        CloseLevel(builder, indent, level);
        builder.Append('}');
    }

    private static void WriteArray(StringBuilder builder, IEnumerable sequence, int? indent, int level)
    {
        var items = sequence.GetEnumerator();
        if (!items.MoveNext())
        {
            builder.Append("[]");
            return;
        }
        builder.Append('[');
        string separator = OpenLevel(builder, indent, level + 1);
        bool first = true;
        do
        {
            if (!first)
                builder.Append(separator);
            first = false;
            Write(builder, items.Current, indent, level + 1);
        } while (items.MoveNext());
        CloseLevel(builder, indent, level);
        builder.Append(']');
    }

    private static string OpenLevel(StringBuilder builder, int? indent, int level)
    {
        if (indent is null)
            return ", ";
        string newlineIndent = "\n" + new string(' ', indent.Value * level);
        builder.Append(newlineIndent);
        return "," + newlineIndent;
    }

    private static void CloseLevel(StringBuilder builder, int? indent, int level)
    {
        if (indent is not null)
            builder.Append('\n').Append(' ', indent.Value * level);
    }

    private static void WriteString(StringBuilder builder, string text)
    {
        builder.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                default:
                    if (c >= ' ' && c <= '~')
                        builder.Append(c);
                    else
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
            }
        }
        builder.Append('"');
    }

    public static string FloatRepr(double value)
    {
        if (double.IsNaN(value))
            return "NaN";
        if (double.IsPositiveInfinity(value))
            return "Infinity";
        if (double.IsNegativeInfinity(value))
            return "-Infinity";
        if (value == 0)
            return double.IsNegative(value) ? "-0.0" : "0.0";

        string r = value.ToString("R", CultureInfo.InvariantCulture);
        bool negative = r.StartsWith('-');
        if (negative)
            r = r[1..];
        int exponent = 0;
        int e = r.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            exponent = int.Parse(r[(e + 1)..], CultureInfo.InvariantCulture);
            r = r[..e];
        }
        int point = r.IndexOf('.');
        string digits = point >= 0 ? r.Remove(point, 1) : r;
        int decimalPoint = (point >= 0 ? point : r.Length) + exponent;
        int leadingZeros = digits.Length - digits.TrimStart('0').Length;
        digits = digits.Trim('0');
        decimalPoint -= leadingZeros;
        if (digits.Length == 0)
            digits = "0";

        string body;
        if (decimalPoint > -4 && decimalPoint <= 16)
        {
            if (decimalPoint <= 0)
                body = "0." + new string('0', -decimalPoint) + digits;
            else if (decimalPoint >= digits.Length)
                body = digits + new string('0', decimalPoint - digits.Length) + ".0";
            else
                body = digits[..decimalPoint] + "." + digits[decimalPoint..];
        }
        else
        {
            int shown = decimalPoint - 1;
            string mantissa = digits.Length > 1 ? digits[0] + "." + digits[1..] : digits;
            body = mantissa + "e" + (shown < 0 ? "-" : "+") + Math.Abs(shown).ToString("00", CultureInfo.InvariantCulture);
        }
        return negative ? "-" + body : body;
    }

    public static object? Loads(string text) => PyJsonDecoder.Loads(text);
}
