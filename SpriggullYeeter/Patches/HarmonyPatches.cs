using System;
using Alta.Intelligence;
using Alta.Networking;
using Alta.Pathfinding;
using HarmonyLib;
using MelonLoader;
using SpriggullYeeter.Runtime;

namespace SpriggullYeeter.Patches
{
    /// <summary>
    /// Defensive Harmony patches ensuring held Spriggulls are completely immobilized (AI, pathfinding,
    /// flail, and NetworkRigidbody client sync blocked) and cleaned up cleanly on kill/despawn/pool release.
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

        [HarmonyPatch(typeof(NetworkRigidbody), "ClientUpdate")]
        private static class NetworkRigidbodyClientUpdatePatch
        {
            private static bool Prefix(NetworkRigidbody __instance)
            {
                try
                {
                    return YeetManager.Instance == null || !YeetManager.Instance.IsComponentEntityHeld(__instance);
                }
                catch
                {
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(NetworkRigidbody), "ApplySerializedValues", new[] { typeof(bool) })]
        private static class NetworkRigidbodyApplySerializedValuesPatch
        {
            private static bool Prefix(NetworkRigidbody __instance)
            {
                try
                {
                    return YeetManager.Instance == null || !YeetManager.Instance.IsComponentEntityHeld(__instance);
                }
                catch
                {
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(Walker), "FixedUpdate")]
        private static class WalkerFixedUpdatePatch
        {
            private static bool Prefix(Walker __instance)
            {
                try
                {
                    return YeetManager.Instance == null || !YeetManager.Instance.IsComponentEntityHeld(__instance);
                }
                catch
                {
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(Agent), "ChangeAction")]
        private static class AgentChangeActionPatch
        {
            private static bool Prefix(Agent __instance, ref bool __result)
            {
                try
                {
                    if (YeetManager.Instance != null && YeetManager.Instance.IsComponentEntityHeld(__instance))
                    {
                        __result = false;
                        return false;
                    }
                }
                catch
                {
                    // Allow normal execution on unexpected failure.
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Spriggull), nameof(Spriggull.BeginFlail), new[] { typeof(float) })]
        private static class SpriggullBeginFlailPatch
        {
            private static bool Prefix(Spriggull __instance)
            {
                try
                {
                    return YeetManager.Instance == null || !YeetManager.Instance.IsComponentEntityHeld(__instance);
                }
                catch
                {
                    return true;
                }
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
