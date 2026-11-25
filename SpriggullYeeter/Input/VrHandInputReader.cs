using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace SpriggullYeeter.Input
{
    /// <summary>
    /// Reads strict hold-to-grip state across local VR ControllerInput and server-replicated
    /// HandGestureController without sticky Interact/ToggleGrab or false GrabStrength triggers.
    /// </summary>
    internal static class VrHandInputReader
    {
        private const float GripAxisThreshold = 0.70f;
        private const float FreeModeCurlThreshold = 0.75f;

        private static readonly AccessTools.FieldRef<HandGestureController, float> GripAxisRef =
            CreateFieldRef<float>("gripAxis");

        private static readonly AccessTools.FieldRef<HandGestureController, bool> IsFreeGestureModeRef =
            CreateFieldRef<bool>("isFreeGestureMode");

        private static readonly AccessTools.FieldRef<HandGestureController, float[]> SkeletonInputRef =
            CreateFieldRef<float[]>("skeletonInput");

        private static AccessTools.FieldRef<HandGestureController, T> CreateFieldRef<T>(string fieldName)
        {
            try
            {
                return AccessTools.FieldRefAccess<HandGestureController, T>(fieldName);
            }
            catch
            {
                return null;
            }
        }

        public static Transform GetHandAnchor(Controller controller)
        {
            if (controller == null)
                return null;

            return controller.GripPoint != null ? controller.GripPoint : controller.transform;
        }

        public static bool IsHandEmpty(Controller controller)
        {
            if (controller == null)
                return false;

            try
            {
                Interactor interactor = controller.Interactor;
                if (interactor != null && (interactor.IsInteracting || interactor.InteractingWith != null))
                    return false;

                if (controller.TryGetHeldPickup(out Pickup heldPickup) && heldPickup != null)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsGripPressed(PlayerController player, Controller controller, bool isLeftHand)
        {
            if (player == null || controller == null)
                return false;

            // Local VR player: strictly read RawInput.Grab / RawInput.GrabAxis (no sticky ActionsManager/HardGrab).
            if (controller.PlayerInput != null)
            {
                try
                {
                    ControllerInput raw = controller.PlayerInput.RawInput;
                    if (raw != null)
                    {
                        if (raw.Grab != null && raw.Grab.State)
                            return true;

                        return raw.GrabAxis >= GripAxisThreshold;
                    }
                }
                catch
                {
                    // Fall through to XR fallback only if ATT input read threw.
                }

                if (player.IsLocalPlayer)
                    return TryReadLocalXrGrip(isLeftHand ? XRNode.LeftHand : XRNode.RightHand);

                return false;
            }

            // Remote player on server: read ATT's replicated HandGestureController state.
            return TryReadReplicatedGestureGrip(controller);
        }

        public static void TryPulseHaptic(
            PlayerController player,
            Controller controller,
            float strength,
            float durationSeconds,
            float frequency)
        {
            if (player == null || !player.IsLocalPlayer || controller == null)
                return;

            try
            {
                controller.TriggerHaptic(strength, durationSeconds, frequency);
            }
            catch
            {
                // Ignore when XR device does not support haptics or is disconnected.
            }
        }

        private static bool TryReadLocalXrGrip(XRNode node)
        {
            try
            {
                InputDevice device = InputDevices.GetDeviceAtXRNode(node);
                if (!device.isValid)
                    return false;

                if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool buttonDown) && buttonDown)
                    return true;

                if (device.TryGetFeatureValue(CommonUsages.grip, out float gripValue) && gripValue >= GripAxisThreshold)
                    return true;
            }
            catch
            {
                // Ignore if XR subsystem query fails.
            }

            return false;
        }

        private static bool TryReadReplicatedGestureGrip(Controller controller)
        {
            try
            {
                HandGestureController gesture = controller.Hand != null
                    ? controller.Hand.GestureController
                    : null;

                if (gesture == null)
                    gesture = controller.GetComponent<HandGestureController>();

                if (gesture == null)
                    gesture = controller.GetControllerExtension<HandGestureController>();

                if (gesture == null)
                    return false;

                if (IsFreeGestureModeRef != null && SkeletonInputRef != null && IsFreeGestureModeRef(gesture))
                {
                    float[] skeleton = SkeletonInputRef(gesture);
                    if (skeleton != null && skeleton.Length >= 5)
                    {
                        return skeleton[2] >= FreeModeCurlThreshold &&
                               skeleton[3] >= FreeModeCurlThreshold &&
                               skeleton[4] >= FreeModeCurlThreshold;
                    }
                }

                if (GripAxisRef != null && GripAxisRef(gesture) >= GripAxisThreshold)
                    return true;
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
