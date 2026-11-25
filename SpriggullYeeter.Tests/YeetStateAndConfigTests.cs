using SpriggullYeeter.Core.Config;
using SpriggullYeeter.Core.State;
using Xunit;

namespace SpriggullYeeter.Tests
{
    public sealed class YeetStateAndConfigTests
    {
        [Fact]
        public void StateMachine_NormalThrowAndRecovery_TransitionsCleanly()
        {
            var sm = new YeetStateMachine();
            Assert.Equal(YeetState.Idle, sm.State);

            Assert.True(sm.TryBeginHold(1.0));
            Assert.Equal(YeetState.Held, sm.State);
            Assert.False(sm.TryBeginHold(1.1));

            Assert.True(sm.TryLaunch(2.0));
            Assert.Equal(YeetState.Airborne, sm.State);

            Assert.True(sm.TryBeginRecovery(5.5));
            Assert.Equal(YeetState.Recovering, sm.State);

            sm.CompleteRecovery(5.6);
            Assert.Equal(YeetState.Idle, sm.State);
        }

        [Fact]
        public void StateMachine_AbortHold_ReturnsToIdle()
        {
            var sm = new YeetStateMachine();
            Assert.False(sm.TryAbortHold(1.0));
            Assert.True(sm.TryBeginHold(1.0));
            Assert.True(sm.TryAbortHold(1.5));
            Assert.Equal(YeetState.Idle, sm.State);
        }

        [Fact]
        public void ConfigSnapshot_ClampsOutOfRangeValues()
        {
            var snap = new YeetConfigSnapshot(
                requireTwoHands: false,
                grabRadius: -5f,
                maxHandSeparation: 99f,
                yeetForceMultiplier: float.NaN,
                ragdollDurationSeconds: 100f,
                enableImpactDamage: true,
                minImpactDamageSpeed: 0.1f,
                impactDamageMultiplier: 99f,
                enableDebugLogging: true);

            Assert.False(snap.RequireTwoHands);
            Assert.Equal(0.15f, snap.GrabRadius);
            Assert.Equal(2.50f, snap.MaxHandSeparation);
            Assert.Equal(0.25f, snap.YeetForceMultiplier);
            Assert.Equal(20.00f, snap.RagdollDurationSeconds);
            Assert.Equal(1.50f, snap.MinImpactDamageSpeed);
            Assert.Equal(10.00f, snap.ImpactDamageMultiplier);
            Assert.True(snap.EnableDebugLogging);
        }
    }
}
