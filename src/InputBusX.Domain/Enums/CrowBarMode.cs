namespace InputBusX.Domain.Enums;

/// <summary>Operating mode for the CrowBar cooperative anti-recoil mechanic.</summary>
public enum CrowBarMode
{
    /// <summary>40% aim assist — player controls 60% of recoil manually. Lower automatic compensation.</summary>
    Rapido,

    /// <summary>90% aim assist — system handles most recoil control. Higher automatic compensation.</summary>
    Padrao,
}
