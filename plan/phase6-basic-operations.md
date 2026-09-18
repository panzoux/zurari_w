# Phase 6 — 基本操作の穴埋め + プレビュー刷新

**位置づけ**: [roadmap.md](roadmap.md) の Phase 6。着手時の原文ではなく、進行中の現況(表と Records は随時更新)。

## Context

Phase 6 was sixteen loose checkboxes. Grilling turned it into decisions, and four reshaped it:

- **The drive pane becomes a virtual folder** — drives, shares, mounted images and Trash, the way
  Finder's sidebar lists Locations. It was filed as a preview-pane item; it is not one.
- **Phase 8 comes first.** `Location` replaces `Column.Path`, which *dissolves* several problems
  rather than relocating them.
- **The preview pipeline becomes cancellable** — not just video. Opening a handle on a slow or
  cloud-backed disk stalls, and it runs on every keystroke.
- **This is the GUI version, so it uses Windows' own facilities.** Inventory with decisions below.

---

# Status

**6a-6d, 6f and 6g are on `main`** - fast-forwarded from `phase6a-location-foundation` on
2026-09-05, once 6d was complete (see R-1). 6c and 6e.1-6e.6 followed; 6e.7 is deferred.
850 tests, `scripts/check.ps1` green.

<sub>The test count is written by `scripts/status.ps1`, and `check.ps1` refuses to pass while it is
stale. Do not edit it by hand. A commit count used to live here too; it was removed because it is
wrong the moment the next commit lands - `git log --oneline main..` is the honest form of it.</sub>

| | Item | Status | Commit |
|---|---|---|---|
| **6a** | **Location foundation** (absorbed Phase 8) | `[x]` | `2b74c1a` |
| **6b** | **Column data model**: raw list, derived view | `[x]` | `2cfbfee` |
| **6c** | **Cancellable preview pipeline** | `[x]` | |
| 6c.1 | Dedicated preview slot, off the worker pool | `[x]` | `53bc98c` |
| 6c.2 | Cancellation: generation-aware, ordering-safe | `[x]` | `ae3219a` `593194e` `cf41efa` |
| 6c.3 | Settle delay before touching the disk | `[x]` | `593194e` |
| 6c.4 | Thumbnail cache, failures included | `[x]` | `2f4459c` |
| 6c.5 | Shell thumbnails before ffmpeg (`IThumbnailCache`; never writes `Thumbs.db`) | `[x]` | `1a5c096` |
| 6c.6 | PDF and Office documents preview as Explorer's thumbnail, where a handler is installed | `[x]` | `a932bab` |
| 6c.7 | Shell thumbnails on one dedicated STA thread: latest-wins, time budget, failures cached | `[x]` | `10e6819` |
| 6c.8 | Retry a timed-out video preview from a link: 10, 20, then 30 s | `[x]` | (this commit) |
| **6d** | **The root pane** | `[x]` | |
| 6d.1 | Sections, headers, cursor on header, `Space` collapses | `[x]` | `9082b8c` |
| 6d.2 | Drive labels ("Windows (C:)"), marks refused in the pane | `[x]` | `9082b8c` |
| 6d.3 | Favorites section (`SHGetKnownFolderPath`; labels qualified on collision) | `[x]` | `2ae7810` |
| 6d.4 | Pinned places (Ctrl+D pins, Delete unpins), persisted | `[x]` | `f76b684` |
| 6d.5 | Trash as its own group, enumerable via the shell namespace | `[x]` | `3a1d56c` |
| 6d.5a | ゴミ箱 usable: its own context menu, readable rows, real drive/bin icons | `[x]` | `0faeecd` |
| 6d.6 | Collapse state persisted | `[x]` | `7923fb9` |
| 6d.7 | Drive / share capacity preview | `[x]` | `a39b004` |
| 6d.8 | ISO mount on activate (eject: see roadmap 見送った案) | `[x]` | `5e7b402` |
| 6d.9 | Live refresh (`SHChangeNotifyRegister`) | `[x]` | `ef5d7a3` |
| **6e** | **Sorting, hidden files, cursor memory, rename** | `[~]` | |
| 6e.1 | Sort in `Transition` (modes + direction + dirs-first) | `[x]` | `6e646c0` |
| 6e.2 | Hidden-file toggle | `[x]` | `06ce820` |
| 6e.3 | Cursor memory (entry name, LRU-capped) | `[x]` | `cbc9505` |
| 6e.4 | Rename (overlay TextBox, `F2`) | `[x]` | `20cc44f` |
| 6e.5 | New folder (`Ctrl+Shift+N`) | `[x]` | `b43a6a1` |
| 6e.6 | Open with default app (`Enter`) | `[x]` | `b43a6a1` |
| 6e.7 | Path bar (full path of the cursor item) above a status bar of contextual key hints | `[x]` | `5180e8f` |
| 6e.8 | Sort mode: `S` then `N`/`E`/`S`/`M` (Shift = descending, repeat flips), `D` folders first; hidden files `Ctrl+Shift+.` | `[x]` | `5180e8f` |
| 6e.9 | Help screen | `[ ]` | **deferred by decision** - see 6e-7 |
| **6e-bis** | **Smooth cursor movement** | `[x]` | `f61c46b` `593194e` |
| **6g** | **Finder-style auto-extend**: the column beside the cursor | `[x]` | `75f84f9` |
| **6f** | **Findings parked from 6a–6e** | `[~]` | |
| 6f.1 | Settings store: injectable path, atomic save | `[x]` | `134fce0` |
| 6f.2 | `JobEngineTests` cancel race | `[ ]` | **when needed** (your call, 2026-09-18) - see 6f-2 |
| 6f.3 | Unguarded `Directory.Delete` in test cleanup (89, not 57) | `[x]` | `eb5ad3e` |
| **docs** | Stale plan facts, CLAUDE.md pointer | `[x]` | `9d55e8f` `06dde17` |

Deferred out of Phase 6 with reasons: reparse-point cycle prevention (coupled to Finder-style
auto-extend), VHD/VHDX mounting (needs elevation), the named-brush refactor (this design needs no new
colour). See "Deferred, with reasons".

---

# Records

Findings, reversals and open flags, kept so they are not lost between sessions.
`[ ]` = still open.

## Open

- **6c-7** `[x]` **Shell thumbnails, 6c.5 - and a measured `Thumbs.db` risk, closed three ways.**
  Explorer's own thumbnail is now tried before ffmpeg for video: no external tool, and whatever
  codecs Windows already has. Asked to be sure this never produces `Thumbs.db` anywhere, I measured
  before building rather than assuming.

  **The risk is real on this machine.** No policy disables `Thumbs.db` on network folders here, so
  Windows' default applies. Extracting a thumbnail for a picture reached through `\\localhost\C$` -
  a genuine SMB round trip - with no flags wrote `Thumbs.db` beside it (`files=[picture.png,Thumbs.db]`,
  `outFlags=WTS_CACHED`). With `WTS_EXTRACTDONOTCACHE` it did not (`files=[picture.png]`). Locally,
  neither did. The experiment cleaned up after itself, including the control's `Thumbs.db`.

  **That rules out the API the plan named.** `IShellItemImageFactory` has no do-not-cache option, so
  on a share it writes exactly that file. `IThumbnailCache::GetThumbnail` has one. Every GUID, flag
  and vtable slot was checked against `thumbcache.h` in the installed SDK: three bare Win32 constants
  this phase were valid-but-wrong, so memory was not good enough.

  Three independent guards, any one of which would do on its own:
  1. `WTS_EXTRACTDONOTCACHE` - the shell writes the result nowhere. This app keeps it in its own
     cache under %LOCALAPPDATA% instead.
  2. `ShellThumbnailer.IsPlainlyLocal` refuses a UNC path or a network drive letter from the string
     and `GetDriveType` alone, so refusing costs no network round trip (tested: under 2 s against a
     host that does not exist).
  3. The Runtime asks `GetFinalPathNameByHandle` about the handle already open for the head read,
     which sees through a link, junction, `subst` or mapped drive into a share. Anything it cannot
     classify counts as network.

  **Each guard fails a test when removed.** Deleting the flag fails
  `Even_a_network_path_gets_no_thumbs_db`, which runs the extraction through the loopback share on
  every check - past guard 2 on purpose, so that it tests the flag rather than the guard. Making guard
  3 always answer "local" fails both share tests in the Runtime. A thumbnail of a known green picture
  is decoded and its centre pixel checked, which is what proves the interface declarations: a method
  in the wrong vtable slot does not fail politely.

  **Scope, stated.** Video only - "before ffmpeg" is where ffmpeg is used. PDF and Office documents
  would get Explorer thumbnails from the same call instead of a hex dump; that changes what a preview
  *is* for those files, so it is a question for you rather than something done in passing
  (answered: do them - see 6c-8). And an
  ffmpeg failure verdict cached before this change still wins over the shell for that one file until
  the 30-day sweep; those are files ffmpeg rejected, where the shell most likely fails too.

- **6c-11** `[x]` **6c.8: a timed-out video preview can be retried, waiting longer each time.**
  Your direction: a link after the timeout message; three tries at 10, 20 and 30 s, then give up.
  - **Core** owns the attempts: `PreviewState.Attempt` and `TimedOut`, `CanRetry` while attempts
    remain, and `Msg.RetryPreview`, which reloads the same file as the next attempt under a new
    generation - so a late result from the try that timed out is discarded, not mistaken for this
    one. Moving to another file starts again at the first attempt. The wait per attempt,
    `PreviewState.WaitFor`, lives in Core too, so the wait the Runtime uses and the wait the link
    promises are one number.
  - **Runtime** reports a timeout as its own verdict, `ThumbnailFailure.TimedOut`, never cached -
    unlike a file ffmpeg rejects, which will not decode on a longer wait and gets no link.
  - **App** shows `再試行 (20 秒)`, then `再試行 (30 秒)`, then `30 秒待っても作れませんでした`. The link
    does not take keyboard focus, so clicking it leaves the arrow keys with the column browser.

  **Fixed along the way: the old 20 s timeout was really 40 s.** ffmpeg is tried at 3 s in, then at the
  first frame, and each run got the full timeout. The budget now covers the whole call, so 10 s
  means 10 s. That half is untested: forcing a real ffmpeg to stall on cue needs a fixture this
  suite does not have.

  **Evidence.** Core, Runtime and App each red first; the passes that came free with a stub were
  mutated to failure (6 mutations: a retry offered whatever the attempt, one offered without a
  timeout, the attempt count kept across files, every attempt given the first wait, every failure
  reported as a timeout, the link offered without a timeout). A test seam,
  `WorkerRuntime.CreateVideoThumbnail`, makes ffmpeg time out or reject on demand.

- **6c-12** `[ ]` **Manual check carried over from 6c-8: PDF and Office thumbnails with a handler.**
  6c-8 is closed, so this was easy to miss inside it. On a machine with Office, Acrobat or PowerToys
  installed: preview a `.pdf` and a `.docx`, confirm the page appears, and confirm the folder gains no
  `Thumbs.db`. Not possible on the development machine - no handler is registered for either.

- **6e-8b** `[ ]` **Sort keys reported as not working - the first traced run did not include an `S`.**
  Traced with `--debug-input` at `864a058` (2026-09-18): `Ctrl+Shift+.` arrived, resolved and
  toggled hidden files (58 to 76 entries in `C:\Users\user`), so the key path works end to end. The
  run holds no `S` keypress at all - only Alt, the arrows and `Ctrl+Shift+.`. If `S` was pressed and
  is absent, WPF never raised the key event, which points at the IME rather than the sort code; if it
  was not pressed, the next traced run settles it. Also still possible: the drive pane, where focus
  starts, is never sorted by design (`sortable=False` in the log) while its hint offers `S 並べ替え`.

- **6c-10** `[x]` **ffmpeg first, the shell as the fallback - 6c.5's ordering, reversed on measurement.**
  6c.5 put the shell first reasoning that an in-process call must beat spawning a process. Measured
  on real files that is simply not true here: ffmpeg won all six (205-624 ms against 282-722 ms), it
  needs no apartment and no installed handler, and it is the path that still works on a machine with
  no thumbnail provider for the type. So video now goes to ffmpeg first and falls back to Explorer.

  **One behaviour changed with it, deliberately: a remembered ffmpeg rejection no longer ends the
  preview.** It used to post the hex-dump fallback immediately. Now it means only "ffmpeg has had its
  turn on this exact file", and the shell is still asked - which is what "fallback" has to mean if a
  file ffmpeg cannot decode is ever to get a picture from Windows.

  **Red first:** a real video, built with ffmpeg's own `testsrc` (the test skips where ffmpeg is not
  on PATH, because a fabricated `.mp4` is rejected by ffmpeg and would let the shell win by default,
  testing nothing). Before the change the shell's 1x1 PNG won; after it, ffmpeg's frame does and the
  shell is never asked.

  Two bugs in my own fixtures, found while writing it: the video generator redirected ffmpeg's
  stdout and stderr without draining them and deadlocked on a full pipe buffer for 30 seconds; and
  the 6c.7 thread tests used fabricated `.mp4`s, which now sit behind ffmpeg and would have made the
  latest-wins timing test flaky - they use documents now, which reach the shell directly.

