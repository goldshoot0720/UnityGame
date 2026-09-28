// In-game guide / strategy book (遊戲指南・攻略). Each game provides a GameGuide subclass in its
// own folder; the book opens from the hub, from any title screen, or with G, pauses the game
// (timeScale 0) and blocks game input while open. The same text exports to Docs/Guides/GameN.md.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MoeGames
{
    public abstract class GameGuide
    {
        public abstract int Number { get; }
        /// <summary>Tabs: (title, body). Body lines starting with "・" are bullets, "■" are sub-headings.</summary>
        public abstract (string title, string body)[] Sections { get; }

        public string ToMarkdown()
        {
            var info = Catalog.Get(Number);
            var sb = new StringBuilder();
            sb.Append("# ").Append(info?.Title ?? ("Game" + Number)).Append(" 遊戲指南・攻略\n\n");
            if (info != null) sb.Append("> ").Append(info.Genre).Append("｜").Append(info.Blurb).Append("\n\n");
            foreach (var (title, body) in Sections)
            {
                sb.Append("## ").Append(title).Append("\n\n");
                foreach (var raw in body.Split('\n'))
                {
                    string line = raw.TrimEnd();
                    if (line.StartsWith("■")) sb.Append("### ").Append(line.Substring(1).Trim()).Append('\n');
                    else if (line.StartsWith("・")) sb.Append("- ").Append(line.Substring(1).Trim()).Append('\n');
                    else sb.Append(line).Append('\n');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    public static class GuideBook
    {
        static readonly Dictionary<int, GameGuide> guides = new Dictionary<int, GameGuide>();
        static bool scanned;
        public static bool IsOpen { get; private set; }
        static GameGuide open;
        static int tab;
        static float scroll;
        static float savedScale = 1f;

        public static GameGuide Get(int n)
        {
            if (!scanned)
            {
                scanned = true;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string an = asm.GetName().Name;
                    if (!an.StartsWith("MoeGames.Game") && an != "Assembly-CSharp") continue;
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                    foreach (var t in types)
                    {
                        if (t == null || t.IsAbstract || !typeof(GameGuide).IsAssignableFrom(t)) continue;
                        try
                        {
                            var g = (GameGuide)Activator.CreateInstance(t);
                            guides[g.Number] = g;
                        }
                        catch { /* skip broken guide */ }
                    }
                }
            }
            return guides.TryGetValue(n, out var gg) ? gg : null;
        }

        public static bool Has(int n) => Get(n) != null;

        public static void Open(int n)
        {
            var g = Get(n);
            if (g == null) { App.I?.Toast("這款遊戲的指南還在撰寫中"); return; }
            open = g;
            tab = 0;
            scroll = 0;
            if (!IsOpen) savedScale = Time.timeScale;
            IsOpen = true;
            In.Blocked = true;
            Gui.ModalOpen = true;
            Time.timeScale = 0f;
            Gui.Swallow();
        }

        /// <summary>Open the guide of the running game (or the focused hub card).</summary>
        public static void OpenCurrent()
        {
            var app = App.I;
            if (app == null) return;
            Open(app.Current ? app.Current.Number : app.HubFocus + 1);
        }

        public static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            open = null;
            In.Blocked = false;
            Gui.ModalOpen = false;
            Time.timeScale = savedScale <= 0 ? 1f : savedScale;
            Gui.Swallow();
        }

        /// <summary>Called by App every frame (raw input, since game input is blocked).</summary>
        internal static void Tick()
        {
            if (!IsOpen)
            {
                if (In.RawDown(KeyCode.G)) OpenCurrent();
                return;
            }
            if (In.RawDown(KeyCode.Escape) || In.RawDown(KeyCode.G) || TouchPad.Down(KeyCode.Escape)) { Close(); return; }
            int n = open.Sections.Length;
            if (In.RawDown(KeyCode.RightArrow) || In.RawDown(KeyCode.D) || In.RawDown(KeyCode.Tab)) { tab = (tab + 1) % n; scroll = 0; }
            if (In.RawDown(KeyCode.LeftArrow) || In.RawDown(KeyCode.A)) { tab = (tab + n - 1) % n; scroll = 0; }
            float dt = Time.unscaledDeltaTime;
            if (In.RawHeld(KeyCode.DownArrow) || In.RawHeld(KeyCode.S)) scroll += 500 * dt;
            if (In.RawHeld(KeyCode.UpArrow) || In.RawHeld(KeyCode.W)) scroll -= 500 * dt;
            scroll -= In.RawScroll * 40f;
            scroll = Mathf.Max(0, scroll);
        }

        internal static void Draw()
        {
            if (!IsOpen || open == null) return;
            Gui.BeginModal();
            float W = Gui.W, H = Gui.H;
            Gui.Rect(0, 0, W, H, new Color(0.02f, 0.03f, 0.08f, 0.88f));
            var info = Catalog.Get(open.Number);
            float pw = Mathf.Min(1100, W - 40), px = (W - pw) / 2, py = 20, ph = H - 40;
            Gui.Panel(new Rect(px, py, pw, ph), Js.Hex("#101830", 0.97f), Js.Hex(info?.Color ?? "#ffe066"));
            Gui.Label($"{info?.Title}　遊戲指南・攻略", px + 24, py + 30, 30, Js.Hex("#ffe066"), 0f, 0.5f, Color.black);
            if (Gui.Button(new Rect(px + pw - 130, py + 12, 110, 40), "關閉 Esc", 18, Js.Hex("#8a2a3a"))) { Gui.EndModal(); Close(); return; }
            // Tabs.
            var secs = open.Sections;
            float tx = px + 20, ty = py + 64;
            for (int i = 0; i < secs.Length; i++)
            {
                float tw = Mathf.Max(90, Gui.Measure(secs[i].title, 18).x + 30);
                if (Gui.Button(new Rect(tx, ty, tw, 40), secs[i].title, 18, i == tab ? Js.Hex("#2a6fdb") : Js.Hex("#26304f"), null, i == tab)) { tab = i; scroll = 0; }
                tx += tw + 8;
                if (tx > px + pw - 120 && i < secs.Length - 1) { tx = px + 20; ty += 46; }
            }
            // Body.
            float bx = px + 30, by = ty + 60, bw = pw - 60, bh = py + ph - by - 36;
            GUI.BeginGroup(new Rect(bx, by, bw, bh));
            float y = -scroll;
            foreach (var raw in secs[tab].body.Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0) { y += 12; continue; }
                bool head = line.StartsWith("■");
                bool bullet = line.StartsWith("・");
                string text = head ? line.Substring(1).Trim() : line;
                float size = head ? 22 : 18;
                float indent = bullet ? 16 : 0;
                var sz = Gui.Measure(text, size);
                int rows = Mathf.Max(1, Mathf.CeilToInt(sz.x / (bw - indent - 10)));
                float h = rows * (size + 8);
                if (y + h > 0 && y < bh)
                    Gui.Label(text, indent, y, size, head ? Js.Hex("#9fd2ff") : Color.white, 0f, 0f, null, bw - indent - 10, head);
                y += h + (head ? 6 : 2);
            }
            GUI.EndGroup();
            float total = y + scroll;
            if (scroll > Mathf.Max(0, total - bh)) scroll = Mathf.Max(0, total - bh);
            Gui.Label("← → 切換分頁　↑ ↓／滾輪 捲動　Esc／G 關閉", W / 2, py + ph - 16, 15, new Color(1, 1, 1, 0.6f));
            Gui.EndModal();
        }
    }
}
