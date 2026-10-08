using System.Collections.Concurrent;
using Metrics.Application.Logs;

namespace Metrics.Infrastructure.Caching;

/// <summary>
/// One pending signal per automation. <see cref="Notify"/> completes it and removes it, so the next waiter gets a fresh one.
/// Single-server only: a change made on another server is not seen here (the long-poll's periodic database re-check
/// bounds that delay). Scaling out means replacing this with Redis pub/sub behind <see cref="IChangeNotifier"/>.
/// </summary>
public sealed class InProcessChangeNotifier : IChangeNotifier
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _pending = new();

    public Task NextChangeAsync(Guid automationId) =>
        _pending.GetOrAdd(automationId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    public void Notify(Guid automationId)
    {
        if (_pending.TryRemove(automationId, out var signal)) signal.TrySetResult();
    }
}
