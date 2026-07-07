using System.Windows;
using System.Windows.Controls;
using Zurari.Controls;

namespace Zurari.Gallery;

/// <summary>
/// Gallery harness: hosts a ColumnBrowser with fake data for visual verification.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Browser.Columns = new[]
        {
            new ColumnVm(
                "placeholder",
                new[] { new EntryVm("hello", EntryKind.Directory, false, null, null) },
                0,
                true),
        };
    }

    private void OnDatasetSelected(object sender, SelectionChangedEventArgs e)
    {
        // Wired up in the "complete the Gallery" task.
    }
}
