using System.ComponentModel;
using Zurari.App;

namespace Zurari.App.Tests;

/// <summary>
/// Regression coverage for Phase 5 bug B1 ("キャンセルボタンが効かない") at the level that is
/// actually testable without a live WPF window: <see cref="JobRowVm"/> must update its bound
/// properties in place - never require the job strip's <c>ItemsSource</c> collection itself to be
/// touched - so a running job's row (and its "キャンセル" button container) survives every
/// <see cref="Zurari.Core.Msg.JobProgress"/>-driven render instead of being torn down and rebuilt
/// roughly every 100ms. See <see cref="MainWindow.RenderJobs"/> for how this is wired up and
/// <see cref="JobRowVm"/>'s remarks for the full root-cause explanation.
/// </summary>
public class JobRowVmTests
{
    private static StateProjection.JobVm MakeVm(
        int jobId = 1,
        string description = "コピー: dest へ (1件)",
        string detail = "待機中",
        double progressFraction = 0,
        bool isIndeterminate = false,
        bool isRunning = true,
        bool isFinished = false,
        string statusLabel = "待機中") =>
        new(jobId, description, detail, progressFraction, isIndeterminate, isRunning, isFinished, statusLabel);

    [Fact]
    public void JobId_is_taken_from_the_constructor_and_never_changes()
    {
        var row = new JobRowVm(MakeVm(jobId: 42));

        row.UpdateFrom(MakeVm(jobId: 42, detail: "実行中"));

        Assert.Equal(42, row.JobId);
    }

    [Fact]
    public void UpdateFrom_copies_every_display_field()
    {
        var row = new JobRowVm(MakeVm());

        row.UpdateFrom(MakeVm(
            description: "移動: other へ (3件)",
            detail: "a.txt — 1/3 (1.0 KB/3.0 KB)",
            progressFraction: 0.33,
            isIndeterminate: true,
            isRunning: true,
            isFinished: false,
            statusLabel: "実行中"));

        Assert.Equal("移動: other へ (3件)", row.Description);
        Assert.Equal("a.txt — 1/3 (1.0 KB/3.0 KB)", row.Detail);
        Assert.Equal(0.33, row.ProgressFraction);
        Assert.True(row.IsIndeterminate);
        Assert.True(row.IsRunning);
        Assert.False(row.IsFinished);
        Assert.Equal("実行中", row.StatusLabel);
    }

    [Fact]
    public void UpdateFrom_raises_PropertyChanged_only_for_fields_that_actually_changed()
    {
        var row = new JobRowVm(MakeVm(detail: "待機中", progressFraction: 0));
        var changed = new List<string>();
        ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        // Same Detail/ProgressFraction as construction, only Description differs - simulates a
        // render where this job's own progress has not moved but something else in the render
        // pass touched the projection (e.g. re-running StateProjection.ProjectJobs for an
        // unrelated reason).
        row.UpdateFrom(MakeVm(description: "コピー: dest2 へ (1件)", detail: "待機中", progressFraction: 0));

        Assert.Equal(["Description"], changed);
    }

    [Fact]
    public void UpdateFrom_on_an_unrelated_progress_tick_does_not_touch_the_instance_identity()
    {
        // The core guarantee this class exists for: the SAME JobRowVm instance (and therefore the
        // SAME generated WPF container/button) is reused across a whole run of progress ticks -
        // nothing here ever constructs a replacement.
        var row = new JobRowVm(MakeVm(detail: "0/10", progressFraction: 0));
        var sameInstance = row;

        for (var doneFiles = 1; doneFiles <= 10; doneFiles++)
        {
            row.UpdateFrom(MakeVm(detail: $"{doneFiles}/10", progressFraction: doneFiles / 10.0));
        }

        Assert.Same(sameInstance, row);
        Assert.Equal("10/10", row.Detail);
        Assert.Equal(1.0, row.ProgressFraction);
    }

    [Fact]
    public void Constructor_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => new JobRowVm(null!));
    }

    [Fact]
    public void UpdateFrom_rejects_null()
    {
        var row = new JobRowVm(MakeVm());

        Assert.Throws<ArgumentNullException>(() => row.UpdateFrom(null!));
    }
}
