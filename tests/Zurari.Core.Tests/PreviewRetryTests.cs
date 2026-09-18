using Zurari.Core;

namespace Zurari.Core.Tests;

/// <summary>
/// A preview whose thumbnail timed out can be tried again with a longer wait - three attempts in
/// all, then it gives up. Moving to another file starts over.
/// </summary>
public class PreviewRetryTests
{
    private const string Clip = @"C:\v\clip.mp4";

    private static Column Folder() =>
        new(
            new Location.RealDirectory(@"C:\v"),
            [new Entry("clip.mp4", EntryKind.File), new Entry("other.mp4", EntryKind.File)],
            Cursor: 0,
            Load: LoadState.Loaded);

    private static AppState TimedOut(int attempt) => new()
    {
        Columns = [Folder()],
        FocusedColumn = 0,
        Preview = new PreviewState(5, Clip, PreviewKind.Binary, "MP4 Video (ffmpeg: タイムアウト)", [], null)
        {
            Attempt = attempt,
            TimedOut = true,
        },
    };

    [Fact]
    public void A_timed_out_preview_is_retried_as_the_next_attempt()
    {
        var (next, effects) = Transition.Apply(TimedOut(1), new Msg.RetryPreview());

        Assert.Equal(PreviewKind.Loading, next.Preview.Kind);
        Assert.Equal(6, next.Preview.Generation);
        Assert.Equal(2, next.Preview.Attempt);
        Assert.False(next.Preview.TimedOut);
        Assert.Equal(
            new Effect.LoadPreview(6, Clip, PreviewTarget.File, Attempt: 2),
            Assert.Single(effects.OfType<Effect.LoadPreview>()));
    }

    [Fact]
    public void After_the_last_attempt_there_is_no_retry()
    {
        var state = TimedOut(PreviewState.MaxAttempts);

        var (next, effects) = Transition.Apply(state, new Msg.RetryPreview());

        Assert.Equal(state.Preview, next.Preview);
        Assert.Empty(effects);
    }

    [Fact]
    public void A_preview_that_did_not_time_out_is_not_retried()
    {
        var state = TimedOut(1) with { Preview = TimedOut(1).Preview with { TimedOut = false } };

        var (next, effects) = Transition.Apply(state, new Msg.RetryPreview());

        Assert.Equal(state.Preview, next.Preview);
        Assert.Empty(effects);
    }

    [Fact]
    public void A_timeout_reported_by_the_runtime_is_kept_on_the_preview()
    {
        var loading = TimedOut(1) with { Preview = new PreviewState(5, Clip, PreviewKind.Loading, null, [], null) };

        var (next, _) = Transition.Apply(
            loading, new Msg.PreviewLoaded(5, PreviewKind.Binary, null, [], "label", null, TimedOut: true));

        Assert.True(next.Preview.TimedOut);
        Assert.True(next.Preview.CanRetry);
    }

    /// <summary>The attempt count belongs to one file; the next file gets the short wait again.</summary>
    [Fact]
    public void Moving_to_another_file_starts_again_at_the_first_attempt()
    {
        var (next, effects) = Transition.Apply(TimedOut(2), new Msg.CursorDown(0));

        Assert.Equal(1, next.Preview.Attempt);
        Assert.False(next.Preview.TimedOut);
        Assert.Equal(1, Assert.Single(effects.OfType<Effect.LoadPreview>()).Attempt);
    }
}
