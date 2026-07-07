using NetArchTest.Rules;

namespace Zurari.Arch.Tests;

/// <summary>
/// Mechanical enforcement of the layering rules. Project references already
/// make most violations impossible; these tests additionally reject dependencies
/// that references alone cannot catch (e.g. Runtime touching WPF via a shared BCL).
/// </summary>
public class LayeringTests
{
    private static void AssertSuccess(TestResult result)
    {
        Assert.True(
            result.IsSuccessful,
            "Layering violation in: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Core_depends_on_no_other_layer_and_no_io_or_ui()
    {
        AssertSuccess(
            Types.InAssembly(typeof(Zurari.Core.Transition).Assembly)
                .ShouldNot()
                .HaveDependencyOnAny(
                    "Zurari.Runtime",
                    "Zurari.Shell",
                    "Zurari.App",
                    "System.Windows",
                    "System.IO.File",
                    "System.IO.Directory")
                .GetResult());
    }

    [Fact]
    public void Runtime_does_not_depend_on_ui_layers()
    {
        AssertSuccess(
            Types.InAssembly(typeof(Zurari.Runtime.WorkerRuntime).Assembly)
                .ShouldNot()
                .HaveDependencyOnAny("Zurari.Shell", "Zurari.App", "System.Windows")
                .GetResult());
    }

    [Fact]
    public void Controls_depends_on_no_other_zurari_layer()
    {
        // ColumnBrowser is a dumb view: view models in, input events out.
        // The State→VM adapter lives in Zurari.App (Phase 3), never in Controls.
        AssertSuccess(
            Types.InAssembly(typeof(Zurari.Controls.ColumnBrowser).Assembly)
                .ShouldNot()
                .HaveDependencyOnAny(
                    "Zurari.Core",
                    "Zurari.Runtime",
                    "Zurari.Shell",
                    "Zurari.App")
                .GetResult());
    }

    [Fact]
    public void Shell_does_not_depend_on_runtime_or_app()
    {
        AssertSuccess(
            Types.InAssembly(typeof(Zurari.Shell.ShellServices).Assembly)
                .ShouldNot()
                .HaveDependencyOnAny("Zurari.Runtime", "Zurari.App")
                .GetResult());
    }
}
