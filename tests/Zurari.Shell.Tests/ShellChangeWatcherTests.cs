using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Zurari.Shell.Tests;

/// <summary>
/// Live drive notifications (6d.9). Plugging a USB stick in cannot be arranged from a test, but the
/// shell will broadcast an event on request - so registration, delivery, the message hook and the
/// event filter are all exercised for real, against the real shell.
/// </summary>
public class ShellChangeWatcherTests
{
    /// <summary>
    /// Runs a message loop until <paramref name="done"/> or the timeout. The notification arrives as
    /// a window message, so something has to pump for it to be delivered at all.
    /// </summary>
    private static void PumpUntil(Func<bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(25),
        };

        timer.Tick += (_, _) =>
        {
            if (done() || DateTime.UtcNow > deadline)
            {
                frame.Continue = false;
            }
        };

        timer.Start();
        try
        {
            // PushFrame runs a real message loop. Dispatcher.Invoke does not: it queues work on the
            // dispatcher, and a shell notification is a window message that nothing would dispatch.
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timer.Stop();
        }
    }

    private static HwndSource CreateMessageWindow() =>
        new(new HwndSourceParameters("zurari-shellwatcher-test") { Width = 0, Height = 0 });

    [StaFact]
    public void It_registers_against_a_real_window()
    {
        using var source = CreateMessageWindow();

        using var watcher = new ShellChangeWatcher(source.Handle, () => { });

        Assert.True(watcher.IsRegistered, "the shell should accept a registration for a real window");
    }

    /// <summary>
    /// The whole point, end to end: the shell announces a drive event and the watcher reports it.
    /// Without this, everything else here would pass against a watcher that never fires.
    /// </summary>
    [StaFact]
    public void A_drive_event_reaches_the_callback()
    {
        using var source = CreateMessageWindow();
        var fired = 0;
        using var watcher = new ShellChangeWatcher(source.Handle, () => Interlocked.Increment(ref fired));
        Assert.True(watcher.IsRegistered);

        source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (watcher.HandleMessage(message, wParam, lParam))
            {
                handled = true;
            }

            return IntPtr.Zero;
        });

        Broadcast(NativeMethods.SHCNE_MEDIAINSERTED);
        PumpUntil(() => Volatile.Read(ref fired) > 0, TimeSpan.FromSeconds(10));

        Assert.True(Volatile.Read(ref fired) > 0, "a media event should have reached the callback");
    }

    /// <summary>
    /// The registration asks for drive events only. Registering for everything would wake the app on
    /// every file written anywhere on the machine, to re-read a listing that had not changed.
    /// </summary>
    [StaFact]
    public void An_event_it_did_not_ask_for_does_not_reach_the_callback()
    {
        using var source = CreateMessageWindow();
        var fired = 0;
        using var watcher = new ShellChangeWatcher(source.Handle, () => Interlocked.Increment(ref fired));
        Assert.True(watcher.IsRegistered);

        source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (watcher.HandleMessage(message, wParam, lParam))
            {
                handled = true;
            }

            return IntPtr.Zero;
        });

        // SHCNE_CREATE - a file appeared. True constantly on a working machine, and none of our
        // business: the drive pane shows drives.
        Broadcast(0x00000002);
        PumpUntil(() => Volatile.Read(ref fired) > 0, TimeSpan.FromSeconds(2));

        Assert.Equal(0, Volatile.Read(ref fired));
    }

    [StaFact]
    public void A_message_that_is_not_ours_is_left_alone()
    {
        using var source = CreateMessageWindow();
        using var watcher = new ShellChangeWatcher(source.Handle, () => Assert.Fail("should not fire"));

        Assert.False(watcher.HandleMessage(0x0010, IntPtr.Zero, IntPtr.Zero)); // WM_CLOSE
    }

    [StaFact]
    public void Without_a_window_it_does_nothing_rather_than_failing()
    {
        using var watcher = new ShellChangeWatcher(IntPtr.Zero, () => Assert.Fail("should not fire"));

        Assert.False(watcher.IsRegistered);
        Assert.False(watcher.HandleMessage(ShellChangeWatcher.NotifyMessage, IntPtr.Zero, IntPtr.Zero));
    }

    [StaFact]
    public void Disposing_twice_is_harmless()
    {
        using var source = CreateMessageWindow();
        var watcher = new ShellChangeWatcher(source.Handle, () => { });

        watcher.Dispose();

        Assert.Null(Record.Exception(watcher.Dispose));
    }

    /// <summary>
    /// Announces an event against a temporary directory. The path is real so the shell has something
    /// to hand out, and it is not a drive root, so nothing else on the machine has any reason to act
    /// on it - what is being tested is delivery, not what the event claims.
    /// </summary>
    private static void Broadcast(int eventId)
    {
        var dir = TempDirectory.Create("zurari-shellnotify");
        var path = Marshal.StringToCoTaskMemUni(dir);
        try
        {
            NativeMethods.SHChangeNotify(eventId, NativeMethods.SHCNF_PATHW, path, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
            TempDirectory.Delete(dir);
        }
    }
}
