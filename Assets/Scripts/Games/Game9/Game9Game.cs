// 萌友大富翁 — 3D board presentation of the Game9 turn machine (title → select → board → result).
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoeGames.Game9
{
    [MoeGame(9)]
    public class Game9Game : MiniGame
    {
        public override TouchLayout Touch => TouchLayout.TapOnly;

        enum Scr { Title, Select, Board, Over }

        const float CELL = 1.4f;
        const float PanelFrac = 0.34f;

        Scr scr;
        float t;
        string hero = "whale";
        CastPicker picker;
        Chibi[] lineup;
        Game g;
        List<Result> results = new List<Result>();
        string reason = "";
        readonly Dictionary<Player, Chibi> tokens = new Dictionary<Player, Chibi>();
        readonly Dictionary<Tile, Transform> buildings = new Dictionary<Tile, Transform>();
        readonly Dictionary<Tile, GameObject> ownerStrips = new Dictionary<Tile, GameObject>();
        readonly Dictionary<Tile, string> builtKey = new Dictionary<Tile, string>();
        Transform[] dice;
        int hover = -1;

        protected override void Begin()
        {
            Sfx.Define("dice", new Tone { Type = Wave.Noise, Duration = 0.25f, Volume = 0.25f, Filter = FilterType.Bandpass, FilterFreq = 2400, FilterFreqEnd = 900, Q = 2 });
            Sfx.Define("hop", Tone.Beep(520, 0.05f, Wave.Square, 0.12f, 780));
            Sfx.Define("coin", Tone.Melody("B5 E6", 0.06f, Wave.Square, 0.22f));
            Sfx.Define("pay", Tone.Melody("E5 C5", 0.07f, Wave.Triangle, 0.3f));
            Sfx.Define("build", Tone.Melody("C5 E5 G5 C6", 0.05f, Wave.Square, 0.25f));
            Sfx.Define("card", Tone.Melody("G5 A5 B5 D6", 0.05f, Wave.Triangle, 0.25f));
            Sfx.Define("bust", Tone.Melody("C4 B3 A#3 A3:3", 0.12f, Wave.Sawtooth, 0.25f));
            Rig.Background(Js.Hex("#123049"));
            Sfx.MusicByName("music", 0.3f);
            GoTitle();
        }

        void GoTitle() { scr = Scr.Title; lineup = ShowLineup(Cast.Ids, "#0a1d2e", "#ffd44a"); }

        void GoSelect()
        {
            scr = Scr.Select;
            var ids = Rules.HEROES.Select(h => h.Id).ToList();
            picker = new CastPicker(ids, 1);
            picker.Picks.Add(hero);
            picker.Cursor = ids.IndexOf(hero);
            lineup = ShowLineup(ids, "#0a1d2e", "#ffd44a", 1.35f, 1.4f);
        }

        void GoBoard()
        {
            scr = Scr.Board;
            g = new Game(hero);
            g.Sound += n => Sfx.Play(n);
            g.Float += (p, text, col) => { if (tokens.TryGetValue(p, out var c)) Popups.Add(c.transform.position + Vector3.up * 1.5f, text, Js.Hex(col), 24f, 1.6f, 0.8f); };
            g.Shake += () => Rig.Shake(0.15f, 0.3f);
            BuildBoard();
        }

        void GoOver()
        {
            scr = Scr.Over;
            results = g.Results;
            reason = g.Reason;
            lineup = ShowLineup(results.Select(r => r.Id).ToList(), "#0a1d2e", "#ffd44a", 1.5f, 1.6f);
            bool won = results.Count > 0 && results[0].You;
            if (won) Sfx.Notes("C5 E5 G5 C6 G5 C6:3", 0.1f, Wave.Square, 0.4f);
            else Sfx.Notes("G4 F4 E4 D4 C4:3", 0.14f, Wave.Triangle, 0.4f);
        }

        Vector3 TileCenter(int i)
        {
            var (gx, gy) = Rules.GridOf(i);
            return new Vector3((gx - 3.5f) * CELL, 0, (3.5f - gy) * CELL);
        }

        void BuildBoard()
        {
            ClearWorld();
            tokens.Clear(); buildings.Clear(); ownerStrips.Clear(); builtKey.Clear();
            Prim.Box(World, new Vector3(0, -0.2f, 0), new Vector3(CELL * 8 + 0.6f, 0.3f, CELL * 8 + 0.6f), Js.Hex("#ffd44a"));
            Prim.Box(World, new Vector3(0, -0.02f, 0), new Vector3(CELL * 6, 0.05f, CELL * 6), Js.Hex("#7fd0a8"));
            for (int i = 0; i < 6; i++) for (int j = 0; j < 6; j++) if ((i + j) % 2 == 1)
                Prim.Tile(World, new Vector3((i - 2.5f) * CELL, 0.01f, (j - 2.5f) * CELL), CELL, CELL, Js.Hex("#8adbb3"), 0.02f);
            foreach (var tl in g.Board)
            {
                var c = TileCenter(tl.I);
                string bg = tl.Kind switch
                {
                    Kind.Prop => "#fff7ea", Kind.Chance => "#fff0a8", Kind.Tax => "#dfe6f0", Kind.Start => "#ffd6e8", Kind.Rest => "#d6f5cf", Kind.Pot => "#ffe9a8", _ => "#ffc9c9",
                };
                Prim.Box(World, c + Vector3.up * 0.05f, new Vector3(CELL * 0.96f, 0.1f, CELL * 0.96f), Js.Hex(bg));
                if (tl.Kind == Kind.Prop)
                {
                    // District colour band on the inner edge.
                    var inward = -c.normalized;
                    var band = Prim.Box(World, c + inward * CELL * 0.36f + Vector3.up * 0.11f, new Vector3(CELL * 0.9f, 0.04f, CELL * 0.2f), Js.Hex(Rules.GROUPS[tl.Group].color));
                    band.transform.localRotation = Quaternion.LookRotation(inward);
                }
                else if (tl.Kind == Kind.Chance)
                {
                    var q = Prim.Box(World, c + Vector3.up * 0.45f, new Vector3(0.35f, 0.35f, 0.35f), Js.Hex("#ff8a00"));
                    q.transform.localRotation = Quaternion.Euler(45, 45, 0);
                }
                else if (tl.Kind == Kind.Pot) Prim.Cyl(World, c + Vector3.up * 0.25f, 0.7f, 0.3f, Js.Hex("#ffcf3a"));
                else if (tl.Kind == Kind.Jail) Prim.Box(World, c + Vector3.up * 0.4f, new Vector3(0.9f, 0.6f, 0.1f), Js.Hex("#4a4a6a"));
                else if (tl.Kind == Kind.Rest) { Prim.Cyl(World, c + Vector3.up * 0.2f, 0.25f, 0.4f, Js.Hex("#7a4a22")); Prim.Sphere(World, c + Vector3.up * 0.6f, 0.6f, Js.Hex("#2f7d32")); }
                else if (tl.Kind == Kind.Tax) Prim.Box(World, c + Vector3.up * 0.3f, new Vector3(0.5f, 0.4f, 0.5f), Js.Hex("#8a9ab0"));
            }
            for (int i = 0; i < g.Ps.Count; i++)
            {
                var p = g.Ps[i];
                var ch = SpawnChar(p.Hero.Id, TokenPos(p, i, p.Pos), 1.0f);
                tokens[p] = ch;
            }
            dice = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                dice[i] = Prim.Box(World, new Vector3(-0.7f + i * 1.4f, 0.5f, 0), Vector3.one * 0.8f, Color.white).transform;
                for (int k = 0; k < 6; k++)
                {
                    var n = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back }[k];
                    Prim.Box(dice[i], n * 0.51f, new Vector3(n.x != 0 ? 0.02f : 0.25f, n.y != 0 ? 0.02f : 0.25f, n.z != 0 ? 0.02f : 0.25f) / 0.8f, Js.Hex("#e0445a"));
                }
            }
            Rig.Viewport(new Rect(0, 0, 1f - PanelFrac, 1));
            float aspect = Screen.width * (1f - PanelFrac) / Mathf.Max(1f, Screen.height);
            float dist = Mathf.Max(13f, 12.5f / Mathf.Max(0.8f, aspect));
            Rig.Set(new Vector3(0, dist * 0.9f, -dist * 0.55f), new Vector3(0, 0, 0.3f), 42f);
        }

        Vector3 TokenPos(Player p, int i, int tile)
        {
            var c = TileCenter(tile);
            var off = new Vector3((i % 2 - 0.5f) * 0.55f, 0.1f, (i / 2 - 0.5f) * 0.45f);
            return c + off;
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
                    if (picker.UpdateKeys(lineup)) StartGame();
                    break;
                case Scr.Board:
                    if (In.Back) { GoTitle(); return; }
                    if (!g.P.Ai)
                    {
                        if (In.Confirm) Primary();
                        if (In.Down(KeyCode.N, KeyCode.Backspace) && g.Phase == Phase.Decide) g.Decide(false);
                    }
                    if (g.Update(dt)) { GoOver(); return; }
                    SyncViews(dt);
                    break;
                case Scr.Over:
                    if (In.Confirm) GoBoard();
                    else if (In.Back) GoSelect();
                    if (lineup != null && lineup.Length > 0 && Mathf.Repeat(t, 0.8f) < dt) lineup[0].Act("win", 0.7f);
                    break;
            }
        }

        void StartGame()
        {
            if (picker.Picks.Count > 0) hero = picker.Picks[0];
            Sfx.Notes("G4 C5 E5 G5", 0.06f, Wave.Square, 0.3f);
            GoBoard();
        }

        void Primary()
        {
            switch (g.Phase)
            {
                case Phase.Await: g.Roll(); break;
                case Phase.Decide: g.Decide(true); break;
                case Phase.Card: g.CardOk(); break;
                case Phase.End: g.EndTurnNow(); break;
            }
        }

        void SyncViews(float dt)
        {
            for (int i = 0; i < g.Ps.Count; i++)
            {
                var p = g.Ps[i];
                var ch = tokens[p];
                ch.gameObject.SetActive(p.Alive);
                Vector3 pos = TokenPos(p, i, p.Pos);
                if (i == g.Cur && g.Phase == Phase.Moving)
                {
                    var a = TokenPos(p, i, g.HopFrom);
                    var b = TokenPos(p, i, (g.HopFrom + g.Dir + Rules.BOARD_SIZE) % Rules.BOARD_SIZE);
                    float k = Mathf.Min(1, (float)g.HopT);
                    pos = Vector3.Lerp(a, b, k) + Vector3.up * Mathf.Sin(Mathf.PI * k) * 0.6f;
                    ch.Face(b - a);
                }
                else if (i == g.Cur && g.Phase == Phase.Await && Mathf.Repeat(t, 1f) < dt) ch.Act("hop", 0.4f);
                ch.transform.localPosition = pos;
            }
            // Buildings and owner strips.
            foreach (var tl in g.Board.Where(x => x.Kind == Kind.Prop))
            {
                string key = tl.Owner + ":" + tl.Level;
                if (builtKey.TryGetValue(tl, out var old) && old == key) continue;
                builtKey[tl] = key;
                if (buildings.TryGetValue(tl, out var bt)) { Destroy(bt.gameObject); buildings.Remove(tl); }
                if (ownerStrips.TryGetValue(tl, out var os)) { Destroy(os); ownerStrips.Remove(tl); }
                if (tl.Owner < 0) continue;
                var c = TileCenter(tl.I);
                var outward = c.normalized;
                var strip = Prim.Box(World, c + outward * CELL * 0.4f + Vector3.up * 0.11f, new Vector3(CELL * 0.9f, 0.05f, CELL * 0.14f), Js.Hex(g.Ps[tl.Owner].Color));
                strip.transform.localRotation = Quaternion.LookRotation(outward);
                ownerStrips[tl] = strip;
                if (tl.Level == 0) continue;
                var root = Prim.Empty("bld", World).transform;
                buildings[tl] = root;
                if (tl.Level >= Rules.MAX_LEVEL && Art.Standee(root, "hotel", c + Vector3.up * 0.1f, 1.3f)) continue;
                if (tl.Level < Rules.MAX_LEVEL && Art.Tex("house"))
                {
                    for (int k = 0; k < tl.Level; k++) Art.Standee(root, "house", c + new Vector3((k - (tl.Level - 1) / 2f) * 0.38f, 0.1f, -0.1f), 0.6f);
                    continue;
                }
                if (tl.Level >= Rules.MAX_LEVEL)
                {
                    Prim.Box(root, c + new Vector3(0, 0.55f, 0), new Vector3(0.6f, 0.9f, 0.6f), Js.Hex("#e0445a"));
                    Prim.Box(root, c + new Vector3(0, 1.05f, 0), new Vector3(0.7f, 0.1f, 0.7f), Js.Hex("#ffd44a"));
                }
                else
                    for (int k = 0; k < tl.Level; k++)
                    {
                        var hp = c + new Vector3((k - (tl.Level - 1) / 2f) * 0.38f, 0.28f, -0.1f);
                        Prim.Box(root, hp, new Vector3(0.28f, 0.28f, 0.28f), Js.Hex("#5fb85a"));
                        var roof = Prim.Box(root, hp + Vector3.up * 0.2f, new Vector3(0.22f, 0.22f, 0.3f), Js.Hex("#c0503a"));
                        roof.transform.localRotation = Quaternion.Euler(0, 0, 45);
                    }
            }
            // Dice.
            bool rolling = g.Phase == Phase.Rolling;
            for (int i = 0; i < 2; i++)
            {
                dice[i].localPosition = new Vector3(-0.7f + i * 1.4f, rolling ? 0.9f + Mathf.Abs(Mathf.Sin(t * 12 + i)) * 0.6f : 0.45f, 0);
                if (rolling) dice[i].localRotation = Quaternion.Euler(t * 720 + i * 90, t * 540, t * 360);
                else dice[i].localRotation = Quaternion.Slerp(dice[i].localRotation, Quaternion.identity, 0.2f);
            }
            // Hover tile.
            hover = -1;
            var ray = Cam.ScreenPointToRay(In.MousePos);
            if (Cam.pixelRect.Contains(In.MousePos) && new Plane(Vector3.up, Vector3.up * 0.1f).Raycast(ray, out float d))
            {
                var hp = ray.GetPoint(d);
                for (int i = 0; i < Rules.BOARD_SIZE; i++)
                {
                    var c = TileCenter(i);
                    if (Mathf.Abs(hp.x - c.x) < CELL / 2 && Mathf.Abs(hp.z - c.z) < CELL / 2) hover = i;
                }
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
                    Gui.TitleCard("萌友大富翁", "擲骰子、買地蓋房、收過路費　20 回合後最富有的人獲勝", null, t, Js.Hex("#ffe066"));
                    break;
                case Scr.Select:
                    Gui.Label("選擇你的角色", cx, 44, 40, Color.white, 0.5f, 0.5f, Js.Hex("#0a1d2e"));
                    picker.Draw(Cam, lineup, 80, (r, i) =>
                    {
                        var h = Rules.HEROES[i];
                        Gui.Label(h.Name, r.center.x, r.y + 16, 18, Color.white);
                        Gui.Label(h.PerkText, r.center.x, r.y + 36, 12, Js.Hex("#ffe066"), 0.5f, 0f, null, r.width - 10);
                    }, "#ffd44a");
                    if (picker.Picks.Count > 0) hero = picker.Picks[0];
                    if (Gui.Button(new Rect(cx - 150, H - 70, 300, 56), "開始遊戲！", 28, Js.Hex("#e0567f"), Js.Hex("#ff7aa0"))) StartGame();
                    break;
                case Scr.Board: DrawPanel(W, H); break;
                case Scr.Over: DrawOver(W, H); break;
            }
        }

        void DrawPanel(float W, float H)
        {
            // Tile labels.
            foreach (var tl in g.Board)
            {
                var sp = Gui.WorldToGui(Cam, TileCenter(tl.I) + Vector3.up * 0.15f);
                if (sp.z < 0) continue;
                string top = tl.Kind == Kind.Prop ? tl.Name : tl.Name;
                string sub = tl.Kind == Kind.Prop ? (tl.Owner >= 0 ? $"過路 ${Rules.RentOf(tl, g.Board)}" : $"${tl.Price}") : tl.Kind == Kind.Pot ? $"${g.Pot}" : tl.Kind == Kind.Tax ? "$150" : "";
                Gui.Label(top, sp.x, sp.y - 8, 12, Js.Hex("#3a2a20"), 0.5f, 0.5f, Color.white);
                if (sub != "") Gui.Label(sub, sp.x, sp.y + 8, 11, tl.Owner >= 0 ? Js.Hex("#7a4a20") : Js.Hex("#2a7a4a"), 0.5f, 0.5f, Color.white);
            }
            var dsp = Gui.WorldToGui(Cam, new Vector3(0, 1.4f, 0));
            if (g.Phase != Phase.Rolling) Gui.Label($"{g.Dice[0]} + {g.Dice[1]}", dsp.x, dsp.y, 26, Color.white, 0.5f, 0.5f, Color.black);
            // Right panel.
            float px = W * (1 - PanelFrac);
            Gui.Rect(px, 0, W - px, H, Js.Hex("#123049"));
            float x = px + 14, pw = W - px - 28, y = 14;
            Gui.Panel(new Rect(x, y, pw, 44));
            Gui.Label($"第 {g.Round} / {Rules.MAX_ROUNDS} 回合　幸運池 ${g.Pot}", x + pw / 2, y + 22, 18, Js.Hex("#ffe066"));
            y += 54;
            for (int i = 0; i < g.Ps.Count; i++)
            {
                var p = g.Ps[i];
                var r = new Rect(x, y, pw, 52);
                Gui.Panel(r, i == g.Cur ? Js.Hex("#1d4a7a", 0.95f) : Js.Hex("#0d1433", 0.85f), Js.Hex(p.Color));
                Gui.Circle(x + 24, y + 26, 16, Js.Hex(p.Hero.Color));
                Gui.Label(p.Name.Substring(0, 1), x + 24, y + 26, 16, Color.white, 0.5f, 0.5f, Color.black);
                Gui.Label($"{Game.Pn(p)}{(p.Alive ? "" : "（破產）")}{(p.Skip > 0 ? "（暫停）" : "")}", x + 48, y + 16, 15, p.Alive ? Color.white : Js.Hex("#888888"), 0f, 0.5f);
                Gui.Label($"現金 ${p.Cash}　資產 ${Rules.Worth(p, g.Board)}　地 {g.Board.Count(tl => tl.Owner == p.Id)}", x + 48, y + 36, 13, Js.Hex("#ffe8a0"), 0f, 0.5f);
                y += 58;
            }
            // Hover info.
            if (hover >= 0)
            {
                var tl = g.Board[hover];
                string info = tl.Kind == Kind.Prop
                    ? $"{tl.Name}（{Rules.GROUPS[tl.Group].name}）地價 ${tl.Price}｜{(tl.Owner >= 0 ? $"地主 {g.Ps[tl.Owner].Name}｜等級 {tl.Level}｜過路費 ${Rules.RentOf(tl, g.Board)}" : "無主")}"
                    : tl.Name;
                Gui.Label(info, x, y + 6, 13, Js.Hex("#9fd2ff"), 0f, 0f, null, pw);
                y += 34;
            }
            // Log.
            Gui.Panel(new Rect(x, y, pw, 190));
            var log = g.Log;
            float ly = y + 10;
            for (int i = Mathf.Max(0, log.Count - 8); i < log.Count; i++)
            {
                Gui.Label(log[i], x + 10, ly, 13, i == log.Count - 1 ? Color.white : new Color(1, 1, 1, 0.65f), 0f, 0f, null, pw - 20);
                ly += 22;
            }
            y += 200;
            // Buttons.
            if (!g.P.Ai)
            {
                var r = new Rect(x, H - 74, pw, 56);
                switch (g.Phase)
                {
                    case Phase.Await: if (Gui.Button(r, "擲骰子（Space）", 24, Js.Hex("#e0567f"))) g.Roll(); break;
                    case Phase.Decide:
                        {
                            var o = g.Offer;
                            string what = o.Buy ? $"買下 {o.Tile.Name}（${o.Cost}）" : $"加蓋（${o.Cost}）";
                            if (Gui.Button(new Rect(x, H - 74, pw * 0.62f, 56), what, 18, Js.Hex("#2f9e5a"))) g.Decide(true);
                            if (Gui.Button(new Rect(x + pw * 0.64f, H - 74, pw * 0.36f, 56), "不要（N）", 18, Js.Hex("#8a2a3a"))) g.Decide(false);
                            break;
                        }
                    case Phase.End: if (Gui.Button(r, "結束回合（Space）", 22, Js.Hex("#b8742a"))) g.EndTurnNow(); break;
                }
            }
            else Gui.Label($"{g.P.Name} 行動中…", x + pw / 2, H - 46, 20, Js.Hex("#cccccc"));
            // Chance card.
            if (g.Card != null)
            {
                float bw = Mathf.Min(520, px - 40), bx = px / 2 - bw / 2, by = H / 2 - 110;
                Gui.Panel(new Rect(bx, by, bw, 220), Js.Hex("#fff0a8", 0.98f), Js.Hex("#ff8a00"));
                Gui.Label(g.Board[g.P.Pos].Name, bx + bw / 2, by + 36, 30, Js.Hex("#b06a00"));
                Gui.Label(g.Card.Text, bx + bw / 2, by + 80, 22, Js.Hex("#3a2a20"), 0.5f, 0f, null, bw - 40);
                if (!g.P.Ai && Gui.Button(new Rect(bx + bw / 2 - 80, by + 160, 160, 44), "確定（Space）", 18)) g.CardOk();
            }
        }

        void DrawOver(float W, float H)
        {
            float cx = W / 2;
            int place = results.FindIndex(r => r.You) + 1;
            Gui.Label(place == 1 ? "你是大富翁！" : $"第 {place} 名", cx, 50, 50, place == 1 ? Js.Hex("#ffe066") : Color.white, 0.5f, 0.5f, Js.Hex("#0a1d2e"));
            Gui.Label(reason, cx, 100, 22, Color.white, 0.5f, 0.5f, Color.black);
            var r0 = new Rect(W - 380, 150, 350, 34 * results.Count + 20);
            Gui.Panel(r0);
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                var col = r.You ? Js.Hex("#39c6ff") : Color.white;
                Gui.Label($"{i + 1}. {r.Name}{(r.You ? "（你）" : "")}{(r.Alive ? "" : "（破產）")}", r0.x + 16, r0.y + 26 + i * 34, 17, col, 0f, 0.5f);
                Gui.Label($"${r.Worth}　{r.Props} 塊地", r0.xMax - 16, r0.y + 26 + i * 34, 17, Js.Hex("#ffe8a0"), 1f, 0.5f);
            }
            if (Gui.Button(new Rect(cx - 250, H - 100, 230, 62), "再玩一局", 26, Js.Hex("#e0567f"))) GoBoard();
            if (Gui.Button(new Rect(cx + 20, H - 100, 230, 62), "換角色", 26)) GoSelect();
        }
    }
}
