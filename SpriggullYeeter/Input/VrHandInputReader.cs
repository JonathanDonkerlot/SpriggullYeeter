using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace SpriggullYeeter.Input
{
    /// <summary>
    /// Reads VR hold-to-grip state cleanly without false positives (never reads GrabStrength),
    /// supporting both local ControllerInput/ActionsManager and server-replicated HandGestureController.
    /// </summary>
    internal static class VrHandInputReader
    {
        private const float GripAxisThreshold = 0.65f;
        private const float FreeModeCurlThreshold = 0.70f;

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

            // Local VR player: read ATT's bound action/raw input directly and do not fall through
            // when RawInput is available so releasing Grip guarantees an immediate false.
            if (controller.PlayerInput != null)
            {
                try
                {
                    ControllerActionsManagerProxy actions = controller.ActionsManager;
                    if (actions != null)
                    {
                        ControllerActionProxy interact = actions[ControllerActionType.Interact];
                        if (interact != null && interact.IsEnabled && interact.State)
                            return true;
                    }

                    ControllerInput raw = controller.PlayerInput.RawInput;
                    if (raw != null)
                    {
                        if (raw.Grab != null && raw.Grab.State)
                            return true;

                        if (raw.HardGrab != null && raw.HardGrab.State)
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
                        float curlAvg = (skeleton[2] + skeleton[3] + skeleton[4]) / 3f;
                        if (curlAvg >= FreeModeCurlThreshold)
                            return true;
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
