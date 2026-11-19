using System.Collections.Concurrent;
using Zurari.Core;
using Zurari.Runtime;
using Zurari.Runtime.Migemo;
using Zurari.TestSupport;

namespace Zurari.Runtime.Tests.Migemo;

/// <summary>Expanding migemo words off the UI thread (7b.5).</summary>
public class MigemoServiceTests
{
    [Fact]
    public void Without_cmigemo_the_built_in_port_expands()
    {
        using var service = MigemoService.Create([]);

        var pattern = service.Expand("nihon");

        Assert.Equal("built-in", service.Source);
        Assert.NotNull(pattern);
        Assert.Matches(pattern, "日本語.txt");
    }

    [Fact]
    public void A_broken_cmigemo_falls_back_to_the_built_in_port()
    {
        var folder = TempDirectory.Create("zurari-migemo");
        try
        {
            File.WriteAllText(Path.Combine(folder, "migemo.dll"), "not a library");
            Directory.CreateDirectory(Path.Combine(folder, "dict"));
            File.WriteAllText(Path.Combine(folder, "dict", "migemo-dict"), "a\tあ");

            using var service = MigemoService.Create([folder]);

            Assert.Equal("built-in", service.Source);
        }
        finally
        {
            TempDirectory.Delete(folder);
        }
    }

    [Fact]
    public void A_repeated_word_comes_back_as_the_same_compiled_pattern()
    {
        var calls = 0;
        using var service = new MigemoService(
            word =>
            {
                Interlocked.Increment(ref calls);
                return word;
            },
            "test");

        var first = service.Expand("abc");

        Assert.Same(first, service.Expand("abc"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void The_least_recently_used_word_is_forgotten_first()
    {
        var calls = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        using var service = new MigemoService(word => { calls.AddOrUpdate(word, 1, (_, n) => n + 1); return word; }, "test");

        service.Expand("keep");
        for (var i = 0; i < MigemoService.Capacity - 1; i++)
        {
            service.Expand("w" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        service.Expand("keep");
        service.Expand("one-more");
        service.Expand("keep");
        service.Expand("w0");

        Assert.Equal(1, calls["keep"]);
        Assert.Equal(2, calls["w0"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("(")]
    [InlineData(@"(a)\1")]
    public void A_pattern_that_is_empty_or_does_not_compile_is_no_pattern(string raw)
    {
        using var service = new MigemoService(_ => raw, "test");

        Assert.Null(service.Expand("x"));
    }

    [Fact]
    public void The_runtime_answers_an_expansion_with_the_compiled_pattern()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var service = new MigemoService(_ => "日本|nihon", "test");
        using var runtime = new WorkerRuntime(
            queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()), migemo: new Lazy<MigemoService>(service));

        runtime.Submit(new Effect.ExpandMigemo("nihon"));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        Msg.MigemoExpanded? reply = null;
        while (reply is null && DateTime.UtcNow < deadline)
        {
            if (queue.TryDequeue(out var msg) && msg is Msg.MigemoExpanded expanded)
            {
                reply = expanded;
            }

            Thread.Sleep(5);
        }

        Assert.NotNull(reply);
        Assert.Equal("nihon", reply.Word);
        Assert.Matches(reply.Pattern!, "日本.txt");
    }

    [Fact]
    public void Prewarming_fills_the_cache_without_posting_anything()
    {
        var queue = new ConcurrentQueue<Msg>();
        var expanded = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        using var service = new MigemoService(word => { expanded[word] = true; return word; }, "test");
        using var runtime = new WorkerRuntime(
            queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()), migemo: new Lazy<MigemoService>(service));

        runtime.PrewarmMigemo();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (expanded.Count < 26 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        Assert.Equal(26, expanded.Count);
        Assert.DoesNotContain(queue, m => m is Msg.MigemoExpanded);
    }
}