- **6c-9** `[x]` **6c.7 built: one STA thread, latest-wins, a 1.5 s budget, and failures remembered.**
  1. **`ShellThumbnailThread`** - one dedicated STA thread, one extraction in flight, at most one
     waiting; a newer request displaces the waiter and completes it with "nothing". Holding an arrow
     down now costs one extraction in flight plus one waiting, whatever the folder's size, where
     before nothing bounded it at all. The STA call is guarded by `OperatingSystem.IsWindows()`
     because Runtime targets plain net8.0 and apartments are a Windows notion.
  2. **Budget 1500 ms**, derived from the measurements above rather than taste: real extractions ran
     282-722 ms plus ~100 ms of COM start-up, so this sits above the measured worst case. An ordinary
     video therefore still shows its thumbnail directly instead of flipping from hex dump to picture,
     and only a pathologically slow handler falls back. Past the budget the preview posts what it
     would have shown anyway and the picture replaces it if it arrives - `Msg.PreviewLoaded` already
     carries the generation, so Core applies or discards it with no new machinery.
  3. **Failures remembered - reversing 6c-8 decision 3 - under their own marker.** A shell "nothing"
     writes `.noshell`, deliberately *not* the `.miss` that means "ffmpeg rejected this file":
     `ExecuteVideoPreview` consults that entry before ffmpeg runs, so storing a shell result there
     would report an ffmpeg rejection for a file ffmpeg had never been asked about and skip it
     entirely. A test holds that line. The 30-day sweep covers the new marker like any other entry,
     which is exactly what answers the original objection to caching failures.
  4. **An extraction superseded mid-wait still reaches the cache.** It is already running and cannot
     be stopped, so discarding its result would only mean paying for it again on the next pass.

  **Evidence: 783 tests green**, 7 new and 1 rewritten. Three were strict red-first - the STA test
  failed with `MTA`, the budget test posted `Image` first instead of falling back, and the
  missing-handler test asked twice. The four that passed on arrival were mutation-verified instead,
  5 mutations each failing at least one test: a fresh thread per request, a FIFO backlog in place of
  latest-wins, a shell failure stored as an ffmpeg verdict, and the late and in-budget empty results
  each left unremembered.

  **Not changed here:** ffmpeg's 20 s timeout (see below - it is ours, not ffmpeg's). The
  shell-before-ffmpeg ordering *was* changed, immediately after: record 6c-10.

- **6c-8** `[x]` **PDF and Office thumbnails, 6c.6 - built and tested, but inert on this machine.**
  A document with a PDF or Office extension is now offered to the same shell call as a video, before
  the text sniff and the hex dump. With a thumbnail it previews as that picture, labelled with the
  file's type; without one it previews exactly as before.

  **Nothing on this machine can produce one.** Measured before building, through the real
  `ShellThumbnailer`: a PDF, a .docx with an embedded `docProps/thumbnail.jpeg`, a .txt, an unknown
  extension and `notepad.exe` all came back `WTS_E_FAILEDEXTRACTION` (`0x8004B200`), and no file was
  written. No Office, Acrobat or PowerToys is installed, and this Windows 10 has no PDF or Office
  thumbnail handler that works here either. Also measured: when there is no handler the call **does not
  substitute a generic icon**, so no "thumbnail" here is ever just the type's icon. So the only
  end-to-end evidence is for the "no handler" path; the "picture" path is tested with an injected
  shell and has never shown a real document's page. **Wants a manual run on a machine with Office or
  a PDF thumbnail handler installed** - preview a .pdf and a .docx, and confirm the page appears and
  the folder gains no `Thumbs.db` (it cannot: same flag, same three guards as 6c-7).

  Decisions made in passing, each small:
  1. **An explicit extension list, not "anything that would otherwise be a hex dump".** Handlers are
     registered by extension, and a .docx is indistinguishable from any ZIP by content.
     Archives, executables, text and real pictures are never offered - tested.
  2. **Before the text sniff.** A PDF can be all ASCII and would otherwise preview as its own source.
     Tested with exactly such a PDF; moving the branch after the sniff fails the test.
  3. **A success is cached, a failure is not.** No handler is a fact about the machine: install
     Office and the next look should find it, not a remembered failure. Tested both ways.
     **Reversed by 6c.7**: failures will be cached with the same 30-day sweep as ffmpeg's, which
     answers the objection (an Acrobat install is picked up within a month) while stopping a
     handler-less PDF being re-probed on every cursor pass.
  4. **A thumbnail is labelled with its file's type** ("PDF Document", "MP4 Video"), not 画像ファイル,
     **and carries no resolution.** This corrected video too: since 6c.5 - and for ffmpeg thumbnails
     long before - a video's preview said 画像ファイル and gave the 512-wide thumbnail's size as the
     video's resolution. Showing none beats showing that; the real resolution needs the MP4 parsing
     still open in the roadmap.

  Every new Runtime and App test was mutation-checked: 8 mutations (branch order, share guard ignored,
  failure cached, every file a document, label dropped, pixel info restored, projection ignoring the
  label, label shown as a text body), each failing at least one test. The first attempt at the
  branch-order mutation survived - because the mutation itself was wrong, reinserting the branch
  before the sniff; corrected, it fails.

- **6f-6** `[x]` **Tests were writing into the user's real thumbnail cache.** Found while checking
  6c.5: `LoadPreview_of_a_video_extension_with_no_magic_match_is_routed_as_video` built a
  `WorkerRuntime` without injecting a cache, so every check run wrote a `.miss` entry into
  `%LOCALAPPDATA%\zurari\thumbs` - the newest entries there were 5-byte test files timestamped at
  check runs. Worse, all 68 test runtimes built without a cache each started a sweep of that real
  directory. The default cache is now created on first use, so a runtime that never previews a video
  never touches it, and that test has its own. Verified: a full Runtime suite run left the real cache
  at exactly the 145 entries it started with. The stray entries already there are left to the sweep.

- **status-2** `[x]` **Phase rows are now derived, and hashes come from the commit that added the row.**
  6d's own row read `[~]` and its section "half done" long after its ninth item landed - which is
  why "what's not finished in 6d?" had to be asked at all. `status.ps1` now derives each phase row
  from its items (done, partial or open), and `check.ps1` fails while one is wrong. Applying it
  corrected exactly 6d (`[~]` to `[x]`) and 6e (`[ ]` to `[~]`) and nothing else. Section prose is
  still written by hand, and was fixed by hand.

  The same pass found a latent bug in the hash fill: it only ran when the whole working tree was
  clean, so an unrelated modified file (`build.txt`) meant it would never fill a hash again. Each
  `(this commit)` is now filled with the commit that introduced that exact row, found with
  `git log -G`, which is right regardless of what else is dirty or how late it runs.

- **status-3** `[x]` **`status.ps1` corrupted both plan files under Windows PowerShell 5.1, and I
  committed them.** I ran it as `powershell` (5.1) rather than `pwsh` (7). 5.1 reads a BOM-less file
  as the ANSI code page - CP932 here - so every Japanese character in `phase6-basic-operations.md`
  and `roadmap.md` came back as mojibake and was written out that way, and `a932bab` committed both.
  Nothing caught it: mojibake is still valid UTF-8, and `check.ps1` only compares test counts. The
  source and test files in that commit were untouched (written by Python, not PowerShell). The
  pre-commit hook falls back to 5.1 on a machine without `pwsh`, so this was reachable without my
  mistake too.

  Fixed forward rather than by amending: the plans restored from `a932bab~1` with the 6c.6 changes
  reapplied, and `status.ps1` now reads and writes with an explicit UTF-8 encoding. A second bug of
  the same kind surfaced while verifying: the script itself has no BOM, so its literal katakana
  regex never matched under 5.1 and the roadmap's test count was silently never updated there. It is
  now `\u` escapes and the script is pure ASCII. **Verified under 5.1** against the restored plans
  at a stale 746: both counts became 776 and the Japanese text survived (`基本操作` still found in
  both files); the pwsh run agrees.

- **6e-1b** `[x]` **A `sed` of mine had stripped every blank line from `NaturalComparer.cs`** during
  6e.1. It compiled and passed the format check, so nothing flagged it. Restored; the diff is blank
  lines only, verified with `git diff --ignore-blank-lines -w` showing nothing else.

- **6e-4** `[x]` **Rename, 6e.4.** `F2` opens a text box over the row, Explorer-style. An **adorner**
  rather than an editable item template, as the plan called for: the list virtualizes with
  `Recycling` and picks rows with a template selector, so an editable template would have to survive
  both - and a recycled container could carry the editor onto an unrelated row. A plain `TextBox`
  also means IME composition needs nothing from us, which matters here more than anywhere else. The
  editor watches for `Key.ImeProcessed` so that Enter during composition belongs to the composition.

  **What Core decides and what it does not.** Empty names, unchanged names and names containing
  characters Windows forbids are refused immediately, with the editor still open and the text still
  there - the thing that needs fixing is what is on screen. Whether a name is already *taken* is not
  decided here: only the filesystem knows, and asking first would be a race. The Runtime checks the
  destination before moving, because `File.Move` would otherwise overwrite it, and for a rename that
  is silent data loss. Changing only the casing is a real rename, not a collision with itself.

  The editor is a projection like everything else: it exists because `AppState.Rename` says a row is
  being renamed. The control raises what the user did and Core decides what it meant. Its presence is
  tested against a realized window - a mistake in the adorner plumbing produces no build error and
  fails nothing else, it simply never shows. Verified by breaking it: all three tests fail.

- **6e-7** `[ ]` **The help screen (6e.9) is still deferred by your decision; the status bar is not.**
  "help is a feature we will add in the future so we dont want F1 now", "we dont want to show obvious
  hints". The status bar and the path bar came back in on your direction as 6e.7 - see 6e-8.

  **Corrected: there was never a mock-up in this plan.** This record used to say "the mock-up and the
  four zones are in this plan ready for when it is wanted". Searched when 6e.7 came back: no plan or
  doc contains one. It existed only in a conversation, and the record claimed otherwise. The layout
  that shipped is the one you gave directly - path bar above the status bar, key hints on the status
  bar.

- **6e-8** `[x]` **Path bar, key hints, and the keys sorting and hidden files were waiting for (6e.7, 6e.8).**
  Your direction: a path bar above the status bar showing the full path; key hints on the status
  bar; sorting as `S` then a field key, with Shift for descending; `S D` for folders first; hidden
  files on `Ctrl+Shift+.`; and incremental search opening on `/` as in zrr, so no letter is taken from
  it.

  **Sort is a mode, not a chord.** `S` opens it and it stays open while fields are chosen, so
  `S N N N` keeps flipping name order - the existing `SortOrder.Select` rule (choosing the field
  already in use reverses it) does the flipping. Esc closes it; any other key closes it *and* goes where
  it would have gone, so an arrow key leaves sort mode and moves the cursor in one press. The status
  bar shows the mode while it is open (`並べ替え: サイズ ↓ ...`), which is what makes a mode acceptable
  at all.
  - **Core owns the mode:** `AppState.InputMode`, `Msg.EnterSortMode` / `ExitSortMode`, and
    `Msg.SetSortDescending` for Shift - a destination, not a toggle. `EnterSortMode` is refused while
    a rename is being typed.
  - **App owns the keys**, in a pure `KeyMap` tested as a table. `MainWindow` asks it from a
    preview-key handler only while sort mode is open, so the column list cannot move the cursor first.
    `KeyMap` covers only these keys; the single command table the plan wants behind both key dispatch
    and the help screen is still 6e.9's.

  **One behaviour worth knowing, pinned by a test.** The default order is already name ascending, so
  the very first `S N` flips to name *descending* rather than selecting name. Kept, because it is the
  column-header rule and the arrow is shown - but `From_the_default_order_choosing_name_flips_it` is
  where to change it.

  **Hazards found while building it, each handled:**
  - Shift arrives as a key of its own before its letter. Closing the mode on it would have made
    `Shift+N` impossible to type; a lone modifier is ignored, and a test holds that.
  - The rename box handles only Enter and Escape, so every other key bubbled up to the window handler,
    which had no guard: `S` would have opened sort mode in the middle of typing a name. The window keys
    now stand down while an *editable* text box has focus - the read-only preview box keeps them - and
    Core refuses the mode during a rename as a second line.
  - With a Japanese IME on, a letter arrives as `Key.ImeProcessed` with the real key carried
    separately, so `S` would have done nothing. Keys are normalised before the map sees them.
  - **Suspected, not verified:** the old handler marked `Space` handled even with the rename box
    focused, which in WPF can cancel the character it types. If spaces were being lost from names
    while renaming, the guard fixes that; WPF text input could not be reproduced headlessly to confirm
    the bug existed.

  **The path bar** shows the full path of what the cursor is on - the selected item, as a Finder path
  bar does, not only the folder around it. A deleted file shows where it came from; a section header
  names its pane. "What is this row's path" moved to `Column.PathOf`, and `Transition` now asks it
  too, so the path bar and the rest of the app cannot drift apart. The count, marks and notice that
  used to follow the path moved with it into `StateProjection`, out of `MainWindow`, where that text
  had been computed untested.

  **Hints show only what this app adds** - `S 並べ替え`, `Ctrl+Shift+. 隠しファイル`, `Ctrl+D ピン留め` -
  leaving Explorer's own keys out as obvious, per 6e-7, and show nothing while renaming. The wording is
  yours to review.

  **Corrected along the way:** I suspected the column list would jump its selection on a typed letter
  and fight a bare `S`. Measured rather than assumed: `IsTextSearchEnabled` defaults to `False` for a
  WPF `ListBox`, and `ColumnView` reverts any selection to Core's cursor regardless.

  **Evidence.** Core: three sort-mode tests red first (entering and staying open read `Normal`; Shift
  gave `Name ↑`). The rename guard passed on arrival and fails with the guard removed. My first
  stays-open test asserted the wrong direction - the default is already name ascending - and became
  the pin above. App: 35 key-map and path/status-bar tests, 27 red first; the 8 that passed against
  the stubs were covered by 4 mutations, each failing (S ignoring modifiers, every letter opening the
  mode, an unrecognised key ignored instead of closing it, hints shown while renaming). **Not covered
  by any test:** the WPF wiring itself - which event asks the map, IME normalisation, the text-box
  guard - and a mouse click does not close sort mode. Those want a manual pass.

