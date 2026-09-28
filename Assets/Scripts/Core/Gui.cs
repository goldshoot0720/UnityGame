// Immediate-mode HUD helpers (IMGUI) mirroring the Phaser ui.ts API: label(), panel(),
// button(), bars, circles and lines, in a virtual coordinate space that is 720 units tall
// (width follows the aspect ratio). Call only from OnGUI. Needs no UI packages, and the
// dynamic OS font covers Traditional Chinese.
using System.Collections.Generic;
using UnityEngine;

namespace MoeGames
{
    public static class Gui
    {
        public const float RefH = 720f;
        public static float Scale => Screen.height / RefH;
        /// <summary>Virtual width (depends on aspect ratio).</summary>
        public static float W => Screen.width / Scale;
        public static float H => RefH;

        static Font font;
        static Texture2D white, circle, rounded;
        static readonly Dictionary<int, Texture2D> rings = new Dictionary<int, Texture2D>();
        static GUIStyle labelStyle, roundStyle;
        static readonly GUIContent content = new GUIContent();

        public static Font Font
        {
            get
            {
                if (!font)
                {
                    // Bundled Noto Sans TC subset (needed on WebGL, which has no OS fonts); OS fonts as fallback.
                    font = Resources.Load<Font>("Fonts/MoeFont");
                    if (!font) font = Font.CreateDynamicFontFromOSFont(new[] { "PingFang TC", "Noto Sans CJK TC", "Noto Sans TC", "Microsoft JhengHei", "Heiti TC", "Arial Unicode MS", "Arial" }, 32);
                }
                return font;
            }
        }

        public static Texture2D White
        {
            get
            {
                if (!white)
                {
                    white = new Texture2D(2, 2) { hideFlags = HideFlags.DontSave };
                    white.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                    white.Apply();
                }
                return white;
            }
        }

        static Texture2D CircleTex
        {
            get
            {
                if (!circle) circle = MakeRing(64, 0f);
                return circle;
            }
        }

        /// <summary>Anti-aliased disc (inner = 0) or ring (inner = inner radius fraction).</summary>
        static Texture2D MakeRing(int size, float inner)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float r = size * 0.5f, ir = r * inner;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    float a = Mathf.Clamp01(r - d);
                    if (inner > 0) a *= Mathf.Clamp01(d - ir);
                    px[y * size + x] = new Color(1, 1, 1, a);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static GUIStyle RoundStyle
        {
            get
            {
                if (roundStyle == null || !rounded)
                {
                    const int s = 32, rad = 10;
                    rounded = new Texture2D(s, s, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                    var px = new Color[s * s];
                    for (int y = 0; y < s; y++)
                        for (int x = 0; x < s; x++)
                        {
                            float cx = Mathf.Clamp(x + 0.5f, rad, s - rad), cy = Mathf.Clamp(y + 0.5f, rad, s - rad);
                            float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                            px[y * s + x] = new Color(1, 1, 1, Mathf.Clamp01(rad - d + 0.5f));
                        }
                    rounded.SetPixels(px);
                    rounded.Apply();
                    roundStyle = new GUIStyle { border = new RectOffset(rad, rad, rad, rad) };
                    roundStyle.normal.background = rounded;
                }
                return roundStyle;
            }
        }

        static GUIStyle LabelStyle
        {
            get
            {
                if (labelStyle == null)
                {
                    labelStyle = new GUIStyle { font = Font, richText = false, clipping = TextClipping.Overflow };
                    labelStyle.normal.textColor = Color.white;
                }
                return labelStyle;
            }
        }

        /// <summary>Call at the top of OnGUI: sets the virtual-resolution matrix.</summary>
        public static void Begin()
        {
            float s = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            GUI.color = Color.white;
        }

        public static Vector2 Mouse => Event.current != null ? Event.current.mousePosition : ScreenToGui(In.MousePos);

        /// <summary>Screen pixels (origin bottom-left) → virtual GUI coordinates (origin top-left).</summary>
        public static Vector2 ScreenToGui(Vector2 screen) => new Vector2(screen.x / Scale, (Screen.height - screen.y) / Scale);

        /// <summary>World → virtual GUI coordinates; z is the camera depth (negative = behind).</summary>
        public static Vector3 WorldToGui(Camera cam, Vector3 world)
        {
            var sp = cam.WorldToScreenPoint(world);
            return new Vector3(sp.x / Scale, (Screen.height - sp.y) / Scale, sp.z);
        }

        static bool Repaint => Event.current != null && Event.current.type == EventType.Repaint;

        public static void Rect(float x, float y, float w, float h, Color c)
        {
            if (!Repaint) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, h), White);
            GUI.color = old;
        }

