using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonProtocol
{
    // Thin polled input layer over the Input System devices (keyboard, mouse and standard Xbox/PlayStation gamepads).
    // Anything can inject a simulated state for automated tests via Override.
    public static class NPInput
    {
        public static float Sensitivity = 1f;          // user setting, 0.2..3
        public static bool InvertY;
        public static bool Enabled = true;             // false while menus are open

        public struct State
        {
            public Vector2 move, look;
            public bool controller;
            public float throttle, brake;
            public bool sprint, walk, crouchPressed, jumpPressed, fire, aim, zoomPressed, reloadPressed, shoulderPressed, interactPressed;
        }

        public static State Override;
        public static bool UseOverride;

        public static bool UsingGamepad { get; private set; }
        public static float StickSensitivity = 145f;
        static int deviceFrame = -1;

        // Sample device intent once per frame; a connected idle controller must not steal mouse input.
        static void SelectDevice(Gamepad pad, Keyboard kb, Mouse mouse)
        {
            if (deviceFrame == Time.frameCount) return;
            deviceFrame = Time.frameCount;
            if (pad == null) { UsingGamepad = false; return; }
            if ((kb != null && kb.anyKey.wasPressedThisFrame) || (mouse != null &&
                (mouse.delta.ReadValue().sqrMagnitude > 4f || mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))) UsingGamepad = false;
            if (pad.leftStick.ReadValue().sqrMagnitude > 0.01f || pad.rightStick.ReadValue().sqrMagnitude > 0.01f ||
                pad.rightTrigger.ReadValue() > 0.08f || pad.leftTrigger.ReadValue() > 0.08f ||
                pad.buttonSouth.isPressed || pad.buttonNorth.isPressed || pad.buttonEast.isPressed || pad.buttonWest.isPressed ||
                pad.leftShoulder.isPressed || pad.rightShoulder.isPressed || pad.startButton.isPressed) UsingGamepad = true;
        }

        public static State Read()
        {
            if (UseOverride) return Override;
            var s = new State();
            if (!Enabled) return s;
            var kb = Keyboard.current; var m = Mouse.current; var pad = Gamepad.current;
            SelectDevice(pad, kb, m);
            if (kb != null)
            {
                float x = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
                float y = (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
                s.move = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
                s.sprint = kb.leftShiftKey.isPressed;
                s.walk = kb.leftAltKey.isPressed || kb.capsLockKey.isPressed;
                s.crouchPressed = kb.cKey.wasPressedThisFrame || kb.leftCtrlKey.wasPressedThisFrame;
                s.jumpPressed = kb.spaceKey.wasPressedThisFrame;
                s.reloadPressed = kb.rKey.wasPressedThisFrame;
                s.shoulderPressed = kb.qKey.wasPressedThisFrame;
                s.interactPressed = kb.eKey.wasPressedThisFrame;
                s.zoomPressed = kb.vKey.wasPressedThisFrame;
            }
            if (m != null && Cursor.lockState == CursorLockMode.Locked)
            {
                // Raw pixel delta, no smoothing: the camera must follow the hand exactly.
                Vector2 d = m.delta.ReadValue();
                s.look = new Vector2(d.x, InvertY ? -d.y : d.y) * (0.075f * Sensitivity);
                s.fire = m.leftButton.isPressed;
                s.aim = m.rightButton.isPressed;
                s.zoomPressed |= m.middleButton.wasPressedThisFrame;
            }
            if (UsingGamepad && pad != null)
            {
                bool riding = FinalityRun.Instance && FinalityRun.Instance.Running;
                s.controller = true;
                s.move = pad.leftStick.ReadValue();
                var look = pad.rightStick.ReadValue();
                // Stick speed is degrees per second, independent of frame rate. Mouse remains raw delta.
                s.look = new Vector2(look.x, InvertY ? -look.y : look.y) * (StickSensitivity * Sensitivity * Time.deltaTime);
                s.throttle = pad.rightTrigger.ReadValue(); s.brake = pad.leftTrigger.ReadValue();
                s.fire = riding ? pad.rightShoulder.isPressed : s.throttle > 0.1f;
                s.aim = !riding && s.brake > 0.1f;
                s.sprint = riding ? pad.buttonSouth.isPressed : pad.leftStickButton.isPressed;
                s.jumpPressed = riding ? pad.buttonEast.wasPressedThisFrame : pad.buttonSouth.wasPressedThisFrame;
                s.crouchPressed = !riding && pad.buttonEast.wasPressedThisFrame;
                s.reloadPressed = pad.buttonWest.wasPressedThisFrame;
                s.interactPressed = pad.buttonNorth.wasPressedThisFrame;
                s.shoulderPressed = !riding && pad.rightShoulder.wasPressedThisFrame;
                s.zoomPressed = pad.dpad.up.wasPressedThisFrame;
                s.walk = false;
            }
            return s;
        }
    }
}
