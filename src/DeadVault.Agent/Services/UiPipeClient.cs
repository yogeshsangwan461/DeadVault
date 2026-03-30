using System.IO.Pipes;
using System.Text.Json;
using DeadVault.Core.Ipc;
using DeadVault.Core.Services;

namespace DeadVault.Agent.Services;

/// <summary>
/// Sends requests to the UI's reverse pipe (DeadVault_UI_Pipe).
/// Used by the Agent to ask the UI to show the version prompt dialog.
/// </summary>
public class UiPipeClient
{
    private const string PipeName = "DeadVault_UI_Pipe";

    public async Task<VersionPromptResponse?> RequestVersionPromptAsync(VersionPromptRequest request, int timeoutMs = 60000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous);

            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(cts.Token);

            using var writer = new StreamWriter(client) { AutoFlush = true };
            using var reader = new StreamReader(client);

            var msg = new IpcMessage
            {
                Command = IpcMessage.Commands.VersionPrompt,
                ProjectId = request.ProjectId,
                Payload = JsonSerializer.Serialize(request),
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(msg));

            // Wait for user to interact with the dialog (up to 60s)
            string? responseLine = await reader.ReadLineAsync(cts.Token);
            if (responseLine == null) return null;

            return JsonSerializer.Deserialize<VersionPromptResponse>(responseLine);
        }
        catch (OperationCanceledException)
        {
            VaultLogger.Warn("Version prompt timed out (UI not responding)");
            return null;
        }
        catch (Exception ex)
        {
            VaultLogger.Warn($"Version prompt failed (UI may not be running): {ex.Message}");
            return null;
        }
    }

    public async Task<bool> IsUiRunningAsync()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(1000);
            await client.ConnectAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
