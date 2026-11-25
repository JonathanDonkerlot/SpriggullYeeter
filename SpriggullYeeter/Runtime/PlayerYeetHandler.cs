using System;
using System.Collections.Generic;
using Alta.Character;
using Alta.Intelligence;
using Alta.Networking;
using MelonLoader;
using RagdollLib.API;
using SpriggullYeeter.Core.Config;
using SpriggullYeeter.Core.Math;
using SpriggullYeeter.Core.Physics;
using SpriggullYeeter.Core.State;
using SpriggullYeeter.Feedback;
using SpriggullYeeter.Input;
using UnityEngine;

namespace SpriggullYeeter.Runtime
{
    /// <summary>
    /// Per-player VR grab/throw controller sampling left and right hand ring buffers,
    /// detecting two-handed (or optional single-handed) Spriggull grabs, and launching via RagdollLib.
    /// </summary>
    internal sealed class PlayerYeetHandler
    {
        private const int MaxOverlapColliders = 48;

        private readonly PlayerController _player;
        private readonly YeetManager _manager;
        private readonly MelonLogger.Instance _log;

        private readonly VelocityRingBuffer _leftVelBuffer = new VelocityRingBuffer();
        private readonly VelocityRingBuffer _rightVelBuffer = new VelocityRingBuffer();
        private readonly YeetStateMachine _stateMachine = new YeetStateMachine();
        private readonly Collider[] _overlapBuffer = new Collider[MaxOverlapColliders];
        private readonly HashSet<int> _seenCandidateRoots = new HashSet<int>();

        private HeldSpriggullSession _heldSession;
        private bool _usingLeftForSingle;
        private bool _usingRightForSingle;

        public PlayerController Player => _player;
        public int HeldSpriggullId => _heldSession != null ? _heldSession.RootInstanceId : 0;

        public PlayerYeetHandler(PlayerController player, YeetManager manager, MelonLogger.Instance log)
        {
            _player = player;
            _manager = manager;
            _log = log;
        }

        public void Tick(YeetConfigSnapshot cfg)
        {
            if (_player == null)
                return;

            if (!_player.IsAlive || _player.IsDowned || _player.IsSpirit)
            {
                AbortHoldIfActive();
                _leftVelBuffer.Clear();
                _rightVelBuffer.Clear();
                return;
            }

            Controller leftCtrl = _player.LeftController;
            Controller rightCtrl = _player.RightController;
            Transform leftAnchor = VrHandInputReader.GetHandAnchor(leftCtrl);
            Transform rightAnchor = VrHandInputReader.GetHandAnchor(rightCtrl);

            double now = Time.unscaledTime;

            if (leftAnchor != null)
            {
                Vector3 p = leftAnchor.position;
                _leftVelBuffer.Record(now, new Vec3(p.x, p.y, p.z));
            }
            if (rightAnchor != null)
            {
                Vector3 p = rightAnchor.position;
                _rightVelBuffer.Record(now, new Vec3(p.x, p.y, p.z));
            }

            if (_stateMachine.State == YeetState.Held)
            {
                UpdateHeldState(cfg, leftCtrl, rightCtrl, leftAnchor, rightAnchor, now);
            }
            else
            {
                TryAcquireHold(cfg, leftCtrl, rightCtrl, leftAnchor, rightAnchor, now);
            }
        }

        public void AbortHoldIfActive()
        {
            if (_heldSession != null)
            {
                _heldSession.RestorePreHoldState(resetMotion: true);
                _heldSession = null;
            }

            _usingLeftForSingle = false;
            _usingRightForSingle = false;

            if (_stateMachine.State == YeetState.Held)
                _stateMachine.TryAbortHold(Time.unscaledTime);
        }

        public void AbortIfHoldingEntity(int rootInstanceId)
        {
            if (_heldSession != null && _heldSession.RootInstanceId == rootInstanceId)
            {
                AbortHoldIfActive();
            }
        }

