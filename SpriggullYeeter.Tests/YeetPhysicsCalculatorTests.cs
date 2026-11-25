using SpriggullYeeter.Core.Math;
using SpriggullYeeter.Core.Physics;
using Xunit;

namespace SpriggullYeeter.Tests
{
    public sealed class YeetPhysicsCalculatorTests
    {
        [Fact]
        public void ComputeThrowVelocity_TwoHands_ScalesAndClamps()
        {
            Vec3 left = new Vec3(0f, 1f, 6f);
            Vec3 right = new Vec3(0f, 1f, 8f);

            Vec3 result = YeetPhysicsCalculator.ComputeThrowVelocity(
                left, hasLeft: true,
                right, hasRight: true,
                yeetForceMultiplier: 1.6f);

            // Average = (0, 1, 7), * 1.6 = (0, 1.6, 11.2)
            Assert.InRange(result.Y, 1.59f, 1.61f);
            Assert.InRange(result.Z, 11.19f, 11.21f);
        }

        [Fact]
        public void ComputeThrowVelocity_SingleHandActive_IgnoresIdleOtherHand()
        {
            Vec3 leftIdle = new Vec3(0f, 0f, 0.05f);
            Vec3 rightWhip = new Vec3(0f, 1f, 6f);

            Vec3 result = YeetPhysicsCalculator.ComputeThrowVelocity(
                leftIdle, hasLeft: true,
                rightWhip, hasRight: true,
                yeetForceMultiplier: 1.5f);

            Assert.InRange(result.Z, 8.9f, 9.1f);
        }

        [Fact]
        public void ComputeLaunchImpulse_AddsUpwardAssistAndCaps()
        {
            Vec3 throwVel = new Vec3(0f, 0f, 10f);
            Vec3 impulse = YeetPhysicsCalculator.ComputeLaunchImpulse(throwVel);

            Assert.True(impulse.Y > 0f);
            Assert.True(impulse.Z > 0f);
            Assert.True(impulse.Magnitude <= YeetPhysicsCalculator.DefaultMaxLaunchImpulse + 1e-3f);
        }

        [Theory]
        [InlineData(2.0f, 4.5f, 1.0f, 0.0f)]
        [InlineData(4.49f, 4.5f, 1.0f, 0.0f)]
        [InlineData(6.0f, 4.5f, 1.0f, 10.205f)]
        public void ComputeImpactDamage_RespectsThresholdAndScales(
            float speed,
            float minSpeed,
            float mult,
            float expectedApprox)
        {
            float dmg = YeetPhysicsCalculator.ComputeImpactDamage(speed, minSpeed, mult);
            if (expectedApprox <= 0f)
                Assert.Equal(0f, dmg);
            else
                Assert.InRange(dmg, expectedApprox - 0.1f, expectedApprox + 0.1f);
        }
    }
}
