using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace SpriggullYeeter.Input
{
    /// <summary>
    /// Reads VR grip state, empty-hand availability, hand world anchors, and haptic triggers safely.
    /// </summary>
    internal static class VrHandInputReader
    {
        private const float GripAxisThreshold = 0.70f;

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
                if (interactor == null)
                    return false;

                if (interactor.IsInteracting || interactor.InteractingWith != null)
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

            if (player.IsLocalPlayer)
            {
                if (TryReadLocalXrGrip(isLeftHand ? XRNode.LeftHand : XRNode.RightHand, out bool xrPressed))
                    return xrPressed;
            }

            return TryReadGestureGrip(controller);
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

        private static bool TryReadLocalXrGrip(XRNode node, out bool pressed)
        {
            pressed = false;
            try
            {
                InputDevice device = InputDevices.GetDeviceAtXRNode(node);
                if (!device.isValid)
                    return false;

                if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool buttonDown))
                {
                    if (buttonDown)
                    {
                        pressed = true;
                        return true;
                    }
                }

                if (device.TryGetFeatureValue(CommonUsages.grip, out float gripValue))
                {
                    pressed = gripValue >= GripAxisThreshold;
                    return true;
                }
            }
            catch
            {
                // Fall back to HandGestureController if XR subsystem query fails.
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
