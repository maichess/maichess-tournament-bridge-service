using System.Collections.Concurrent;

namespace MaichessTournamentBridgeService.Kafka;

// Correlates outstanding bot-move requests with their replies. Register returns a
// Task that completes when Complete is called with the matching request_id; the
// caller (KafkaEngineMoveSource) abandons it via Cancel on timeout. A reply for an
// unknown or already-completed request_id is ignored. Thread-safe: the producer
// side registers/cancels, the engine.events.v1 consumer completes.
internal sealed class PendingBotMoves
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> pending =
        new(StringComparer.Ordinal);

    // Begin tracking requestId; the returned Task yields the move UCI once it arrives.
    public Task<string> Register(string requestId)
    {
        TaskCompletionSource<string> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[requestId] = tcs;
        return tcs.Task;
    }

    // Deliver a reply. Returns true if a caller was waiting on requestId, false if it
    // was unknown or already settled.
    public bool Complete(string requestId, string moveUci) =>
        pending.TryRemove(requestId, out TaskCompletionSource<string>? tcs)
        && tcs.TrySetResult(moveUci);

    // Stop tracking requestId without delivering a reply (caller timed out / failed).
    public void Cancel(string requestId) => pending.TryRemove(requestId, out _);
}