- **6e-56** `[x]` **Open with the default app (6e.6) and new folder (6e.5).** `Enter` on a file now
  opens it the way double-clicking would; before, it selected the file and did nothing else. An
  `.iso` still mounts instead, since opening a disc image means making it a drive.

  **The shell's own UI is deliberately left on for `open`**, unlike every other verb this app
  invokes. A file with no association is *supposed* to raise "how do you want to open this?", and
  suppressing that would turn a normal prompt into a keypress that appears to do nothing - the same
  failure mode as a silent eject.

  `Ctrl+Shift+N` makes a folder, Explorer's own binding. **The name is the Runtime's to choose**,
  because choosing it means looking at what is already there: 新しいフォルダー, then (2), and so on.
  Core refuses where there is no directory to create in - the drive pane holds places, the bin is not
  somewhere to put things - and says so rather than failing quietly. The cursor lands on the new
  folder through `RevealTarget`, which 6d.8 already built for the mounted-drive case.

- **6e-3** `[x]` **Cursor memory, 6e.3.** Coming back to a place lands on the entry you left it on,
  by name and never by index - an index means something different after a sort or a delete, and the
  point is to come back to the file rather than to the fourth row. Session-only: where you were an
  hour ago is a memory, not a preference.

  Three rules it needed. **Only the focused column records** - a column that opened beside the cursor
  has a cursor nobody chose, and recording it would overwrite where the user actually was. **Only a
  column with no cursor recalls** - re-reading a column someone is standing in must not jump them
  back to where they were earlier, which `WithAllEntries` already handles. And **an explicit reveal
  outranks the memory**: the drive a disc image was just mounted as is something asked for now.

  The cap is a checked invariant, as the plan required. Same trap as the auto-extend, hit again:
  recording on every `Apply` made a message that was supposed to be ignored return a different state,
  and the fix is the same - compare with what came before and do nothing when nothing moved.

- **6e-2** `[x]` **Hidden files, 6e.2 - and `SortOrder` became `ViewOptions`.** The Runtime reports
  `IsHidden` on every entry (hidden *or* system: Explorer treats them as one class behind one switch,
  and a listing that showed `pagefile.sys` but not a hidden folder would be explaining a distinction
  nobody asked about) and filters nothing. Core hides them, so revealing them reads nothing.

  The parameter list was the tell. `Derive` had grown to four arguments and each new filter would
  touch every call site, so what a listing is filtered and ordered by is now one `ViewOptions`
  record. `AppState.Sort` became `AppState.View`, and `Msg.SortOrderRestored` / `Effect.SetSortOrder`
  became `ViewRestored` / `SetViewOptions` rather than growing a third and fourth of each.

  Two details worth keeping. A **header is never hidden** - it is ours, not the filesystem's, and
  hiding one would take away the row that brings its section back. And a listing with nothing hidden
  in it still hands back the *same array instance*, which is what lets the projection skip rebuilding
  every row on a cursor move (6e-bis); there is a test pinning that.

- **6e-1** `[x]` **Sorting, 6e.1 - and the 6b invariant it broke.** Name (natural), extension, size
  and modified; direction; folders-first as a switch that composes with every mode rather than a mode
  of its own. Re-sorting reads nothing: the full listing is already in `AllEntries`, which is what
  that field was added for, so changing the order is instant on a network share or a directory with a
  hundred thousand files in it.

  **The subsequence invariant had to go.** 6b established that `Entries` is a *subsequence* of
  `AllEntries` - same objects, same relative order. Sorting is precisely the act of not preserving
  that order, so the property could not survive. Replaced with a subset check (by instance, no
  duplicates), which is what it was actually guarding: a visible row no read produced, or one row
  shown twice. A test now covers each half, plus one asserting that a reordered view is *not* a
  violation.

  **`Array.Sort` is an introsort and is not stable**, and my first version's comment claimed it was.
  Two rows that the mode and the name cannot tell apart would have come out in an order that varied
  with the length of the list. Sorting an index array with the original position as the final tiebreak
  makes stability a property of the comparison rather than of the algorithm; there is a test for it.

  Natural ordering is implemented here rather than calling `StrCmpLogicalW`: Core takes no dependency
  on the shell, and an order that changed with the OS version would make a listing untestable.

  **Deliberately not done.** Created, deleted-on and shell type name are listed in the plan as sort
  modes; each needs data `Entry` does not carry (a creation time, a bin-only field, a per-extension
  shell lookup). A member that ordered by nothing would be worse than its absence. And the plan's
  per-location-kind sort state is a single order for now - that map earns itself when a mode exists
  that only one kind supports, which is exactly those three.

  **No key binding.** Sorting has no way to invoke it yet, deliberately: keys are being decided as a
  set with the status bar and help screen, and Explorer's own affordance (a column header) does not
  exist in a miller-columns view. The mechanism, its persistence and its tests are done; the gesture
  is an open question.

- **R-1** `[x]` **Merged.** 43 commits fast-forwarded onto `main` at the end of 6d - the first point
  where the phase had a coherent stopping place: `Location` is in, the drive pane is finished, and
  the two test-reliability problems that were making the gate unreliable are fixed. A fast-forward
  rather than a merge commit, because `main` had not moved and the history here has been linear
  throughout. There is no remote, so nothing was published. Work on 6e continues on the same branch.

  (This record had been deleted by accident while I was rewriting 6f-4, and was restored when the
  merge went in - which is the second time this phase that an edit to one record quietly took a
  neighbouring one with it.)

- **status-1** `[x]` **The status tool's first use wrote the wrong hash, and it was my own bug.**
  `scripts/status.ps1` replaces a `(this commit)` placeholder with HEAD's short hash - but HEAD is
  the commit being described only *after* it exists. Run with a dirty tree, it filled 6d.9's row with
  the hash of the previous commit, which is exactly the kind of quietly-wrong record the tool was
  written to prevent. Corrected, and the script now refuses to substitute unless the working tree is
  clean, saying so rather than doing it silently.

  A second bug in the same tool, found the same way: it matched the placeholder as a *substring*, so
  the sentence above - which contains those words as prose - was itself a candidate for having a
  commit hash written into it. It now matches a whole table cell.

- **6d-17** `[x]` **Live refresh, 6d.9 - and `SHCNRF_NewDelivery` is 0x8000, not 0x1000.** The drive
  pane now notices a stick going in or a disc coming out without an F5.
  `SHChangeNotifyRegister` rather than `WM_DEVICECHANGE`: it reports a superset of the same events
  (media and shares as well as volumes), needs no device-interface registration, and is the shell's
  own view - so what it says appeared is what the pane is about to enumerate.

  **The end-to-end test earned itself immediately.** 0x1000 is `SHCNRF_RecursiveInterrupt`; asking
  for it instead of new delivery is completely silent - the registration succeeds, the notifications
  arrive, and every single one fails to lock, because they are being delivered in the old form where
  `wParam` is a pointer and `lParam` is the event id rather than a process id. A structural test would
  have passed. What found it was broadcasting a real event with `SHChangeNotify` and watching for it
  to come back: `lockfail(l=32), lockfail(l=256), lockfail(l=2)` - those numbers are event ids, which
  is what gave the game away. After the fix: `0x20, 0x100, 0x2`. Verified the test fails again with
  the wrong constant put back.

  Two supporting decisions. The event mask is deliberately narrow (drives, media, shares) - watching
  everything would wake the app on every file written anywhere on the machine to re-read a listing
  that had not changed. And the App coalesces notifications over 400 ms before re-reading, because
  inserting a disc produces several in a row and each re-read enumerates every drive, queries the bin
  and asks the shell for a display name per drive.

- **6f-5** `[x]` **`ThumbnailCache`'s constructor started a background sweep nothing could wait
  for.** It surfaced as the pre-commit hook rejecting an unrelated commit:
  `UnauthorizedAccessException` from a test's `Directory.Delete` - one instance of the parked 6f-3,
  but with a specific cause rather than a generic lingering handle. The constructor fires
  `Task.Run(Sweep)` (mine, 6c.4), so a test that made a cache and then deleted its directory was
  racing a sweep still walking it.

  The task is now exposed as `InitialSweep`. Nothing in the app waits for it - that is the point of
  running it off-thread - but a constructor that starts background work otherwise leaves the type
  with no quiescent point at all, which is not a property a type should have. The tests await it
  before deleting; 5 runs, 5 green.

- **6f-4** `[x]` **My diagnosis was wrong: there is no truncation, and no app bug.** I recorded that
  enumerating the bin under concurrent shell COM returned a short list, and put that in the roadmap
  as a hazard in the running app. It is not true, and the correction matters more than the finding
  did.

  What the evidence actually showed. Under four threads hammering `SHGetFileInfo`/`IShellItem`
  display names, the enumeration was **perfect**: 581 loops, 581 kept, no display-name failures,
  terminating on `S_FALSE`, matching `SHQueryRecycleBin` exactly. So concurrency does not truncate it.

  The real cause: **`ShellEffectExecutorTests` genuinely recycles a file** (`victim.txt`) to test
  `DeleteToRecycleBin`, and xUnit runs test classes in parallel - so it landed between the two
  `Enumerate()` calls that `RecycleBinFolderTests` was comparing. Proved directly rather than
  inferred: enumerate (581), recycle one file, enumerate again (582), difference is exactly
  `…\zurari-6f4-…\victim.txt`. Both classes were behaving correctly; the test was asserting that a
  shared machine-wide resource holds still.

  **What I got wrong, specifically.** I saw a strong statistical signal (5 failures in 6 with
  parallel collections, 0 in 6 without), reached for the most alarming explanation that fit, and
  wrote it up as fact without testing it. "Correlates with parallelism" is not "caused by concurrent
  COM" - and the cheap experiment that separated them took ten minutes.

  Fixed properly rather than papered over. The assembly-wide `xunit.runner.json` is gone; instead the
  two classes that share the bin share an xUnit collection, so only they serialize. And the test now
  compares each pass against `SHQueryRecycleBin` rather than against the previous pass, so a bin that
  legitimately changes mid-run moves both numbers together - verified non-vacuous by making the
  enumeration drop every tenth item, which fails it. 6 runs, 6 green.

- **6c-5** `[ ]` **The preview stall relief has no automated test.** Reproducing it needs a preview
  that genuinely blocks, which needs ffmpeg guaranteed present or a seam for injecting one. The
  guard test that exists says so in its own remarks. **Wants a manual run:** hold ↓ through a folder
  of large videos and confirm navigation stays responsive. **Do it on a fresh build — see 6c-6.**
