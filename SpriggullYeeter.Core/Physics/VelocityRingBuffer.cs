using SpriggullYeeter.Core.Math;

namespace SpriggullYeeter.Core.Physics
{
    /// <summary>
    /// Fixed-capacity rolling ring buffer (~80ms time window) for VR hand positions.
    /// Captures peak flick velocity cleanly so grip-finger release deceleration does not kill throws.
    /// </summary>
    public sealed class VelocityRingBuffer
    {
        public const int DefaultCapacity = 16;
        public const double DefaultWindowSeconds = 0.080d;
        public const float DefaultMaxTeleportStepMeters = 1.50f;

        private struct Sample
        {
            public double Timestamp;
            public Vec3 Position;
        }

        private readonly Sample[] _samples;
        private readonly double _windowSeconds;
        private readonly float _maxTeleportStepSqr;

        private int _head;
        private int _count;

        public VelocityRingBuffer(
            int capacity = DefaultCapacity,
            double windowSeconds = DefaultWindowSeconds,
            float maxTeleportStepMeters = DefaultMaxTeleportStepMeters)
        {
            int safeCapacity = capacity < 4 ? 4 : capacity;
            _samples = new Sample[safeCapacity];
            _windowSeconds = windowSeconds > 0.01d ? windowSeconds : DefaultWindowSeconds;
            float safeMaxStep = maxTeleportStepMeters > 0.1f ? maxTeleportStepMeters : DefaultMaxTeleportStepMeters;
            _maxTeleportStepSqr = safeMaxStep * safeMaxStep;
        }

        public int Count => _count;

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// Records a world-space hand sample. Non-finite or single-frame teleport jumps reset the buffer.
        /// </summary>
        public void Record(double timestampSeconds, Vec3 position)
        {
            if (!position.IsFinite || double.IsNaN(timestampSeconds) || double.IsInfinity(timestampSeconds))
                return;

            if (_count > 0)
            {
                Sample latest = GetNewest();
                if (timestampSeconds <= latest.Timestamp)
                    return;

                float distSqr = (position - latest.Position).SqrMagnitude;
                if (distSqr > _maxTeleportStepSqr)
                {
                    Clear();
                }
            }

            _samples[_head] = new Sample
            {
                Timestamp = timestampSeconds,
                Position = position
            };

            _head = (_head + 1) % _samples.Length;
            if (_count < _samples.Length)
                _count++;
        }

        /// <summary>
        /// Computes release velocity over the recent ~80ms window, blending peak segment flick
        /// velocity with window-average velocity to prevent finger-release slowdown artifacts.
        /// </summary>
        public Vec3 ComputeReleaseVelocity(double nowSeconds)
        {
            if (_count < 2)
                return Vec3.Zero;

            double cutoff = nowSeconds - _windowSeconds;

            Sample oldestInWindow = default;
            bool hasOldest = false;

            Vec3 peakSegVel = Vec3.Zero;
            float peakSegSpeedSqr = 0f;

            Sample prev = default;
            bool hasPrev = false;

            for (int i = 0; i < _count; i++)
            {
                Sample current = GetChronological(i);
                if (current.Timestamp < cutoff)
                {
                    prev = current;
                    hasPrev = true;
                    continue;
                }

                if (!hasOldest)
                {
                    oldestInWindow = hasPrev && (current.Timestamp - prev.Timestamp) <= _windowSeconds
                        ? prev
                        : current;
                    hasOldest = true;
                }

                if (hasPrev)
                {
                    double dt = current.Timestamp - prev.Timestamp;
                    if (dt >= 0.001d && dt <= _windowSeconds * 1.5d)
                    {
                        Vec3 segVel = (current.Position - prev.Position) / (float)dt;
                        float segSpeedSqr = segVel.SqrMagnitude;
                        if (segSpeedSqr > peakSegSpeedSqr)
                        {
                            peakSegSpeedSqr = segSpeedSqr;
                            peakSegVel = segVel;
                        }
                    }
                }

                prev = current;
                hasPrev = true;
            }

            if (!hasOldest || !hasPrev)
                return Vec3.Zero;

            double totalDt = prev.Timestamp - oldestInWindow.Timestamp;
            Vec3 avgVel = totalDt >= 0.004d
                ? (prev.Position - oldestInWindow.Position) / (float)totalDt
                : peakSegVel;

            if (peakSegSpeedSqr <= 1e-6f)
                return avgVel;

            if (avgVel.SqrMagnitude <= 1e-6f)
                return peakSegVel * 0.75f;

            float alignment = Vec3.Dot(peakSegVel.Normalized, avgVel.Normalized);
            if (alignment < 0.25f)
            {
                // Sharp late deceleration or micro-rebound on release: trust peak flick direction & magnitude.
                return peakSegVel * 0.85f;
            }

            return (peakSegVel * 0.65f) + (avgVel * 0.35f);
        }

        private Sample GetNewest()
        {
            int idx = (_head - 1 + _samples.Length) % _samples.Length;
            return _samples[idx];
        }

        private Sample GetChronological(int indexFromOldest)
        {
            int oldestIdx = (_head - _count + _samples.Length) % _samples.Length;
            int idx = (oldestIdx + indexFromOldest) % _samples.Length;
            return _samples[idx];
        }
    }
}