        private void TryAcquireHold(
            YeetConfigSnapshot cfg,
            Controller leftCtrl,
            Controller rightCtrl,
            Transform leftAnchor,
            Transform rightAnchor,
            double now)
        {
            if (cfg.RequireTwoHands)
            {
                if (leftAnchor == null || rightAnchor == null)
                    return;

                if (!VrHandInputReader.IsHandEmpty(leftCtrl) || !VrHandInputReader.IsHandEmpty(rightCtrl))
                    return;

                if (!VrHandInputReader.IsGripPressed(_player, leftCtrl, isLeftHand: true) ||
                    !VrHandInputReader.IsGripPressed(_player, rightCtrl, isLeftHand: false))
                    return;

                float handSep = Vector3.Distance(leftAnchor.position, rightAnchor.position);
                if (handSep > cfg.MaxHandSeparation)
                    return;

                Vector3 midpoint = (leftAnchor.position + rightAnchor.position) * 0.5f;
                GameObject spriggull = FindNearestSpriggull(midpoint, cfg.GrabRadius, leftAnchor.position, rightAnchor.position, requireBothHandsNear: true, out Spriggull agent, out HealthObject health);
                if (spriggull == null)
                    return;

                StartHold(spriggull, agent, health, leftUsed: true, rightUsed: true, now, cfg);
            }
            else
            {
                bool leftReady = leftAnchor != null &&
                                 VrHandInputReader.IsHandEmpty(leftCtrl) &&
                                 VrHandInputReader.IsGripPressed(_player, leftCtrl, isLeftHand: true);

                bool rightReady = rightAnchor != null &&
                                  VrHandInputReader.IsHandEmpty(rightCtrl) &&
                                  VrHandInputReader.IsGripPressed(_player, rightCtrl, isLeftHand: false);

                if (!leftReady && !rightReady)
                    return;

                if (leftReady && rightReady && Vector3.Distance(leftAnchor.position, rightAnchor.position) <= cfg.MaxHandSeparation)
                {
                    Vector3 midpoint = (leftAnchor.position + rightAnchor.position) * 0.5f;
                    GameObject spriggull = FindNearestSpriggull(midpoint, cfg.GrabRadius, leftAnchor.position, rightAnchor.position, requireBothHandsNear: false, out Spriggull agent, out HealthObject health);
                    if (spriggull != null)
                    {
                        StartHold(spriggull, agent, health, leftUsed: true, rightUsed: true, now, cfg);
                        return;
                    }
                }

                if (rightReady)
                {
                    GameObject spriggull = FindNearestSpriggull(rightAnchor.position, cfg.GrabRadius, rightAnchor.position, rightAnchor.position, requireBothHandsNear: false, out Spriggull agent, out HealthObject health);
                    if (spriggull != null)
                    {
                        StartHold(spriggull, agent, health, leftUsed: false, rightUsed: true, now, cfg);
                        return;
                    }
                }

                if (leftReady)
                {
                    GameObject spriggull = FindNearestSpriggull(leftAnchor.position, cfg.GrabRadius, leftAnchor.position, leftAnchor.position, requireBothHandsNear: false, out Spriggull agent, out HealthObject health);
                    if (spriggull != null)
                    {
                        StartHold(spriggull, agent, health, leftUsed: true, rightUsed: false, now, cfg);
                    }
                }
            }
        }

        private void StartHold(
            GameObject spriggullRoot,
            Spriggull agent,
            HealthObject health,
            bool leftUsed,
            bool rightUsed,
            double now,
            YeetConfigSnapshot cfg)
        {
            if (!_stateMachine.TryBeginHold(now))
                return;

            _usingLeftForSingle = leftUsed;
            _usingRightForSingle = rightUsed;
            _heldSession = new HeldSpriggullSession(spriggullRoot, _player, agent, health);
            _heldSession.BeginHold();

            if (cfg.EnableDebugLogging)
            {
                _log?.Msg($"[SpriggullYeeter] '{_player.name}' grabbed '{spriggullRoot.name}' (TwoHands={leftUsed && rightUsed}).");
            }
        }