- **6c-6** `[ ]` **`publish/` holds a two-week-old build, and instances of it were still running.**
  Diagnosed 2026-08-30: three `Zurari.App` processes were live at once — two from
  `publish\Zurari.App.exe` (built 2026-08-22 12:23, predating every Phase 6 commit) and one from
  `publish-sc\` (current). That one fact explained two separate symptoms: the drive pane showing no
  section headers, and `dotnet publish -o publish` failing with `GenerateBundle` →
  `IOException: being used by another process`. The running app was holding its own exe open.
  Nothing was wrong with the source or the build command.

  The stale exe also still contains the ffmpegthumbnailer code path (the literal appears 293 times as
  UTF-16 inside it; a plain `grep` misses it because .NET stores string literals that way), with
  `ffmpegthumbnailer.exe` beside it — which is what prompted the question.

  **Still open because the trap is still there.** `publish/` looks current and is not; it has already
  cost one debugging session. Deleting the directory removes the ambiguity, but that is the user's
  call. `publish/` is gitignored, so nothing here is tracked.

  **Standing rule for these records: every manual check must name which binary it ran.** An
  observation against `publish/` describes the app as it was on 22 August.
- **6d-4** `[ ]` **What in 6d still needs a person at the machine.** Rewritten 2026-09-11. The
  original said Trash enumeration and change notifications could only be checked structurally; both
  have since been tested against the real shell (enumeration agrees with `SHQueryRecycleBin`, and a
  real `SHChangeNotify` broadcast reaches the watcher). Eject is no longer ours - it is the context
  menu's. What genuinely cannot be exercised by a test on this machine:
  1. **Mounting a real `.iso`** by pressing Enter on it, and the cursor landing on the new drive.
     Tests cover the verb being registered and the refusal path, never an actual mount - that needs
     an image file and changes system state.
  2. **Live refresh with physical media** - plugging in a USB stick, inserting a disc. The test
     broadcasts the event; it does not produce it.
  3. **Unmounting through the context menu's 取り出し** on a mounted image.
  4. **Drive types this machine does not have** - a mapped network drive, a RAM disk - for their
     labels, icons and capacity panel.

  **2026-09-18:** under way in another session, starting with the AppData folders; mostly good so
  far, by your report.

- **6f-2** `[ ]` **`JobEngineTests.CancelJob_mid_copy_deletes_the_incomplete_destination_file` is
  racy.** It copies 50MB, waits for one `JobProgress`, then cancels; if the copy finishes first,
  `JobCancelled` never arrives and it times out. Fixing it means a larger fixture (slower every run)
  or a throttle seam in `JobEngine` - a design decision, not a tidy-up.

  **2026-09-18: deferred until needed, by your decision.** A second JobEngine flake seen the same
  day: `RunFileJob_posts_JobConflictsFound_then_skips_when_resolved_Skip` timed out once at 10 s in a
  full Runtime run, then passed in the two runs that followed. Different test, same family - a wait
  on a message that a busy machine delivers late.

- **6f-3** `[x]` **Unguarded `Directory.Delete` in test cleanup - 89 of them, not the 57 I counted.**
  A test that has finished asserting has already passed or failed on its own merits; cleanup throwing
  afterwards turns it red for a reason that has nothing to do with what it was checking, and lands
  the failure on whichever test happened to be running. 6f-5 was one of these caught in the act.

  `tests/TestSupport/TempDirectory.cs` is compiled into every test assembly through
  `tests/Directory.Build.props`. `Delete` retries five times with a growing wait and gives up quietly,
  clearing read-only attributes once on the way - a read-only file will not delete however long you
  wait, so waiting for it is the one case retrying cannot fix. All 89 call sites converted, and the
  twelve near-identical private `CreateTempDir` helpers now delegate to `TempDirectory.Create`.

  The helper has its own tests, and four of the six fail against a plain `Directory.Delete` - the
  locked-file and read-only cases especially, which are the ones that were actually biting. Full gate
  run three times, green each time.

## Closed

- **6d-6** `[x]` **Every place gesture was a no-op in the running app.** The composition root routed
  effects with a `switch` statement and `Effect.SetPinned` had no case, so it fell through and was
  silently discarded — Ctrl+B, Ctrl+D, Delete-to-unpin and drop-to-add all did nothing.
  `Effect.CancelPreview` was unrouted too, so preview cancellation never reached the runtime in the
  real app after 6c. **No test could have caught it:** every test submits effects straight to the
  executor it is about, so nothing exercised the router at all. Fixed in `76baaa1` — routing is a
  switch *expression* that throws, with a reflection walk over the (closed) `Effect` hierarchy
  asserting each one routes, plus a test that the walk itself is not vacuous.
- **6d-7** `[x]` **Four passing interop tests proved nothing.** The recycle-bin tests asserted "does
  not throw" and "empty implies empty" — all satisfied by `Enumerate()` returning `[]` from its
  catch block. A throwaway probe established the truth: 373 items in the bin, 373 returned,
  agreeing with `SHQueryRecycleBin`. The tests now assert both directions of that agreement.

- **6d-16** `[x]` **The eject key was reviewed away, and the drive rows were the real problem.**
  Reviewing the status-bar design turned up three corrections, all of them right.

  **`Ctrl+E` is withdrawn.** It is not an Explorer convention, and 取り出し is already in the shell
  context menu on any drive row - so a dedicated key bought speed alone, at the price of a key, a
  help line, and `Msg.EjectAtCursor` / `Effect.EjectDrive` / `Entry.IsEjectable` in Core. All removed;
  the reasoning is in the roadmap's 見送った案 so re-adding it is a decision rather than a rediscovery.
  Mount stays: opening an `.iso` is the existing "open this" gesture, not a new key.

  **"Windows (C:) は取り出せません" was the wrong answer to the wrong question.** An operation that
  cannot apply should not be accepted and then explained - it should not be offered. Moot now that the
  key is gone, but the principle stands for whatever replaces it.

  **The drive rows never said what the drives were.** `C:` and `D:` both rendered as a bare letter,
  so a fixed disk and a USB stick were indistinguishable in the pane - the type was visible only in
  the preview, and only for the one drive that happened to be *not ready*. The cause: an unlabelled
  volume has no name, and the code fell back to the letter. Explorer does not; it substitutes a type
  name. `ShellDisplayName.For` (`SIGDN_NORMALDISPLAY`, already listed as "Adopt" in the facilities
  inventory) now supplies it, injected into Runtime as a delegate for the same reason favorites and
  the bin are. Measured on this machine: `ローカル ディスク (C:)`, `USB ドライブ (D:)`,
  `BD-ROM ドライブ (E:)` - localized, per-machine correct, and better than the hand-written
  「E: (光学ドライブ)」 it replaces.

  **The title bar now shows only `zurari <version>`.** It was repeating the focused path, which the
  status bar already carries - a duplicate occupying the most visible line in the window.

- **6d-15** `[x]` **Mount and eject, 6d.8.** Opening an `.iso` invokes the shell's `mount` verb;
  `Ctrl+E` invokes `Eject` on the drive under the cursor. Verbs rather than APIs, deliberately: they
  are what Explorer's own menu uses and need no elevation, where `AttachVirtualDisk` (VHD/VHDX) needs
  an administrator and raw `IOCTL_STORAGE_EJECT_MEDIA` needs a volume handle and notifies nobody. A
  test reads the registry and confirms Windows still registers `mount` for `.iso` - the assumption
  the whole approach rests on, and one that would otherwise fail silently at the point of use.

  Three supporting pieces. `Entry.IsEjectable` comes from `DriveType`, so Core can refuse a fixed
  disk immediately and specifically instead of letting the shell refuse it silently - a mounted image
  reports as an optical drive, which is also how it gets unmounted. `AppState.Notice` gives the
  status bar a line for a result with no other visible outcome, cleared as soon as the cursor moves
  on; without it a refused eject is indistinguishable from a key that was never wired up, which is
  precisely the defect shape of 6d-6. And `AppState.RevealTarget` holds "put the cursor here once a
  listing containing it arrives", because the mounted drive does not exist until the pane is re-read
  - the same shape 6e.5's "the cursor lands on the folder you just created" will need.

  **Superseded in part by 6d-16**: the `Ctrl+E` binding and everything that existed only to serve it
  were removed on review. Mount, `AppState.Notice` and `AppState.RevealTarget` remain.

- **6g-1** `[x]` **The column beside the cursor now shows what the cursor is resting on.** Reported
  as "a long standing bug"; it was in fact the deferred Finder-style auto-extend, never built.
  `ReconcileChildColumn` is a common post-step of `Apply`, like `ReconcilePreview` and for the same
  reason - every message that can move a cursor or change focus has to be followed by it, and
  enumerating those per branch is how one gets missed.

  **Bounded to exactly one column beyond the focus, which is the whole safety argument.** The child
  loads asynchronously and its `DirectoryLoaded` runs the reconciliation again, so a rule of "extend
  wherever a cursor sits on a folder" would have each load trigger the next - a chain unrolling
  itself, and never terminating inside a junction that points at its own ancestor. Reading only the
  *focused* column means the grandchild's load extends nothing, because focus has not moved. There is
  a test for exactly that.

  Two further decisions. It reconciles only when the cursor's target actually **changed** between
  before and after, so a message that was ignored stays ignored rather than rebuilding the pane
  underneath it - without that, 51 existing tests failed, all of them correctly. And `EnterDirectory`
  now **moves into the column that is already showing the folder** instead of discarding a loaded
  listing and re-reading it, which would flash 読み込み中… over contents already on screen.

  The speculative read is marked as such on the effect and **debounced in the App exactly like a
  preview** (same gate, same 150 ms): a held-down arrow would otherwise put one directory listing on
  the worker pool per keystroke, which on a network share is precisely the stall the preview slot was
  separated out to avoid in 6c.

- **6g-2** `[x]` **The columns slid sideways whenever that child column came and went.** Reported
  from screenshots: moving the cursor off a folder onto a file scrolled the whole pane. Two causes,
  both of the same shape - the horizontal offset was a side effect rather than a decision.

  One: `ListBox.ScrollIntoView` on a cursor row is a bubbling `BringIntoView`. The list's own scroll
  viewer scrolls vertically and then passes it on, so it reaches the browser's horizontal scroll
  viewer, which scrolls sideways until that row is visible. *Any* column could therefore drag the
  pane - the child column arriving with a cursor remembered from an earlier visit, or a column
  reloading in the background - and where the columns ended up depended on which request arrived
  last: the same six-column state was measured settling at 431, 456 and 671.

  Two: losing a column shrinks the extent, and a `ScrollViewer` clamps its offset to whatever its
  content still justifies, pulling every remaining column across the screen.

  So the offset became one number `ColumnBrowser` owns, computed from the column widths rather than
  from requests arriving in some order (`ApplyHorizontalScroll`). Reveal the child column beside the
  cursor when it sits past the right edge; never scroll left except to bring the focused column back
  when a snapshot would strand it; and **reserve `offset + viewport` on the panel before the layout
  pass that drops a column**, so the offset stays legal - the space goes blank and the next child
  column to open takes it back. Rows no longer reach the browser's scroll viewer at all (a
  `RequestBringIntoView` class handler on `ColumnView`).

  **The first attempt was wrong and was reverted.** Suppressing the row requests alone did stop the
  movement - and stopped the child column ever being revealed, which is the whole point of 6g-1. The
  rule you chose instead: scroll right until it is visible, and never scroll back. Blank space on the
  right is acceptable; columns moving under the cursor is not.

- **6d-14** `[x]` **A not-ready drive said only that something had gone wrong.** It was reported as a
  failed preview, so an empty optical drive read as an error. It is not an error - it is an empty
  drive, and what kind of drive it is stays worth saying. `PreviewCapacity` gained a `Status` and
  nullable byte counts, so the pane shows 光学ドライブ / 準備できていません with no bar and no figures.
  `DriveType` needs no disc; `VolumeLabel` and `DriveFormat` would both throw there.

- **6d-12** `[note]` **The preview effect had to learn what it is previewing.** A drive's `C:\` is
  also a perfectly good directory path, so nothing in `Effect.LoadPreview(generation, path)` could
  say whether the question was "read this" or "how full is this". The row knows; the path does not -
  so the effect now carries a `PreviewTarget` (File / Volume / RecycleBin) decided where the row is,
  in `Transition`.

  Two consequences worth recording. **A pinned share gets a capacity panel, a folder inside it does
  not** - `\srv\share` is a volume, `\srv\share\sub` is a folder, and the test pins that
  distinction down. And **the bin's totals reach the Runtime through the delegate the composition
  root already supplies for its label**: they come from `SHQueryRecycleBin`, and Runtime may not
  reference Shell, so `TrashPlace` simply gained the two numbers rather than a new seam being cut.

- **6d-13** `[note]` **One P/Invoke now lives in Zurari.Runtime**, which had none. `DriveInfo` throws
  on a UNC path, so a share's capacity has no managed route at all; `GetDiskFreeSpaceEx` is what
  `DriveInfo` calls for a local volume anyway. Kernel32 and filesystem-only - shell interop stays in
  Zurari.Shell, and the layering tests still pass unchanged.

- **6d-11** `[x]` **Nothing was ever restored, because closing the window erased it.** Reported as
  "it doesn't restore on startup. favorites neither". Confirmed from the user's own
  `%APPDATA%\zurari\settings.json`: `"PinnedPaths": null`, in a session where a folder had definitely
  been pinned.

  `MainWindow`'s `Closed` handler saved `new UserSettings(PreviewWidth: …)` - a **fresh** record. The
  store writes the file wholesale, so every close threw away every pinned place and collapsed
  section, keeping only the pane width. The save half of persistence had tests; the restore half was
  only ever asserted as "the value reached the file", which is where the missing half hid.

  Three changes: `UserSettingsStore.Update(change)` does read-modify-write and is what every caller
  now uses; `Save` is **internal**, so no caller outside the store can erase the file by holding one
  value; and an end-to-end test runs three sessions over one settings directory - pin, restart,
  collapse, restart - asserting the pinned row and the collapsed section are both there on the way
  back. Nothing short of that test would have caught this.

  **The user's own pins are gone and have to be re-added** - the file recorded `null`, so there is
  nothing to recover.

- **6d-10** `[x]` **The bin's icon was a folder with a badge on it - `SIID_RECYCLER` is 31, not 32.**
  Reported as "ゴミ箱のアイコンは考えなおしてください". The stock-icon set is addressed by bare integers,
  so being one off is completely silent: the call succeeds and returns a perfectly valid icon of
  something else. A probe dumping ids 30-34 as PNGs settled it in one look.

  Fixed by dropping the stock set entirely and asking the shell about the **actual bin**:
  `SHParseDisplayName` on the CLSID, then `SHGetFileInfo` with `SHGFI_PIDL`. That route cannot be off
  by one, it draws full or empty by itself (verified: the returned icon matches `SIID_RECYCLERFULL`
  on a bin with 413 items in it), and it follows the user's theme. `SHGetStockIconInfo` and its
  struct are gone. The item count survives only as the icon's **cache key**, so emptying the bin makes
  the icon be looked up again. The test compares the bin's icon against the folder icon by pixels -
  which is precisely the assertion that would have caught the original mistake.

- **6d-8** `[x]` **Three things the drive pane got wrong, all reported from one screenshot.**
  1. **Every drive drew the generic folder icon.** `ShellIconCache` asked the shell for "an icon for
     something with the directory attribute", which is the folder icon by definition - so a fixed
     disk, an optical drive and a USB stick were indistinguishable. Drives now resolve from their own
     path, cached per drive. The test compares *pixels*, not references: two HICONs are never the
     same instance, so a reference check would have passed either way. Verified it fails without the
     fix.
  2. **The ゴミ箱 row had no context menu**, so ゴミ箱を空にする was unreachable. Its `Location` has no
     filesystem path, so `ResolveFullPath` returned `null` and the menu was skipped before anything
     was drawn. `Location.ShellParsingName` is the fix - the bin's CLSID (`::{645FF040-...}`), kept
     separate from `FilesystemPath` so a caller that wants a *path* still correctly sees nothing. A
     test resolves the CLSID through `SHParseDisplayName` for real.
  3. **The menu key did nothing anywhere.** Now `Apps` and `Shift+F10` open the same menu as a
     right-click, anchored under the cursor row (`ColumnBrowser.TryGetRowScreenRect`). Right-click
     and keyboard share one `ShowContextMenu`, so they cannot drift.
- **6d-9** `[x]` **The bin listing was a wall of identical truncated paths.** Rows were labelled by
  the *original* path whenever two labels collided - a rule borrowed from the drive pane, where
  collisions are rare. In a bin of 393 items nearly every name collides, so nearly every row became a
  path, end-trimmed by WPF down to the same `C:\Users\user\AppData\Local...`. Two changes: a row is
  now always labelled by its original **file name**, and the original **path** moved to
  `Entry.OriginalPath` -> `PreviewState.OriginalPath` -> the preview header, above the name.
  `TextTrim` (ported from rwf's `smart_truncate`/`shorten_path`) shortens from the middle;
  `TextFitter` finds the budget by bisection against the realized control's own typeface, since WPF's
  own trimming only cuts the end - the very thing that made the rows unreadable.

- **6d-5** `[x]` **Delete on a favorite would have recycled the folder it pointed at.** Favorites
  arrived in `2ae7810` as `EntryKind.Directory` rows in the Drives column, and `DeleteEntry` only
  refused `Drive` and `Header` — so Delete on ホーム resolved to the profile path and returned
  `DeleteToRecycleBin` targeting the whole of the user profile folder. Verified before fixing, then fixed in `621acf3`:
  the pane holds places, not files, so nothing in it is a deletion target. Delete now means
  "unpin" on a pinned row and nothing anywhere else in that pane.

- **6a-1** `[x]` `ShellEffectExecutor` keyed its "Operation was aborted" failure on `DestPath` while
  every other path keyed on the column. An aborted *row-granular* drop therefore failed the staleness
  check, was discarded, and left the column stuck in `LoadState.Loading`. Pre-existing; surfaced by
  the type change. Fixed in `2b74c1a`.
- **6a-2** `[x]` **The plan was wrong about `NormalizePath`.** I had recorded its `TrimEnd` as a latent
  bug. Tracing every call site showed it is comparison-only and that the `C:\`→`C:` collapse is what
  makes root equality work — "fixing" it would have broken documented behaviour. Renamed to
  `PathComparisonKey` so it cannot be misused instead.
- **6a-3** `[x]` **I deleted five files from version control by accident.** A stray
  `git rm --cached` while investigating left the removal staged and `9d55e8f` swept it up, deleting
  `docs/superpowers/plans/` (385 lines) unmentioned — while that same commit message and my report
  claimed the files were being left alone. Restored byte-identical in `06dde17`; branch audited for
  other deletions, there are none.
- **6b-1** `[x]` A test fixture with **two entries both named `a.txt`** forced mark identity from
  name to **instance**. Names are unique in a directory listing but would not be across a
  `SearchResult` location gathering hits from several directories.
- **6c-1** `[x]` **`CancelPreview` ignored its generation**, so a cancel executing after the load it
  was meant to precede killed the new preview — pane loading forever. Mine, introduced `53bc98c`,
  fixed `ae3219a`. Found only as an intermittent test timeout.
- **6c-2** `[x]` **The invisible gate flake was `UCEERR_RENDERTHREADFAILURE`** — the WPF compositor
  dying and taking the test host with it. The run aborts mid-assembly, *no test fails*, every summary
  prints 成功, and `dotnet test` still exits non-zero. Fixed by software rendering in the test host
  (`a7877d1`); production still renders on the GPU.
- **6c-3** `[x]` Two more ordering races, both mine: the CTS was disposed while its task still read
  the token (`WaitHandle` throws once disposed, unlike `IsCancellationRequested`), and
  `StartPreview` could run out of order and let a stale generation win. Fixed `593194e`.
- **6c-4** `[x]` `Dispose`'s `_previewCts?.Dispose()` was **unreachable dead code satisfying CA2213
  with a no-op**, and its comment asserted the opposite of the only case it claimed to cover.
  Removed `cf41efa`, suppression made explicit instead.
- **6d-1** `[x]` **Headers moved into Core, reversing the projection-only decision** — because the
  cursor lands on them and `Cursor` indexes `Entries`. Costs a new `EntryKind` across 88 sites; saves
  `EntryVm.CoreIndex` and its translation layer entirely.
- **6d-2** `[x]` **`AllEntries` shrank along with the view.** It falls back to `Entries` when its
  backing field was never set, so a bare `with { Entries = … }` silently redefined "everything".
  Caught by the collapse tests; `Column.WithView` is now the only writer of the three fields.
- **6d-3** `[x]` Pins and collapse persistence blocked on 6f.1, which was pulled forward and done.
- **6e-bis-1** `[x]` The double `ScrollIntoView` measured **0.7–0.9 ms** per move and was
  **deliberately left alone** — it is a documented fix for scrolling stalling at the viewport edge
  (`43d6358`, `16f2283`), and under a millisecond is not worth risking a visible bug.
- **6e-bis-2** `[x]` `RenderJobs` is O(jobs), normally zero; `RenderPreview` guards image decoding by
  generation and only formats a hex dump for a `Binary` preview, which a moving cursor never shows.
  Measured negligible, no change.

---

# 6a — Location foundation (was Phase 8) — **DONE** (`2b74c1a`)

**Evidence:** `scripts/check.ps1` → `ALL CHECKS GREEN`, run by the pre-commit hook. **471 tests,
0 failed** — Arch 7, Gallery 21, Runtime 44, App 78, Controls 106, Shell 22, Core 193. Same count as
before the change: no test added, removed, or skipped.

**Behavioural equivalence held.** Every line added to a test names a `Location`; no assertion changed
what it checks. The one restructured assertion (a tuple compare split into four scalar compares)
verifies the same four values.

Two defects surfaced by the type change and fixed in the same commit:

- `ShellEffectExecutor` keyed its "Operation was aborted" failure on `DestPath` while every other
  path keyed on the column. `Msg.ShellOpFailed` is matched against the column's own location, so an
  aborted *row-granular* drop failed that check, was discarded, and left the column stuck in
  `LoadState.Loading`.
- `NormalizePath` turned out to be comparison-only, never used to build a path. Its `TrimEnd` maps
  `C:\` to `C:` — which is load-bearing for making `C:\` and `C:` compare equal, not the bug the plan
  assumed. Changing the trimming would have broken that; renamed to `PathComparisonKey` instead so
  it cannot be misused as a path producer.

## Why first

Child paths are built by concatenation — `Path.Combine(column.Path, entry.Name)`
(`Transition.cs:137`). Everything awkward downstream traces to that line: `CheckInvariants` must
enforce `Columns[i].Path.StartsWith(Columns[i-1].Path)` (`AppState.cs:249`); favorites, pins and
Trash point *outside* their parent and so fight that invariant; and `""` means "drive list" in
**19 places** (`plan/phase8-virtual-locations.md` claims one — it is wrong). zurari hit the same wall
and used a `::DRIVES::` sentinel string (`..\zurari\Program.cs:6137`) — the hack `Location` replaces.

**`Entry.Target: Location`** removes all three at once: a row carries where it opens to, which need
not be under its parent. The `StartsWith` invariant is **deleted, not preserved**.

```
Location = RealDirectory(path)
         | Drives                      // the root pane
         | RecycleBin                  // Trash
         | Archive(archivePath, inner) // Phase 9
         | SearchResult(query, roots)  // later
