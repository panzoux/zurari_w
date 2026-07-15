using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Zurari.Controls;

namespace Zurari.Gallery;

/// <summary>
/// Gallery harness: hosts a ColumnBrowser wired to a <see cref="GalleryBrowserSession"/>
/// over fake in-memory datasets, for visual verification without any real filesystem.
/// </summary>
public partial class MainWindow : Window
{
    private GalleryBrowserSession session = null!;

    public MainWindow()
    {
        InitializeComponent();

        Browser.CursorMoveRequested += (_, e) => Apply(
            () => session.HandleCursorMoveRequested(e),
            $"CursorMoveRequested(col={e.ColumnIndex}, move={e.Move}, visibleRows={e.VisibleRowCount})");
        Browser.ColumnFocusRequested += (_, e) => Apply(
            () => session.HandleColumnFocusRequested(e),
            $"ColumnFocusRequested(col={e.ColumnIndex})");
        Browser.EntryActivated += (_, e) => Apply(
            () => session.HandleEntryActivated(e),
            $"EntryActivated(col={e.ColumnIndex}, entry={e.EntryIndex})");
        Browser.NavigateUpRequested += (_, e) => Apply(
            () => session.HandleNavigateUpRequested(e),
            $"NavigateUpRequested(col={e.ColumnIndex})");
        Browser.EntryPointerPressed += (_, e) => Apply(
            () => session.HandleEntryPointerPressed(e),
            $"EntryPointerPressed(col={e.ColumnIndex}, entry={e.EntryIndex}, button={e.Button}, modifiers={e.Modifiers})");
        Browser.ColumnWidthsChanged += (_, e) =>
            LastEventText.Text = "ColumnWidthsChanged(widths="
                + string.Join(", ", e.Widths.Select(w => w.ToString("F0", CultureInfo.InvariantCulture)))
                + ")";

        DatasetSelector.SelectedIndex = 0;
    }

    private void Apply(Action handle, string eventText)
    {
        handle();
        Browser.Columns = session.Columns;
        LastEventText.Text = eventText;
    }

    private void OnDatasetSelected(object sender, SelectionChangedEventArgs e)
    {
        var name = (DatasetSelector.SelectedItem as ComboBoxItem)?.Content as string ?? "Small";
        session = name switch
        {
            "Huge" => new GalleryBrowserSession(GalleryDatasets.Huge()),
            "CJK" => new GalleryBrowserSession(GalleryDatasets.Cjk()),
            "Empty" => new GalleryBrowserSession(GalleryDatasets.Empty()),
            "Deep" => new GalleryBrowserSession(GalleryDatasets.Deep(), initialDepth: 8),
            _ => new GalleryBrowserSession(GalleryDatasets.Small()),
        };
        Browser.Columns = session.Columns;
        LastEventText.Text = $"Dataset switched: {name}";
    }
}
