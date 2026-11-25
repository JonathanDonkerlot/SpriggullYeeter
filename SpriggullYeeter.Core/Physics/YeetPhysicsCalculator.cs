using SpriggullYeeter.Core.Math;

namespace SpriggullYeeter.Core.Physics
{
    /// <summary>
    /// Pure physics math for throw velocity combination, RagdollLib impulse scaling, and impact bowling damage.
    /// </summary>
    public static class YeetPhysicsCalculator
    {
        public const float MinLaunchSpeedMetersPerSec = 1.35f;
        public const float DefaultMaxThrowSpeed = 28.0f;
        public const float DefaultEffectiveMassKg = 22.0f;
        public const float DefaultUpwardAssistRatio = 0.12f;
        public const float DefaultMaxLaunchImpulse = 650.0f;
        public const float DefaultMaxImpactDamage = 80.0f;

        /// <summary>
        /// Combines left and right hand release velocities and applies <paramref name="yeetForceMultiplier"/>.
        /// </summary>
        public static Vec3 ComputeThrowVelocity(
            Vec3 leftVel,
            bool hasLeft,
            Vec3 rightVel,
            bool hasRight,
            float yeetForceMultiplier,
            float maxThrowSpeed = DefaultMaxThrowSpeed)
        {
            Vec3 combined;
            if (hasLeft && hasRight)
            {
                float leftMag = leftVel.Magnitude;
                float rightMag = rightVel.Magnitude;
                if (leftMag < 0.25f && rightMag >= 0.5f)
                    combined = rightVel;
                else if (rightMag < 0.25f && leftMag >= 0.5f)
                    combined = leftVel;
                else
                    combined = (leftVel + rightVel) * 0.5f;
            }
            else if (hasLeft)
            {
                combined = leftVel;
            }
            else if (hasRight)
            {
                combined = rightVel;
            }
            else
            {
                return Vec3.Zero;
            }

            if (!combined.IsFinite)
                return Vec3.Zero;

            Vec3 scaled = combined * yeetForceMultiplier;
            return Vec3.ClampMagnitude(scaled, maxThrowSpeed);
        }

        /// <summary>
        /// Returns true when the throw velocity exceeds the minimum launch threshold (otherwise it is a gentle drop).
        /// </summary>
        public static bool ShouldLaunchAsYeet(Vec3 throwVelocity)
        {
            return throwVelocity.IsFinite && throwVelocity.Magnitude >= MinLaunchSpeedMetersPerSec;
        }

        /// <summary>
        /// Converts throw velocity into a RagdollLib world-space impulse vector (N*s) with gentle upward arc assist.
        /// </summary>
        public static Vec3 ComputeLaunchImpulse(
            Vec3 throwVelocity,
            float effectiveMassKg = DefaultEffectiveMassKg,
            float upwardAssistRatio = DefaultUpwardAssistRatio,
            float maxImpulse = DefaultMaxLaunchImpulse)
        {
            if (!throwVelocity.IsFinite)
                return Vec3.Zero;

            float speed = throwVelocity.Magnitude;
            if (speed < 1e-4f)
                return Vec3.Zero;

            Vec3 assistedVel = new Vec3(
                throwVelocity.X,
                throwVelocity.Y + (speed * upwardAssistRatio),
                throwVelocity.Z);

            Vec3 impulse = assistedVel * effectiveMassKg;
            return Vec3.ClampMagnitude(impulse, maxImpulse);
        }

        /// <summary>
        /// Computes velocity-scaled Spriggull bowling damage for collisions exceeding <paramref name="minImpactSpeed"/>.
        /// </summary>
        public static float ComputeImpactDamage(
            float impactSpeed,
            float minImpactSpeed,
            float damageMultiplier,
            float maxDamage = DefaultMaxImpactDamage)
        {
            if (float.IsNaN(impactSpeed) || float.IsInfinity(impactSpeed) || impactSpeed < minImpactSpeed)
                return 0f;

            if (float.IsNaN(damageMultiplier) || float.IsInfinity(damageMultiplier) || damageMultiplier <= 0f)
                return 0f;

            float excess = impactSpeed - minImpactSpeed;
            float baseDamage = (5.0f + (excess * 3.2f) + (excess * excess * 0.18f)) * damageMultiplier;
            if (baseDamage > maxDamage)
                return maxDamage;

            return baseDamage < 0f ? 0f : baseDamage;
        }
    }
}