```

## Steps

1. `Location` as a record union next to `Msg`/`Effect`. Add `Entry.Target`.
2. Migrate `Column.Path` → `Column.Location`, `Effect.ReadDirectory`, and title generation in
   `StateProjection` (from the `Location`, not by parsing a path). All 19 `""` sites go.
3. **Delete the `StartsWith` invariant**; a child column's `Location` is whatever the parent row's
   `Target` said.
4. `DirectoryWatcher` narrows to `RealDirectory`. Virtual locations are not watchable.
5. Fix `NormalizePath` (`Transition.cs:477`) — `TrimEnd('\\','/')` maps `"C:\"` → `"C:"`, which
   Windows reads as *relative*.

Verification is behavioural equivalence: the 471 tests migrate for types only. Any test whose
*assertions* change means something moved that should not have.

---

# 6b — Column data model: raw list, derived view — **DONE** (`2cfbfee`)

**Evidence:** `check.ps1` green via the pre-commit hook. **491 tests, 0 failed** (5 new in Core).
All pre-existing tests pass unedited.

Built: `Column.AllEntries` with `Entries` derived from it (identity for now); marks recorded on
`AllEntries` and identified **by instance**, not by name — deriving carries the same `Entry` objects
across, so reference identity assumes nothing about name uniqueness, which a `SearchResult` location
would break. New invariant: `Entries` is a subsequence of `AllEntries`.

**One deliberate behaviour change:** re-reading a directory keeps the cursor on the same *entry*
rather than the same index, falling back to the index only when the entry is gone. Previously a file
appearing or disappearing above the cursor silently moved it onto a different file — which then
became what the next Delete or Enter acted on.

**Not yet exercised by the property tests:** no `Msg` produces a narrowed view, so `GenMsg` cannot
reach one. The subsequence invariant becomes load-bearing when collapse lands in 6d and its toggle
joins `GenMsg`.

**Was a prerequisite for 6d, not just for 6e.** Collapsible sections hide rows, and hiding rows
through the projection alone desyncs the cursor from `Column.Entries` — the same defect the
hidden-file toggle would have caused. Collapse becomes one more filter feeding the derived list, so
it needs this model first. Order is therefore **6b → 6d**.


