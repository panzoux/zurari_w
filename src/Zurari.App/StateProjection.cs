using System.Collections.Immutable;
using System.Globalization;
using System.Windows.Media;
using Zurari.Core;

namespace Zurari.App;

/// <summary>
/// Pure mapping from <see cref="AppState"/> (Core) to the view-model shapes
/// <see cref="Zurari.Controls.ColumnBrowser"/> understands. No I/O, no decisions beyond display
/// formatting — everything here is a straight projection of already-decided state.
/// </summary>
public static class StateProjection
{
    private static readonly string[] SizeUnits = ["KB", "MB", "GB", "TB", "PB"];

    /// <summary>
    /// Projects every column of <paramref name="state"/> into a <see cref="Controls.ColumnVm"/>.
    /// <paramref name="iconResolver"/> is optional so callers without a shell (tests, headless
    /// runs) can omit it — entries then simply project with a <c>null</c> icon.
    /// </summary>
    public static IReadOnlyList<Controls.ColumnVm> Project(
        AppState state,
        Func<Entry, ImageSource?>? iconResolver = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Built once per Project call (not per column/entry) - CutPending is a flat list of full
        // paths shared across every column, so a HashSet lookup here is what keeps the per-entry
        // membership check cheap instead of an O(entries * CutPending) scan.
        var cutPending = state.CutPending.IsDefaultOrEmpty
            ? null
            : state.CutPending.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new Controls.ColumnVm[state.Columns.Length];
        for (var i = 0; i < state.Columns.Length; i++)
        {
            result[i] = ProjectColumn(state.Columns[i], isFocused: i == state.FocusedColumn, iconResolver, cutPending);
        }

        return result;
    }

    private static Controls.ColumnVm ProjectColumn(
        Column column, bool isFocused, Func<Entry, ImageSource?>? iconResolver, HashSet<string>? cutPending)
    {
        var entries = new Controls.EntryVm[column.Entries.Length];
        for (var i = 0; i < column.Entries.Length; i++)
        {
            entries[i] = ProjectEntry(column.Entries[i], iconResolver, column.Location, cutPending);
        }

        return new Controls.ColumnVm(
            Title: ProjectTitle(column),
            Entries: entries,
            CursorIndex: column.Cursor,
            IsFocused: isFocused);
    }

    private static string ProjectTitle(Column column)
    {
        var baseTitle = column.Location switch
        {
            Location.Drives => "ドライブ",
            Location.RealDirectory directory => LastPathSegment(directory.Path),
            var other => other.FilesystemPath is { } path ? LastPathSegment(path) : string.Empty,
        };

        return column.Load switch
        {
            LoadState.Error => baseTitle + " (エラー)",
            LoadState.Loading when column.Entries.Length == 0 => baseTitle + " …",
            _ => baseTitle,
        };
    }

    private static string LastPathSegment(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length == 0)
        {
            // A bare drive root like "C:\" trims to "C:".
            return path.TrimEnd('\\', '/');
        }

