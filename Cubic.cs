namespace XClientTransaction;

public class Cubic
{
    private readonly double[] _curves;

    public Cubic(double[] curves)
    {
        _curves = curves;
    }

    public double GetValue(double time)
    {
        double start = 0, end = 1, mid = 0;
        
        if (time <= 0)
        {
            var startGrad = _curves[0] > 0
                ? _curves[1] / _curves[0]
                : _curves[1] == 0 && _curves[2] > 0
                    ? _curves[3] / _curves[2]
                    : 0;
            return startGrad * time;
        }
        
        if (time >= 1)
        {
            var endGrad = _curves[2] < 1
                ? (_curves[3] - 1) / (_curves[2] - 1)
                : _curves[2] == 1 && _curves[0] < 1
                    ? (_curves[1] - 1) / (_curves[0] - 1)
                    : 0;
            return 1 + endGrad * (time - 1);
        }
        
        while (start < end)
        {
            mid = (start + end) / 2;
            var xEst = Calculate(_curves[0], _curves[2], mid);
            if (Math.Abs(time - xEst) < 0.00001) 
                return Calculate(_curves[1], _curves[3], mid);
            
            if (xEst < time) 
                start = mid;
            else 
                end = mid;
        }
        
        return Calculate(_curves[1], _curves[3], mid);
    }

    private static double Calculate(double a, double b, double m)
    {
        return 3 * a * Math.Pow(1 - m, 2) * m + 3 * b * (1 - m) * m * m + Math.Pow(m, 3);
    }
}