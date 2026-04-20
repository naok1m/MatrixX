namespace InputBusX.Domain.Enums;

public enum TriggerSource
{
    /// <summary>Not gated by a trigger — only the ActivationButton (if set) controls this macro.</summary>
    None,
    LeftTrigger,
    RightTrigger
}