**Corrected from the previous draft.** Filter belongs in `Transition` alongside sort, not in Runtime.
Making one pure and the other a disk re-read was an inconsistency with no justification — and a
filter change needs no data the app does not already hold.

```
Column {
  Location
  AllEntries      // exactly what the last read returned
  Entries         // derived: AllEntries filtered, then sorted
  Cursor          // indexes Entries — "what can be viewed is the source"
  ScrollOffset
}
```

Both the hidden-file toggle and any sort change become pure, instant transitions. No re-read, no
worker, no spinner.

**Consequences to handle:**

- `CheckInvariants` checks `Cursor` against `Entries`, and additionally that `Entries` is a
  subsequence of `AllEntries`.
- **Re-sorting silently moves the cursor** — `Cursor` is an index. Fixed by the principle you gave
  for cursor memory: capture the cursor's entry *name*, re-derive, find the name again. Same for
  `ScrollOffset`.
- **Marks on filtered-out entries — settled: visible marks only.** Marks live on `Entry`, so hiding a
  marked file keeps its mark alive but invisible. Operations act on the **visible** list only, so a
  hidden marked file is never copied or deleted, and unhiding brings its mark back rather than
  discarding the user's selection. Consistent with "what can be viewed is the source", and it means
  the invisible-file-in-a-bulk-delete hazard cannot occur.

## Sort modes — the enumeration that was missing

| Mode | Key field | Notes |
|---|---|---|
| Name | `Name` | **Natural/numeric ordering is in** — `file2` before `file10`. Confirmed, not optional |
| Extension | derived from `Name` | Ties to name as secondary |
| Size | `SizeBytes` | Directories are `-1`; where they land is a sub-decision |
| Modified | `Modified` | Present today |
| Created | **missing** | `Entry` has no `Created` field. Adding it costs a `FileInfo` read already being done |
| Deleted-on | **missing** | Recycle Bin only; a distinct field the shell supplies |
| Type | shell type name | **Effectively free** — see below |

Plus **direction** (asc/desc) and **directories-first**, which per your correction is a *mode that
composes*, not a permanent grouping.

### Sorting by "type" — which type, and what it really costs

The instinct that a filer is fast because it reads names and touches attributes only when needed is
right. For *this* comparison it cuts the other way, which is worth stating plainly:

| | Input it needs | Touches each file? | Cost profile |
|---|---|---|---|
| **Our `FileTypeDetector`** | the file's first bytes | **Yes — open + read every file** | Ruinous when sorting a directory; fine for the one file being previewed |
| **Shell `SHGFI_TYPENAME`** | path string + attributes | **No** | Registry association lookup per *distinct extension*, then cached |

`FileTypeDetector` is magic-bytes only and takes a byte array, not a path — its own doc comment says
it "has no extension-based fallback (Core never sees a path here)" (`FileTypeDetector.cs:34`). So
sorting a column by our type means opening and reading the head of **every file in it**. That is the
disk access to avoid, and it is on our side, not the shell's.

`SHGFI_USEFILEATTRIBUTES` means the shell must *not* access the file — it answers from the path
string and the attributes passed in. Not literally free: it is a registry lookup, and for some types
the shell may load a handler DLL, so the first call per extension costs milliseconds. But it is
per-extension and cached, not per-file.

**And we already pay it for every entry.** `StateProjection.Project` invokes the icon resolver in
`ProjectEntry` for *every* entry in a column, not just visible rows (`MainWindow.xaml.cs:713`), and
`ShellIconCache` makes exactly this call keyed by extension (`ShellIconCache.cs:35`, `:82`). Adding
`SHGFI_TYPENAME` is one more flag on a call already being made.

**Proposal — split sort key from displayed label:**

- **Sort by the shell type name.** Filename-only, already-cached, no per-file I/O.
- **Keep our detector for the preview** and for the extension-mismatch warning, where the head has
  already been read for the preview anyway. It is the better answer where we can afford it.
- A shell-primary / ours-fallback arrangement is the wrong way round: ours is the one that degrades
  on a slow drive, because it opens files.

**Coupling to note:** this cheapness depends on icons being resolved for every entry. If first-load
cost on a huge directory ever forces icon resolution to become lazy/visible-only, sort-by-type
becomes a real cost again and would need its own extension-keyed pass.

## Where sorting applies

| Location | Sortable? |
|---|---|
| `Drives` (root pane) | **No.** Curated order — sections and their fixed contents. A user sort would fight the headers |
| `RealDirectory` | All modes |
| `RecycleBin` | Name / size / deleted-on |
| `Archive` (Phase 9) | Name / size / modified |
| `SearchResult` (later) | Path / relevance |

So sort state is **per-`Location`-kind**, not global — and the root pane simply has none.

---

# 6c — Cancellable preview pipeline — **done**

**Done:** dedicated single-slot preview executor off the worker pool; `Effect.CancelPreview` emitted
by `ReconcilePreview` when a load is abandoned; `CancellationToken` through the head read and
ffmpeg's wait loop (kills the process rather than waiting out its budget); poll interval 4s → 250ms.

**Evidence:** `check.ps1` green via the pre-commit hook. **477 tests, 0 failed** (6 new: 3 Core for
cancel emission, 3 Runtime for cancel handling and slot reuse).

**Verification gap, stated plainly:** the stall relief itself has no automated test. Reproducing it
needs a preview that genuinely blocks, which needs either ffmpeg guaranteed present or a seam for
injecting a slow preview; a text preview completes in microseconds so the guard test would have
passed before the fix too. The test says so in its own remarks. **Still wants a manual run**: hold ↓
through a folder of large videos and confirm navigation stays responsive.

**Thumbnail cache — done** (`2f4459c`). Key = path + last-write + length; failures cached too, but
*only* verdicts ffmpeg actually reached (`ThumbnailFailure.FileRejected` vs `Unavailable`), so a
missing ffmpeg or a timeout is never remembered against a file. Temp-file-then-move so a crash
cannot leave a half-written entry. Sweep: 30 days, then oldest-first to 256MB, off-thread from the
constructor. Directory injectable.

**A defect in 6c part 1, found and fixed** (`ae3219a`): `CancelPreview` carried a generation that the
runtime ignored, cancelling whatever was current. Since `ReconcilePreview` emits cancel+load together
into a channel drained by several workers, the cancel could execute *after* the load and kill it —
leaving the pane on 読み込み中… forever. Caught only as an intermittent test timeout.

**6c.5 landed** - shell thumbnails before ffmpeg. See record 6c-7 for why it is `IThumbnailCache` rather than the `IShellItemImageFactory` named below.

**6c.6 landed** - PDF and Office documents through the same call (record 6c-8), inert on this machine
until a thumbnail handler is installed.

**6c.7 landed** - the COM call is synchronous and uncancellable, so it now has one STA thread, a
bound on how many extractions can be in flight, and a time budget. Design below, outcome in record
6c-9.

## What is actually broken

Not the spinner — `PreviewKind.Loading` renders 読み込み中… (`MainWindow.xaml.cs:865`). Two gaps:

1. **Nothing cancels in-flight work.** `ReconcilePreview` bumps the generation and discards the stale
   result, but the worker runs to completion. `Effect.CancelJob` exists; `CancelPreview` does not.
2. **Preview starves navigation.** `workerCount: 2` (`MainWindow.xaml.cs:87`) and
   `TryCreateThumbnail` blocks up to 20s in `WaitForExit` (`VideoThumbnailer.cs:165`). Two videos
   occupy both workers and `ReadDirectory` queues behind them.

Cancellation is *downstream* of the starvation fix: while both workers block, a newer `LoadPreview`
cannot be dequeued, so a cancel signal has nothing to act on.

## Approach

- **Dedicated single-slot preview executor**, separate from the effect pool; a new request cancels
  the one in flight. `ReadDirectory` can never queue behind a preview.
- `Effect.CancelPreview`, emitted by `ReconcilePreview` — Core stays authoritative about what is abandoned.
- `CancellationToken` through the file open, head read, and `VideoThumbnailer` (async wait + kill).
- **Shell thumbnail first, ffmpeg as fallback** (see inventory).
- **Thumbnail cache** for the ffmpeg path only, keyed on path + `LastWriteTimeUtc` + length per
  zurari's `GetVideoCachePath`. **Cache failures too** — a corrupt `.mp4` otherwise re-spins for the
  full timeout on every cursor landing. `%LOCALAPPDATA%` means owning eviction: a size or age budget.


## 6c.7 — one dedicated STA thread for shell thumbnails, latest-wins, with a time budget

**Built; see record 6c-9 for what shipped and the evidence.** The design below is as it was agreed,
kept for the reasoning.

**Required, not optional**, and it applies to video (6c.5, shipped) exactly as much as to documents
(6c.6). Your verdict: *"seems unreliably slow, com calls should be separated, and only single."*

### What is actually wrong - corrected from the first telling

The premise this started from was that the COM call shares the two-worker pool with directory
listings, so listings queue behind extractions. **That has not been true since 6c.1** (`53bc98c`):
`Effect.LoadPreview` is the one effect that does not execute on the pool. A pool worker calls
`StartPreview` and returns immediately, so `ReadDirectory` is never stuck behind a thumbnail.

The real defect is worse in a way that matters more. `StartPreview` cancels the previous preview's
token and starts the next one **without waiting for it**, and a `CancellationToken` cannot reach
inside a synchronous out-of-process COM call. So the "single slot" is single only in its bookkeeping
(`_previewCts`, `_previewGeneration`): whenever the work is sitting inside `_shellThumbnail(...)`,
every new cursor landing past the 100 ms settle delay starts *another* task. Arrowing through a
folder of documents can leave many extractions in flight at once, on MTA thread-pool threads, all
but the last discarded by the generation check. Nothing bounds that number today.

Two consequences, both of which this item fixes:

1. **Unbounded concurrent extractions**, each holding a thread-pool thread for as long as the shell
   takes. That is the "unreliably slow" that prompted this.
2. **MTA.** Thread-pool threads are MTA, so an STA-only thumbnail provider - which is the common
   kind - is marshalled to an apartment the shell chooses for us. It works; it is not worth
   continuing to bet on.

### Design, agreed

- **One dedicated STA thread inside `WorkerRuntime`, own queue, latest-wins.** One extraction in
  flight, at most one waiting; a newer request replaces the waiter. Holding an arrow down then costs
  one extra extraction in total, not one per row. Video moves onto the same thread - the 6c.5 tests
  should cover it unchanged, which is the check that this is a move and not a rewrite.
- **A time budget of N ms.** The COM call cannot be cancelled, but the preview need not wait for it:
  past N, post the text/binary preview immediately, and post the thumbnail later *if* it arrives
  while that generation is still current. `Msg.PreviewLoaded` already carries the generation, so a
  late arrival is applied or ignored with no new machinery.
