using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

public class ThumbnailCacheTests
{
    private static string CreateTempDir() => TempDirectory.Create("zurari-thumbcache");

    private static string CreateVideoFile(string dir, string name = "clip.mp4")
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
        return path;
    }

    [Fact]
    public async Task A_stored_thumbnail_comes_back()
    {
        var dir = CreateTempDir();
        try
        {
            var cache = new ThumbnailCache(Path.Combine(dir, "cache"));
            await cache.InitialSweep;
            var video = CreateVideoFile(dir);
            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0xAA];

            cache.StoreSuccess(video, png);

            Assert.True(cache.TryGet(video, out var bytes, out var failure));
            Assert.Equal(png, bytes);
            Assert.Null(failure);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public async Task A_stored_failure_comes_back_instead_of_regenerating()
    {
        var dir = CreateTempDir();
        try
        {
            var cache = new ThumbnailCache(Path.Combine(dir, "cache"));
            await cache.InitialSweep;
            var video = CreateVideoFile(dir);

            cache.StoreFailure(video, "ffmpeg: moov atom not found");

            // The whole point: without this the caller pays the full ffmpeg timeout every time the
            // cursor lands on an undecodable file.
            Assert.True(cache.TryGet(video, out var bytes, out var failure));
            Assert.Null(bytes);
            Assert.Equal("ffmpeg: moov atom not found", failure);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public async Task An_unknown_file_is_a_miss()
    {
        var dir = CreateTempDir();
        try
        {
            var cache = new ThumbnailCache(Path.Combine(dir, "cache"));
            await cache.InitialSweep;
            var video = CreateVideoFile(dir);

            Assert.False(cache.TryGet(video, out var bytes, out var failure));
            Assert.Null(bytes);
            Assert.Null(failure);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public async Task Editing_the_file_invalidates_its_entry()
    {
        var dir = CreateTempDir();
        try
        {
            var cache = new ThumbnailCache(Path.Combine(dir, "cache"));
            await cache.InitialSweep;
            var video = CreateVideoFile(dir);
            cache.StoreSuccess(video, [1, 2, 3]);
            Assert.True(cache.TryGet(video, out _, out _));

            // Length and last-write time are both part of the key, so a re-encoded video does not
            // keep showing a frame from the version it replaced.
            File.WriteAllBytes(video, [9, 9, 9, 9, 9, 9, 9, 9]);

            Assert.False(cache.TryGet(video, out var bytes, out _));
            Assert.Null(bytes);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public async Task Two_files_with_the_same_name_in_different_directories_do_not_collide()
    {
        var dir = CreateTempDir();
        try
        {
            var cache = new ThumbnailCache(Path.Combine(dir, "cache"));
            await cache.InitialSweep;
            var a = Path.Combine(dir, "a");
            var b = Path.Combine(dir, "b");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);
            var videoA = CreateVideoFile(a);
            var videoB = CreateVideoFile(b);

            cache.StoreSuccess(videoA, [1, 1, 1]);
            cache.StoreSuccess(videoB, [2, 2, 2]);

            Assert.True(cache.TryGet(videoA, out var bytesA, out _));
            Assert.True(cache.TryGet(videoB, out var bytesB, out _));
            Assert.Equal([1, 1, 1], bytesA);
            Assert.Equal([2, 2, 2], bytesB);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public async Task Sweep_removes_entries_past_the_age_limit_and_keeps_fresh_ones()
    {
        var dir = CreateTempDir();
        try
        {
            var cacheDir = Path.Combine(dir, "cache");
            var cache = new ThumbnailCache(cacheDir);
            await cache.InitialSweep;
            await cache.InitialSweep;
            var fresh = CreateVideoFile(dir, "fresh.mp4");
            var stale = CreateVideoFile(dir, "stale.mp4");

            // Store the stale one first and backdate everything present, then store the fresh one -
            // rather than backdating a file picked out by content, which TryGet would touch back to
            // the present while looking for it.
            cache.StoreSuccess(stale, [4, 5, 6]);
            foreach (var file in Directory.GetFiles(cacheDir))
            {
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-40));
            }

            cache.StoreSuccess(fresh, [1, 2, 3]);

            cache.Sweep();

            Assert.True(cache.TryGet(fresh, out _, out _));
            Assert.False(cache.TryGet(stale, out _, out _));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public async Task A_cache_over_an_unusable_directory_degrades_to_misses_without_throwing()
    {
        var dir = CreateTempDir();
        try
        {
            var video = CreateVideoFile(dir);

            // A path that cannot be created (a directory under a file). Caching is an optimization;
            // losing it must never surface as an error.
            var cache = new ThumbnailCache(Path.Combine(video, "cache"));
            await cache.InitialSweep;

            cache.StoreSuccess(video, [1, 2, 3]);
            cache.StoreFailure(video, "nope");
            cache.Sweep();

            Assert.False(cache.TryGet(video, out _, out _));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }
}
