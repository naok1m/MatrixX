using Avalonia.Controls;
using Avalonia.Threading;
using InputBusX.UI.ViewModels;

namespace InputBusX.UI.Views;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is LogsViewModel vm)
            vm.ScrollToBottomRequested += ScrollToBottom;
    }

    private void ScrollToBottom()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var listBox = this.FindControl<ListBox>("LogListBox");
            if (listBox?.ItemCount > 0)
                listBox.ScrollIntoView(listBox.ItemCount - 1);
        });
    }
}
