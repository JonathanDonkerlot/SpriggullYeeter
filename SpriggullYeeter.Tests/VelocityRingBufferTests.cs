using SpriggullYeeter.Core.Math;
using SpriggullYeeter.Core.Physics;
using Xunit;

namespace SpriggullYeeter.Tests
{
    public sealed class VelocityRingBufferTests
    {
        [Fact]
        public void ComputeReleaseVelocity_SteadyMotion_ReturnsExpectedVelocity()
        {
            var buffer = new VelocityRingBuffer();
            double t = 1.000d;

            // 8 frames at 90Hz moving along +Z at 5 m/s
            for (int i = 0; i < 8; i++)
            {
                double dt = i * 0.011d;
                buffer.Record(t + dt, new Vec3(0f, 1f, (float)(5.0d * dt)));
            }

            Vec3 vel = buffer.ComputeReleaseVelocity(t + (7 * 0.011d));
            Assert.InRange(vel.Z, 4.8f, 5.2f);
            Assert.InRange(System.Math.Abs(vel.X), 0f, 0.01f);
            Assert.InRange(System.Math.Abs(vel.Y), 0f, 0.01f);
        }

        [Fact]
        public void ComputeReleaseVelocity_FingerReleaseSlowdown_PreservesPeakThrowVelocity()
        {
            var buffer = new VelocityRingBuffer();
            double t = 10.0d;

            // Fast whip followed by 2 slow frames while grip fingers relax
            buffer.Record(t + 0.000d, new Vec3(0f, 1.0f, 0.00f));
            buffer.Record(t + 0.011d, new Vec3(0f, 1.0f, 0.08f));
            buffer.Record(t + 0.022d, new Vec3(0f, 1.0f, 0.19f)); // 10 m/s peak
            buffer.Record(t + 0.033d, new Vec3(0f, 1.0f, 0.30f)); // 10 m/s peak
            buffer.Record(t + 0.044d, new Vec3(0f, 1.0f, 0.31f)); // finger release stall
            buffer.Record(t + 0.055d, new Vec3(0f, 1.0f, 0.31f)); // finger release stall

            Vec3 vel = buffer.ComputeReleaseVelocity(t + 0.055d);
            Assert.True(vel.Z >= 7.5f, $"Expected preserved flick velocity >= 7.5 m/s, got {vel.Z:F2}");
        }

        [Fact]
        public void Record_TeleportJump_ResetsBufferInsteadOfSpiking()
        {
            var buffer = new VelocityRingBuffer();
            buffer.Record(1.00d, new Vec3(0f, 0f, 0f));
            buffer.Record(1.01d, new Vec3(0f, 0f, 0.05f));
            // Sudden 5m teleport jump in 10ms
            buffer.Record(1.02d, new Vec3(0f, 0f, 5.05f));

            Assert.Equal(1, buffer.Count);
            Vec3 vel = buffer.ComputeReleaseVelocity(1.02d);
            Assert.Equal(0f, vel.Magnitude);
        }

        [Fact]
        public void Record_NonFiniteSample_IsIgnored()
        {
            var buffer = new VelocityRingBuffer();
            buffer.Record(1.00d, new Vec3(0f, 0f, 0f));
            buffer.Record(1.01d, new Vec3(float.NaN, 0f, 1f));
            buffer.Record(1.02d, new Vec3(0f, 0f, float.PositiveInfinity));

            Assert.Equal(1, buffer.Count);
        }
    }
}
