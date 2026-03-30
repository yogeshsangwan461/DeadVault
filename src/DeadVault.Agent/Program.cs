using DeadVault.Agent.Services;
using DeadVault.Core.Services;
using DeadVault.Store.Services;

namespace DeadVault.Agent;

class Program
{
    static async Task Main(string[] args)
    {
        Console.Title = "DeadVault Agent";
        VaultLogger.Info("=== DeadVault Agent starting ===");
        Console.WriteLine("[DeadVault Agent] Starting...");

        var store = new JsonMetadataStore();
        var repoManager = new RepoManager();
        var snapshotManager = new SnapshotManager();
        var lockManager = new LockManager();

        var orchestrator = new AgentOrchestrator(store, repoManager, snapshotManager, lockManager);
        var pipeServer = new PipeServer(orchestrator);

        using var cts = new CancellationTokenSource();

        // Start pipe server in background
        _ = Task.Run(() => pipeServer.StartAsync(cts.Token));

        // Start watching all registered projects
        await orchestrator.StartAsync();

        Console.WriteLine("[DeadVault Agent] Running. Press Ctrl+C to stop.");

        // Block until Ctrl+C
        var shutdownTcs = new TaskCompletionSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("[DeadVault Agent] Shutting down...");
            shutdownTcs.TrySetResult();
        };

        await shutdownTcs.Task;

        cts.Cancel();
        orchestrator.Stop();

        VaultLogger.Info("=== DeadVault Agent stopped ===");
        Console.WriteLine("[DeadVault Agent] Stopped.");
    }
}
