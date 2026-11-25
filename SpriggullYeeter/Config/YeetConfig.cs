using MelonLoader;
using SpriggullYeeter.Core.Config;

namespace SpriggullYeeter.Config
{
    /// <summary>
    /// MelonPreferences configuration wrapper generating [SpriggullYeeter] settings on first launch.
    /// </summary>
    public sealed class YeetConfig
    {
        private MelonPreferences_Category _category;
        private MelonPreferences_Entry<bool> _requireTwoHands;
        private MelonPreferences_Entry<float> _grabRadius;
        private MelonPreferences_Entry<float> _maxHandSeparation;
        private MelonPreferences_Entry<float> _yeetForceMultiplier;
        private MelonPreferences_Entry<float> _ragdollDurationSeconds;
        private MelonPreferences_Entry<bool> _enableImpactDamage;
        private MelonPreferences_Entry<float> _minImpactDamageSpeed;
        private MelonPreferences_Entry<float> _impactDamageMultiplier;
        private MelonPreferences_Entry<bool> _enableDebugLogging;

        public YeetConfigSnapshot Snapshot { get; private set; } = new YeetConfigSnapshot();

        public void Initialize()
        {
            _category = MelonPreferences.CreateCategory("SpriggullYeeter", "SpriggullYeeter");

            _requireTwoHands = _category.CreateEntry(
                "RequireTwoHands",
                true,
                description: "Require both VR hands gripping simultaneously to grab a Spriggull (false allows single-hand grab).");

            _grabRadius = _category.CreateEntry(
                "GrabRadius",
                0.40f,
                description: "Maximum distance (meters) from hand(s) to a live Spriggull to initiate a grab.");

            _maxHandSeparation = _category.CreateEntry(
                "MaxHandSeparation",
                1.10f,
                description: "Maximum distance (meters) between left and right hands before auto-releasing a held Spriggull.");

            _yeetForceMultiplier = _category.CreateEntry(
                "YeetForceMultiplier",
                1.60f,
                description: "Throw velocity multiplier applied when launching a Spriggull into RagdollLib flight.");

            _ragdollDurationSeconds = _category.CreateEntry(
                "RagdollDurationSeconds",
                3.50f,
                description: "Duration (seconds) a thrown Spriggull remains ragdolled before recovering if it survives.");

            _enableImpactDamage = _category.CreateEntry(
                "EnableImpactDamage",
                true,
                description: "Apply velocity-scaled impact damage to thrown Spriggulls and creatures they bowl into.");

            _minImpactDamageSpeed = _category.CreateEntry(
                "MinImpactDamageSpeed",
                4.50f,
                description: "Minimum collision speed (m/s) required during ragdoll flight to inflict impact damage.");

            _impactDamageMultiplier = _category.CreateEntry(
                "ImpactDamageMultiplier",
                1.00f,
                description: "Scalar applied to velocity-scaled Spriggull bowling impact damage.");

            _enableDebugLogging = _category.CreateEntry(
                "EnableDebugLogging",
                false,
                description: "Enable verbose [SpriggullYeeter] diagnostic logs.");

            _category.SaveToFile(false);
            Refresh();
        }

        public void Refresh()
        {
            if (_category == null)
                return;

            Snapshot = new YeetConfigSnapshot(
                requireTwoHands: _requireTwoHands.Value,
                grabRadius: _grabRadius.Value,
                maxHandSeparation: _maxHandSeparation.Value,
                yeetForceMultiplier: _yeetForceMultiplier.Value,
                ragdollDurationSeconds: _ragdollDurationSeconds.Value,
                enableImpactDamage: _enableImpactDamage.Value,
                minImpactDamageSpeed: _minImpactDamageSpeed.Value,
                impactDamageMultiplier: _impactDamageMultiplier.Value,
                enableDebugLogging: _enableDebugLogging.Value);
        }
    }
}
