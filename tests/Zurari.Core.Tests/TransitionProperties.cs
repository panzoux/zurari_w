using System.Collections.Immutable;
using CsCheck;
using Zurari.Core;
using static Zurari.TestSupport.TestPanes;

namespace Zurari.Core.Tests;

/// <summary>
/// Property-based tests (rwf's proptest equivalent). As Msg variants are added,
/// extend <see cref="GenMsg"/> so every property automatically covers them.
/// </summary>
public class TransitionProperties
{
    private static Gen<int> GenColumnIndex => Gen.Int[-2, 4];

    /// <summary>
    /// Ids a generated message may address. ToggleSplit hands out NextPaneId on every open and keeps
    /// the focused pane on close, so after a few toggles the live panes can be any small id; 7 is
    /// never reached in a 50-message sequence and exercises the closed-pane path. Pane 0 is weighted
    /// up so chains of messages to the one pane that always exists at the start stay common.
    /// </summary>
    private static Gen<PaneId> GenPaneId => Gen.FrequencyConst(
        (3, new PaneId(0)),
        (1, new PaneId(1)),
        (1, new PaneId(2)),
        (1, new PaneId(3)),
        (1, new PaneId(7)));

    private static Gen<ColumnRef> GenColumnRef =>
        Gen.Select(GenPaneId, GenColumnIndex).Select(t => new ColumnRef(t.Item1, t.Item2));

    private static Gen<int> GenEntryIndex => Gen.Int[-2, 4];

    private static Gen<int> GenPageSize => Gen.Int[-3, 8];

    private static Gen<string> GenPath => Gen.OneOfConst(
        "",
        @"C:\",
        @"D:\",
        @"C:\Users",
        @"C:\Users\someone",
        @"C:\stale");

    private static Gen<ImmutableArray<string>> GenPaths =>
        GenPath.List[0, 3].Select(list => list.ToImmutableArray());

