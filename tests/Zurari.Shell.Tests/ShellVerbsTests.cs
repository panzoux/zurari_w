namespace Zurari.Shell.Tests;

/// <summary>
/// The interop behind mounting a disc image and ejecting a drive. Neither can be exercised for real
/// without a disc in the machine, so what is asserted here is the part that can silently be wrong:
/// that the call is bound, that a refusal comes back as an error rather than a crash or a lie, and
/// that the verb this design rests on is actually registered on this system.
/// </summary>
public class ShellVerbsTests
{
    [StaFact]
    public void A_verb_on_a_path_that_does_not_exist_comes_back_as_an_error()
    {
        var error = ShellVerbs.Invoke("mount", @"Q:\nothing-here-4f1c9c2e.iso");

        Assert.NotNull(error);
        Assert.NotEmpty(error!.Message);
    }

    [StaFact]
    public void An_unknown_verb_is_refused_rather_than_silently_succeeding()
    {
        var error = ShellVerbs.Invoke("definitely-not-a-verb", @"Q:\nothing-here-4f1c9c2e.iso");

        Assert.NotNull(error);
    }

    /// <summary>
    /// The whole no-elevation approach rests on Windows registering a <c>mount</c> verb for .iso
    /// (<c>Windows.IsoFile</c>). If that ever stops being true, opening an image would fail at the
    /// point of use with nothing explaining why.
    /// </summary>
    [StaFact]
    public void Windows_still_registers_the_mount_verb_for_iso_files()
    {
        using var isoClass = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(".iso");
        var progId = isoClass?.GetValue(null) as string;
        Assert.False(string.IsNullOrEmpty(progId), ".iso has no registered file class");

        using var verbs = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\mount");
        Assert.NotNull(verbs);
    }
}
