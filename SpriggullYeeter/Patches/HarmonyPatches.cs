using System;
using Alta.Networking;
using HarmonyLib;
using MelonLoader;
using SpriggullYeeter.Runtime;

namespace SpriggullYeeter.Patches
{
    /// <summary>
    /// Defensive Harmony patches ensuring held or airborne Spriggulls are cleaned up cleanly
    /// if killed mid-grab, pooled, or unloaded from a chunk/scene.
    /// </summary>
    internal static class HarmonyPatches
    {
        public static void Apply(HarmonyLib.Harmony harmony, MelonLogger.Instance log)
        {
            try
            {
                harmony?.PatchAll(typeof(HarmonyPatches).Assembly);
            }
            catch (Exception ex)
            {
                log?.Error($"[SpriggullYeeter] Failed to apply Harmony patches: {ex}");
            }
        }

        [HarmonyPatch(typeof(HealthObject), nameof(HealthObject.OnKilled))]
        private static class HealthObjectOnKilledPatch
        {
            private static void Postfix(HealthObject __instance)
            {
                try
                {
                    YeetManager.Instance?.HandleEntityKilled(__instance);
                }
                catch (Exception ex)
                {
                    YeetManager.Instance?.LogError($"[SpriggullYeeter] Error in HealthObject.OnKilled postfix: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(NetworkEntity), nameof(NetworkEntity.OnDestroyedByScene), new[] { typeof(bool) })]
        private static class NetworkEntityDestroyedByScenePatch
        {
            private static void Prefix(NetworkEntity __instance)
            {
                try
                {
                    YeetManager.Instance?.HandleEntityDespawn(__instance);
                }
                catch (Exception ex)
                {
                    YeetManager.Instance?.LogError($"[SpriggullYeeter] Error in NetworkEntity.OnDestroyedByScene prefix: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(NetworkEntity), "OnDestroy")]
        private static class NetworkEntityOnDestroyPatch
        {
            private static void Prefix(NetworkEntity __instance)
            {
                try
                {
                    YeetManager.Instance?.HandleEntityDespawn(__instance);
                }
                catch (Exception ex)
                {
                    YeetManager.Instance?.LogError($"[SpriggullYeeter] Error in NetworkEntity.OnDestroy prefix: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(PooledObject), "OnReleased")]
        private static class PooledObjectOnReleasedPatch
        {
            private static void Prefix(PooledObject __instance)
            {
                try
                {
                    YeetManager.Instance?.HandleEntityDespawn(__instance);
                }
                catch (Exception ex)
                {
                    YeetManager.Instance?.LogError($"[SpriggullYeeter] Error in PooledObject.OnReleased prefix: {ex}");
                }
            }
        }
    }
}
