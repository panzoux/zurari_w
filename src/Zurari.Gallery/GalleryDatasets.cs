namespace Zurari.Gallery;

/// <summary>
/// Deterministic fake directory trees for the Gallery harness. Every generator is a
/// pure function of its seed (where it takes one) - no file I/O, no clock, no
/// ambient randomness - so switching datasets is reproducible across runs.
/// </summary>
public static class GalleryDatasets
{
    private static readonly DateTime Epoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>20 entries per directory, 3 levels deep.</summary>
    public static GalleryNode Small(int seed = 1)
    {
        var rng = new Random(seed);
        return BuildDirectory("root", rng, entriesPerDir: 20, maxDepth: 3, depth: 0);
    }

    /// <summary>The root's first directory ("big") holds 100,000 flat file entries.</summary>
    public static GalleryNode Huge(int seed = 2)
    {
        var rng = new Random(seed);
        var bigChildren = new GalleryNode[100_000];
        for (var i = 0; i < bigChildren.Length; i++)
        {
            bigChildren[i] = BuildFile($"file_{i:D6}.dat", rng);
        }

        GalleryNode[] children =
        [
            GalleryNode.Directory("big", bigChildren),
            BuildFile("notes.txt", rng),
            GalleryNode.Directory("empty_sibling", []),
        ];
        return GalleryNode.Directory("root", children);
    }

    /// <summary>Japanese/Chinese/emoji names, including one name longer than 100 characters.</summary>
    public static GalleryNode Cjk()
    {
        var rng = new Random(3);
        var veryLongName =
            string.Concat(Enumerable.Repeat("とても長いファイル名の一部です_", 7)) + "終わり.txt";

        GalleryNode[] children =
        [
            GalleryNode.Directory(
                "日本語ディレクトリ",
                [
                    BuildFile("報告書_2026年度.docx", rng),
                    BuildFile(veryLongName, rng),
                ]),
            GalleryNode.Directory(
                "中文文件夹",
                [
                    BuildFile("你好世界.txt", rng),
                ]),
            GalleryNode.Directory(
                "😀🎉📁 emoji フォルダ 🚀🔥",
                [
                    BuildFile("🎂🎁🎈 party file 🎊.png", rng),
                ]),
            BuildFile("Ω≈ç√∫˜µ≤≥÷ mixed symbols.txt", rng),
        ];
        return GalleryNode.Directory("root", children);
    }

    /// <summary>Root containing a single empty directory.</summary>
    public static GalleryNode Empty() =>
        GalleryNode.Directory("root", [GalleryNode.Directory("empty", [])]);

    /// <summary>A chain of 9 nested directories - 8+ columns once pre-expanded (see initialDepth).</summary>
    public static GalleryNode Deep()
    {
        var rng = new Random(4);
        return GalleryNode.Directory("root", [BuildDeepLevel(1, rng)]);
    }

    private static GalleryNode BuildDeepLevel(int depth, Random rng)
    {
        if (depth >= 9)
        {
            return GalleryNode.Directory($"level_{depth}", [BuildFile("bottom.txt", rng)]);
        }

        GalleryNode[] children = [BuildDeepLevel(depth + 1, rng), BuildFile($"sibling_{depth}.txt", rng)];
        return GalleryNode.Directory($"level_{depth}", children);
    }

    private static GalleryNode BuildDirectory(string name, Random rng, int entriesPerDir, int maxDepth, int depth)
    {
        if (depth >= maxDepth)
        {
            return GalleryNode.Directory(name, []);
        }

        var children = new GalleryNode[entriesPerDir];
        for (var i = 0; i < entriesPerDir; i++)
        {
            // The seeded suffix makes names (not just sizes/dates) a function of the seed.
            var isDirectory = depth < maxDepth - 1 && i % 4 == 0;
            children[i] = isDirectory
                ? BuildDirectory($"dir_{depth}_{i}", rng, entriesPerDir, maxDepth, depth + 1)
                : BuildFile($"file_{depth}_{i}_{rng.Next(0, 1000):D3}.txt", rng);
        }

        return GalleryNode.Directory(name, children);
    }

    private static GalleryNode BuildFile(string name, Random rng)
    {
        var size = (long)rng.Next(0, 10_000_000);
        var modified = Epoch.AddMinutes(rng.Next(0, 500_000));
        return GalleryNode.File(name, size, modified);
    }
}
