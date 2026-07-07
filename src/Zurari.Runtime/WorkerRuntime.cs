namespace Zurari.Runtime;

/// <summary>
/// Placeholder for the Phase 1 worker pool: executes <see cref="Zurari.Core.Effect"/>s
/// on background workers and posts results back as <see cref="Zurari.Core.Msg"/>s.
/// </summary>
public static class WorkerRuntime
{
    public static string Description => "Executes Effects, produces Msgs (Phase 1)";
}
