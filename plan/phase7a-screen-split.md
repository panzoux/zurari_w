# Phase 7a — Screen split (Pane extraction + split UI) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two independent miller-column panes stacked top/bottom (Ctrl+W), each with its own preview and view options, with `c`/`m` copy/move between them — built on a Core that addresses every column through `ColumnRef(PaneId, Index)`.

**Architecture:** First half is a behaviour-preserving refactor of Core: `AppState.Columns/FocusedColumn/Preview/View` move into a `Pane` record, and every column-addressed Msg/Effect carries a `ColumnRef` instead of an `int`. It is staged through a temporary compatibility shim so every commit stays green. Second half adds the split Msgs to Core, keys the Runtime's preview slot and the App's coalescing gates by pane, extracts a `PaneView` UserControl, and stacks two of them.

**Tech Stack:** C# 12 / .NET 8, WPF, xunit + CsCheck (`Gen`), BannedApiAnalyzers, `scripts/check.ps1`.

**Design:** [2026-09-19-phase7-dual-pane-and-search-design.md](../docs/superpowers/specs/2026-09-19-phase7-dual-pane-and-search-design.md), sections "7a 前段" and "7a 後段".
**Summary plan:** [phase7-dual-pane-and-search.md](phase7-dual-pane-and-search.md)

---

# Status

895 tests, `scripts/check.ps1` green.

<sub>The test count is written by `scripts/status.ps1`; do not edit it by hand. Write `(this commit)`
in a row's commit cell (no backticks) and `status.ps1` fills in the hash after the commit lands.</sub>

| | Item | Status | Commit |
|---|---|---|---|
| **7a-front** | **Pane extraction (no visible change)** — code done; hands-on "behaves as before" check pending (user) | `[~]` | `55770c0`..`8ec0f31` |
| 7a.1 | `PaneId` / `ColumnRef` / `Pane`; state moves into `Pane` behind a compatibility shim | `[x]` | `55770c0` `05460e3` |
| 7a.2 | Column-addressed Msgs and Effects carry `ColumnRef` | `[x]` | `267963f` `b022607` |
| 7a.3 | Preview Msgs/Effects carry `PaneId`; post-steps run per pane; `ViewRestored` per pane | `[x]` | `6afba7f` `e4c72bc` |
| 7a.4 | Remove the compatibility shim | `[x]` | `c0024a9` `537a62d` |
| 7a.5 | Property tests: refs to panes that do not exist; per-pane invariants | `[x]` | `d35a7ca` `8ec0f31` |
| **7a-back** | **Split UI** | `[ ]` | |
| 7a.6 | `ToggleSplit` / `FocusPane` | `[x]` | `7aef49d` `50797e3` |
| 7a.7 | `SwapPanes` / `SyncToOtherPane` | `[ ]` | |
| 7a.8 | `CopyToOtherPane` (`c` / `m`) | `[ ]` | |
| 7a.8b | Two-pane correctness follow-ups (from the 7a.2 review): scoped reveal, `ShellOpCompleted` rereads, sections in every drive pane | `[ ]` | |
| 7a.9 | Runtime: one preview slot per pane | `[ ]` | |
| 7a.10 | App: coalescing gates and projection cache per pane; `PaneView` extracted (still one row) | `[ ]` | |
| 7a.11 | App: second row, splitter, focus routing, dimming, keys | `[ ]` | |
| 7a.12 | Persistence: split open/ratio, per-position view options | `[ ]` | |
| 7a.13 | Headless E2E: split, copy top → bottom | `[ ]` | |
| 7a.14 | Manual verification; roadmap/summary updated | `[ ]` | |

---

# Conventions for every task

- **Done** = the task's tests pass AND `pwsh scripts/check.ps1` prints `ALL CHECKS GREEN`. Run with
  `pwsh`, never `powershell` (5.1 mangles the Japanese in `plan/*.md`, see a932bab).
- The pre-commit hook runs `check.ps1`. **Never `--no-verify`.** If it fails, fix the cause.
- `dotnet format Zurari.sln` before committing if check.ps1 reports formatting.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- After each commit, set the task's Status row to `[x]` with `(this commit)` in the commit cell in the
  *next* commit, or run `pwsh scripts/status.ps1` after committing the row.
- `plan/*.md` are CRLF. Edit them without converting line endings.
- A new `Msg` must be added to `GenMsg` in `tests/Zurari.Core.Tests/TransitionTests.cs` (project rule).
- Do not leave the shell's cwd in `artifacts/testresults` when committing (check.ps1 deletes it).

> **No LINQ (added 2026-09-20, commit `d142ce2`).** `build/no-linq/BannedSymbols.txt` applies to
> `src/` and `tests/` alike, and `ImplicitUsings` means `System.Linq` is in scope without a `using` -
> only the analyzer stops it. Use explicit loops and the non-LINQ BCL (`List.Find/FindAll/Exists/
> ConvertAll`, `Array.Find/Sort`, `ImmutableArray.CreateBuilder`). In tests, pick effects with
> `TestEffects.Of<Effect.X>(effects)` (`tests/TestSupport/EffectPicks.cs`), which returns a `List<T>`
> - so `Assert.Single(...)`, `.Count` and `Assert.Empty(...)` all work on it. The snippets below
> follow this; if you spot a stray `Where`/`Select`/`Any`/`OfType` in a later task, rewrite it.

## Decisions made while planning (not in the design; recorded here)

1. **The Msg/Effect field is named `At`, not `Column`**: `new Msg.CursorUp(At: ...)`. A property
   called `Column` of type `ColumnRef` next to the `Column` record reads badly in handlers
   (`m.Column` vs `state.ColumnAt(m.At)`).
2. **`o` (`SyncToOtherPane`) copies the other pane's column chain** (its `Columns` and
   `FocusedColumn`, re-derived under this pane's `View`) instead of rebuilding it through a
   `JumpTo` helper. The other pane's listings are already loaded, so no I/O happens and there is no
   loading flash. `JumpTo(PaneId, Location)` is deferred to 7c, where the address bar and history
   actually need it. The design doc is updated in 7a.14.
3. **`Refresh` (F5) re-reads every column of every pane**, as it re-reads every column today.
   `ExternalDirectoryChanged`, `ShellOpCompleted`, `JobCompleted`/`JobCancelled` and
   `PlacesChanged` likewise look at every pane: a directory that changed is stale wherever it is shown.
4. **`RevealTarget` stays shared.** The first `DirectoryLoaded` in *any* pane that contains the name
   consumes it, and moves that pane's `FocusedColumn` (never `FocusedPane`).
5. **Dimming = the unfocused pane projects no focused column.** Its cursor rows then draw with the
   weak highlight unfocused columns already use. No Controls change.
6. **The bottom pane's restored view arrives with `ToggleSplit`**: `Msg.ToggleSplit(ViewOptions? NewPaneView = null)`.
   The App passes the stored bottom view when it restores the split at startup; a key press passes
   nothing and the new pane copies the focused pane's view.
7. **Persisting view options becomes `Effect.SetPaneViews(ImmutableArray<ViewOptions> TopFirst)`**,
   emitted with every pane's view whenever any of them changes, and on swap and toggle. Settings keep
   entries beyond the current pane count, so closing the split does not forget the bottom view.
8. **Both panes share one preview width.** Dragging either preview splitter sets both, and the one
   stored `PreviewWidth` keeps its meaning.

---

# 7a-front — Pane extraction

## File map

| File | Change |
|---|---|
| `src/Zurari.Core/Pane.cs` | **Create.** `PaneId`, `ColumnRef`, `Pane` |
| `src/Zurari.Core/AppState.cs` | `Panes`/`FocusedPane`/`NextPaneId`, helpers, shim (7a.1), shim removed (7a.4), per-pane invariants |
| `src/Zurari.Core/Msg.cs`, `Effect.cs` | `int ColumnIndex` → `ColumnRef At`; preview records gain `PaneId Pane` |
| `src/Zurari.Core/Transition.cs` | Handlers address through `ColumnAt`/`WithColumn`; post-steps per pane |
| `src/Zurari.Runtime/WorkerRuntime.cs` | Pass `At`/`Pane` through; `ViewRestored(pane, view)` |
| `src/Zurari.Shell/ShellEffectExecutor.cs` | Pass `At` through |
| `src/Zurari.App/*.cs` | Dispatch with `ColumnRef`s built from the focused pane; project `state.Focused` |
| `tests/TestSupport/TestPanes.cs` | **Create.** `First`, `Col(int)` helpers |
| `tests/Directory.Build.props` | Link `TestPanes.cs` into the test projects that reference Core |
| all test files listed by the compiler | Mechanical migration |

---

### Task 7a.1: Pane types, state moved into `Pane`, compatibility shim

The shim keeps every existing reader and writer of `state.Columns`, `state.FocusedColumn`,
`state.Preview` and `state.View` compiling and behaving identically while there is one pane.
It is deleted in 7a.4. **Do not write new code against it.**

**Files:**
- Create: `src/Zurari.Core/Pane.cs`
- Modify: `src/Zurari.Core/AppState.cs` (the `AppState` record, lines ~645-790)
- Test: `tests/Zurari.Core.Tests/PaneTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

Create `tests/Zurari.Core.Tests/PaneTests.cs`:

```csharp
using System.Collections.Immutable;
using Zurari.Core;

namespace Zurari.Core.Tests;

public class PaneTests
{
    private static Column Dir(string path, params string[] names) =>
        new Column(new Location.RealDirectory(path), [.. names.Select(n => new Entry(n, EntryKind.File))], Cursor: names.Length > 0 ? 0 : -1, Load: LoadState.Loaded);

    [Fact]
    public void Initial_has_one_pane_with_the_drive_list()
    {
        var state = AppState.Initial;

        Assert.Single(state.Panes);
        Assert.Equal(0, state.FocusedPane);
        Assert.Equal(new PaneId(0), state.Focused.Id);
        Assert.Equal(1, state.NextPaneId);
        Assert.IsType<Location.Drives>(state.Focused.Columns[0].Location);
        Assert.Empty(state.CheckInvariants());
    }

