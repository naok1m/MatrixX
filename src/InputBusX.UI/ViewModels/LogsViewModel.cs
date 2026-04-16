using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InputBusX.Domain.Interfaces;

namespace InputBusX.UI.ViewModels;

public partial class LogsViewModel : ViewModelBase
{
    private readonly ILogSink _logSink;
    private readonly List<LogEntry> _allEntries = [];

    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private string _filterText = "";

    public event Action? ScrollToBottomRequested;

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public LogsViewModel(ILogSink logSink)
    {
        _logSink = logSink;

        foreach (var entry in _logSink.RecentEntries)
        {
            _allEntries.Add(entry);
            LogEntries.Add(entry);
        }

        _logSink.LogReceived += OnLogReceived;
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void ClearLogs()
    {
        _allEntries.Clear();
        LogEntries.Clear();
    }

    private void OnLogReceived(LogEntry entry)
    {
        _allEntries.Add(entry);
        while (_allEntries.Count > 1000)
            _allEntries.RemoveAt(0);

        if (PassesFilter(entry))
        {
            LogEntries.Add(entry);
            while (LogEntries.Count > 500)
                LogEntries.RemoveAt(0);

            if (AutoScroll)
                ScrollToBottomRequested?.Invoke();
        }
    }

    private void ApplyFilter()
    {
        LogEntries.Clear();
        foreach (var entry in _allEntries.Where(PassesFilter))
            LogEntries.Add(entry);

        if (AutoScroll && LogEntries.Count > 0)
            ScrollToBottomRequested?.Invoke();
    }

    private bool PassesFilter(LogEntry entry)
    {
        if (string.IsNullOrEmpty(FilterText)) return true;
        return entry.Message.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || entry.Category.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || entry.Level.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }
}
