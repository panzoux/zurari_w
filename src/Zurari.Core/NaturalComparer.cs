namespace Zurari.Core;

/// <summary>
/// Compares names the way a person reads them: runs of digits count as numbers, so <c>file2</c>
/// comes before <c>file10</c>.
/// </summary>
/// <remarks>
/// <para>
/// Ordinal comparison puts <c>file10</c> first, because <c>1</c> sorts before <c>2</c>. That is
/// correct about characters and wrong about what the name means, and it is most wrong exactly where
/// it matters most - a directory of numbered episodes, pages, or takes.
/// </para>
/// <para>
/// Windows' own comparison (<c>StrCmpLogicalW</c>) does this, and is not used here: Core takes no
/// dependency on the shell, and a comparison that changes with the OS version would make the order
/// of a listing untestable. This is a plain implementation with its own tests.
/// </para>
/// </remarks>
public sealed class NaturalComparer : IComparer<string>
{
    /// <summary>The shared instance; this type holds no state.</summary>
    public static NaturalComparer Instance { get; } = new();

    private NaturalComparer()
    {
    }

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var compared = CompareNumbers(x, ref i, y, ref j);
                if (compared != 0)
                {
                    return compared;
                }

                continue;
            }

            var byChar = CompareChars(x[i], y[j]);
            if (byChar != 0)
            {
                return byChar;
            }

            i++;
            j++;
        }

        // One ran out: the shorter name is the prefix of the longer, so it comes first.
        var byLength = (x.Length - i).CompareTo(y.Length - j);
        if (byLength != 0)
        {
            return byLength;
        }

        // Equal to a reader, so the tie has to be broken by something total, or a sort with two
        // names differing only in case would be unstable.
        return string.CompareOrdinal(x, y);
    }

    /// <summary>
    /// Compares the runs of digits starting at <paramref name="i"/>/<paramref name="j"/> as numbers,
    /// advancing both past them.
    /// </summary>
    /// <remarks>
    /// By length after dropping leading zeros, then digit by digit - so this works for numbers far
    /// too long to fit in any integer type, which a file name is under no obligation to respect.
    /// </remarks>
    private static int CompareNumbers(string x, ref int i, string y, ref int j)
    {
        while (i < x.Length - 1 && x[i] == '0' && char.IsDigit(x[i + 1]))
        {
            i++;
        }

        while (j < y.Length - 1 && y[j] == '0' && char.IsDigit(y[j + 1]))
        {
            j++;
        }

        var startX = i;
        var startY = j;
        while (i < x.Length && char.IsDigit(x[i]))
        {
            i++;
        }

        while (j < y.Length && char.IsDigit(y[j]))
        {
            j++;
        }

        var lengthX = i - startX;
        var lengthY = j - startY;
        if (lengthX != lengthY)
        {
            return lengthX.CompareTo(lengthY);
        }

        return string.CompareOrdinal(x, startX, y, startY, lengthX);
    }

    /// <summary>
    /// Case-insensitively, the way Windows treats file names - so <c>Alpha</c> and <c>alpha</c> land
    /// next to each other rather than in separate blocks of the alphabet.
    /// </summary>
    private static int CompareChars(char a, char b) =>
        char.ToUpperInvariant(a).CompareTo(char.ToUpperInvariant(b));

    /// <summary>The extension including its dot, upper-cased, or empty when there is none.</summary>
    /// <remarks>
    /// A leading dot is a name, not an extension: <c>.gitignore</c> is a file called that, and
    /// <see cref="System.IO.Path.GetExtension"/> would call the whole thing an extension and file it
    /// among the <c>.g</c>s.
    /// </remarks>
    public static string ExtensionOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? string.Empty : name[dot..].ToUpperInvariant();
    }

    /// <summary>Ordinal comparison of two extensions, for <see cref="SortMode.Extension"/>.</summary>
    public static int CompareExtensions(string x, string y) =>
        string.Compare(ExtensionOf(x), ExtensionOf(y), StringComparison.Ordinal);
}