    /// <summary>
    /// Locations a column or a column-keyed result can refer to. Covers both cases of the union -
    /// the drive list has no filesystem path, which is exactly the branch that used to be the
    /// empty-string sentinel - plus enough distinct real directories that staleness checks
    /// (a result arriving for a location the column no longer shows) are actually exercised.
    /// </summary>
    private static Gen<Location> GenLocation => Gen.OneOfConst<Location>(
        Location.Drives.Instance,
        new Location.RealDirectory(@"C:\"),
        new Location.RealDirectory(@"D:\"),
        new Location.RealDirectory(@"C:\Users"),
        new Location.RealDirectory(@"C:\Users\someone"),
        new Location.RealDirectory(@"C:\stale"));

    private static Gen<bool> GenIsMove => Gen.OneOfConst(true, false);

    private static Gen<int> GenJobId => Gen.Int[-2, 4];

    private static Gen<ConflictDecision> GenConflictDecision => Gen.OneOfConst(
        ConflictDecision.Overwrite, ConflictDecision.Skip, ConflictDecision.Cancel);

    private static Gen<PreviewKind> GenPreviewKind => Gen.OneOfConst(
        PreviewKind.None, PreviewKind.Loading, PreviewKind.Image, PreviewKind.Text, PreviewKind.Binary);

    private static Gen<ImmutableArray<byte>> GenImageBytes => Gen.OneOfConst(
        ImmutableArray<byte>.Empty, ImmutableArray.Create<byte>(1, 2, 3));

    private static Gen<PreviewMetadata?> GenPreviewMetadata => Gen.OneOfConst<PreviewMetadata?>(
        null,
        new PreviewMetadata("file.txt", 1024, new DateTime(2024, 1, 1), new DateTime(2024, 1, 2)),
        new PreviewMetadata(
            "pic.png", 2048, new DateTime(2024, 1, 1), new DateTime(2024, 1, 2), PixelWidth: 100, PixelHeight: 50, BitsPerPixel: 24));

    private static Gen<EntryKind> GenEntryKind => Gen.OneOfConst(
        EntryKind.Drive,
        EntryKind.Directory,
        EntryKind.File,
        EntryKind.Header);

    /// <summary>
    /// Entries carry a group so that collapsing one actually narrows the view - a generated header
    /// with no group would toggle nothing, and the subsequence invariant would never be tested
    /// against a list that is genuinely shorter than the one it came from.
    /// </summary>
    private static Gen<Entry> GenEntry =>
        Gen.Select(
                Gen.OneOfConst("a", "b", "c"),
                GenEntryKind,
                Gen.OneOfConst("g1", "g2"),
                Gen.OneOfConst<string?>(null, @"D:\somewhere"))
            .Select(t => new Entry(t.Item1, t.Item2, Group: t.Item3, OriginalPath: t.Item4));

    private static Gen<ImmutableArray<Entry>> GenEntries =>
        GenEntry.List[0, 3].Select(list => list.ToImmutableArray());

    private static AppState Start(bool split) =>
        split ? Transition.Apply(AppState.Initial, new Msg.ToggleSplit()).State : AppState.Initial;

    private static Gen<Msg> GenMsg => Gen.OneOf(
        Gen.Const<Msg>(new Msg.Noop()),
        GenColumnRef.Select(r => (Msg)new Msg.CursorUp(r)),
        GenColumnRef.Select(r => (Msg)new Msg.CursorDown(r)),
        Gen.Select(GenColumnRef, GenPageSize).Select(t => (Msg)new Msg.CursorPageUp(t.Item1, t.Item2)),
        Gen.Select(GenColumnRef, GenPageSize).Select(t => (Msg)new Msg.CursorPageDown(t.Item1, t.Item2)),
        GenColumnRef.Select(r => (Msg)new Msg.CursorHome(r)),
        GenColumnRef.Select(r => (Msg)new Msg.CursorEnd(r)),
        Gen.Select(GenColumnRef, GenEntryIndex).Select(t => (Msg)new Msg.CursorTo(t.Item1, t.Item2)),
        GenColumnRef.Select(r => (Msg)new Msg.FocusColumn(r)),
        Gen.Select(Gen.Select(GenColumnRef, GenEntryIndex), GenIsMove)
            .Select(t => (Msg)new Msg.EnterDirectory(t.Item1.Item1, t.Item1.Item2, t.Item2)),
        GenColumnRef.Select(r => (Msg)new Msg.GoToParent(r)),
        Gen.Const<Msg>(new Msg.Refresh()),
        Gen.Select(GenColumnRef, GenLocation, GenEntries).Select(t => (Msg)new Msg.DirectoryLoaded(t.Item1, t.Item2, t.Item3)),
        Gen.Select(GenColumnRef, GenLocation).Select(t => (Msg)new Msg.DirectoryLoadFailed(t.Item1, t.Item2, "error")),
        Gen.Select(Gen.Select(GenColumnRef, GenEntryIndex), GenIsMove)
            .Select(t => (Msg)new Msg.DeleteEntry(t.Item1.Item1, t.Item1.Item2, t.Item2)),
        Gen.Select(
            Gen.Select(GenColumnRef, GenEntryIndex),
            Gen.Select(GenPaths, GenIsMove, GenIsMove))
            .Select(t => (Msg)new Msg.DropFiles(t.Item1.Item1, t.Item1.Item2, t.Item2.Item1, t.Item2.Item2, t.Item2.Item3)),
        Gen.Select(Gen.Select(GenColumnRef, GenLocation), GenPaths)
            .Select(t => (Msg)new Msg.ShellOpCompleted(t.Item1.Item1, t.Item1.Item2, t.Item2)),
        Gen.Select(GenColumnRef, GenLocation).Select(t => (Msg)new Msg.ShellOpFailed(t.Item1, t.Item2, "error")),
        Gen.Select(GenColumnRef, GenEntryIndex).Select(t => (Msg)new Msg.ToggleMark(t.Item1, t.Item2)),
        GenColumnRef.Select(r => (Msg)new Msg.ToggleMarkAtCursor(r)),
        GenColumnRef.Select(r => (Msg)new Msg.ClearMarks(r)),
        Gen.Select(GenColumnRef, GenIsMove).Select(t => (Msg)new Msg.DeleteMarked(t.Item1, t.Item2)),
        Gen.Select(
            Gen.Select(GenColumnRef, GenEntryIndex),
            Gen.Select(GenEntryIndex, GenIsMove))
            .Select(t => (Msg)new Msg.MarkRange(t.Item1.Item1, t.Item1.Item2, t.Item2.Item1, t.Item2.Item2)),
        Gen.Select(GenColumnRef, GenPaths, GenIsMove)
            .Select(t => (Msg)new Msg.PasteRequested(t.Item1, t.Item2, t.Item3)),
        Gen.Select(GenJobId, Gen.Select(GenColumnIndex, GenColumnIndex))
            .Select(t => (Msg)new Msg.JobProgress(t.Item1, t.Item2.Item1, t.Item2.Item2, t.Item2.Item1, t.Item2.Item2, null)),
        Gen.Select(GenJobId, GenPaths).Select(t => (Msg)new Msg.JobCompleted(t.Item1, 0, t.Item2)),
        GenJobId.Select(i => (Msg)new Msg.JobFailed(i, "error")),
        Gen.Select(GenJobId, GenPaths).Select(t => (Msg)new Msg.JobCancelled(t.Item1, t.Item2)),
        GenJobId.Select(i => (Msg)new Msg.JobCancelRequested(i)),
        GenJobId.Select(i => (Msg)new Msg.JobDismissed(i)),
        Gen.Select(GenPaneId, Gen.Select(GenJobId, GenPreviewKind), Gen.Select(GenImageBytes, GenPreviewMetadata))
            .Select(t => (Msg)new Msg.PreviewLoaded(t.Item1, t.Item2.Item1, t.Item2.Item2, "text", t.Item3.Item1, "label", t.Item3.Item2)),
        Gen.Select(GenPaneId, GenJobId).Select(t => (Msg)new Msg.PreviewFailed(t.Item1, t.Item2, "error")),
        Gen.Select(GenPaneId, GenJobId)
            .Select(t => (Msg)new Msg.PreviewLoaded(t.Item1, t.Item2, PreviewKind.Binary, null, [], "timed out", null, TimedOut: true)),
        GenPaneId.Select(p => (Msg)new Msg.RetryPreview(p)),
        GenPaths.Select(paths => (Msg)new Msg.SetCutPending(paths)),
        Gen.Select(GenJobId, Gen.Int[0, 5]).Select(t => (Msg)new Msg.JobConflictsFound(t.Item1, t.Item2)),
        Gen.Select(GenJobId, GenConflictDecision).Select(t => (Msg)new Msg.JobConflictResolved(t.Item1, t.Item2)),
        GenPath.Select(p => (Msg)new Msg.ExternalDirectoryChanged(p)),
        GenColumnRef.Select(r => (Msg)new Msg.PinFocusedLocation(r)),
        GenColumnRef.Select(r => (Msg)new Msg.PinEntryAtCursor(r)),
        Gen.Select(GenColumnRef, GenEntryIndex).Select(t => (Msg)new Msg.ToggleSection(t.Item1, t.Item2)),
        Gen.Select(GenColumnRef, Gen.OneOfConst("g1", "g2").List[0, 2])
            .Select(t => (Msg)new Msg.CollapsedGroupsRestored(t.Item1, [.. t.Item2])),
        Gen.OneOfConst(@"E:\", @"Z:\").Select(d => (Msg)new Msg.ImageMounted(d)),
        Gen.OneOfConst("ok", "failed").Select(m => (Msg)new Msg.NoticeRaised(m)),
        Gen.OneOfConst(SortMode.Name, SortMode.Extension, SortMode.Size, SortMode.Modified)
            .Select(m => (Msg)new Msg.SetSortMode(m)),
        Gen.Const<Msg>(new Msg.ToggleDirectoriesFirst()),
        Gen.Const<Msg>(new Msg.ToggleHiddenFiles()),
        Gen.Const<Msg>(new Msg.EnterSortMode()),
        Gen.Const<Msg>(new Msg.ExitSortMode()),
        Gen.OneOfConst(SortMode.Name, SortMode.Extension, SortMode.Size, SortMode.Modified)
            .Select(m => (Msg)new Msg.SetSortDescending(m)),
        GenColumnRef.Select(r => (Msg)new Msg.CreateFolderRequested(r)),
        GenColumnRef.Select(r => (Msg)new Msg.RenameRequested(r)),
        Gen.Const<Msg>(new Msg.RenameCancelled()),
        Gen.OneOfConst("a", "", "bad:name").Select(n => (Msg)new Msg.RenameSubmitted(n)),
        Gen.Select(GenColumnRef, GenLocation).Select(t => (Msg)new Msg.RenameCompleted(t.Item1, t.Item2, "renamed")),
        Gen.Const<Msg>(new Msg.RenameFailed("nope")),
        Gen.Select(GenColumnRef, GenLocation).Select(t => (Msg)new Msg.FolderCreated(t.Item1, t.Item2, "new")),
        Gen.Select(
                GenPaneId,
                Gen.OneOfConst(SortMode.Name, SortMode.Size),
                Gen.OneOfConst(true, false),
                Gen.OneOfConst(true, false))
            .Select(t => (Msg)new Msg.ViewRestored(t.Item1, new ViewOptions(new SortOrder(t.Item2, t.Item3, t.Item4)))),
        Gen.Select(GenPaneId, GenJobId).Select(t => (Msg)new Msg.PreviewCapacityLoaded(
            t.Item1, t.Item2, new PreviewCapacity("vol", "kind", UsedBytes: 1, TotalBytes: 2))),
        Gen.Const<Msg>(new Msg.PlacesChanged()),
        Gen.Const<Msg>(new Msg.ToggleSplit()),
        GenPaneId.Select(p => (Msg)new Msg.FocusPane(p)),
        Gen.Const<Msg>(new Msg.SwapPanes()),
        Gen.Const<Msg>(new Msg.SyncToOtherPane()),
        GenIsMove.Select(m => (Msg)new Msg.CopyToOtherPane(m)));

    [Fact]
    public void Any_msg_sequence_yields_valid_state_and_effects()
    {
        Gen.Select(Gen.Bool, GenMsg.List[0, 50]).Sample(t =>
        {
            var msgs = t.Item2;
            var state = Start(t.Item1);
            foreach (var msg in msgs)
            {
                var (next, effects) = Transition.Apply(state, msg);
                Assert.NotNull(next);
                Assert.NotNull(effects);
                Assert.Empty(next.CheckInvariants());
                // Preview.Generation only ever increases - PreviewLoaded/PreviewFailed never bump
                // it themselves, and ReconcilePreview only bumps forward.
                foreach (var pane in next.Panes)
                {
                    if (state.PaneOf(pane.Id) is { } before)
                    {
                        Assert.True(pane.Preview.Generation >= before.Preview.Generation);
                    }
                }

                state = next;
            }
        });
    }

    [Fact]
    public void Apply_is_deterministic()
    {
        // A prefix first, so jobs, cursor memory, collapsed sections and two panes are present for
        // the comparator to look at; from Initial alone most of it is never exercised.
        // Half the runs start from an open split: ToggleSplit is one of ~60 alternatives, so left to
        // chance most cases never have two panes.
        Gen.Select(Gen.Bool, GenMsg.List[0, 20], GenMsg).Sample(t =>
        {
            var s = Start(t.Item1);
            foreach (var prefixMsg in t.Item2)
            {
                s = Transition.Apply(s, prefixMsg).State;
            }

            var first = Transition.Apply(s, t.Item3);
            var second = Transition.Apply(s, t.Item3);

            AssertStatesEqual(first.State, second.State);
            AssertEffectsEqual(first.Effects, second.Effects);
        });
    }

    /// <summary>
    /// A message addressed to a pane that was open and has been closed changes nothing: same
    /// instance, no effects. Restricted to pane-addressed messages on purpose - those that fan out
    /// to every pane (Refresh, ExternalDirectoryChanged, job results, ShellOpCompleted) legitimately
    /// change state whatever pane id they carry.
    /// </summary>
    [Fact]
    public void A_message_for_a_closed_pane_changes_nothing()
    {
        Gen.Select(Gen.Bool, GenMsg.List[0, 20], Gen.Int[-2, 4], GenLocation, GenEntries).Sample(t =>
        {
            var s = Start(t.Item1);
            foreach (var prefixMsg in t.Item2)
            {
                s = Transition.Apply(s, prefixMsg).State;
            }

            // The smallest id that has been handed out and is no longer live.
            PaneId? found = null;
            for (var id = 0; id < s.NextPaneId && found is null; id++)
            {
                if (s.IndexOfPane(new PaneId(id)) < 0)
                {
                    found = new PaneId(id);
                }
            }

            if (found is not { } closed)
            {
                return;
            }

            var at = new ColumnRef(closed, 0);
            var n = t.Item3;
            var late = new Msg[]
            {
                new Msg.CursorUp(at),
                new Msg.CursorDown(at),
                new Msg.CursorTo(at, n),
                new Msg.EnterDirectory(at, n, FocusChild: n % 2 == 0),
                new Msg.PreviewLoaded(closed, n, PreviewKind.Text, "text", [], "label", null),
                new Msg.PreviewFailed(closed, n, "error"),
                new Msg.RetryPreview(closed),
                new Msg.DirectoryLoaded(at, t.Item4, t.Item5),
                new Msg.DirectoryLoadFailed(at, t.Item4, "error"),
                new Msg.PreviewCapacityLoaded(closed, n, new PreviewCapacity("vol", "kind", UsedBytes: 1, TotalBytes: 2)),
                new Msg.ShellOpFailed(at, t.Item4, "error"),
                new Msg.ViewRestored(closed, ViewOptions.Default),
            };
            foreach (var msg in late)
            {
                var (next, effects) = Transition.Apply(s, msg);

                Assert.Same(s, next);
                Assert.Empty(effects);
            }
        });
    }

    // Record equality compares ImmutableArray members by reference. Two runs from one state share the
    // generated message's arrays but build new ones inside Core, so effects carrying an array are
    // compared with the array blanked and then element by element.
    private static void AssertEffectsEqual(IReadOnlyList<Effect> expected, IReadOnlyList<Effect> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            // When the determinism test fails on a new effect that carries an array, add a case here.
            switch (expected[i])
            {
                case Effect.DeleteToRecycleBin e:
                    var d = Assert.IsType<Effect.DeleteToRecycleBin>(actual[i]);
                    Assert.Equal(e with { Targets = default }, d with { Targets = default });
                    Assert.Equal(e.Targets, d.Targets);
                    break;
                case Effect.ShellCopyOrMove e:
                    var c = Assert.IsType<Effect.ShellCopyOrMove>(actual[i]);
                    Assert.Equal(e with { Paths = default }, c with { Paths = default });
                    Assert.Equal(e.Paths, c.Paths);
                    break;
                case Effect.RunFileJob e:
                    var r = Assert.IsType<Effect.RunFileJob>(actual[i]);
                    Assert.Equal(e with { Sources = default }, r with { Sources = default });
                    Assert.Equal(e.Sources, r.Sources);
                    break;
                case Effect.SetCollapsedGroups e:
                    var g = Assert.IsType<Effect.SetCollapsedGroups>(actual[i]);
                    Assert.Equal(e with { Groups = default }, g with { Groups = default });
                    Assert.Equal(e.Groups, g.Groups);
                    break;
                case Effect.SetPaneViews e:
                    var v = Assert.IsType<Effect.SetPaneViews>(actual[i]);
                    Assert.Equal(e with { TopFirst = default }, v with { TopFirst = default });
                    Assert.Equal(e.TopFirst, v.TopFirst);
                    break;
                default:
                    Assert.Equal(expected[i], actual[i]);
                    break;
            }
        }
    }

    // ImmutableArray<T> and ImmutableHashSet<T> compare by reference under record equality, so two
    // independently built states with equal content would compare unequal. Every such member is
    // blanked on a copy, which leaves record equality to cover every scalar member - a field added
    // later is compared without anyone remembering to list it - and the blanked members are then
    // compared element by element. ImmutableArray<T> goes through Assert.Equal's IEnumerable path.
    private static void AssertStatesEqual(AppState expected, AppState actual)
    {
        Assert.Equal(
            expected with { Panes = default, Jobs = default, CursorMemory = default, CutPending = default },
            actual with { Panes = default, Jobs = default, CursorMemory = default, CutPending = default });
        Assert.Equal(expected.Jobs.Length, actual.Jobs.Length);
        for (var j = 0; j < expected.Jobs.Length; j++)
        {
            var ej = expected.Jobs[j];
            var aj = actual.Jobs[j];
            Assert.Equal(ej with { Sources = default }, aj with { Sources = default });
            Assert.Equal(ej.Sources, aj.Sources);
        }

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
                // Empty rather than default: an ImmutableHashSet is a reference type, so default is null.
                Assert.Equal(ec with { Entries = default, AllEntries = default, CollapsedGroups = ImmutableHashSet<string>.Empty }, ac with { Entries = default, AllEntries = default, CollapsedGroups = ImmutableHashSet<string>.Empty });
                Assert.Equal(ec.Entries, ac.Entries);
                Assert.Equal(ec.AllEntries, ac.AllEntries);
                Assert.True(ec.CollapsedGroups.SetEquals(ac.CollapsedGroups));
            }
        }
    }
}
