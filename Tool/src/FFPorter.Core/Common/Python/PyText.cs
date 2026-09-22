using System.Globalization;
using System.Text;

namespace FFPorter.Core.Common.Python;

public static class PyText
{
    public static bool IsSpace(char c) => (int)c switch
    {
        >= 0x09 and <= 0x0D => true,
        >= 0x1C and <= 0x20 => true,
        0x85 or 0xA0 or 0x1680 => true,
        >= 0x2000 and <= 0x200A => true,
        0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000 => true,
        _ => false,
    };

    public static string Strip(string text)
    {
        int start = 0, end = text.Length;
        while (start < end && IsSpace(text[start]))
            start++;
        while (end > start && IsSpace(text[end - 1]))
            end--;
        return start == 0 && end == text.Length ? text : text[start..end];
    }

    public static string Lower(string text)
    {
        const char CapitalIWithDot = (char)0x130;
        if (!text.Contains(CapitalIWithDot))
            return text.ToLowerInvariant();
        string lowered = "i" + (char)0x307;
        return string.Join(lowered, text.Split(CapitalIWithDot).Select(part => part.ToLowerInvariant()));
    }

    public static string Repr(string text)
    {
        char quote = text.Contains('\'') && !text.Contains('"') ? '"' : '\'';
        var builder = new StringBuilder(text.Length + 2);
        builder.Append(quote);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == quote || c == '\\')
            {
                builder.Append('\\').Append(c);
            }
            else if (c == '\t')
            {
                builder.Append("\\t");
            }
            else if (c == '\n')
            {
                builder.Append("\\n");
            }
            else if (c == '\r')
            {
                builder.Append("\\r");
            }
            else if (c < ' ' || c == 0x7F)
            {
                builder.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
            }
            else if (c < 0x7F)
            {
                builder.Append(c);
            }
            else
            {
                int codePoint = c;
                int width = 1;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(c, text[i + 1]);
                    width = 2;
                }
                if (IsPrintable(codePoint))
                    builder.Append(text, i, width);
                else if (codePoint <= 0xFF)
                    builder.Append("\\x").Append(codePoint.ToString("x2", CultureInfo.InvariantCulture));
                else if (codePoint <= 0xFFFF)
                    builder.Append("\\u").Append(codePoint.ToString("x4", CultureInfo.InvariantCulture));
                else
                    builder.Append("\\U").Append(codePoint.ToString("x8", CultureInfo.InvariantCulture));
                i += width - 1;
            }
        }
        builder.Append(quote);
        return builder.ToString();
    }

    private static readonly (int First, int Last)[] AssignedAfterUnicode15 =
    [
        (0x897, 0x897), (0x1B4E, 0x1B4F), (0x1B7F, 0x1B7F), (0x1C89, 0x1C8A), (0x2427, 0x2429), (0x2FFC, 0x2FFF),
        (0x31E4, 0x31E5), (0x31EF, 0x31EF), (0xA7CB, 0xA7CD), (0xA7DA, 0xA7DC), (0x105C0, 0x105F3), (0x10D40, 0x10D65),
        (0x10D69, 0x10D85), (0x10D8E, 0x10D8F), (0x10EC2, 0x10EC4), (0x10EFC, 0x10EFC), (0x11380, 0x11389), (0x1138B, 0x1138B),
        (0x1138E, 0x1138E), (0x11390, 0x113B5), (0x113B7, 0x113C0), (0x113C2, 0x113C2), (0x113C5, 0x113C5), (0x113C7, 0x113CA),
        (0x113CC, 0x113D5), (0x113D7, 0x113D8), (0x113E1, 0x113E2), (0x116D0, 0x116E3), (0x11BC0, 0x11BE1), (0x11BF0, 0x11BF9),
        (0x11F5A, 0x11F5A), (0x13460, 0x143FA), (0x16100, 0x16139), (0x16D40, 0x16D79), (0x18CFF, 0x18CFF), (0x1CC00, 0x1CCF9),
        (0x1CD00, 0x1CEB3), (0x1E5D0, 0x1E5FA), (0x1E5FF, 0x1E5FF), (0x1F8B2, 0x1F8BB), (0x1F8C0, 0x1F8C1), (0x1FA89, 0x1FA89),
        (0x1FA8F, 0x1FA8F), (0x1FABE, 0x1FABE), (0x1FAC6, 0x1FAC6), (0x1FADC, 0x1FADC), (0x1FADF, 0x1FADF), (0x1FAE9, 0x1FAE9),
        (0x1FBCB, 0x1FBEF), (0x2EBF0, 0x2EE5D),
    ];

    public static bool IsPrintable(int codePoint)
    {
        if (codePoint == ' ')
            return true;
        if (IsAssignedAfterUnicode15(codePoint))
            return false;
        return CharUnicodeInfo.GetUnicodeCategory(codePoint) is not (UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.SpaceSeparator);
    }

    private static bool IsAssignedAfterUnicode15(int codePoint)
    {
        int low = 0, high = AssignedAfterUnicode15.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            (int first, int last) = AssignedAfterUnicode15[middle];
            if (codePoint < first)
                high = middle - 1;
            else if (codePoint > last)
                low = middle + 1;
            else
                return true;
        }
        return false;
    }

    public static string Truncate(string text, int maxCodePoints)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (count == maxCodePoints)
                return text[..i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                i++;
            count++;
        }
        return text;
    }
}
