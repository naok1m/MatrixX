using CommunityToolkit.Mvvm.ComponentModel;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;

namespace InputBusX.UI.ViewModels;

public partial class ScriptStepViewModel : ViewModelBase
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private ScriptActionKind _action = ScriptActionKind.Wait;
    [ObservableProperty] private GamepadButton _button;
    [ObservableProperty] private AnalogAxis _axis;
    [ObservableProperty] private int _value;
    [ObservableProperty] private int _durationMs = 16;
    [ObservableProperty] private int _loopTargetIndex;
    [ObservableProperty] private int _repeatCount;
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private bool _disabled;

    // UI visibility helpers per action type
    public bool ShowButton => Action is ScriptActionKind.PressButton or ScriptActionKind.ReleaseButton;
    public bool ShowAxis => Action is ScriptActionKind.SetAxis or ScriptActionKind.SetTrigger;
    public bool ShowValue => Action is ScriptActionKind.SetAxis or ScriptActionKind.SetTrigger;
    public bool ShowDuration => Action == ScriptActionKind.Wait;
    public bool ShowLoopTarget => Action == ScriptActionKind.LoopBack;
    public bool ShowRepeatCount => Action is ScriptActionKind.LoopBack or ScriptActionKind.LoopStart;

    // Human-readable summary for the step list
    public string Summary => Action switch
    {
        ScriptActionKind.PressButton => $"Press {Button}",
        ScriptActionKind.ReleaseButton => $"Release {Button}",
        ScriptActionKind.SetAxis => $"{Axis} = {Value}",
        ScriptActionKind.SetTrigger => $"{Axis} = {Value}",
        ScriptActionKind.Wait => $"Wait {DurationMs}ms",
        ScriptActionKind.LoopStart => RepeatCount > 0 ? $"Loop {RepeatCount}x" : "Loop (infinite)",
        ScriptActionKind.LoopBack => $"Jump to #{LoopTargetIndex}",
        _ => Action.ToString()
    };

    // Color coding for the step list
    public string ActionColor => Action switch
    {
        ScriptActionKind.PressButton => "#4CAF50",
        ScriptActionKind.ReleaseButton => "#F44336",
        ScriptActionKind.SetAxis => "#2196F3",
        ScriptActionKind.SetTrigger => "#9C27B0",
        ScriptActionKind.Wait => "#FF9800",
        ScriptActionKind.LoopStart => "#00BCD4",
        ScriptActionKind.LoopBack => "#00BCD4",
        _ => "#4A4F5A"
    };

    partial void OnActionChanged(ScriptActionKind value)
    {
        OnPropertyChanged(nameof(ShowButton));
        OnPropertyChanged(nameof(ShowAxis));
        OnPropertyChanged(nameof(ShowValue));
        OnPropertyChanged(nameof(ShowDuration));
        OnPropertyChanged(nameof(ShowLoopTarget));
        OnPropertyChanged(nameof(ShowRepeatCount));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ActionColor));
    }

    partial void OnButtonChanged(GamepadButton value) => OnPropertyChanged(nameof(Summary));
    partial void OnAxisChanged(AnalogAxis value) => OnPropertyChanged(nameof(Summary));
    partial void OnValueChanged(int value) => OnPropertyChanged(nameof(Summary));
    partial void OnDurationMsChanged(int value) => OnPropertyChanged(nameof(Summary));
    partial void OnLoopTargetIndexChanged(int value) => OnPropertyChanged(nameof(Summary));
    partial void OnRepeatCountChanged(int value) => OnPropertyChanged(nameof(Summary));

    public ScriptStep ToModel() => new()
    {
        Action = Action,
        Button = ShowButton ? Button : null,
        Axis = ShowAxis ? Axis : null,
        Value = (short)Math.Clamp(Value, short.MinValue, short.MaxValue),
        DurationMs = DurationMs,
        LoopTargetIndex = LoopTargetIndex,
        RepeatCount = RepeatCount,
        Label = Label,
        Disabled = Disabled,
    };

    public static ScriptStepViewModel FromModel(ScriptStep step, int index) => new()
    {
        Index = index,
        Action = step.Action,
        Button = step.Button ?? GamepadButton.None,
        Axis = step.Axis ?? AnalogAxis.LeftStickX,
        Value = (int)step.Value,
        DurationMs = step.DurationMs,
        LoopTargetIndex = step.LoopTargetIndex,
        RepeatCount = step.RepeatCount,
        Label = step.Label,
        Disabled = step.Disabled,
    };
}
