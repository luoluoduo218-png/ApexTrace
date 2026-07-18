namespace ApexTrace.Core;

public sealed class SessionStateMachine
{
    private static readonly IReadOnlyDictionary<AppSessionState, AppSessionState[]> Allowed =
        new Dictionary<AppSessionState, AppSessionState[]>
        {
            [AppSessionState.Disconnected] = [AppSessionState.GameDetected, AppSessionState.Faulted],
            [AppSessionState.GameDetected] = [AppSessionState.SharedMemoryReady, AppSessionState.Disconnected, AppSessionState.Faulted],
            [AppSessionState.SharedMemoryReady] = [AppSessionState.TrackAndVehicleReady, AppSessionState.Disconnected, AppSessionState.Faulted],
            [AppSessionState.TrackAndVehicleReady] = [AppSessionState.ReadyToRecord, AppSessionState.Disconnected, AppSessionState.Faulted],
            [AppSessionState.ReadyToRecord] = [AppSessionState.Recording, AppSessionState.Disconnected, AppSessionState.Faulted],
            [AppSessionState.Recording] = [AppSessionState.Finalizing, AppSessionState.Faulted],
            [AppSessionState.Finalizing] = [AppSessionState.ReviewPending, AppSessionState.Faulted],
            [AppSessionState.ReviewPending] = [AppSessionState.Saved, AppSessionState.Exported, AppSessionState.Discarded, AppSessionState.Faulted],
            [AppSessionState.Saved] = [AppSessionState.ReadyToRecord, AppSessionState.Disconnected],
            [AppSessionState.Exported] = [AppSessionState.ReadyToRecord, AppSessionState.Disconnected],
            [AppSessionState.Discarded] = [AppSessionState.ReadyToRecord, AppSessionState.Disconnected],
            [AppSessionState.Faulted] = [AppSessionState.Disconnected, AppSessionState.ReadyToRecord]
        };

    public AppSessionState State { get; private set; } = AppSessionState.Disconnected;
    public string? LastFault { get; private set; }

    public bool CanTransitionTo(AppSessionState next) =>
        Allowed.TryGetValue(State, out var targets) && targets.Contains(next);

    public void TransitionTo(AppSessionState next, string? diagnostic = null)
    {
        if (!CanTransitionTo(next))
        {
            throw new InvalidOperationException($"Invalid ApexTrace state transition: {State} -> {next}.");
        }

        State = next;
        LastFault = next == AppSessionState.Faulted ? diagnostic ?? "Unknown fault" : null;
    }
}