        private void UpdateHeldState(
            YeetConfigSnapshot cfg,
            Controller leftCtrl,
            Controller rightCtrl,
            Transform leftAnchor,
            Transform rightAnchor,
            double now)
        {
            if (_heldSession == null || !_heldSession.IsValid)
            {
                AbortHoldIfActive();
                return;
            }

            bool leftHeld = _usingLeftForSingle &&
                            leftAnchor != null &&
                            VrHandInputReader.IsHandEmpty(leftCtrl) &&
                            VrHandInputReader.IsGripPressed(_player, leftCtrl, isLeftHand: true);

            bool rightHeld = _usingRightForSingle &&
                             rightAnchor != null &&
                             VrHandInputReader.IsHandEmpty(rightCtrl) &&
                             VrHandInputReader.IsGripPressed(_player, rightCtrl, isLeftHand: false);

            if (cfg.RequireTwoHands)
            {
                if (leftAnchor == null || rightAnchor == null)
                {
                    AbortHoldIfActive();
                    return;
                }

                float handSep = Vector3.Distance(leftAnchor.position, rightAnchor.position);
                if (handSep > cfg.MaxHandSeparation)
                {
                    if (cfg.EnableDebugLogging)
                        _log?.Msg($"[SpriggullYeeter] Auto-dropped '{_heldSession.SpriggullRoot.name}': hand separation {handSep:F2}m > {cfg.MaxHandSeparation:F2}m.");
                    AbortHoldIfActive();
                    return;
                }

                if (!leftHeld || !rightHeld)
                {
                    ReleaseAndLaunch(cfg, leftAnchor.position, rightAnchor.position, useLeft: true, useRight: true, now);
                    return;
                }

                Vector3 midpoint = (leftAnchor.position + rightAnchor.position) * 0.5f;
                Vector3 rightAxis = rightAnchor.position - leftAnchor.position;
                Vector3 playerFwd = _player.Head != null ? _player.Head.forward : _player.transform.forward;
                _heldSession.UpdateCarriedPose(midpoint, rightAxis, playerFwd);
            }
            else
            {
                if (_usingLeftForSingle && _usingRightForSingle && leftAnchor != null && rightAnchor != null)
                {
                    float handSep = Vector3.Distance(leftAnchor.position, rightAnchor.position);
                    if (handSep > cfg.MaxHandSeparation)
                    {
                        AbortHoldIfActive();
                        return;
                    }
                }

                if (!leftHeld && !rightHeld)
                {
                    Vector3 lPos = leftAnchor != null ? leftAnchor.position : _heldSession.SpriggullRoot.transform.position;
                    Vector3 rPos = rightAnchor != null ? rightAnchor.position : lPos;
                    ReleaseAndLaunch(cfg, lPos, rPos, _usingLeftForSingle, _usingRightForSingle, now);
                    return;
                }

                _usingLeftForSingle = leftHeld;
                _usingRightForSingle = rightHeld;

                Vector3 carryPos;
                Vector3 rightAxis;
                Vector3 playerFwd = _player.Head != null ? _player.Head.forward : _player.transform.forward;

                if (leftHeld && rightHeld && leftAnchor != null && rightAnchor != null)
                {
                    carryPos = (leftAnchor.position + rightAnchor.position) * 0.5f;
                    rightAxis = rightAnchor.position - leftAnchor.position;
                }
                else if (rightHeld && rightAnchor != null)
                {
                    carryPos = rightAnchor.position + (rightAnchor.forward * 0.12f);
                    rightAxis = rightAnchor.right;
                }
                else if (leftAnchor != null)
                {
                    carryPos = leftAnchor.position + (leftAnchor.forward * 0.12f);
                    rightAxis = leftAnchor.right;
                }
                else
                {
                    AbortHoldIfActive();
                    return;
                }

                _heldSession.UpdateCarriedPose(carryPos, rightAxis, playerFwd);
            }
        }

        private void ReleaseAndLaunch(
            YeetConfigSnapshot cfg,
            Vector3 leftPos,
            Vector3 rightPos,
            bool useLeft,
            bool useRight,
            double now)
        {
            HeldSpriggullSession session = _heldSession;
            _heldSession = null;
            _usingLeftForSingle = false;
            _usingRightForSingle = false;

            if (session == null || session.SpriggullRoot == null)
            {
                _stateMachine.ForceReset();
                return;
            }

            Vec3 leftVel = useLeft ? _leftVelBuffer.ComputeReleaseVelocity(now) : Vec3.Zero;
            Vec3 rightVel = useRight ? _rightVelBuffer.ComputeReleaseVelocity(now) : Vec3.Zero;

            Vec3 throwVel = YeetPhysicsCalculator.ComputeThrowVelocity(
                leftVel, useLeft,
                rightVel, useRight,
                cfg.YeetForceMultiplier);

            if (!YeetPhysicsCalculator.ShouldLaunchAsYeet(throwVel))
            {
                session.RestorePreHoldState(resetMotion: true);
                _stateMachine.TryAbortHold(now);
                if (cfg.EnableDebugLogging)
                    _log?.Msg($"[SpriggullYeeter] Released '{session.SpriggullRoot.name}' gently (speed={throwVel.Magnitude:F2}m/s).");
                return;
            }

            // CRITICAL: Restore pre-hold flags right before TriggerRagdoll so RagdollLib captures true AI baseline.
            session.RestorePreHoldState(resetMotion: true);

            Vec3 impulseVec = YeetPhysicsCalculator.ComputeLaunchImpulse(throwVel);
            Vector3 impulse = new Vector3(impulseVec.X, impulseVec.Y, impulseVec.Z);
            Vector3 launchPoint = (useLeft && useRight)
                ? (leftPos + rightPos) * 0.5f
                : (useRight ? rightPos : leftPos);

            bool ragdolled = false;
            try
            {
                ragdolled = RagdollAPI.TriggerRagdoll(
                    session.SpriggullRoot.transform,
                    impulse,
                    launchPoint,
                    cfg.RagdollDurationSeconds);
            }
            catch (Exception ex)
            {
                _log?.Error($"[SpriggullYeeter] RagdollAPI.TriggerRagdoll failed: {ex}");
            }

            if (!ragdolled)
            {
                _stateMachine.TryAbortHold(now);
                return;
            }

            _stateMachine.TryLaunch(now);
            _stateMachine.CompleteRecovery(now);

            SpriggullFeedback.PlaySquawk(session.SpriggullRoot, isLaunch: true);
            SpriggullFeedback.PlayLaunchFeatherBurst(session.SpriggullRoot, launchPoint, impulse);
            VrHandInputReader.TryPulseHaptic(_player, _player.LeftController, 0.70f, 0.12f, 150f);
            VrHandInputReader.TryPulseHaptic(_player, _player.RightController, 0.70f, 0.12f, 150f);

            _manager.RegisterAirborneLaunch(session.SpriggullRoot, _player, cfg);

            if (cfg.EnableDebugLogging)
            {
                _log?.Msg(
                    $"[SpriggullYeeter] YEETED '{session.SpriggullRoot.name}'! " +
                    $"throwSpeed={throwVel.Magnitude:F2}m/s impulse={impulse.magnitude:F1} duration={cfg.RagdollDurationSeconds:F1}s");
            }
        }

