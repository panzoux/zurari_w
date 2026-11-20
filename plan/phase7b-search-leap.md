# Phase 7b — Incremental search / Leap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** zrr's two keyboard finders in the GUI: `/` highlights matches in the focused column without hiding anything, and F3 (Leap) narrows the column as you type and walks down through folders - both understanding romaji for Japanese names through migemo.

**Architecture:** One pure matcher in Core (`NameQuery`: AND segments, literal tiers, regex, migemo slots). Migemo patterns are expanded in the Runtime (cmigemo when installed, otherwise a C# port of rustmigemo with an embedded BSD dictionary) and arrive as compiled `Regex` objects in a Msg, so Core never compiles a large pattern on the UI thread. Search state and Leap state live on `Pane`; Leap's filter is a new input of `Column.Derive`, so `AllEntries`, marks and the subset invariant hold unchanged. The App shows one text box in the path bar's place for both modes and turns its text and a few intercepted keys into Msgs.

**Tech Stack:** C# 12 / .NET 8, WPF, xunit + CsCheck (`Gen`), BannedApiAnalyzers, `scripts/check.ps1`. Rust (`cargo`) only once, to generate the migemo golden file.

**Design:** [2026-09-19-phase7-dual-pane-and-search-design.md](../docs/superpowers/specs/2026-09-19-phase7-dual-pane-and-search-design.md), sections "共通マッチャ", "migemo", "`/` 検索", "Leap", "モード中の再読込の保留".
**Summary plan:** [phase7-dual-pane-and-search.md](phase7-dual-pane-and-search.md)
**Branch:** `phase7b-search-leap`, cut from `phase7a-screen-split` (7b needs `Pane`; 7a is not on main yet).

---

# Status

1153 tests, `scripts/check.ps1` green.

<sub>The test count is written by `scripts/status.ps1`; do not edit it by hand. Write `(this commit)`
in a row's commit cell (no backticks) and `status.ps1` fills in the hash after the commit lands.</sub>

| | Item | Status | Commit |
|---|---|---|---|
| 7b.0 | This plan | `[x]` | `cb6613a` |
| 7b.1 | Core: `NameQuery` matcher (segments, tiers, regex, migemo slots) | `[x]` | `d4ceaec` |
| 7b.2 | Runtime: built-in migemo (rustmigemo port + embedded dictionary + golden test) | `[x]` | `4e08ff4` |
| 7b.3 | Core: `/` search (`Pane.Search`, Msgs, migemo round trip, deferred rereads) | `[x]` | `155e242` |
| 7b.4 | Core: Leap (`Column` filter in `Derive`, `Pane.Leap`, auto-enter, blank preview) | `[x]` | `155e242` |
| 7b.5 | Runtime: `ExpandMigemo` executor (cmigemo → built-in), word LRU, a-z prewarm | `[x]` | `f24aedf` |
| 7b.6 | App: mode bar, keys, match highlight, status hints | `[x]` | `72392cf` |
| 7b.7 | Manual verification; roadmap/summary/spec/notices updated | `[x]` hands-on OK 2026-11-20 (single click no longer opens a file; 0.7.0) | `(this commit)` |

---

# Conventions for every task

Same as [phase7a-screen-split.md](phase7a-screen-split.md#conventions-for-every-task): done = tests pass
AND `pwsh scripts/check.ps1` prints `ALL CHECKS GREEN`; never `--no-verify`; `plan/*.md` are CRLF;
no LINQ (`TestEffects.Of<Effect.X>(effects)` in tests); every new `Msg` goes into `GenMsg` in
`tests/Zurari.Core.Tests/TransitionProperties.cs`; write paths with backslashes only through the
Edit/Write tools or raw strings (check.ps1 rejects C0 control bytes).

## Decisions made while planning (not in the design; recorded here)

1. **Migemo patterns are per word, not per query, and carry no `PaneId`.** The design had
   `Effect.ExpandMigemo(PaneId, query)` → `Msg.MigemoExpanded(PaneId, query, pattern?)`. Every match
   is per AND segment anyway, and the Runtime's cache is per word (wappsw), so the round trip is
   `Effect.ExpandMigemo(string Word)` → `Msg.MigemoExpanded(string Word, Regex? Pattern)`. A pattern
   is applied to every open search/Leap whose current segments contain that word, and dropped
   otherwise - which is the "a reply for a different query is discarded" rule, per word.
2. **The pattern arrives compiled.** `MigemoExpanded` carries a `System.Text.RegularExpressions.Regex`
   built by the Runtime with `NonBacktracking | IgnoreCase | CultureInvariant`. The pattern for a
   one-letter word is tens of kilobytes; compiling it in `Transition.Apply` would stall the UI thread
   the expansion was moved off. `Regex` is immutable and its matching is pure, so Core may hold and use
   it. `null` means "no migemo for this word" (none installed, or the pattern failed to compile).
3. **Each open mode keeps the patterns for its own words.** `NameQuery.Patterns` is an
   `ImmutableDictionary<string, Regex?>`; a word present with value `null` but *also* listed in
   `Pending` is still being expanded. Editing the query keeps the entries for words still in it and
   requests the new ones. Nothing is shared in `AppState`, so there is no cache in Core to bound -
   the Runtime's LRU is the only cache.
4. **AND everywhere, including the migemo tier.** An entry matches a segment when the segment is a
   case-insensitive substring of its label *or* the segment's migemo pattern matches. All segments
   must match. zrr's Leap ORed the segments in its migemo fallback (`migemoMatches` collected an entry
   if any segment's pattern matched); that contradicts its own "AND" rule and is not copied.
5. **Leap keeps the listing order.** zrr reordered the visible rows by tier (prefix, then substring,
   then migemo). Here the filter is an input of `Column.Derive`, which sorts by the pane's view, and
   rows that jump between keystrokes are hard to follow. The tiers decide where the **cursor** goes:
   the first prefix match in listing order, else the first substring match, else the first migemo
   match. The migemo tier is used only when the literal tiers are empty (zrr).
6. **`/` search uses the same segments.** `foo bar` matches `foo bar.txt` either way, and it also finds
   `bar - foo.txt`. In regex mode (Ctrl+R) the whole query is one pattern and there are no segments.
7. **What is matched is `Entry.Label`** (what the user reads: `Windows (C:)`, not `C:\`). Section
   headers never match and are never shown by a non-empty Leap filter.
8. **One text box for both modes, holding only what is being typed.** It sits where the path bar is.
   For Leap it holds the *local* filter; the trail (`mapm/`) is a label in front of it. That keeps
   the text box ordinary - Backspace and IME work as in any text box - and Core never has to diff a
   whole buffer. The App intercepts the keys a mode owns before the text box sees them:
   - search: ↑ ↓ Ctrl+P Ctrl+N (`SearchStep`), Ctrl+R (`ToggleSearchRegex`), Enter / Esc (`EndSearch`).
   - Leap: ↑ ↓ Ctrl+P Ctrl+N (cursor keys on the focused column), → (`LeapStepIn`, or `EndLeap` on a
     file), ← and Backspace in an empty box (`LeapStepOut`), `/` (`LeapStepIn`), Ctrl+K
     (`LeapToRoot`), Ctrl+U (empties the box), Enter / F3 / Esc (`EndLeap`).
9. **When a mode closes by itself.** Losing keyboard focus (a click in a pane, Alt+Tab back is fine:
   only focus moving to another element inside the window counts) confirms it: `EndSearch(Restore:false)`
   / `EndLeap(Restore:false)`. Core also closes it, as a confirm, when `FocusedPane` changes, and closes
   a search when its pane's `FocusedColumn` changes - those can happen without the box losing focus
   (`FocusPane` from Tab, a click). Leap moves `FocusedColumn` itself and is not closed by it.
10. **Deferred rereads re-read the whole pane.** While a pane has a mode open, `RereadWhere`
    (external change, job finished, shell op, rename elsewhere, F5) marks that pane
    `NeedsRefresh` instead of re-reading its columns. Closing the mode re-reads every column of the
    pane and clears the flag (zrr `RefreshPaneNowAsync`). Reads for columns the mode itself opens
    (Leap entering a folder) are not rereads and go out at once.
11. **The built-in migemo lives in `Zurari.Runtime`** (`src/Zurari.Runtime/Migemo/`, namespace
    `Zurari.Runtime.Migemo`), the dictionary as an embedded resource. The rustmigemo checkout on this
    machine is `36a17c4` (the design said `121101d`); the golden file records the revision it was made
    with.
12. **No debounce.** Literal matching of one column is cheap enough per keystroke, and migemo is
    asynchronous. If profiling on a 50k-entry folder says otherwise, the App may coalesce
    `TextChanged` (the design allows it); Core stays clock-free.

---

# Task 7b.1 — Core: `NameQuery`

**Files:** Create `src/Zurari.Core/NameQuery.cs`; test `tests/Zurari.Core.Tests/NameQueryTests.cs`.

```csharp
public enum MatchTier { None, Prefix, Substring, Migemo }

public sealed record NameQuery
{
    public static NameQuery Literal(string text);   // AND segments
    public static NameQuery Regex(string text);     // one NonBacktracking pattern; IsInvalid when it does not compile
    public string Text { get; }
    public bool IsRegex { get; }
    public bool IsInvalid { get; }                  // regex only
    public bool IsEmpty { get; }                    // no segments / empty pattern
    public ImmutableArray<string> Segments { get; } // unescaped; literal only
    public ImmutableDictionary<string, Regex?> Patterns { get; init; }  // migemo, by segment
    public ImmutableHashSet<string> Pending { get; init; }              // words asked for, not answered
    public ImmutableArray<string> WordsToExpand();  // literal segments with no entry in Patterns or Pending
    public NameQuery WithText(string text);         // keeps Patterns/Pending for segments still present
    public NameQuery WithPattern(string word, Regex? pattern); // ignored for a word not in Segments
    public MatchTier Match(string label);
}
```

- [ ] `SplitSegments`: space separates; `\ ` is a literal space; leading/trailing/multiple spaces
      ignored; a lone trailing `\` stays literal. Tests: `"map tex"` → `[map, tex]`, `"a\ b c"` →
      `["a b", c]`, `"  x  "` → `[x]`, `""` → `[]`.
- [ ] Literal `Match`: every segment matches (substring, `OrdinalIgnoreCase`, or its pattern); tier is
      `Prefix` when the label starts with the first segment, `Substring` when all segments are
      substrings, `Migemo` when at least one segment needed its pattern. Tests: `tex2map`/`map2tex` for
      `map tex`; a migemo-only Japanese name with a test `Regex("日本")` for `nihon`.
- [ ] Regex: `NonBacktracking | IgnoreCase | CultureInvariant`; `ArgumentException` and
      `NotSupportedException` (back-references, lookarounds) → `IsInvalid`, `Match` is `None`.
      `IsMatch` → `Substring` tier (search does not rank).
- [ ] `WithText` keeps patterns for surviving words and drops the rest; `WithPattern` for a word no
      longer present returns the same instance.
- [ ] Commit: `Core: NameQuery, the matcher behind search and Leap`.

# Task 7b.2 — Runtime: built-in migemo

**Files:** Create `src/Zurari.Runtime/Migemo/*.cs`, `src/Zurari.Runtime/Migemo/migemo-compact-dict.bin`
(embedded resource), `tests/Zurari.Runtime.Tests/Migemo/*`, golden `tests/Zurari.Runtime.Tests/Migemo/golden.tsv`;
`THIRD-PARTY-NOTICES.txt` (create if absent).

Port the **query path** of rustmigemo (`~\.cargo\git\checkouts\rustmigemo-ab0dfd795e4ce5b2\36a17c4\src\migemo\`):
`bit_vector`, `bit_list` (if `louds_trie` needs it), `louds_trie`, `compact_dictionary`,
`character_converter`, `romaji_processor`, `ternary_regex_generator`, `regex_generator` (only
`RegexOperator.Default`), `query` (with `tokenize`). Not the dictionary builders, not `sorted_array_trie`
unless the query path uses it.

- [ ] Public surface: `public sealed class BuiltinMigemo { public static BuiltinMigemo LoadEmbedded(); public string Query(string word); }`.
      Thread-safe for concurrent `Query` calls (no shared mutable state, or a lock).
- [ ] Dictionary: copy `..\wappsw\assets\migemo-compact-dict.bin` into the project as
      `<EmbeddedResource>`; its licenses (`LICENSE`, `LICENSE_Mozc`, `LICENSE_UniDic` from the same
      folder) and rustmigemo's MIT `LICENSE` go into `THIRD-PARTY-NOTICES.txt` at the repo root.
- [ ] Golden: build the Rust CLI once (`cargo run --features cli --release -- -d <dict> -w <word>`, or
      a tiny loop over a word list) and write `word<TAB>pattern` lines for at least: `a`, `i`, `ka`,
      `kai`, `kaisha`, `nihon`, `nihongo`, `tt`, `tta`, `n`, `nn`, `kyou`, `toukyou`, `shi`, `chi`,
      `tsu`, `fu`, `ja`, `xtu`, `ltu`, `wo`, `vu`, `abc`, `ABC`, `Tower`, `toukyouOosaka`, `東京`,
      `ﾃｽﾄ`, `テスト`, `123`, `a b`, `-`, `.`, `(`, `[`, `\`, `*`. A header line records the rustmigemo
      revision and dictionary size. The test asserts the C# output equals every line exactly.
- [ ] Every golden pattern compiles with `RegexOptions.NonBacktracking` (test).
- [ ] Commit(s): `Runtime: built-in migemo, ported from rustmigemo` (split as convenient).

# Task 7b.3 — Core: `/` search

**Files:** `src/Zurari.Core/Pane.cs`, `Msg.cs`, `Effect.cs`, create `src/Zurari.Core/Transition.Search.cs`;
tests `tests/Zurari.Core.Tests/SearchTests.cs`, `TransitionProperties.cs`.

```csharp
public sealed record SearchState(int ColumnIndex, string? AnchorName, NameQuery Query)
{
    public ImmutableArray<int> Matches { get; init; } = [];   // indices into the column's Entries, ascending
    public int MatchIndex { get; init; } = -1;                // into Matches, -1 when none
}
// Pane: public SearchState? Search { get; init; }  public bool NeedsRefresh { get; init; }

Msg.BeginSearch                        // `/`: on the focused pane's focused column
Msg.SearchTextChanged(string Text)
Msg.SearchStep(int Delta)              // +1 / -1, wraps around
Msg.ToggleSearchRegex
Msg.EndSearch(bool Restore)            // Esc: cursor back to AnchorName; Enter: stays
Msg.MigemoExpanded(string Word, Regex? Pattern)
Effect.ExpandMigemo(string Word)
```

- [ ] `BeginSearch` refused (same instance) while renaming, while a mode is open in any pane, or with
      no focused column; closes sort mode. Records the cursor's entry name as `AnchorName`.
- [ ] Query change → recompute `Matches` over the column's `Entries` (headers skipped), move the cursor
      to the first match at or after the anchor's index, wrapping to the first (zrr
      `FindNearestMatchIndex`); no match leaves the cursor where it is. Emit `ExpandMigemo` for
      `WordsToExpand()` (literal mode only) and add them to `Pending`.
- [ ] `MigemoExpanded` → `WithPattern` in every pane's search whose segments contain the word; recompute.
- [ ] `SearchStep` moves `MatchIndex` with wrap-around and puts the cursor there.
- [ ] Matches stay right when the column's `Entries` change under an open search (a load that was
      already in flight): recompute in a post-step when the column's `Entries` instance differs from the
      one the matches were computed on (keep that reference in `SearchState`).
- [ ] Closing rules (decision 9): post-step closes the search as a confirm when `FocusedPane` changed or
      the pane's `FocusedColumn` is no longer `ColumnIndex`.
- [ ] `NeedsRefresh` (decision 10): `RereadWhere` skips the columns of a pane with `Search` (or, after
      7b.4, `Leap`) and sets its flag; `EndSearch` re-reads all its columns when set.
- [ ] Invariant: an open search's `ColumnIndex` is in range and every `Matches` index is in range.
- [ ] `GenMsg`: all new Msgs, `MigemoExpanded` with a few fixed words and small regexes, sometimes `null`.
- [ ] Commit: `Core: incremental search on /`.

# Task 7b.4 — Core: Leap

**Files:** `src/Zurari.Core/AppState.cs` (`Column`), `Pane.cs`, `Msg.cs`, create `src/Zurari.Core/Transition.Leap.cs`;
tests `tests/Zurari.Core.Tests/LeapTests.cs`, `TransitionProperties.cs`.

```csharp
// Column
public NameQuery? Filter { get; init; }        // narrows Entries; never touches AllEntries
public Column WithFilter(NameQuery? filter, ViewOptions view);

public sealed record LeapState(int RootIndex, string? RootCursorName, ImmutableArray<string> Trail, NameQuery Query)
{
    public bool AutoEnterArmed { get; init; }
}
// Pane: public LeapState? Leap { get; init; }

Msg.BeginLeap             // F3
Msg.LeapTextChanged(string Text)
Msg.LeapStepIn            // → on a folder, `/`
Msg.LeapStepOut           // ←, Backspace in an empty box
Msg.LeapToRoot            // Ctrl+K
Msg.EndLeap(bool Restore) // Esc restores; Enter / F3 / → on a file confirm
```

- [ ] `Derive` applies a non-empty `Filter` after hidden/collapsed and before sorting: rows whose label
      matches at any tier stay; when the literal tiers matched anything, migemo-only rows are dropped
      (decision 5); headers are dropped. Cursor follows its entry as today; when its entry is filtered
      out it goes to the best-tier match (decision 5), or -1 when nothing matches.
- [ ] Only the focused column carries a filter. `BeginLeap` sets an empty filter there and records
      `RootIndex` / `RootCursorName`; refused like `BeginSearch`.
- [ ] `LeapTextChanged` → new local query on the focused column (migemo requests as in 7b.3), arm
      auto-enter.
- [ ] `LeapStepIn` on a folder/drive: push the local text onto `Trail`, clear the parent's filter,
      `EnterDirectory(..., FocusChild: true)`, empty filter on the child, arm auto-enter.
      On a file: no-op (the App sends `EndLeap(false)` for → on a file).
- [ ] `LeapStepOut` above `RootIndex`: clear the child's filter, focus the parent, pop `Trail` and make
      that text the parent's filter (zrr `mapm/we` → `mapm`). No auto-enter. At the root: no-op.
- [ ] `LeapToRoot`: focus `RootIndex`, clear `Trail` and every filter, empty query.
- [ ] Auto-enter (post-step): while armed, once the focused column is `Loaded` and no `Pending` word
      could still change its matches (only relevant when the literal tiers are empty), if exactly one
      non-header row is visible and it is a folder or drive, `LeapStepIn`; disarm either way. Chains
      through `DirectoryLoaded`, not timers.
- [ ] `EndLeap`: clear every filter in the pane; `Restore` → focus `RootIndex`, cursor on
      `RootCursorName`. Then the `NeedsRefresh` reread (7b.3).
- [ ] Preview: `ResolveCursorFileTarget` returns none while `pane.Leap` is open, so the preview is
      emptied on entry and loaded on exit by the existing reconcile.
- [ ] `FocusedPane` change closes it as a confirm (decision 9).
- [ ] Invariants: only the focused column of a pane in Leap has a filter, and no column outside Leap has
      one; `RootIndex <= FocusedColumn`.
- [ ] Commit: `Core: Leap (F3), filtering through Column.Derive`.

# Task 7b.5 — Runtime: `ExpandMigemo`

**Files:** create `src/Zurari.Runtime/Migemo/CMigemo.cs`, `MigemoService.cs`; `WorkerRuntime.cs`;
tests `tests/Zurari.Runtime.Tests/Migemo/MigemoServiceTests.cs`.

- [ ] `CMigemo`: zrr's `MigemoProvider` P/Invoke (`migemo_open/query/release/close`), dictionary searched in
      `<exe>\dict\utf-8\migemo-dict`, `<exe>\dict\migemo-dict`, `%APPDATA%\zurari_w\dict\utf-8\migemo-dict`,
      `%APPDATA%\zurari_w\dict\migemo-dict`, then zrr's publish folder
      (`..\zurari\publish\dict\utf-8\migemo-dict` next to a `migemo.dll`). `NativeLibrary.TryLoad` with the
      DLL's full path when found next to the dictionary. Serialize calls with a lock.
- [ ] `MigemoService`: picks cmigemo if it loads, else `BuiltinMigemo` (lazily loaded on first use, off the
      UI thread). `Expand(word)` → compiled `Regex?` (decision 2); an empty or non-compiling pattern →
      `null`. LRU of 100 words.
- [ ] `WorkerRuntime` executes `Effect.ExpandMigemo` on one dedicated background thread/queue (newest
      first is unnecessary: words are cached and each reply is independently useful) and posts
      `Msg.MigemoExpanded`. At startup, queue `a`..`z` at low priority (after anything the user asks).
- [ ] Tests: built-in path returns a pattern that matches `日本` for `nihon`; LRU returns the same
      `Regex` instance for a repeated word; a cmigemo probe failure falls back to built-in.
- [ ] Commit: `Runtime: expand migemo words off the UI thread`.

# Task 7b.6 — App

**Files:** `MainWindow.xaml(.cs)`, `KeyMap.cs`, `StateProjection.cs`, `src/Zurari.Controls/ViewModels.cs`,
`Themes/Generic.xaml`; tests `tests/Zurari.App.Tests/KeyMapTests.cs`, `StateProjectionTests.cs`.

- [ ] `KeyMap.ResolveNormal`: `/` (`Key.OemQuestion`, also `Key.Divide`) → `BeginSearch`, F3 → `BeginLeap`.
- [ ] Mode bar in the path bar's `Border`: prefix label (`/`, or `LEAP ` + trail), a `TextBox`, a right
      label (`[regex]`, `(n/m)` / red `(0)` for search; `(no match)` for Leap). Projected by
      `StateProjection.ProjectModeBar(state)`; the text box is written only when its text differs from
      state (so typing does not reset the caret). Opening a mode focuses the box; closing it hands focus
      back to the focused row.
- [ ] Keys of decision 8 intercepted in the box's `PreviewKeyDown`; `TextChanged` → `SearchTextChanged`
      / `LeapTextChanged`; `LostKeyboardFocus` → confirm (decision 9).
- [ ] `EntryVm.IsSearchMatch` + a highlight in `Generic.xaml` (distinct from cursor, mark and cut).
- [ ] Status hints for both modes (6e.7), and `/ 検索  F3 Leap` in the normal hints.
- [ ] Commit(s): `App: search and Leap bar`.

# Task 7b.7 — Manual verification and documents

- [ ] Checklist (user, on the real machine): `/` with a romaji query on a folder of Japanese names;
      ↑↓ wrap; Ctrl+R with a bad pattern shows red `(0)`; Esc returns the cursor; Leap `map tex`;
      auto-enter chain through single-folder levels; ← / Backspace unwinding; Esc restores; the
      preview is empty during Leap and returns after; IME on, typing `/` and F3; a copy finishing into
      the folder while Leap is open re-reads only after Leap closes; both panes split.
- [ ] Update `plan/roadmap.md`, `plan/phase7-dual-pane-and-search.md`, the design doc (decisions 1-5, 8),
      `kanban/`.

---

# Records

**7b.3 / 7b.4 (as built, one commit — they share the mode post-step and the deferred reread):**
- `LeapState` has no `Query`: the Leap's query *is* the focused column's `Column.Filter`, so there is
  one copy. `Trail` holds `NameQuery`s rather than strings, so stepping out restores the parent's
  migemo patterns too, and `MigemoExpanded` updates the trail as well as the live filter (a word
  answered while it sat in the trail would otherwise stay `Pending` for ever).
- `Transition.ReconcileModes` runs right after `ApplyCore`, before the cursor memory and the per-pane
  reconciles. It closes a search whose pane is unfocused or whose column is no longer focused, closes
  a Leap whose focused column has no filter (that is how "something else moved the focus" shows), and
  runs auto-enter. Auto-enter loops over already-loaded columns within one message, capped at 64
  levels (`MaxAutoEnterDepth`) against a junction that points at its own ancestor.
- Clearing a search's text goes back to the anchor (zrr left the cursor on the last match).
- A late migemo pattern only moves a search's cursor when there were no matches before; for Leap it
  re-derives keeping the cursor on its entry. A cursor the user already moved stays put.
- `Apply_is_deterministic` compares `NameQuery` / `SearchState` / `LeapState` structurally in the test
  (they hold immutable collections, which compare by reference). Core keeps record equality.
- New invariants: a filter only on the focused column of an open Leap; `Trail.Length ==
  FocusedColumn - RootIndex`; search on the focused column with in-range matches; `NeedsRefresh` only
  under an open mode; a mode only in the focused pane.
- Until 7b.5 the Runtime answers every `ExpandMigemo` with `null` (no migemo), routed in `EffectRouting`.

**7b.2 (agent, merged in 9eb930e):** rustmigemo's own output is not deterministic -
`romaji_to_hiragana_predictively` collects suffixes from a `HashSet`, and the generator drops a word
that extends an earlier one, so `n`, `tt`, `abc` ... differed between runs of the Rust CLI. The golden
file was generated from a scratch copy with the suffixes sorted descending, and the C# port uses that
order (`RomajiProcessor.ExpandSuffixes`); longer suffixes first means none are dropped. Rust's Unicode
case/whitespace predicates are approximated with `ToLowerInvariant` and `Rune.Is*`.

**7b.5 (as built):** `WorkerRuntime` takes a `Lazy<MigemoService>`, so opening cmigemo's dictionary (or
the embedded one) happens on the migemo reader thread at first use, never in the App's constructor.
`ExpandMigemo` has its own single reader with a request queue and an idle queue (prewarm a-z); the App
calls `PrewarmMigemo()` after restoring settings. cmigemo is bound with `NativeLibrary` + function
pointers (loaded from the folder its dictionary is in). Verified on this machine: the cmigemo in zrr's
publish folder loads from the x64 process and `nihon` matches 日本.

**7b.6 (as built):** `MainWindow.Modes.cs` holds the bar. The text box is focused at
`DispatcherPriority.Input` after the opening key is handled, so `/` is not typed into the box it opened
(to be confirmed by hand with IME on). Search matches feed `RenderKey` and the `ProjectionCache` key by
array reference - a query that matches nothing changes no column, and the highlight still has to go.
Match highlight is `#FFFFF0A0`, after `IsMarked` and before the strong cursor in the trigger order.
The search count shows `*` while a migemo word is pending, and `(0)` (red) only once nothing is pending.
Smoke-launched the Debug build: the window comes up and stays up. Not driven by keyboard here - that is
7b.7's checklist.
