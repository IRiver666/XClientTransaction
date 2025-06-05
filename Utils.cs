namespace XClientTransaction;

public class Utils
{
    public static double[] Interpolate(double[] from, double[] to, double f)
    {
        if (from.Length != to.Length)
            throw new ArgumentException($"Mismatched interpolation args {from.Length} vs {to.Length}");
        
        return from.Select((v, i) => v * (1 - f) + to[i] * f).ToArray();
    }

    public static double[] ConvertRotationToMatrix(double deg)
    {
        var rad = deg * Math.PI / 180;
        return new[] { Math.Cos(rad), -Math.Sin(rad), Math.Sin(rad), Math.Cos(rad) };
    }

    public static int IsOdd(int num)
    {
        return num % 2 == 1 ? -1 : 0;
    }

    public static string FloatToHex(double xInput)
    {
        var result = new List<string>();
        var x = xInput;
        var quotient = (int)Math.Floor(x);
        var fraction = x - quotient;
        
        while (quotient > 0)
        {
            var q = (int)Math.Floor(x / 16);
            var rem = (int)Math.Floor(x - q * 16);
            result.Insert(0, rem > 9 ? ((char)(rem + 55)).ToString() : rem.ToString());
            x = q;
            quotient = (int)Math.Floor(x);
        }
        
        if (fraction == 0)
            return string.Join("", result);
        
        result.Add(".");
        var frac = fraction;
        while (frac > 0)
        {
            frac *= 16;
            var integer = (int)Math.Floor(frac);
            frac -= integer;
            result.Add(integer > 9 ? ((char)(integer + 55)).ToString() : integer.ToString());
        }
        
        return string.Join("", result);
    }
}