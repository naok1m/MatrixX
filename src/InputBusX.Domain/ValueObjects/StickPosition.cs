namespace InputBusX.Domain.ValueObjects;

public readonly record struct StickPosition(short X, short Y)
{
    public double Magnitude => Math.Sqrt(X * (double)X + Y * (double)Y);

    public double NormalizedMagnitude => Math.Min(Magnitude / short.MaxValue, 1.0);

    public double Angle => Math.Atan2(Y, X);

    public StickPosition WithDeadzone(double deadzone)
    {
        if (NormalizedMagnitude < deadzone)
            return new StickPosition(0, 0);
        return this;
    }

    public static StickPosition Zero => new(0, 0);
}
