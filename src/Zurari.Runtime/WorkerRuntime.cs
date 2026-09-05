using System.Collections.Immutable;
using System.Security;
using System.Text;
using System.Threading.Channels;
using Zurari.Core;

namespace Zurari.Runtime;

/// <summary>
/// Executes <see cref="Effect"/>s on a fixed pool of background workers and reports the
/// resulting <see cref="Msg"/>s back through the <c>post</c> delegate supplied at construction.
/// This is the only layer allowed to perform file I/O; Core stays pure and describes I/O as data.
/// </summary>
/// <remarks>
/// <see cref="Effect.LoadPreview"/> is the exception: it runs on its own single cancellable slot
/// rather than on the pool - see <see cref="StartPreview"/> for why sharing the pool starved
/// navigation.
/// </remarks>
public sealed class WorkerRuntime : IDisposable
{
    /// <summary>
    /// Above this size, an image preview is reported as <see cref="PreviewKind.Binary"/> instead
    /// of decoding the whole file into memory. Overridable per-instance (see the constructor) so
    /// tests can exercise the "too big" branch without writing a 50MB fixture.
    /// </summary>
    private const long DefaultPreviewImageSizeLimitBytes = 50L * 1024 * 1024;

    /// <summary>How much of a file <see cref="Effect.LoadPreview"/> reads to sniff/decode as text.</summary>
    private const int PreviewHeadBytes = 64 * 1024;

    /// <summary>
    /// How much of the already-read head is carried along on a <see cref="PreviewKind.Binary"/>
    /// result (in <see cref="Msg.PreviewLoaded.ImageBytes"/> - see its remarks) for the hex-dump
    /// view. A hex pane never needs more than a handful of KB, so this is far smaller than
    /// <see cref="PreviewHeadBytes"/>.
    /// </summary>
    private const int PreviewHexHeadBytes = 4096;

    /// <summary>
    /// How long a preview waits before touching the disk, so a cursor passing over a file does not
    /// read it at all.
    /// </summary>
    /// <remarks>
    /// Holding an arrow key produces a preview request per keystroke - around thirty a second - and
    /// without this each one opens a file handle that is cancelled a moment later. On a network
    /// share, an optical drive or anything else slow that is a lot of pointless seeking, competing
    /// with the directory reads that navigation actually needs. Waiting a beat first means only the
    /// file the cursor settles on is ever opened.
    ///
    /// Short enough to stay imperceptible on a deliberate single move (the pane shows 読み込み中…
    /// meanwhile), long enough to skip the intermediate files of a held keypress.
    /// </remarks>
    private static readonly TimeSpan PreviewSettleDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>How long <see cref="ExecuteLoadPreview"/> waits for each external thumbnailer
    /// attempt (see <see cref="VideoThumbnailer"/>) before giving up on that attempt - re-checked
    /// every few seconds rather than as one hard wait, so a slow decode of a large/slow-disk file
    /// is not killed the instant a single check expires.</summary>
    private static readonly TimeSpan VideoThumbnailTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Extensions treated as video even when <see cref="FileTypeDetector"/>'s magic-number sniff
    /// is inconclusive (e.g. ASF-based WMV, or any container the signature table does not cover) -
    /// see <see cref="ExecuteLoadPreview"/>.
    /// </summary>
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm"];

    private readonly Channel<Effect> _channel;
    private readonly Action<Msg> _post;
    private readonly CancellationTokenSource _cts;
    private readonly Task[] _workers;
    private readonly long _previewImageSizeLimitBytes;
    private readonly ThumbnailCache _thumbnailCache;

    /// <summary>
    /// Supplies the places shown above the drives - favorites now, pinned shares next. Queried on
    /// every read rather than captured once, so pinning something shows up on the next refresh.
    /// </summary>
    private readonly Func<IReadOnlyList<RootPlace>>? _places;

    /// <summary>Describes the recycle bin row, or null to leave the section out entirely.</summary>
    private readonly Func<TrashPlace?>? _trash;

    /// <summary>Where pinned places are kept. Injected so tests do not touch the real settings file.</summary>
    private readonly UserSettingsStore _settings;

    /// <summary>
    /// Explorer's own name for a path, or <c>null</c> when it has none. Injected for the same reason
    /// favorites and the bin are: it comes from the shell, and Runtime may not reference Shell.
    /// </summary>
    /// <remarks>
    /// A volume with no label has no name of its own. Showing the bare letter told the user nothing -
    /// C: and a USB stick both read as just a letter, so there was no way to see which was which
    /// without opening the preview. The shell substitutes a type name ("ローカル ディスク (C:)",
    /// "USB ドライブ (D:)"), localized and correct per machine in a way a hand-kept table would not be.
    /// </remarks>
    private readonly Func<string, string?>? _displayName;

    /// <summary>
    /// 1 once the stored collapsed sections have been handed to the state - see
    /// <see cref="ExecuteReadDirectory"/>. Read from whichever worker picks up a drive-pane read, so
    /// it is claimed with <see cref="Interlocked"/> rather than a plain test-and-set.
    /// </summary>
    private int _collapseRestored;

    /// <summary>
    /// Guards every <c>_preview*</c> field below. All four move together and are only ever read or
    /// written under it.
    /// </summary>
    /// <remarks>
    /// The invariant the pair of generation fields must satisfy: whenever
    /// <see cref="_previewGeneration"/> is not -1 it equals <see cref="_highestPreviewGeneration"/>.
    /// They differ only in what survives cancellation - the current generation is cleared when a
    /// preview stops, the high-water mark is not, which is what makes a late, out-of-order request
    /// recognisable as stale rather than as something new.
    ///
    /// Nothing may block while holding this lock. <see cref="RunPreview"/>'s finally takes it, so
    /// waiting on a preview task from inside it would deadlock; see <see cref="Dispose"/>.
    /// </remarks>
    private readonly object _previewGate = new();

