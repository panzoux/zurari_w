using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Zurari.Controls;

/// <summary>
/// The just-sized text box Explorer puts over a file name while you rename it.
/// </summary>
/// <remarks>
/// <para>
/// An adorner over the row rather than an editable item template. The list virtualizes with
/// <c>VirtualizationMode="Recycling"</c> and picks its rows with a template selector; an editable
/// template would have to survive both, and a recycled container could carry the editor to an
/// unrelated row. An adorner is attached to the one container being edited and to nothing else.
/// </para>
/// <para>
/// A plain <see cref="TextBox"/> also means IME composition works with no help from us - which
/// matters here more than anywhere else in the app.
/// </para>
/// </remarks>
internal sealed class RenameAdorner : Adorner
{
    private readonly VisualCollection visuals;
    private readonly Border frame;
    private readonly TextBox editor;
    private readonly TextBlock error;
    private readonly StackPanel panel;
    private readonly FrameworkElement anchor;

    /// <summary>Raised when the user accepts the name.</summary>
    public event EventHandler<string>? Submitted;

    /// <summary>Raised when the user gives up - Escape, or focus going elsewhere.</summary>
    public event EventHandler? Cancelled;

    public RenameAdorner(FrameworkElement adornedElement, string initialName, string? initialError)
        : base(adornedElement)
    {
        anchor = adornedElement;
        visuals = new VisualCollection(this);

        editor = new TextBox
        {
            Text = initialName,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x86, 0xE0)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2, 0, 2, 0),
        };

        error = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x20, 0x20)),
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xF4)),
            Padding = new Thickness(3, 1, 3, 1),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        panel = new StackPanel();
        panel.Children.Add(editor);
        panel.Children.Add(error);

        frame = new Border { Child = panel, Background = Brushes.White };
        visuals.Add(frame);

        editor.PreviewKeyDown += OnEditorKeyDown;
        editor.LostKeyboardFocus += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);

        ShowError(initialError);
    }

    /// <summary>
    /// Puts the caret in, selecting the part a rename usually replaces: the name without its
    /// extension, as Explorer does.
    /// </summary>
    public void Focus(bool selectAll)
    {
        editor.Focus();
        Keyboard.Focus(editor);

        var dot = editor.Text.LastIndexOf('.');
        if (selectAll || dot <= 0)
        {
            editor.SelectAll();
        }
        else
        {
            editor.Select(0, dot);
        }
    }

    /// <summary>Shows why the last attempt was refused, keeping the text for the user to fix.</summary>
    public void ShowError(string? message)
    {
        error.Text = message ?? string.Empty;
        error.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        InvalidateMeasure();
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        // While an IME is composing, Enter and Escape belong to the composition, not to us.
        if (e.Key is Key.ImeProcessed or Key.DeadCharProcessed)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Submitted?.Invoke(this, editor.Text);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancelled?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    protected override int VisualChildrenCount => visuals.Count;

    /// <inheritdoc />
    protected override Visual GetVisualChild(int index) => visuals[index];

    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        frame.Measure(new Size(anchor.ActualWidth, double.PositiveInfinity));
        return new Size(anchor.ActualWidth, anchor.ActualHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Sits on the row and grows downwards if there is an error to show, so the row underneath
        // stays where it is and the list does not jump while someone is typing into it.
        frame.Arrange(new Rect(0, 0, finalSize.Width, frame.DesiredSize.Height));
        return finalSize;
    }
}
