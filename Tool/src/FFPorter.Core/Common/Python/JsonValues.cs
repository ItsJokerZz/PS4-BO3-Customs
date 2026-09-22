using System.Numerics;

namespace FFPorter.Core.Common.Python;

public static class JsonValues
{
    public static bool Truthy(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        long number => number != 0,
        BigInteger big => !big.IsZero,
        double real => real != 0,
        string text => text.Length > 0,
        List<object?> list => list.Count > 0,
        JsonMap map => map.Count > 0,
        _ => true,
    };

    public static bool PyEquals(object? a, object? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        if (IsNumber(a) && IsNumber(b))
            return NumbersEqual(a, b);
        switch (a)
        {
            case string text:
                return b is string other && string.Equals(text, other, StringComparison.Ordinal);
            case List<object?> list:
                if (b is not List<object?> items || items.Count != list.Count)
                    return false;
                for (int i = 0; i < list.Count; i++)
                {
                    if (!PyEquals(list[i], items[i]))
                        return false;
                }
                return true;
            case JsonMap map:
                if (b is not JsonMap entries || entries.Count != map.Count)
                    return false;
                foreach ((string key, object? value) in map)
                {
                    if (!entries.TryGetValue(key, out object? otherValue) || !PyEquals(value, otherValue))
                        return false;
                }
                return true;
            default:
                return Equals(a, b);
        }
    }

    public static bool TryIntKey(object? value, out long key)
    {
        key = 0;
        switch (value)
        {
            case long number:
                key = number;
                return true;
            case bool flag:
                key = flag ? 1 : 0;
                return true;
            case BigInteger big when big >= long.MinValue && big <= long.MaxValue:
                key = (long)big;
                return true;
            case double real when double.IsFinite(real) && Math.Floor(real) == real && real >= -9.2233720368547758E18 && real < 9.2233720368547758E18:
                key = (long)real;
                return true;
            case List<object?>:
                throw new InvalidDataException("TypeError: unhashable type: 'list'");
            case JsonMap:
                throw new InvalidDataException("TypeError: unhashable type: 'dict'");
            default:
                return false;
        }
    }

    public static long? AsInt64(object? value) => value switch
    {
        long number => number,
        BigInteger big when big >= long.MinValue && big <= long.MaxValue => (long)big,
        _ => null,
    };

    private static bool IsNumber(object value) => value is bool or long or BigInteger or double;

    private static bool NumbersEqual(object a, object b)
    {
        if (a is double x || b is double)
        {
            double left = ToDouble(a), right = ToDouble(b);
            if (a is double && b is double)
                return left == right;
            BigInteger integer = a is double ? ToBigInteger(b) : ToBigInteger(a);
            double real = a is double ? left : right;
            return double.IsFinite(real) && Math.Floor(real) == real && new BigInteger(real) == integer;
        }
        return ToBigInteger(a) == ToBigInteger(b);
    }

    private static double ToDouble(object value) => value switch
    {
        double real => real,
        bool flag => flag ? 1 : 0,
        long number => number,
        BigInteger big => (double)big,
        _ => double.NaN,
    };

    private static BigInteger ToBigInteger(object value) => value switch
    {
        bool flag => flag ? BigInteger.One : BigInteger.Zero,
        long number => number,
        BigInteger big => big,
        _ => BigInteger.Zero,
    };
}
