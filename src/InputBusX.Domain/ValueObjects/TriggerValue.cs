namespace InputBusX.Domain.ValueObjects;

public readonly record struct TriggerValue(byte Value)
{
    public double Normalized => Value / 255.0;

    public bool IsPressed(byte threshold = 30) => Value >= threshold;

    public static TriggerValue Zero => new(0);
    public static TriggerValue Full => new(255);
}