        public static void Rect(Rect r, Color c) => Rect(r.x, r.y, r.width, r.height, c);

        public static void Panel(Rect r, Color? fill = null, Color? border = null)
        {
            if (!Repaint) return;
            var old = GUI.color;
            var st = RoundStyle;
            if (border.HasValue)
            {
                GUI.color = border.Value;
                st.Draw(r, false, false, false, false);
                r = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
            }
            GUI.color = fill ?? new Color(0.05f, 0.08f, 0.2f, 0.8f);
            st.Draw(r, false, false, false, false);
            GUI.color = old;
        }

        public static void Circle(float x, float y, float r, Color c)
        {
            if (!Repaint) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x - r, y - r, r * 2, r * 2), CircleTex);
            GUI.color = old;
        }

        public static void Ring(float x, float y, float r, float width, Color c)
        {
            if (!Repaint || r <= 0) return;
            int key = Mathf.Clamp(Mathf.RoundToInt((1f - width / r) * 20f), 0, 19);
            if (!rings.TryGetValue(key, out var tex) || !tex) rings[key] = tex = MakeRing(128, key / 20f);
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x - r, y - r, r * 2, r * 2), tex);
            GUI.color = old;
        }

        public static void Line(float x1, float y1, float x2, float y2, float width, Color c)
        {
            if (!Repaint) return;
            float dx = x2 - x1, dy = y2 - y1, len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) return;
            var m = GUI.matrix;
            var old = GUI.color;
            GUI.color = c;
            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(ang, new Vector2(x1, y1) * Scale);
            GUI.DrawTexture(new Rect(x1, y1 - width / 2, len, width), White);
            GUI.matrix = m;
            GUI.color = old;
        }

        public static void Bar(Rect r, float frac, Color fill, Color? back = null)
        {
            Rect(r, back ?? new Color(0, 0, 0, 0.55f));
            Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height, fill);
        }

        public static void Texture(Rect r, Texture tex, Color? tint = null, ScaleMode mode = ScaleMode.ScaleToFit)
        {
            if (!Repaint || !tex) return;
            var old = GUI.color;
            GUI.color = tint ?? Color.white;
            GUI.DrawTexture(r, tex, mode);
            GUI.color = old;
        }

        public static Vector2 Measure(string s, float size)
        {
            var st = LabelStyle;
            st.fontSize = Mathf.Max(1, Mathf.RoundToInt(size));
            st.fontStyle = FontStyle.Bold;
            st.wordWrap = false;
            content.text = s;
            return st.CalcSize(content);
        }

        /// <summary>Text anchored at (x, y); ox/oy are anchor fractions (0.5,0.5 = centred). Optional outline.</summary>
        public static void Label(string s, float x, float y, float size, Color? color = null, float ox = 0.5f, float oy = 0.5f,
            Color? stroke = null, float maxWidth = 0f, bool bold = true)
        {
            if (string.IsNullOrEmpty(s) || !Repaint) return;
            var st = LabelStyle;
            st.fontSize = Mathf.Max(1, Mathf.RoundToInt(size));
            st.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            content.text = s;
            Vector2 sz;
            if (maxWidth > 0)
            {
                st.wordWrap = true;
                float h = st.CalcHeight(content, maxWidth);
                var one = st.CalcSize(content);
                sz = new Vector2(Mathf.Min(maxWidth, one.x), h);
                st.alignment = ox <= 0.01f ? TextAnchor.UpperLeft : ox >= 0.99f ? TextAnchor.UpperRight : TextAnchor.UpperCenter;
            }
            else
            {
                st.wordWrap = false;
                sz = st.CalcSize(content);
                st.alignment = TextAnchor.UpperLeft;
            }
            var r = new Rect(x - sz.x * ox, y - sz.y * oy, sz.x, sz.y);
            var col = color ?? Color.white;
            if (stroke.HasValue)
            {
                var sc = stroke.Value;
                sc.a *= col.a;
                st.normal.textColor = sc;
                float o = Mathf.Max(1.5f, size / 14f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f;
                    st.Draw(new Rect(r.x + Mathf.Cos(a) * o, r.y + Mathf.Sin(a) * o, r.width, r.height), content, false, false, false, false);
                }
            }
            st.normal.textColor = col;
            st.Draw(r, content, false, false, false, false);
        }

        public static bool Inside(Rect r, Vector2 p) => r.Contains(p);

        static int swallowFrame = -1;
        /// <summary>Ignore GUI clicks for the rest of this frame (call when switching screens so the
        /// click that caused the switch does not also press a button on the new screen).</summary>
        public static void Swallow() => swallowFrame = Time.frameCount;
        /// <summary>Set while a modal overlay is open; only code drawing inside the modal
        /// (between BeginModal/EndModal) receives clicks.</summary>
        public static bool ModalOpen;
        static bool inModal;
        public static void BeginModal() => inModal = true;
        public static void EndModal() => inModal = false;
        static bool Swallowed => Time.frameCount == swallowFrame || (ModalOpen && !inModal);

        /// <summary>A rounded button; returns true on the frame it is clicked.</summary>
        public static bool Button(Rect r, string text, float size = 24f, Color? color = null, Color? hover = null, bool selected = false, bool enabled = true)
        {
            var ev = Event.current;
            if (ev == null) return false;
            bool over = enabled && r.Contains(ev.mousePosition) && (!ModalOpen || inModal);
            if (ev.type == EventType.Repaint)
            {
                Color baseC = color ?? new Color(0.16f, 0.24f, 0.5f, 0.95f);
                Color hovC = hover ?? Color.Lerp(baseC, Color.white, 0.25f);
                Color fill = !enabled ? new Color(0.3f, 0.3f, 0.35f, 0.8f) : over ? hovC : baseC;
                Panel(r, fill, selected ? Js.Hex("#ffe066") : new Color(1, 1, 1, 0.35f));
                Label(text, r.x + r.width / 2, r.y + r.height / 2, size, enabled ? Color.white : new Color(1, 1, 1, 0.5f), 0.5f, 0.5f, new Color(0, 0, 0, 0.6f));
            }
            if (over && ev.type == EventType.MouseDown && ev.button == 0 && !Swallowed)
            {
                ev.Use();
                Sfx.Click();
                return true;
            }
            return false;
        }

        /// <summary>True when the left mouse went down inside r during this OnGUI event (no drawing).</summary>
        public static bool Clicked(Rect r)
        {
            var ev = Event.current;
            if (ev == null || ev.type != EventType.MouseDown || ev.button != 0 || !r.Contains(ev.mousePosition) || Swallowed) return false;
            ev.Use();
            return true;
        }

        public static bool Hover(Rect r) => Event.current != null && r.Contains(Event.current.mousePosition);

        /// <summary>A full-width translucent banner with a big line and an optional sub line.</summary>
        public static void Banner(string big, string sub = null, float y = -1, Color? col = null)
        {
            if (y < 0) y = H / 2;
            Rect(0, y - 64, W, 128, new Color(0.05f, 0.08f, 0.2f, 0.85f));
            Label(big, W / 2, y - (sub != null ? 14 : 0), 50, col ?? Js.Hex("#ffe066"), 0.5f, 0.5f, Color.black);
            if (sub != null) Label(sub, W / 2, y + 36, 24, Color.white);
        }

        /// <summary>Standard title card used by every game's title screen.</summary>
        public static void TitleCard(string title, string subtitle, string prompt, float t, Color? titleColor = null)
        {
            float cx = W / 2;
            // The original game logo (Phaser Game Agent art) when it has been imported.
            var logo = Art.Tex("logo");
            if (logo)
            {
                float lh = H * 0.3f, lw = lh * logo.width / Mathf.Max(1, logo.height);
                if (lw > W * 0.8f) { lw = W * 0.8f; lh = lw * logo.height / logo.width; }
                Texture(new Rect(cx - lw / 2, H * 0.02f, lw, lh), logo);
                Label(title, cx, H * 0.36f, 44, titleColor ?? Js.Hex("#fff6c8"), 0.5f, 0.5f, Js.Hex("#1b2a6b"));
                if (!string.IsNullOrEmpty(subtitle)) Label(subtitle, cx, H * 0.43f, 22, Color.white, 0.5f, 0.5f, Color.black, W * 0.8f);
            }
            else
            {
                Label(title, cx, H * 0.24f, 60, titleColor ?? Js.Hex("#fff6c8"), 0.5f, 0.5f, Js.Hex("#1b2a6b"));
                if (!string.IsNullOrEmpty(subtitle)) Label(subtitle, cx, H * 0.34f, 26, Color.white, 0.5f, 0.5f, Color.black, W * 0.8f);
            }
            if (Mathf.Sin(t * 5) > -0.3f) Label(prompt ?? "點擊或按 Space 開始", cx, H * 0.86f, 30, Js.Hex("#ffe066"), 0.5f, 0.5f, Js.Hex("#402000"));
            Label("G 遊戲指南／攻略　Esc 回到遊戲大廳", cx, H * 0.93f, 18, new Color(1, 1, 1, 0.7f));
            if (Button(new Rect(W - 190, 20, 170, 50), "遊戲指南", 20, Js.Hex("#2a6fdb"))) GuideBook.OpenCurrent();
        }
    }
}
