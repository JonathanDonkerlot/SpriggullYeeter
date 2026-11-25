using HarmonyLib;
using UnityEngine.XR;

namespace SpriggullYeeter.Input
{
    /// <summary>
    /// Reads VR grip state across ATT's native ControllerInput, replicated HandGestureController,
    /// and Unity XR InputDevices, plus empty-hand availability and haptic feedback.
    /// </summary>
    internal static class VrHandInputReader
    {
        private const float GripAxisThreshold = 0.55f;

        private static readonly AccessTools.FieldRef<HandGestureController, float> GripAxisRef =
            CreateGripAxisFieldRef();

        private static AccessTools.FieldRef<HandGestureController, float> CreateGripAxisFieldRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<HandGestureController, float>("gripAxis");
            }
            catch
            {
                return null;
            }
        }

        public static UnityEngine.Transform GetHandAnchor(Controller controller)
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

            // 1. ATT native ControllerInput (works cleanly with SteamVR/OpenVR, Oculus, and custom bindings)
            if (TryReadControllerInputGrip(controller))
                return true;

            // 2. ATT replicated HandGestureController.gripAxis (works on both client and server)
            if (TryReadGestureGrip(controller))
                return true;

            // 3. Unity XR InputDevices fallback for local VR player
            if (player.IsLocalPlayer && TryReadLocalXrGrip(isLeftHand ? XRNode.LeftHand : XRNode.RightHand))
                return true;

            return false;
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

        private static bool TryReadControllerInputGrip(Controller controller)
        {
            try
            {
                PlayerInput playerInput = controller.PlayerInput;
                ControllerInput raw = playerInput != null ? playerInput.RawInput : null;
                if (raw == null)
                    return false;

                if (raw.Grab != null && raw.Grab.State)
                    return true;

                if (raw.HardGrab != null && raw.HardGrab.State)
                    return true;

                if (raw.GrabAxis >= GripAxisThreshold || raw.GrabStrength >= GripAxisThreshold)
                    return true;
            }
            catch
            {
                // Ignore and fall back to gesture/XR reads.
            }

            return false;
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

        private static bool TryReadGestureGrip(Controller controller)
        {
            if (GripAxisRef == null)
                return false;

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

                return GripAxisRef(gesture) >= GripAxisThreshold;
            }
            catch
            {
                return false;
            }
        }
    }
}
