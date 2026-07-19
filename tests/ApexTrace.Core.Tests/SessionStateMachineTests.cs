using ApexTrace.Core;

namespace ApexTrace.Core.Tests;

public sealed class SessionStateMachineTests
{
    [Fact]
    public void RecordingLifecycle_ReachesSavedOnlyThroughReview()
    {
        var machine = new SessionStateMachine();
        foreach (var state in new[]
        {
            AppSessionState.GameDetected, AppSessionState.SharedMemoryReady,
            AppSessionState.TrackAndVehicleReady, AppSessionState.ReadyToRecord,
            AppSessionState.Recording, AppSessionState.Finalizing,
            AppSessionState.ReviewPending, AppSessionState.Saved
        }) machine.TransitionTo(state);

        Assert.Equal(AppSessionState.Saved, machine.State);
    }

    [Fact]
    public void Recording_CannotSkipFinalization()
    {
        var machine = new SessionStateMachine();
        machine.TransitionTo(AppSessionState.GameDetected);
        machine.TransitionTo(AppSessionState.SharedMemoryReady);
        machine.TransitionTo(AppSessionState.TrackAndVehicleReady);
        machine.TransitionTo(AppSessionState.ReadyToRecord);
        machine.TransitionTo(AppSessionState.Recording);

        Assert.Throws<InvalidOperationException>(() => machine.TransitionTo(AppSessionState.Saved));
    }
}