        var separatorIndex = trimmed.LastIndexOfAny(['\\', '/']);
        return separatorIndex < 0 ? trimmed : trimmed[(separatorIndex + 1)..];
    }

    private static Controls.EntryVm ProjectEntry(
        Entry entry, Func<Entry, ImageSource?>? iconResolver, Location columnLocation, HashSet<string>? cutPending)
    {
        var fullPath = entry.Target is { } target
            ? target.FilesystemPath
            : columnLocation.ChildPath(entry.Name);
        return new(
            Name: entry.Name,
            Kind: ProjectKind(entry.Kind),
            IsMarked: entry.IsMarked,
            SizeText: ProjectSize(entry),
            DateText: ProjectDate(entry.Modified),
            Icon: iconResolver?.Invoke(entry),
            IsCut: fullPath is not null && (cutPending?.Contains(fullPath) ?? false));
    }

    private static Controls.EntryKind ProjectKind(EntryKind kind) => kind switch
    {
        EntryKind.Drive => Controls.EntryKind.Drive,
        EntryKind.Directory => Controls.EntryKind.Directory,
        _ => Controls.EntryKind.File,
    };

    private static string? ProjectSize(Entry entry)
    {
        if (entry.Kind != EntryKind.File || entry.SizeBytes < 0)
        {
            return null;
        }

        return FormatBytes(entry.SizeBytes);
    }

    /// <summary>
    /// Human-readable byte count ("512 B", "1.0 KB", "1.0 MB", ...), shared by file-size display
    /// (<see cref="ProjectSize"/>) and job progress display (<see cref="ProjectJobs"/>).
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        double size = bytes;
        var unitIndex = -1;
        while (size >= 1024 && unitIndex < SizeUnits.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return size.ToString("F1", CultureInfo.InvariantCulture) + " " + SizeUnits[unitIndex];
    }

    private static string? ProjectDate(DateTime modified) =>
        modified == default ? null : modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// View model for a single row in the job strip (see <see cref="ProjectJobs"/>).
    /// </summary>
    /// <param name="JobId">Identifies the underlying <see cref="Job"/>, for dispatching
    /// <see cref="Msg.JobCancelRequested"/> / <see cref="Msg.JobDismissed"/> from the UI.</param>
    /// <param name="Description">Short summary of what the job is doing, e.g. "コピー: dest へ (3件)".</param>
    /// <param name="Detail">Secondary line: current file + progress, or a status message.</param>
    /// <param name="ProgressFraction">0..1 byte-based completion ratio (0 when unknown).</param>
    /// <param name="IsIndeterminate">True while running but the total size is not yet known.</param>
    /// <param name="IsRunning">True while Queued or Running (cancel button should show).</param>
    /// <param name="IsFinished">True once Completed/Failed/Cancelled (dismiss button should show).</param>
    /// <param name="StatusLabel">Short status word for the row (待機中/実行中/完了/失敗/キャンセル済み/確認中).</param>
    /// <param name="IsWaitingConflict">
    /// True while <see cref="JobStatus.WaitingConflict"/> - the row should show the 上書き/スキップ/
    /// 中止 conflict-resolution buttons instead of (or alongside) the ordinary cancel button.
    /// </param>
    public sealed record JobVm(
        int JobId,
        string Description,
        string Detail,
        double ProgressFraction,
        bool IsIndeterminate,
        bool IsRunning,
        bool IsFinished,
        string StatusLabel,
        bool IsWaitingConflict = false);

    /// <summary>
    /// Projects every job in <paramref name="state"/> into a <see cref="JobVm"/>, in the order they
    /// appear in <see cref="AppState.Jobs"/>. Pure - no I/O, no decisions beyond display formatting.
    /// </summary>
    public static IReadOnlyList<JobVm> ProjectJobs(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var result = new JobVm[state.Jobs.Length];
        for (var i = 0; i < state.Jobs.Length; i++)
        {
            result[i] = ProjectJob(state.Jobs[i]);
        }

        return result;
    }

    private static JobVm ProjectJob(Job job)
    {
        var kindLabel = job.Kind == JobKind.Copy ? "コピー" : "移動";
        var destName = LastPathSegment(job.DestDir);
        var count = job.TotalFiles > 0 ? job.TotalFiles : job.Sources.Length;
        var description = $"{kindLabel}: {destName} へ ({count}件)";

        var (detail, statusLabel) = job.Status switch
        {
            JobStatus.Queued => ("待機中", "待機中"),
            JobStatus.Running => (
                $"{job.CurrentFile} — {job.DoneFiles}/{job.TotalFiles} "
                + $"({FormatBytes(job.DoneBytes)}/{FormatBytes(job.TotalBytes)})",
                "実行中"),
            JobStatus.Completed => (
                job.SkippedFiles > 0 ? $"完了 (スキップ {job.SkippedFiles}件)" : "完了",
                "完了"),
            JobStatus.Failed => ($"失敗: {job.Error}", "失敗"),
            JobStatus.Cancelled => ("キャンセル済み", "キャンセル済み"),
            JobStatus.WaitingConflict => ($"同名のファイルが {job.ConflictCount} 件あります", "確認中"),
            _ => (string.Empty, string.Empty),
        };

        var progressFraction = job.TotalBytes > 0 ? job.DoneBytes / (double)job.TotalBytes : 0;
        var isIndeterminate = job.Status == JobStatus.Running && job.TotalBytes == 0;
        var isRunning = job.Status is JobStatus.Queued or JobStatus.Running;
        var isFinished = job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled;
        var isWaitingConflict = job.Status == JobStatus.WaitingConflict;

        return new JobVm(
            job.JobId, description, detail, progressFraction, isIndeterminate, isRunning, isFinished, statusLabel,
            isWaitingConflict);
    }

    /// <summary>
    /// View model for the preview pane (see <see cref="ProjectPreview"/>). Carries the raw
    /// <see cref="AppState.Preview"/> data straight through - <see cref="Zurari.App.MainWindow"/>
    /// is the one that decides how to render each <see cref="Kind"/> (Image bytes -&gt;
    /// <c>BitmapImage</c> is wiring, not projection logic).
    /// </summary>
    /// <param name="Generation">
    /// Mirrors <see cref="PreviewState.Generation"/> so the renderer can cache the last
    /// <c>BitmapImage</c> it decoded and skip re-decoding on an unrelated re-render.
    /// </param>
    /// <param name="FileName">Last path segment of <see cref="PreviewState.Path"/>, or <c>null</c> when <see cref="Kind"/> is <see cref="PreviewKind.None"/>.</param>
    /// <param name="Kind">What to render.</param>
    /// <param name="Text">
    /// Decoded text body when <see cref="Kind"/> is <see cref="PreviewKind.Text"/>. When
    /// <see cref="Kind"/> is <see cref="PreviewKind.Binary"/>, this is the type label from
    /// <see cref="PreviewState.Text"/> followed by a <see cref="HexDump.Format"/> of
    /// <see cref="ImageBytes"/> - so the App layer can show both in the same monospace box it
    /// already uses for <see cref="PreviewKind.Text"/>, no separate widget needed.
    /// </param>
    /// <param name="ImageBytes">Whole-file bytes when <see cref="Kind"/> is <see cref="PreviewKind.Image"/>.</param>
    /// <param name="Error">Set when the most recent load for this generation failed.</param>
    /// <param name="MetadataText">
    /// Finder-inspector-style metadata block (file name, size, dates, and - for images - pixel
    /// resolution and bit depth), one line per fact, already formatted for display - see
    /// <see cref="FormatMetadata"/>. Empty string when <see cref="PreviewState.Metadata"/> is
    /// <c>null</c> (nothing loaded yet, or the load failed before any metadata arrived).
    /// </param>
    public sealed record PreviewVm(
        int Generation,
        string? FileName,
        PreviewKind Kind,
        string? Text,
        ImmutableArray<byte> ImageBytes,
        string? Error,
        string MetadataText);

    /// <summary>
    /// Projects <see cref="AppState.Preview"/> into a <see cref="PreviewVm"/>. Pure - no I/O, no
    /// decisions beyond display formatting (the file name, the metadata block).
    /// </summary>
    public static PreviewVm ProjectPreview(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var preview = state.Preview;
        var fileName = preview.Path is null ? null : LastPathSegment(preview.Path);
        var text = preview.Kind == PreviewKind.Binary ? FormatBinaryText(preview) : preview.Text;
        var metadataText = FormatMetadata(preview);
        return new PreviewVm(preview.Generation, fileName, preview.Kind, text, preview.ImageBytes, preview.Error, metadataText);
    }

    /// <summary>
    /// Builds the metadata-block lines (see <see cref="PreviewVm.MetadataText"/>), Finder-inspector
    /// style: file name, a kind label, human-readable size (plus the exact byte count),
    /// created/modified timestamps, and - only when <see cref="PreviewMetadata.PixelWidth"/>/
    /// <see cref="PreviewMetadata.PixelHeight"/>/<see cref="PreviewMetadata.BitsPerPixel"/> are
    /// present - resolution and bit depth. Empty string when there is no metadata to show.
    /// </summary>
    private static string FormatMetadata(PreviewState preview)
    {
        var metadata = preview.Metadata;
        if (metadata is null)
        {
            return string.Empty;
        }

        var lines = new List<string>
        {
            $"ファイル名: {metadata.FileName}",
            $"種類: {PreviewKindLabel(preview)}",
            $"サイズ: {FormatBytes(metadata.SizeBytes)} ({metadata.SizeBytes.ToString("N0", CultureInfo.InvariantCulture)} バイト)",
            $"作成日時: {metadata.Created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}",
            $"更新日時: {metadata.Modified.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}",
        };

        if (metadata.PixelWidth is int width && metadata.PixelHeight is int height)
        {
            lines.Add($"解像度: {width} × {height}");
        }

        if (metadata.BitsPerPixel is int bitsPerPixel)
        {
            lines.Add($"色深度: {bitsPerPixel} bit");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Short "種類" (kind) label for the metadata block. Binary reuses the
    /// <see cref="FileTypeDetector"/> label already carried in <see cref="PreviewState.Text"/> for
    /// that kind (e.g. "PE Executable") since it is more informative than a generic word; Image
    /// and Text kinds get a plain Japanese label since there is no equivalent per-kind detail to
    /// surface for them.
    /// </summary>
    private static string PreviewKindLabel(PreviewState preview) => preview.Kind switch
    {
        PreviewKind.Image => "画像ファイル",
        PreviewKind.Text => "テキストファイル",
        PreviewKind.Binary => preview.Text ?? "バイナリファイル",
        _ => "",
    };

    /// <summary>
    /// Builds the Binary-kind display text: the type label (<see cref="PreviewState.Text"/>) as a
    /// header line, then a blank line, then a <see cref="HexDump.Format"/> of the carried head
    /// bytes - or just the label alone when there are no bytes to dump (e.g. a
    /// <see cref="Msg.PreviewFailed"/> raced in before any bytes arrived).
    /// </summary>
    private static string FormatBinaryText(PreviewState preview)
    {
        var label = preview.Text ?? "バイナリファイル";
        if (preview.ImageBytes.IsEmpty)
        {
            return label;
        }

        return label + "\n\n" + HexDump.Format(preview.ImageBytes.AsSpan());
    }
}
