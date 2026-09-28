// 萌友卡牌對決 — 3D card table for the Game8 rules engine (title → duel → result). Minions are
// the characters standing on card bases; the hand, mana and heroes are drawn as HUD.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoeGames.Game8
{
    [MoeGame(8)]
    public class Game8Game : MiniGame
    {
        public override TouchLayout Touch => TouchLayout.TapOnly;

        protected override string Backdrop => "table";

        enum Scr { Title, Duel, Over }

        const int YOU = 0, CPU = 1;
        const float CW = 108, CH = 144;

        Scr scr;
        float t;
        int first;
        bool? resultWin;
        int resultTurns;
        Chibi[] lineup;

        Battle b;
        string sel;
        readonly Dictionary<string, Rect> rects = new Dictionary<string, Rect>();
        readonly Dictionary<string, Chibi> minionViews = new Dictionary<string, Chibi>();
        readonly Dictionary<string, Transform> bases = new Dictionary<string, Transform>();
        (string uid, Vector3 target, float t)? lunge;
        float cpuT, busy, bannerT, endT = -1;
        string banner = "";
        string hover;
        readonly List<(string uid, Rect r)> handRects = new List<(string, Rect)>();
        Rect endBtn;
        Transform table;

        protected override void Begin()
        {
            Sfx.Define("card", new Tone { Type = Wave.Noise, Duration = 0.08f, Volume = 0.25f, Filter = FilterType.Bandpass, FilterFreq = 2500, Q = 2 });
            Sfx.Define("play", Tone.Melody("C5 G5", 0.05f, Wave.Triangle, 0.3f));
            Sfx.Define("hit", Tone.Noise(0.12f, 0.5f, FilterType.Lowpass, 1400), Tone.Beep(220, 0.1f, Wave.Square, 0.25f, 90));
            Sfx.Define("heal", Tone.Melody("E5 G5 C6", 0.06f, Wave.Triangle, 0.3f));
            Sfx.Define("die", Tone.Beep(500, 0.35f, Wave.Square, 0.25f, 80));
            Sfx.Define("turn", Tone.Melody("G4 C5 E5", 0.08f, Wave.Square, 0.3f));
            Rig.Background(Js.Hex("#1a120c"));
            Sfx.MusicByName("music", 0.3f);
            GoTitle();
        }

        void GoTitle()
        {
            scr = Scr.Title;
            lineup = ShowLineup(Cast.Ids, "#3a2a18", "#c9a46a");
        }

        void GoDuel()
        {
            scr = Scr.Duel;
            ClearWorld();
            minionViews.Clear();
            bases.Clear();
            sel = null;
            endT = -1;
            // The table.
            table = Prim.Empty("Table", World).transform;
            Prim.Box(table, new Vector3(0, -0.15f, 0), new Vector3(16, 0.3f, 9), Js.Hex("#6a4a2a"));
            var felt = Prim.Box(table, new Vector3(0, 0.005f, 0), new Vector3(15, 0.02f, 8), Js.Hex("#2f6a3a"));
            Art.Apply(felt, "table", Vector2.one);
            Prim.Box(table, new Vector3(0, 0.02f, 0), new Vector3(13, 0.02f, 0.05f), Js.Hex("#ffe8a0"));
            Rig.Set(new Vector3(0, 9.5f, -6.5f), new Vector3(0, 0, 0.3f), 45f);
            b = new Battle(Rand.Default, first);
            first = 1 - first;
            Replay(b.Start());
        }

        void GoOver()
        {
            scr = Scr.Over;
            resultWin = b.Winner == -1 ? (bool?)null : b.Winner == YOU;
            resultTurns = b.Turn;
            var ids = resultWin == true ? new List<string> { "whale", "penguin", "glasses", "tshirt" } : new List<string> { "calico", "whitecat", "redcat", "sailor" };
            lineup = ShowLineup(ids, "#3a2a18", "#c9a46a", 1.4f, 1.6f);
            if (resultWin == true) Sfx.Notes("C5 E5 G5 C6 G5 C6:3", 0.1f, Wave.Square, 0.4f);
            else Sfx.Notes("G4 F4 E4 D4 C4:3", 0.14f, Wave.Triangle, 0.4f);
        }

        // ── world positions of board slots ──

        Vector3 SlotPos(int owner, int i, int count)
        {
            float x = (i - (count - 1) / 2f) * 2.1f;
            return new Vector3(x, 0.05f, owner == YOU ? -1.3f : 1.6f);
        }

        Vector3 HeroPos(int p) => p == YOU ? new Vector3(0, 0.05f, -3.4f) : new Vector3(0, 0.05f, 3.6f);

        Vector3 WorldOf(string id)
        {
            if (id == "H0") return HeroPos(YOU);
            if (id == "H1") return HeroPos(CPU);
            foreach (var p in b.Players)
            {
                int i = p.Board.FindIndex(m => m.Uid == id);
                if (i >= 0) return SlotPos(p.Id, i, p.Board.Count);
            }
            return Vector3.zero;
        }

        void Float(string id, string text, string color) => Popups.Add(WorldOf(id) + Vector3.up * 1.4f, text, Js.Hex(color), 22f, 1.2f, 0.8f);

        /// <summary>Turn rule events into feedback.</summary>
        void Replay(List<Ev> ev)
        {
            foreach (var e in ev)
            {
                switch (e.Type)
                {
                    case "turnStart":
                        {
                            bool mine = e.Player == YOU;
                            banner = mine ? "你的回合" : "對手回合";
                            bannerT = 1.1f;
                            Sfx.Play("turn");
                            if (!mine) cpuT = 1.4f;
                            break;
                        }
                    case "play": Sfx.Play("play"); break;
                    case "draw": if (e.Player == YOU) Sfx.Play("card"); break;
                    case "attack": lunge = (e.Attacker, WorldOf(e.Target), 0f); if (minionViews.TryGetValue(e.Attacker, out var av)) av.Act("attack", 0.4f); break;
                    case "damage":
                        Float(e.Target, "-" + e.Amount, "#ff6b6b");
                        Sfx.Play("hit");
                        Fx.Burst(WorldOf(e.Target) + Vector3.up, 12, Js.Hex("#ffb347"), 3f, 0.35f, 0.08f);
                        if (minionViews.TryGetValue(e.Target, out var hv)) hv.Act("hit", 0.3f);
                        break;
                    case "heal": if (e.Amount > 0) { Float(e.Target, "+" + e.Amount, "#5cffb0"); Sfx.Play("heal"); } break;
                    case "buff": Float(e.Target, "攻擊 +1", "#ffe066"); break;
                    case "ability":
                        {
                            var c = Cards.Get(e.CardId);
                            int k = c.Text.IndexOf('：');
                            Float(e.Source, k >= 0 ? c.Text.Substring(k + 1) : c.Text, "#ffe8a0");
                            if (minionViews.TryGetValue(e.Source, out var sv)) sv.Act("cast", 0.5f);
                            break;
                        }
                    case "death":
                        Fx.Burst(WorldOf(e.Target) + Vector3.up * 0.6f, 24, Js.Hex("#c9a46a"), 4f, 0.6f, 0.1f);
                        Sfx.Play("die");
                        break;
                    case "fatigue": Float(Battle.HeroId(e.Player), $"牌庫耗盡 -{e.Amount}", "#ff6b6b"); break;
                    case "burn": if (e.Player == YOU) Float(Battle.HeroId(YOU), "手牌滿了，卡被燒掉", "#ff9a3c"); break;
                    case "gameOver": endT = 0; break;
                }
            }
            busy = 0.35f;
            SyncBoard();
        }

        void SyncBoard()
        {
            var alive = new HashSet<string>();
            foreach (var p in b.Players)
                foreach (var m in p.Board)
                {
                    alive.Add(m.Uid);
                    if (!minionViews.ContainsKey(m.Uid))
                    {
                        var basePlate = Prim.Box(World, Vector3.zero, new Vector3(1.8f, 0.12f, 1.8f), Js.Hex(Cards.Get(m.CardId).Color)).transform;
                        bases[m.Uid] = basePlate;
                        var ch = SpawnChar(m.CardId, Vector3.zero, 1.4f);
                        ch.Face(p.Id == YOU ? Vector3.forward : Vector3.back);
                        ch.Act("hop", 0.4f);
                        minionViews[m.Uid] = ch;
                    }
                }
            foreach (var gone in minionViews.Keys.Where(k => !alive.Contains(k)).ToList())
            {
                Destroy(minionViews[gone].gameObject);
                Destroy(bases[gone].gameObject);
                minionViews.Remove(gone);
                bases.Remove(gone);
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
                    if (In.Confirm || In.MouseDown(0)) { Sfx.Notes("C5 E5 G5", 0.07f, Wave.Square, 0.3f); GoDuel(); }
                    break;
                case Scr.Duel: UpdateDuel(dt); break;
                case Scr.Over:
                    if (In.Confirm) GoDuel();
                    else if (In.Back) GoTitle();
                    if (lineup != null && Mathf.Repeat(t, 0.8f) < dt) foreach (var c in lineup) if (c) c.Act(resultWin == true ? "win" : "hop", 0.6f);
                    break;
            }
        }

        void UpdateDuel(float dt)
        {
            if (In.Back && endT < 0) { if (sel != null) sel = null; else { GoTitle(); return; } }
            if (bannerT > 0) bannerT -= dt;
            if (lunge.HasValue) { var l = lunge.Value; l.t += dt * 3; lunge = l.t >= 1 ? null : ((string, Vector3, float)?)l; }
            if (busy > 0) busy -= dt;
            PlaceViews();
            if (endT >= 0)
            {
                endT += dt;
                if (endT > 2) GoOver();
                return;
            }
            // CPU plays one action at a time.
            if (b.Current == CPU)
            {
                cpuT -= dt;
                if (cpuT <= 0 && busy <= 0)
                {
                    var a = Cpu.Choose(b, CPU);
                    Replay(Cpu.Apply(b, CPU, a));
                    cpuT = a.Type == ActionType.End ? 0 : 0.9f;
                }
                return;
            }
            if (In.Down(KeyCode.Space, KeyCode.E)) EndTurn();
        }

        void PlaceViews()
        {
            foreach (var p in b.Players)
                for (int i = 0; i < p.Board.Count; i++)
                {
                    var m = p.Board[i];
                    if (!minionViews.TryGetValue(m.Uid, out var v)) continue;
                    var pos = SlotPos(p.Id, i, p.Board.Count);
                    if (lunge.HasValue && lunge.Value.uid == m.Uid) pos += (lunge.Value.target - pos) * Mathf.Sin(lunge.Value.t * Mathf.PI) * 0.6f;
                    v.transform.localPosition = Vector3.Lerp(v.transform.localPosition, pos + Vector3.up * 0.12f, 0.3f);
                    bases[m.Uid].localPosition = new Vector3(pos.x, 0.06f, pos.z);
                    bool ready = p.Id == YOU && b.Current == YOU && b.CanAttack(YOU, m.Uid);
                    bool targetable = sel != null && b.ValidTargets(YOU, sel).Contains(m.Uid);
                    var col = sel == m.Uid ? Js.Hex("#ffe066") : targetable ? Js.Hex("#ff5f5f") : ready ? Js.Hex("#5cffb0") : m.Taunt ? Js.Hex("#c9c9c9") : Js.Hex(Cards.Get(m.CardId).Color);
                    Prim.SetColor(bases[m.Uid].gameObject, col, sel == m.Uid || targetable || ready);
                    v.SetLoop(m.Sleeping ? Chibi.Loop.None : Chibi.Loop.Idle);
                }
        }

        void EndTurn()
        {
            if (b.Current != YOU || busy > 0 || b.IsOver) return;
            sel = null;
            Replay(b.EndTurn(YOU));
        }

        void ClickCharacter(string hit)
        {
            if (sel != null && b.ValidTargets(YOU, sel).Contains(hit)) { Replay(b.Attack(YOU, sel, hit)); sel = null; return; }
            if (b.CanAttack(YOU, hit)) { sel = hit; Sfx.Play("card"); return; }
            if (sel != null && hit == Battle.HeroId(CPU) && b.Players[CPU].Board.Count > 0) Float(hit, "請先清除對手場上的卡牌", "#ffe066");
            else if (sel != null && b.Players[CPU].Board.Any(m => m.Uid == hit) && b.Players[CPU].Board.Any(m => m.Taunt)) Float(hit, "請先攻擊嘲諷角色", "#ffe066");
            sel = null;
        }

        // ── GUI ──

        void OnGUI()
        {
            Gui.Begin();
            float W = Gui.W, H = Gui.H, cx = W / 2;
            switch (scr)
            {
                case Scr.Title:
                    Gui.TitleCard("萌友卡牌對決", "八位萌友化身卡牌　法力曲線、嘲諷、衝鋒與登場效果", null, t, Js.Hex("#ffe8a0"));
                    break;
                case Scr.Duel: DrawDuel(W, H); break;
                case Scr.Over:
                    Gui.Label(resultWin == null ? "平手" : resultWin == true ? "勝利！" : "敗北…", cx, 100, 80, resultWin == true ? Js.Hex("#ffd23f") : Js.Hex("#ff8a80"), 0.5f, 0.5f, Color.black);
                    Gui.Label($"共 {resultTurns} 回合", cx, 180, 28, Color.white, 0.5f, 0.5f, Color.black);
                    if (Gui.Button(new Rect(cx - 250, H - 100, 230, 62), "再戰一場", 26, Js.Hex("#b8742a"), Js.Hex("#e0923a"))) GoDuel();
                    if (Gui.Button(new Rect(cx + 20, H - 100, 230, 62), "回到標題", 26)) GoTitle();
                    break;
            }
        }

        void DrawDuel(float W, float H)
        {
            if (b == null) return;
            float cx = W / 2;
            bool myTurn = b.Current == YOU && busy <= 0 && endT < 0;
            // Minion stats + click areas.
            rects.Clear();
            foreach (var p in b.Players)
                for (int i = 0; i < p.Board.Count; i++)
                {
                    var m = p.Board[i];
                    var pos = SlotPos(p.Id, i, p.Board.Count);
                    var top = Gui.WorldToGui(Cam, pos + Vector3.up * 1.6f);
                    var foot = Gui.WorldToGui(Cam, pos + new Vector3(0, 0, -0.9f));
                    var r = new Rect(top.x - 50, top.y, 100, foot.y - top.y);
                    rects[m.Uid] = r;
                    var c = Cards.Get(m.CardId);
                    Stat(r.x + 8, r.yMax - 8, m.Atk, "#e0a020", m.Atk > c.Atk ? "#5cffb0" : "#ffffff");
                    Stat(r.xMax - 8, r.yMax - 8, m.Hp, "#c03030", m.Hp < m.MaxHp ? "#ff9a9a" : "#ffffff");
                    if (m.Taunt) Gui.Label("嘲諷", r.center.x, r.y - 6, 13, Color.white, 0.5f, 0.5f, Color.black);
                    if (m.Sleeping && p.Id == YOU) Gui.Label("Zzz", r.xMax - 10, r.y + 6, 14, Js.Hex("#9fd2ff"), 0.5f, 0.5f, Color.black);
                    if (myTurn && Gui.Clicked(r)) ClickCharacter(m.Uid);
                }
            DrawHero(CPU, cx, 70);
            DrawHero(YOU, cx - 380, H - 92);
            if (myTurn && Gui.Clicked(new Rect(cx - 70, 10, 140, 120))) ClickCharacter("H1");
            // Enemy hand (backs) and decks.
            int eh = b.Players[CPU].Hand.Count;
            var back = Art.Tex("back");
            for (int i = 0; i < eh; i++)
            {
                var br = new Rect(W - 90 - i * 22, 12, 50, 68);
                if (back) Gui.Texture(br, back, null, ScaleMode.StretchToFill);
                else Gui.Panel(br, Js.Hex("#6a4a8a"), Js.Hex("#c9a46a"));
            }
            Gui.Label($"牌庫 {b.Players[CPU].Deck.Count}", W - 60, 96, 14, Color.white, 0.5f, 0.5f, Color.black);
            Gui.Label($"牌庫 {b.Players[YOU].Deck.Count}", W - 60, H - 20, 14, Color.white, 0.5f, 0.5f, Color.black);
            // My hand.
            var hand = b.Players[YOU].Hand;
            Gui.Label($"目前法力 {b.Players[YOU].Mana}/{b.Players[YOU].MaxMana}　｜　卡牌左上藍色數字是所需法力", cx, H - CH - 26, 18, Js.Hex("#9fd2ff"), 0.5f, 0.5f, Color.black);
            float hw = Mathf.Min(CW + 8, (W - 560) / Mathf.Max(1, hand.Count));
            float hx = cx - (hand.Count - 1) * hw / 2 + 60;
            handRects.Clear();
            hover = null;
            var mouse = Event.current.mousePosition;
            for (int i = hand.Count - 1; i >= 0; i--)
            {
                var r0 = new Rect(hx + i * hw - CW / 2, H - CH - 12, CW, CH);
                if (hover == null && r0.Contains(mouse)) hover = hand[i].Uid;
            }
            for (int i = 0; i < hand.Count; i++)
            {
                var hc = hand[i];
                var r = new Rect(hx + i * hw - CW / 2, H - CH - 12 + (hover == hc.Uid ? -40 : 0), CW, CH);
                handRects.Add((hc.Uid, r));
                DrawCard(Cards.Get(hc.CardId), r.x, r.y, 1, b.CanPlay(YOU, hc.Uid));
            }
            if (myTurn && hover != null && Gui.Clicked(handRects.First(h => h.uid == hover).r))
            {
                if (b.CanPlay(YOU, hover)) { Replay(b.PlayCard(YOU, hover)); sel = null; }
                else
                {
                    var own = b.Players[YOU];
                    var card = own.Hand.FirstOrDefault(c => c.Uid == hover);
                    string message = own.Board.Count >= RULES.BOARD_MAX ? "場上已滿" : card != null ? $"需要 {Cards.Get(card.CardId).Cost} 法力／目前 {own.Mana}" : "無法出牌";
                    Float(Battle.HeroId(YOU), message, "#9fd2ff");
                }
            }
            // End turn.
            endBtn = new Rect(W - 190, H / 2 - 30, 160, 56);
            bool mine = b.Current == YOU;
            bool noMoves = mine && !b.HasAnyAction(YOU);
            if (Gui.Button(endBtn, mine ? "結束回合" : "對手思考中…", 22, !mine ? Js.Hex("#555a70") : noMoves ? (Mathf.FloorToInt(t * 3) % 2 == 1 ? Js.Hex("#2f9e5a") : Js.Hex("#3fbf70")) : Js.Hex("#b8742a"), mine ? Js.Hex("#e0923a") : Js.Hex("#555a70")) && mine) EndTurn();
            if (hover != null)
            {
                var hc = hand.FirstOrDefault(h => h.Uid == hover);
                if (hc != null) DrawCard(Cards.Get(hc.CardId), W - 230, 110, 1.6f, b.CanPlay(YOU, hc.Uid));
            }
            if (sel != null) Gui.Label("選擇攻擊目標（Esc 取消）", cx, H * 0.47f, 16, Js.Hex("#ffe066"), 0.5f, 0.5f, Color.black);
            if (bannerT > 0)
            {
                float a = Mathf.Clamp01(bannerT * 3);
                Gui.Rect(0, H / 2 - 40, W, 80, Js.Hex("#1a2a3a", 0.8f * a));
                var c = Js.Hex("#ffe8a0");
                c.a = a;
                Gui.Label(banner, cx, H / 2, 44, c, 0.5f, 0.5f, Color.black);
            }
        }

        void DrawHero(int p, float x, float y)
        {
            var st = b.Players[p];
            string id = Battle.HeroId(p);
            var r = new Rect(x - 70, y - 60, 140, 120);
            bool targetable = sel != null && b.ValidTargets(YOU, sel).Contains(id);
            Gui.Panel(r, Js.Hex("#2a1c10", 0.93f), targetable ? Js.Hex("#ff5f5f") : Js.Hex("#c9a46a"));
            Gui.Label(p == YOU ? "你的生命" : "對手生命", x, y - 28, 22, Js.Hex("#ffe8a0"));
            Gui.Circle(x, y + 18, 30, Js.Hex("#b82e2e"));
            Gui.Ring(x, y + 18, 30, 3, Js.Hex("#ffe8a0"));
            Gui.Label(Mathf.Max(0, st.Hp).ToString(), x, y + 18, 26, Color.white);
            float mx = p == YOU ? x + 90 : x - 70 - 10 * 22 - 20, my = p == YOU ? y + 40 : y - 30;
            for (int i = 0; i < RULES.MAX_MANA; i++)
                Gui.Circle(mx + i * 22, my, 8, i < st.Mana ? Js.Hex("#39a8ff") : i < st.MaxMana ? Js.Hex("#1d4a7a") : new Color(1, 1, 1, 0.13f));
            Gui.Label($"法力 {st.Mana}/{st.MaxMana}", mx + 10 * 22 + 8, my, 16, Js.Hex("#9fd2ff"), 0f, 0.5f, Color.black);
        }

        void Stat(float x, float y, int v, string bg, string fg)
        {
            Gui.Circle(x, y, 15, Js.Hex(bg));
            Gui.Ring(x, y, 15, 2, Color.black);
            Gui.Label(v.ToString(), x, y, 17, Js.Hex(fg), 0.5f, 0.5f, Color.black);
        }

        void DrawCard(Card c, float x, float y, float s, bool playable)
        {
            float w = CW * s, h = CH * s;
            Gui.Rect(x - 3, y - 3, w + 6, h + 6, playable ? Js.Hex("#5cffb0") : Js.Hex("#6a4a2a"));
            Gui.Rect(x, y, w, h, Js.Hex("#f4e4c0"));
            Gui.Rect(x + 4 * s, y + 4 * s, w - 8 * s, h * 0.5f, Js.Hex(c.Color, 0.55f));
            var portrait = Art.Tex("cast_" + c.Id, "Shared");
            if (portrait) Gui.Texture(new Rect(x + 4 * s, y + 4 * s, w - 8 * s, h * 0.5f - 4 * s), portrait);
            else
            {
                Gui.Circle(x + w / 2, y + h * 0.28f, h * 0.18f, Js.Hex(c.Color));
                Gui.Label(Cast.Name(c.Id).Substring(0, 1), x + w / 2, y + h * 0.28f, 26 * s, Color.white, 0.5f, 0.5f, Color.black);
            }
            Gui.Rect(x, y + h * 0.53f, w, 18 * s, Js.Hex("#6a4a2a"));
            var parts = c.Name.Split(' ');
            Gui.Label(parts[parts.Length - 1], x + w / 2, y + h * 0.53f + 9 * s, 13 * s, Js.Hex("#ffe8a0"));
            Gui.Label(c.Text, x + w / 2, y + h * 0.68f, 10 * s, Js.Hex("#3a2a1a"), 0.5f, 0f, null, w - 12 * s);
            Gui.Circle(x + 12 * s, y + 12 * s, 13 * s, Js.Hex("#2a6fdb"));
            Gui.Label(c.Cost.ToString(), x + 12 * s, y + 12 * s, 15 * s, Color.white);
            Gui.Circle(x + 12 * s, y + h - 12 * s, 12 * s, Js.Hex("#e0a020"));
            Gui.Label(c.Atk.ToString(), x + 12 * s, y + h - 12 * s, 14 * s, Color.white, 0.5f, 0.5f, Color.black);
            Gui.Circle(x + w - 12 * s, y + h - 12 * s, 12 * s, Js.Hex("#c03030"));
            Gui.Label(c.Hp.ToString(), x + w - 12 * s, y + h - 12 * s, 14 * s, Color.white, 0.5f, 0.5f, Color.black);
        }
    }
}
