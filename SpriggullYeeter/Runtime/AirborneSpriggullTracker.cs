using System;
using System.Collections.Generic;
using Alta.Character;
using Alta.Impact;
using Alta.Networking;
using MelonLoader;
using RagdollLib.API;
using SpriggullYeeter.Core.Config;
using SpriggullYeeter.Core.Physics;
using SpriggullYeeter.Feedback;
using UnityEngine;

namespace SpriggullYeeter.Runtime
{
    /// <summary>
    /// Monitors a thrown Spriggull during RagdollLib airborne flight, attaches collision relays
    /// across all active ragdoll bones (including detached pelvis), applies velocity-scaled
    /// Spriggull-bowling impact damage, and cleans up cleanly upon recovery or despawn.
    /// </summary>
    internal sealed class AirborneSpriggullTracker
    {
        private const float ThrowerGraceSeconds = 0.25f;
        private const float SelfImpactCooldownSeconds = 0.35f;
        private const float BowlingTargetCooldownSeconds = 0.35f;

        private readonly GameObject _spriggullRoot;
        private readonly PlayerController _thrower;
        private readonly HealthObject _spriggullHealth;
        private readonly ImpactDetector _spriggullDetector;
        private readonly YeetConfigSnapshot _config;
        private readonly MelonLogger.Instance _log;

        private readonly HashSet<int> _selfTransformIds = new HashSet<int>();
        private readonly List<RagdollBoneCollisionRelay> _relays = new List<RagdollBoneCollisionRelay>();
        private readonly Dictionary<int, float> _targetCooldowns = new Dictionary<int, float>();
        private readonly float _launchTime;

        private float _lastSelfImpactTime = -10f;
        private bool _cleanedUp;

        public int RootInstanceId { get; }
        public GameObject SpriggullRoot => _spriggullRoot;
        public bool IsCleanedUp => _cleanedUp;

        public AirborneSpriggullTracker(
            GameObject spriggullRoot,
            PlayerController thrower,
            YeetConfigSnapshot config,
            MelonLogger.Instance log)
        {
            _spriggullRoot = spriggullRoot;
            RootInstanceId = spriggullRoot.GetInstanceID();
            _thrower = thrower;
            _config = config;
            _log = log;
            _launchTime = Time.unscaledTime;

            _spriggullHealth = spriggullRoot.GetComponentInChildren<HealthObject>(true);
            _spriggullDetector = spriggullRoot.GetComponentInChildren<ImpactDetector>(true);

            AttachBoneRelays();
        }

        public bool ShouldCompleteCleanup()
        {
            if (_cleanedUp)
                return true;

            if (_spriggullRoot == null || !_spriggullRoot.activeInHierarchy)
                return true;

            // Give RagdollLib at least one frame after TriggerRagdoll before polling IsRagdolled
            if ((Time.unscaledTime - _launchTime) > 0.15f && !RagdollAPI.IsRagdolled(_spriggullRoot.transform))
                return true;

            return false;
        }