    [Fact]
    public void ColumnAt_resolves_by_pane_id_and_rejects_unknown_panes_and_indices()
    {
        var state = AppState.SinglePane([Dir(@"C:\", "a")]);
        var pane = state.Focused.Id;

        Assert.NotNull(state.ColumnAt(new ColumnRef(pane, 0)));
        Assert.Null(state.ColumnAt(new ColumnRef(pane, 1)));
        Assert.Null(state.ColumnAt(new ColumnRef(pane, -1)));
        Assert.Null(state.ColumnAt(new ColumnRef(new PaneId(99), 0)));
    }

    [Fact]
    public void WithColumn_replaces_only_the_addressed_column()
    {
        var state = AppState.SinglePane([Dir(@"C:\", "a"), Dir(@"C:\x", "b")]);
        var at = new ColumnRef(state.Focused.Id, 1);
        var replaced = Dir(@"C:\y", "c");

        var next = state.WithColumn(at, replaced);

        Assert.Same(replaced, next.Focused.Columns[1]);
        Assert.Same(state.Focused.Columns[0], next.Focused.Columns[0]);
    }

    [Fact]
    public void WithColumn_for_an_unknown_pane_returns_the_same_state()
    {
        var state = AppState.SinglePane([Dir(@"C:\", "a")]);

        var next = state.WithColumn(new ColumnRef(new PaneId(99), 0), Dir(@"D:\"));

        Assert.Same(state, next);
    }

    [Fact]
    public void Invariants_reject_duplicate_pane_ids_and_ids_not_below_NextPaneId()
    {
        var pane = new Pane(new PaneId(0), [Dir(@"C:\")], 0);
        var duplicate = AppState.Initial with { Panes = [pane, pane], NextPaneId = 1 };
        var tooHigh = AppState.Initial with { Panes = [pane with { Id = new PaneId(5) }], NextPaneId = 1 };

        Assert.Contains(duplicate.CheckInvariants(), v => v.Contains("more than once", StringComparison.Ordinal));
        Assert.Contains(tooHigh.CheckInvariants(), v => v.Contains("NextPaneId", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariants_reject_more_than_two_panes_and_an_out_of_range_focus()
    {
        var p = (int id) => new Pane(new PaneId(id), [Dir(@"C:\")], 0);
        var three = AppState.Initial with { Panes = [p(0), p(1), p(2)], NextPaneId = 3 };
        var badFocus = AppState.Initial with { FocusedPane = 1 };

        Assert.Contains(three.CheckInvariants(), v => v.Contains("1 or 2 panes", StringComparison.Ordinal));
        Assert.Contains(badFocus.CheckInvariants(), v => v.Contains("FocusedPane", StringComparison.Ordinal));
    }

    [Fact]
    public void Column_invariants_are_checked_in_every_pane()
    {
        var broken = new Column(new Location.RealDirectory(@"C:\"), [new Entry("a", EntryKind.File)], Cursor: 7);
        var state = AppState.Initial with
        {
            Panes = [new Pane(new PaneId(0), [Dir(@"C:\")], 0), new Pane(new PaneId(1), [broken], 0)],
            NextPaneId = 2,
        };

        Assert.Contains(state.CheckInvariants(), v => v.Contains("Panes[1].Columns[0].Cursor", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Zurari.Core.Tests --filter FullyQualifiedName~PaneTests`
Expected: build FAILS — `PaneId`, `ColumnRef`, `Pane`, `AppState.Panes`, `SinglePane`, `ColumnAt` do not exist.

- [ ] **Step 3: Create `src/Zurari.Core/Pane.cs`**

```csharp
using System.Collections.Immutable;

namespace Zurari.Core;

/// <summary>
/// Which pane something belongs to. Stable for the pane's lifetime, unlike its position: swapping the
/// panes moves them, and a result still in flight must land in the pane that asked for it.
/// </summary>
public readonly record struct PaneId(int Value);

/// <summary>A column, named by the pane it is in and its index within that pane.</summary>
public readonly record struct ColumnRef(PaneId Pane, int Index);

/// <summary>
/// One miller-column browser: what it shows and where its cursor is, plus the preview and view
/// options that go with it. Everything that is "where am I looking" lives here; everything shared
/// between panes (jobs, cursor memory, notices) stays on <see cref="AppState"/>.
/// </summary>
/// <param name="Id">See <see cref="PaneId"/>.</param>
/// <param name="Columns">The columns shown, left to right.</param>
/// <param name="FocusedColumn">Index into <paramref name="Columns"/> of the column the cursor keys act on.</param>
public sealed record Pane(PaneId Id, ImmutableArray<Column> Columns, int FocusedColumn)
{
    /// <summary>What this pane's preview shows.</summary>
    public PreviewState Preview { get; init; } = PreviewState.Initial;

    /// <summary>How this pane's listings are ordered and filtered.</summary>
    public ViewOptions View { get; init; } = ViewOptions.Default;

    /// <summary>The column the cursor keys act on, or <c>null</c> when <see cref="FocusedColumn"/> is out of range.</summary>
    public Column? FocusedColumnOrNull =>
        FocusedColumn >= 0 && FocusedColumn < Columns.Length ? Columns[FocusedColumn] : null;

    /// <summary>A reference to <see cref="FocusedColumn"/>.</summary>
    public ColumnRef FocusedRef => new(Id, FocusedColumn);

    /// <summary>A reference to column <paramref name="index"/> of this pane.</summary>
    public ColumnRef Ref(int index) => new(Id, index);
}
```

- [ ] **Step 4: Replace the head of the `AppState` record**

In `src/Zurari.Core/AppState.cs`, replace the two properties `Columns` and `FocusedColumn`, and the
properties `Preview` and `View`, with the block below. Leave `Jobs`, `NextJobId`, `Notice`,
`CursorMemory`, `Rename`, `InputMode`, `RevealTarget`, `CutPending` exactly as they are.

```csharp
    /// <summary>The panes, top first. One, or two when the window is split.</summary>
    public ImmutableArray<Pane> Panes { get; init; } = [new Pane(new PaneId(0), [], 0)];

    /// <summary>Index into <see cref="Panes"/> of the pane that has keyboard focus.</summary>
    public int FocusedPane { get; init; }

    /// <summary>The <see cref="PaneId"/> the next pane created gets.</summary>
    public int NextPaneId { get; init; } = 1;

    /// <summary>The pane that has keyboard focus.</summary>
    public Pane Focused => Panes[FocusedPane];

    // ---- 7a compatibility shim: removed in 7a.4. Reads and writes the FOCUSED pane. ----
    // Do not write new code against these; address panes through Focused, PaneOf, ColumnAt, WithColumn.

    /// <summary>Transitional: the focused pane's columns.</summary>
    public ImmutableArray<Column> Columns
    {
        get => Focused.Columns;
        init => Panes = Panes.SetItem(FocusedPane, Panes[FocusedPane] with { Columns = value });
    }

    /// <summary>Transitional: the focused pane's focused column.</summary>
    public int FocusedColumn
    {
        get => Focused.FocusedColumn;
        init => Panes = Panes.SetItem(FocusedPane, Panes[FocusedPane] with { FocusedColumn = value });
    }

    /// <summary>Transitional: the focused pane's preview.</summary>
    public PreviewState Preview
    {
        get => Focused.Preview;
        init => Panes = Panes.SetItem(FocusedPane, Panes[FocusedPane] with { Preview = value });
    }

    /// <summary>Transitional: the focused pane's view options.</summary>
    public ViewOptions View
    {
        get => Focused.View;
        init => Panes = Panes.SetItem(FocusedPane, Panes[FocusedPane] with { View = value });
    }

    // ---- end of shim ----

    /// <summary>Index into <see cref="Panes"/> of <paramref name="id"/>, or -1 when no such pane exists.</summary>
    public int IndexOfPane(PaneId id)
    {
        for (var i = 0; i < Panes.Length; i++)
        {
            if (Panes[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The pane <paramref name="id"/>, or <c>null</c> when it no longer exists (closed).</summary>
    public Pane? PaneOf(PaneId id) => IndexOfPane(id) is var i and >= 0 ? Panes[i] : null;

    /// <summary>The column <paramref name="at"/> names, or <c>null</c> when its pane or index is gone.</summary>
    public Column? ColumnAt(ColumnRef at) =>
        PaneOf(at.Pane) is { } pane && at.Index >= 0 && at.Index < pane.Columns.Length
            ? pane.Columns[at.Index]
            : null;

    /// <summary>Replaces the pane with <paramref name="pane"/>'s id. Unknown id: returns this state unchanged.</summary>
    public AppState WithPane(Pane pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        var index = IndexOfPane(pane.Id);
        return index < 0 ? this : this with { Panes = Panes.SetItem(index, pane) };
    }

    /// <summary>Replaces the column <paramref name="at"/> names. Unknown pane or index: returns this state unchanged.</summary>
    public AppState WithColumn(ColumnRef at, Column column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return ColumnAt(at) is null || PaneOf(at.Pane) is not { } pane
            ? this
            : WithPane(pane with { Columns = pane.Columns.SetItem(at.Index, column) });
    }

    /// <summary>A state with one pane showing <paramref name="columns"/>. For startup and tests.</summary>
    public static AppState SinglePane(ImmutableArray<Column> columns, int focusedColumn = 0) =>
        new() { Panes = [new Pane(new PaneId(0), columns, focusedColumn)], FocusedPane = 0, NextPaneId = 1 };
```

Change `Initial` to:

```csharp
    /// <summary>Starting state: one pane with a single, still-loading virtual root column (the drive list).</summary>
    public static AppState Initial { get; } = SinglePane([new Column(Location.Drives.Instance, Entries: [])]);
```

- [ ] **Step 5: Make `CheckInvariants` per pane**

Replace the part of `CheckInvariants` from `if (Columns.IsDefaultOrEmpty)` down to (but not
including) the `CursorMemory` checks with:

```csharp
        if (Panes.IsDefaultOrEmpty || Panes.Length > 2)
        {
            violations.Add($"Panes holds {(Panes.IsDefault ? 0 : Panes.Length)} panes; expected 1 or 2 panes.");
            return violations;
        }

        if (FocusedPane < 0 || FocusedPane >= Panes.Length)
        {
            violations.Add($"FocusedPane {FocusedPane} is out of range [0, {Panes.Length}).");
        }

        var seenPanes = new HashSet<PaneId>();
        for (var p = 0; p < Panes.Length; p++)
        {
            var pane = Panes[p];
            if (!seenPanes.Add(pane.Id))
            {
                violations.Add($"Panes holds {pane.Id} more than once.");
            }

            if (pane.Id.Value < 0 || pane.Id.Value >= NextPaneId)
            {
                violations.Add($"Panes[{p}].Id {pane.Id.Value} is not in [0, NextPaneId {NextPaneId}).");
            }

            if (pane.Preview.Attempt < 1 || pane.Preview.Attempt > PreviewState.MaxAttempts)
            {
                violations.Add($"Panes[{p}].Preview.Attempt is outside 1.." + PreviewState.MaxAttempts);
            }

            CheckColumns(pane, $"Panes[{p}].", violations);
        }
```

and move the old per-column loop into a new private static method (same body, with the prefix
added to every message and `Columns`/`FocusedColumn` read from the pane):

```csharp
    private static void CheckColumns(Pane pane, string prefix, List<string> violations)
    {
        if (pane.Columns.IsDefaultOrEmpty)
        {
            violations.Add(prefix + "Columns must be non-empty.");
            return;
        }

        if (pane.FocusedColumn < 0 || pane.FocusedColumn >= pane.Columns.Length)
        {
            violations.Add($"{prefix}FocusedColumn {pane.FocusedColumn} is out of range [0, {pane.Columns.Length}).");
        }

        for (var i = 0; i < pane.Columns.Length; i++)
        {
            var column = pane.Columns[i];

            if (column.Entries.IsEmpty)
            {
                if (column.Cursor != -1)
                {
                    violations.Add($"{prefix}Columns[{i}].Cursor is {column.Cursor} but Entries is empty (expected -1).");
                }
            }
            else if (column.Cursor < 0 || column.Cursor >= column.Entries.Length)
            {
                violations.Add($"{prefix}Columns[{i}].Cursor {column.Cursor} is out of range [0, {column.Entries.Length}).");
            }

            if (column.Load == LoadState.Error && column.ErrorMessage is null)
            {
                violations.Add($"{prefix}Columns[{i}].Load is Error but ErrorMessage is null.");
            }

            // (keep the existing comment block about the subset check and about there being no
            // parent/child relationship between adjacent columns)
            if (!IsSubsetOfAll(column))
            {
                violations.Add($"{prefix}Columns[{i}].Entries contains rows that are not in AllEntries.");
            }
        }
    }
```

Remove the old top-level `Preview.Attempt` check (it is now per pane, above).

- [ ] **Step 6: Fix existing tests that match old invariant messages**

Run: `dotnet build Zurari.sln`
Then: `dotnet test tests/Zurari.Core.Tests`
Some existing tests assert on messages such as `"Columns[0].Cursor"` (TransitionTests ~2207-2253).
They now read `"Panes[0].Columns[0].Cursor"`. Update the expected substrings — use `Contains`, and
change only the expected text. `new AppState { Columns = [...], FocusedColumn = 0 }` still compiles
(the shim's `init` writes into the default pane).

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/Zurari.Core.Tests`
Expected: PASS, including the 7 new `PaneTests`.

- [ ] **Step 8: Full check and commit**

Run: `pwsh scripts/check.ps1` → `ALL CHECKS GREEN`

```bash
git add src/Zurari.Core/Pane.cs src/Zurari.Core/AppState.cs tests/Zurari.Core.Tests
git commit -m "Move columns, preview and view into a Pane, behind a transitional shim"
```

---

### Task 7a.2: Column-addressed Msgs and Effects carry `ColumnRef`

Mechanical but wide. The compiler lists every site once the types change; work through the list.

**Files:**
- Modify: `src/Zurari.Core/Msg.cs`, `src/Zurari.Core/Effect.cs`, `src/Zurari.Core/AppState.cs` (`RenameState`), `src/Zurari.Core/Transition.cs`
- Modify: `src/Zurari.Runtime/WorkerRuntime.cs`, `src/Zurari.Shell/ShellEffectExecutor.cs`, `src/Zurari.App/MainWindow.xaml.cs`, `src/Zurari.App/InputTrace.cs`
- Create: `tests/TestSupport/TestPanes.cs`; Modify: `tests/Directory.Build.props`
- Modify: every test file the compiler flags

- [ ] **Step 1: Test helper**

Create `tests/TestSupport/TestPanes.cs`:

```csharp
using Zurari.Core;

namespace Zurari.TestSupport;

/// <summary>Addresses in the single pane every pre-split test works with.</summary>
public static class TestPanes
{
    /// <summary>The id <see cref="AppState.Initial"/> and <see cref="AppState.SinglePane"/> give their pane.</summary>
    public static readonly PaneId First = new(0);

    /// <summary>Column <paramref name="index"/> of <see cref="First"/>.</summary>
    public static ColumnRef Col(int index) => new(First, index);
}
```

In `tests/Directory.Build.props`, next to the existing `TempDirectory.cs` include, add (only the
projects that reference Core; Controls/Gallery tests do not):

```xml
    <Compile Include="$(MSBuildThisFileDirectory)TestSupport/TestPanes.cs" Link="TestSupport/TestPanes.cs"
             Condition="'$(MSBuildProjectName)' == 'Zurari.Core.Tests' Or '$(MSBuildProjectName)' == 'Zurari.App.Tests' Or '$(MSBuildProjectName)' == 'Zurari.Runtime.Tests' Or '$(MSBuildProjectName)' == 'Zurari.Shell.Tests' Or '$(MSBuildProjectName)' == 'Zurari.Arch.Tests'" />
```

- [ ] **Step 2: Write the failing test for cross-pane staleness**

Append to `tests/Zurari.Core.Tests/PaneTests.cs`:

```csharp
    [Fact]
    public void A_listing_for_a_pane_that_no_longer_exists_is_ignored()
    {
        var state = AppState.SinglePane([new Column(new Location.RealDirectory(@"C:\"), Entries: [])]);
        var ghost = new ColumnRef(new PaneId(42), 0);

        var (next, effects) = Transition.Apply(
            state, new Msg.DirectoryLoaded(ghost, new Location.RealDirectory(@"C:\"), [new Entry("a", EntryKind.File)]));

        Assert.Same(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void Entering_a_directory_reads_it_for_the_same_pane()
    {
        var root = new Column(
            new Location.RealDirectory(@"C:\"), [new Entry("sub", EntryKind.Directory)], Cursor: 0, Load: LoadState.Loaded);
        var state = AppState.SinglePane([root]);

        var (_, effects) = Transition.Apply(state, new Msg.EnterDirectory(state.Focused.Ref(0), 0));

        var read = Assert.Single(TestEffects.Of<Effect.ReadDirectory>(effects));
        Assert.Equal(state.Focused.Ref(1), read.At);
    }
```

Run: `dotnet test tests/Zurari.Core.Tests --filter FullyQualifiedName~PaneTests`
Expected: build FAILS (`DirectoryLoaded` takes an `int`).

- [ ] **Step 3: Change the records**

In `Msg.cs`, for **every** record with `int ColumnIndex`, replace that parameter with `ColumnRef At`
and update its XML doc (`<paramref name="ColumnIndex"/>` → `<paramref name="At"/>`). The records:
`CursorUp`, `CursorDown`, `CursorPageUp`, `CursorPageDown`, `CursorHome`, `CursorEnd`, `CursorTo`,
`FocusColumn`, `EnterDirectory`, `GoToParent`, `DirectoryLoaded`, `DirectoryLoadFailed`,
`DeleteEntry`, `DropFiles`, `ShellOpCompleted`, `ShellOpFailed`, `ToggleMark`, `ToggleMarkAtCursor`,
`ClearMarks`, `DeleteMarked`, `MarkRange`, `PasteRequested`, `PinFocusedLocation`,
`PinEntryAtCursor`, `ToggleSection`, `CollapsedGroupsRestored`, `RenameRequested`,
`RenameCompleted`, `CreateFolderRequested`, `FolderCreated`. Example:

```csharp
    /// <summary>Move the cursor of <paramref name="At"/> up by one entry.</summary>
    public sealed record CursorUp(ColumnRef At) : Msg;

    public sealed record DirectoryLoaded(ColumnRef At, Location Location, ImmutableArray<Entry> Entries) : Msg;
```

Add to the `DirectoryLoaded` doc: "Ignored when <c>At.Pane</c> no longer exists (the pane was closed)."

In `Effect.cs`, the same for `ReadDirectory`, `DeleteToRecycleBin`, `ShellCopyOrMove`, `RenameEntry`,
`CreateFolder`:

```csharp
    public sealed record ReadDirectory(ColumnRef At, Location Location, bool Speculative = false) : Effect;
```

In `AppState.cs`: `public sealed record RenameState(ColumnRef At, string EntryName, string? Error = null);`

- [ ] **Step 4: Rewrite Transition's addressing**

Delete `InRange(AppState, int)` and `WithColumn(AppState, int, Column)` from `Transition.cs`.
Every handler taking `int columnIndex` takes `ColumnRef at` instead. The rewrite rules:

| Before | After |
|---|---|
| `if (!InRange(state, columnIndex)) return …; var column = state.Columns[columnIndex];` | `if (state.ColumnAt(at) is not { } column) return …;` |
| `WithColumn(state, columnIndex, c)` | `state.WithColumn(at, c)` |
| `state with { Columns = X, FocusedColumn = Y }` | `state.WithPane(pane with { Columns = X, FocusedColumn = Y })` where `var pane = state.PaneOf(at.Pane)!;` (after the `ColumnAt` check it exists) |
| `state.Columns.Take(columnIndex + 1)` | `pane.Columns.Take(at.Index + 1)` |
| `new Effect.ReadDirectory(newColumnIndex, loc)` | `new Effect.ReadDirectory(pane.Ref(newColumnIndex), loc)` |
| `columnIndex == 0` (GoToParent) | `at.Index == 0` |

Worked example — `EnterDirectory` after the rewrite (full method):

```csharp
    private static (AppState, IReadOnlyList<Effect>) EnterDirectory(
        AppState state, ColumnRef at, int entryIndex, bool focusChild)
    {
        if (state.ColumnAt(at) is not { } column || state.PaneOf(at.Pane) is not { } pane)
        {
            return (state, NoEffects);
        }

        if (entryIndex < 0 || entryIndex >= column.Entries.Length)
        {
            return (state, NoEffects);
        }

        var entry = column.Entries[entryIndex];
        if (entry.Kind is EntryKind.File or EntryKind.Header)
        {
            var fileTruncated = pane.Columns.Take(at.Index + 1).ToImmutableArray();
            fileTruncated = fileTruncated.SetItem(at.Index, column with { Cursor = entryIndex });
            var fileState = state.WithPane(pane with { Columns = fileTruncated, FocusedColumn = at.Index });

            if (entry.Kind != EntryKind.File || EntryPath(column, entry) is not { } filePath)
            {
                return (fileState, NoEffects);
            }

            // (keep the existing disc-image comment)
            return IsDiscImage(entry.Name)
                ? (fileState, new Effect[] { new Effect.MountImage(filePath) })
                : (fileState, new Effect[] { new Effect.OpenWithDefaultApp(filePath) });
        }

        var childLocation = ChildLocation(column, entry);
        var childIndex = at.Index + 1;

        // (keep the existing "column beside the cursor is usually already showing this" comment)
        if (childIndex < pane.Columns.Length && pane.Columns[childIndex].Location == childLocation)
        {
            var reused = pane.Columns.SetItem(at.Index, column with { Cursor = entryIndex });
            return (
                state.WithPane(pane with { Columns = reused, FocusedColumn = focusChild ? childIndex : at.Index }),
                NoEffects);
        }

        var truncated = pane.Columns.Take(at.Index + 1).ToImmutableArray();
        truncated = truncated.SetItem(at.Index, column with { Cursor = entryIndex });

        var newColumnIndex = truncated.Length;
        var newColumn = new Column(childLocation, Entries: [], Cursor: -1, Load: LoadState.Loading);
        var newState = state.WithPane(pane with
        {
            Columns = truncated.Add(newColumn),
            FocusedColumn = focusChild ? newColumnIndex : at.Index,
        });
        return (newState, [new Effect.ReadDirectory(pane.Ref(newColumnIndex), childLocation)]);
    }
```

**Every-pane re-reads.** Replace the four copies of the "mark Loading and emit ReadDirectory for
each matching column" loop (`Refresh`, `ShellOpCompleted`, `ReloadDrivePanes`, `JobFinished`,
`ExternalDirectoryChanged`) with one helper:

```csharp
    /// <summary>
    /// Marks every column in every pane that <paramref name="shouldReread"/> accepts as
    /// <see cref="LoadState.Loading"/> and requests it again. A directory that changed is stale
    /// wherever it is shown, so this deliberately does not stop at one pane.
    /// </summary>
    private static (AppState, IReadOnlyList<Effect>) RereadWhere(
        AppState state, Func<ColumnRef, Column, bool> shouldReread)
    {
        var effects = new List<Effect>();
        var panes = state.Panes;
        for (var p = 0; p < panes.Length; p++)
        {
            var pane = panes[p];
            var columns = pane.Columns;
            for (var i = 0; i < columns.Length; i++)
            {
                if (!shouldReread(pane.Ref(i), columns[i]))
                {
                    continue;
                }

                columns = columns.SetItem(i, columns[i] with { Load = LoadState.Loading });
                effects.Add(new Effect.ReadDirectory(pane.Ref(i), columns[i].Location));
            }

            panes = panes.SetItem(p, pane with { Columns = columns });
        }

        return effects.Count == 0 ? (state, NoEffects) : (state with { Panes = panes }, effects);
    }
```

Uses (each replaces its whole loop):

```csharp
    // Refresh
    private static (AppState, IReadOnlyList<Effect>) Refresh(AppState state) =>
        RereadWhere(state, (_, _) => true);

    // ReloadDrivePanes
    private static (AppState, IReadOnlyList<Effect>) ReloadDrivePanes(AppState state) =>
        RereadWhere(state, (_, column) => column.Location is Location.Drives);

    // ExternalDirectoryChanged
    private static (AppState, IReadOnlyList<Effect>) ExternalDirectoryChanged(AppState state, string path) =>
        RereadWhere(state, (_, column) => IsAffectedDirectory(column.Location, [path]));

    // ShellOpCompleted, after its staleness check on `at`:
    return RereadWhere(state, (r, c) => r == at || IsAffectedDirectory(c.Location, affectedDirs));

    // JobFinished, replacing its trailing loop:
    return RereadWhere(stateWithJob, (_, c) => IsAffectedDirectory(c.Location, affectedDirs));
```

Note `ExternalDirectoryChanged` with no match now returns `(state, NoEffects)` with the *same*
instance — it used to return `state with { Columns = sameArray }`. Both are "unchanged"; if a test
asserted reference inequality, it was asserting an accident — change it to `Assert.Same`.

**`DirectoryLoaded`'s reveal**: `var focused = state.WithColumn(at, updated with { Cursor = revealed });`
then `return focused.WithPane(focused.PaneOf(at.Pane)! with { FocusedColumn = at.Index }) with { RevealTarget = null };`
(decision 4: moves that pane's focused column, never `FocusedPane`).

**Rename**: `BeginRename` stores `new RenameState(at, entry.Name)`; `SubmitRename` resolves
`state.ColumnAt(editing.At)` (return unchanged when null) and emits
`new Effect.RenameEntry(editing.At, column.Location, editing.EntryName, trimmed)`.

The post-steps (`RememberFocusedCursor`, `ReconcileChildColumn`, `ReconcilePreview`) keep reading
the shim in this task; 7a.3 rewrites them. Their `new Effect.ReadDirectory(childIndex, …)` becomes
`new Effect.ReadDirectory(state.Focused.Ref(childIndex), …)`.

The `ApplyCore` switch passes `m.At` wherever it passed `m.ColumnIndex`.

**No-op rule (from the 7a.1 review).** A message that changes nothing must return the original
`state` instance, never a rebuilt one: about 25 tests assert `Assert.Equal(state, next)` for ignored
messages, and `ImmutableArray<Pane>` compares by reference. Guard before writing (`ColumnAt` returning
`null`, unchanged cursor, …) exactly as the handlers do today; `WithPane` already returns `this` when
handed the very pane instance it holds.

- [ ] **Step 5: Runtime, Shell, App**

- `WorkerRuntime.ExecuteReadDirectory`: `new Msg.DirectoryLoaded(effect.At, …)`,
  `new Msg.DirectoryLoadFailed(effect.At, …)`, `new Msg.CollapsedGroupsRestored(effect.At, …)`.
  Same for `ExecuteRename` (`RenameCompleted(effect.At, …)`) and `ExecuteCreateFolder`
  (`FolderCreated(effect.At, …)`).
- `ShellEffectExecutor`: `ShellOpCompleted(effect.At, …)` / `ShellOpFailed(effect.At, …)`, and its
  recycle-bin `ReadDirectory` result.
- `MainWindow.xaml.cs`: every `loop.State.FocusedColumn` passed to a Msg becomes
  `loop.State.Focused.FocusedRef`; every `e.ColumnIndex` from a `ColumnBrowser` event becomes
  `loop.State.Focused.Ref(e.ColumnIndex)` (7a.11 replaces this with the pane the event came from).
- `InputTrace.cs`: follow the compiler.

- [ ] **Step 6: Migrate tests**

`dotnet build Zurari.sln 2>&1 | Select-String "error CS"` lists every site. Rules:
- `new Msg.X(0, …)` → `new Msg.X(Col(0), …)` with `using static Zurari.TestSupport.TestPanes;`
  at the top of the file.
- `new Effect.ReadDirectory(1, loc)` in expectations → `new Effect.ReadDirectory(Col(1), loc)`.
- `effect.ColumnIndex` → `effect.At.Index` (or compare the whole `At` to `Col(n)`).
- In `TransitionProperties`, `GenColumnIndex.Select(i => (Msg)new Msg.CursorUp(i))` →
  `GenColumnRef.Select(r => (Msg)new Msg.CursorUp(r))` with, for now,
  `private static Gen<ColumnRef> GenColumnRef => GenColumnIndex.Select(i => new ColumnRef(TestPanes.First, i));`
  (7a.5 widens it to other panes).

- [ ] **Step 7: Run everything**

Run: `dotnet test Zurari.sln` → all pass, including the two new `PaneTests`.
Run: `pwsh scripts/check.ps1` → `ALL CHECKS GREEN`

- [ ] **Step 8: Commit**

```bash
git add -A src tests
git commit -m "Address columns by ColumnRef(PaneId, Index) in every Msg and Effect"
```

---

### Task 7a.3: Preview addressed by pane; post-steps per pane; `ViewRestored` per pane

**Files:**
- Modify: `src/Zurari.Core/Msg.cs`, `Effect.cs`, `Transition.cs`
- Modify: `src/Zurari.Runtime/WorkerRuntime.cs`, `src/Zurari.App/MainWindow.xaml.cs`, `src/Zurari.App/StateProjection.cs`
- Test: `tests/Zurari.Core.Tests/PaneTests.cs`

- [ ] **Step 1: Failing tests**

Append to `PaneTests.cs`:

```csharp
    private static AppState TwoPanes(Column top, Column bottom) => AppState.Initial with
    {
        Panes = [new Pane(new PaneId(0), [top], 0), new Pane(new PaneId(1), [bottom], 0)],
        FocusedPane = 0,
        NextPaneId = 2,
    };

    private static Column Files(string path, params string[] names) =>
        new(new Location.RealDirectory(path), [.. names.Select(n => new Entry(n, EntryKind.File))], Cursor: 0, Load: LoadState.Loaded);

    [Fact]
    public void Moving_the_cursor_in_the_unfocused_pane_loads_that_panes_preview_only()
    {
        var state = TwoPanes(Files(@"C:\a", "x.txt", "y.txt"), Files(@"C:\b", "p.txt", "q.txt"));

        var (next, effects) = Transition.Apply(state, new Msg.CursorDown(new ColumnRef(new PaneId(1), 0)));

        var load = Assert.Single(TestEffects.Of<Effect.LoadPreview>(effects));
        Assert.Equal(new PaneId(1), load.Pane);
        Assert.Equal(@"C:\b\q.txt", load.Path);
        Assert.Equal(state.Panes[0].Preview, next.Panes[0].Preview);
    }

    [Fact]
    public void A_preview_result_lands_in_the_pane_it_was_for()
    {
        var state = TwoPanes(Files(@"C:\a", "x.txt"), Files(@"C:\b", "p.txt"));
        var (loading, _) = Transition.Apply(state, new Msg.CursorTo(new ColumnRef(new PaneId(1), 0), 0));
        var generation = loading.Panes[1].Preview.Generation;

        var (next, _) = Transition.Apply(
            loading, new Msg.PreviewLoaded(new PaneId(1), generation, PreviewKind.Text, "hello", [], null));

        Assert.Equal("hello", next.Panes[1].Preview.Text);
        Assert.Equal(loading.Panes[0].Preview, next.Panes[0].Preview);
    }

    [Fact]
    public void Sorting_changes_the_focused_panes_view_only()
    {
        var state = TwoPanes(Files(@"C:\a", "x"), Files(@"C:\b", "p"));

        var (next, _) = Transition.Apply(state, new Msg.SetSortMode(SortMode.Size));

        Assert.Equal(SortMode.Size, next.Panes[0].View.Sort.Mode);
        Assert.Equal(state.Panes[1].View, next.Panes[1].View);
    }

    [Fact]
    public void ViewRestored_applies_to_the_named_pane()
    {
        var state = TwoPanes(Files(@"C:\a", "x"), Files(@"C:\b", "p"));
        var view = ViewOptions.Default with { ShowHidden = true };

        var (next, _) = Transition.Apply(state, new Msg.ViewRestored(new PaneId(1), view));

        Assert.True(next.Panes[1].View.ShowHidden);
        Assert.False(next.Panes[0].View.ShowHidden);
    }
```

(Two-pane states are built by hand here; `ToggleSplit` arrives in 7a.6.)

Run: `dotnet test tests/Zurari.Core.Tests --filter FullyQualifiedName~PaneTests` → build FAILS.

- [ ] **Step 2: Records**

```csharp
// Msg.cs
public sealed record PreviewLoaded(
    PaneId Pane, int Generation, PreviewKind Kind, string? Text, ImmutableArray<byte> ImageBytes,
    string? BinaryLabel, PreviewMetadata? Metadata = null, bool TimedOut = false) : Msg;
public sealed record PreviewFailed(PaneId Pane, int Generation, string Error) : Msg;
public sealed record PreviewCapacityLoaded(PaneId Pane, int Generation, PreviewCapacity Capacity) : Msg;
public sealed record RetryPreview(PaneId Pane) : Msg;
public sealed record ViewRestored(PaneId Pane, ViewOptions View) : Msg;

// Effect.cs
public sealed record LoadPreview(
    PaneId Pane, int Generation, string Path, PreviewTarget Target = PreviewTarget.File, int Attempt = 1) : Effect;
public sealed record CancelPreview(PaneId Pane, int Generation) : Effect;
```

Update their docs: "Ignored unless <paramref name="Pane"/> still exists and <paramref name="Generation"/>
matches that pane's <see cref="Pane.Preview"/> generation."

- [ ] **Step 3: Transition — preview handlers**

`PreviewLoaded`, `PreviewFailed`, `PreviewCapacityLoaded`, `RetryPreview` take the `PaneId`, look up
`state.PaneOf(pane)` (return `state` unchanged when null), compare against `pane.Preview.Generation`,
and write back with `state.WithPane(pane with { Preview = … })`. Example:

```csharp
    private static AppState PreviewFailed(AppState state, PaneId paneId, int generation, string error)
    {
        if (state.PaneOf(paneId) is not { } pane || generation != pane.Preview.Generation)
        {
            return state;
        }

        return state.WithPane(pane with { Preview = pane.Preview with { Kind = PreviewKind.None, Error = error } });
    }
```

`RetryPreview` uses `ResolveCursorFileTarget(pane)` (below) and emits
`new Effect.LoadPreview(pane.Id, generation, preview.Path, target.Kind, attempt)`.

- [ ] **Step 4: Transition — post-steps per pane**

Change `ResolveCursorFileTarget(AppState)` to `ResolveCursorFileTarget(Pane pane)` (read
`pane.FocusedColumnOrNull` instead of `state.Columns[state.FocusedColumn]`; body otherwise unchanged).

`Apply` becomes:

```csharp
    public static (AppState State, IReadOnlyList<Effect> Effects) Apply(AppState state, Msg msg)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(msg);

        var (applied, effects) = ApplyCore(state, msg);
        var remembered = RememberFocusedCursor(state, applied);
        var result = (State: remembered, Effects: effects);
        foreach (var pane in remembered.Panes)
        {
            // Per pane, compared against the same pane before the message. A pane that did not
            // exist before (just opened) compares against nothing, so its child column and preview
            // are set up by the same code that keeps them current.
            var before = state.PaneOf(pane.Id);
            result = ReconcileChildColumn(before, pane.Id, result.State, result.Effects);
            result = ReconcilePreview(before, pane.Id, result.State, result.Effects);
        }

        return result;
    }
```

`RememberFocusedCursor(oldState, state)`: read `state.Focused.FocusedColumnOrNull` and compare with the old
state's pane of the same id (`oldState.PaneOf(state.Focused.Id)?.FocusedColumnOrNull`).

`ReconcileChildColumn(Pane? before, PaneId id, AppState state, IReadOnlyList<Effect> effects)`: the
existing body, with `state.Columns`/`FocusedColumn` replaced by `pane = state.PaneOf(id)!`'s, the
old-side comparison using `before?.FocusedColumnOrNull`, the write via `state.WithPane(pane with { Columns = … })`,
and `new Effect.ReadDirectory(pane.Ref(childIndex), wanted, Speculative: true)`.

`ReconcilePreview(Pane? before, PaneId id, AppState state, IReadOnlyList<Effect> effects)`: the
existing body with `oldState.Preview` → `before?.Preview ?? PreviewState.Initial`,
`ResolveCursorFileTarget(oldState)` → `before is null ? default : ResolveCursorFileTarget(before)`,
the new side from `pane = state.PaneOf(id)!`, writes via `state.WithPane(pane with { Preview = … })`,
and effects `new Effect.CancelPreview(id, …)` / `new Effect.LoadPreview(id, …)`. `Notice = null`
stays a whole-state write (a cursor move in either pane ends the notice).

- [ ] **Step 5: Transition — view per pane**

`Rederive(state, view, remember)` re-derives the **focused pane's** columns only, writes
`Focused with { Columns = …, View = view }`, and (unchanged for now) emits
`new Effect.SetViewOptions(view)`. The `SetSortMode`/`ToggleDirectoriesFirst`/`ToggleHiddenFiles`/
`SetSortDescending` arms read `state.Focused.View` instead of `state.View`.
`ViewRestored` gets its own helper: re-derive the named pane (unchanged state when it does not
exist), `remember: false`. `DirectoryLoaded` derives with the addressed pane's view:
`column.WithAllEntries(carried, pane.View)` — look the pane up with `state.PaneOf(at.Pane)`.
Every other `state.View` use in Transition (mark changes via `WithMarkChange`, `ToggleSection`,
`RestoreCollapsedGroups`) uses the view of the pane its `ColumnRef` names.

- [ ] **Step 6: Runtime and App**

- `WorkerRuntime`: `StartPreview`/`RunPreview`/`ExecuteLoadPreview` post
  `PreviewLoaded(effect.Pane, effect.Generation, …)` etc. `CancelPreview` passes
  `cancelPreview.Generation` as before (keying by pane is 7a.9). After the drive-list read, post
  `new Msg.ViewRestored(effect.At.Pane, ReadViewOptions(settings))`.
- `MainWindow`: `OnPreviewRetryClick` → `Dispatch(new Msg.RetryPreview(loop.State.Focused.Id))`.
  `StateProjection.ProjectPreview(state)` reads `state.Focused.Preview`.

- [ ] **Step 7: Run, check, commit**

Run: `dotnet test Zurari.sln` → PASS (fix test compile errors with the same rules as 7a.2: preview
Msgs get `TestPanes.First` as their first argument).
Run: `pwsh scripts/check.ps1` → `ALL CHECKS GREEN`

```bash
git add -A src tests
git commit -m "Keep each pane's preview and view options to itself"
```

---

### Task 7a.4: Remove the compatibility shim

**Files:** `src/Zurari.Core/AppState.cs`, every file the compiler then flags.

- [ ] **Step 1: Delete the shim block** (the four properties between the `7a compatibility shim`
  markers) from `AppState.cs`.

- [ ] **Step 2: Build and fix every error**

Run: `dotnet build Zurari.sln 2>&1 | Select-String "error CS"`. Rules:
- Production code: `state.Columns` → `state.Focused.Columns`, `state.FocusedColumn` →
  `state.Focused.FocusedColumn`, `state.Preview` → `state.Focused.Preview`, `state.View` →
  `state.Focused.View` — **only where "the focused pane" is really what is meant** (App rendering,
  status bar, input trace, `Render`'s change detection). In Transition there should be none left
  after 7a.3; if one appears, it is a bug the shim was hiding — resolve it to the pane its message
  addresses, and add a `PaneTests` case for it.
- Tests: `AppState.Initial with { Columns = [..cols], FocusedColumn = n }` and
  `new AppState { Columns = …, FocusedColumn = … }` → `AppState.SinglePane([..cols], n)`
  (then `with { Jobs = … }` etc. as before). Reads `state.Columns[…]` → `state.Focused.Columns[…]`,
  `state.Preview` → `state.Focused.Preview`, `state.View` → `state.Focused.View`.
- `TransitionTests.StateWithColumns(params Column[] columns)` becomes
  `AppState.SinglePane([.. columns])`.
- `AssertStatesEqual` in `TransitionProperties`: compare `FocusedPane`, `NextPaneId`, `Panes.Length`,
  and per pane `Id`, `FocusedColumn`, `Preview`, `View` and `Columns` (element-wise as today).

- Add `AppState.ViewAt(ColumnRef at)` (the view of the pane `at` names) next to `ColumnAt`, and use
  it for the six `state.PaneOf(at.Pane)!.View` sites in Transition, dropping those `!`s.
- In `Apply`'s per-pane loop, skip both reconcile steps for a pane whose instance is reference-equal
  to its `before` snapshot: a message that did not touch a pane needs no reconciling, and this makes
  the unfocused pane free on every keystroke instead of costing several `PaneOf` scans.
- Make `AppState.Panes` `required` again (it lost `required` only so the shim's `init` accessors
  had a pane to write into), so `new AppState()` without panes stops compiling. `Initial` and
  `SinglePane` set it.

- [ ] **Step 3: Run, check, commit**

Run: `dotnet test Zurari.sln` → PASS. `pwsh scripts/check.ps1` → `ALL CHECKS GREEN`

```bash
git add -A src tests
git commit -m "Drop the single-pane shim: everything addresses a pane explicitly"
```

---

### Task 7a.5: Property tests cover other panes

**Files:** `tests/Zurari.Core.Tests/TransitionTests.cs` (`TransitionProperties`)

- [ ] **Step 1: Widen the generators**

```csharp
    /// <summary>
    /// Pane 0 exists from the start; 1 exists once a split is opened (7a.6 adds ToggleSplit to
    /// GenMsg); 7 never does, so results addressed to it exercise the closed-pane path.
    /// </summary>
    private static Gen<PaneId> GenPaneId => Gen.OneOfConst(new PaneId(0), new PaneId(1), new PaneId(7));

    private static Gen<ColumnRef> GenColumnRef =>
        Gen.Select(GenPaneId, GenColumnIndex).Select(t => new ColumnRef(t.Item1, t.Item2));
```

Preview Msgs use `GenPaneId` for their pane; `ViewRestored` uses
`Gen.Select(GenPaneId, …)`. `RetryPreview` becomes `GenPaneId.Select(p => (Msg)new Msg.RetryPreview(p))`.

- [ ] **Step 2: Per-pane generation monotonicity**

In `Any_msg_sequence_yields_valid_state_and_effects`, replace the single generation assertion with:

```csharp
                foreach (var pane in next.Panes)
                {
                    if (state.PaneOf(pane.Id) is { } before)
                    {
                        Assert.True(pane.Preview.Generation >= before.Preview.Generation);
                    }
                }
```

- [ ] **Step 3: Three example tests the 7a.3 review asked for** (in `PaneTests.cs`; `TwoPanes` and `Dir`
  are that file's existing helpers)

```csharp
    [Fact]
    public void A_preview_result_for_one_pane_is_ignored_by_the_other_at_the_same_generation()
    {
        // Both panes count generations from their own start, so the same number means two different
        // files. An implementation that checked only the generation would pass the earlier tests.
        var state = TwoPanes(Dir(@"C:\a", "x.txt"), Dir(@"C:\b", "p.txt"));
        var (loadedA, _) = Transition.Apply(state, new Msg.CursorTo(new ColumnRef(new PaneId(0), 0), 0));
        var (both, _) = Transition.Apply(loadedA, new Msg.CursorTo(new ColumnRef(new PaneId(1), 0), 0));
        Assert.Equal(both.Panes[0].Preview.Generation, both.Panes[1].Preview.Generation);

        var (next, _) = Transition.Apply(
            both, new Msg.PreviewLoaded(new PaneId(1), both.Panes[1].Preview.Generation, PreviewKind.Text, "hello", [], null));

        Assert.Equal("hello", next.Panes[1].Preview.Text);
        Assert.Null(next.Panes[0].Preview.Text);
    }

    [Fact]
    public void Leaving_a_file_cancels_that_panes_preview_and_names_the_pane()
    {
        var state = TwoPanes(Dir(@"C:\a", "x.txt"), Dir(@"C:\b", "p.txt", "q"));
        var (loading, _) = Transition.Apply(state, new Msg.CursorTo(new ColumnRef(new PaneId(1), 0), 0));
        var generation = loading.Panes[1].Preview.Generation;

        var (_, effects) = Transition.Apply(loading, new Msg.CursorTo(new ColumnRef(new PaneId(1), 0), 1));

        var cancelled = Assert.Single(TestEffects.Of<Effect.CancelPreview>(effects));
        Assert.Equal(new PaneId(1), cancelled.Pane);
        Assert.Equal(generation, cancelled.Generation);
    }

    [Fact]
    public void Refreshing_both_panes_leaves_both_previews_alone()
    {
        var state = TwoPanes(Dir(@"C:\a", "x.txt"), Dir(@"C:\b", "p.txt"));

        var (next, effects) = Transition.Apply(state, new Msg.Refresh());

        Assert.Equal(2, TestEffects.Of<Effect.ReadDirectory>(effects).Count);
        Assert.Empty(TestEffects.Of<Effect.LoadPreview>(effects));
        Assert.Empty(TestEffects.Of<Effect.CancelPreview>(effects));
        Assert.Equal(state.Panes[0].Preview, next.Panes[0].Preview);
        Assert.Equal(state.Panes[1].Preview, next.Panes[1].Preview);
    }
```

- [ ] **Step 4: The focused pane is not always pane 0 (7a.4 review)**

Until 7a.6 adds `FocusPane`, no valid test state has `FocusedPane = 1`, so a regression from "the
focused pane" to "`Panes[0]`" in `Rederive`, `RememberFocusedCursor`, `ProjectStatusBar`,
`InputTrace.State` or `Render` would pass every test. Give `PaneTests.TwoPanes` a third parameter
`int focused = 0` (set `FocusedPane = focused`), fix its stale doc line about "7a.3 may add its own
`TwoPanes`", and add:

```csharp
    [Fact]
    public void Sorting_with_the_bottom_pane_focused_changes_only_the_bottom_view()
    {
        var state = TwoPanes(Dir(@"C:\a", "x"), Dir(@"C:\b", "p"), focused: 1);

        var (next, _) = Transition.Apply(state, new Msg.SetSortMode(SortMode.Size));

        Assert.Equal(SortMode.Size, next.Panes[1].View.Sort.Mode);
        Assert.Equal(state.Panes[0].View, next.Panes[0].View);
    }

    [Fact]
    public void A_cursor_move_in_the_focused_bottom_pane_is_remembered()
    {
        var state = TwoPanes(Dir(@"C:\a", "x"), Dir(@"C:\b", "p", "q"), focused: 1);

        var (next, _) = Transition.Apply(state, new Msg.CursorDown(new ColumnRef(new PaneId(1), 0)));

        Assert.Equal("q", next.RecallCursor(new Location.RealDirectory(@"C:\b")));
        Assert.Null(next.RecallCursor(new Location.RealDirectory(@"C:\a")));
    }
```

and in `tests/Zurari.App.Tests/StatusBarProjectionTests.cs` one case whose state has
`FocusedPane = 1` (bottom pane at a different path), asserting the path bar shows the bottom pane's
path.

Add a test-side helper per test project where the pattern `x.WithPane(x.Focused with { ... })`
repeats (about 15 sites): an extension `state.WithFocused(Func<Pane, Pane> change)` in
`tests/TestSupport/` (linked like `TestPanes.cs`), and use it where it removes an intermediate local.
In `InputTraceTests.cs` and `StatusBarProjectionTests.cs` the locals named `withView` hold the state
*before* the view is set - inline them or rename them `initial`.

- [ ] **Step 5: Make the determinism property compare everything (7a.4 review)**

`AssertStatesEqual` in `TransitionProperties` is a hand-kept field list that has drifted: it skips
`CursorMemory`, `Notice`, `InputMode`, `Rename`, `RevealTarget`, most of `PreviewState` (`Metadata`,
`Attempt`, `TimedOut`, `OriginalPath`, `Capacity`) and `Column.AllEntries`/`CollapsedGroups`.
Replace it with record equality on copies whose array fields are blanked, plus element-wise
comparison of those arrays, so a new scalar field is covered without anyone remembering to add it:

```csharp
    private static void AssertStatesEqual(AppState expected, AppState actual)
    {
        Assert.Equal(
            expected with { Panes = default, Jobs = default, CursorMemory = default, CutPending = default },
            actual with { Panes = default, Jobs = default, CursorMemory = default, CutPending = default });
        Assert.Equal(expected.Jobs, actual.Jobs);
        Assert.Equal(expected.CursorMemory, actual.CursorMemory);
        Assert.Equal(expected.CutPending, actual.CutPending);
        Assert.Equal(expected.Panes.Length, actual.Panes.Length);
        for (var p = 0; p < expected.Panes.Length; p++)
        {
            var e = expected.Panes[p];
            var a = actual.Panes[p];
            Assert.Equal(e with { Columns = default, Preview = PreviewState.Initial }, a with { Columns = default, Preview = PreviewState.Initial });
            Assert.Equal(e.Preview with { ImageBytes = default }, a.Preview with { ImageBytes = default });
            Assert.Equal(e.Preview.ImageBytes, a.Preview.ImageBytes);
            Assert.Equal(e.Columns.Length, a.Columns.Length);
            for (var c = 0; c < e.Columns.Length; c++)
            {
                var ec = e.Columns[c];
                var ac = a.Columns[c];
                Assert.Equal(ec with { Entries = default, AllEntries = default }, ac with { Entries = default, AllEntries = default });
                Assert.Equal(ec.Entries, ac.Entries);
                Assert.Equal(ec.AllEntries, ac.AllEntries);
            }
        }
    }
```

Adjust to the records' real members if any differ (e.g. if `Job` or `PreviewCapacity` hold arrays,
blank and compare them the same way). `with { AllEntries = default }` goes through `Column`'s
`init` accessor; if that throws or misbehaves on `default`, compare `AllEntries` before blanking and
blank only `Entries`.

- [ ] **Step 6: Run, check, commit**

Run: `dotnet test tests/Zurari.Core.Tests` → PASS. `pwsh scripts/check.ps1` → green.

```bash
git add tests
git commit -m "Generate messages for other panes, including ones that do not exist"
```

**7a-front is done** when 7a.1-7a.5 are committed and the app, run by hand, behaves exactly as
before (browse, preview, sort, rename, paste, drive pane). Record that check in the Records section.

---

# 7a-back — Split UI

## File map

| File | Change |
|---|---|
| `src/Zurari.Core/Msg.cs` | `ToggleSplit`, `FocusPane`, `SwapPanes`, `SyncToOtherPane`, `CopyToOtherPane` |
| `src/Zurari.Core/Effect.cs` | `SetViewOptions` → `SetPaneViews` |
| `src/Zurari.Core/Transition.cs` | Handlers for the above |
| `tests/Zurari.Core.Tests/SplitTests.cs` | **Create.** |
| `src/Zurari.Runtime/WorkerRuntime.cs` | Preview slot per `PaneId`; `SetPaneViews`; restore reads `PaneViews[0]` |
| `src/Zurari.Runtime/UserSettingsStore.cs` | `SplitOpen`, `SplitRatio`, `PaneViews` |
| `src/Zurari.App/PaneView.xaml(.cs)` | **Create.** Browser + splitter + preview |
| `src/Zurari.App/MainWindow.xaml(.cs)` | Two `PaneView` rows, keys, focus, gates per pane |
| `src/Zurari.App/StateProjection.cs` | Project a given pane |
| `src/Zurari.App/KeyMap.cs` | Split keys |
| `tests/Zurari.App.Tests/SplitE2ETests.cs` | **Create.** |

---

### Task 7a.6: `ToggleSplit` and `FocusPane`

**Files:** `Msg.cs`, `Transition.cs`, `tests/Zurari.Core.Tests/SplitTests.cs`, `TransitionTests.cs` (GenMsg)

- [ ] **Step 1: Failing tests** — create `tests/Zurari.Core.Tests/SplitTests.cs`:

```csharp
using System.Collections.Immutable;
using Zurari.Core;

namespace Zurari.Core.Tests;

public class SplitTests
{
    private static Column Files(string path, params string[] names)
    {
        var entries = ImmutableArray.CreateBuilder<Entry>(names.Length);
        foreach (var name in names)
        {
            entries.Add(new Entry(name, EntryKind.File));
        }

        return new Column(
            new Location.RealDirectory(path),
            entries.MoveToImmutable(),
            Cursor: names.Length > 0 ? 0 : -1,
            Load: LoadState.Loaded);
    }

    private static AppState Browsing() =>
        AppState.SinglePane([Files(@"C:\a", "x.txt", "y.txt")]);

    [Fact]
    public void Opening_adds_a_bottom_pane_at_the_focused_location_and_keeps_focus()
    {
        var state = Browsing();

        var (next, effects) = Transition.Apply(state, new Msg.ToggleSplit());

        Assert.Equal(2, next.Panes.Length);
        Assert.Equal(0, next.FocusedPane);
        Assert.Equal(new PaneId(1), next.Panes[1].Id);
        Assert.Equal(2, next.NextPaneId);
        Assert.Equal(new Location.RealDirectory(@"C:\a"), next.Panes[1].Columns[0].Location);
        Assert.Equal(LoadState.Loading, next.Panes[1].Columns[0].Load);
        var read = Assert.Single(TestEffects.Of<Effect.ReadDirectory>(effects));
        Assert.Equal(new ColumnRef(new PaneId(1), 0), read.At);
        Assert.Empty(next.CheckInvariants());
    }

    [Fact]
    public void Opening_copies_the_focused_view_unless_one_is_given()
    {
        var state = Browsing();
        state = state.WithPane(state.Focused with { View = ViewOptions.Default with { ShowHidden = true } });
        var given = ViewOptions.Default with { Sort = SortOrder.Default with { Mode = SortMode.Size } };

        var (copied, _) = Transition.Apply(state, new Msg.ToggleSplit());
        var (restored, _) = Transition.Apply(state, new Msg.ToggleSplit(given));

        Assert.True(copied.Panes[1].View.ShowHidden);
        Assert.Equal(SortMode.Size, restored.Panes[1].View.Sort.Mode);
    }

    [Fact]
    public void Closing_keeps_only_the_focused_pane()
    {
        var (split, _) = Transition.Apply(Browsing(), new Msg.ToggleSplit());
        var (focusedBottom, _) = Transition.Apply(split, new Msg.FocusPane(new PaneId(1)));

        var (closed, _) = Transition.Apply(focusedBottom, new Msg.ToggleSplit());

        Assert.Single(closed.Panes);
        Assert.Equal(new PaneId(1), closed.Panes[0].Id);
        Assert.Equal(0, closed.FocusedPane);
        Assert.Empty(closed.CheckInvariants());
    }

    [Fact]
    public void A_result_for_the_closed_pane_is_ignored()
    {
        var (split, _) = Transition.Apply(Browsing(), new Msg.ToggleSplit());
        var (closed, _) = Transition.Apply(split, new Msg.ToggleSplit());

        var (next, effects) = Transition.Apply(
            closed, new Msg.DirectoryLoaded(new ColumnRef(new PaneId(1), 0), new Location.RealDirectory(@"C:\a"), []));

        Assert.Same(closed, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void Reopening_gets_a_fresh_pane_id()
    {
        var (split, _) = Transition.Apply(Browsing(), new Msg.ToggleSplit());
        var (closed, _) = Transition.Apply(split, new Msg.ToggleSplit());
        var (reopened, _) = Transition.Apply(closed, new Msg.ToggleSplit());

        Assert.Equal(new PaneId(2), reopened.Panes[1].Id);
    }

    [Fact]
    public void FocusPane_moves_focus_and_ignores_unknown_panes()
    {
        var (split, _) = Transition.Apply(Browsing(), new Msg.ToggleSplit());

        var (bottom, _) = Transition.Apply(split, new Msg.FocusPane(new PaneId(1)));
        var (ignored, _) = Transition.Apply(split, new Msg.FocusPane(new PaneId(9)));

        Assert.Equal(1, bottom.FocusedPane);
        Assert.Same(split, ignored);
    }

    [Fact]
    public void Focusing_the_other_pane_ends_sort_mode_and_a_rename()
    {
        var (split, _) = Transition.Apply(Browsing(), new Msg.ToggleSplit());
        var (sorting, _) = Transition.Apply(split, new Msg.EnterSortMode());

        var (moved, _) = Transition.Apply(sorting, new Msg.FocusPane(new PaneId(1)));

        Assert.Equal(InputMode.Normal, moved.InputMode);
        Assert.Null(moved.Rename);
    }
}
```

Run: `dotnet test tests/Zurari.Core.Tests --filter FullyQualifiedName~SplitTests` → build FAILS.

- [ ] **Step 2: Msgs**

```csharp
    /// <summary>
    /// Ctrl+W. With one pane, adds a second below it, showing the focused column's location and
    /// with <paramref name="NewPaneView"/> (or, when null, a copy of the focused pane's view). Focus
    /// stays where it was. With two, closes the one that does not have focus.
    /// </summary>
    public sealed record ToggleSplit(ViewOptions? NewPaneView = null) : Msg;

    /// <summary>
    /// Gives <paramref name="Pane"/> keyboard focus. Ignored for a pane that does not exist. Ends
    /// sort mode and any rename, which belong to the pane being left.
    /// </summary>
    public sealed record FocusPane(PaneId Pane) : Msg;
```

- [ ] **Step 3: Handlers**

```csharp
    private static (AppState, IReadOnlyList<Effect>) ToggleSplit(AppState state, ViewOptions? newPaneView)
    {
        if (state.Panes.Length == 2)
        {
            var kept = state.Focused;
            return (state with { Panes = [kept], FocusedPane = 0, Rename = null, InputMode = InputMode.Normal }, NoEffects);
        }

        var source = state.Focused.FocusedColumnOrNull;
        if (source is null)
        {
            return (state, NoEffects);
        }

        var id = new PaneId(state.NextPaneId);
        var column = new Column(source.Location, Entries: [], Cursor: -1, Load: LoadState.Loading);
        var pane = new Pane(id, [column], 0) { View = newPaneView ?? state.Focused.View };
        var next = state with { Panes = state.Panes.Add(pane), NextPaneId = state.NextPaneId + 1 };
        return (next, [new Effect.ReadDirectory(pane.Ref(0), source.Location)]);
    }

    private static AppState FocusPane(AppState state, PaneId id)
    {
        var index = state.IndexOfPane(id);
        if (index < 0 || index == state.FocusedPane)
        {
            return state;
        }

        return state with { FocusedPane = index, Rename = null, InputMode = InputMode.Normal };
    }
```

Wire them in `ApplyCore`:
`Msg.ToggleSplit m => ToggleSplit(state, m.NewPaneView),`
`Msg.FocusPane m => (FocusPane(state, m.Pane), NoEffects),`

(`SetPaneViews` is emitted from 7a.12 on; not here.)

- [ ] **Step 4: GenMsg, and make the property tests reach two panes (7a.5 review)**

```csharp
        Gen.Const<Msg>(new Msg.ToggleSplit()),
        GenPaneId.Select(p => (Msg)new Msg.FocusPane(p)),
```

With `ToggleSplit` in `GenMsg` the sequence property finally sees two panes - but closing keeps the
*focused* pane and reopening takes `NextPaneId`, so after one close-and-reopen the live ids are 0
and 2 (or just 1). `GenPaneId = {0, 1, 7}` would then never address the live bottom pane again.
Widen and weight it, and rewrite its doc comment to say why:

```csharp
    /// <summary>
    /// Ids a generated message may address. ToggleSplit hands out NextPaneId on every open and keeps
    /// the focused pane on close, so after a few toggles the live panes can be any small id; 7 is
    /// never reached in a 50-message sequence and exercises the closed-pane path. Pane 0 is weighted
    /// up so chains of messages to the one pane that always exists at the start stay common.
    /// </summary>
    private static Gen<PaneId> GenPaneId => Gen.Frequency(
        (3, Gen.Const(new PaneId(0))),
        (1, Gen.Const(new PaneId(1))),
        (1, Gen.Const(new PaneId(2))),
        (1, Gen.Const(new PaneId(3))),
        (1, Gen.Const(new PaneId(7))));
```

(If CsCheck's `Gen.Frequency` signature differs, use its equivalent; keep the weights.)

Make `Apply_is_deterministic` multi-step: generate a prefix `GenMsg.List[0, 20]`, run it from
`AppState.Initial` to reach `s`, then apply one generated message to `s` twice and compare with
`AssertStatesEqual`. Today it applies one message to `Initial`, so jobs, cursor memory, collapsed
sections and two panes are almost never present and most of the comparator is never exercised.
Effects are compared with `Assert.Equal(first.Effects, second.Effects)`, i.e. record equality, which
compares array members (`StartJob.Sources`, `SetCollapsedGroups.Groups`, delete `Targets`, paste
`Paths`) by reference. Two runs from the same `s` share the generated message's arrays but build
new ones in Core, so add a small structural comparison for effects with array members (blank with
`with { X = default }` and compare `X` element-wise), the same way `AssertStatesEqual` does.

Add a closed-pane property: over arbitrary prefix states, a cursor or preview message addressed to
`PaneId(7)` (`CursorUp/Down/To`, `EnterDirectory`, `PreviewLoaded`, `PreviewFailed`, `RetryPreview`)
returns the identical state instance (`Assert.Same`) and no effects. Restricted to pane-addressed
messages on purpose: messages that fan out to every pane (`Refresh`, `ExternalDirectoryChanged`,
job results) legitimately change state regardless of the pane id they carry.

- [ ] **Step 5: Run, check, commit**

Run: `dotnet test tests/Zurari.Core.Tests` → PASS. `pwsh scripts/check.ps1` → green.

```bash
git add -A src/Zurari.Core tests/Zurari.Core.Tests
git commit -m "Split into two panes and move focus between them"
```

---

### Task 7a.7: `SwapPanes` and `SyncToOtherPane`

**Files:** `Msg.cs`, `Transition.cs`, `SplitTests.cs`, GenMsg

- [ ] **Step 1: Failing tests** — append to `SplitTests.cs`:

```csharp
    private static AppState SplitWith(Column top, Column bottom) => AppState.Initial with
    {
        Panes = [new Pane(new PaneId(0), [top], 0), new Pane(new PaneId(1), [bottom], 0)],
        FocusedPane = 0,
        NextPaneId = 2,
    };

    [Fact]
    public void Swap_exchanges_the_panes_and_keeps_focus_on_the_same_position()
    {
        var state = SplitWith(Files(@"C:\a", "x"), Files(@"C:\b", "p"));

        var (next, _) = Transition.Apply(state, new Msg.SwapPanes());

        Assert.Equal(new PaneId(1), next.Panes[0].Id);
        Assert.Equal(new PaneId(0), next.Panes[1].Id);
        Assert.Equal(0, next.FocusedPane);
    }

    [Fact]
    public void Swap_with_one_pane_does_nothing()
    {
        var state = Browsing();

        var (next, effects) = Transition.Apply(state, new Msg.SwapPanes());

        Assert.Same(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void Sync_copies_the_other_panes_columns_without_reading_anything()
    {
        var other = Files(@"C:\b", "p", "q") with { Cursor = 1 };
        var state = SplitWith(Files(@"C:\a", "x"), other);

        var (next, effects) = Transition.Apply(state, new Msg.SyncToOtherPane());

        Assert.Equal(new Location.RealDirectory(@"C:\b"), next.Panes[0].Columns[0].Location);
        Assert.Equal("q", next.Panes[0].Columns[0].CursorName);
        Assert.Equal(new PaneId(0), next.Panes[0].Id);
        Assert.Empty(TestEffects.Of<Effect.ReadDirectory>(effects));
    }

    [Fact]
    public void Sync_re_derives_the_copied_columns_under_this_panes_view()
    {
        var other = new Column(
            new Location.RealDirectory(@"C:\b"),
            [new Entry("big", EntryKind.File, SizeBytes: 9), new Entry("small", EntryKind.File, SizeBytes: 1)],
            Cursor: 0, Load: LoadState.Loaded);
        var state = SplitWith(Files(@"C:\a", "x"), other);
        state = state.WithPane(state.Panes[0] with
        {
            View = ViewOptions.Default with { Sort = SortOrder.Default with { Mode = SortMode.Size } },
        });

        var (next, _) = Transition.Apply(state, new Msg.SyncToOtherPane());

        Assert.Equal("small", next.Panes[0].Columns[0].Entries[0].Name);
    }
```

Run → build FAILS.

- [ ] **Step 2: Msgs**

```csharp
    /// <summary>Shift+O. Exchanges the two panes; focus stays on the same position (top stays top).</summary>
    public sealed record SwapPanes : Msg;

    /// <summary>
    /// <c>o</c>. Makes the focused pane show what the other pane shows - the same columns and focused
    /// column, re-derived under this pane's own view. Nothing is read again.
    /// </summary>
    public sealed record SyncToOtherPane : Msg;
```

- [ ] **Step 3: Handlers**

```csharp
    // A rename in progress belongs to a row on screen; swapping moves panes between rows and syncing
    // replaces the columns it was typed into, so both end it (as FocusPane does).
    private static AppState SwapPanes(AppState state) =>
        state.Panes.Length == 2
            ? state with { Panes = [state.Panes[1], state.Panes[0]], Rename = null, InputMode = InputMode.Normal }
            : state;

    private static AppState SyncToOtherPane(AppState state)
    {
        if (state.Panes.Length != 2)
        {
            return state;
        }

        var mine = state.Focused;
        var other = state.Panes[1 - state.FocusedPane];
        var columns = ImmutableArray.CreateBuilder<Column>(other.Columns.Length);
        foreach (var column in other.Columns)
        {
            columns.Add(column.WithView(mine.View));
        }

        return state.WithPane(mine with { Columns = columns.MoveToImmutable(), FocusedColumn = other.FocusedColumn })
            with { Rename = null };
    }
```

`ApplyCore`: `Msg.SwapPanes => (SwapPanes(state), NoEffects),`
`Msg.SyncToOtherPane => (SyncToOtherPane(state), NoEffects),`
(The per-pane post-steps then start the preview for the synced pane's cursor.)

- [ ] **Step 4: GenMsg** — `Gen.Const<Msg>(new Msg.SwapPanes()), Gen.Const<Msg>(new Msg.SyncToOtherPane()),`

**Rename invariant (7a.6 review).** Add to `CheckInvariants`: when `Rename` is not null, `Rename.At.Pane`
names a live pane. (The stronger "is the focused pane" would need a focused-pane gate in
`RenameRequested`, which `GenMsg` addresses to any pane; the weaker form is what swap, sync, close
and focus must all preserve, and the property test then enforces it.) If it fails, the handler that
let a rename outlive its pane is the bug.

**File size (7a.6 review).** `Transition.cs` is ~1800 lines and `TransitionTests.cs` ~3060. Move the
pane-level handlers (`ToggleSplit`, `FocusPane`, `SwapPanes`, `SyncToOtherPane`, and 7a.8's
`CopyToOtherPane` when it lands) into a `partial` `Transition.Split.cs`, and the property-test class
`TransitionProperties` into its own `TransitionProperties.cs`. Moves only, in their own commit.

- [ ] **Step 5: Run, check, commit**

```bash
git add -A src/Zurari.Core tests/Zurari.Core.Tests
git commit -m "Swap the panes, and point one at what the other shows"
```

---

### Task 7a.8: `CopyToOtherPane` (`c` / `m`)

The destination rule is zrr's `ResolvePaneDirectory`: the folder under the other pane's cursor if
the cursor is on one, else that column's own location. Everything after choosing the destination
is `PasteRequested`.

**Files:** `Msg.cs`, `Transition.cs`, `SplitTests.cs`, GenMsg

- [ ] **Step 1: Failing tests** — append to `SplitTests.cs`:

```csharp
    private static Column Mixed(string path, int cursor) => new(
        new Location.RealDirectory(path),
        [new Entry("sub", EntryKind.Directory), new Entry("f.txt", EntryKind.File)],
        Cursor: cursor, Load: LoadState.Loaded);

    [Fact]
    public void Copies_the_cursor_entry_into_the_folder_under_the_other_cursor()
    {
        var state = SplitWith(Mixed(@"C:\src", cursor: 1), Mixed(@"D:\dst", cursor: 0));

        var (next, effects) = Transition.Apply(state, new Msg.CopyToOtherPane(IsMove: false));

        var job = Assert.Single(TestEffects.Of<Effect.RunFileJob>(effects));
        Assert.Equal(@"D:\dst\sub", job.DestDir);
        Assert.Equal([@"C:\src\f.txt"], job.Sources);
        Assert.Equal(JobKind.Copy, job.Kind);
        Assert.Single(next.Jobs);
    }

    [Fact]
    public void Uses_the_other_columns_own_location_when_its_cursor_is_on_a_file()
    {
        var state = SplitWith(Mixed(@"C:\src", cursor: 1), Mixed(@"D:\dst", cursor: 1));

        var (_, effects) = Transition.Apply(state, new Msg.CopyToOtherPane(IsMove: true));

        var job = Assert.Single(TestEffects.Of<Effect.RunFileJob>(effects));
        Assert.Equal(@"D:\dst", job.DestDir);
        Assert.Equal(JobKind.Move, job.Kind);
    }

    [Fact]
    public void Marked_entries_are_copied_instead_of_the_cursor_entry()
    {
        var source = Mixed(@"C:\src", cursor: 1);
        source = source.WithAllEntries(
            [new Entry("sub", EntryKind.Directory, IsMarked: true), new Entry("f.txt", EntryKind.File)],
            ViewOptions.Default);
        var state = SplitWith(source, Mixed(@"D:\dst", cursor: 1));

        var (_, effects) = Transition.Apply(state, new Msg.CopyToOtherPane(IsMove: false));

        Assert.Equal([@"C:\src\sub"], Assert.Single(TestEffects.Of<Effect.RunFileJob>(effects)).Sources);
    }

    [Fact]
    public void Refuses_with_a_notice_when_not_split()
    {
        var (next, effects) = Transition.Apply(Browsing(), new Msg.CopyToOtherPane(IsMove: false));

        Assert.Empty(effects);
        Assert.NotNull(next.Notice);
    }

    [Fact]
    public void Refuses_the_drive_list_as_a_destination()
    {
        // With the cursor on a drive row the folder under it (the drive) is a fine destination; an
        // empty drive list leaves only the drive list itself, which is not a directory.
        var emptyDrives = new Column(Location.Drives.Instance, [], Cursor: -1, Load: LoadState.Loaded);
        var state = SplitWith(Mixed(@"C:\src", cursor: 1), emptyDrives);

        var (next, effects) = Transition.Apply(state, new Msg.CopyToOtherPane(IsMove: false));

        Assert.Empty(effects);
        Assert.NotNull(next.Notice);
    }

    [Fact]
    public void Refuses_copying_a_folder_into_itself()
    {
        var source = Mixed(@"C:\src", cursor: 0);                  // cursor on C:\src\sub
        var inside = Mixed(@"C:\src\sub", cursor: 1);               // other pane is inside it
        var state = SplitWith(source, inside);

        var (next, effects) = Transition.Apply(state, new Msg.CopyToOtherPane(IsMove: true));

        Assert.Empty(TestEffects.Of<Effect.RunFileJob>(effects));
        Assert.NotNull(next.Notice);
    }
```

Run → build FAILS.

- [ ] **Step 2: Msg**

```csharp
    /// <summary>
    /// <c>c</c> / <c>m</c>. Copies (or moves, when <paramref name="IsMove"/>) the focused column's
    /// marked entries - or, with none marked, the entry under its cursor - into the other pane's
    /// "directory under the cursor": the folder the other cursor is on, else that column's own
    /// location. Refused with a notice when there is no second pane, when that directory is the
    /// drive list, or when it is one of the sources or inside one. Otherwise exactly
    /// <see cref="PasteRequested"/>.
    /// </summary>
    public sealed record CopyToOtherPane(bool IsMove) : Msg;
```

- [ ] **Step 3: Handler**

Find how `PasteRequested`'s handler collects nothing itself (it receives `Sources`). Collecting
"marks, else cursor" is done by the App for Ctrl+C today; here Core does it:

```csharp
    private static (AppState, IReadOnlyList<Effect>) CopyToOtherPane(AppState state, bool isMove)
    {
        if (state.Panes.Length != 2)
        {
            return (state with { Notice = "分割していません (Ctrl+W で分割)" }, NoEffects);
        }

        var mine = state.Focused;
        if (mine.FocusedColumnOrNull is not { } column)
        {
            return (state, NoEffects);
        }

        var sources = OperationTargets(column);
        if (sources.IsEmpty)
        {
            return (state, NoEffects);
        }

        var other = state.Panes[1 - state.FocusedPane];
        if (DirectoryUnderCursor(other) is not { } dest)
        {
            return (state with { Notice = "ドライブ一覧にはコピー・移動できません" }, NoEffects);
        }

        foreach (var source in sources)
        {
            if (IsSameOrInside(dest, source))
            {
                return (state with { Notice = "フォルダーをそれ自身の中へコピー・移動することはできません" }, NoEffects);
            }
        }

        return PasteRequested(state, mine.FocusedRef, sources, isMove, destOverride: dest);
    }

    /// <summary>The marked entries' paths, or the cursor entry's when nothing is marked. Headers and drives never.</summary>
    private static ImmutableArray<string> OperationTargets(Column column)
    {
        var marked = ImmutableArray.CreateBuilder<string>();
        foreach (var entry in column.Entries)
        {
            if (entry.IsMarked && entry.Kind is EntryKind.File or EntryKind.Directory
                && EntryPath(column, entry) is { } markedPath)
            {
                marked.Add(markedPath);
            }
        }

        if (marked.Count > 0)
        {
            return marked.ToImmutable();
        }

        return column.Cursor >= 0 && column.Cursor < column.Entries.Length
            && column.Entries[column.Cursor] is { Kind: EntryKind.File or EntryKind.Directory } entry
            && EntryPath(column, entry) is { } path
                ? [path]
                : [];
    }

    /// <summary>
    /// zrr's ResolvePaneDirectory: the folder under <paramref name="pane"/>'s cursor, else its focused
    /// column's own location. <c>null</c> when that is not a real directory (the drive list).
    /// </summary>
    /// <remarks>
    /// Not simply the column's location: the path bar shows the folder under the cursor, and zrr
    /// shipped a bug where copying went one level above what the screen said.
    /// </remarks>
    private static string? DirectoryUnderCursor(Pane pane)
    {
        if (pane.FocusedColumnOrNull is not { } column)
        {
            return null;
        }

        if (ChildOfCursor(column) is { FilesystemPath: { } child })
        {
            return child;
        }

        return column.Location.FilesystemPath;
    }

    private static bool IsSameOrInside(string dest, string source)
    {
        var d = PathComparisonKey(dest);
        var s = PathComparisonKey(source);
        return string.Equals(d, s, StringComparison.OrdinalIgnoreCase)
            || d.StartsWith(s + "\\", StringComparison.OrdinalIgnoreCase);
    }
```

`PasteRequested` today computes the destination from its column. Give it an optional
`string? destOverride = null` parameter: when non-null it is used instead of the column's path.
Everything else (no-op filtering, job creation, `RunFileJob`, clearing `CutPending`) is unchanged.
Show the changed signature and the one changed line:

```csharp
    private static (AppState, IReadOnlyList<Effect>) PasteRequested(
        AppState state, ColumnRef at, ImmutableArray<string> sources, bool isMove, string? destOverride = null)
    {
        if (state.ColumnAt(at) is not { } column)
        {
            return (state, NoEffects);
        }

        // was: column.Location.FilesystemPath is not { } destDir
        if ((destOverride ?? column.Location.FilesystemPath) is not { } destDir || sources.IsDefaultOrEmpty)
        {
            return (state, NoEffects);
        }

        // ... the rest of the existing body, unchanged (FilterDropSources, Job, RunFileJob, CutPending = []) ...
    }
```

`FilterDropSources` already drops sources the destination is inside of, silently. The explicit
check in `CopyToOtherPane` runs first so that `c`/`m` say why nothing happened instead.

`ApplyCore`: `Msg.CopyToOtherPane m => CopyToOtherPane(state, m.IsMove),`

- [ ] **Step 4: GenMsg** — `GenIsMove.Select(m => (Msg)new Msg.CopyToOtherPane(m)),`

- [ ] **Step 5: Run, check, commit**

```bash
git add -A src/Zurari.Core tests/Zurari.Core.Tests
git commit -m "Copy or move to the other pane with c and m"
```

---

### Task 7a.8b: Two-pane correctness follow-ups

Found by the 7a.2 code review; each is invisible with one pane and wrong with two.

**Files:** `AppState.cs`, `Transition.cs`, `tests/Zurari.Core.Tests/SplitTests.cs`

- [ ] **Step 1: Failing tests** — append to `SplitTests.cs` (reuse `SplitWith`):

```csharp
    [Fact]
    public void A_rename_reveal_lands_in_the_pane_that_renamed_even_if_the_other_loads_first()
    {
        var dir = new Location.RealDirectory(@"C:\x");
        var shown = new Column(dir, [new Entry("old", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var state = SplitWith(shown, shown);
        var mine = new ColumnRef(new PaneId(0), 0);
        var theirs = new ColumnRef(new PaneId(1), 0);
        var (renamed, _) = Transition.Apply(state, new Msg.RenameCompleted(mine, dir, "new"));

        var listing = ImmutableArray.Create(new Entry("a", EntryKind.File), new Entry("new", EntryKind.File));
        var (otherFirst, _) = Transition.Apply(renamed, new Msg.DirectoryLoaded(theirs, dir, listing));
        var (both, _) = Transition.Apply(otherFirst, new Msg.DirectoryLoaded(mine, dir, listing));

        Assert.Equal("new", both.Panes[0].Columns[0].CursorName);
        Assert.Equal("a", both.Panes[1].Columns[0].CursorName);   // kept its index, not pulled to "new"
        Assert.Null(both.RevealTarget);
    }

    [Fact]
    public void A_mounted_image_is_revealed_by_whichever_drive_list_loads_first()
    {
        var drives = new Column(Location.Drives.Instance, [new Entry(@"C:\", EntryKind.Drive)], Cursor: 0, Load: LoadState.Loaded);
        var state = SplitWith(drives, drives);
        var (mounted, _) = Transition.Apply(state, new Msg.ImageMounted(@"E:\"));

        var listing = ImmutableArray.Create(new Entry(@"C:\", EntryKind.Drive), new Entry(@"E:\", EntryKind.Drive));
        var (next, _) = Transition.Apply(
            mounted, new Msg.DirectoryLoaded(new ColumnRef(new PaneId(1), 0), Location.Drives.Instance, listing));

        Assert.Equal(@"E:\", next.Panes[1].Columns[0].CursorName);
    }

    [Fact]
    public void A_finished_shell_op_rereads_affected_folders_even_when_its_column_is_gone()
    {
        var src = new Location.RealDirectory(@"C:\src");
        var state = SplitWith(
            new Column(src, [new Entry("f", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"D:\dst"), [], Load: LoadState.Loaded));
        var closedPane = new ColumnRef(new PaneId(9), 0);

        var (_, effects) = Transition.Apply(
            state, new Msg.ShellOpCompleted(closedPane, new Location.RealDirectory(@"D:\dst"), [@"C:\src"]));

        var read = Assert.Single(TestEffects.Of<Effect.ReadDirectory>(effects));
        Assert.Equal(new ColumnRef(new PaneId(0), 0), read.At);
    }

    [Fact]
    public void Collapsing_a_section_collapses_it_in_every_drive_list()
    {
        var header = new Entry("net", EntryKind.Header, Group: "net");
        var row = new Entry(@"\\s\x", EntryKind.Directory, Group: "net");
        var drives = new Column(Location.Drives.Instance, [header, row], Cursor: 0, Load: LoadState.Loaded);
        var state = SplitWith(drives, drives);

        var (next, _) = Transition.Apply(state, new Msg.ToggleSection(new ColumnRef(new PaneId(0), 0), 0));

        Assert.Single(next.Panes[0].Columns[0].Entries);
        Assert.Single(next.Panes[1].Columns[0].Entries);
    }
```

Also add a close-during-reread case (7a.6 review): a rename in the bottom pane sets the reveal, the
split is closed before the reread's listing arrives, and a later `DirectoryLoaded` in the kept pane
containing the same name must not jump its cursor - the reveal named a column that no longer exists.
`DirectoryLoaded` should drop a `RevealTarget` whose `At.Pane` is gone (or `ToggleSplit`'s close
should clear it when it names the dropped pane; pick one and test it).

Run → the first, third and fourth FAIL (the second documents intended behaviour and may already pass).

- [ ] **Step 2: Scope the reveal**

In `AppState.cs` replace `string? RevealTarget` with:

```csharp
/// <summary>
/// A name to put the cursor on when a listing containing it arrives. <paramref name="At"/> names the
/// one column that may consume it (a rename or a new folder happened there, and the same directory
/// shown in the other pane must not take it); <c>null</c> lets any listing consume it (a mounted
/// image appears in every drive list).
/// </summary>
public sealed record Reveal(string Name, ColumnRef? At);
```

and `public Reveal? RevealTarget { get; init; }` (keep its existing remarks). `RereadColumn` sets
`new Reveal(reveal, at)` (when `reveal` is non-null); `ImageMounted` sets `new Reveal(m.DriveRoot, null)`.
`DirectoryLoaded` consumes it only when `RevealTarget.At is null || RevealTarget.At == at`, matching
`RevealTarget.Name`. Existing tests that set `RevealTarget = "x"` become `new Reveal("x", null)`.

- [ ] **Step 3: `ShellOpCompleted` rereads affected folders even when its column is gone**

Replace its staleness early-return with the check inside the predicate:

```csharp
        return RereadWhere(
            state,
            (r, c) => (r == at && c.Location == columnLocation) || IsAffectedDirectory(c.Location, affectedDirs));
```

- [ ] **Step 4: Sections apply to every drive list**

`ToggleSection` computes the new collapsed set from the addressed column (as today), then applies
it with `WithCollapsedGroups(set, pane.View)` to every `Location.Drives` column in every pane, and
still emits one `SetCollapsedGroups`. `CollapsedGroupsRestored` does the same with the restored set.

- [ ] **Step 5: Run, check, commit**

Run: `dotnet test tests/Zurari.Core.Tests` → PASS. `pwsh scripts/check.ps1` → green.

```bash
git add -A src/Zurari.Core tests/Zurari.Core.Tests
git commit -m "Scope a reveal to its column, and keep rereads and sections right with two panes"
```

---

### Task 7a.9: Runtime — one preview slot per pane

Today one slot, one `_highestPreviewGeneration`. Each pane counts generations from its own start,
so a single "highest generation" would drop the second pane's first preview as stale.

**Files:** `src/Zurari.Runtime/WorkerRuntime.cs`, `tests/Zurari.Runtime.Tests/WorkerRuntimeTests.cs`

- [ ] **Step 1: Failing test** — add to `WorkerRuntimeTests.cs` (add `using System.Collections.Concurrent;` and
  `using System.IO;` if the file lacks them):

```csharp
    [Fact]
    public void Two_panes_previews_load_independently()
    {
        var dir = TempDirectory.Create("zurari-two-previews");
        var a = Path.Combine(dir, "a.txt");
        var b = Path.Combine(dir, "b.txt");
        File.WriteAllText(a, "alpha");
        File.WriteAllText(b, "bravo");
        var posted = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(posted.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));

        // Pane 0 is already at generation 5; pane 1 starts at 1. With one shared "highest
        // generation" the second request would be dropped as stale.
        runtime.Submit(new Effect.LoadPreview(new PaneId(0), 5, a));
        runtime.Submit(new Effect.LoadPreview(new PaneId(1), 1, b));

        var loaded = PreviewsIn(WaitFor(posted, TimeSpan.FromSeconds(5), seen => PreviewsIn(seen).Count >= 2));
        Assert.Contains(loaded, m => m.Pane == new PaneId(0) && m.Text == "alpha");
        Assert.Contains(loaded, m => m.Pane == new PaneId(1) && m.Text == "bravo");
        TempDirectory.Delete(dir);
    }
```

If `WorkerRuntimeTests` has no `WaitFor`, add it and the `PreviewsIn` picker beside it. Both are
explicit loops because LINQ is banned; `new List<Msg>(queue)` is `ConcurrentQueue`'s own snapshot.

```csharp
    private static List<Msg> WaitFor(ConcurrentQueue<Msg> queue, TimeSpan timeout, Func<List<Msg>, bool> done)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var seen = new List<Msg>(queue);
            if (done(seen))
            {
                return seen;
            }

            Thread.Sleep(10);
        }

        return new List<Msg>(queue);
    }

    private static List<Msg.PreviewLoaded> PreviewsIn(List<Msg> msgs)
    {
        var loaded = new List<Msg.PreviewLoaded>();
        foreach (var msg in msgs)
        {
            if (msg is Msg.PreviewLoaded preview)
            {
                loaded.Add(preview);
            }
        }

        return loaded;
    }
```

Run: `dotnet test tests/Zurari.Runtime.Tests --filter FullyQualifiedName~Two_panes` → FAIL (one of the two never arrives).

- [ ] **Step 2: Key the slot by pane**

Replace the fields `_previewCts`, `_previewGeneration`, `_highestPreviewGeneration`, `_previewTask`
with one class and a dictionary, all guarded by the existing `_previewGate`:

```csharp
    /// <summary>One pane's preview: what is in flight and the newest generation ever started.</summary>
    private sealed class PreviewSlot
    {
        public CancellationTokenSource? Cts;
        public int Generation = -1;
        public int HighestGeneration = -1;
        public Task? Task;
    }

    /// <summary>
    /// A slot per pane, so starting one pane's preview never cancels the other's. Created on first
    /// use; a closed pane's slot is simply never used again (its last task still unwinds normally).
    /// </summary>
    private readonly Dictionary<PaneId, PreviewSlot> _previewSlots = [];

    // ToggleSplit's close emits CancelPreview(closedPane, gen) when that pane was loading (7a.6
    // review follow-up); after cancelling, call ForgetSlotUnderLock for that pane so the slot goes.
    /// <summary>Forgets <paramref name="pane"/>'s slot once its work has unwound.</summary>
    /// <remarks>
    /// Closing and reopening the split hands out a fresh <see cref="PaneId"/> each time, so without
    /// this the dictionary grows by one entry - each holding a finished <see cref="Task"/> - for
    /// every toggle in the process's life.
    /// </remarks>
    private void ForgetSlotUnderLock(PaneId pane)
    {
        if (_previewSlots.TryGetValue(pane, out var slot) && slot.Cts is null)
        {
            _previewSlots.Remove(pane);
        }
    }

    private PreviewSlot SlotFor(PaneId pane)
    {
        if (!_previewSlots.TryGetValue(pane, out var slot))
        {
            slot = new PreviewSlot();
            _previewSlots.Add(pane, slot);
        }

        return slot;
    }
```

In `StartPreview`, `RunPreview`, `CancelInFlightPreview` and `CancelPreviewCtsUnderLock`, replace
each field access with `SlotFor(effect.Pane).X` (under the lock, as today).
`CancelInFlightPreview(PaneId pane, int generation)`. `Dispose` waits for every slot's `Task`
instead of the one `_previewTask`. The shell-thumbnail thread's "latest wins" must also be per pane:
find where `ShellThumbnailThread` is asked for a thumbnail from `ExecuteLoadPreview` and pass the
pane id as its key; in `ShellThumbnailThread`, keep the newest *pending* request per key instead of
one global newest (a `Dictionary<PaneId, Request>` drained in insertion order).

- [ ] **Step 3: Run, check, commit**

Run: `dotnet test tests/Zurari.Runtime.Tests` → PASS. `pwsh scripts/check.ps1` → green.

```bash
git add -A src/Zurari.Runtime tests/Zurari.Runtime.Tests
git commit -m "Give each pane its own preview slot, so neither cancels the other"
```

---

### Task 7a.10: App — gates and projection per pane; `PaneView` extracted

Still one pane on screen. This task only restructures, so two panes become a matter of adding a
second `PaneView`.

**Files:**
- Create: `src/Zurari.App/PaneView.xaml`, `src/Zurari.App/PaneView.xaml.cs`
- Modify: `src/Zurari.App/MainWindow.xaml`, `MainWindow.xaml.cs`, `StateProjection.cs`
- Test: `tests/Zurari.App.Tests/StateProjectionTests.cs`, `PreviewProjectionTests.cs`

- [ ] **Step 1: Failing projection tests** — add to `StateProjectionTests.cs`:

```csharp
    [Fact]
    public void Projects_the_pane_it_is_given()
    {
        var top = new Column(new Location.RealDirectory(@"C:\a"), [new Entry("x", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var bottom = new Column(new Location.RealDirectory(@"C:\b"), [new Entry("p", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var state = AppState.Initial with
        {
            Panes = [new Pane(new PaneId(0), [top], 0), new Pane(new PaneId(1), [bottom], 0)],
            NextPaneId = 2,
        };

        var projected = StateProjection.Project(state, state.Panes[1]);

        Assert.Equal("p", Assert.Single(projected).Entries[0].Name);
    }

    [Fact]
    public void The_unfocused_pane_projects_no_focused_column()
    {
        var column = new Column(new Location.RealDirectory(@"C:\a"), [new Entry("x", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var state = AppState.Initial with
        {
            Panes = [new Pane(new PaneId(0), [column], 0), new Pane(new PaneId(1), [column], 0)],
            NextPaneId = 2,
        };

        Assert.True(StateProjection.Project(state, state.Panes[0])[0].IsFocused);
        Assert.False(StateProjection.Project(state, state.Panes[1])[0].IsFocused);
    }
```

and to `PreviewProjectionTests.cs`: `ProjectPreview(state, pane)` returns the given pane's preview.

Run → build FAILS.

- [ ] **Step 2: `StateProjection`**

`Project(AppState state, Pane pane, Func<Entry, ImageSource?>? iconResolver = null, ProjectionCache? cache = null)`:
the existing body with `state.Columns`/`FocusedColumn` replaced by `pane.Columns`/`pane.FocusedColumn`,
and `isFocused: i == pane.FocusedColumn && pane.Id == state.Focused.Id` (decision 5).
`ProjectPreview(AppState state, Pane pane)` reads `pane.Preview`. `ProjectStatusBar(state)` keeps
its signature and reads `state.Focused`.

- [ ] **Step 3: `PaneView` UserControl**

Move the `Grid` that holds `Browser`, the `GridSplitter` and `PreviewPane` out of
`MainWindow.xaml` into `PaneView.xaml` **unchanged** (same element names — `Browser`,
`PreviewColumn`, `PreviewImage`, `PreviewTextBox`, `PreviewCapacityPanel`, … — they are now
`PaneView`'s fields). `PaneView.xaml`:

```xml
<UserControl x:Class="Zurari.App.PaneView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:Zurari.Controls;assembly=Zurari.Controls">
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVisibility" />
    </UserControl.Resources>
    <!-- paste here the <Grid Margin="8"> … </Grid> block from MainWindow.xaml, verbatim,
         including its comments; change the Hyperlink's Click to OnPreviewRetryClick (same name) -->
</UserControl>
```

`PaneView.xaml.cs` owns what is per pane and raises everything else outward:

```csharp
using System.Windows;
using System.Windows.Controls;
using Zurari.Controls;

namespace Zurari.App;

/// <summary>
/// One pane on screen: its column browser, its preview, and the splitter between them. Holds no
/// state of its own beyond what WPF needs; MainWindow renders into it and listens to it.
/// </summary>
public partial class PaneView : UserControl
{
    public PaneView() => InitializeComponent();

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
}
```

Move `RenderPreview`, `RenderCapacity`, and every helper that only touches preview elements from
`MainWindow.xaml.cs` into `PaneView` as `public void RenderPreview(StateProjection.PreviewVm vm)`
(plus the image-decode generation field it uses, line ~70 of MainWindow). Whatever raised the
width-save on splitter drag (`settingsStore.Update(s => s with { PreviewWidth = … })`, ~line 172)
becomes: `PaneView` raises `PreviewWidthChanged` from a `DragCompleted` handler on its splitter, and
MainWindow saves.

`MainWindow.xaml`: replace the moved `Grid` with

```xml
        <local:PaneView x:Name="TopPane" />
```

- [ ] **Step 4: Per-pane gates and caches in `MainWindow`**

```csharp
    private readonly Dictionary<PaneId, PendingEffectGate<Effect.LoadPreview>> previewGates = [];
    private readonly Dictionary<PaneId, PendingEffectGate<Effect.ReadDirectory>> speculativeReadGates = [];
    private readonly Dictionary<PaneId, ProjectionCache> projectionCaches = [];

    private static T For<T>(Dictionary<PaneId, T> map, PaneId pane) where T : new()
    {
        if (!map.TryGetValue(pane, out var value))
        {
            value = new T();
            map.Add(pane, value);
        }

        return value;
    }
```

`SchedulePreview(effect)` holds into `For(previewGates, effect.Pane)`; the debounce tick drains
**every** gate. Same for speculative reads, keyed by `effect.At.Pane`. Without this, pane B's
speculative read inside the debounce window replaces pane A's and A's child column stays
"loading" forever. `Render` passes `For(projectionCaches, pane.Id)`. When a pane disappears from
`state.Panes`, remove its three entries.

**Shared two-pane builder (7a.5 review).** Move `TwoPanes(top, bottom, focused = 0)` from
`PaneTests` into `tests/TestSupport/TestPanes.cs` (public, same signature) and use it in
`PaneTests`, `StatusBarProjectionTests` (which builds one by hand) and every App test this task and
7a.11 add. Add a `FocusedPane = 1` case for `InputTrace.State` and for whatever `Render`-side
projection this task introduces: they are the two focus-sensitive sites 7a.5 left without one.

**Address by pane, not by focus (from the 7a.2 review).** The MainWindow helpers that take a bare
column index and read the focused pane - `OnEntryDragRequested`, `ResolveFullPath`, `IsEntryMarked`,
`ResolveMarkedFullPaths`, `ShowContextMenu`, `ResolveShellName`, `RenderRename` - take a `ColumnRef` instead and resolve
through `state.ColumnAt`. Do not convert them to `state.Focused.Columns` or `state.Focused.Ref(i)`
(that bakes in the assumption 7a.11 must remove). The keyboard-only actions (`TryCopyToClipboard`,
`TryDelete`, `TryDeleteFocusedEntry`) legitimately mean the focused pane and follow automatically. `RenderRename` draws the editor in the `PaneView` showing
`rename.At.Pane` and clears it in the other.

`Render(state)` renders `state.Panes[0]` into `TopPane` (`Project(state, state.Panes[0], …)`,
`TopPane.RenderPreview(StateProjection.ProjectPreview(state, state.Panes[0]))`), with the existing
reference-equality guard kept per pane (`lastRenderedColumns`/`lastRenderedFocusedColumn` become
per-`PaneId` dictionaries). `directoryWatcher.SetWatchedPaths` gets the real directories of every
column of every pane.

- [ ] **Step 5: Run, check, commit**

Run: `dotnet test Zurari.sln` → PASS. `pwsh scripts/check.ps1` → green.
Run the app (`dotnet run --project src/Zurari.App`) and confirm nothing changed visibly: browse,
preview an image and a text file, drag the preview splitter, restart and see the width kept.

```bash
git add -A src/Zurari.App tests/Zurari.App.Tests
git commit -m "Extract PaneView, and keep coalescing gates and projection caches per pane"
```

---

### Task 7a.11: App — second row, focus routing, dimming, keys

**Files:** `MainWindow.xaml`, `MainWindow.xaml.cs`, `KeyMap.cs`, `tests/Zurari.App.Tests/KeyMapTests.cs`

- [ ] **Step 1: Failing KeyMap tests** — add to `KeyMapTests.cs`:

```csharp
    [Theory]
    [InlineData(Key.W, ModifierKeys.Control, typeof(Msg.ToggleSplit))]
    [InlineData(Key.O, ModifierKeys.Shift, typeof(Msg.SwapPanes))]
    [InlineData(Key.O, ModifierKeys.None, typeof(Msg.SyncToOtherPane))]
    [InlineData(Key.C, ModifierKeys.None, typeof(Msg.CopyToOtherPane))]
    [InlineData(Key.M, ModifierKeys.None, typeof(Msg.CopyToOtherPane))]
    public void Split_keys_resolve(Key key, ModifierKeys modifiers, Type expected)
    {
        Assert.IsType(expected, KeyMap.ResolveNormal(key, modifiers, AppState.Initial));
    }

    [Fact]
    public void M_moves_and_C_copies()
    {
        Assert.Equal(new Msg.CopyToOtherPane(IsMove: true), KeyMap.ResolveNormal(Key.M, ModifierKeys.None, AppState.Initial));
        Assert.Equal(new Msg.CopyToOtherPane(IsMove: false), KeyMap.ResolveNormal(Key.C, ModifierKeys.None, AppState.Initial));
    }

    [Fact]
    public void Tab_focuses_the_other_pane_only_when_split()
    {
        var single = AppState.Initial;
        var (split, _) = Transition.Apply(single, new Msg.ToggleSplit());

        Assert.Null(KeyMap.ResolveNormal(Key.Tab, ModifierKeys.None, single));
        Assert.Equal(new Msg.FocusPane(split.Panes[1].Id), KeyMap.ResolveNormal(Key.Tab, ModifierKeys.None, split));
    }
```

Run → build FAILS (`ResolveNormal` has no state parameter).

- [ ] **Step 2: `KeyMap.ResolveNormal(Key, ModifierKeys, AppState)`**

```csharp
    public static Msg? ResolveNormal(Key key, ModifierKeys modifiers, AppState state) => (key, modifiers) switch
    {
        (Key.S, ModifierKeys.None) => new Msg.EnterSortMode(),
        (Key.OemPeriod, ModifierKeys.Control | ModifierKeys.Shift) => new Msg.ToggleHiddenFiles(),
        (Key.W, ModifierKeys.Control) => new Msg.ToggleSplit(),
        (Key.Tab, ModifierKeys.None) when state.Panes.Length == 2 =>
            new Msg.FocusPane(state.Panes[1 - state.FocusedPane].Id),
        (Key.O, ModifierKeys.Shift) => new Msg.SwapPanes(),
        (Key.O, ModifierKeys.None) => new Msg.SyncToOtherPane(),
        (Key.C, ModifierKeys.None) => new Msg.CopyToOtherPane(IsMove: false),
        (Key.M, ModifierKeys.None) => new Msg.CopyToOtherPane(IsMove: true),
        _ => null,
    };
```

Update the class remarks: the plain letters now claimed are `S`, `o`, `c`, `m`. Existing `KeyMapTests`
calls `ResolveNormal(key, modifiers)` gain a third argument, `AppState.Initial`. Update the caller in
`MainWindow` to pass `loop.State`. Ctrl+C / Ctrl+X / Ctrl+V are unaffected (different modifiers).
Tab must be handled in `OnWindowPreviewKeyDown` (WPF's own Tab navigation would otherwise take it);
mark it handled only when the map returned a message.

- [ ] **Step 3: Layout**

In `MainWindow.xaml`, the `TopPane` becomes a three-row grid:

```xml
        <Grid x:Name="PaneRows">
            <Grid.RowDefinitions>
                <RowDefinition x:Name="TopRow" Height="*" MinHeight="120" />
                <RowDefinition x:Name="SplitterRow" Height="0" />
                <RowDefinition x:Name="BottomRow" Height="0" MinHeight="0" />
            </Grid.RowDefinitions>
            <local:PaneView x:Name="TopPane" Grid.Row="0" />
            <!-- Focusable=False: a focused GridSplitter eats the arrow keys (see the preview splitter). -->
            <GridSplitter x:Name="PaneSplitter" Grid.Row="1" Height="6" HorizontalAlignment="Stretch"
                          Background="Transparent" ShowsPreview="False" Focusable="False" IsTabStop="False"
                          Visibility="Collapsed" />
            <local:PaneView x:Name="BottomPane" Grid.Row="2" Visibility="Collapsed" />
        </Grid>
```

`Render`: when `state.Panes.Length == 2` and the layout is single, set `SplitterRow.Height = 6`,
`BottomRow.Height = new GridLength(1 - ratio, GridUnitType.Star)`,
`TopRow.Height = new GridLength(ratio, GridUnitType.Star)` (ratio from settings in 7a.12; `0.5`
until then), `BottomRow.MinHeight = 120`, show splitter and `BottomPane`. When it goes back to one,
collapse them and set `TopRow` to `*`. Render `state.Panes[i]` into row `i`'s `PaneView`.

- [ ] **Step 4: Events carry the pane they came from**

Wire both `PaneView`s' `ColumnBrowser` events through one method that knows the row:

```csharp
    /// <summary>
    /// The pane shown in <paramref name="row"/> right now. By position at the moment of the event,
    /// not captured at wiring time: a swap moves panes between rows.
    /// </summary>
    private PaneId PaneInRow(int row) =>
        row < loop.State.Panes.Length ? loop.State.Panes[row].Id : loop.State.Focused.Id;
```

Every handler that built `loop.State.Focused.Ref(e.ColumnIndex)` in 7a.2 (and every helper converted to
`ColumnRef` in 7a.10) now builds
`new ColumnRef(PaneInRow(row), e.ColumnIndex)`, where `row` is 0 for `TopPane`'s browser and 1 for
`BottomPane`'s (pass it when subscribing, e.g. `TopPane.ColumnBrowser.EntryClicked += (s, e) => OnEntryClicked(0, e);`).
Before dispatching any pointer Msg from row `r`, dispatch `new Msg.FocusPane(PaneInRow(r))` if it
is not the focused pane — clicking a pane focuses it.

Keyboard focus: after `Render`, if `state.FocusedPane` changed since the last render, call
`Keyboard.Focus` on that row's `ColumnBrowser` (the same call the window uses for its single
browser today). The window's `Loaded += (_, _) => Browser.Focus();` likewise focuses the row showing
`state.Focused`, not `TopPane`. `ShowContextMenuAtCursor` (the menu key) calls
`TryGetRowScreenRect` on a browser: resolve the `PaneView` of the row showing `state.Focused`, or
the menu appears over the wrong pane whenever the bottom one is focused.

`InputTrace.State` adds `pane={state.Focused.Id.Value}/{state.Panes.Length}` so a trace line says
which pane it describes.

`RetryRequested` from row `r` → `Dispatch(new Msg.RetryPreview(PaneInRow(r)))`.
`PreviewWidthChanged` from either → set the other's `PreviewWidth` to match and save (decision 8).

- [ ] **Step 5: Status-bar hints**

Where 6e.7's context hints are built (`StateProjection.ProjectStatusBar`), when
`state.Panes.Length == 2` append `Tab 切替  c/m コピー/移動  o 同期  Shift+O 入替  Ctrl+W 閉じる`;
when single, append `Ctrl+W 分割`. Add a `StatusBarProjectionTests` case for each.

- [ ] **Step 6: Run, check, commit**

Run: `dotnet test Zurari.sln` → PASS. `pwsh scripts/check.ps1` → green.

```bash
git add -A src/Zurari.App tests/Zurari.App.Tests
git commit -m "Show two panes: Ctrl+W, Tab, click to focus, o, Shift+O, c and m"
```

---

### Task 7a.12: Persistence — split open/ratio, per-position view options

**Files:** `Effect.cs`, `Transition.cs`, `UserSettingsStore.cs`, `WorkerRuntime.cs`, `EffectRouting.cs`,
`MainWindow.xaml.cs`, tests in `Zurari.Core.Tests/SplitTests.cs`, `Zurari.Runtime.Tests` (settings)

- [ ] **Step 1: Failing tests**

`SplitTests.cs`:

```csharp
    [Fact]
    public void Changing_a_view_persists_every_panes_view_top_first()
    {
        var (split, _) = Transition.Apply(Browsing(), new Msg.ToggleSplit());

        var (_, effects) = Transition.Apply(split, new Msg.ToggleHiddenFiles());

        var saved = Assert.Single(TestEffects.Of<Effect.SetPaneViews>(effects));
        Assert.Equal(2, saved.TopFirst.Length);
        Assert.True(saved.TopFirst[0].ShowHidden);
        Assert.False(saved.TopFirst[1].ShowHidden);
    }

    [Fact]
    public void Swapping_persists_the_views_in_their_new_positions()
    {
        var (split, _) = Transition.Apply(Browsing(), new Msg.ToggleSplit());
        var (hidden, _) = Transition.Apply(split, new Msg.ToggleHiddenFiles());

        var (_, effects) = Transition.Apply(hidden, new Msg.SwapPanes());

        var saved = Assert.Single(TestEffects.Of<Effect.SetPaneViews>(effects));
        Assert.False(saved.TopFirst[0].ShowHidden);
        Assert.True(saved.TopFirst[1].ShowHidden);
    }
```

Runtime settings test (in the existing `UserSettingsStore` tests file):

```csharp
    [Fact]
    public void Old_single_view_fields_read_as_the_top_panes_view()
    {
        var dir = IsolatedSettings.NewDirectory();
        File.WriteAllText(Path.Combine(dir, "settings.json"), """{ "SortMode": "Size", "ShowHidden": true }""");

        var settings = new UserSettingsStore(dir).Load();

        var top = Assert.Single(settings.EffectivePaneViews());
        Assert.Equal("Size", top.SortMode);
        Assert.True(top.ShowHidden);
    }

    [Fact]
    public void Saving_fewer_views_keeps_the_stored_bottom_one()
    {
        var store = new UserSettingsStore(IsolatedSettings.NewDirectory());
        store.Update(s => s.WithPaneViews([new PaneViewSettings("Name", false, true, false), new PaneViewSettings("Size", true, true, true)]));

        store.Update(s => s.WithPaneViews([new PaneViewSettings("Modified", false, true, false)]));

        var views = store.Load().EffectivePaneViews();
        Assert.Equal(2, views.Count);
        Assert.Equal("Modified", views[0].SortMode);
        Assert.Equal("Size", views[1].SortMode);
    }
```

Run → build FAILS.

- [ ] **Step 2: Effect and Transition**

Replace `Effect.SetViewOptions` with:

```csharp
    /// <summary>
    /// Remembers every pane's view options, top first, so each position comes back the way it was
    /// left. Sent whole, like <see cref="SetCollapsedGroups"/>, so a lost or reordered effect cannot
    /// leave storage disagreeing with the screen.
    /// </summary>
    public sealed record SetPaneViews(ImmutableArray<ViewOptions> TopFirst) : Effect;
```

In Transition add a `PaneViews(AppState)` helper that builds the array with an explicit loop over
`state.Panes` (`ImmutableArray.CreateBuilder<ViewOptions>`); `Select` is banned solution-wide.
`Rederive(..., remember: true)` emits `PaneViews(next)`; `SwapPanes` and `ToggleSplit` emit
`PaneViews(next)` too (so their handler return types become `(AppState, IReadOnlyList<Effect>)`).
`EffectRouting`: `Effect.SetPaneViews => EffectTarget.Runtime` (remove `SetViewOptions`).

- [ ] **Step 3: Settings**

In `UserSettingsStore.cs`:

```csharp
/// <summary>One pane position's view options, as stored.</summary>
public sealed record PaneViewSettings(string? SortMode, bool? SortDescending, bool? DirectoriesFirst, bool? ShowHidden);
```

Add to `UserSettings`: `bool? SplitOpen = null, double? SplitRatio = null, IReadOnlyList<PaneViewSettings>? PaneViews = null`,
and these members on the record:

```csharp
    /// <summary>
    /// The stored per-position views. A file from before the split has none; its single set of
    /// fields is what the one pane used, so it reads as the top pane's.
    /// </summary>
    public IReadOnlyList<PaneViewSettings> EffectivePaneViews() =>
        PaneViews is { Count: > 0 } views
            ? views
            : [new PaneViewSettings(SortMode, SortDescending, DirectoriesFirst, ShowHidden)];

    /// <summary>
    /// Stores <paramref name="topFirst"/>, keeping any stored positions beyond it: closing the split
    /// saves one view, and that must not forget how the bottom pane was set up.
    /// </summary>
    public UserSettings WithPaneViews(IReadOnlyList<PaneViewSettings> topFirst)
    {
        var existing = EffectivePaneViews();
        var merged = new List<PaneViewSettings>(topFirst);
        for (var i = topFirst.Count; i < existing.Count; i++)
        {
            merged.Add(existing[i]);
        }

        return this with { PaneViews = merged };
    }
```

**The restore must name the top pane, not whichever read won (7a.3 review).** `ViewRestored` and
`CollapsedGroupsRestored` are posted from a once-per-process gate, fired by the first
`ReadDirectory(Drives)` to finish. With the split restored at startup, the *bottom* pane's read can
win, which would apply the stored top-pane view to the bottom pane and leave the top on defaults.
Post the restore for the pane of the drive read only when it is the first pane (`effect.At.Pane`
equals the id the App started with, which the Runtime is told once at construction), or - simpler -
have the App dispatch `ViewRestored` itself for each pane from the settings it already reads in
Step 4, and drop the Runtime's `ViewRestored` post entirely. Decide when implementing; the second
is preferred, because the App is where the split is restored and it already holds the settings.

`WorkerRuntime`: handle `Effect.SetPaneViews` by mapping each `ViewOptions` to `PaneViewSettings`
(`Mode.ToString()`, etc.) and `_settings.Update(s => s.WithPaneViews(mapped))`. `ReadViewOptions`
takes a `PaneViewSettings`.
Add `public static ViewOptions ReadViewOptions(PaneViewSettings stored)` as **public** so the App can
convert the stored bottom view (next step).

- [ ] **Step 4: App restores and saves the split**

In `MainWindow`, after the runtime has started (where the initial Msgs are dispatched today):

```csharp
        var settings = settingsStore.Load();
        if (settings.SplitOpen == true)
        {
            var views = settings.EffectivePaneViews();
            ViewOptions? bottom = views.Count > 1 ? WorkerRuntime.ReadViewOptions(views[1]) : null;
            Dispatch(new Msg.ToggleSplit(bottom));
        }
```

`SplitRatio` (default 0.5, clamped to 0.15-0.85) sets the row heights when the split opens. On the
pane splitter's `DragCompleted`, save `TopRow.ActualHeight / (TopRow.ActualHeight + BottomRow.ActualHeight)`.
In `Render`, when `state.Panes.Length` changes, `settingsStore.Update(s => s with { SplitOpen = state.Panes.Length == 2 })`.

- [ ] **Step 5: Run, check, commit**

Run: `dotnet test Zurari.sln` → PASS. `pwsh scripts/check.ps1` → green.

```bash
git add -A src tests
git commit -m "Remember the split, its ratio, and each position's view options"
```

---

### Task 7a.13: Headless E2E — split, copy top → bottom

**Files:** Create `tests/Zurari.App.Tests/SplitE2ETests.cs`

- [ ] **Step 1: Write the test** (harness copied from `JobPasteE2ETests`, which it mirrors):

```csharp
using System.Collections.Concurrent;
using System.IO;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.App.Tests;

/// <summary>Split the window, copy a file from the top pane into the bottom one, see it arrive.</summary>
public class SplitE2ETests
{
    private static void DrainUntil(MessageLoop loop, ConcurrentQueue<Msg> queue, Func<bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !done())
        {
            if (queue.TryDequeue(out var msg))
            {
                loop.Dispatch(msg);
            }
            else
            {
                Thread.Sleep(10);
            }
        }

        Assert.True(done(), "Timed out waiting for the expected state.");
    }

    [Fact]
    public void Copying_to_the_other_pane_lands_in_the_directory_it_shows()
    {
        var source = TempDirectory.Create("zurari-split-src");
        var dest = TempDirectory.Create("zurari-split-dst");
        var queue = new ConcurrentQueue<Msg>();
        var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));
        var jobs = new JobEngine(queue.Enqueue);
        try
        {
            File.WriteAllText(Path.Combine(source, "hello.txt"), "hello");

            void RunEffect(Effect effect)
            {
                switch (effect)
                {
                    case Effect.ReadDirectory:
                        runtime.Submit(effect);
                        break;
                    case Effect.RunFileJob:
                    case Effect.CancelJob:
                        jobs.Submit(effect);
                        break;
                }
            }

            // The layout Ctrl+W and navigation would produce (both are covered by Core tests):
            // top pane at the source with its cursor on the file, bottom pane at the destination.
            var initial = AppState.Initial with
            {
                Panes =
                [
                    new Pane(new PaneId(0), [new Column(new Location.RealDirectory(source), Entries: [])], 0),
                    new Pane(new PaneId(1), [new Column(new Location.RealDirectory(dest), Entries: [])], 0),
                ],
                FocusedPane = 0,
                NextPaneId = 2,
            };
            var loop = new MessageLoop(initial, RunEffect);
            loop.Dispatch(new Msg.Refresh());
            DrainUntil(
                loop, queue,
                () => loop.State.Panes[0].Columns[0].Load == LoadState.Loaded
                    && loop.State.Panes[1].Columns[0].Load == LoadState.Loaded
                    && loop.State.Panes[0].Columns[0].CursorName == "hello.txt",
                TimeSpan.FromSeconds(10));

            loop.Dispatch(new Msg.CopyToOtherPane(IsMove: false));

            DrainUntil(
                loop, queue,
                () => HasEntry(loop.State.Panes[1].Columns[0], "hello.txt"),
                TimeSpan.FromSeconds(15));
            Assert.True(File.Exists(Path.Combine(source, "hello.txt")), "a copy leaves the source in place");
        }
        finally
        {
            runtime.Dispose();
            jobs.Dispose();
            TempDirectory.Delete(source);
            TempDirectory.Delete(dest);
        }
    }

    /// <summary>Whether <paramref name="column"/> lists an entry called <paramref name="name"/>. A loop: LINQ is banned.</summary>
    private static bool HasEntry(Column column, string name)
    {
        foreach (var entry in column.Entries)
        {
            if (entry.Name == name)
            {
                return true;
            }
        }

        return false;
    }
}
```

The initial listing puts the cursor on the first entry, which is `hello.txt` (the only one).

- [ ] **Step 2: Run, check, commit**

Run: `dotnet test tests/Zurari.App.Tests --filter FullyQualifiedName~SplitE2ETests` → PASS.
`pwsh scripts/check.ps1` → green.

```bash
git add tests/Zurari.App.Tests/SplitE2ETests.cs
git commit -m "E2E: copy from the top pane lands in the bottom pane's directory"
```

---

### Task 7a.14: Manual verification, docs

- [ ] **Step 1: Manual checks** (run `dotnet run --project src/Zurari.App`), record results under Records:
  1. Ctrl+W opens a bottom pane at the same place; focus stays top; Ctrl+W again closes the unfocused one.
  2. Tab and clicking move focus; the unfocused pane's cursor rows are the weak highlight.
  3. Each pane previews its own cursor; moving in one never changes or blanks the other's preview.
  4. `S N` / `Ctrl+Shift+.` affect only the focused pane.
  5. `c` / `m` copy/move to the folder under the other cursor; conflict prompt works; `m` into its own subfolder is refused with a notice.
  6. `o` and Shift+O behave as described.
  7. Drag the pane splitter; restart: split, ratio and both panes' sort/hidden settings are restored; both start at the drive list.
  8. Drag a file from one pane to the other (existing shell DnD) copies/moves it.
  9. Single-pane use is unchanged from before 7a.
- [ ] **Step 2: Docs**
  - Design doc: in "7a 後段", change the `SyncToOtherPane` row to "copies the other pane's column
    chain (re-derived under this pane's view); `JumpTo` moves to 7c", and the Msg field name to `At`.
  - `plan/phase7-dual-pane-and-search.md`: 7a row → `[x]`, plan link to this file.
  - `plan/roadmap.md`: Phase 7 status text mentions 7a done.
  - Run `pwsh scripts/status.ps1`, then `pwsh scripts/check.ps1`.
- [ ] **Step 3: Commit**

```bash
git add -A plan docs
git commit -m "Close 7a: record the manual checks"
```

---

# Records

(Evidence per task goes here: test output excerpts, manual check results, anything the plan did not
anticipate. Stop and ask rather than deciding unilaterally when something contradicts the design.)

## 7a.1 (2026-09-19)

- Canary for the subagent run. `check.ps1` green, 863 tests (853 + 10 `PaneTests`).
- Plan Step 6 was a no-op: existing invariant assertions match substrings unaffected by the new
  `Panes[i].` prefix.
- Quality review (no critical issues) led to a follow-up commit: `ColumnRef.Column` renamed to
  `ColumnRef.Index`, `Pane.Focused` renamed to `Pane.FocusedColumnOrNull` (avoids
  `state.Focused.Focused`), `WithPane` returns the same state for the identical pane instance, and
  three more tests (shim with `FocusedPane = 1`, `WithPane` unknown id / same instance). The later
  task texts were updated for the renames; 7a.2 gained the no-op rule, 7a.4 making `Panes` required.
- Not taken: the reviewer's note that the record's generated `ToString` can throw on an out-of-range
  `FocusedPane`. Only an invalid state reaches that, and the property tests assert
  `CheckInvariants()` (which never throws) first.

## 7a.2 (2026-09-19)

- 30 Msg/Effect records now carry `ColumnRef At`. Transition addresses through `ColumnAt`/`PaneOf`/
  `WithPane`/`WithColumn`; `RereadWhere` replaces five copies of the "mark Loading and re-read" loop
  and covers every pane. `check.ps1` green, 863 tests after the main commit, 874 after the follow-up.
- Spec review: compliant. Quality review (no critical issues) led to a follow-up commit: six tests
  that address the *unfocused* pane (the single-pane tests could not tell a shim-routed handler
  from a correct one), a non-default `PaneId` round-trip through Runtime and Shell (`PaneId(0)` is
  `default`, so a dropped pane id was undetectable), no-op identity for `FocusColumn` and the
  `EnterDirectory` reuse branch, `RereadWhere` touching only panes that matched.
- Deferred to later tasks (plan updated): scoped `RevealTarget`, `ShellOpCompleted` rereading
  affected folders when its own column is gone, and sections in every drive list (new 7a.8b); App
  helpers and the rename editor addressed by pane rather than focus (7a.10/7a.11); swap and sync end
  a rename (7a.7). Already covered by 7a.3: `state.View` used while deriving another pane's column,
  and the post-steps reading only the focused pane.

## 7a.3 (2026-09-20)

- Preview Msgs/Effects and `ViewRestored` carry a `PaneId`; `Apply` runs `ReconcileChildColumn` and
  `ReconcilePreview` once per pane against that pane's own pre-message snapshot, looked up by id.
  Transition no longer reads the shim at all, so 7a.4 is a deletion rather than an investigation.
  `check.ps1` green, 878 tests.
- Addressing `before` by pane id also fixes a latent bug: the old post-steps compared
  `oldState.Focused` with `state.Focused`, which would have compared two *different* panes once
  `FocusPane` (7a.6) exists, rebuilding a column chain for nothing.
- Review follow-up (`e4c72bc`): `Rederive`'s `remember` flag was dead after `RestoreView` split off;
  the two `PaneOf(id)!` uses in the post-steps became guards (7a.6/7a.7 add handlers that add and
  remove panes); one preview test strengthened to `Assert.Same` on the untouched pane.
- Deferred to later tasks (plan updated): `AppState.ViewAt` plus skipping reconciliation for a pane
  the message did not touch (7a.4); three example tests - same generation in both panes, the pane on
  `CancelPreview`, and `Refresh` not churning previews (7a.5); releasing a closed pane's preview slot
  (7a.9); and, in 7a.12, restoring each pane's view from the App rather than from whichever drive
  read finishes first - with the split restored at startup the bottom pane's read can win, which
  would apply the top pane's stored view to the bottom one.

## 7a.4 (2026-11-19)

- Shim deleted; `Panes` is `required` again; `AppState.ViewAt` replaces six `PaneOf(at.Pane)!.View`
  uses; `Apply` skips reconciling a pane reference-equal to its pre-message snapshot. 877 tests (the
  one removed test exercised only the shim). `check.ps1` green.
- The implementer's session stalled before reporting, with eight files rewritten from CRLF to LF
  (a 7,600-line diff that was really 490 lines). The controller restored CRLF, refreshed the counts
  and committed; both reviews then ran against the commit.
- Spec review: compliant. Quality review: no critical issues. It confirmed the skip is exactly
  behaviour-preserving - both reconcilers read only `before` and the pane, never shared state - and
  that precondition is now written next to the skip and on both reconcilers (follow-up commit).
- Deferred (plan updated): a focused pane other than pane 0 is untested until `FocusPane` exists,
  and `AssertStatesEqual` had drifted behind the records - both into 7a.5, with a `WithFocused` test
  helper; `ResolveShellName` joins 7a.10's list of helpers to address by pane; the menu key's
  context-menu position, the window's initial `Focus()` and a pane id in the input trace into 7a.11.

## 7a.5 (2026-11-19)

- Tests only. `GenPaneId`/`GenColumnRef` address other and nonexistent panes; per-pane generation
  monotonicity; five `PaneTests` (same generation in both panes, `CancelPreview` names its pane,
  `Refresh` leaves previews alone, sort and cursor memory with the *bottom* pane focused); a
  status-bar test with `FocusedPane = 1`; `WithFocused` test helper; `AssertStatesEqual` rewritten to
  compare records with collection members blanked, so new scalar fields are covered automatically.
  No src/ bug exposed. 883 tests, `check.ps1` green.
- Two of the plan's example tests were wrong as written: the cursor started on a file whose preview
  was still `None`, so the move was a no-op (one failed, one would have passed only because both
  generations were 0). The implementer moved the cursor to index 1 first and asserted the
  precondition; the spec review confirmed the intent survived.
- Quality review follow-up: jobs compared element-wise in `AssertStatesEqual` (`Job.Sources` is an
  array, compared by reference inside record equality - a false failure waiting for the first
  reachable paste), and the remaining `WithPane(x.Focused with …)` sites converted.
- Deferred (plan updated): widening and weighting `GenPaneId` once `ToggleSplit` makes ids beyond 1
  live, a multi-step determinism property with structural effect comparison, and a closed-pane
  property (7a.6); a shared `TwoPanes` builder in TestSupport and `FocusedPane = 1` cases for the
  input trace and `Render` (7a.10).
- Found, not fixed here: a literal BEL (0x07) inside a test path string in `GenEntry`, present since
  before Phase 7 (`e7a4e27`). Suggested as a separate task.

## 7a.6 (2026-11-19)

- `ToggleSplit` / `FocusPane` in Core. A new pane needs no setup code of its own: its pre-message
  snapshot is null, so the existing per-pane post-step gives it a child column and a preview.
  `GenPaneId` weighted and widened; `Apply_is_deterministic` made multi-step with a structural
  effect comparison; a closed-pane property. Spec review compliant.
- Quality review found the main goal only weakly met: `ToggleSplit` was one of ~62 equally weighted
  messages, so only ~14% of property runs ever had two panes - "no bug found" meant little. The
  follow-up commit starts half of all property runs from an open split, makes the closed-pane
  property address an id that *was* live (not one that never existed) with the commonest late
  results, cancels a closed pane's in-flight preview on close (it could hold a worker for 30 s, and
  is 7a.9's slot-release signal), and fixes a rename assertion that passed vacuously.
- Deferred (plan updated): a "`Rename` names a live pane" invariant and moving the split handlers
  and the property tests into their own files (7a.7); a close-during-reread case for the reveal
  (7a.8b); releasing a pane's preview slot on that cancel (7a.9).
