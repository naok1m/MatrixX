using InputBusX.Domain.Entities;

namespace InputBusX.Domain.Interfaces;

public interface IMacroProcessor
{
    GamepadState Process(GamepadState input, IReadOnlyList<MacroDefinition> activeMacros);
    void Reset();

    /// <summary>
    /// Override the recoil values used by all NoRecoil macros with those from the given
    /// weapon profile. Pass null to revert to the macro's own configured values.
    /// </summary>
    void SetWeaponProfile(WeaponProfile? profile);
}