        public void HandleBoneCollision(RagdollBoneCollisionRelay relay, Collision collision)
        {
            if (_cleanedUp || _spriggullRoot == null || collision == null)
                return;

            try
            {
                Collider otherCol = collision.collider;
                if (otherCol == null)
                    return;

                if (IsSelfCollider(otherCol))
                    return;

                if ((Time.unscaledTime - _launchTime) < ThrowerGraceSeconds && IsThrowerCollider(otherCol))
                    return;

                float impactSpeed = collision.relativeVelocity.magnitude;
                if (impactSpeed < _config.MinImpactDamageSpeed)
                    return;

                Vector3 hitPoint = collision.contactCount > 0
                    ? collision.GetContact(0).point
                    : relay.transform.position;

                Vector3 hitNormal = collision.contactCount > 0
                    ? collision.GetContact(0).normal
                    : Vector3.up;

                Vector3 impactDir = -collision.relativeVelocity;
                if (impactDir.sqrMagnitude < 1e-5f)
                    impactDir = -hitNormal;

                SpriggullFeedback.PlayImpactSound(_spriggullRoot, hitPoint);

                if (!_config.EnableImpactDamage)
                    return;

                float damage = YeetPhysicsCalculator.ComputeImpactDamage(
                    impactSpeed,
                    _config.MinImpactDamageSpeed,
                    _config.ImpactDamageMultiplier);

                if (damage <= 0f)
                    return;

                float now = Time.unscaledTime;

                // 1. Damage the thrown Spriggull itself on hard impact
                if (NetworkSceneManager.IsServer && (now - _lastSelfImpactTime) >= SelfImpactCooldownSeconds)
                {
                    _lastSelfImpactTime = now;
                    ApplyDamage(_spriggullHealth, _spriggullDetector, relay.GetComponent<Collider>(), damage * 0.75f, hitPoint, hitNormal, impactDir);
                }

                // 2. Spriggull bowling: check if we slammed into another creature
                TryBowlIntoTarget(otherCol, impactSpeed, damage, hitPoint, hitNormal, impactDir, now);
            }
            catch (Exception ex)
            {
                _log?.Error($"[SpriggullYeeter] Exception in HandleBoneCollision: {ex}");
            }
        }

        public void Cleanup()
        {
            if (_cleanedUp)
                return;

            _cleanedUp = true;
            for (int i = 0; i < _relays.Count; i++)
            {
                RagdollBoneCollisionRelay relay = _relays[i];
                if (relay != null)
                    UnityEngine.Object.Destroy(relay);
            }
            _relays.Clear();
            _selfTransformIds.Clear();
            _targetCooldowns.Clear();
        }

        private void AttachBoneRelays()
        {
            var seenBodies = new HashSet<int>();

            // Collect all transforms under the Spriggull root
            Transform[] rootChildren = _spriggullRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < rootChildren.Length; i++)
            {
                if (rootChildren[i] != null)
                    _selfTransformIds.Add(rootChildren[i].GetInstanceID());
            }

            // Collect all bones via SkinnedMeshRenderer.bones (essential because RagdollLib detaches the pelvis!)
            SkinnedMeshRenderer[] smrs = _spriggullRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int s = 0; s < smrs.Length; s++)
            {
                SkinnedMeshRenderer smr = smrs[s];
                if (smr == null || smr.bones == null)
                    continue;

                Transform[] bones = smr.bones;
                for (int b = 0; b < bones.Length; b++)
                {
                    Transform bone = bones[b];
                    if (bone == null)
                        continue;

                    _selfTransformIds.Add(bone.GetInstanceID());
                    TryAttachRelayOnTransform(bone.gameObject, seenBodies);
                }
            }

