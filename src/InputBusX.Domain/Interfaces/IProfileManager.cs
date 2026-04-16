using InputBusX.Domain.Entities;

namespace InputBusX.Domain.Interfaces;

public interface IProfileManager
{
    event Action<Profile>? ActiveProfileChanged;

    Profile ActiveProfile { get; }
    IReadOnlyList<Profile> Profiles { get; }

    void SetActiveProfile(string profileId);
    Profile CreateProfile(string name);
    Profile DuplicateProfile(string profileId, string newName);
    void DeleteProfile(string profileId);
    void SaveProfile(Profile profile);
    void CheckProcessSwitch();
}
