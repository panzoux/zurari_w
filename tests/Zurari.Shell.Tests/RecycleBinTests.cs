namespace Zurari.Shell.Tests;

/// <summary>
/// Test classes that read the recycle bin and test classes that put things into it, run one at a
/// time.
/// </summary>
/// <remarks>
/// The bin is a single shared resource belonging to the machine, not to a test. Reading it while
/// another test recycles a file is not a race in the code being tested - both are behaving
/// correctly - it just means a listing taken before and a listing taken after legitimately differ.
/// xUnit runs classes in parallel by default, so without this the two happen at once: measured 5
/// failures in 6 runs.
///
/// This is narrower than turning parallelism off for the assembly, which is what a first,
/// wrong diagnosis of the same symptom led to - see the plan's 6f-4.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class SharedRecycleBin
{
    public const string Name = "recycle bin";
}