            // Also scan any Rigidbodies still parented under the Spriggull root
            Rigidbody[] childRbs = _spriggullRoot.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < childRbs.Length; i++)
            {
                if (childRbs[i] != null)
                    TryAttachRelayOnTransform(childRbs[i].gameObject, seenBodies);
            }
        }

        private void TryAttachRelayOnTransform(GameObject go, HashSet<int> seenBodies)
        {
            if (go == null)
                return;

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic)
                return;

            if (!seenBodies.Add(rb.GetInstanceID()))
                return;

            RagdollBoneCollisionRelay relay = go.GetComponent<RagdollBoneCollisionRelay>();
            if (relay == null)
                relay = go.AddComponent<RagdollBoneCollisionRelay>();

            relay.Initialize(this);
            _relays.Add(relay);
        }

        private bool IsSelfCollider(Collider col)
        {
            Transform t = col.transform;
            while (t != null)
            {
                if (_selfTransformIds.Contains(t.GetInstanceID()))
                    return true;
                t = t.parent;
            }
            return false;
        }

        private bool IsThrowerCollider(Collider col)
        {
            if (_thrower == null)
                return false;

            if (col.transform.IsChildOf(_thrower.transform))
                return true;

            Pickup heldOrDocked = col.GetComponentInParent<Pickup>();
            if (heldOrDocked != null && heldOrDocked.Dock != null && heldOrDocked.Dock.PlayerController == _thrower)
                return true;

            return false;
        }

        private void TryBowlIntoTarget(
            Collider otherCol,
            float impactSpeed,
            float damage,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 impactDir,
            float now)
        {
            HealthObject targetHealth = otherCol.GetComponentInParent<HealthObject>();
            if (targetHealth == null && otherCol.attachedRigidbody != null)
                targetHealth = otherCol.attachedRigidbody.GetComponent<HealthObject>();

            if (targetHealth == null || targetHealth == _spriggullHealth)
                return;

            if (targetHealth is PlayerHealth || targetHealth.GetComponentInParent<PlayerCharacter>() != null)
                return;

            if (targetHealth.Health <= 0f)
                return;

            int targetId = targetHealth.GetInstanceID();
            if (_targetCooldowns.TryGetValue(targetId, out float nextAllowed) && now < nextAllowed)
                return;

            _targetCooldowns[targetId] = now + BowlingTargetCooldownSeconds;

            ImpactDetector targetDetector = otherCol.GetComponentInParent<ImpactDetector>();
            if (targetDetector == null)
                targetDetector = targetHealth.GetComponent<ImpactDetector>();

            if (NetworkSceneManager.IsServer)
            {
                ApplyDamage(targetHealth, targetDetector, otherCol, damage, hitPoint, -hitNormal, impactDir);
            }

            Vector3 safeDir = impactDir.sqrMagnitude > 1e-5f ? impactDir.normalized : Vector3.forward;
            Vector3 bowlImpulse = (safeDir + (Vector3.up * 0.30f)).normalized * Mathf.Clamp(impactSpeed * 20f, 120f, 360f);
            bool bowled = RagdollAPI.TriggerRagdoll(targetHealth, bowlImpulse, hitPoint, _config.RagdollDurationSeconds);

            if (_config.EnableDebugLogging)
            {
                _log?.Msg(
                    $"[SpriggullYeeter] BOWLING HIT target='{targetHealth.gameObject.name}' " +
                    $"speed={impactSpeed:F2}m/s dmg={damage:F1} ragdoll={bowled}");
            }
        }

        private void ApplyDamage(
            HealthObject targetHealth,
            ImpactDetector targetDetector,
            Collider struckCollider,
            float damage,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 hitDirection)
        {
            if (!NetworkSceneManager.IsServer || targetHealth == null || targetHealth.Health <= 0f || damage <= 0f)
                return;

            Vector3 safeDir = hitDirection.sqrMagnitude > 1e-5f ? hitDirection.normalized : Vector3.forward;
            Vector3 safeNorm = hitNormal.sqrMagnitude > 1e-5f ? hitNormal.normalized : -safeDir;

            // Plain ImpactHit (never ImpactHitWithTool with null tool)
            var hit = new ImpactHit(
                baseHealthDamage: damage,
                baseCrippleDamage: damage * 0.35f,
                multipliers: 1f,
                effectiveness: 1f,
                detector: targetDetector,
                impactType: ImpactType.Blunt,
                point: hitPoint,
                normal: safeNorm,
                direction: safeDir,
                struckCollider: struckCollider,
                wielder: _thrower != null ? _thrower.Entity : null,
                playerWielder: _thrower);

            hit.RelativeVelocity = safeDir * Mathf.Max(1f, _config.MinImpactDamageSpeed);

            if (targetDetector != null && targetDetector.HealthTarget == targetHealth)
                targetDetector.OnImpactHit(hit);
            else
                targetHealth.DetectImpact(hit);

            if (_config.EnableDebugLogging)
            {
                _log?.Msg($"[SpriggullYeeter] Impact damage {damage:F1} applied to '{targetHealth.gameObject.name}' (hp={targetHealth.Health:F1}).");
            }
        }
    }
}
