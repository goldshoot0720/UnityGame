// 萌友水球大作戰 — 3D arena for the Game12 match (title → select → rounds → result).
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoeGames.Game12
{
    [MoeGame(12)]
    public class Game12Game : MiniGame
    {
        public override TouchLayout Touch => new TouchLayout().Button("水球", KeyCode.Space).Button("針", KeyCode.E);

        enum Scr { Title, Select, Arena, Over }

        Scr scr;
        float t;
        string hero = "whale";
        CastPicker picker;
        Chibi[] lineup;
        Match m;
        List<MatchResult> results = new List<MatchResult>();
        int winner;
        Transform crates;
        int gridVersion = -1;
        readonly Dictionary<PlayerState, Chibi> views = new Dictionary<PlayerState, Chibi>();
        readonly Dictionary<PlayerState, GameObject> bubbles = new Dictionary<PlayerState, GameObject>();

        protected override void Begin()
        {
            Sfx.Define("place", Tone.Beep(300, 0.09f, Wave.Sine, 0.25f, 520));
            Sfx.Define("splash", new Tone { Type = Wave.Noise, Duration = 0.45f, Volume = 0.4f, Filter = FilterType.Bandpass, FilterFreq = 1400, FilterFreqEnd = 300, Q = 0.9f });
            Sfx.Define("trap", Tone.Melody("C6 G5 E5", 0.06f, Wave.Sine, 0.3f));
            Sfx.Define("popped", Tone.Melody("G5 C5 G4", 0.06f, Wave.Square, 0.3f));
            Sfx.Define("item", Tone.Melody("E6 G6 C7", 0.05f, Wave.Square, 0.22f));
            Sfx.Define("free", Tone.Melody("C5 E5 G5 C6", 0.05f, Wave.Triangle, 0.3f));
            Sfx.Define("go", Tone.Melody("C5 C5 C5 G5:2", 0.12f, Wave.Square, 0.3f));
            Rig.Background(Js.Hex("#2a6a8f"));
            Sfx.MusicByName("music", 0.28f);
            GoTitle();
        }

        void GoTitle() { scr = Scr.Title; lineup = ShowLineup(Cast.Ids, "#1a3a5a", "#ffd46a"); }

        void GoSelect()
        {
            scr = Scr.Select;
            var ids = Rules.HEROES.Select(h => h.Id).ToList();
            picker = new CastPicker(ids, 1);
            picker.Picks.Add(hero);
            picker.Cursor = ids.IndexOf(hero);
            lineup = ShowLineup(ids, "#1a3a5a", "#ffd46a", 1.35f, 1.4f);
        }

        void GoArena()
        {
            scr = Scr.Arena;
            m = new Match(hero);
            m.Sound += n => Sfx.Play(n);
            m.Float += (c, r, text, col) => Popups.Add(Cell3(c, r) + Vector3.up * 1.4f, text, Js.Hex(col), 20f, 1.4f, 0.6f);
            m.BoxBroken += (c, r) => Fx.Burst(Cell3(c, r) + Vector3.up * 0.4f, 10, Js.Hex("#c98a4a"), 3f, 0.5f, 0.12f);
            m.Popped += p => { Fx.Burst(Cell3(p.X, p.Y) + Vector3.up * 0.6f, 30, Js.Hex("#9fe3ff"), 5f, 0.7f, 0.12f); Rig.Shake(0.08f, 0.12f); };
            ClearWorld();
            views.Clear();
            bubbles.Clear();
            gridVersion = -1;
            var w = World;
            float cw = Rules.COLS, ch = Rules.ROWS;
            Prim.Box(w, new Vector3(cw / 2 - 0.5f, -0.3f, -(ch / 2 - 0.5f)), new Vector3(cw + 1, 0.4f, ch + 1), Js.Hex("#ffd46a"));
            for (int r = 0; r < Rules.ROWS; r++)
                for (int c = 0; c < Rules.COLS; c++)
                    Prim.Tile(w, Cell3(c, r) + Vector3.down * 0.1f, 1f, 1f, (r + c) % 2 == 1 ? Js.Hex("#bfeaa0") : Js.Hex("#aee08c"), 0.1f);
            // Pillars are fixed: little houses on odd/odd cells.
            for (int r = 1; r < Rules.ROWS; r += 2)
                for (int c = 1; c < Rules.COLS; c += 2)
                {
                    var p = Cell3(c, r);
                    if (Art.Standee(w, "house", p, 1.25f)) continue;
                    Prim.Box(w, p + Vector3.up * 0.4f, new Vector3(0.9f, 0.8f, 0.9f), Js.Hex("#f0e0c0"));
                    var roof = Prim.Box(w, p + Vector3.up * 0.95f, new Vector3(0.66f, 0.66f, 0.95f), Js.Hex("#e06a5a"));
                    roof.transform.localRotation = Quaternion.Euler(0, 0, 45);
                }
            crates = Prim.Empty("crates", w).transform;
            foreach (var pl in m.Ps)
            {
                views[pl] = SpawnChar(pl.Hero.Id, Cell3(pl.X, pl.Y), 1.1f);
                var bub = Prim.Sphere(w, Vector3.zero, 1.2f, Js.Hex("#9fe3ff"));
                Prim.SetColor(bub, Js.Hex("#9fe3ff"), true);
                bub.SetActive(false);
                bubbles[pl] = bub;
            }
            var center = new Vector3(cw / 2 - 0.5f, 0, -(ch / 2 - 0.5f));
            Rig.Set(center + new Vector3(0, 15.5f, -8.5f), center + new Vector3(0, 0, 0.6f), 45f);
        }

        void GoOver()
        {
            scr = Scr.Over;
            results = m.Results;
            winner = m.Winner ?? -1;
            lineup = ShowLineup(results.Select(r => r.Id).ToList(), "#1a3a5a", "#ffd46a", 1.5f, 1.6f);
            bool won = winner == 0;
            if (won) Sfx.Notes("C5 E5 G5 C6 G5 C6:3", 0.1f, Wave.Square, 0.4f);
            else Sfx.Notes("G4 F4 E4 D4 C4:3", 0.14f, Wave.Triangle, 0.4f);
        }

        static Vector3 Cell3(double c, double r) => new Vector3((float)c, 0, -(float)r);

        void RebuildCrates()
        {
            Prim.Clear(crates);
            for (int r = 0; r < Rules.ROWS; r++)
                for (int c = 0; c < Rules.COLS; c++)
                    if (m.Grid[r, c] == Cell.Box)
                    {
                        var p = Cell3(c, r);
                        if (Art.Standee(crates, "crate", p, 1.05f)) continue;
                        Prim.Box(crates, p + Vector3.up * 0.4f, new Vector3(0.86f, 0.8f, 0.86f), Js.Hex("#c98a4a"));
                        Prim.Box(crates, p + Vector3.up * 0.4f, new Vector3(0.9f, 0.1f, 0.9f), Js.Hex("#8a5a2a"));
                    }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            switch (scr)
            {
                case Scr.Title:
                    if (In.Back) { ExitToHub(); return; }
                    if (In.Confirm || In.MouseDown(0)) { Sfx.Notes("C5 E5 G5", 0.07f, Wave.Square, 0.3f); GoSelect(); }
                    break;
                case Scr.Select:
                    if (In.Back) { GoTitle(); return; }
                    if (picker.UpdateKeys(lineup)) StartMatch();
                    break;
                case Scr.Arena:
                    if (In.Back) { GoTitle(); return; }
                    var k = new MoveInput
                    {
                        Dx = (In.RightHeld ? 1 : 0) - (In.LeftHeld ? 1 : 0),
                        Dy = (In.DownHeld ? 1 : 0) - (In.UpHeld ? 1 : 0),
                        Drop = In.Down(KeyCode.Space),
                        Needle = In.Down(KeyCode.LeftShift, KeyCode.RightShift, KeyCode.E),
                    };
                    if (m.Update(dt, k)) { GoOver(); return; }
                    SyncViews(dt);
                    break;
                case Scr.Over:
                    if (In.Confirm) GoArena();
                    else if (In.Back) GoSelect();
                    if (lineup != null && lineup.Length > 0 && Mathf.Repeat(t, 0.8f) < dt) lineup[0].Act("win", 0.7f);
                    break;
            }
        }

        void StartMatch()
        {
            if (picker.Picks.Count > 0) hero = picker.Picks[0];
            Sfx.Notes("G4 C5 E5 G5", 0.06f, Wave.Square, 0.3f);
            GoArena();
        }

        void SyncViews(float dt)
        {
            if (gridVersion != m.GridVersion) { gridVersion = m.GridVersion; RebuildCrates(); }
            foreach (var p in m.Ps)
            {
                var v = views[p];
                bool show = !p.Dead || p.DeadT < 0.6f;
                v.gameObject.SetActive(show);
                var pos = Cell3(p.X, p.Y);
                var prev = v.transform.localPosition;
                v.transform.localPosition = pos + (p.Trapped > 0 ? Vector3.up * (0.4f + Mathf.Sin(t * 3) * 0.1f) : Vector3.zero);
                var vel = pos - new Vector3(prev.x, 0, prev.z);
                if (vel.sqrMagnitude > 1e-6f) v.FaceSmooth(vel, 14f, dt);
                v.SetLoop(vel.sqrMagnitude > 1e-6f && p.Trapped <= 0 ? Chibi.Loop.Run : Chibi.Loop.Idle, 1.3f);
                if (p.Dead && p.DeadT < 0.05f) v.KnockDown(true);
                if (!p.Dead) v.KnockDown(false);
                bubbles[p].SetActive(p.Trapped > 0 && !p.Dead);
                if (p.Trapped > 0)
                {
                    bubbles[p].transform.localPosition = pos + Vector3.up * 0.9f;
                    float wob = 1.25f + Mathf.Sin(t * 6) * 0.05f;
                    bubbles[p].transform.localScale = new Vector3(wob, wob * 1.05f, wob);
                }
            }
            foreach (var b in m.Balloons)
            {
                float pulse = 0.62f + Mathf.Sin(t * (8 + (2.6f - b.T) * 6)) * 0.05f;
                var owner = m.Ps[b.Owner];
                Instanced.Add(Cell3(b.C, b.R) + Vector3.up * 0.35f, Vector3.one * pulse, Js.Hex(owner.Color), true);
            }
            foreach (var kv in m.Water)
            {
                int c = kv.Key % Rules.COLS, r = kv.Key / Rules.COLS;
                float a = Mathf.Clamp01((float)kv.Value / Rules.WATER_TIME * 1.6f);
                Instanced.Add(Cell3(c, r) + Vector3.up * 0.15f, new Vector3(0.95f, 0.25f * a + 0.05f, 0.95f), Js.Hex("#3aa8ff"), true, true);
            }
            foreach (var kv in m.Items)
            {
                int c = kv.Key % Rules.COLS, r = kv.Key / Rules.COLS;
                var col = kv.Value.kind switch
                {
                    ItemKind.Balloon => "#39c6ff", ItemKind.Potion => "#7ee05a", ItemKind.Skate => "#ffc83a", ItemKind.Needle => "#ffb0d0", _ => "#ff7a9a",
                };
                Instanced.Add(Cell3(c, r) + Vector3.up * (0.45f + Mathf.Sin(t * 4 + c) * 0.08f), Vector3.one * 0.45f, Js.Hex(col), true, kv.Value.kind != ItemKind.Balloon, Quaternion.Euler(0, t * 120, 0));
            }
        }

        // ── GUI ──

        void OnGUI()
        {
            Gui.Begin();
            float W = Gui.W, H = Gui.H, cx = W / 2;
            switch (scr)
            {
                case Scr.Title:
                    Gui.TitleCard("萌友水球大作戰", "四人水球對戰　困住對手、戳破水泡　先贏兩回合獲勝", null, t, Js.Hex("#9fe3ff"));
                    break;
                case Scr.Select:
                    Gui.Label("選擇你的角色", cx, 44, 40, Color.white, 0.5f, 0.5f, Js.Hex("#1a3a5a"));
                    picker.Draw(Cam, lineup, 96, (r, i) =>
                    {
                        var h = Rules.HEROES[i];
                        Gui.Label(h.Name, r.center.x, r.y + 16, 18, Color.white);
                        CastPicker.Stat(r, r.y + 40, "水球", h.Balloons, 3, "#39c6ff");
                        CastPicker.Stat(r, r.y + 58, "水柱", h.Range, 3, "#7ee05a");
                        CastPicker.Stat(r, r.y + 76, "速度", h.Speed, 3, "#ffc83a");
                    }, "#ffd46a");
                    if (picker.Picks.Count > 0) hero = picker.Picks[0];
                    if (Gui.Button(new Rect(cx - 150, H - 70, 300, 56), "開戰！", 28, Js.Hex("#2a8fdf"))) StartMatch();
                    break;
                case Scr.Arena: DrawHud(W, H); break;
                case Scr.Over:
                    {
                        bool won = winner == 0;
                        Gui.Label(winner < 0 ? "平手！" : won ? "你是水球王！" : $"{Rules.HEROES.First(h => h.Id == results[0].Id).Name} 獲勝", cx, 70, 60, won ? Js.Hex("#ffe066") : Color.white, 0.5f, 0.5f, Js.Hex("#1a3a5a"));
                        var r0 = new Rect(W - 360, 150, 330, 34 * results.Count + 20);
                        Gui.Panel(r0);
                        for (int i = 0; i < results.Count; i++)
                        {
                            var r = results[i];
                            Gui.Label($"{i + 1}. {r.Name}{(r.You ? "（你）" : "")}", r0.x + 16, r0.y + 26 + i * 34, 18, r.You ? Js.Hex("#39c6ff") : Color.white, 0f, 0.5f);
                            Gui.Label($"{r.Wins} 勝", r0.xMax - 16, r0.y + 26 + i * 34, 18, Js.Hex("#ffe8a0"), 1f, 0.5f);
                        }
                        if (Gui.Button(new Rect(cx - 250, H - 100, 230, 62), "再戰一場", 26, Js.Hex("#2a8fdf"))) GoArena();
                        if (Gui.Button(new Rect(cx + 20, H - 100, 230, 62), "換角色", 26)) GoSelect();
                        break;
                    }
            }
        }

        void DrawHud(float W, float H)
        {
            float cx = W / 2;
            // Player cards.
            for (int i = 0; i < m.Ps.Count; i++)
            {
                var p = m.Ps[i];
                var r = new Rect(12, 12 + i * 70, 250, 62);
                Gui.Panel(r, p.Dead ? Js.Hex("#333344", 0.8f) : Js.Hex("#0d1433", 0.85f), Js.Hex(p.Color));
                Gui.Label($"{(p.You ? "你（" + p.Hero.Name + "）" : p.Hero.Name)}　{new string('★', p.Wins)}", r.x + 12, r.y + 18, 16, p.Dead ? Js.Hex("#888888") : Color.white, 0f, 0.5f);
                Gui.Label(p.Dead ? "出局" : p.Trapped > 0 ? $"被困住！{p.Trapped:F1}s" : $"水球 {p.Max}　水柱 {p.Range}　速度 {p.SpeedLv}　針 {p.Needles}", r.x + 12, r.y + 42, 13, p.Trapped > 0 ? Js.Hex("#9fe3ff") : Js.Hex("#ffe8a0"), 0f, 0.5f);
            }
            int mm = Mathf.Max(0, (int)(m.Clock / 60)), ss = Mathf.Max(0, (int)(m.Clock % 60));
            Gui.Panel(new Rect(cx - 110, 10, 220, 50));
            Gui.Label($"第 {m.Round} 回合　{mm}:{ss:00}", cx, 35, 22, Js.Hex("#ffe066"));
            if (m.Phase == RoundPhase.Intro) Gui.Banner($"第 {m.Round} 回合", "方向鍵移動　Space 放水球　Shift／E 用針脫困");
            if (m.Phase == RoundPhase.End) Gui.Banner(m.RoundMsg);
        }
    }
}
