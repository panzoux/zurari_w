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
    public void Recycle_bin_file_is_named_and_placed_by_where_it_came_from()
    {
        // What the bin stores it as says nothing; what the user deleted is the only readable thing.
        var preview = new PreviewState(
            3,
            @"C:\$Recycle.Bin\S-1-5-21-1\$R00L0W8.txt",
            PreviewKind.Text,
            "body",
            [],
            null)
        {
            OriginalPath = @"C:\Users\someone\Documents\notes.txt",
        };
        var state = StateWithPreview(preview);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Equal("notes.txt", vm.FileName);
        Assert.Equal(@"C:\Users\someone\Documents", vm.OriginalDirectory);
    }

    [Fact]
    public void A_file_outside_the_bin_has_no_original_directory()
    {
        var preview = new PreviewState(1, @"C:\Users\someone\notes.txt", PreviewKind.Text, "body", [], null);

        var vm = StateProjection.ProjectPreview(StateWithPreview(preview));

        Assert.Equal("notes.txt", vm.FileName);
        Assert.Null(vm.OriginalDirectory);
    }

    [Fact]
    public void A_volume_projects_a_bar_fraction_and_a_summary_line()
    {
        var preview = PreviewState.Initial with
        {
            Generation = 4,
            Kind = PreviewKind.Capacity,
            Capacity = new PreviewCapacity(
                "Windows (C:)", "NTFS · 固定ドライブ", UsedBytes: 768L * 1024 * 1024, TotalBytes: 1024L * 1024 * 1024),
        };

        var vm = StateProjection.ProjectPreview(StateWithPreview(preview));

        Assert.NotNull(vm.Capacity);
        Assert.Equal(0.75, vm.Capacity!.UsedFraction);
        Assert.Equal("256.0 MB", vm.Capacity.Free);
        Assert.Equal("768.0 MB / 1.0 GB 使用中", vm.Capacity.Summary);

        // The metadata block describes the volume rather than a file.
        Assert.Contains("名前: Windows (C:)", vm.MetadataText, StringComparison.Ordinal);
        Assert.Contains("使用済み: 768.0 MB", vm.MetadataText, StringComparison.Ordinal);
        Assert.Contains("空き: 256.0 MB", vm.MetadataText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bin has a size but no size limit. No fraction means the renderer draws no bar, which is
    /// the honest thing - a bar needs something to be full of.
    /// </summary>
    [Fact]
    public void The_recycle_bin_projects_a_count_and_a_size_but_no_bar()
    {
        var preview = PreviewState.Initial with
        {
            Kind = PreviewKind.Capacity,
            Capacity = new PreviewCapacity("ゴミ箱", "ゴミ箱", UsedBytes: 2048, TotalBytes: null, ItemCount: 413),
        };

        var vm = StateProjection.ProjectPreview(StateWithPreview(preview));

        Assert.Null(vm.Capacity!.UsedFraction);
        Assert.Null(vm.Capacity.Free);
        Assert.Equal("2.0 KB", vm.Capacity.Summary);
        Assert.Contains("項目数: 413 件", vm.MetadataText, StringComparison.Ordinal);
        Assert.DoesNotContain("容量:", vm.MetadataText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_drive_that_is_not_ready_shows_what_it_is_instead_of_figures()
    {
        var preview = PreviewState.Initial with
        {
            Kind = PreviewKind.Capacity,
            Capacity = new PreviewCapacity("E:", "光学ドライブ", Status: "準備できていません"),
        };

        var vm = StateProjection.ProjectPreview(StateWithPreview(preview));

        Assert.Equal("準備できていません", vm.Capacity!.Summary);
        Assert.Null(vm.Capacity.UsedFraction);
        Assert.Null(vm.Capacity.Free);
        Assert.Contains("種類: 光学ドライブ", vm.MetadataText, StringComparison.Ordinal);
        Assert.Contains("状態: 準備できていません", vm.MetadataText, StringComparison.Ordinal);
        Assert.DoesNotContain("使用済み", vm.MetadataText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_preview_has_no_capacity()
    {
        var preview = new PreviewState(1, @"C:\a.txt", PreviewKind.Text, "body", [], null);

        Assert.Null(StateProjection.ProjectPreview(StateWithPreview(preview)).Capacity);
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

    /// <summary>
    /// A thumbnail standing in for a document or video names that file's type, and the label stays
    /// in the metadata block - it is not a text body to show over the picture.
    /// </summary>
    [Fact]
    public void A_thumbnail_image_names_the_type_of_the_file_it_stands_for()
    {
        var metadata = new PreviewMetadata("paper.pdf", 204800, new DateTime(2024, 1, 1), new DateTime(2024, 1, 2));
        var preview = new PreviewState(1, @"C:\paper.pdf", PreviewKind.Image, "PDF Document", [1, 2, 3], null, metadata);

        var vm = StateProjection.ProjectPreview(StateWithPreview(preview));

        Assert.Contains("種類: PDF Document", vm.MetadataText);
        Assert.DoesNotContain("画像ファイル", vm.MetadataText);
        Assert.DoesNotContain("解像度", vm.MetadataText);
        Assert.Null(vm.Text);
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
