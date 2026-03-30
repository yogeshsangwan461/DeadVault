using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using DeadVault.Core.Ipc;

namespace DeadVault.UI.Services;

public class PipeClient
{
    private const string PipeName = "DeadVault_IPC_Pipe";

    public async Task<IpcResponse?> SendCommandAsync(IpcMessage message, int timeoutMs = 3000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous);

            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(cts.Token);

            using var writer = new StreamWriter(client) { AutoFlush = true };
            using var reader = new StreamReader(client);

            await writer.WriteLineAsync(JsonSerializer.Serialize(message))
                .WaitAsync(cts.Token);

            string? response = await reader.ReadLineAsync()
                .WaitAsync(cts.Token);
            if (response == null) return null;

            return JsonSerializer.Deserialize<IpcResponse>(response);
        }
        catch (TimeoutException)
        {
            return new IpcResponse { Success = false, Message = "Agent not running (timeout)" };
        }
        catch (OperationCanceledException)
        {
            return new IpcResponse { Success = false, Message = "Agent not running (timeout)" };
        }
        catch (IOException ex)
        {
            return new IpcResponse { Success = false, Message = $"Agent pipe unavailable: {ex.Message}" };
        }
        catch (Exception)
        {
            return new IpcResponse { Success = false, Message = "Agent not running" };
        }
    }

    public async Task<bool> IsAgentRunningAsync()
    {
        var resp = await SendCommandAsync(new IpcMessage { Command = IpcMessage.Commands.Status }, 4000);
        return resp?.Success == true;
    }
}
