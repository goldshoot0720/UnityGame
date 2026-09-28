// Mobile / touch controls: a virtual joystick (arrow keys) and per-game buttons that inject
// virtual key presses into In, so every game's keyboard logic works unchanged on phones and
// touch browsers. Taps outside the controls still act as the mouse (IMGUI buttons, tap-to-play).
using System.Collections.Generic;
using UnityEngine;

namespace MoeGames
{
    /// <summary>A game's touch layout: optional joystick and buttons (label → key).</summary>
    public class TouchLayout
    {
        public bool Joystick = true;
        /// <summary>Joystick only drives left/right (e.g. kart steering).</summary>
        public bool HorizontalOnly;
        public readonly List<(string label, KeyCode key)> Buttons = new List<(string, KeyCode)>();
        public TouchLayout Button(string label, KeyCode key) { Buttons.Add((label, key)); return this; }
        public static TouchLayout TapOnly => new TouchLayout { Joystick = false };
    }

    [DefaultExecutionOrder(-1000)]
    public class TouchPad : MonoBehaviour
    {
        static TouchPad inst;
        static readonly HashSet<KeyCode> held = new HashSet<KeyCode>(), prevHeld = new HashSet<KeyCode>();
        /// <summary>True once a touch has been seen (or on a mobile platform): the overlay is shown.</summary>
        public static bool Active { get; private set; }
        /// <summary>True while a finger that started on a control is down (the simulated mouse is ignored).</summary>
        public static bool PointerOnControls { get; private set; }
        public static TouchLayout Layout;
        static Vector2 stickCenter, stickPos;
        static int stickFinger = -1;
        static readonly Dictionary<int, KeyCode> fingerKeys = new Dictionary<int, KeyCode>();
        static readonly HashSet<int> controlFingers = new HashSet<int>();

        public static void Ensure()
        {
            if (inst) return;
            var go = new GameObject("MoeTouchPad");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<TouchPad>();
            Active = Application.isMobilePlatform;
        }

        public static bool Held(KeyCode k) => Active && held.Contains(k);
        public static bool Down(KeyCode k) => Active && held.Contains(k) && !prevHeld.Contains(k);
        public static bool Up(KeyCode k) => Active && !held.Contains(k) && prevHeld.Contains(k);

        // ── layout geometry in GUI (virtual) coordinates ──
        const float StickR = 90f, KnobR = 38f, BtnR = 46f;
        static Vector2 StickHome => new Vector2(150, Gui.H - 150);
        static Rect BackRect => new Rect(Gui.W - 76, 8, 64, 44);

        static List<(Rect r, string label, KeyCode key)> ButtonRects()
        {
            var list = new List<(Rect, string, KeyCode)>();
            var lay = Layout;
            if (lay == null) return list;
            int n = lay.Buttons.Count;
            // Arc of buttons in the bottom-right corner.
            for (int i = 0; i < n; i++)
            {
                int row = i / 3, col = i % 3;
                float x = Gui.W - 90 - col * (BtnR * 2 + 18) - row * 30, y = Gui.H - 90 - row * (BtnR * 2 + 14);
                list.Add((new Rect(x - BtnR, y - BtnR, BtnR * 2, BtnR * 2), lay.Buttons[i].label, lay.Buttons[i].key));
            }
            return list;
        }

        static Vector2 ToGui(Vector2 screen) => Gui.ScreenToGui(screen);

