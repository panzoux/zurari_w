using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Zurari.Controls;
using Zurari.Core;

namespace Zurari.App;

/// <summary>
/// One pane on screen: its column browser, its preview, and the splitter between them. Holds no
/// state of its own beyond what WPF needs; MainWindow renders into it and listens to it.
/// </summary>
public partial class PaneView : UserControl
{
    /// <summary>
    /// Caps decode resolution (Phase 5 fix P1) so a huge photo cannot make the UI thread's decode
    /// pass itself sluggish - roughly the preview pane's width; <see cref="BitmapImage.DecodePixelWidth"/>
    /// alone (leaving <see cref="BitmapImage.DecodePixelHeight"/> at its default 0) preserves aspect
    /// ratio automatically.
    /// </summary>
    private const int PreviewDecodePixelWidth = 1024;

    /// <summary>
    /// The <see cref="StateProjection.PreviewVm.Generation"/> of the image most recently decoded
    /// into <see cref="PreviewImage"/>'s source - lets <see cref="RenderPreview"/> skip re-decoding
    /// the same <c>BitmapImage</c> on every unrelated re-render (e.g. a job progress tick) and only
    /// pay the decode cost when the previewed file actually changed.
    /// </summary>
    private int lastDecodedImageGeneration = -1;

    /// <summary>The preview header's two lines, kept so a pane resize can re-fit them - see <see cref="FitPreviewHeader"/>.</summary>
    private string? previewFileName;

    private string? previewOriginalDirectory;

    public PaneView()
    {
        InitializeComponent();

        // The header's two lines are shortened to fit, so a resize has to re-shorten them - the
        // splitter drag is the whole reason the preview pane's width is not a constant.
        PreviewPane.SizeChanged += (_, _) => FitPreviewHeader();
    }

    /// <summary>The pane's column browser, for MainWindow to wire events to and focus.</summary>
    public ColumnBrowser ColumnBrowser => Browser;

    /// <summary>The preview column's width, shared by both panes (MainWindow keeps them equal).</summary>
    public double PreviewWidth
    {
        get => PreviewColumn.ActualWidth;
        set => PreviewColumn.Width = new GridLength(value);
    }

    /// <summary>Raised when the preview splitter finishes a drag, so MainWindow can store and mirror the width.</summary>
    public event EventHandler? PreviewWidthChanged;

    /// <summary>Raised when the timed-out preview's retry link is clicked.</summary>
    public event EventHandler? RetryRequested;

    private void OnPreviewRetryClick(object sender, RoutedEventArgs e) => RetryRequested?.Invoke(this, EventArgs.Empty);