    /// <summary>Cancels the one in-flight preview, if any. Replaced each time a new one starts.</summary>
    private CancellationTokenSource? _previewCts;

    /// <summary>
    /// <see cref="Effect.LoadPreview.Generation"/> of the in-flight preview, or -1 when idle. Makes
    /// <see cref="Effect.CancelPreview"/> order-independent - see <see cref="CancelInFlightPreview"/>.
    /// </summary>
    private int _previewGeneration = -1;

    /// <summary>
    /// Highest <see cref="Effect.LoadPreview.Generation"/> ever started, so an out-of-order request
    /// cannot supersede a newer one - see <see cref="StartPreview"/>.
    /// </summary>
    private int _highestPreviewGeneration = -1;

    /// <summary>The in-flight preview, kept only so <see cref="Dispose"/> can wait for it to unwind.</summary>
    private Task? _previewTask;

    static WorkerRuntime()
    {
        // Shift-JIS (codepage 932) is not one of .NET Core's built-in encodings; this registers
        // the full legacy codepage table so Effect.LoadPreview's Shift-JIS fallback can use
        // Encoding.GetEncoding(932) below.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Starts <paramref name="workerCount"/> background workers that pull queued effects and
    /// execute them. <paramref name="post"/> may be invoked concurrently from any worker thread;
    /// the caller is responsible for marshalling results onto whatever thread owns application
    /// state (the UI thread in the real app). <paramref name="external"/>, if given, lets the
    /// caller cancel the whole runtime cooperatively in addition to <see cref="Dispose"/>.
    /// <paramref name="previewImageSizeLimitBytes"/> overrides <see cref="DefaultPreviewImageSizeLimitBytes"/>
    /// (test-only knob; production callers should omit it).
    /// </summary>
    public WorkerRuntime(
        Action<Msg> post,
        int workerCount = 2,
        CancellationToken? external = null,
        long? previewImageSizeLimitBytes = null,
        ThumbnailCache? thumbnailCache = null,
        Func<IReadOnlyList<RootPlace>>? places = null,
        Func<TrashPlace?>? trash = null,
        UserSettingsStore? settings = null,
        Func<string, string?>? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(post);
        if (workerCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(workerCount), workerCount, "At least one worker is required.");
        }

        _post = post;
        _channel = Channel.CreateUnbounded<Effect>();
        _cts = external.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(external.Value)
            : new CancellationTokenSource();
        _previewImageSizeLimitBytes = previewImageSizeLimitBytes ?? DefaultPreviewImageSizeLimitBytes;
        _thumbnailCache = thumbnailCache ?? new ThumbnailCache();
        _places = places;
        _trash = trash;
        _settings = settings ?? new UserSettingsStore();
        _displayName = displayName;

        _workers = new Task[workerCount];
        for (var i = 0; i < workerCount; i++)
        {
            _workers[i] = Task.Run(() => RunWorkerAsync(_cts.Token));
        }
    }

    /// <summary>Enqueues an effect for execution by one of the worker threads. Never blocks.</summary>
    public void Submit(Effect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _channel.Writer.TryWrite(effect);
    }

