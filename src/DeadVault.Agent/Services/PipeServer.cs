using System.IO.Pipes;
using System.Text.Json;
using DeadVault.Core.Ipc;
using DeadVault.Core.Services;

namespace DeadVault.Agent.Services;

public class PipeServer
{
    private readonly AgentOrchestrator _orchestrator;
    private const string PipeName = "DeadVault_IPC_Pipe";

    public PipeServer(AgentOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        VaultLogger.Info($"Pipe server starting on: {PipeName}");

        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;

            try
            {
                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(ct);
                _ = Task.Run(() => HandleConnectionAsync(server), CancellationToken.None);
                server = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                server?.Dispose();
                break;
            }
            catch (IOException ex) when (!ct.IsCancellationRequested)
            {
                server?.Dispose();
                VaultLogger.Warn($"Pipe server transport reset: {ex.Message}");
                await Task.Delay(250, ct);
            }
            catch (Exception ex)
            {
                server?.Dispose();
                VaultLogger.Error("Pipe server error", ex);
                await Task.Delay(1000, ct);
            }
        }

        VaultLogger.Info("Pipe server stopped");
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server)
    {
        await using var ownedServer = server;
        using var reader = new StreamReader(server);
        using var writer = new StreamWriter(server) { AutoFlush = true };

        try
        {
            string? line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
                return;

            IpcResponse response;
            try
            {
                var msg = JsonSerializer.Deserialize<IpcMessage>(line);
                response = msg == null
                    ? new IpcResponse { Success = false, Message = "Invalid message" }
                    : await _orchestrator.HandleCommandAsync(msg);
            }
            catch (Exception ex)
            {
                response = new IpcResponse { Success = false, Message = ex.Message };
            }

            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }
        catch (IOException)
        {
            // Client disconnected.
        }
        catch (ObjectDisposedException)
        {
            // Shutdown race.
        }
    }
}
