using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using DeadVault.Core.Ipc;
using DeadVault.Core.Models;
using DeadVault.Core.Services;
using DeadVault.UI.Views;

namespace DeadVault.UI.Services;

/// <summary>
/// Listens on a reverse pipe so the Agent can ask the UI to show dialogs.
/// Runs in the background for the lifetime of the UI app.
/// </summary>
public class UiPipeServer
{
    public const string PipeName = "DeadVault_UI_Pipe";
    private CancellationTokenSource? _cts;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoop(_cts.Token));
        VaultLogger.Info("UI pipe server started");
    }

    public void Stop()
    {
        _cts?.Cancel();
        VaultLogger.Info("UI pipe server stopped");
    }

    private async Task ListenLoop(CancellationToken ct)
    {
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
                VaultLogger.Warn($"UI pipe transport reset: {ex.Message}");
                await Task.Delay(250, ct);
            }
            catch (Exception ex)
            {
                server?.Dispose();
                VaultLogger.Error("UI pipe server error", ex);
                await Task.Delay(1000, ct);
            }
        }
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

            var msg = JsonSerializer.Deserialize<IpcMessage>(line);
            if (msg?.Command == IpcMessage.Commands.VersionPrompt && msg.Payload != null)
            {
                var request = JsonSerializer.Deserialize<VersionPromptRequest>(msg.Payload);
                var response = request != null
                    ? await ShowVersionPromptOnUiThread(request)
                    : new VersionPromptResponse { Answered = false };

                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                return;
            }

            await writer.WriteLineAsync(JsonSerializer.Serialize(new VersionPromptResponse { Answered = false }));
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

    private Task<VersionPromptResponse> ShowVersionPromptOnUiThread(VersionPromptRequest request)
    {
        var tcs = new TaskCompletionSource<VersionPromptResponse>();

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (!SemanticVersion.TryParse(request.CurrentVersion, out var currentVer) || currentVer == null)
                    currentVer = SemanticVersion.Initial;

                var window = new VersionPromptWindow(request.ProjectName, currentVer, request.FilesChanged);
                var result = window.ShowDialog();

                if (result == true && window.SelectedBump.HasValue)
                {
                    string kind = window.SelectedBump.Value switch
                    {
                        VersionBumpKind.Patch => "patch",
                        VersionBumpKind.Minor => "minor",
                        VersionBumpKind.Major => "major",
                        VersionBumpKind.Dev => "dev",
                        VersionBumpKind.Custom => "custom",
                        _ => "patch",
                    };

                    tcs.SetResult(new VersionPromptResponse
                    {
                        Answered = true,
                        BumpKind = kind,
                        CustomVersion = window.CustomVersion,
                        DontAskJustPatch = window.DontAskJustPatch,
                    });
                }
                else
                {
                    tcs.SetResult(new VersionPromptResponse
                    {
                        Answered = true,
                        BumpKind = "patch",
                        DontAskJustPatch = false,
                    });
                }
            }
            catch (Exception ex)
            {
                VaultLogger.Error("Version prompt dialog failed", ex);
                tcs.SetResult(new VersionPromptResponse { Answered = false });
            }
        });

        return tcs.Task;
    }
}
