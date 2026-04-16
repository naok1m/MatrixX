using InputBusX.Domain.Entities;

namespace InputBusX.Domain.Interfaces;

public interface IInputFilter
{
    GamepadState Apply(GamepadState state, FilterSettings settings);
    void Reset();
}
