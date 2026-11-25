namespace SpriggullYeeter.Core.Config
{
    /// <summary>
    /// Immutable runtime snapshot of SpriggullYeeter preferences with defensive bounds clamping.
    /// </summary>
    public sealed class YeetConfigSnapshot
    {
        public bool RequireTwoHands { get; }
        public float GrabRadius { get; }
        public float MaxHandSeparation { get; }
        public float YeetForceMultiplier { get; }
        public float RagdollDurationSeconds { get; }
        public bool EnableImpactDamage { get; }
        public float MinImpactDamageSpeed { get; }
        public float ImpactDamageMultiplier { get; }
        public bool EnableDebugLogging { get; }

        public YeetConfigSnapshot(
            bool requireTwoHands = true,
            float grabRadius = 0.40f,
            float maxHandSeparation = 1.10f,
            float yeetForceMultiplier = 1.60f,
            float ragdollDurationSeconds = 3.50f,
            bool enableImpactDamage = true,
            float minImpactDamageSpeed = 4.50f,
            float impactDamageMultiplier = 1.00f,
            bool enableDebugLogging = false)
        {
            RequireTwoHands = requireTwoHands;
            GrabRadius = Clamp(grabRadius, 0.15f, 1.50f);
            MaxHandSeparation = Clamp(maxHandSeparation, 0.35f, 2.50f);
            YeetForceMultiplier = Clamp(yeetForceMultiplier, 0.25f, 6.00f);
            RagdollDurationSeconds = Clamp(ragdollDurationSeconds, 0.50f, 20.00f);
            EnableImpactDamage = enableImpactDamage;
            MinImpactDamageSpeed = Clamp(minImpactDamageSpeed, 1.50f, 30.00f);
            ImpactDamageMultiplier = Clamp(impactDamageMultiplier, 0.05f, 10.00f);
            EnableDebugLogging = enableDebugLogging;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
