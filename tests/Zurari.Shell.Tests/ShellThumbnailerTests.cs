using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zurari.Shell.Tests;

/// <summary>
/// Explorer's thumbnails for previews (6c.5) - and the guarantee that asking for one never leaves a
/// <c>Thumbs.db</c> anywhere.
/// </summary>
/// <remarks>
/// The experiment these rest on, run on this machine before the feature was kept: extracting a
/// thumbnail for a file reached through <c>\\localhost\C$</c> <em>without</em>
/// <c>WTS_EXTRACTDONOTCACHE</c> wrote <c>Thumbs.db</c> beside it; with the flag, it did not. So the
/// danger is Windows' real default behaviour, not a hypothetical, and the flag is what prevents it.
/// </remarks>
public class ShellThumbnailerTests
{
    /// <summary>Hidden and system files too - Thumbs.db is both, and a plain listing skips it.</summary>
    private static readonly EnumerationOptions EverythingIncludingHidden = new()
    {
        AttributesToSkip = 0,
        RecurseSubdirectories = true,
    };

    private static readonly Color Green = Color.FromRgb(0x40, 0xC0, 0x20);

    private static void WriteImage(string file, int width = 400, int height = 300)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = Green.B;
            pixels[i + 1] = Green.G;
            pixels[i + 2] = Green.R;
            pixels[i + 3] = 0xFF;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    private static string[] FilesIn(string dir) =>
        [.. Directory.GetFiles(dir, "*", EverythingIncludingHidden).Select(Path.GetFileName).Order()!];

    /// <summary>
    /// A real extraction of a known image. What this proves is that the interface declarations are
    /// right - a method in the wrong vtable slot does not fail politely - and that the bitmap
    /// conversion produces the picture rather than a black rectangle of the right size.
    /// </summary>
    [StaFact]
    public void A_thumbnail_comes_back_as_a_png_of_the_actual_picture()
    {
        var dir = TempDirectory.Create("zurari-shellthumb");
        try
        {
            var file = Path.Combine(dir, "picture.png");
            WriteImage(file);

            var png = ShellThumbnailer.TryGetPng(file, 256);

            Assert.NotNull(png);
            var decoded = BitmapDecoder.Create(new MemoryStream(png!), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.True(decoded.PixelWidth is > 0 and <= 256, $"width {decoded.PixelWidth} should fit the request");

            var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
            var pixel = new byte[4];
            converted.CopyPixels(new System.Windows.Int32Rect(converted.PixelWidth / 2, converted.PixelHeight / 2, 1, 1), pixel, 4, 0);
            Assert.InRange(pixel[2], Green.R - 16, Green.R + 16);
            Assert.InRange(pixel[1], Green.G - 16, Green.G + 16);
            Assert.InRange(pixel[0], Green.B - 16, Green.B + 16);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [StaFact]
    public void Asking_for_a_thumbnail_leaves_nothing_behind_in_a_local_folder()
    {
        var dir = TempDirectory.Create("zurari-shellthumb");
        try
        {
            var file = Path.Combine(dir, "picture.png");
            WriteImage(file);

            Assert.NotNull(ShellThumbnailer.TryGetPng(file, 256));
            SettleShellWrites();

            Assert.Equal(["picture.png"], FilesIn(dir));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// The case that matters, run for real on every check: the extraction aimed at a network path on
    /// purpose, past the guard that would normally refuse it, so that what is being tested is the
    /// flag itself. Without <c>WTS_EXTRACTDONOTCACHE</c> this folder gets a Thumbs.db - that was
    /// measured, not assumed - so deleting the flag fails this test.
    /// </summary>
    /// <remarks>
    /// Goes through the loopback administrative share, which is a real SMB round trip through the
    /// network redirector. Skipped quietly on a machine where that share is not reachable, since
    /// there is then no network path to test against.
    /// </remarks>
    [StaFact]
    public void Even_a_network_path_gets_no_thumbs_db()
    {
        var local = TempDirectory.Create("zurari-shellthumb-unc");
        try
        {
            var viaShare = @"\\localhost\" + local[0] + "$" + local[2..];
            if (!Directory.Exists(viaShare))
            {
                return; // no loopback share on this machine; nothing network-shaped to test against.
            }

            WriteImage(Path.Combine(local, "picture.png"));

            var png = ShellThumbnailer.ExtractPng(Path.Combine(viaShare, "picture.png"), 256);
            SettleShellWrites();

            Assert.NotNull(png);
            Assert.Equal(["picture.png"], FilesIn(local));
        }
        finally
        {
            TempDirectory.Delete(local);
        }
    }

    /// <summary>
    /// The guard in front of the flag. A UNC path is refused from the string alone - and quickly,
    /// because a server that does not exist takes many seconds to fail to answer, and a refusal that
    /// went and asked would be that slow on every cursor move.
    /// </summary>
    [StaFact]
    public void A_unc_path_is_refused_without_going_to_the_network()
    {
        var watch = Stopwatch.StartNew();

        var png = ShellThumbnailer.TryGetPng(@"\\no-such-host-4f1c9c2e\share\video.mp4", 256);

        Assert.Null(png);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed} - it went looking");
    }

    [Theory]
    [InlineData(@"\\server\share\a.mp4", false)]
    [InlineData(@"\\?\UNC\server\share\a.mp4", false)]
    [InlineData(@"relative\a.mp4", false)]
    [InlineData("", false)]
    public void Only_a_path_plainly_on_a_local_volume_counts_as_local(string path, bool expected)
    {
        Assert.Equal(expected, ShellThumbnailer.IsPlainlyLocal(path));
    }

    [StaFact]
    public void The_system_drive_counts_as_local()
    {
        Assert.True(ShellThumbnailer.IsPlainlyLocal(Path.Combine(Path.GetTempPath(), "a.mp4")));
    }

    [StaFact]
    public void A_file_the_shell_cannot_thumbnail_yields_nothing_rather_than_throwing()
    {
        var dir = TempDirectory.Create("zurari-shellthumb");
        try
        {
            var file = Path.Combine(dir, "data.zurari-unknown-type");
            File.WriteAllBytes(file, [1, 2, 3, 4]);

            var exception = Record.Exception(() => ShellThumbnailer.TryGetPng(file, 256));

            Assert.Null(exception);
            SettleShellWrites();
            Assert.Equal(["data.zurari-unknown-type"], FilesIn(dir));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [StaFact]
    public void A_file_that_does_not_exist_yields_nothing()
    {
        Assert.Null(ShellThumbnailer.TryGetPng(Path.Combine(Path.GetTempPath(), "no-such-file-8e2a.png"), 256));
    }

    /// <summary>
    /// The shell may write its cache file after the call returns, when its objects are finally
    /// released - so a check made immediately could pass on a write that simply had not happened
    /// yet. The experiment that established the risk waited the same way.
    /// </summary>
    private static void SettleShellWrites()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Thread.Sleep(1500);
    }
}
