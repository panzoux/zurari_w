using System.Reflection;

namespace Zurari.Runtime.Migemo;

/// <summary>
/// Built-in migemo: turns romaji such as <c>nihon</c> into a regex that also matches 日本, にほん and ニホン.
/// A C# port of the query path of rustmigemo (MIT, by oguna; see THIRD-PARTY-NOTICES.txt) with the
/// compact dictionary embedded as a resource. Instances are immutable, so <see cref="Query"/> may be
/// called concurrently from any number of threads.
/// </summary>
public sealed class BuiltinMigemo
{
    private const string ResourceName = "migemo-compact-dict.bin";

    private readonly CompactDictionary _dictionary;
    private readonly RomajiProcessor _romaji = new();

    private BuiltinMigemo(CompactDictionary dictionary)
    {
        _dictionary = dictionary;
    }

    /// <summary>Loads the dictionary embedded in this assembly.</summary>
    public static BuiltinMigemo LoadEmbedded()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The embedded migemo dictionary is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Load(memory.ToArray());
    }

    /// <summary>Loads a compact dictionary (rustmigemo format) from <paramref name="dictionary"/>.</summary>
    public static BuiltinMigemo Load(byte[] dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        return new BuiltinMigemo(CompactDictionary.Parse(dictionary));
    }

    /// <summary>
    /// The regex source for <paramref name="word"/> (rustmigemo <c>query</c> with the default operator): the
    /// query is split into words and each is expanded. Empty input gives an empty string.
    /// </summary>
    public string Query(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return MigemoQuery.Query(word, _dictionary, _romaji);
    }
}
