using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

public class WorkerRuntimeTests
{
    [Fact]
    public void Placeholder_compiles_and_links()
    {
        Assert.False(string.IsNullOrEmpty(WorkerRuntime.Description));
    }
}