- **Cache failures, ffmpeg-style, swept at 30 days.** Otherwise a handler-less PDF is re-probed on
  every cursor pass. **This reverses decision 3 in record 6c-8** ("a success is cached, a failure is
  not"): with a 30-day sweep an Acrobat install is picked up within a month rather than never, which
  was the whole objection to caching failures.

### N comes from measurement, not from taste

A temporary `[StaFact]` in `Zurari.Shell.Tests` - which already has STA, the references and
`TempDirectory` - with `ITestOutputHelper`, run via `dotnet test --filter` with
`-l "console;verbosity=detailed"`, then **deleted**. Five iterations per case, the first call
reported separately from the rest, and a fresh process per case: a warm shell hides exactly the cost
being guarded against.

Cases: a minimal valid `.pdf` · a `.docx` with `docProps/thumbnail.jpeg` · a `.png` (control - proves
the stopwatch is seeing real work) · an `.mp4` (the existing `VideoFile` bytes) · a
`.zurari-unknown` (the floor cost of a refusal).

Two numbers decide it: what a `0x8004B200` refusal costs, and whether any case has a long tail. They
set N, and they say whether ffmpeg's video fallback needs the same budget.

### That measurement, run 2026-09-12 — and what it settles

Exactly that protocol: five cases, five iterations each, a fresh process per case, against the real
`ShellThumbnailer`; the `[StaFact]` was deleted afterwards. Milliseconds:

| Case | First call in the process | The other four | Result |
|---|---|---|---|
| `paper.pdf` | 103.5 | 7.6 - 9.3 | refused |
| `letter.docx` (ZIP header) | 99.0 | 7.8 - 9.0 | refused |
| `picture.png`, 400×300 (control) | 116.1 | 17.3 - 20.0 | 1192-byte PNG |
| `clip.mp4`, 12 bytes | 166.6 | 10.7 - 11.7 | refused |
| `data.zurari-unknown` | 74.1 | 8.0 - 10.4 | refused |

**A refusal costs about 8-10 ms, and there is no long tail** - the spread across four warm calls is
under 2 ms in every case. The 74-166 ms first call is COM and surrogate start-up, paid once per
process, not per file. The control did real work (it returned a PNG, and cost about twice a refusal),
so the stopwatch is not measuring nothing.

A first reading of this set N at about 200 ms and called 6c.7 "not urgent". **Both were wrong, and
the next section says why** - these files were synthetic, and synthetic files measure refusals.

### Real files, the same day - and the correction they force

A 60-byte "PDF" and a 12-byte "MP4" are malformed, and a handler refuses them on sight whether or not
it is installed. So the probe above was timing almost nothing. Repeated against real files - 8 PDFs
of 7-59 MB, and 6 MP4s of 849 MB to 1.4 GB on a **removable USB drive**:

| Case | Size | Cost | Result |
|---|---|---|---|
| PDF × 8 | 7.4 - 59.1 MB | 85.1 ms first call, then **5.4 - 6.2 ms** | all refused |
| MP4 × 6 | 849 - 1390 MB | **282 - 722 ms each** | **all succeeded**, 140-300 KB PNG |

Two facts, pointing opposite ways:

1. **A refusal is flat in file size.** A 59 MB PDF is refused as fast as a 7 MB one, because the shell
   answers from the registration without reading the file. And the registration is genuinely absent:
   `.pdf` has no ProgID at all here, and no `IThumbnailProvider` under any of the four places one can
   be registered. 6c-8's "inert on this machine" is now confirmed on real documents, not just a stub.
2. **The success path is the expensive one - and it is the path this machine actually takes.** `.mp4`
   resolves to Windows' own *Property Thumbnail Handler*
   (`{9DBD2C50-62AD-11D0-B806-00C04FD706EC}`), and every real video produced a thumbnail in
   **282-722 ms**: 30 to 70 times the 100 ms settle delay, and fifty times what the synthetic file
   implied.

**So 6c.7 is urgent, not optional-but-nice.** Arrowing through a folder of videos starts a ~500 ms
uncancellable extraction per landing with nothing bounding how many run at once - which is exactly
the "unreliably slow" this item started from. The bound is the point of it.

**N is not 200 ms.** A 200 ms budget would fire on every real video here, so every video preview would
show a fallback first and flip to the thumbnail half a second later. Either N sits above the honest
success range (~1 s, firing only on a pathological handler) or it is per-kind. A decision for when
6c.7 is built, but now with data under it.

**The cost is once per file** - our own `ThumbnailCache` answers the second landing - so this is the
first pass through a folder, not a standing tax.

**Nothing was written.** Neither directory had a `Thumbs.db` before or after, and no file of any kind
appeared in either (checked by modification time, not only by name). The three guards hold against a
real user folder and a real removable drive.

**ffmpeg, on the same six files**, through the app's own `VideoThumbnailer` so the arguments are the
real ones rather than my approximation of them (ffmpeg 8.1.1, on `PATH`):

| File | Shell (`IThumbnailCache`) | ffmpeg (`-ss … -frames:v 1 -vf scale=512:-1`) |
|---|---|---|
| 1389.9 MB | 721.8 ms | 623.5 ms |
| 1078.1 MB | 536.6 ms | 296.3 ms |
| 985.5 MB | 490.7 ms | 253.7 ms |
| 936.7 MB | 282.4 ms | 205.2 ms |
| 867.1 MB | 635.9 ms | 370.9 ms |
| 849.1 MB | 463.6 ms | 316.0 ms |

**ffmpeg was faster on all six**, process spawn included, by roughly 1.5×. **Caveat, stated because
it matters:** the shell ran first, on cold files, and ffmpeg second, so the OS file cache favours
ffmpeg in this comparison; a fair rematch alternates the order. What survives the caveat is that the
two are the same order of magnitude - which is the fact 6c.7 needs, and it is not the fact 6c.5
assumed ("no external tool" implied the shell was the cheap path).

So **the ordering is a question again, not a settled one**: shell-first is still right where ffmpeg is
absent, ffmpeg-first looks faster where it is present, and deciding properly needs the case the
ordering exists to survive - no ffmpeg *and* a slow third-party handler - which this machine cannot
produce. **It changes nothing about 6c.7**: both paths are 200-700 ms, so the bound and the budget
are needed whichever goes first, and N must sit above ~700 ms for video, or be per-kind.

Nothing was written for this run either: `D:\iv` still holds exactly its 11 files, no `Thumbs.db`,
and no temp PNG outlived the run.

**The PDF preview visible in Explorer is a different interface, and it cannot feed ours.** `.pdf` here
has a *preview handler* - `{3A84F9C2-6164-485C-A7D9-4B27F8AC009E}`, "Microsoft PDF Previewer",
registered under `IPreviewHandler` (`{8895b1c6-…}`) - which is what fills Explorer's preview pane, and
is why a PDF looks previewable there. It has no `IThumbnailProvider`, the interface `IThumbnailCache`
asks and the only one that hands back a bitmap. A preview handler renders into an HWND instead, so it
can never be an image in our pane; using it would mean hosting its window, which is a feature for the
roadmap rather than a fix here. The file list corroborates the split: those PDFs carry the generic
red icon while the videos carry frames. (Default PDF app here is Firefox; Acrobat is what would add
the missing thumbnail provider.)

**Still unmeasured:** a document thumbnail handler doing real work, which this machine cannot show.

**Asked whether to remove the feature or put it behind a setting; still no, but for a better reason.**
A setting does not make 500 ms shorter - it makes the user responsible for it, having first asked them
to know what a thumbnail handler is. Removal would not even cost much *here* (ffmpeg is on `PATH`, so
video would fall back to it) but it would lose thumbnails wherever ffmpeg is absent, and trade an
in-process call for a process spawn of unmeasured cost. The 500 ms is an argument for bounding the
call, which is 6c.7, not for a switch in front of it.

### Standing context for 6c.6

No PDF or Office thumbnail handler is registered on this machine - probes of a PDF, a `.docx`, a
`.txt`, an unknown extension and `notepad.exe` all returned `WTS_E_FAILEDEXTRACTION` (`0x8004B200`),
with no icon fallback and no files written. 6c.6 is **inert here** until Office, Acrobat or PowerToys
is installed; records say so plainly rather than implying a visible change. All three no-`Thumbs.db`
guards stay as they are: `WTS_EXTRACTDONOTCACHE`, `ShellThumbnailer.IsPlainlyLocal`, and the
Runtime's `IsOnNetwork` on the already-open handle.

---
# 6d — The root pane — **done**

All nine items are in and tested (see the Status table for commits). What remains is the manual
pass on real hardware in record 6d-4; everything below this line is the design as it was decided,
kept for the reasoning.

## Row kinds

| Row | Kind | Target | Preview |
|---|---|---|---|
| Section header | **`Header`** (new) | none | unreachable — not selectable |
| Fixed / removable / optical / RAM drive | `Drive` | `RealDirectory("D:\")` | capacity panel |
| Network drive (mapped) | `Drive` | `RealDirectory("Z:\")` | capacity; label via `WNetGetConnection` |
| Mounted image | `Drive` | `RealDirectory("E:\")` | capacity; eject offered |
| Not-ready drive (empty optical) | `Drive` | — | "Not ready", not enterable |
| Pinned UNC share | `Directory` | `RealDirectory(@"\\srv\share")` | capacity via `GetDiskFreeSpaceEx` — **`DriveInfo` throws on UNC** (verified) |
| Favorite (known folder) | `Directory` | `RealDirectory(path)` | as a directory |
| **Trash** | `Directory` | **`RecycleBin`** | item count + total size via `SHQueryRecycleBin` |
| Directory / file | `Directory` / `File` | as today | as today |
| Link / junction | `Directory`/`File` + reparse flag | resolved target | target path in metadata |

One new `EntryKind` (`Header`), plus `Entry.Target`, `DisplayName`, `Group`, and a drive-subtype
field. **Drive subtype is a field, not a kind** — folding five `DriveType` values into `EntryKind`
would multiply its 88 usage sites.

**Per-row-kind operation rules are needed.** zurari blocks copy, move and bookmark on the drive list
by name (`..\zurari\Program.cs:3581`, `:3602`, `:6267`). With Trash, pins and favorites the rules get
finer: Trash accepts *restore* and *empty* but not paste; a favorite is a normal directory for
operations; a not-ready drive accepts nothing. This becomes a table on `Location`, not scattered guards.

## Headers — the design does not exist

Honest answer to "where is it stated?": **nowhere.** There are no mockups, no color spec, and no
design tokens anywhere in the repo — `Generic.xaml` uses inline hex literals (`#FFB0B0B0`,
`#FF3A86E0`), row padding `6,2`, icon height 16, and no named brush resources. zurari has no
precedent either: its drive list is a flat `DriveInfo.GetDrives()` with no sections.

So a visual spec is a **deliverable, and it is reviewed and approved before any header code is
written.** That is an explicit gate, not a courtesy: the design is unstated today, so implementing
first would be inventing it silently.

The spec must settle: header height and whether it differs from a row; indentation of rows beneath a
header; typography (weight, size, caps); color, and where it comes from given there is no token
system; whether headers stick while scrolling; whether the first section shows a header at all; and
section order and names.

Recommendation: introduce named brush resources at the same time, since inventing a header color
against hardcoded literals just adds a sixth. And render the mockup against the *real* row metrics
already in `Generic.xaml` (padding `6,2`, icon 16px) so what is approved is what ships.

## Headers live in Core — reversed again, deliberately

**The cursor lands on the section name, and `Space` collapses/expands it.** That decides the
question: a row the cursor can reach must be in `Column.Entries`, because `Cursor` is an index into
it. So `EntryKind.Header` goes into Core after all, and the projection-only design below is
superseded — kept for the reasoning, which is still what makes the *rest* of the rules right.

What this costs, knowingly:

- A new `EntryKind` member, reviewed across its 88 usage sites.
- `GenMsg` must generate states containing headers, and the property tests then explore them.
- Marks must exclude headers (`DeleteMarked` and rectangle-selection already skip `Drive`; `Header`
  joins it), and `Space` becomes context-dependent: mark at cursor normally, collapse on a header.
- Search must still skip headers — narrowing to "ドライブ" makes no sense. That is now a filter
  inside search rather than a structural guarantee.

What it saves: **`EntryVm.CoreIndex` is no longer needed.** Headers being real entries means the VM
index and the Core index stay 1:1, so the translation layer through `ColumnBrowser`/`ColumnView` -
which was the sharp edge of the projection-only design - disappears entirely.

### Key bindings — all three already exist, verified

- `←` is already inert in the root pane: `GoToParent` returns unchanged at column 0. Left alone.
- `→` keeps descend-or-advance-focus (`HandleRight`). Not repurposed.
- **Device refresh is already `F5`** (and `Ctrl+R`): `Msg.Refresh` re-reads every column, and the
  root column's read *is* `ReadDrives()`. No new binding, and nothing slow on an accidental arrow.

Only `Space`-on-a-header is new.

## Superseded: headers live in the projection

Headers are not entries. Search must not narrow to "Drives"; the cursor must never land on one;
they have no target, no size, no date, and no operations. Putting them in `Column.Entries` would
mean adding an `EntryKind` member across 88 sites, a "`Cursor` never points at a `Header`"
invariant, `GenMsg` cases, and header-skipping in every navigation path — all to model something
that then has to be excluded from each of them in turn.

Keeping them out of Core makes every one of those costs vanish rather than be paid and then worked
around: cursor movement, search, marking and rectangle-selection need no header logic at all,
because there are no headers in the list they walk.

**Design:**

- `Entry.Group` lives in Core as **data** — it is a real property of a row (which section it belongs
  to), and features like "select all in this section" can use it.
- `StateProjection` synthesizes header rows from `Group`.
- `EntryVm.CoreIndex` carries the real index, and `ColumnBrowser` reports *that* rather than the raw
  list index. This is the one genuine cost, confined to `ColumnBrowser` and `ColumnView`.

### The audit stands regardless

Headers turned out not to belong in Core, but the class of error is real and these do:

- **`DisplayName` must be searchable** — search should match "Data (D:)", not just `D:\`. On `Entry`
  already, but only by luck.
- **Sorting must be in Core** or Leap and search index a different order than `Transition` holds. (6b.)
- **Filtering must not be a view filter** or marks and cursor desync. (6b.)
- **Cursor memory keys on entry name**, which requires the name to be in Core — it is.
- **`Entry.Group` must be in Core**, even though headers are not: "select all in this section" and
  any section-aware navigation need it as data.
- **`CoreIndex` translation is the sharp edge.** Every event path from `ColumnBrowser` and every
  container-index walk in `ColumnView` must translate, or clicks and rectangle-selection land on the
  wrong row once headers shift the visual list.

## Commands, keys and help

Every command added here needs a keybinding **and** a help entry — the key-hint/help screen is
already a Phase 6 item, and it is the only way any of this is discoverable. Eject is the example that
prompted this, but it applies to mount, pin/unpin, sort mode, sort direction, hidden toggle, preview
toggle, new folder, rename, and open-with-default-app. A single command table drives both the
key dispatch and the help screen, so the two cannot drift.

## Remaining decisions

- Surface `DriveType` / `VolumeLabel` / `IsReady`, all discarded today at `WorkerRuntime.cs:155`.
- Enter on `.iso` invokes the shell `mount` verb — `Windows.IsoFile` registers it (verified), no
  elevation. VHD/VHDX deferred (`AttachVirtualDisk` needs admin). On success, refresh and put the
  cursor on the new drive; do not auto-descend.
- `Effect.LoadPreview` carries a target kind, not a bare path — a drive's `C:\` is also a valid
  directory path. `PreviewKind` gains `Drive`. Port zrr's `LoadDrive` (`..\zurari\Program.cs:2545`).
- **No `Environment.SpecialFolder.Downloads`** (verified) — needs `SHGetKnownFolderPath`.

---

# 6e — Sorting, hidden files, cursor memory, rename

Data model and sort tables are in 6b. Remaining:

**Hidden files.** `Entries` is the derived visible list, so the toggle is pure. Runtime must return
hidden entries in `AllEntries` for the filter to have anything to reveal.

**Cursor memory.** Entry name, never index — index breaks on sort change, deletion and refresh.
Matches zurari's LRU-capped `path → entry name` map (`..\zurari\Program.cs:2929`), session-only.
Belongs on `AppState` as an `ImmutableDictionary`; **the LRU cap must be a checked invariant** or the
map grows without bound inside the property tests. Headers excluded.

**Rename.** Explorer overlays a just-sized TextBox on the filename — here a WPF Adorner over the
row's `ListBoxItem`, not an editable item template, so `VirtualizationMode="Recycling"` and
template-selector concerns do not apply. `ColumnView` already computes container bounds for
rectangle-selection. `TextBox` handles IME natively. Conflict → inline message, editor stays open
with the text selected.

Also here: new folder (cursor lands on it) and open-with-default-app — `Enter` already reaches Core
as `EntryActivated` → `Msg.EnterDirectory` (`MainWindow.xaml.cs:95`), so it is a branch on
`EntryKind` plus a Shell effect, not a new input path.

---

# 6e-bis — Smooth cursor movement

**Objective: ↑/↓ stays responsive regardless of directory size.** This is a named goal, not a
one-off fix — the projection rebuild below is the first cause found, and further ones are expected
to surface behind it once it is gone. Each gets measured before and after.

## Cause 1: the projection rebuilds everything on every keystroke — **fixed** (`f61c46b`)

**Measured, projection only, per cursor move:** 1k 1.09 → 0.016 ms; 10k 15.19 → 0.001 ms;
50k 92.14 → **0.002 ms**. The row array is now reused, so WPF's `ItemsSource` assignment is a no-op
and the container rebuild is gone as well.


Measured cost of `StateProjection.Project` for a **single column with the cursor moving up/down**
(not switching columns): 1k entries 0.89 ms, 10k 17.1 ms, **50k 83.9 ms** — before WPF does anything.

A cursor move changes one integer, but `Project` rebuilds every `EntryVm` in the column (resolving
an icon each) into a **new array**. `Generic.xaml` binds `ItemsSource="{Binding Column.Entries}"`, so
a new array makes WPF tear down and regenerate the ListBox's containers and re-run virtualization -
every keystroke.

Nothing to do with preview: `Column.Entries` is the *same* `ImmutableArray` instance across a cursor
move (verified), so the work is pure waste.

**Fix:** memoize the projected `EntryVm[]` per column, keyed on the `Entries` instance, the column's
`Location`, and the cut-pending set. Handing WPF the same array reference makes the `ItemsSource`
setter a no-op and the container rebuild disappears.

## Remaining candidates — all measured, one acted on

Measured through the **full WPF cycle** (a real off-screen window, `Columns` assigned, dispatcher
pumped), per cursor move:

| Directory | Before memoization | After | Of which `ScrollIntoView` |
|---|---|---|---|
| 1,000 | 98.5 ms | **1.44 ms** | 0.67 ms |
| 50,000 | 142.5 ms | **1.71 ms** | 0.88 ms |

Cursor cost is now essentially flat in directory size, and ~1.7 ms against a ~33 ms budget at
key-repeat rate.

- **Double `ScrollIntoView`: measured 0.7–0.9 ms, deliberately left alone.** The second call is a
  documented workaround for scrolling stalling at the viewport edge (`43d6358`, `16f2283`). Removing
  it would reclaim well under a millisecond and risk reintroducing a visible bug.
- **`RenderPreview` / `RenderJobs`: negligible, no change.** `RenderJobs` is O(jobs), normally zero.
  `RenderPreview` guards image decoding by generation, and only formats a hex dump for a `Binary`
  preview - which a moving cursor never shows, since the preview is `Loading` throughout.
- **Preview I/O churn per keystroke: fixed** (`593194e`). Not a UI-thread cost, so it does not move
  the numbers above - it stops the machine performing ~30 file opens a second while a key is held,
  which is what would hurt on a slow or networked disk. A preview now waits 100 ms before any I/O.

**That wait exposed two more races in the 6c preview slot**, both fixed in the same commit: the
cancelling side disposed the `CancellationTokenSource` while its task still held the token
(`WaitHandle` throws once disposed, unlike `IsCancellationRequested` - so it only appeared once the
settle wait started using it), and `StartPreview` could run out of order and let a stale generation
win, leaving the pane loading forever.

# 6f — Findings parked from 6a–6e

Not in the original scope, found while doing it, and all touching "basic operations" rather than any
one feature. Recorded so they are not lost.

- **`JobEngineTests.CancelJob_mid_copy_deletes_the_incomplete_destination_file` is racy.** It copies
  50MB, waits for one `JobProgress`, then cancels and expects `JobCancelled`. If the copy finishes
  first, that Msg never comes and the test times out (observed under load). Fixing it properly means
  either a much larger fixture - slower every run - or a throttle seam in `JobEngine`, which is a
  design decision rather than a tidy-up.
- **57 unguarded `Directory.Delete(dir, recursive: true)` calls in test `finally` blocks**, none
  wrapped. On Windows a lingering handle (a watcher unwinding, an ffmpeg child, a virus scanner)
  turns a test whose assertions all passed into a failure. A shared best-effort temp-dir helper
  would remove a whole class of flake.
- ~~**`UserSettingsStore` has no injectable path.**~~ **Done** (`134fce0`), pulled forward because it
  blocked the rest of 6d. Constructor argument defaulting to `%APPDATA%\zurari`; six tests including
  the missing-file and corrupt-file branches that were previously impossible to cover. Saving is now
  temp-file-then-move, so a crash partway cannot leave a truncated file that loads as defaults and
  discards every preference rather than the one being written.

# Configuration — following rwf and zurari

## The problem is already in the repo

`UserSettingsStore` resolves its path from a **static** property with no injection point. So
`tests/Zurari.Runtime.Tests/UserSettingsStoreTests.cs` writes to the **real** `%APPDATA%` and
save-restores around it, and its second test carries the comment *"Cannot safely delete the real
settings file"* — the corrupt and missing cases are untestable today.

**Both references already solved this.** rwf has `ConfigManager::with_paths(…)` beside the
`dirs::config_dir()` default. zurari has `_pathOverride` on its `Bookmarks` store
(`..\zurari\Program.cs:1310`). zurari_w is the outlier.

Correcting my earlier note: zurari is not config-free. It has no *settings* file by design, but it
does persist bookmarks to `%APPDATA%\zurari\bookmarks.txt` — the same directory zurari_w already uses.

## Decisions

- Make the store **instance-based with an injectable directory**; production keeps the `%APPDATA%`
  default. Prerequisite for pins, sort mode, hidden toggle and preview toggle, and it makes the
  corrupt/missing paths testable for the first time.
- **Start with one `settings.json`**, splitting only when a second concern appears (keybindings, in
  Phase 7). rwf's six files reflect six matured concerns, not a starting shape.

---

# Windows facilities — inventory and decisions

Already wrapped in `NativeMethods.cs`: `SHGetFileInfoW`, `SHCreateItemFromParsingName`,
`SHParseDisplayName`, `SHBindToParent` — so `IShellItem` is one hop away, not new ground.

| Facility | API | What it buys | Decision |
|---|---|---|---|
| Directory change | `FileSystemWatcher` | in use today | **Keep** for `RealDirectory`. On buffer overflow or share disconnect, `OnError` fires and that path silently stops being watched (`DirectoryWatcher.cs:175`) — needs a re-arm, not just a log |
| Shell change | **`SHChangeNotifyRegister`** | drive add/remove, media insert/eject, network share, renames — a **superset** of the drive events `WM_DEVICECHANGE` gives, via one window message | **Adopt for the root pane** |
| Device arrival | `WM_DEVICECHANGE` | volume arrival/removal, no registration | **Skip** unless testing shows `SHChangeNotifyRegister` gaps |
| Thumbnails | **`IShellItemImageFactory::GetImage`** | Explorer's own thumbnails for video, PDF, Office — no ffmpeg, and **Windows caches them itself** | **Try first, ffmpeg fallback.** Inverts the dependency correctly and shrinks our cache to the fallback path. Depends on installed extractors; must run off the UI thread |
| Display names | `IShellItem::GetDisplayName(SIGDN_NORMALDISPLAY)` | "Data (D:)" exactly as Explorer renders it | **Adopt** for drive `DisplayName` |
| Type names | `SHGetFileInfo` + `SHGFI_TYPENAME` | Explorer's Type text ("PNG ファイル"). **One extra flag on the call `ShellIconCache` already makes**, sharing its extension-keyed cache | **Adopt** — makes sort-by-type cheap and gives the preview a real type line |
| Known folders | `SHGetKnownFolderPath` | Downloads — no `SpecialFolder` member exists | **Required** for favorites |
| Recycle Bin contents | shell namespace (`FOLDERID_RecycleBinFolder`) | listing Trash as a column | **Required** for the Trash row |
| Recycle Bin stats | **`SHQueryRecycleBin`** | item count and total size | **Adopt** — this is the Trash row's preview |
| UNC capacity | `GetDiskFreeSpaceEx` | free/total on a share; `DriveInfo` throws on UNC | **Required** for pinned shares |
| Mapped-drive target | `WNetGetConnection` | `Z:` → `\\srv\share` for the label | **Adopt**, cheap |
| ISO mount | shell `mount` verb via `ShellExecuteEx` | mount without elevation | **Adopt** |
| Placeholder files | `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS` / `FILE_ATTRIBUTE_OFFLINE` | detect files that **hydrate on open** | **Adopt**: never auto-preview one; show a "press to load" affordance |
| Taskbar progress | `ITaskbarList3::SetProgressValue` | job progress on the taskbar icon | **Defer** — polish, no dependency |
| Dark title bar | `DwmSetWindowAttribute` | native dark chrome | **Defer** |

**On placeholders:** OneDrive is only the common example. The attribute is set by any Cloud Files API
provider — OneDrive, Dropbox, Google Drive, iCloud for Windows — and `FILE_ATTRIBUTE_OFFLINE` also
covers HSM and archive tiers. Detect the attribute, never a vendor.

---

# Deferred, with reasons

- **Reparse-point cycle prevention.** Auto-extend has now landed (6g-1) and this is **still not
  needed**, which corrects what this entry used to say. zurari's `ExtendRightSideIfReadyAsync`
  extends the whole chain, and that is what runs away on a self-referencing junction
  (`..\zurari\Program.cs:5879`). Ours extends exactly one column beyond the focus, so nothing
  advances without a keypress: walking a junction into its own ancestor costs one column per press,
  the same as Explorer. `ResolveCanonicalPath` plus a visited-set (`:4042`) becomes the design to
  copy only if the chain is ever extended more than one deep at a time. Link *display* is cheap and
  rides along in 6d.
- **VHD/VHDX mounting** — needs elevation.
- Extension-mismatch warning, audio/exe/PDF metadata, MP4 self-parsing — small and unblocked; the
  shell-thumbnail decision may make MP4 self-parsing unnecessary.

# Documentation fixes to fold in

- `plan/roadmap.md:4` says HEAD `16f2283`; it is `b561d45`.
- The three video-thumbnail rows marked 実装済み(未コミット) were committed in `b561d45`.
- CLAUDE.md points at `docs/superpowers/plans/`; the live plans are in `plan/`. A fresh session reads
  the stale copies and never sees `phase6-basic-operations.md`.
- `plan/phase8-virtual-locations.md` says the `""` special case exists in one place. It is 19 — and
  that file folds into 6a.
- Phase 8 is no longer separate; the roadmap table needs resequencing.
