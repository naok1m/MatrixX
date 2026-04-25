using InputBusX.Application.Interfaces;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InputBusX.Application.Pipeline;

public sealed class InputPipeline : IInputPipeline
{
    private readonly IInputProvider _inputProvider;
    private readonly IOutputController _outputController;
    private readonly IMacroProcessor _macroProcessor;
    private readonly IInputFilter _inputFilter;
    private readonly IProfileManager _profileManager;
    private readonly ILogger<InputPipeline> _logger;
    private CancellationTokenSource? _cts;
    private bool _vigemAvailable;

    // Cached sorted-enabled macros — rebuilt only when the profile changes or macros are saved
    private List<MacroDefinition> _activeMacrosCache = [];
    private volatile bool _macroCacheDirty = true;

    public event Action<GamepadState>? InputProcessed;
    public event Action<GamepadState>? RawInputReceived;

    public bool IsRunning => _cts is { IsCancellationRequested: false };
    public bool ViGEmAvailable => _vigemAvailable;
    public int? VirtualXInputSlot => _outputController.VirtualXInputSlot;

    public InputPipeline(
        IInputProvider inputProvider,
        IOutputController outputController,
        IMacroProcessor macroProcessor,
        IInputFilter inputFilter,
        IProfileManager profileManager,
        ILogger<InputPipeline> logger)
    {
        _inputProvider    = inputProvider;
        _outputController = outputController;
        _macroProcessor   = macroProcessor;
        _inputFilter      = inputFilter;
        _profileManager   = profileManager;
        _logger           = logger;

        _profileManager.ActiveProfileChanged += _ => _macroCacheDirty = true;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (IsRunning) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _inputProvider.StateUpdated += OnStateUpdated;

        try
        {
            _outputController.Connect();
            _vigemAvailable = true;

            // Exclude the virtual controller's XInput slot from physical input polling.
            // Without this, XInputProvider reads the virtual device back as input,
            // creating a feedback loop and preventing Warzone from using the virtual slot.
            if (_outputController.VirtualXInputSlot.HasValue)
                _inputProvider.ExcludeXInputSlots(new[] { _outputController.VirtualXInputSlot.Value });

            _logger.LogInformation("Virtual controller connected");
        }
        catch (Exception ex)
        {
            _vigemAvailable = false;
            _logger.LogWarning("ViGEmBus not available — running in monitor-only mode. Install ViGEmBus to enable virtual controller output. Details: {Message}", ex.Message);
        }

        await _inputProvider.StartAsync(_cts.Token);
        _logger.LogInformation("Input pipeline started (ViGEm: {ViGEmStatus})", _vigemAvailable ? "active" : "unavailable");
    }

    public async Task StopAsync()
    {
        _inputProvider.StateUpdated -= OnStateUpdated;

        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        // Clear slot exclusion before disconnecting
        _inputProvider.ExcludeXInputSlots(Array.Empty<int>());
        _outputController.Disconnect();
        _macroProcessor.Reset();
        _inputFilter.Reset();

        await _inputProvider.StopAsync();
        _logger.LogInformation("Input pipeline stopped");
    }

    public void InvalidateMacroCache() => _macroCacheDirty = true;

    private void OnStateUpdated(string deviceId, GamepadState rawState)
    {
        try
        {
            RawInputReceived?.Invoke(rawState);

            var profile = _profileManager.ActiveProfile;

            // Step 1: Apply input filters
            var filtered = _inputFilter.Apply(rawState, profile.Filters);

            // Step 2: Rebuild macro list only when the profile changed or macros were saved.
            // Avoids allocating a new list and running LINQ on every input frame (~1000x/sec).
            if (_macroCacheDirty)
            {
                _activeMacrosCache = profile.Macros
                    .Where(m => m.Enabled)
                    .OrderByDescending(m => m.Priority)
                    .ToList();
                _macroCacheDirty = false;
            }

            var processed = _macroProcessor.Process(filtered, _activeMacrosCache);

            // Step 3: Send to virtual controller (if available)
            if (_vigemAvailable)
                _outputController.Update(processed);

            InputProcessed?.Invoke(processed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in input pipeline processing");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _outputController.Disconnect();
    }
}
