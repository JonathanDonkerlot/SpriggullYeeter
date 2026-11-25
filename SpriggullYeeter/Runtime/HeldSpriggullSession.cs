using Alta.Impact;
using Alta.Intelligence;
using Alta.Networking;
using Alta.Pathfinding;
using HarmonyLib;
using SpriggullYeeter.Feedback;
using SpriggullYeeter.Input;
using UnityEngine;

namespace SpriggullYeeter.Runtime
{
    /// <summary>
    /// Manages a single Spriggull while grabbed and carried by a player:
    /// disables AI, pathfinding, Walker, flail, beak, and all colliders while held,
    /// pins physics pose across Update/FixedUpdate/LateUpdate, and cleanly restores pre-hold state.
    /// </summary>
    internal sealed class HeldSpriggullSession
    {
        private const float SquawkIntervalSeconds = 1.05f;
        private const float HapticIntervalSeconds = 0.08f;

        private static readonly AccessTools.FieldRef<Spriggull, FixedImpactor> BeakFieldRef =
            CreateBeakFieldRef();

        private static AccessTools.FieldRef<Spriggull, FixedImpactor> CreateBeakFieldRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<Spriggull, FixedImpactor>("beak");
            }
            catch
            {
                return null;
            }
        }

        public GameObject SpriggullRoot { get; }
        public int RootInstanceId { get; }
        public PlayerController Holder { get; }
        public Spriggull Agent { get; }
        public HealthObject Health { get; }

        private readonly Walker _walker;
        private readonly Navigator _navigator;
        private readonly Director _director;
        private readonly SpriggullFlail _flail;
        private readonly Rigidbody _rootRb;
        private readonly NetworkRigidbody _netRb;
        private readonly FixedImpactor _beakImpactor;
        private readonly Animator _animator;
        private readonly Collider[] _allColliders;
        private readonly bool[] _savedColliderEnabled;

        private bool _savedAgentEnabled;
        private bool _savedWalkerEnabled;
        private bool _savedWalkerGoActive;
        private bool _savedNavigatorEnabled;
        private bool _savedDirectorEnabled;
        private bool _savedFlailEnabled;
        private bool _savedBeakEnabled;
        private bool _savedRootRbKinematic;
        private bool _savedRootRbDetectCollisions;
        private bool _savedRootRbUseGravity;
        private bool _savedNetRbSuspended;
        private bool _savedAnimatorSpeedCaptured;
        private float _savedAnimatorSpeed = 1f;
        private bool _suspended;

        private Vector3 _lastDesiredPos;
        private Quaternion _lastDesiredRot;
        private float _holdStartTime;
        private float _nextSquawkTime;
        private float _nextHapticTime;

        public HeldSpriggullSession(
            GameObject spriggullRoot,
            PlayerController holder,
            Spriggull agent,
            HealthObject health)
        {
            SpriggullRoot = spriggullRoot;
            RootInstanceId = spriggullRoot.GetInstanceID();
            Holder = holder;
            Agent = agent;
            Health = health;

            _walker = spriggullRoot.GetComponentInChildren<Walker>(true);
            _navigator = spriggullRoot.GetComponentInChildren<Navigator>(true);
            _director = spriggullRoot.GetComponentInChildren<Director>(true);
            _flail = spriggullRoot.GetComponentInChildren<SpriggullFlail>(true);
            _rootRb = spriggullRoot.GetComponent<Rigidbody>();
            _netRb = spriggullRoot.GetComponent<NetworkRigidbody>();
            _animator = spriggullRoot.GetComponentInChildren<Animator>(true);

            _allColliders = spriggullRoot.GetComponentsInChildren<Collider>(true);
            _savedColliderEnabled = new bool[_allColliders.Length];

            if (agent != null && BeakFieldRef != null)
            {
                try
                {
                    _beakImpactor = BeakFieldRef(agent);
                }
                catch
                {
                    _beakImpactor = null;
                }
            }

            if (_beakImpactor == null)
                _beakImpactor = spriggullRoot.GetComponentInChildren<FixedImpactor>(true);
        }

        public bool IsValid =>
            SpriggullRoot != null &&
            SpriggullRoot.activeInHierarchy &&
            Holder != null &&
            (Health == null || Health.Health > 0f);

        public void BeginHold()
        {
            _holdStartTime = Time.unscaledTime;
            _nextSquawkTime = _holdStartTime + 0.15f;
            _nextHapticTime = _holdStartTime;

            _lastDesiredPos = SpriggullRoot.transform.position;
            _lastDesiredRot = SpriggullRoot.transform.rotation;

            SuspendForCarry();

            SpriggullFeedback.PlaySquawk(SpriggullRoot, isLaunch: false);
            VrHandInputReader.TryPulseHaptic(Holder, Holder.LeftController, 0.35f, 0.07f, 110f);
            VrHandInputReader.TryPulseHaptic(Holder, Holder.RightController, 0.35f, 0.07f, 110f);
        }

        public void UpdateCarriedPose(Vector3 targetCenterPos, Vector3 handRightAxis, Vector3 playerForward)
        {
            if (!IsValid)
                return;

            float now = Time.unscaledTime;
            float elapsed = now - _holdStartTime;

            // Gentle squirm oscillation while clutched in midair
            float squirmY = Mathf.Sin(elapsed * 14f) * 0.025f;
            float squirmYaw = Mathf.Sin(elapsed * 11f) * 14f;
            float squirmRoll = Mathf.Cos(elapsed * 16f) * 10f;

            Vector3 desiredPos = targetCenterPos + new Vector3(0f, -0.14f + squirmY, 0f);

            Vector3 flatForward = Vector3.Cross(handRightAxis, Vector3.up);
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 1e-4f)
            {
                flatForward = new Vector3(playerForward.x, 0f, playerForward.z);
                if (flatForward.sqrMagnitude < 1e-4f)
                    flatForward = Vector3.forward;
            }
            else if (Vector3.Dot(flatForward, playerForward) < 0f)
            {
                flatForward = -flatForward;
            }

            Quaternion baseRot = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            Quaternion desiredRot = baseRot * Quaternion.Euler(8f, squirmYaw, squirmRoll);

            float t = Mathf.Clamp01(Time.deltaTime * 28f);
            _lastDesiredPos = Vector3.Lerp(SpriggullRoot.transform.position, desiredPos, t);
            _lastDesiredRot = Quaternion.Slerp(SpriggullRoot.transform.rotation, desiredRot, t);

            ApplyPinnedPose();

            if (now >= _nextHapticTime)
            {
                _nextHapticTime = now + HapticIntervalSeconds;
                VrHandInputReader.TryPulseHaptic(Holder, Holder.LeftController, 0.16f, 0.05f, 90f);
                VrHandInputReader.TryPulseHaptic(Holder, Holder.RightController, 0.16f, 0.05f, 90f);
            }

            if (now >= _nextSquawkTime)
            {
                _nextSquawkTime = now + SquawkIntervalSeconds;
                SpriggullFeedback.PlaySquawk(SpriggullRoot, isLaunch: false);
            }
        }

        public void SyncPhysicsPose()
        {
            if (!_suspended || !IsValid)
                return;

            if (Agent != null && NetworkSceneManager.IsServer && Agent.IsEnabled)
            {
                Agent.CancelCurrentAction();
                Agent.IsEnabled = false;
            }

            if (_walker != null && _walker.gameObject.activeSelf)
                _walker.gameObject.SetActive(false);

            ApplyPinnedPose();
        }

        /// <summary>
        /// Restores pre-hold AI/Walker/Collider/Rigidbody flags. Must be called both on clean drop AND
        /// immediately prior to RagdollAPI.TriggerRagdoll so RagdollLib snapshots true baseline states.
        /// </summary>
        public void RestorePreHoldState(bool resetMotion)
        {
            if (!_suspended)
                return;

            _suspended = false;

            if (SpriggullRoot == null)
                return;

            try
            {
                if (_animator != null && _savedAnimatorSpeedCaptured)
                {
                    _animator.speed = _savedAnimatorSpeed;
                    _savedAnimatorSpeedCaptured = false;
                }

                for (int i = 0; i < _allColliders.Length; i++)
                {
                    Collider col = _allColliders[i];
                    if (col != null)
                        col.enabled = _savedColliderEnabled[i];
                }

                if (_rootRb != null)
                {
                    _rootRb.useGravity = _savedRootRbUseGravity;
                    _rootRb.isKinematic = _savedRootRbKinematic;
                    _rootRb.detectCollisions = _savedRootRbDetectCollisions;
                    if (resetMotion && !_savedRootRbKinematic)
                    {
                        _rootRb.velocity = Vector3.zero;
                        _rootRb.angularVelocity = Vector3.zero;
                    }
                }

                if (_netRb != null)
                    _netRb.isSuspended = _savedNetRbSuspended;

                if (_flail != null)
                    _flail.enabled = _savedFlailEnabled;

                if (_beakImpactor != null)
                    _beakImpactor.enabled = _savedBeakEnabled;

                if (_walker != null)
                {
                    if (_savedWalkerGoActive && !_walker.gameObject.activeSelf)
                        _walker.gameObject.SetActive(true);
                    _walker.enabled = _savedWalkerEnabled;
                    if (NetworkSceneManager.IsServer)
                        _walker.ClearBlocked();
                }

                if (_navigator != null)
                {
                    _navigator.enabled = _savedNavigatorEnabled;
                    if (NetworkSceneManager.IsServer)
                        _navigator.ResetPosition();
                }

                if (_director != null)
                    _director.enabled = _savedDirectorEnabled;

                if (Agent != null && NetworkSceneManager.IsServer)
                    Agent.IsEnabled = _savedAgentEnabled;
            }
            catch
            {
                // Ignore restoration errors if entity is mid-destroy.
            }
        }

        private void ApplyPinnedPose()
        {
            SpriggullRoot.transform.SetPositionAndRotation(_lastDesiredPos, _lastDesiredRot);

            if (_rootRb != null)
            {
                if (!_rootRb.isKinematic)
                {
                    _rootRb.velocity = Vector3.zero;
                    _rootRb.angularVelocity = Vector3.zero;
                    _rootRb.isKinematic = true;
                }

                _rootRb.position = _lastDesiredPos;
                _rootRb.rotation = _lastDesiredRot;
            }
        }

        private void SuspendForCarry()
        {
            if (_suspended)
                return;

            _suspended = true;

            if (Agent != null)
            {
                _savedAgentEnabled = Agent.IsEnabled;
                if (NetworkSceneManager.IsServer)
                {
                    Agent.CancelCurrentAction();
                    Agent.IsEnabled = false;
                }
            }

            if (_walker != null)
            {
                _savedWalkerEnabled = _walker.enabled;
                _savedWalkerGoActive = _walker.gameObject.activeSelf;
                if (NetworkSceneManager.IsServer)
                    _walker.StopNormalizedMove();
                _walker.enabled = false;
                _walker.gameObject.SetActive(false);
            }

            if (_navigator != null)
            {
                _savedNavigatorEnabled = _navigator.enabled;
                if (NetworkSceneManager.IsServer)
                    _navigator.Stop();
                _navigator.enabled = false;
            }

            if (_director != null)
            {
                _savedDirectorEnabled = _director.enabled;
                if (NetworkSceneManager.IsServer)
                    _director.Stop();
                _director.enabled = false;
            }

            if (_flail != null)
            {
                _savedFlailEnabled = _flail.enabled;
                _flail.enabled = false;
            }

            if (_beakImpactor != null)
            {
                _savedBeakEnabled = _beakImpactor.enabled;
                _beakImpactor.enabled = false;
            }

            if (_netRb != null)
            {
                _savedNetRbSuspended = _netRb.isSuspended;
                if (!NetworkSceneManager.IsServer)
                    _netRb.isSuspended = true;
            }

            if (_rootRb != null)
            {
                _savedRootRbKinematic = _rootRb.isKinematic;
                _savedRootRbDetectCollisions = _rootRb.detectCollisions;
                _savedRootRbUseGravity = _rootRb.useGravity;
                if (!_rootRb.isKinematic)
                {
                    _rootRb.velocity = Vector3.zero;
                    _rootRb.angularVelocity = Vector3.zero;
                }
                _rootRb.useGravity = false;
                _rootRb.isKinematic = true;
                _rootRb.detectCollisions = false;
            }

            // Disable all colliders while held so player hands never physically stick to or push the Spriggull
            for (int i = 0; i < _allColliders.Length; i++)
            {
                Collider col = _allColliders[i];
                if (col != null)
                {
                    _savedColliderEnabled[i] = col.enabled;
                    col.enabled = false;
                }
            }

            if (_animator != null)
            {
                _savedAnimatorSpeed = _animator.speed;
                _savedAnimatorSpeedCaptured = true;
                _animator.speed = 1.65f;
            }
        }
    }
}