    private void OnPreviewSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        // The width is read from ActualWidth, so let the last drag delta's layout pass finish first.
        UpdateLayout();
        PreviewWidthChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Renders the preview header - the folder a deleted file came from, then its name - shortening
    /// each from the middle to fit the pane's current width.
    /// </summary>
    /// <remarks>
    /// Called both when the preview changes and when the pane is resized, so it reads its text from
    /// <see cref="previewFileName"/>/<see cref="previewOriginalDirectory"/> rather than taking it as
    /// an argument: a resize has no new state to project from.
    /// </remarks>
    private void FitPreviewHeader()
    {
        var width = PreviewPane.ActualWidth;
        TextFitter.Fit(PreviewFileName, previewFileName, width, TextTrim.Middle);
        TextFitter.Fit(PreviewOriginalPath, previewOriginalDirectory, width, TextTrim.Path);
        PreviewOriginalPath.Visibility =
            string.IsNullOrEmpty(previewOriginalDirectory) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Draws how full a volume is: a summary line, a bar, and what is left.
    /// </summary>
    /// <remarks>
    /// The bar is two star-sized grid columns rather than a width computed here, so it follows the
    /// splitter without this method being told about it. The recycle bin has a size but no capacity,
    /// so it gets the figures and no bar - a bar with nothing to be full of would be a lie.
    /// </remarks>
    private void RenderCapacity(StateProjection.CapacityVm capacity)
    {
        PreviewCapacitySummary.Text = capacity.Summary;

        if (capacity.UsedFraction is { } fraction)
        {
            PreviewCapacityUsed.Width = new GridLength(fraction, GridUnitType.Star);
            PreviewCapacityFree.Width = new GridLength(1 - fraction, GridUnitType.Star);
            PreviewCapacityBar.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewCapacityBar.Visibility = Visibility.Collapsed;
        }

        PreviewCapacityFreeText.Text = capacity.Free is { } free ? $"空き {free}" : string.Empty;
        PreviewCapacityFreeText.Visibility =
            capacity.Free is null ? Visibility.Collapsed : Visibility.Visible;
        PreviewCapacityPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Wires a <see cref="StateProjection.PreviewVm"/> onto the preview pane's four
    /// mutually-exclusive content views (image / text-or-hex / capacity bar / status message), toggling
    /// visibility by <see cref="PreviewKind"/>, plus the Finder-style metadata block underneath
    /// (<c>PreviewMetadataBlock</c>, shown whenever <c>vm.MetadataText</c> is non-empty -
    /// independent of <see cref="PreviewKind"/>, since it applies to every kind alike).
    /// <see cref="PreviewKind.Binary"/> shares the same monospace text box as
    /// <see cref="PreviewKind.Text"/> - its <c>vm.Text</c> is already the label header plus hex
    /// dump, composed in the projection. The only decision made here rather than in the projection
    /// is the <c>ImageBytes</c> -&gt; <c>BitmapImage</c> decode, which is wiring (WPF-specific, not
    /// a display-formatting choice) - cached by <see cref="lastDecodedImageGeneration"/> so it only
    /// runs once per distinct preview, not on every unrelated re-render.
    /// </summary>
    public void RenderPreview(StateProjection.PreviewVm vm)
    {
        ArgumentNullException.ThrowIfNull(vm);

        previewFileName = vm.FileName;
        previewOriginalDirectory = vm.OriginalDirectory;
        FitPreviewHeader();

        PreviewMetadataBlock.Text = vm.MetadataText;
        PreviewMetadataBlock.Visibility =
            string.IsNullOrEmpty(vm.MetadataText) ? Visibility.Collapsed : Visibility.Visible;

        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewTextBox.Visibility = Visibility.Collapsed;
        PreviewMeta.Visibility = Visibility.Collapsed;
        PreviewCapacityPanel.Visibility = Visibility.Collapsed;

        switch (vm.Kind)
        {
            case PreviewKind.Image:
                if (lastDecodedImageGeneration != vm.Generation)
                {
                    PreviewImage.Source = DecodeImage(vm.ImageBytes);
                    lastDecodedImageGeneration = vm.Generation;
                }

                PreviewImage.Visibility = Visibility.Visible;
                break;

            case PreviewKind.Text:
            case PreviewKind.Binary:
                // Binary's vm.Text is already the label header + hex dump (see
                // StateProjection.ProjectPreview) - the same monospace box as Text needs no
                // special-casing to show it.
                PreviewTextBox.Text = vm.Text ?? string.Empty;
                PreviewTextBox.Visibility = Visibility.Visible;
                break;

            case PreviewKind.Capacity when vm.Capacity is { } capacity:
                RenderCapacity(capacity);
                break;

            case PreviewKind.Loading:
                PreviewMeta.Text = "読み込み中…";
                PreviewMeta.Visibility = Visibility.Visible;
                break;

            case PreviewKind.None:
            default:
                if (vm.Error is not null)
                {
                    PreviewMeta.Text = vm.Error;
                    PreviewMeta.Visibility = Visibility.Visible;
                }

                break;
        }

        PreviewRetryText.Text = vm.RetryLink ?? string.Empty;
        PreviewRetry.Visibility = vm.RetryLink is null ? Visibility.Collapsed : Visibility.Visible;
        PreviewGaveUp.Text = vm.GaveUp ?? string.Empty;
        PreviewGaveUp.Visibility = vm.GaveUp is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Decodes already-loaded image bytes (from <see cref="Msg.PreviewLoaded"/>, via
    /// <see cref="Effect.LoadPreview"/> - Runtime's job, not this method's) into a frozen
    /// <see cref="BitmapImage"/> so it is safe to hand to the UI thread's <see cref="Image"/>
    /// control from here regardless of which thread posted the underlying <see cref="Msg"/>.
    /// </summary>
    private static BitmapImage DecodeImage(System.Collections.Immutable.ImmutableArray<byte> bytes)
    {
        using var stream = new MemoryStream([.. bytes]);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = PreviewDecodePixelWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
