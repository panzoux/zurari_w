using Zurari.Controls;

namespace Zurari.Gallery.Tests;

public class GalleryDatasetsTests
{
    [Fact]
    public void Small_is_deterministic_for_the_same_seed()
    {
        var a = GalleryDatasets.Small(42);
        var b = GalleryDatasets.Small(42);

        Assert.Equal(FlattenNames(a), FlattenNames(b));
    }

    [Fact]
    public void Small_differs_for_a_different_seed()
    {
        var a = GalleryDatasets.Small(1);
        var b = GalleryDatasets.Small(2);

        Assert.NotEqual(FlattenNames(a), FlattenNames(b));
    }

    [Fact]
    public void Huge_puts_100000_entries_under_the_roots_first_directory()
    {
        var root = GalleryDatasets.Huge();
        var big = root.Children[0];

        Assert.Equal(EntryKind.Directory, big.Kind);
        Assert.Equal(100_000, big.Children.Count);
    }

    [Fact]
    public void Huge_is_deterministic_for_the_same_seed()
    {
        var a = GalleryDatasets.Huge(7);
        var b = GalleryDatasets.Huge(7);

        Assert.Equal(a.Children[0].Children[0].Name, b.Children[0].Children[0].Name);
        Assert.Equal(a.Children[0].Children[0].SizeBytes, b.Children[0].Children[0].SizeBytes);
    }

    [Fact]
    public void Cjk_contains_a_name_longer_than_100_characters()
    {
        var names = FlattenNames(GalleryDatasets.Cjk());

        Assert.Contains(names, n => n.Length > 100);
    }

    [Fact]
    public void Empty_root_has_a_single_empty_directory()
    {
        var root = GalleryDatasets.Empty();

        Assert.Single(root.Children);
        Assert.Empty(root.Children[0].Children);
    }

    [Fact]
    public void Deep_chain_is_at_least_8_directories_deep()
    {
        var node = GalleryDatasets.Deep();
        var depth = 0;
        var next = node.Children.FirstOrDefault(c => c.Kind == EntryKind.Directory);
        while (next is not null)
        {
            depth++;
            next = next.Children.FirstOrDefault(c => c.Kind == EntryKind.Directory);
        }

        Assert.True(depth >= 8, $"expected at least 8 nested directories, got {depth}");
    }

    private static List<string> FlattenNames(GalleryNode node)
    {
        var names = new List<string> { node.Name };
        foreach (var child in node.Children)
        {
            names.AddRange(FlattenNames(child));
        }

        return names;
    }
}
