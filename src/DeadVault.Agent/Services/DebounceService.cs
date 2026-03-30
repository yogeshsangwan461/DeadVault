namespace DeadVault.Agent.Services;

public class DebounceService : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Func<Task> _onElapsed;
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();
    private readonly bool _disabled;

    public DebounceService(TimeSpan delay, Func<Task> onElapsed)
    {
        _delay = delay;
        _onElapsed = onElapsed;
        _disabled = delay == Timeout.InfiniteTimeSpan || delay.TotalMilliseconds < 0;
    }

    public void Signal()
    {
        if (_disabled)
            return;

        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_delay, token);
                    if (token.IsCancellationRequested)
                        return;

                    await _executionGate.WaitAsync(token);
                    try
                    {
                        if (!token.IsCancellationRequested)
                            await _onElapsed();
                    }
                    finally
                    {
                        _executionGate.Release();
                    }
                }
                catch (TaskCanceledException)
                {
                    // Expected - timer was reset.
                }
                catch (Exception ex)
                {
                    Core.Services.VaultLogger.Error("Debounce callback error", ex);
                }
            });
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        _executionGate.Dispose();
    }
}
