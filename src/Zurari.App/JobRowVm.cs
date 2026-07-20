using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Zurari.App;

/// <summary>
/// Mutable, <see cref="INotifyPropertyChanged"/>-backed wrapper around a
/// <see cref="StateProjection.JobVm"/> for the job strip's <c>ItemsSource</c>. An existing instance
/// is updated in place field-by-field (<see cref="UpdateFrom"/>) rather than the whole
/// <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/> being rebuilt from scratch on
/// every render - see <see cref="MainWindow.RenderJobs"/>, which is the only place that constructs
/// or mutates instances of this class.
/// </summary>
/// <remarks>
/// This exists to fix a reported bug (Phase 5 "B1: キャンセルボタンが効かない"): the job strip used
/// to bind directly to a brand-new list of immutable <see cref="StateProjection.JobVm"/> records on
/// every render, including every <see cref="Zurari.Core.Msg.JobProgress"/> tick (about every 100ms
/// while a job is running - see <c>JobEngine.ProgressThrottle</c>). Reassigning an
/// <see cref="System.Windows.Controls.ItemsControl.ItemsSource"/> to a new collection reference
/// makes WPF tear down and regenerate that item's containers (the row's "キャンセル" <see cref="System.Windows.Controls.Button"/>
/// included). A real mouse click on that button spans a MouseDown-then-MouseUp gesture that can
/// straddle two such regenerations (they happen roughly 10 times/second while a job runs): the
/// button instance that received the MouseDown - and WPF's mouse capture on it - can be gone by the
/// time MouseUp would have raised <c>Click</c>, silently swallowing the click. Keeping the same
/// <see cref="JobRowVm"/> instance (and therefore the same container) alive across progress ticks,
/// and only touching its bound properties via <see cref="PropertyChanged"/>, means the button the
/// user is clicking is never pulled out from under the gesture.
/// </remarks>
public sealed class JobRowVm : INotifyPropertyChanged
{
    private string description = string.Empty;
    private string detail = string.Empty;
    private double progressFraction;
    private bool isIndeterminate;
    private bool isRunning;
    private bool isFinished;
    private string statusLabel = string.Empty;
    private bool isWaitingConflict;

    public JobRowVm(StateProjection.JobVm vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        JobId = vm.JobId;
        UpdateFrom(vm);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stable identity: never changes for the lifetime of this instance.</summary>
    public int JobId { get; }

    public string Description
    {
        get => description;
        private set => SetField(ref description, value);
    }

    public string Detail
    {
        get => detail;
        private set => SetField(ref detail, value);
    }

    public double ProgressFraction
    {
        get => progressFraction;
        private set => SetField(ref progressFraction, value);
    }

    public bool IsIndeterminate
    {
        get => isIndeterminate;
        private set => SetField(ref isIndeterminate, value);
    }

    public bool IsRunning
    {
        get => isRunning;
        private set => SetField(ref isRunning, value);
    }

    public bool IsFinished
    {
        get => isFinished;
        private set => SetField(ref isFinished, value);
    }

    public string StatusLabel
    {
        get => statusLabel;
        private set => SetField(ref statusLabel, value);
    }

    /// <summary>True while the underlying job is <see cref="Zurari.Core.JobStatus.WaitingConflict"/>
    /// - the row's 上書き/スキップ/中止 buttons should show only then.</summary>
    public bool IsWaitingConflict
    {
        get => isWaitingConflict;
        private set => SetField(ref isWaitingConflict, value);
    }

    /// <summary>
    /// Copies every display field from <paramref name="vm"/> onto this instance (the caller
    /// guarantees <paramref name="vm"/>.JobId matches <see cref="JobId"/>; not checked here),
    /// raising <see cref="PropertyChanged"/> only for fields that actually changed. Called on every
    /// render instead of replacing the row - that in-place update, not the row's data, is the whole
    /// point of this class; see the remarks above.
    /// </summary>
    public void UpdateFrom(StateProjection.JobVm vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        Description = vm.Description;
        Detail = vm.Detail;
        ProgressFraction = vm.ProgressFraction;
        IsIndeterminate = vm.IsIndeterminate;
        IsRunning = vm.IsRunning;
        IsFinished = vm.IsFinished;
        StatusLabel = vm.StatusLabel;
        IsWaitingConflict = vm.IsWaitingConflict;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
