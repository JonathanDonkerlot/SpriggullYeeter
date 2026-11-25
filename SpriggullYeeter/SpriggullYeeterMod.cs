using System;
using System.IO;
using Alta.Networking.Scripts.Player;
using MelonLoader;
using MelonLoader.Utils;
using SpriggullYeeter.Config;
using SpriggullYeeter.Core.Config;
using SpriggullYeeter.Patches;
using SpriggullYeeter.Runtime;
using UnityEngine;

namespace SpriggullYeeter
{
    /// <summary>
    /// MelonLoader entry point for SpriggullYeeter — two-handed VR Spriggull grabbing and yeeting via RagdollLib.
    /// </summary>
    public sealed class SpriggullYeeterMod : MelonMod
    {
        private const float ReconcileIntervalSeconds = 1.5f;

        private readonly YeetConfig _config = new YeetConfig();
        private YeetManager _manager;
        private float _reconcileTimer;
        private bool _abortedIl2Cpp;

        public override void OnInitializeMelon()
        {
            if (File.Exists(Path.Combine(MelonEnvironment.GameRootDirectory, "GameAssembly.dll")))
            {
                _abortedIl2Cpp = true;
                LoggerInstance.Error("[SpriggullYeeter] IL2CPP GameAssembly.dll detected; this mod targets Mono PCVR only and will remain inactive.");
                return;
            }

            _config.Initialize();
            _manager = new YeetManager(_config, LoggerInstance);
            _manager.Initialize();

            HarmonyPatches.Apply(HarmonyInstance, LoggerInstance);

            try
            {
                Player.OnPlayerRemoved += OnPlayerRemoved;
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[SpriggullYeeter] Could not subscribe to Player.OnPlayerRemoved: {ex.Message}");
            }

            YeetConfigSnapshot snap = _config.Snapshot;
            LoggerInstance.Msg(
                $"[SpriggullYeeter] v1.0.0 Initialized (RagdollLib Integration) | " +
                $"RequireTwoHands={snap.RequireTwoHands}, GrabRadius={snap.GrabRadius:F2}m, " +
                $"MaxSep={snap.MaxHandSeparation:F2}m, YeetMult={snap.YeetForceMultiplier:F2}x, " +
                $"RagdollDuration={snap.RagdollDurationSeconds:F1}s, ImpactDmg={snap.EnableImpactDamage}");
        }

        public override void OnPreferencesLoaded()
        {
            if (_abortedIl2Cpp)
                return;

            _config.Refresh();
        }

        public override void OnUpdate()
        {
            if (_abortedIl2Cpp || _manager == null)
                return;

            _reconcileTimer += Time.unscaledDeltaTime;
            if (_reconcileTimer >= ReconcileIntervalSeconds)
            {
                _reconcileTimer = 0f;
                _manager.ReconcilePlayers();
            }

            _manager.TickAll();
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (_abortedIl2Cpp)
                return;

            _reconcileTimer = 0f;
            _manager?.ClearAll();
        }

        public override void OnDeinitializeMelon()
        {
            if (_abortedIl2Cpp)
                return;

            try
            {
                Player.OnPlayerRemoved -= OnPlayerRemoved;
            }
            catch
            {
                // Ignore during shutdown.
            }

            _manager?.Shutdown();

            try
            {
                HarmonyInstance?.UnpatchSelf();
            }
            catch
            {
                // Ignore during shutdown.
            }
        }

        private void OnPlayerRemoved(Player player)
        {
            _manager?.UnregisterPlayer(player);
        }
    }
}
