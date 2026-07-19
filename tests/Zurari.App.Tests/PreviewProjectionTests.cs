using System.Collections.Immutable;
using Zurari.App;
using Zurari.Core;

namespace Zurari.App.Tests;

public class PreviewProjectionTests
{
    private static AppState StateWithPreview(PreviewState preview) =>
        new()
        {
            Columns = [new Column("", [], Load: LoadState.Loaded)],
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
    public void Binary_kind_projects_the_type_label_via_Text()
    {
        var preview = new PreviewState(1, @"C:\app.exe", PreviewKind.Binary, "PE Executable", [], null);
        var state = StateWithPreview(preview);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Equal(PreviewKind.Binary, vm.Kind);
        Assert.Equal("PE Executable", vm.Text);
    }

    [Fact]
    public void Failed_preview_projects_the_error_message()
    {
        var preview = PreviewState.Initial with { Error = "access denied" };
        var state = StateWithPreview(preview);

        var vm = StateProjection.ProjectPreview(state);

        Assert.Equal("access denied", vm.Error);
    }
}
