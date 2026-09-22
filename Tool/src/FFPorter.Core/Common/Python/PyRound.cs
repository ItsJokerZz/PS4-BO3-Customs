using System.Globalization;
using System.Numerics;

namespace FFPorter.Core.Common.Python;

internal static class PyRound
{
    public static double Half(double value, int digits)
    {
        if (!double.IsFinite(value))
            return value;
        if (value == 0)
            return value;

        long bits = BitConverter.DoubleToInt64Bits(value);
        bool negative = bits < 0;
        int exponent = (int)((bits >> 52) & 0x7FF);
        long fraction = bits & 0xFFFFFFFFFFFFFL;
        BigInteger mantissa = exponent == 0 ? fraction : fraction | (1L << 52);
        int scale = (exponent == 0 ? 1 : exponent) - 1075;

        BigInteger power = BigInteger.Pow(10, digits);
        BigInteger numerator = mantissa * power;
        BigInteger denominator = BigInteger.One;
        if (scale >= 0)
            numerator <<= scale;
        else
            denominator = BigInteger.One << -scale;

        BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        int comparison = (remainder * 2).CompareTo(denominator);
        if (comparison > 0 || (comparison == 0 && !quotient.IsEven))
            quotient += 1;

        string text = quotient.ToString(CultureInfo.InvariantCulture);
        if (digits > 0)
        {
            text = text.PadLeft(digits + 1, '0');
            text = text[..^digits] + "." + text[^digits..];
        }
        double rounded = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return negative ? -rounded : rounded;
    }
}
