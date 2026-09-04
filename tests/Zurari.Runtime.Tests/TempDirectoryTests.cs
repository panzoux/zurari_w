namespace Zurari.Runtime.Tests;

/// <summary>
/// The shared cleanup helper (6f-3). Lives in one assembly because the file itself is compiled into
/// every test project - testing it once is enough.
/// </summary>
public class TempDirectoryTests
{
    [Fact]
    public void It_deletes_a_directory_and_everything_in_it()
    {
        var dir = TempDirectory.Create("zurari-tempdir");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "sub", "b.txt"), "y");

        TempDirectory.Delete(dir);

        Assert.False(Directory.Exists(dir));
    }

    /// <summary>
    /// The whole point: a handle someone else is holding must not turn a passing test red. The file
    /// stays, the exception does not escape, and the next attempt (once the handle is gone) works.
    /// </summary>
    [Fact]
    public void A_file_someone_else_still_has_open_does_not_throw()
    {
        var dir = TempDirectory.Create("zurari-tempdir");
        var locked = Path.Combine(dir, "locked.bin");
        File.WriteAllText(locked, "held");

        using (var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var exception = Record.Exception(() => TempDirectory.Delete(dir));

            Assert.Null(exception);
            Assert.True(Directory.Exists(dir), "nothing could have deleted it while it was held");
        }

        TempDirectory.Delete(dir);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void A_read_only_file_is_still_removed()
    {
        var dir = TempDirectory.Create("zurari-tempdir");
        var file = Path.Combine(dir, "readonly.txt");
        File.WriteAllText(file, "x");
        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);

        TempDirectory.Delete(dir);

        Assert.False(Directory.Exists(dir));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_to_delete_is_not_an_error(string? path)
    {
        Assert.Null(Record.Exception(() => TempDirectory.Delete(path)));
        Assert.Null(Record.Exception(() => TempDirectory.Delete(Path.Combine(Path.GetTempPath(), "never-existed-8f2a"))));
    }

    [Fact]
    public void Created_directories_do_not_collide()
    {
        var first = TempDirectory.Create("zurari-tempdir");
        var second = TempDirectory.Create("zurari-tempdir");
        try
        {
            Assert.NotEqual(first, second);
            Assert.True(Directory.Exists(first));
            Assert.True(Directory.Exists(second));
        }
        finally
        {
            TempDirectory.Delete(first);
            TempDirectory.Delete(second);
        }
    }
}
