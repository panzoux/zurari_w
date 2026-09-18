using Zurari.App;
using Zurari.Core;

namespace Zurari.App.Tests;

/// <summary>
/// The retry link under a video preview that timed out: how long the next try will wait, and what
/// the pane says once the last try has run out too.
/// </summary>
public class PreviewRetryProjectionTests
{
    private static StateProjection.PreviewVm Project(int attempt, bool timedOut)
    {
        var state = AppState.Initial with
        {
            Preview = new PreviewState(1, @"C:\v\clip.mp4", PreviewKind.Binary, "MP4 Video (ffmpeg: タイムアウト)", [], null)
            {
                Attempt = attempt,
                TimedOut = timedOut,
            },
        };

        return StateProjection.ProjectPreview(state);
    }

    [Fact]
    public void A_first_timeout_offers_a_second_try_with_a_longer_wait()
    {
        var vm = Project(attempt: 1, timedOut: true);

        Assert.Equal("再試行 (20 秒)", vm.RetryLink);
        Assert.Null(vm.GaveUp);
    }

    [Fact]
    public void After_the_last_try_the_pane_says_it_gave_up()
    {
        var vm = Project(attempt: PreviewState.MaxAttempts, timedOut: true);

        Assert.Null(vm.RetryLink);
        Assert.Equal("30 秒待っても作れませんでした", vm.GaveUp);
    }

    [Fact]
    public void Without_a_timeout_there_is_nothing_to_retry()
    {
        var vm = Project(attempt: 1, timedOut: false);

        Assert.Null(vm.RetryLink);
        Assert.Null(vm.GaveUp);
    }
}
