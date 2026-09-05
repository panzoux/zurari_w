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
2026-09-05, once 6d was complete (see R-1). 6e and 6c.5 remain.
686 tests, `scripts/check.ps1` green.

<sub>The test count is written by `scripts/status.ps1`, and `check.ps1` refuses to pass while it is
stale. Do not edit it by hand. A commit count used to live here too; it was removed because it is
wrong the moment the next commit lands - `git log --oneline main..` is the honest form of it.</sub>

| | Item | Status | Commit |
|---|---|---|---|
| **6a** | **Location foundation** (absorbed Phase 8) | `[x]` | `2b74c1a` |
| **6b** | **Column data model**: raw list, derived view | `[x]` | `2cfbfee` |
| **6c** | **Cancellable preview pipeline** | `[~]` | |
| 6c.1 | Dedicated preview slot, off the worker pool | `[x]` | `53bc98c` |
| 6c.2 | Cancellation: generation-aware, ordering-safe | `[x]` | `ae3219a` `593194e` `cf41efa` |
| 6c.3 | Settle delay before touching the disk | `[x]` | `593194e` |
| 6c.4 | Thumbnail cache, failures included | `[x]` | `2f4459c` |
| 6c.5 | Shell thumbnails (`IShellItemImageFactory`) tried before ffmpeg | `[ ]` | |
| **6d** | **The root pane** | `[~]` | |
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
| **6e** | **Sorting, hidden files, cursor memory, rename** | `[ ]` | |
| 6e.1 | Sort in `Transition` (modes + direction + dirs-first) | `[x]` | `6e646c0` |
| 6e.2 | Hidden-file toggle | `[x]` | `06ce820` |
| 6e.3 | Cursor memory (entry name, LRU-capped) | `[x]` | (this commit) |
| 6e.4 | Rename (overlay TextBox) | `[ ]` | |
| 6e.5 | New folder | `[ ]` | |
| 6e.6 | Open with default app | `[ ]` | |
| 6e.7 | Status bar, key hints / help screen | `[ ]` | |
| **6e-bis** | **Smooth cursor movement** | `[x]` | `f61c46b` `593194e` |
| **6g** | **Finder-style auto-extend**: the column beside the cursor | `[x]` | `75f84f9` |
| **6f** | **Findings parked from 6a–6e** | `[~]` | |
| 6f.1 | Settings store: injectable path, atomic save | `[x]` | `134fce0` |
| 6f.2 | `JobEngineTests` cancel race | `[ ]` | |
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
- **6d-4** `[ ]` **The rest of 6d is Shell interop that cannot be verified headlessly.** Mount,
  eject, Trash enumeration and change notifications are COM calls assertable only structurally.
  Build order puts the testable parts (favorites, pins, persistence) first and isolates the interop
  at the end, where it needs a manual pass.
- **6f-2** `[ ]` **`JobEngineTests.CancelJob_mid_copy_deletes_the_incomplete_destination_file` is
  racy.** It copies 50MB, waits for one `JobProgress`, then cancels; if the copy finishes first,
  `JobCancelled` never arrives and it times out. Fixing it means a larger fixture (slower every run)
  or a throttle seam in `JobEngine` - a design decision, not a tidy-up.
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

# 6c — Cancellable preview pipeline — **part 1 done** (`53bc98c`)

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

**Remaining in 6c:** shell thumbnails (`IShellItemImageFactory`) tried before ffmpeg.

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


---
# 6d — The root pane — **half done** (`9082b8c`)

**Shipped:** sections with headers, the cursor landing on them, `Space` collapsing/expanding,
`Entry.DisplayName`/`Group`, drive labels ("Windows (C:)"), marks refused in the pane entirely.
514 tests green, including a realized-window test that the header template is really wired up in
`Generic.xaml` — a mistake there produces no build error and would just render headers as rows.

**Blocked on 6f's settings fix:** pinned UNC shares and remembering which sections are collapsed both
need persistence, and `UserSettingsStore` still has no injectable path. That fix is now a hard
prerequisite for finishing 6d rather than a nice-to-have.

**Still to do:** favorites (needs `SHGetKnownFolderPath` for Downloads), pinned shares, Trash as its
own group (shell namespace), drive capacity preview, ISO mount/eject, live refresh via
`SHChangeNotifyRegister`.


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
