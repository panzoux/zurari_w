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
        Func<Entry, ImageSource?>? iconResolver = null,
        ProjectionCache? cache = null)
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
            result[i] = ProjectColumn(
                state.Columns[i], isFocused: i == state.FocusedColumn, iconResolver, cutPending, cache, i, state.CutPending);
        }

        cache?.Trim(state.Columns.Length);

        return result;
    }

    private static Controls.ColumnVm ProjectColumn(
        Column column,
        bool isFocused,
        Func<Entry, ImageSource?>? iconResolver,
        HashSet<string>? cutPending,
        ProjectionCache? cache,
        int columnIndex,
        ImmutableArray<string> cutPendingKey)
    {
        // Reusing the array instance is the whole point - see ProjectionCache. The ColumnVm around
        // it is still rebuilt (its Title and CursorIndex do change), but that is one small object.
        var entries = cache?.TryReuse(columnIndex, column, cutPendingKey);
        if (entries is null)
        {
            entries = new Controls.EntryVm[column.Entries.Length];
            for (var i = 0; i < column.Entries.Length; i++)
            {
                entries[i] = ProjectEntry(
                    column.Entries[i], iconResolver, column.Location, cutPending, column.CollapsedGroups);
            }

            cache?.Store(columnIndex, column, cutPendingKey, entries);
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
            Location.Drives => DrivesLabel,
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

    /// <summary>Everything before the last segment of <paramref name="path"/>, without its trailing separator.</summary>
    private static string ParentPath(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var separatorIndex = trimmed.LastIndexOfAny(['\\', '/']);
        return separatorIndex <= 0 ? string.Empty : trimmed[..separatorIndex];
    }

    private static Controls.EntryVm ProjectEntry(
        Entry entry,
        Func<Entry, ImageSource?>? iconResolver,
        Location columnLocation,
        HashSet<string>? cutPending,
        ImmutableHashSet<string> collapsedGroups)
    {
        // A header is a label, not a place: no path, no icon, no size or date column. It does carry
        // whether its section is collapsed, which is what its hover control reads.
        if (entry.Kind == EntryKind.Header)
        {
            return new(
                Name: entry.Label,
                Kind: Controls.EntryKind.Header,
                IsMarked: false,
                SizeText: null,
                DateText: null,
                IsSectionCollapsed: entry.Group is { } group && collapsedGroups.Contains(group));
        }

        var fullPath = entry.Target is { } target
            ? target.FilesystemPath
            : columnLocation.ChildPath(entry.Name);
        return new(
            // What the user reads. Differs from Entry.Name in the drive pane, where the name is the
            // target path so that two favorites called the same thing stay distinguishable.
            Name: entry.Label,
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
        EntryKind.Header => Controls.EntryKind.Header,
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
    /// <param name="FileName">
    /// The name to show for the previewed file, or <c>null</c> when <see cref="Kind"/> is
    /// <see cref="PreviewKind.None"/>. Normally the last segment of <see cref="PreviewState.Path"/>;
    /// for a file in the recycle bin, the name it had before it was deleted, since the bin's own
    /// storage name (<c>$R00L0W8.txt</c>) identifies nothing.
    /// </param>
    /// <param name="OriginalDirectory">
    /// The folder a deleted file came out of, or <c>null</c> for anything not in the recycle bin.
    /// Shown above the name: in a bin of hundreds it is often the only thing telling two identically
    /// named files apart.
    /// </param>
    /// <param name="Kind">What to render.</param>
    /// <param name="Text">
    /// Decoded text body when <see cref="Kind"/> is <see cref="PreviewKind.Text"/>. When
    /// <see cref="Kind"/> is <see cref="PreviewKind.Binary"/>, this is the type label from
    /// <see cref="PreviewState.Text"/> followed by a <see cref="HexDump.Format"/> of
    /// <see cref="ImageBytes"/> - so the App layer can show both in the same monospace box it
    /// already uses for <see cref="PreviewKind.Text"/>, no separate widget needed.
    /// </param>
    /// <param name="ImageBytes">
    /// Whole-file bytes when <see cref="Kind"/> is <see cref="PreviewKind.Image"/> - or a thumbnail
    /// standing in for a video or document.
    /// </param>
    /// <param name="Error">Set when the most recent load for this generation failed.</param>
    /// <param name="MetadataText">
    /// Finder-inspector-style metadata block (file name, size, dates, and - for images - pixel
    /// resolution and bit depth), one line per fact, already formatted for display - see
    /// <see cref="FormatMetadata"/>. Empty string when <see cref="PreviewState.Metadata"/> is
    /// <c>null</c> (nothing loaded yet, or the load failed before any metadata arrived).
    /// </param>
    /// <summary>
    /// The capacity panel's numbers, already formatted: how full a volume is, or how much is in the
    /// recycle bin.
    /// </summary>
    /// <param name="UsedFraction">
    /// How full, 0 to 1, for sizing the bar. <c>null</c> when there is no capacity to be a fraction
    /// of - the bin has a size but no limit - and the bar is then not drawn at all.
    /// </param>
    /// <param name="Free">What is left, or <c>null</c> when there is no total to subtract from.</param>
    /// <param name="Summary">
    /// The one line above the bar: <c>"412.7 GB / 931.5 GB 使用中"</c>, or the reason there are no
    /// figures - <c>"準備できていません"</c> for a drive with nothing in it.
    /// </param>
    public sealed record CapacityVm(
        double? UsedFraction,
        string? Free,
        string Summary);

    public sealed record PreviewVm(
        int Generation,
        string? FileName,
        string? OriginalDirectory,
        CapacityVm? Capacity,
        PreviewKind Kind,
        string? Text,
        ImmutableArray<byte> ImageBytes,
        string? Error,
        string MetadataText)
    {
        /// <summary>
        /// Text of the link that tries a timed-out preview again, naming how long the next try will
        /// wait. <c>null</c> when there is nothing to retry.
        /// </summary>
        public string? RetryLink { get; init; }

        /// <summary>Said instead of the link once the last try has timed out too. <c>null</c> otherwise.</summary>
        public string? GaveUp { get; init; }
    }

    /// <summary>
    /// Projects <see cref="AppState.Preview"/> into a <see cref="PreviewVm"/>. Pure - no I/O, no
    /// decisions beyond display formatting (the file name, the metadata block).
    /// </summary>
    public static PreviewVm ProjectPreview(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var preview = state.Preview;

        // A deleted file is named and placed by where it came from, not by where the bin keeps it.
        var displayPath = preview.OriginalPath ?? preview.Path;
        var fileName = displayPath is null ? null : LastPathSegment(displayPath);
        var originalDirectory = preview.OriginalPath is null ? null : ParentPath(preview.OriginalPath);
        var text = preview.Kind switch
        {
            PreviewKind.Binary => FormatBinaryText(preview),

            // An image's Text is a type label for the metadata block, never a body to show.
            PreviewKind.Image => null,
            _ => preview.Text,
        };
        var metadataText = FormatMetadata(preview);
        return new PreviewVm(
            preview.Generation,
            fileName,
            originalDirectory,
            ProjectCapacity(preview.Capacity),
            preview.Kind,
            text,
            preview.ImageBytes,
            preview.Error,
            metadataText)
        {
            // A timeout is the one failure worth another try, waiting longer; once the last try has
            // timed out too, the pane says so rather than offering a link that would do nothing.
            RetryLink = preview.CanRetry
                ? $"再試行 ({(int)PreviewState.WaitFor(preview.Attempt + 1).TotalSeconds} 秒)"
                : null,
            GaveUp = preview.TimedOut && !preview.CanRetry
                ? $"{(int)PreviewState.WaitFor(preview.Attempt).TotalSeconds} 秒待っても作れませんでした"
                : null,
        };
    }

    /// <summary>Formats a <see cref="PreviewCapacity"/> for display, or <c>null</c> when there is none.</summary>
    private static CapacityVm? ProjectCapacity(PreviewCapacity? capacity)
    {
        if (capacity is null)
        {
            return null;
        }

        var summary = (capacity.UsedBytes, capacity.TotalBytes) switch
        {
            (long used, long total) => $"{FormatBytes(used)} / {FormatBytes(total)} 使用中",
            (long used, null) => FormatBytes(used),
            _ => capacity.Status ?? "サイズ不明",
        };

        return new CapacityVm(
            capacity.UsedFraction,
            capacity.FreeBytes is { } free ? FormatBytes(free) : null,
            summary);
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
        if (preview.Capacity is { } capacity)
        {
            return FormatCapacityMetadata(capacity);
        }

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
    /// The metadata block for a volume or the bin: what it is, then the figures behind the bar.
    /// </summary>
    private static string FormatCapacityMetadata(PreviewCapacity capacity)
    {
        var lines = new List<string>
        {
            $"名前: {capacity.Title}",
            $"種類: {capacity.TypeName}",
        };

        if (capacity.Status is { } status)
        {
            lines.Add($"状態: {status}");
        }

        if (capacity.ItemCount is { } count)
        {
            lines.Add($"項目数: {count.ToString("N0", CultureInfo.InvariantCulture)} 件");
        }

        if (capacity.TotalBytes is { } total)
        {
            lines.Add($"容量: {FormatBytes(total)}");
        }

        if (capacity.UsedBytes is { } used)
        {
            lines.Add($"使用済み: {FormatBytes(used)}");
        }

        if (capacity.FreeBytes is { } free)
        {
            lines.Add($"空き: {FormatBytes(free)}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Short "種類" (kind) label for the metadata block. Binary reuses the
    /// <see cref="FileTypeDetector"/> label already carried in <see cref="PreviewState.Text"/> for
    /// that kind (e.g. "PE Executable") since it is more informative than a generic word. Image does
    /// the same when the picture is a thumbnail of something else ("PDF Document"), and says
    /// 画像ファイル for an actual image; Text gets a plain Japanese label.
    /// </summary>
    private static string PreviewKindLabel(PreviewState preview) => preview.Kind switch
    {
        // A thumbnail standing in for a video or a document carries that file's own type.
        PreviewKind.Image => preview.Text ?? "画像ファイル",
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

    /// <summary>
    /// The path bar and status bar: the full path of what the cursor is on, key hints for the mode
    /// the app is in, and the counts that used to share one line with the path.
    /// </summary>
    /// <param name="Path">Shown in the path bar, above the status bar.</param>
    /// <param name="Hints">Keys worth knowing right now. Empty when there are none worth showing.</param>
    /// <param name="Summary">Entry count, marks and any notice.</param>
    public sealed record StatusBarVm(string Path, string Hints, string Summary);

    /// <summary>Projects the path bar and status bar. Pure - see <see cref="StatusBarVm"/>.</summary>
    public static StatusBarVm ProjectStatusBar(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.FocusedColumn < 0 || state.FocusedColumn >= state.Columns.Length)
        {
            return new StatusBarVm(string.Empty, string.Empty, string.Empty);
        }

        var column = state.Columns[state.FocusedColumn];
        return new StatusBarVm(PathBarText(column), HintsFor(state), Summarize(state, column));
    }

    private const string DrivesLabel = "ドライブ";

    private const string RecycleBinLabel = "ゴミ箱";

    /// <summary>
    /// The full path of what the cursor is on - the way a Finder path bar shows the selected item,
    /// not only the folder around it. Falls back to the column's own location on a section header
    /// or in an empty column.
    /// </summary>
    /// <remarks>
    /// A deleted file shows where it came from: inside the bin it has a meaningless name, and its
    /// original path is the only one a person recognises.
    /// </remarks>
    private static string PathBarText(Column column)
    {
        if (column.Cursor >= 0 && column.Cursor < column.Entries.Length)
        {
            var entry = column.Entries[column.Cursor];
            if (entry.Kind != EntryKind.Header)
            {
                if (entry.OriginalPath is { } original)
                {
                    return original;
                }

                if (entry.Target is Location.RecycleBin)
                {
                    return RecycleBinLabel;
                }

                if (column.PathOf(entry) is { } entryPath)
                {
                    return entryPath;
                }
            }
        }

        return column.Location switch
        {
            Location.RealDirectory directory => directory.Path,
            Location.Drives => DrivesLabel,
            Location.RecycleBin => RecycleBinLabel,
            var other => other.FilesystemPath ?? string.Empty,
        };
    }

    /// <summary>
    /// Keys worth knowing right now - not a reference card. Explorer's own keys (F2, Ctrl+C, Delete)
    /// are left out because they need no explaining: only what this app adds is shown, plus what an
    /// open mode changes.
    /// </summary>
    private static string HintsFor(AppState state)
    {
        // Enter and Esc in a rename box are obvious, and nothing else applies while typing.
        if (state.Rename is not null)
        {
            return string.Empty;
        }

        if (state.InputMode == Core.InputMode.Sort)
        {
            var sort = state.View.Sort;
            var arrow = sort.Descending ? "↓" : "↑";
            var folders = sort.DirectoriesFirst ? "・フォルダ優先" : string.Empty;
            return $"並べ替え: {FieldLabel(sort.Mode)} {arrow}{folders}    "
                + "N 名前  E 拡張子  S サイズ  M 更新日時  D フォルダ優先  Shift+キー 降順  Esc 閉じる";
        }

        var hidden = state.View.ShowHidden ? "隠しファイル: 表示中" : "隠しファイル";
        return $"S 並べ替え    Ctrl+Shift+. {hidden}    Ctrl+D ピン留め";
    }

    private static string FieldLabel(SortMode mode) => mode switch
    {
        SortMode.Name => "名前",
        SortMode.Extension => "拡張子",
        SortMode.Size => "サイズ",
        SortMode.Modified => "更新日時",
        _ => string.Empty,
    };

    /// <summary>What used to follow the path on the one status line: count, marks, and any notice.</summary>
    private static string Summarize(AppState state, Column column)
    {
        var marked = 0;
        foreach (var entry in column.Entries)
        {
            if (entry.IsMarked)
            {
                marked++;
            }
        }

        var summary = $"{column.Entries.Length} 件";
        if (marked > 0)
        {
            summary += $" | マーク: {marked}";
        }

        // The reply to something the user just asked for, when it has no other visible outcome - an
        // eject the drive refused. Transition clears it as soon as the cursor moves on.
        if (state.Notice is { } notice)
        {
            summary += " | " + notice;
        }

        return summary;
    }
}
