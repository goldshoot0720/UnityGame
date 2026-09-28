// Reusable "pick N characters" screen: GUI cards under a 3D lineup (from MiniGame.ShowLineup),
// mouse or arrow keys + Space to toggle, with the pick order shown as a numbered badge.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoeGames
{
    public class CastPicker
    {
        public readonly List<string> Picks = new List<string>();
        public readonly int Max;
        public int Cursor;
        readonly IList<string> ids;

        public CastPicker(IList<string> ids, int max) { this.ids = ids; Max = max; }

        public bool Ready => Picks.Count == Max;

        public void Toggle(int i, Chibi c = null)
        {
            string id = ids[i];
            int at = Picks.IndexOf(id);
            if (at >= 0) Picks.RemoveAt(at);
            else if (Picks.Count < Max) { Picks.Add(id); if (c) c.Act("hop", 0.35f); }
            else if (Max == 1) { Picks[0] = id; if (c) c.Act("hop", 0.35f); }
            Sfx.Beep(at >= 0 ? 440 : 700, 0.05f, Wave.Square, 0.2f);
        }

        /// <summary>Arrow keys move the cursor, Space toggles. Returns true when Enter confirms a full pick.</summary>
        public bool UpdateKeys(Chibi[] lineup)
        {
            if (In.RightDown) Cursor = (Cursor + 1) % ids.Count;
            if (In.LeftDown) Cursor = (Cursor + ids.Count - 1) % ids.Count;
            if (In.Down(KeyCode.Space)) Toggle(Cursor, lineup != null && Cursor < lineup.Length ? lineup[Cursor] : null);
            return Ready && In.Down(KeyCode.Return, KeyCode.KeypadEnter);
        }

        /// <summary>Draw one card per character under its model; <paramref name="content"/> fills the card.</summary>
        public void Draw(Camera cam, Chibi[] lineup, float cardH, Action<Rect, int> content, string pickColor = "#39c6ff")
        {
            float cw = Mathf.Min(150, Gui.W / (ids.Count + 0.6f));
            for (int i = 0; i < ids.Count && i < lineup.Length; i++)
            {
                if (!lineup[i]) continue;
                var foot = Gui.WorldToGui(cam, lineup[i].transform.position);
                var head = Gui.WorldToGui(cam, lineup[i].transform.position + Vector3.up * lineup[i].Height);
                var card = new Rect(foot.x - cw / 2, foot.y + 8, cw, cardH);
                var hit = new Rect(card.x, head.y, cw, card.yMax - head.y);
                int pk = Picks.IndexOf(ids[i]);
                bool hov = Gui.Hover(hit);
                if (hov) Cursor = i;
                bool cur = Cursor == i;
                Gui.Panel(card, pk >= 0 ? Js.Hex("#1d4a7a", 0.93f) : Js.Hex("#0d1433", 0.85f),
                    cur ? Js.Hex("#ffe066") : pk >= 0 ? Js.Hex(pickColor) : new Color(1, 1, 1, 0.3f));
                content(card, i);
                if (pk >= 0 && Max > 1)
                {
                    Gui.Circle(card.x + 14, card.y + 14, 13, Js.Hex(pickColor));
                    Gui.Label((pk + 1).ToString(), card.x + 14, card.y + 14, 16, Color.white);
                }
                if (Gui.Clicked(hit)) Toggle(i, lineup[i]);
            }
        }

        /// <summary>Label + 1..10 bar row used by stat cards.</summary>
        public static void Stat(Rect card, float y, string name, float v, float max = 10f, string col = "#ffd23f")
        {
            Gui.Label(name, card.x + 8, y, 12, Color.white, 0f, 0.5f);
            Gui.Bar(new Rect(card.x + 40, y - 4, card.width - 50, 8), v / max, Js.Hex(col));
        }
    }
}
