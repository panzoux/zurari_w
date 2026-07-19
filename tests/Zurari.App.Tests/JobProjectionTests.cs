using System.Collections.Immutable;
using Zurari.App;
using Zurari.Core;

namespace Zurari.App.Tests;

public class JobProjectionTests
{
    private static AppState StateWithJobs(params Job[] jobs) =>
        new()
        {
            Columns = [new Column("", [], Load: LoadState.Loaded)],
            FocusedColumn = 0,
            Jobs = [.. jobs],
        };

    private static Job MakeJob(
        JobKind kind = JobKind.Copy,
        string destDir = @"C:\Users\someone\dest",
        JobStatus status = JobStatus.Queued,
        int doneFiles = 0,
        int totalFiles = 0,
        long doneBytes = 0,
        long totalBytes = 0,
        string? currentFile = null,
        string? error = null,
        int skippedFiles = 0) =>
        new(
            JobId: 1,
            Kind: kind,
            Sources: [@"C:\src\a.txt", @"C:\src\b.txt"],
            DestDir: destDir,
            Status: status,
            DoneFiles: doneFiles,
            TotalFiles: totalFiles,
            DoneBytes: doneBytes,
            TotalBytes: totalBytes,
            CurrentFile: currentFile,
            Error: error,
            SkippedFiles: skippedFiles);

    [Fact]
    public void ProjectJobs_rejects_null_state()
    {
        Assert.Throws<ArgumentNullException>(() => StateProjection.ProjectJobs(null!));
    }

    [Fact]
    public void No_jobs_projects_to_empty_list()
    {
        var state = StateWithJobs();

        var vms = StateProjection.ProjectJobs(state);

        Assert.Empty(vms);
    }

    [Fact]
    public void Copy_description_uses_dest_dir_last_segment_and_total_files()
    {
        var job = MakeJob(kind: JobKind.Copy, destDir: @"C:\Users\someone\dest", totalFiles: 5);
        var state = StateWithJobs(job);

        var vms = StateProjection.ProjectJobs(state);

        Assert.Equal("コピー: dest へ (5件)", vms[0].Description);
    }

    [Fact]
    public void Move_description_uses_move_label()
    {
        var job = MakeJob(kind: JobKind.Move, destDir: @"C:\Users\someone\dest", totalFiles: 3);
        var state = StateWithJobs(job);

        var vms = StateProjection.ProjectJobs(state);

        Assert.Equal("移動: dest へ (3件)", vms[0].Description);
    }

    [Fact]
    public void Description_falls_back_to_source_count_when_total_files_unknown()
    {
        var job = MakeJob(totalFiles: 0);
        var state = StateWithJobs(job);

        var vms = StateProjection.ProjectJobs(state);

        Assert.Equal("コピー: dest へ (2件)", vms[0].Description);
    }

    [Fact]
    public void Queued_status_shows_waiting_detail()
    {
        var job = MakeJob(status: JobStatus.Queued);
        var state = StateWithJobs(job);

        var vms = StateProjection.ProjectJobs(state);

        Assert.Equal("待機中", vms[0].Detail);
        Assert.Equal("待機中", vms[0].StatusLabel);
        Assert.True(vms[0].IsRunning);
        Assert.False(vms[0].IsFinished);
    }

    [Fact]
    public void Running_status_formats_current_file_and_byte_progress()
    {
        var job = MakeJob(
            status: JobStatus.Running,
            currentFile: "a.txt",
            doneFiles: 1,
            totalFiles: 4,
            doneBytes: 1024,
            totalBytes: 1024 * 1024);
        var state = StateWithJobs(job);

        var vms = StateProjection.ProjectJobs(state);

        Assert.Equal("a.txt — 1/4 (1.0 KB/1.0 MB)", vms[0].Detail);
        Assert.Equal("実行中", vms[0].StatusLabel);
        Assert.True(vms[0].IsRunning);
        Assert.False(vms[0].IsFinished);
    }

    [Fact]
    public void Completed_status_without_skips_shows_plain_done_detail()
    {
        var job = MakeJob(status: JobStatus.Completed, skippedFiles: 0);
        var state = StateWithJobs(job);

        var vms = StateProjection.ProjectJobs(state);

        Assert.Equal("完了", vms[0].Detail);
        Assert.False(vms[0].IsRunning);
        Assert.True(vms[0].IsFinished);
    }

    [Fact]
    public void Completed_status_with_skips_appends_skip_count()
    {
        var job = MakeJob(status: JobStatus.Completed, skippedFiles: 2);
        var state = StateWithJobs(job);

        var vms = StateProjection.ProjectJobs(state);

        Assert.Equal("完了 (スキップ 2件)", vms[0].Detail);
    }

    [Fact]
    public void Failed_status_shows_error_message()
    {
        var job = MakeJob(status: JobStatus.Failed, error: "アクセスが拒否されました");
        var state = StateWithJobs(job);

        var vms = StateProjection.ProjectJobs(state);

        Assert.Equal("失敗: アクセスが拒否されました", vms[0].Detail);
        Assert.Equal("失敗", vms[0].StatusLabel);
        Assert.True(vms[0].IsFinished);
    }

    [Fact]
    public void Cancelled_status_shows_cancelled_detail()
    {
        var job = MakeJob(status: JobStatus.Cancelled);
        var state = StateWithJobs(job);

        var vms = StateProjection.ProjectJobs(state);

        Assert.Equal("キャンセル済み", vms[0].Detail);
        Assert.Equal("キャンセル済み", vms[0].StatusLabel);
        Assert.True(vms[0].IsFinished);
    }

    [Fact]
    public void Progress_fraction_is_done_over_total_bytes()
    {
        var job = MakeJob(status: JobStatus.Running, doneBytes: 250, totalBytes: 1000);
        var state = StateWithJobs(job);

        var vms = StateProjection.ProjectJobs(state);

        Assert.Equal(0.25, vms[0].ProgressFraction);
    }

    [Fact]
    public void Zero_total_bytes_while_running_is_indeterminate_with_zero_fraction()
    {
        var job = MakeJob(status: JobStatus.Running, doneBytes: 0, totalBytes: 0);
        var state = StateWithJobs(job);

        var vms = StateProjection.ProjectJobs(state);

        Assert.Equal(0, vms[0].ProgressFraction);
        Assert.True(vms[0].IsIndeterminate);
    }

    [Fact]
    public void Zero_total_bytes_while_queued_is_not_indeterminate()
    {
        var job = MakeJob(status: JobStatus.Queued, totalBytes: 0);
        var state = StateWithJobs(job);

        var vms = StateProjection.ProjectJobs(state);

        Assert.False(vms[0].IsIndeterminate);
    }

    [Fact]
    public void JobId_is_passed_through()
    {
        var job = MakeJob() with { JobId = 42 };
        var state = StateWithJobs(job);

        var vms = StateProjection.ProjectJobs(state);

        Assert.Equal(42, vms[0].JobId);
    }
}