    private async Task RunWorkerAsync(CancellationToken token)
    {
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var effect))
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    Execute(effect);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Dispose() requested cancellation; exit quietly.
        }
        catch (ChannelClosedException)
        {
            // Writer completed while we were waiting; exit quietly.
        }
    }

    private void Execute(Effect effect)
    {
        switch (effect)
        {
            case Effect.ReadDirectory readDirectory:
                ExecuteReadDirectory(readDirectory);
                break;
            case Effect.LoadPreview loadPreview:
                StartPreview(loadPreview);
                break;
            case Effect.CancelPreview cancelPreview:
                CancelInFlightPreview(cancelPreview.Generation);
                break;
            case Effect.CreateFolder createFolder:
                ExecuteCreateFolder(createFolder);
                break;
            case Effect.SetViewOptions setView:
                _settings.Update(s => s with
                {
                    SortMode = setView.View.Sort.Mode.ToString(),
                    SortDescending = setView.View.Sort.Descending,
                    DirectoriesFirst = setView.View.Sort.DirectoriesFirst,
                    ShowHidden = setView.View.ShowHidden,
                });
                break;
            case Effect.SetCollapsedGroups setCollapsedGroups:
                _settings.Update(s => s with { CollapsedGroups = [.. setCollapsedGroups.Groups] });
                break;
            case Effect.SetPinned setPinned:
                ExecuteSetPinned(setPinned);
                break;
        }
    }

    /// <summary>
    /// Begins <paramref name="effect"/> on its own task and returns immediately, cancelling whatever
    /// preview was already running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Preview deliberately does not execute on the worker pool. A preview can block for a long time
    /// - opening a handle on a cloud placeholder hydrates the file, and a video thumbnail shells out
    /// to ffmpeg for up to <see cref="VideoThumbnailTimeout"/> - and the pool is small (two workers
    /// by default). Running previews there meant two slow videos under the cursor could occupy every
    /// worker at once, leaving <see cref="Effect.ReadDirectory"/> queued behind them: navigation
    /// stopped until a thumbnail timed out.
    /// </para>
    /// <para>
    /// Only one preview is ever in flight, matching what the UI can show. Starting a new one cancels
    /// the old rather than waiting for it, so cancellation is not itself blocked by the work it is
    /// cancelling; a superseded result would be discarded by the generation check anyway.
    /// </para>
    /// </remarks>
    private void StartPreview(Effect.LoadPreview effect)
    {
        CancellationToken token;
        CancellationTokenSource cts;
        lock (_previewGate)
        {
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            // Generations only ever increase, so anything not newer than what we already started is
            // stale and must not supersede it. Effects are submitted in order but drained from one
            // channel by several workers, so two consecutive cursor moves can reach here reversed;
            // letting the older one win would show the wrong file, and its result would then be
            // discarded by Core's generation check - leaving the pane loading forever.
            if (effect.Generation <= _highestPreviewGeneration)
            {
                return;
            }

            _highestPreviewGeneration = effect.Generation;
            CancelPreviewCtsUnderLock();
            cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _previewCts = cts;
            _previewGeneration = effect.Generation;
            token = cts.Token;
            _previewTask = Task.Run(() => RunPreview(effect, cts, token), CancellationToken.None);
        }
    }

    /// <summary>
    /// Runs one preview and then disposes its <see cref="CancellationTokenSource"/>.
    /// </summary>
    /// <remarks>
    /// Disposal belongs to the task, not to whoever cancels it. A cancelled token is still being
    /// read by the task that owns it - <c>token.WaitHandle</c> in particular throws
    /// <see cref="ObjectDisposedException"/> once the source is disposed, unlike
    /// <c>IsCancellationRequested</c> - so disposing at cancellation time turned a superseded
    /// preview into a spurious <see cref="Msg.PreviewFailed"/>. Cancelling merely signals; the task
    /// cleans up when it is genuinely finished with the token.
    /// </remarks>
    private void RunPreview(Effect.LoadPreview effect, CancellationTokenSource cts, CancellationToken token)
    {
        try
        {
            ExecuteLoadPreview(effect, token);
        }
        finally
        {
            lock (_previewGate)
            {
                if (ReferenceEquals(_previewCts, cts))
                {
                    _previewCts = null;
                    _previewGeneration = -1;
                }

                cts.Dispose();
            }
        }
    }

    /// <summary>
    /// Cancels the in-flight preview, but only if it is still the one for
    /// <paramref name="generation"/>. Safe to call when nothing is running.
    /// </summary>
    /// <remarks>
    /// The generation check is what makes this order-independent, and it is load-bearing rather
    /// than defensive. <c>Transition.ReconcilePreview</c> emits
    /// <see cref="Effect.CancelPreview"/> and <see cref="Effect.LoadPreview"/> together, in that
    /// order, but they are drained from one channel by several workers - so the cancel can be
    /// executed *after* the load it was meant to precede. Cancelling whatever happens to be
    /// current would then kill the new preview, and the pane would sit on "loading" forever.
    /// </remarks>
    private void CancelInFlightPreview(int generation)
    {
        lock (_previewGate)
        {
            if (_previewGeneration != generation)
            {
                return;
            }

            CancelPreviewCtsUnderLock();
        }
    }

    private void CancelPreviewCtsUnderLock()
    {
        var previous = _previewCts;
        _previewCts = null;
        _previewGeneration = -1;
        if (previous is null)
        {
            return;
        }

        try
        {
            // Signal only. The owning task disposes it - see RunPreview.
            previous.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Its task already finished and cleaned up; nothing left to cancel.
        }
    }

    /// <summary>
    /// The stored view options, falling back to the default for anything missing or unrecognized.
    /// </summary>
    /// <remarks>
    /// The mode is stored by name rather than by its numeric value: renaming or reordering the enum
    /// would otherwise silently reinterpret an old settings file as a different mode.
    /// </remarks>
    private static ViewOptions ReadViewOptions(UserSettings settings)
    {
        var mode = Enum.TryParse<SortMode>(settings.SortMode, ignoreCase: true, out var parsed)
            ? parsed
            : SortOrder.Default.Mode;

        return new ViewOptions(
            new SortOrder(
                mode,
                settings.SortDescending ?? SortOrder.Default.Descending,
                settings.DirectoriesFirst ?? SortOrder.Default.DirectoriesFirst),
            settings.ShowHidden ?? ViewOptions.Default.ShowHidden);
    }

    /// <summary>
    /// Creates a new folder, picking a name nothing else in the directory has.
    /// </summary>
    /// <remarks>
    /// The name is decided here rather than in Core because deciding it means looking at what is
    /// already there. Explorer's own behaviour: 新しいフォルダー, then 新しいフォルダー (2) and so on.
    /// </remarks>
    private void ExecuteCreateFolder(Effect.CreateFolder effect)
    {
        if (effect.Location.FilesystemPath is not { } parent)
        {
            _post(new Msg.NoticeRaised("ここにはフォルダーを作れません"));
            return;
        }

        try
        {
            for (var attempt = 1; attempt <= 1000; attempt++)
            {
                var name = attempt == 1 ? "新しいフォルダー" : $"新しいフォルダー ({attempt})";
                var path = Path.Combine(parent, name);
                if (Directory.Exists(path) || File.Exists(path))
                {
                    continue;
                }

                Directory.CreateDirectory(path);
                _post(new Msg.FolderCreated(effect.ColumnIndex, effect.Location, name));
                return;
            }

            _post(new Msg.NoticeRaised("空いている名前が見つかりません"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _post(new Msg.NoticeRaised($"フォルダーを作れません: {ex.Message}"));
        }
    }

    private void ExecuteReadDirectory(Effect.ReadDirectory effect)
    {
        try
        {
            var entries = effect.Location switch
            {
                Location.Drives => ReadDrives(),
                Location.RealDirectory directory => ReadDirectoryEntries(directory.Path),
                _ => throw new NotSupportedException(
                    $"No reader for location kind {effect.Location.GetType().Name}."),
            };
            _post(new Msg.DirectoryLoaded(effect.ColumnIndex, effect.Location, entries));

            // Sections only exist in the drive pane, and only once its rows are in place - so this
            // follows the load rather than riding along with it.
            //
            // Once only, per process. The stored set seeds the session; after that the state carries
            // the collapse and a refresh preserves it, so re-reading the file would only introduce a
            // race - collapse a section and hit F5 fast enough and the read could beat the write,
            // putting the section back.
            if (effect.Location is Location.Drives && Interlocked.Exchange(ref _collapseRestored, 1) == 0)
            {
                var settings = _settings.Load();
                _post(new Msg.CollapsedGroupsRestored(
                    effect.ColumnIndex, [.. settings.CollapsedGroups ?? []]));
                _post(new Msg.ViewRestored(ReadViewOptions(settings)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _post(new Msg.DirectoryLoadFailed(effect.ColumnIndex, effect.Location, ex.Message));
        }
    }


    /// <summary>
    /// Builds the drive pane: a header row per section, followed by that section's places.
    /// </summary>
    /// <remarks>
    /// Both the headers and their rows are produced here rather than during projection, because the
    /// cursor lands on a header and <c>Space</c> collapses it - see <see cref="EntryKind.Header"/>.
    /// A section with nothing in it contributes no header.
    /// </remarks>
    private ImmutableArray<Entry> ReadDrives()
    {
        var builder = ImmutableArray.CreateBuilder<Entry>();

        // Disambiguated across both sections at once, not within each. Two rows reading the same
        // are confusing wherever they sit, and a favorite can easily collide with a pinned folder.
        var favorites = ReadFavorites();
        var pinned = new List<Entry>();
        ReadAddedPlaces(favorites, pinned);
        DisambiguateLabels(favorites, pinned);

        AppendSection(builder, EntryGroups.Favorites, "お気に入り", favorites);
        AppendSection(builder, EntryGroups.Pinned, "ネットワーク", pinned);

        var drives = new List<Entry>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            // Not-ready drives (empty CD/DVD, disconnected mapped drives...) are still listed. The
            // cursor has to be able to reach one - otherwise there is nothing to aim an eject at -
            // and entering one simply fails later with DirectoryLoadFailed.
            drives.Add(new Entry(
                drive.Name,
                EntryKind.Drive,
                SizeBytes: -1,
                Group: EntryGroups.Drives,
                DisplayName: DescribeDrive(drive)));
        }

        AppendSection(builder, EntryGroups.Drives, "ドライブ", drives);
        AppendSection(builder, EntryGroups.Trash, "ゴミ箱", ReadTrash());
        return builder.ToImmutable();
    }

    /// <summary>
    /// Adds or removes a pinned place and reports the change, so the pane re-reads.
    /// </summary>
    /// <remarks>
    /// Reports even when nothing changed. Pinning something already pinned is a no-op to the
    /// settings file, but the user pressed a key and should see the pane settle rather than wonder
    /// whether it registered.
    /// </remarks>
    private void ExecuteSetPinned(Effect.SetPinned effect)
    {
        var settings = _settings.Load();
        var pinned = settings.PinnedPaths is { } existing
            ? new List<string>(existing)
            : new List<string>();

        // Case-insensitive, since Windows paths are - pinning C:\Data twice under different casing
        // would otherwise produce two rows for one folder.
        var index = pinned.FindIndex(p => string.Equals(p, effect.Path, StringComparison.OrdinalIgnoreCase));
        if (effect.Pin && index < 0 && Directory.Exists(effect.Path))
        {
            // Only folders. Dropping a mixed selection onto the pane is normal, and the files in it
            // are simply not places - storing them would leave rows that can never appear.
            pinned.Add(effect.Path);
        }
        else if (!effect.Pin && index >= 0)
        {
            pinned.RemoveAt(index);
        }

        _settings.Update(s => s with { PinnedPaths = [.. pinned] });
        _post(new Msg.PlacesChanged());
    }

    /// <summary>
    /// The pinned section: places the user added, minus any that have since gone away.
    /// </summary>
    /// <remarks>
    /// A pin that no longer resolves is skipped rather than removed. An unreachable share is the
    /// normal state of a laptop away from its network, and silently forgetting the pin because the
    /// machine was offline once would be worse than showing nothing that day.
    /// </remarks>
    private void ReadAddedPlaces(List<Entry> favorites, List<Entry> pinned)
    {
        // Whatever the known folders already cover. Adding Downloads by hand is an easy thing to do
        // and would otherwise put a second, removable row beside the one that is always there.
        var alreadyShown = new HashSet<string>(
            favorites.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var path in _settings.Load().PinnedPaths ?? [])
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || !alreadyShown.Add(path))
            {
                continue;
            }

            // The section follows the path, not which key added it. A UNC path is a network place
            // wherever the user was standing when they added it, and a local folder is a favorite -
            // so the two headers stay honest without the user having to choose the right key.
            var group = EntryGroups.ForPath(path);
            var isNetwork = group == EntryGroups.Pinned;
            (isNetwork ? pinned : favorites).Add(new Entry(
                path,
                EntryKind.Directory,
                SizeBytes: -1,
                Group: group,
                Target: new Location.RealDirectory(path),
                DisplayName: LastSegment(path),
                IsRemovable: true));
        }
    }

    /// <summary>
    /// The recycle bin row: one entry pointing at <see cref="Location.RecycleBin"/>, or nothing at
    /// all when the composition root did not supply one.
    /// </summary>
    /// <remarks>
    /// A section of one, deliberately - it is a place of its own rather than a favorite, the way
    /// Finder keeps Trash apart from everything else. Not removable: it is always there.
    /// </remarks>
    private List<Entry> ReadTrash()
    {
        if (_trash?.Invoke() is not { } trash)
        {
            return [];
        }

        return
        [
            new Entry(
                "::recyclebin",
                EntryKind.Directory,
                SizeBytes: -1,
                Group: EntryGroups.Trash,
                Target: Location.RecycleBin.Instance,
                DisplayName: trash.Label),
        ];
    }

    /// <summary>The trailing folder or share name, which is what a pinned row reads as.</summary>
    private static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var separator = trimmed.LastIndexOfAny(['\\', '/']);
        return separator < 0 || separator == trimmed.Length - 1 ? trimmed : trimmed[(separator + 1)..];
    }

    /// <summary>
    /// The favorites section: the places <see cref="_places"/> offers that actually exist.
    /// </summary>
    /// <remarks>
    /// The list is supplied rather than resolved here because resolving it needs the shell - there
    /// is no <c>Environment.SpecialFolder.Downloads</c> - and this layer is not allowed to depend on
    /// <c>Zurari.Shell</c>. The composition root, which sees both, does the resolving; checking that
    /// a folder is really there is filesystem work and belongs here.
    /// </remarks>
    private List<Entry> ReadFavorites()
    {
        var favorites = new List<Entry>();
        if (_places is null)
        {
            return favorites;
        }

        foreach (var place in _places())
        {
            if (!Directory.Exists(place.Path))
            {
                continue;
            }

            favorites.Add(new Entry(
                // The path, not the label: two favorites can legitimately read the same, and the
                // cursor follows entries by Name. See DisambiguateLabels.
                place.Path,
                EntryKind.Directory,
                SizeBytes: -1,
                Group: EntryGroups.Favorites,
                Target: new Location.RealDirectory(place.Path),
                DisplayName: place.Label));
        }

        return favorites;
    }

    /// <summary>
    /// Qualifies any label that appears more than once with its path, so two rows never read alike.
    /// </summary>
    /// <remarks>
    /// Only the ambiguous ones are qualified. A lone "ダウンロード" needs no address after it; two
    /// pinned folders both called <c>fol1</c> do, and become <c>fol1 (D:\test\fol1)</c> and
    /// <c>fol1 (\\testsv\test\fol1)</c>. This is the one pane where duplicates are possible at all -
    /// a directory listing cannot contain two entries with the same name.
    /// </remarks>
    private static void DisambiguateLabels(params List<Entry>[] sections)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rows in sections)
        {
            foreach (var row in rows)
            {
                counts[row.Label] = counts.TryGetValue(row.Label, out var n) ? n + 1 : 1;
            }
        }

        foreach (var rows in sections)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (counts[rows[i].Label] > 1)
                {
                    rows[i] = rows[i] with { DisplayName = $"{rows[i].Label} ({rows[i].Name})" };
                }
            }
        }
    }

    /// <summary>Adds a header row and its contents, or nothing at all when the section is empty.</summary>
    private static void AppendSection(
        ImmutableArray<Entry>.Builder builder, string group, string label, IReadOnlyList<Entry> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        builder.Add(new Entry(label, EntryKind.Header, SizeBytes: -1, Group: group));
        builder.AddRange(rows);
    }

    /// <summary>
    /// What a drive row reads as: its volume label with the letter, the way Explorer shows it, or
    /// just the letter when the volume has no label or cannot be reached.
    /// </summary>
    private string DescribeDrive(DriveInfo drive)
    {
        var letter = drive.Name.TrimEnd('\\', '/');

        // Explorer's own name first: it names an unlabelled volume by its type, which is the
        // only thing that distinguishes one bare letter from another.
        if (_displayName?.Invoke(drive.Name) is { Length: > 0 } shellName)
        {
            return shellName;
        }

        try
        {
            if (!drive.IsReady)
            {
                return $"{letter} ({DriveKindLabel(drive.DriveType)})";
            }

            var label = drive.VolumeLabel;
            return string.IsNullOrWhiteSpace(label) ? letter : $"{label} ({letter})";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DriveNotFoundException)
        {
            // A drive can stop being ready between the check and the read.
            return letter;
        }
    }

    /// <summary>
    /// What kind of volume this is, for the capacity preview's type line. Distinct from
    /// <see cref="DriveKindLabel"/>, which exists to explain a drive that is <em>not</em> ready and
    /// so has no name for a working fixed disk.
    /// </summary>
    private static string DescribeDriveType(DriveType type) => type switch
    {
        DriveType.Fixed => "固定ドライブ",
        DriveType.CDRom => "光学ドライブ",
        DriveType.Removable => "リムーバブルドライブ",
        DriveType.Network => "ネットワークドライブ",
        DriveType.Ram => "RAM ディスク",
        _ => "ドライブ",
    };

    private static string DriveKindLabel(DriveType type) => type switch
    {
        DriveType.CDRom => "光学ドライブ",
        DriveType.Removable => "リムーバブル",
        DriveType.Network => "ネットワーク",
        DriveType.Ram => "RAM ディスク",
        _ => "準備できていません",
    };

    /// <summary>
    /// Whether Windows considers this hidden. Hidden or system - Explorer treats them as one class
    /// behind one switch, and a listing that showed pagefile.sys but not a hidden folder would be
    /// explaining a distinction the user did not ask about.
    /// </summary>
    /// <remarks>
    /// Reported on every entry rather than filtered out here. The visible list is Core's to derive,
    /// and revealing hidden files has to cost nothing - see <c>ViewOptions.ShowHidden</c>.
    /// </remarks>
    private static bool IsHidden(FileAttributes attributes) =>
        (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    private static ImmutableArray<Entry> ReadDirectoryEntries(string path)
    {
        var directories = new List<Entry>();
        var files = new List<Entry>();
        var dirInfo = new DirectoryInfo(path);

        foreach (var dir in dirInfo.EnumerateDirectories())
        {
            try
            {
                directories.Add(new Entry(
                    dir.Name,
                    EntryKind.Directory,
                    SizeBytes: -1,
                    Modified: dir.LastWriteTime,
                    IsHidden: IsHidden(dir.Attributes)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Broken reparse point or similar: drop this one entry, keep listing the rest.
            }
        }

        foreach (var file in dirInfo.EnumerateFiles())
        {
            try
            {
                files.Add(new Entry(
                    file.Name,
                    EntryKind.File,
                    SizeBytes: file.Length,
                    Modified: file.LastWriteTime,
                    IsHidden: IsHidden(file.Attributes)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Same as above: skip this file, keep the rest of the listing.
            }
        }

        directories.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var builder = ImmutableArray.CreateBuilder<Entry>(directories.Count + files.Count);
        builder.AddRange(directories);
        builder.AddRange(files);
        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Reads up to <see cref="PreviewHeadBytes"/> of <see cref="Effect.LoadPreview.Path"/>,
    /// sniffs it with <see cref="FileTypeDetector"/>, and posts the outcome back:
    /// <list type="bullet">
    /// <item>An image kind whose whole file is at or under <see cref="_previewImageSizeLimitBytes"/>
    /// -&gt; the entire file's bytes as <see cref="PreviewKind.Image"/>.</item>
    /// <item>An image kind over that limit -&gt; <see cref="PreviewKind.Binary"/> with a
    /// "(サイズ超過)" suffix on the detected label.</item>
    /// <item>Anything that decodes as text (BOM-tagged UTF-8/UTF-16, strict UTF-8, or Shift-JIS,
    /// and containing no NUL byte in the sampled head) -&gt; <see cref="PreviewKind.Text"/> with
    /// the decoded head.</item>
    /// <item>Everything else -&gt; <see cref="PreviewKind.Binary"/> with the detected label.</item>
    /// </list>
    /// Both <see cref="PreviewKind.Binary"/> cases above also carry up to
    /// <see cref="PreviewHexHeadBytes"/> of the head in the result's <c>ImageBytes</c> (see
    /// <see cref="HexHead"/> and <see cref="Msg.PreviewLoaded"/>'s remarks) so the App layer can
    /// render a hex dump.
    /// Any exception (missing file, access denied, ...) is reported as
    /// <see cref="Msg.PreviewFailed"/> instead of propagating - a bad preview request must never
    /// take down a worker.
    /// </summary>
    /// <summary>
    /// How full a volume is, or how much is in the recycle bin - the preview for a row that is a
    /// place rather than a file.
    /// </summary>
    /// <remarks>
    /// Runs on the same cancellable slot as a file preview, and for the same reason: a not-ready
    /// optical drive or a disconnected share can take seconds to answer, and the cursor may well
    /// have moved on by then.
    /// </remarks>
    private void ExecuteLoadCapacity(Effect.LoadPreview effect)
    {
        if (effect.Target == PreviewTarget.RecycleBin)
        {
            var trash = _trash?.Invoke();
            _post(trash is null
                ? new Msg.PreviewFailed(effect.Generation, "ゴミ箱の情報を取得できません")
                : new Msg.PreviewCapacityLoaded(
                    effect.Generation,
                    new PreviewCapacity(
                        "ゴミ箱",
                        "ゴミ箱",
                        UsedBytes: trash.TotalBytes,
                        TotalBytes: null,
                        ItemCount: trash.ItemCount)));
            return;
        }

        try
        {
            var (capacity, unavailable) = ReadVolume(effect.Path);
            _post(capacity is null
                ? new Msg.PreviewFailed(effect.Generation, unavailable ?? "利用できません")
                : new Msg.PreviewCapacityLoaded(effect.Generation, capacity));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or SecurityException)
        {
            _post(new Msg.PreviewFailed(effect.Generation, ex.Message));
        }
    }

    /// <summary>
    /// The size and free space of a drive letter or a UNC share, or a reason it cannot say.
    /// </summary>
    /// <remarks>
    /// Two routes because the BCL only covers one of them: <c>DriveInfo</c> throws on a UNC path, so
    /// a share's numbers have to come from <c>GetDiskFreeSpaceEx</c>, which is what
    /// <c>DriveInfo</c> itself calls for a local volume. Kernel32 rather than the shell - this is
    /// filesystem I/O, which is Runtime's job, and the only P/Invoke here for that reason.
    /// </remarks>
    private (PreviewCapacity? Capacity, string? Unavailable) ReadVolume(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            if (!NativeMethods.GetDiskFreeSpaceExW(path, out _, out var shareTotal, out var shareFree))
            {
                return (null, "共有に接続できません");
            }

            return (
                new PreviewCapacity(
                    _displayName?.Invoke(path) is { Length: > 0 } shareName ? shareName : LastSegment(path),
                    "ネットワーク共有",
                    UsedBytes: Math.Max(0, shareTotal - shareFree),
                    TotalBytes: shareTotal),
                null);
        }

        var drive = new DriveInfo(path);
        if (!drive.IsReady)
        {
            // An empty optical drive, an unplugged card reader, a disconnected mapped drive. Not an
            // error to apologise for - the row is deliberately still listed so it can be reached, and
            // what it is stays worth saying even when how full it is cannot be answered. DriveType
            // needs no disc; VolumeLabel and DriveFormat would both throw here.
            return (
                new PreviewCapacity(
                    DescribeDrive(drive),
                    DescribeDriveType(drive.DriveType),
                    Status: "準備できていません"),
                null);
        }

        var total = drive.TotalSize;
        var free = drive.AvailableFreeSpace;
        return (
            new PreviewCapacity(
                DescribeDrive(drive),
                $"{drive.DriveFormat} · {DescribeDriveType(drive.DriveType)}",
                UsedBytes: Math.Max(0, total - free),
                TotalBytes: total),
            null);
    }

    private void ExecuteLoadPreview(Effect.LoadPreview effect, CancellationToken token)
    {
        try
        {
            // Settle first, before any I/O - see PreviewSettleDelay. WaitOne returns as soon as the
            // token is signalled, so a superseded request wakes immediately and never opens the file.
            if (token.WaitHandle.WaitOne(PreviewSettleDelay))
            {
                return;
            }

            if (effect.Target != PreviewTarget.File)
            {
                ExecuteLoadCapacity(effect);
                return;
            }

            using var stream = new FileStream(
                effect.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            token.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(effect.Path);
            var baseMetadata = new PreviewMetadata(fileInfo.Name, fileInfo.Length, fileInfo.CreationTime, fileInfo.LastWriteTime);

            var headLength = (int)Math.Min(stream.Length, PreviewHeadBytes);
            var head = new byte[headLength];
            stream.ReadExactly(head);

            token.ThrowIfCancellationRequested();

            var detected = FileTypeDetector.Detect(head);

            if (detected.Category == FileCategory.Video || IsVideoExtension(effect.Path))
            {
                ExecuteVideoPreview(effect, detected, effect.Path, head, baseMetadata, token);
                return;
            }

            if (detected.Category == FileCategory.Image)
            {
                PostImagePreview(effect, stream, detected, head, baseMetadata);
                return;
            }

            if (TryDecodeAsText(head, out var text))
            {
                _post(new Msg.PreviewLoaded(
                    effect.Generation, PreviewKind.Text, text, ImmutableArray<byte>.Empty, null, baseMetadata));
                return;
            }

            _post(new Msg.PreviewLoaded(
                effect.Generation, PreviewKind.Binary, null, HexHead(head), detected.Label, baseMetadata));
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer preview (or shutting down). The caller already moved on, so
            // there is deliberately no Msg: reporting a failure here would race the new result.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _post(new Msg.PreviewFailed(effect.Generation, ex.Message));
        }
    }

    private static bool IsVideoExtension(string path)
    {
        var ext = Path.GetExtension(path);
        foreach (var candidate in VideoExtensions)
        {
            if (string.Equals(ext, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string VideoLabel(DetectedType detected, string path)
    {
        if (detected.Category == FileCategory.Video)
        {
            return detected.Label;
        }

        var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return ext.Length == 0 ? "Video" : $"{ext} Video";
    }

    /// <summary>
    /// Tries an external thumbnailer (see <see cref="VideoThumbnailer"/>) for a video file: on
    /// success, reports it as a normal <see cref="PreviewKind.Image"/> preview (pixel dimensions
    /// parsed cheaply from the generated PNG via <see cref="ImageHeaderParser"/> - it is a PNG we
    /// just made, no external decoder needed); on failure (no tool installed, or the tool could not
    /// produce a frame for this file), falls back to the same Binary preview as any other
    /// non-renderable file, with a label noting the missing tool.
    /// </summary>
    private void ExecuteVideoPreview(
        Effect.LoadPreview effect,
        DetectedType detected,
        string path,
        byte[] head,
        PreviewMetadata baseMetadata,
        CancellationToken token)
    {
        if (_thumbnailCache.TryGet(path, out var cachedBytes, out var cachedFailure))
        {
            if (cachedBytes is not null)
            {
                PostVideoThumbnail(effect, cachedBytes, baseMetadata);
            }
            else
            {
                PostVideoFallback(effect, detected, path, head, baseMetadata, cachedFailure);
            }

            return;
        }

        var outcome = VideoThumbnailer.TryCreateThumbnail(path, VideoThumbnailTimeout, token);
        if (outcome.Bytes is not null)
        {
            _thumbnailCache.StoreSuccess(path, outcome.Bytes);
            PostVideoThumbnail(effect, outcome.Bytes, baseMetadata);
            return;
        }

        // Only a verdict about this file is worth remembering - a missing ffmpeg or a timeout says
        // nothing about it, and caching either would keep failing after the cause went away.
        if (outcome.Failure == ThumbnailFailure.FileRejected && outcome.FailureDetail is { } detail)
        {
            _thumbnailCache.StoreFailure(path, detail);
        }

        PostVideoFallback(effect, detected, path, head, baseMetadata, outcome.FailureDetail);
    }

    private void PostVideoThumbnail(Effect.LoadPreview effect, byte[] png, PreviewMetadata baseMetadata)
    {
        var metadata = WithPixelInfo(baseMetadata, png);
        _post(new Msg.PreviewLoaded(effect.Generation, PreviewKind.Image, null, [.. png], null, metadata));
    }

    private void PostVideoFallback(
        Effect.LoadPreview effect,
        DetectedType detected,
        string path,
        byte[] head,
        PreviewMetadata baseMetadata,
        string? failureDetail)
    {
        var label = $"{VideoLabel(detected, path)} ({failureDetail})";
        _post(new Msg.PreviewLoaded(effect.Generation, PreviewKind.Binary, null, HexHead(head), label, baseMetadata));
    }

    /// <summary>
    /// Slices <paramref name="head"/> down to <see cref="PreviewHexHeadBytes"/> for the hex-dump
    /// view carried on a <see cref="PreviewKind.Binary"/> result - see <see cref="PreviewHexHeadBytes"/>.
    /// </summary>
    private static ImmutableArray<byte> HexHead(byte[] head)
    {
        var length = Math.Min(head.Length, PreviewHexHeadBytes);
        return ImmutableArray.Create(head, 0, length);
    }

    private void PostImagePreview(
        Effect.LoadPreview effect, FileStream stream, DetectedType detected, byte[] head, PreviewMetadata baseMetadata)
    {
        var metadata = WithPixelInfo(baseMetadata, head);

        if (stream.Length > _previewImageSizeLimitBytes)
        {
            _post(new Msg.PreviewLoaded(
                effect.Generation, PreviewKind.Binary, null, HexHead(head), $"{detected.Label} (サイズ超過)", metadata));
            return;
        }

        stream.Position = 0;
        var allBytes = new byte[stream.Length];
        stream.ReadExactly(allBytes);
        _post(new Msg.PreviewLoaded(effect.Generation, PreviewKind.Image, null, [.. allBytes], null, metadata));
    }

    /// <summary>Attaches pixel width/height/bit-depth to <paramref name="baseMetadata"/> when
    /// <see cref="ImageHeaderParser"/> can parse them out of <paramref name="imageHead"/>; returns
    /// <paramref name="baseMetadata"/> unchanged otherwise.</summary>
    private static PreviewMetadata WithPixelInfo(PreviewMetadata baseMetadata, byte[] imageHead)
    {
        var dims = ImageHeaderParser.Parse(imageHead);
        return dims is { } d
            ? baseMetadata with { PixelWidth = d.Width, PixelHeight = d.Height, BitsPerPixel = d.BitsPerPixel }
            : baseMetadata;
    }

    /// <summary>
    /// Best-effort text decode of <paramref name="head"/>: a BOM (UTF-8/UTF-16 LE/UTF-16 BE)
    /// always wins and is decoded directly; otherwise a NUL byte anywhere in <paramref name="head"/>
    /// rules text out outright (BOM-less UTF-16 legitimately contains NULs, which is exactly why
    /// that path is handled above instead), and the remainder is tried as strict UTF-8, falling
    /// back to strict Shift-JIS. Returns <c>false</c> (binary) if nothing decodes cleanly.
    /// </summary>
    private static bool TryDecodeAsText(byte[] head, out string text)
    {
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
        {
            text = Encoding.UTF8.GetString(head, 3, head.Length - 3);
            return true;
        }

        if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(head, 2, head.Length - 2);
            return true;
        }

        if (head.Length >= 2 && head[0] == 0xFE && head[1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(head, 2, head.Length - 2);
            return true;
        }

        if (head.Length == 0)
        {
            text = string.Empty;
            return true;
        }

        if (Array.IndexOf(head, (byte)0) >= 0)
        {
            text = string.Empty;
            return false;
        }

        var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            text = utf8Strict.GetString(head);
            return true;
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8; fall through to the Shift-JIS attempt below.
        }

        try
        {
            var sjisStrict = Encoding.GetEncoding(
                932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            text = sjisStrict.GetString(head);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Stops accepting new work, cancels in-flight work cooperatively, and waits (bounded to a
    /// few seconds) for all workers to exit. Effects already queued but not yet started are
    /// dropped. Safe to call even while effects are queued: this returns promptly rather than
    /// draining the backlog.
    /// </summary>
    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();

        Task? previewTask;
        lock (_previewGate)
        {
            CancelPreviewCtsUnderLock();
            previewTask = _previewTask;
            _previewTask = null;
        }

        try
        {
            Task.WaitAll(_workers, TimeSpan.FromSeconds(5));

            // The in-flight preview runs off the pool, so waiting on the workers does not cover it.
            // It is given its own budget to unwind: cancellation kills any ffmpeg it started, but
            // that still takes a moment.
            //
            // This wait MUST stay outside _previewGate: RunPreview's finally takes that lock, so
            // waiting while holding it would deadlock against the very task being waited for.
            previewTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // A worker faulted while unwinding from cancellation; nothing more to do on Dispose.
        }

        // _previewCts is deliberately not disposed here; see the CA2213 note in .editorconfig.
        // Only the task that created it may dispose it, and only when it has stopped reading the
        // token - a preview still unwinding after the budget above would otherwise see its token
        // disposed mid-wait. Superseded previews are cancelled but not waited for; they hold
        // nothing but their own token and die on their own.
        _cts.Dispose();
    }
}
