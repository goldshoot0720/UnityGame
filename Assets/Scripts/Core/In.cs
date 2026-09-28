// Input polling that works with either backend (legacy Input Manager or the Input System
// package), so the project runs whatever "Active Input Handling" is set to.
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && MOE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace MoeGames
{
    public static class In
    {
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM && MOE_INPUT_SYSTEM
        static Key Map(KeyCode k)
        {
            if (k >= KeyCode.A && k <= KeyCode.Z) return Key.A + (k - KeyCode.A);
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return k == KeyCode.Alpha0 ? Key.Digit0 : Key.Digit1 + (k - KeyCode.Alpha1);
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return Key.Numpad0 + (k - KeyCode.Keypad0);
            switch (k)
            {
                case KeyCode.Space: return Key.Space;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.KeypadEnter: return Key.NumpadEnter;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.Comma: return Key.Comma;
                case KeyCode.Period: return Key.Period;
                case KeyCode.Slash: return Key.Slash;
                case KeyCode.Semicolon: return Key.Semicolon;
                case KeyCode.Minus: return Key.Minus;
                case KeyCode.Equals: return Key.Equals;
                default: return Key.None;
            }
        }
        static KeyControl Ctl(KeyCode k)
        {
            var kb = Keyboard.current;
            var key = Map(k);
            return kb == null || key == Key.None ? null : kb[key];
        }
        public static bool RawDown(KeyCode k) { var c = Ctl(k); return c != null && c.wasPressedThisFrame; }
        public static bool RawHeld(KeyCode k) { var c = Ctl(k); return c != null && c.isPressed; }
        public static bool RawUp(KeyCode k) { var c = Ctl(k); return c != null && c.wasReleasedThisFrame; }
        static ButtonControl Btn(int b)
        {
            var m = Mouse.current;
            if (m == null) return null;
            return b == 0 ? m.leftButton : b == 1 ? m.rightButton : m.middleButton;
        }
        public static bool RawMouseDown(int b = 0) { var c = Btn(b); return c != null && c.wasPressedThisFrame; }
        public static bool RawMouseHeld(int b = 0) { var c = Btn(b); return c != null && c.isPressed; }
        public static bool RawMouseUp(int b = 0) { var c = Btn(b); return c != null && c.wasReleasedThisFrame; }
        public static Vector2 MousePos => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static float RawScroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
        public static bool RawAnyKeyDown => Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        public static bool RawDown(KeyCode k) => Input.GetKeyDown(k);
        public static bool RawHeld(KeyCode k) => Input.GetKey(k);
        public static bool RawUp(KeyCode k) => Input.GetKeyUp(k);
        public static bool RawMouseDown(int b = 0) => Input.GetMouseButtonDown(b);
        public static bool RawMouseHeld(int b = 0) => Input.GetMouseButton(b);
        public static bool RawMouseUp(int b = 0) => Input.GetMouseButtonUp(b);
        public static Vector2 MousePos => Input.mousePosition;
        public static float RawScroll => Input.mouseScrollDelta.y;
        public static bool RawAnyKeyDown => Input.anyKeyDown;
#else
        public static bool RawDown(KeyCode k) => false;
        public static bool RawHeld(KeyCode k) => false;
        public static bool RawUp(KeyCode k) => false;
        public static bool RawMouseDown(int b = 0) => false;
        public static bool RawMouseHeld(int b = 0) => false;
        public static bool RawMouseUp(int b = 0) => false;
        public static Vector2 MousePos => Vector2.zero;
        public static float RawScroll => 0f;
        public static bool RawAnyKeyDown => false;
#endif

        /// <summary>True while a modal overlay (the guide book) owns the input; games see no input then.</summary>
        public static bool Blocked;
        // Keyboard + touch overlay (TouchPad injects virtual keys; the simulated mouse is ignored while
        // a finger is on a touch control). KeyCode.Mouse1 from the pad acts as a right click.
        public static bool Down(KeyCode k) => !Blocked && (RawDown(k) || TouchPad.Down(k));
        public static bool Held(KeyCode k) => !Blocked && (RawHeld(k) || TouchPad.Held(k));
        public static bool Up(KeyCode k) => !Blocked && (RawUp(k) || TouchPad.Up(k));
        public static bool MouseDown(int b = 0) => !Blocked && ((RawMouseDown(b) && !TouchPad.PointerOnControls) || (b == 1 && TouchPad.Down(KeyCode.Mouse1)));
        public static bool MouseHeld(int b = 0) => !Blocked && ((RawMouseHeld(b) && !TouchPad.PointerOnControls) || (b == 1 && TouchPad.Held(KeyCode.Mouse1)));
        public static bool MouseUp(int b = 0) => !Blocked && RawMouseUp(b);
        public static float Scroll => Blocked ? 0f : RawScroll;
        public static bool AnyKeyDown => !Blocked && RawAnyKeyDown;

        public static bool Down(params KeyCode[] ks) { foreach (var k in ks) if (Down(k)) return true; return false; }
        public static bool Held(params KeyCode[] ks) { foreach (var k in ks) if (Held(k)) return true; return false; }

        // Common bindings used across the ports.
        public static bool Confirm => Down(KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter);
        public static bool Back => Down(KeyCode.Escape);
        public static bool LeftHeld => Held(KeyCode.LeftArrow, KeyCode.A);
        public static bool RightHeld => Held(KeyCode.RightArrow, KeyCode.D);
        public static bool UpHeld => Held(KeyCode.UpArrow, KeyCode.W);
        public static bool DownHeld => Held(KeyCode.DownArrow, KeyCode.S);
        public static bool LeftDown => Down(KeyCode.LeftArrow, KeyCode.A);
        public static bool RightDown => Down(KeyCode.RightArrow, KeyCode.D);
        public static bool UpDown => Down(KeyCode.UpArrow, KeyCode.W);
        public static bool DownDown => Down(KeyCode.DownArrow, KeyCode.S);

        /// <summary>-1..1 axis from arrow keys / WASD.</summary>
        public static Vector2 Axis => new Vector2((RightHeld ? 1 : 0) - (LeftHeld ? 1 : 0), (UpHeld ? 1 : 0) - (DownHeld ? 1 : 0));

        /// <summary>Digit key 1..9 pressed this frame (0 if none).</summary>
        public static int DigitDown
        {
            get
            {
                for (int i = 1; i <= 9; i++)
                    if (Down(KeyCode.Alpha0 + i) || Down(KeyCode.Keypad0 + i)) return i;
                return 0;
            }
        }

        /// <summary>Mouse position in <see cref="Gui"/> virtual coordinates.</summary>
        public static Vector2 GuiMouse => Gui.ScreenToGui(MousePos);
    }
}