        void Update()
        {
            prevHeld.Clear();
            foreach (var k in held) prevHeld.Add(k);
            held.Clear();
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0) Active = true;
            if (!Active) { PointerOnControls = false; return; }
            bool guide = GuideBook.IsOpen;
            var buttons = ButtonRects();
            bool anyOnControls = false;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                var p = ToGui(t.position);
                if (t.phase == TouchPhase.Began)
                {
                    if (BackRect.Contains(p)) { fingerKeys[t.fingerId] = KeyCode.Escape; controlFingers.Add(t.fingerId); }
                    else if (!guide && Layout != null)
                    {
                        foreach (var b in buttons)
                            if (Vector2.Distance(p, b.r.center) < BtnR * 1.15f) { fingerKeys[t.fingerId] = b.key; controlFingers.Add(t.fingerId); break; }
                        if (!controlFingers.Contains(t.fingerId) && Layout.Joystick && stickFinger < 0 && p.x < Gui.W * 0.4f && p.y > Gui.H * 0.35f)
                        {
                            stickFinger = t.fingerId;
                            stickCenter = p;
                            stickPos = p;
                            controlFingers.Add(t.fingerId);
                        }
                    }
                }
                bool ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                if (t.fingerId == stickFinger)
                {
                    stickPos = p;
                    if (ended) stickFinger = -1;
                }
                if (fingerKeys.TryGetValue(t.fingerId, out var key))
                {
                    if (!ended) held.Add(key);
                    else fingerKeys.Remove(t.fingerId);
                }
                if (controlFingers.Contains(t.fingerId))
                {
                    anyOnControls = true;
                    if (ended) controlFingers.Remove(t.fingerId);
                }
            }
            if (Input.touchCount == 0) { stickFinger = -1; fingerKeys.Clear(); controlFingers.Clear(); }
            if (stickFinger >= 0)
            {
                var d = stickPos - stickCenter;
                if (d.magnitude > StickR) { d = d.normalized * StickR; }
                float dead = StickR * 0.3f;
                if (d.x < -dead) held.Add(KeyCode.LeftArrow);
                if (d.x > dead) held.Add(KeyCode.RightArrow);
                if (!Layout.HorizontalOnly)
                {
                    if (d.y < -dead) held.Add(KeyCode.UpArrow);
                    if (d.y > dead) held.Add(KeyCode.DownArrow);
                }
            }
            PointerOnControls = anyOnControls;
#endif
        }

        /// <summary>Drawn by App on top of everything.</summary>
        internal static void Draw()
        {
            if (!Active) return;
            if (Screen.height > Screen.width)
            {
                Gui.Rect(0, 0, Gui.W, Gui.H, new Color(0, 0, 0, 0.85f));
                Gui.Label("請將手機轉為橫向", Gui.W / 2, Gui.H / 2 - 20, 40, Js.Hex("#ffe066"), 0.5f, 0.5f, Color.black);
                Gui.Label("⟲", Gui.W / 2, Gui.H / 2 + 50, 60, Color.white);
                return;
            }
            var faint = new Color(1, 1, 1, 0.18f);
            var ring = new Color(1, 1, 1, 0.45f);
            Gui.Panel(BackRect, new Color(0, 0, 0, 0.45f), ring);
            Gui.Label("返回", BackRect.center.x, BackRect.center.y, 18, Color.white);
            if (GuideBook.IsOpen || Layout == null) return;
            if (Layout.Joystick)
            {
                var c = stickFinger >= 0 ? stickCenter : StickHome;
                Gui.Circle(c.x, c.y, StickR, faint);
                Gui.Ring(c.x, c.y, StickR, 3, ring);
                var k = stickFinger >= 0 ? c + Vector2.ClampMagnitude(stickPos - stickCenter, StickR) : c;
                Gui.Circle(k.x, k.y, KnobR, new Color(1, 1, 1, 0.5f));
            }
            foreach (var b in ButtonRects())
            {
                bool on = held.Contains(b.key);
                Gui.Circle(b.r.center.x, b.r.center.y, BtnR, on ? new Color(1, 0.9f, 0.4f, 0.6f) : faint);
                Gui.Ring(b.r.center.x, b.r.center.y, BtnR, 3, ring);
                Gui.Label(b.label, b.r.center.x, b.r.center.y, b.label.Length > 2 ? 16 : 20, Color.white, 0.5f, 0.5f, Color.black);
            }
        }
    }
}
