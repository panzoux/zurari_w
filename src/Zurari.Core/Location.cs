namespace Zurari.Core;

/// <summary>
/// Where a <see cref="Column"/> points.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the earlier encoding, in which a column's location was a plain path string and the
/// empty string meant "the drive list". That sentinel had leaked into nineteen call sites across
/// Core, Runtime and App, and it could not express a location that is not a filesystem directory at
/// all (the recycle bin, the inside of an archive, a set of search results).
/// </para>
/// <para>
/// The other thing it could not express is a row whose destination is unrelated to the column it
/// sits in. Child locations used to be built by concatenation - <c>Path.Combine(column.Path,
/// entry.Name)</c> - which forced every child to live underneath its parent. A favorite, a pinned
/// network share, or the recycle bin points wherever it likes, so <see cref="Entry.Target"/> now
/// carries an explicit destination and <see cref="Child"/> only supplies the default.
/// </para>
/// <para>
/// Equality is the compiler-generated record equality, so <see cref="RealDirectory"/> comparisons
/// are ordinal and case-sensitive. That is deliberate: the staleness checks in
/// <see cref="Transition"/> previously compared path strings with <c>!=</c>, and preserving the
/// exact comparison keeps this change behaviour-preserving. Callers that need Windows' actual
/// case-insensitive path semantics normalize explicitly, as they did before.
/// </para>
/// </remarks>
public abstract record Location
{
    private protected Location()
    {
    }

    /// <summary>
    /// The real filesystem path this location denotes, or <c>null</c> when it has none.
    /// </summary>
    /// <remarks>
    /// This is the single seam between the virtual and the real: anything that must hand a path to
    /// the filesystem - a copy destination, a directory to watch, a directory to enumerate - asks
    /// here and must handle <c>null</c> rather than assuming a path exists. It replaces the old
    /// <c>column.Path.Length == 0</c> test, which asked the same question far less clearly.
    /// </remarks>
    public abstract string? FilesystemPath { get; }

    /// <summary>
    /// Where opening the row named <paramref name="entryName"/> leads, when that row does not carry
    /// an explicit <see cref="Entry.Target"/> of its own.
    /// </summary>
    public abstract Location Child(string entryName);

    /// <summary>
    /// The filesystem path of the row named <paramref name="entryName"/>, or <c>null</c> when rows
    /// here have no path of their own.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Child"/> on purpose. <see cref="Child"/> answers "where does
    /// entering this lead", which only makes sense for a container; this answers "what is this row
    /// called on disk", which is also what a *file* row needs - for a delete target, a drag source,
    /// or a preview. Routing file paths through <see cref="Child"/> would mean describing a file as
    /// a <see cref="RealDirectory"/> just to read its path back out.
    /// </remarks>
    public abstract string? ChildPath(string entryName);

    /// <summary>A real directory on the filesystem.</summary>
    /// <param name="Path">Full path of the directory. Not normalized here - see the type's remarks.</param>
    public sealed record RealDirectory(string Path) : Location
    {
        /// <inheritdoc />
        public override string? FilesystemPath => Path;

        /// <inheritdoc />
        public override Location Child(string entryName) => new RealDirectory(ChildPath(entryName));

        /// <inheritdoc />
        public override string ChildPath(string entryName) => System.IO.Path.Combine(Path, entryName);
    }

    /// <summary>
    /// The virtual root: the machine's drives. Has no filesystem path of its own - it is a listing
    /// the Runtime synthesizes rather than a directory it enumerates.
    /// </summary>
    public sealed record Drives : Location
    {
        /// <summary>The single shared instance. Records give value equality, so this is a convenience,
        /// not a requirement - <c>new Drives()</c> compares equal to it.</summary>
        public static Drives Instance { get; } = new();

        /// <inheritdoc />
        public override string? FilesystemPath => null;

        /// <inheritdoc />
        /// <remarks>
        /// A drive row's <see cref="Entry.Name"/> is already a full path (<c>C:\</c>), so entering
        /// one begins a fresh chain rather than extending this location's own path. This reproduces
        /// the old special case exactly.
        /// </remarks>
        public override Location Child(string entryName) => new RealDirectory(entryName);

        /// <inheritdoc />
        public override string ChildPath(string entryName) => entryName;
    }
}
