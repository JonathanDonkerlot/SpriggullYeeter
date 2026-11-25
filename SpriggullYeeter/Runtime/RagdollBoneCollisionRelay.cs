using UnityEngine;

namespace SpriggullYeeter.Runtime
{
    /// <summary>
    /// Attached dynamically to active RagdollLib bone Rigidbodies during a Spriggull throw
    /// so collisions on detached pelvis/limb bones report directly back to <see cref="AirborneSpriggullTracker"/>.
    /// </summary>
    internal sealed class RagdollBoneCollisionRelay : MonoBehaviour
    {
        private AirborneSpriggullTracker _tracker;

        public void Initialize(AirborneSpriggullTracker tracker)
        {
            _tracker = tracker;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_tracker == null || collision == null)
                return;

            _tracker.HandleBoneCollision(this, collision);
        }

        private void OnDestroy()
        {
            _tracker = null;
        }
    }
}