        private GameObject FindNearestSpriggull(
            Vector3 searchOrigin,
            float grabRadius,
            Vector3 leftHandPos,
            Vector3 rightHandPos,
            bool requireBothHandsNear,
            out Spriggull bestAgent,
            out HealthObject bestHealth)
        {
            bestAgent = null;
            bestHealth = null;

            int hitCount = Physics.OverlapSphereNonAlloc(
                searchOrigin,
                grabRadius + 0.35f,
                _overlapBuffer,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            if (hitCount <= 0)
                return null;

            if (hitCount > _overlapBuffer.Length)
                hitCount = _overlapBuffer.Length;

            _seenCandidateRoots.Clear();
            GameObject bestRoot = null;
            float bestDistSqr = float.MaxValue;
            float maxRadiusSqr = grabRadius * grabRadius;

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null)
                    continue;

                GameObject root = ResolveSpriggullRoot(col, out Spriggull agent, out HealthObject health);
                if (root == null)
                    continue;

                int rootId = root.GetInstanceID();
                if (!_seenCandidateRoots.Add(rootId))
                    continue;

                if (_manager.IsSpriggullHeld(rootId))
                    continue;

                Vector3 targetCenter = ResolveTargetCenter(root, health);
                if (requireBothHandsNear)
                {
                    float leftDistSqr = (targetCenter - leftHandPos).sqrMagnitude;
                    float rightDistSqr = (targetCenter - rightHandPos).sqrMagnitude;
                    if (leftDistSqr > maxRadiusSqr && rightDistSqr > maxRadiusSqr)
                    {
                        float midDistSqr = (targetCenter - searchOrigin).sqrMagnitude;
                        if (midDistSqr > maxRadiusSqr)
                            continue;
                    }
                    else if (leftDistSqr > (maxRadiusSqr * 1.6f) || rightDistSqr > (maxRadiusSqr * 1.6f))
                    {
                        continue;
                    }
                }
                else
                {
                    if ((targetCenter - searchOrigin).sqrMagnitude > maxRadiusSqr)
                        continue;
                }

                float centerDistSqr = (targetCenter - searchOrigin).sqrMagnitude;
                if (centerDistSqr < bestDistSqr)
                {
                    bestDistSqr = centerDistSqr;
                    bestRoot = root;
                    bestAgent = agent;
                    bestHealth = health;
                }
            }

            return bestRoot;
        }

        private static GameObject ResolveSpriggullRoot(Collider col, out Spriggull agent, out HealthObject health)
        {
            agent = null;
            health = null;

            NetworkEntity net = col.GetComponentInParent<NetworkEntity>();
            GameObject root = net != null
                ? (net.PrefabRoot != null ? net.PrefabRoot.gameObject : net.gameObject)
                : null;

            if (root == null)
            {
                Spriggull directAgent = col.GetComponentInParent<Spriggull>();
                if (directAgent != null)
                    root = directAgent.transform.parent != null ? directAgent.transform.parent.gameObject : directAgent.gameObject;
            }

            if (root == null || !root.activeInHierarchy)
                return null;

            if (root.GetComponentInChildren<PlayerCharacter>(true) != null)
                return null;

            agent = root.GetComponentInChildren<Spriggull>(true);
            bool nameMatches = root.name.IndexOf("Spriggull", StringComparison.OrdinalIgnoreCase) >= 0;
            if (agent == null && !nameMatches)
                return null;

            // Exclude edible/dropped parts such as SpriggullDrumstick Bone(Clone)
            if (agent == null && root.GetComponent<Pickup>() != null)
                return null;

            health = root.GetComponentInChildren<HealthObject>(true);
            if (health != null && health.Health <= 0f)
                return null;

            return root;
        }

        private static Vector3 ResolveTargetCenter(GameObject root, HealthObject health)
        {
            if (health != null && health.Core != null)
                return health.Core.position;

            return root.transform.position + (Vector3.up * 0.25f);
        }
    }
}
