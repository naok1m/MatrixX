using FluentAssertions;
using InputBusX.Domain.Entities;
using InputBusX.Domain.Enums;
using InputBusX.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InputBusX.Tests.Infrastructure;

public class JsonConfigurationStoreTests : IDisposable
{
    private readonly string _tempPath;
    private readonly JsonConfigurationStore _store;

    public JsonConfigurationStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"inputbusx_test_{Guid.NewGuid():N}.json");
        _store = new JsonConfigurationStore(_tempPath, NullLogger<JsonConfigurationStore>.Instance);
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ShouldReturnDefault()
    {
        var config = _store.Load();

        config.Should().NotBeNull();
        config.Profiles.Should().HaveCountGreaterThan(0);
        config.Profiles[0].Name.Should().Be("Default");
        File.Exists(_tempPath).Should().BeTrue();
    }

    [Fact]
    public void Save_ThenLoad_ShouldRoundTrip()
    {
        var config = new AppConfiguration
        {
            ActiveProfileId = "test1",
            Profiles =
            [
                new Profile
                {
                    Id = "test1",
                    Name = "Test Profile",
                    IsDefault = true,
                    Macros =
                    [
                        new MacroDefinition
                        {
                            Name = "Test Macro",
                            Type = MacroType.NoRecoil,
                            Intensity = 0.75,
                            RecoilCompensationY = -400,
                            PingButton = GamepadButton.DPadUp,
                            TriggerSource = TriggerSource.LeftTrigger
                        }
                    ],
                    Filters = new FilterSettings
                    {
                        LeftStickDeadzone = 0.1,
                        ResponseCurveExponent = 1.5
                    }
                }
            ],
            General = new GeneralSettings
            {
                PollingRateMs = 2,
                MinimizeToTray = false
            }
        };

        _store.Save(config);
        var loaded = _store.Load();

        loaded.ActiveProfileId.Should().Be("test1");
        loaded.Profiles.Should().HaveCount(1);
        loaded.Profiles[0].Name.Should().Be("Test Profile");
        loaded.Profiles[0].Macros.Should().HaveCount(1);
        loaded.Profiles[0].Macros[0].RecoilCompensationY.Should().Be(-400);
        loaded.Profiles[0].Macros[0].PingButton.Should().Be(GamepadButton.DPadUp);
        loaded.Profiles[0].Macros[0].TriggerSource.Should().Be(TriggerSource.LeftTrigger);
        loaded.Profiles[0].Filters.LeftStickDeadzone.Should().Be(0.1);
        loaded.General.PollingRateMs.Should().Be(2);
    }

    [Fact]
    public void Load_WithCorruptedFile_ShouldReturnDefault()
    {
        File.WriteAllText(_tempPath, "{ invalid json !!!");

        var config = _store.Load();

        config.Should().NotBeNull();
        config.Profiles.Should().HaveCountGreaterThan(0);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);
    }
}
