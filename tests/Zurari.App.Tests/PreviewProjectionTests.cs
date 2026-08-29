using System.Collections.Immutable;
using Zurari.App;
using Zurari.Core;

namespace Zurari.App.Tests;

public class PreviewProjectionTests
{
    private static AppState StateWithPreview(PreviewState preview) =>
        new()
        {
            Columns = [new Column(Location.Drives.Instance, [], Load: LoadState.Loaded)],
            FocusedColumn = 0,
            Preview = preview,
        };

    [Fact]
    public void ProjectPreview_rejects_null_state()
    {
        Assert.Throws<ArgumentNullException>(() => StateProjection.ProjectPreview(null!));
    }

    [Fact]
    public void None_kind_projects_null_file_name()
    {
        var state = StateWithPreview(PreviewState.Initial);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Equal(PreviewKind.None, vm.Kind);
        Assert.Null(vm.FileName);
        Assert.Equal(0, vm.Generation);
    }

    [Fact]
    public void Loading_kind_projects_the_files_last_path_segment_as_the_name()
    {
        var preview = new PreviewState(2, @"C:\Users\someone\notes.txt", PreviewKind.Loading, null, [], null);
        var state = StateWithPreview(preview);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Equal(PreviewKind.Loading, vm.Kind);
        Assert.Equal("notes.txt", vm.FileName);
        Assert.Equal(2, vm.Generation);
    }

    [Fact]
    public void Image_kind_projects_the_image_bytes()
    {
        ImmutableArray<byte> bytes = [1, 2, 3];
        var preview = new PreviewState(1, @"C:\pic.png", PreviewKind.Image, null, bytes, null);
        var state = StateWithPreview(preview);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Equal(PreviewKind.Image, vm.Kind);
        Assert.Equal(bytes, vm.ImageBytes);
        Assert.Null(vm.Text);
    }

    [Fact]
    public void Text_kind_projects_the_decoded_text()
    {
        var preview = new PreviewState(1, @"C:\notes.txt", PreviewKind.Text, "hello world", [], null);
        var state = StateWithPreview(preview);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Equal(PreviewKind.Text, vm.Kind);
        Assert.Equal("hello world", vm.Text);
    }

    [Fact]
    public void Binary_kind_with_no_bytes_projects_just_the_type_label_via_Text()
    {
        var preview = new PreviewState(1, @"C:\app.exe", PreviewKind.Binary, "PE Executable", [], null);
        var state = StateWithPreview(preview);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Equal(PreviewKind.Binary, vm.Kind);
        Assert.Equal("PE Executable", vm.Text);
    }

    [Fact]
    public void Binary_kind_with_bytes_projects_the_label_followed_by_a_hex_dump()
    {
        ImmutableArray<byte> head = [0x4D, 0x5A, 0x90, 0x00];
        var preview = new PreviewState(1, @"C:\app.exe", PreviewKind.Binary, "PE Executable", head, null);
        var state = StateWithPreview(preview);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Equal(PreviewKind.Binary, vm.Kind);
        Assert.StartsWith("PE Executable\n\n", vm.Text);
        Assert.Contains(HexDump.Format(head.AsSpan()), vm.Text);
    }

    [Fact]
    public void Failed_preview_projects_the_error_message()
    {
        var preview = PreviewState.Initial with { Error = "access denied" };
        var state = StateWithPreview(preview);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Equal("access denied", vm.Error);
    }

    [Fact]
    public void No_metadata_projects_an_empty_metadata_text()
    {
        var preview = new PreviewState(1, @"C:\notes.txt", PreviewKind.Text, "hello", [], null, Metadata: null);
        var state = StateWithPreview(preview);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Equal(string.Empty, vm.MetadataText);
    }

    [Fact]
    public void Text_kind_metadata_projects_name_kind_size_and_dates_without_a_resolution_line()
    {
        var metadata = new PreviewMetadata(
            "notes.txt", 1536, new DateTime(2024, 3, 1, 9, 0, 0), new DateTime(2024, 3, 2, 10, 30, 0));
        var preview = new PreviewState(1, @"C:\notes.txt", PreviewKind.Text, "hello", [], null, metadata);
        var state = StateWithPreview(preview);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Contains("ファイル名: notes.txt", vm.MetadataText);
        Assert.Contains("種類: テキストファイル", vm.MetadataText);
        Assert.Contains("サイズ: 1.5 KB (1,536 バイト)", vm.MetadataText);
        Assert.Contains("作成日時: 2024-03-01 09:00:00", vm.MetadataText);
        Assert.Contains("更新日時: 2024-03-02 10:30:00", vm.MetadataText);
        Assert.DoesNotContain("解像度", vm.MetadataText);
        Assert.DoesNotContain("色深度", vm.MetadataText);
    }

    [Fact]
    public void Image_kind_metadata_projects_resolution_and_bit_depth()
    {
        var metadata = new PreviewMetadata(
            "pic.png", 204800, new DateTime(2024, 1, 1), new DateTime(2024, 1, 2),
            PixelWidth: 1754, PixelHeight: 804, BitsPerPixel: 24);
        var preview = new PreviewState(1, @"C:\pic.png", PreviewKind.Image, null, [1, 2, 3], null, metadata);
        var state = StateWithPreview(preview);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Contains("種類: 画像ファイル", vm.MetadataText);
        Assert.Contains("解像度: 1754 × 804", vm.MetadataText);
        Assert.Contains("色深度: 24 bit", vm.MetadataText);
    }

    [Fact]
    public void Binary_kind_metadata_reuses_the_detected_type_label_as_the_kind_line()
    {
        var metadata = new PreviewMetadata("app.exe", 2048, new DateTime(2024, 1, 1), new DateTime(2024, 1, 2));
        var preview = new PreviewState(1, @"C:\app.exe", PreviewKind.Binary, "PE Executable", [], null, metadata);
        var state = StateWithPreview(preview);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Contains("種類: PE Executable", vm.MetadataText);
        Assert.DoesNotContain("解像度", vm.MetadataText);
    }
}
