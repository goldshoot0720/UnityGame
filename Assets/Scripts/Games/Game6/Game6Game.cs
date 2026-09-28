// 萌友戰棋・八方對決 — 3D presentation of Game6 (title → select → battle → result).
// Mirrors scenes/battle.ts: player phase (select → move → act) and an animated enemy phase.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoeGames.Game6
{
    [MoeGame(6)]
    public class Game6Game : MiniGame
    {
        public override TouchLayout Touch => TouchLayout.TapOnly.Button("取消", KeyCode.Mouse1).Button("結束", KeyCode.E);

        protected override string Backdrop => "bg";

        enum Scr { Title, Select, Battle, Over }
        enum Mode { Idle, Moving, Menu, Target, Busy, Over }

        Scr screen;
        float t;

        // session
        readonly List<string> team = new List<string>();
        int mapIndex;
        bool resultWin;
        int resultTurns;
        List<string> survivors = new List<string>();

        // select
        readonly List<string> picks = new List<string>();
        Chibi[] lineup;

        // battle
        Board b;
        Mode mode;
        Unit sel;
        OrderedMap<string, Node> reach;
        (int x, int y) origin;
        char phase = 'P';
        int turn = 1;
        string targetKind = "atk";
        readonly Dictionary<Unit, Chibi> views = new Dictionary<Unit, Chibi>();
        readonly Dictionary<Unit, GameObject> rings = new Dictionary<Unit, GameObject>();
        Transform overlay;
        (int x, int y)? hover;
        Unit animU;
        Vector3 animPos;
        Unit bumpU;
        Vector3 bumpDir;
        float bumpT;
        string bannerText;
        float bannerT;
        Color bannerCol;
        string hint = "";
        struct MenuItem { public Rect r; public string text; public System.Action fn; public bool enabled; }
        readonly List<MenuItem> menu = new List<MenuItem>();
        Rect endBtn;
        const float PanelFrac = 0.34f;

        protected override void Begin()
        {
            Sfx.Define("sel", Tone.Beep(880, 0.05f, Wave.Square, 0.2f));
            Sfx.Define("step", Tone.Beep(300, 0.04f, Wave.Triangle, 0.15f));
            Sfx.Define("hit", Tone.Noise(0.12f, 0.5f, FilterType.Lowpass, 1500), Tone.Beep(200, 0.1f, Wave.Square, 0.25f, 90));
            Sfx.Define("crit", Tone.Melody("C6 G6", 0.05f, Wave.Square, 0.3f));
            Sfx.Define("heal", Tone.Melody("E5 G5 C6", 0.07f, Wave.Triangle, 0.3f));
            Sfx.Define("ko", Tone.Beep(400, 0.5f, Wave.Square, 0.3f, 60));
            Sfx.Define("phase", Tone.Melody("C5 E5 G5", 0.08f, Wave.Square, 0.3f));
            Sfx.MusicByName("music", 0.3f);
            Rig.Background(Js.Hex("#1a120c"));
            GoTitle();
        }

        // ── screens ──

        void GoTitle()
        {
            screen = Scr.Title;
            StopAllCoroutines();
            lineup = ShowLineup(Cast.Ids, "#3a2a18", "#c9a46a");
            for (int i = 4; i < lineup.Length; i++) lineup[i].Face(new Vector3(-0.4f, 0, -1));
        }

        void GoSelect()
        {
            screen = Scr.Select;
            picks.Clear();
            lineup = ShowLineup(Cast.Ids, "#3a2a18", "#c9a46a", 1.35f, 1.4f);
        }

        void GoBattle()
        {
            screen = Scr.Battle;
            StopAllCoroutines();
            ClearWorld();
            views.Clear();
            rings.Clear();
            var enemy = Cast.Ids.Where(k => !team.Contains(k)).Take(4).ToList();
            b = Board.Create(Tactics.MAPS[Mathf.Clamp(mapIndex, 0, Tactics.MAPS.Length - 1)], team, enemy);
            BuildBoard();
            turn = 1;
            sel = null;
            reach = null;
            menu.Clear();
            StartCoroutine(StartPlayerPhase());
        }

        void GoOver()
        {
            screen = Scr.Over;
            var ids = survivors.Count > 0 ? survivors : new List<string>(resultWin ? team : Cast.Ids.Where(k => !team.Contains(k)).Take(4));
            lineup = ShowLineup(ids, "#3a2a18", "#c9a46a", 1.5f, 1.6f);
            if (resultWin) Sfx.Notes("C5 E5 G5 C6 G5 C6:3", 0.1f, Wave.Square, 0.4f);
            else Sfx.Notes("G4 F4 E4 D4 C4:3", 0.14f, Wave.Triangle, 0.4f);
        }

        // ── board building ──

        static float TopOf(char ch) => ch == 'M' ? 0.55f : ch == 'W' ? 0.02f : 0.12f;

        Vector3 TilePos(float x, float y)
        {
            int ix = Mathf.Clamp(Mathf.RoundToInt(x), 0, Tactics.COLS - 1), iy = Mathf.Clamp(Mathf.RoundToInt(y), 0, Tactics.ROWS - 1);
            return new Vector3(x + 0.5f, TopOf(b.Map.Rows[iy][ix]), (Tactics.ROWS - 1 - y) + 0.5f);
        }

        void BuildBoard()
        {
            var board = Prim.Empty("Board", World).transform;
            Prim.Box(board, new Vector3(Tactics.COLS / 2f, -0.25f, Tactics.ROWS / 2f), new Vector3(Tactics.COLS + 0.4f, 0.5f, Tactics.ROWS + 0.4f), Js.Hex("#2a1c10"));
            var grass = Js.Hex("#6fbf4a");
            var grass2 = Js.Hex("#63b140");
            for (int y = 0; y < Tactics.ROWS; y++)
                for (int x = 0; x < Tactics.COLS; x++)
                {
                    char ch = b.Map.Rows[y][x];
                    var c = new Vector3(x + 0.5f, 0, (Tactics.ROWS - 1 - y) + 0.5f);
                    if (ch == 'W')
                    {
                        var wt = Prim.Box(board, c + Vector3.up * -0.02f, new Vector3(0.98f, 0.06f, 0.98f), Js.Hex("#3f8fe0"));
                        Art.Apply(wt, "water", Vector2.one);
                        continue;
                    }
                    var gt = Prim.Box(board, c + Vector3.up * 0.06f, new Vector3(0.98f, 0.12f, 0.98f), (x + y) % 2 == 0 ? grass : grass2);
                    Art.Apply(gt, "grass", Vector2.one);
                    if (ch != '.' && Art.Standee(board, ch == 'F' ? "forest" : ch == 'M' ? "mountain" : "house", c + Vector3.up * 0.12f, ch == 'M' ? 1.1f : 0.9f)) continue;
                    if (ch == 'F')
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            var o = c + new Vector3(k == 0 ? -0.2f : 0.22f, 0.12f, k == 0 ? 0.18f : -0.15f);
                            Prim.Cyl(board, o + Vector3.up * 0.12f, 0.08f, 0.24f, Js.Hex("#7a4a22"));
                            Prim.Sphere(board, o + Vector3.up * 0.38f, 0.38f, Js.Hex("#2f7d32"));
                        }
                    }
                    else if (ch == 'M')
                    {
                        Prim.Box(board, c + Vector3.up * 0.3f, new Vector3(0.96f, 0.5f, 0.96f), Js.Hex("#8d8a80"));
                        var peak = Prim.Box(board, c + Vector3.up * 0.55f, new Vector3(0.5f, 0.5f, 0.5f), Js.Hex("#a9a598"));
                        peak.transform.localRotation = Quaternion.Euler(45, 45, 0);
                        peak.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
                    }
                    else if (ch == 'H')
                    {
                        Prim.Box(board, c + new Vector3(0, 0.3f, 0.12f), new Vector3(0.6f, 0.36f, 0.4f), Js.Hex("#f0e0c0"));
                        var roof = Prim.Box(board, c + new Vector3(0, 0.52f, 0.12f), new Vector3(0.46f, 0.46f, 0.44f), Js.Hex("#c0503a"));
                        roof.transform.localRotation = Quaternion.Euler(0, 0, 45);
                    }
                }
            overlay = Prim.Empty("Overlay", World).transform;
            foreach (var u in b.Units)
            {
                var ch = SpawnChar(u.C.Key, TilePos(u.X, u.Y), 0.95f);
                ch.Face(u.Team == 'P' ? Vector3.right : Vector3.left);
                views[u] = ch;
                var ring = Prim.Cyl(World, TilePos(u.X, u.Y) + Vector3.up * 0.01f, 0.8f, 0.02f, u.Team == 'P' ? Js.Hex("#3b8dff") : Js.Hex("#ff4d4d"));
                rings[u] = ring;
            }
            // Board on the left, info panel on the right.
            Rig.Viewport(new Rect(0, 0, 1f - PanelFrac, 1));
            var center = new Vector3(Tactics.COLS / 2f, 0, Tactics.ROWS / 2f - 0.3f);
            float vfov = 40f, pitch = 58f * Mathf.Deg2Rad;
            float aspect = Screen.width * (1f - PanelFrac) / Mathf.Max(1f, Screen.height);
            float tanV = Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad);
            float distW = (Tactics.COLS / 2f + 0.6f) / (tanV * aspect);
            float distH = (Tactics.ROWS / 2f + 0.8f) * Mathf.Sin(pitch) / tanV;
            float dist = Mathf.Max(distW, distH) + 1.5f;
            var dir = new Vector3(0, Mathf.Sin(pitch), -Mathf.Cos(pitch));
            Rig.Set(center + dir * dist, center, vfov);
        }

        string overlayKey;

        void DrawOverlay()
        {
            if (screen != Scr.Battle || b == null || !overlay) return;
            string key = $"{sel?.Id}|{mode}|{phase}|{reach?.Count}|{hover}|{targetKind}|{sel?.X},{sel?.Y}";
            if (key == overlayKey) return;
            overlayKey = key;
            Prim.Clear(overlay);
            var showFor = sel != null && (mode == Mode.Moving || phase == 'E') ? sel : null;
            if (showFor != null && reach != null)
            {
                var atkTiles = new HashSet<(int, int)>();
                foreach (var n in reach.Values)
                {
                    if (n.Blocked) continue;
                    Mark(n.X, n.Y, showFor.Team == 'P' ? Js.Hex("#5aa8ff") : Js.Hex("#ff7a7a"), 0.86f);
                    for (int yy = 0; yy < Tactics.ROWS; yy++)
                        for (int xx = 0; xx < Tactics.COLS; xx++)
                            if (b.InRange(showFor, xx, yy, n.X, n.Y) && !reach.Has(Tactics.Key(xx, yy))) atkTiles.Add((xx, yy));
                }
                foreach (var (xx, yy) in atkTiles) Mark(xx, yy, Js.Hex("#d84a4a"), 0.6f);
            }
            if (mode == Mode.Target && sel != null)
                foreach (var tu in Targets(sel, targetKind)) Frame(tu.X, tu.Y, targetKind == "atk" ? Js.Hex("#ff3b3b") : Js.Hex("#5cffb0"));
            if (hover.HasValue && phase == 'P') Frame(hover.Value.x, hover.Value.y, Color.white, 0.04f);
        }

        void Mark(int x, int y, Color c, float size)
        {
            var p = TilePos(x, y);
            Prim.Box(overlay, p + Vector3.up * 0.015f, new Vector3(size, 0.02f, size), c);
        }

        void Frame(int x, int y, Color c, float w = 0.07f)
        {
            var p = TilePos(x, y) + Vector3.up * 0.03f;
            Prim.Box(overlay, p + new Vector3(0, 0, 0.47f), new Vector3(0.98f, 0.03f, w), c);
            Prim.Box(overlay, p + new Vector3(0, 0, -0.47f), new Vector3(0.98f, 0.03f, w), c);
            Prim.Box(overlay, p + new Vector3(0.47f, 0, 0), new Vector3(w, 0.03f, 0.98f), c);
            Prim.Box(overlay, p + new Vector3(-0.47f, 0, 0), new Vector3(w, 0.03f, 0.98f), c);
        }

        // ── flow (coroutines replace the TS async/await) ──

        IEnumerator Banner(string text, Color col)
        {
            bannerText = text;
            bannerT = 1.2f;
            bannerCol = col;
            Sfx.Play("phase");
            yield return new WaitForSeconds(1.1f);
        }

        void Float(Unit u, string text, Color col) => Popups.Add(TilePos(u.X, u.Y) + Vector3.up * 1.1f, text, col, 24f, 1.2f, 0.6f);

        IEnumerator MoveAlong(Unit u, List<(int x, int y)> path)
        {
            if (path.Count <= 1) yield break;
            animU = u;
            float a = 0;
            int lastI = 0;
            var ch = views[u];
            ch.SetLoop(Chibi.Loop.Run, 1.3f);
            while (true)
            {
                a += Time.deltaTime * 7f;
                int i = Mathf.FloorToInt(a);
                if (i >= path.Count - 1) break;
                if (i != lastI) { Sfx.Play("step"); lastI = i; }
                float k = a - i;
                var p0 = TilePos(path[i].x, path[i].y);
                var p1 = TilePos(path[i + 1].x, path[i + 1].y);
                animPos = Vector3.Lerp(p0, p1, k);
                ch.Face(p1 - p0);
                yield return null;
            }
            var last = path[path.Count - 1];
            u.X = last.x;
            u.Y = last.y;
            animU = null;
            ch.SetLoop(Chibi.Loop.Idle);
        }

        IEnumerator StartPlayerPhase()
        {
            phase = 'P';
            mode = Mode.Busy;
            foreach (var u in b.Units) u.Acted = false;
            yield return Banner($"第 {turn} 回合　我方行動", Js.Hex("#3b6fd8"));
            foreach (var h in b.PhaseHeal('P')) { Float(h.u, "+" + h.amt, Js.Hex("#5cffb0")); Sfx.Play("heal"); }
            mode = Mode.Idle;
            hint = "點選我方角色開始行動";
        }

        void Select(Unit u)
        {
            sel = u;
            reach = b.Reachable(u);
            origin = (u.X, u.Y);
            mode = Mode.Moving;
            Sfx.Play("sel");
            views[u].Act("hop", 0.3f);
            hint = "點藍色格子移動（點自己 = 原地）　右鍵/Esc 取消";
        }

        void Deselect()
        {
            sel = null;
            reach = null;
            mode = Mode.Idle;
            menu.Clear();
            hint = "點選我方角色開始行動";
        }

        List<Unit> Targets(Unit u, string kind)
        {
            if (kind == "atk") return b.Team(u.Team == 'P' ? 'E' : 'P').Where(e => b.InRange(u, e)).ToList();
            if (u.C.Heal <= 0) return new List<Unit>();
            return b.Team(u.Team).Where(a => a != u && a.Hp < a.C.Hp && b.InRange(u, a)).ToList();
        }

        void OpenMenu()
        {
            var u = sel;
            mode = Mode.Menu;
            var atk = Targets(u, "atk");
            var heal = Targets(u, "heal");
            var items = new List<MenuItem>
            {
                new MenuItem { text = "攻擊", fn = () => { mode = Mode.Target; targetKind = "atk"; hint = "點選紅框內的敵人"; }, enabled = atk.Count > 0 },
            };
            if (u.C.Heal > 0) items.Add(new MenuItem { text = "治療", fn = () => { mode = Mode.Target; targetKind = "heal"; hint = "點選要治療的隊友"; }, enabled = heal.Count > 0 });
            items.Add(new MenuItem { text = "待命", fn = () => FinishUnit(u), enabled = true });
            var sp = Gui.WorldToGui(Cam, TilePos(u.X, u.Y) + Vector3.up * 0.5f);
            float boardRight = Gui.W * (1f - PanelFrac);
            float mx = sp.x + 50 + 130 > boardRight ? sp.x - 180 : sp.x + 50;
            menu.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                it.r = new Rect(mx, Mathf.Clamp(sp.y - 60 + i * 52, 60, Gui.H - 60), 130, 44);
                menu.Add(it);
            }
            hint = "選擇行動　右鍵/Esc 返回移動";
        }

        void FinishUnit(Unit u)
        {
            u.Acted = true;
            Deselect();
            if (CheckEnd()) return;
            if (b.Team('P').All(q => q.Acted)) StartCoroutine(EnemyPhase());
        }

        IEnumerator Bump(Unit u, Unit toward)
        {
            bumpU = u;
            bumpDir = TilePos(toward.X, toward.Y) - TilePos(u.X, u.Y);
            bumpT = 0;
            views[u].Face(bumpDir);
            views[u].Act("attack", 0.3f);
            yield return new WaitForSeconds(0.15f);
        }

        IEnumerator Shot(Unit from, Unit to)
        {
            views[from].Face(TilePos(to.X, to.Y) - TilePos(from.X, from.Y));
            views[from].Act(from.C.Magic || from.C.Heal > 0 ? "cast" : "throw", 0.3f);
            var ball = Prim.Sphere(World, Vector3.zero, 0.22f, Js.Hex(from.C.Color));
            Prim.SetColor(ball, Js.Hex(from.C.Color), true);
            var p0 = TilePos(from.X, from.Y) + Vector3.up * 0.6f;
            var p1 = TilePos(to.X, to.Y) + Vector3.up * 0.6f;
            for (float s = 0; s < 1f; s += Time.deltaTime * 4f)
            {
                ball.transform.position = Vector3.Lerp(p0, p1, s) + Vector3.up * Mathf.Sin(s * Mathf.PI) * 0.6f;
                yield return null;
            }
            Destroy(ball);
        }

        IEnumerator DoCombat(Unit a, Unit d)
        {
            bool ranged = b.Dist(a, d) > 1;
            if (ranged) yield return Shot(a, d);
            else yield return Bump(a, d);
            var r = b.Combat(a, d, Rand.Default);
            Sfx.Play(r.Crit ? "crit" : "hit");
            Float(d, r.Crit ? $"爆擊！-{r.Dmg}" : $"-{r.Dmg}", r.Crit ? Js.Hex("#ffb13d") : Js.Hex("#ff6b6b"));
            Fx.Burst(TilePos(d.X, d.Y) + Vector3.up * 0.5f, r.Crit ? 24 : 12, Js.Hex(a.C.Color), 3f, 0.4f, 0.08f);
            views[d].Act("hit", 0.35f);
            if (r.Crit) Rig.Shake(0.12f, 0.2f);
            yield return new WaitForSeconds(0.45f);
            if (r.Killed) { yield return KO(d); yield break; }
            if (r.Counter > 0)
            {
                if (b.Dist(a, d) > 1) yield return Shot(d, a);
                else yield return Bump(d, a);
                Sfx.Play(r.CounterCrit ? "crit" : "hit");
                Float(a, $"反擊 -{r.Counter}", Js.Hex("#ff6b6b"));
                Fx.Burst(TilePos(a.X, a.Y) + Vector3.up * 0.5f, 10, Js.Hex(d.C.Color), 3f, 0.4f, 0.08f);
                views[a].Act("hit", 0.35f);
                yield return new WaitForSeconds(0.45f);
                if (r.Died) yield return KO(a);
            }
        }

        IEnumerator KO(Unit u)
        {
            Sfx.Play("ko");
            Float(u, "擊倒！", Js.Hex("#ffe066"));
            views[u].KnockDown(true);
            yield return new WaitForSeconds(0.4f);
            views[u].gameObject.SetActive(false);
            rings[u].SetActive(false);
        }

        IEnumerator DoHeal(Unit a, Unit tu)
        {
            int amt = b.Heal(a, tu);
            Sfx.Play("heal");
            views[a].Face(TilePos(tu.X, tu.Y) - TilePos(a.X, a.Y));
            views[a].Act("cast", 0.4f);
            Fx.Burst(TilePos(tu.X, tu.Y) + Vector3.up * 0.3f, 16, Js.Hex("#5cffb0"), 1.2f, 0.8f, 0.07f, -1.5f);
            Float(tu, "+" + amt, Js.Hex("#5cffb0"));
            yield return new WaitForSeconds(0.6f);
        }

        IEnumerator PlayerAct(Unit target)
        {
            var u = sel;
            mode = Mode.Busy;
            menu.Clear();
            if (targetKind == "atk") yield return DoCombat(u, target);
            else yield return DoHeal(u, target);
            FinishUnit(u);
        }

        IEnumerator EnemyPhase()
        {
            if (mode == Mode.Over) yield break;
            phase = 'E';
            Deselect();
            mode = Mode.Busy;
            hint = "";
            yield return Banner("敵方回合", Js.Hex("#b83a36"));
            foreach (var h in b.PhaseHeal('E')) Float(h.u, "+" + h.amt, Js.Hex("#5cffb0"));
            foreach (var e in b.Team('E'))
            {
                if (e.Hp <= 0) continue;
                var plan = b.Plan(e);
                sel = e;
                reach = b.Reachable(e);
                yield return new WaitForSeconds(0.35f);
                reach = null;
                yield return MoveAlong(e, plan.Path);
                if (plan.Target != null)
                {
                    yield return new WaitForSeconds(0.1f);
                    if (plan.Type == "atk") yield return DoCombat(e, plan.Target);
                    else yield return DoHeal(e, plan.Target);
                }
                sel = null;
                if (CheckEnd()) yield break;
                yield return new WaitForSeconds(0.15f);
            }
            turn++;
            StartCoroutine(StartPlayerPhase());
        }

        bool CheckEnd()
        {
            char w = b.Winner();
            if (w == '\0') return false;
            mode = Mode.Over;
            resultWin = w == 'P';
            resultTurns = turn;
            survivors = b.Team(w).Select(u => u.C.Key).ToList();
            StartCoroutine(DelayedOver());
            return true;
        }

        IEnumerator DelayedOver()
        {
            foreach (var u in b.Team(b.Winner())) views[u].Act("win", 1f);
            yield return new WaitForSeconds(1.2f);
            GoOver();
        }

        void EndTurn()
        {
            if (phase != 'P' || mode == Mode.Busy) return;
            if (sel != null && (mode == Mode.Menu || mode == Mode.Target)) sel.Acted = true;
            else if (sel != null) { sel.X = origin.x; sel.Y = origin.y; }
            StartCoroutine(EnemyPhase());
        }

        // ── update ──

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            switch (screen)
            {
                case Scr.Title:
                    if (In.Back) { ExitToHub(); return; }
                    if (In.Confirm || In.MouseDown(0)) { Sfx.Notes("C5 E5 G5", 0.07f, Wave.Square, 0.3f); GoSelect(); }
                    break;
                case Scr.Select:
                    if (In.Back) { GoTitle(); return; }
                    if (picks.Count == 4 && In.Down(KeyCode.Return)) StartBattle();
                    break;
                case Scr.Battle:
                    UpdateBattle(dt);
                    break;
                case Scr.Over:
                    if (In.Confirm) GoBattle();
                    else if (In.Back) GoSelect();
                    break;
            }
        }

        void StartBattle()
        {
            team.Clear();
            team.AddRange(picks);
            Sfx.Notes("G4 C5 E5 G5", 0.07f, Wave.Square, 0.3f);
            GoBattle();
        }

        (int x, int y)? PickTile()
        {
            var mp = In.MousePos;
            var vp = Cam.pixelRect;
            if (!vp.Contains(mp)) return null;
            var ray = Cam.ScreenPointToRay(mp);
            var plane = new Plane(Vector3.up, new Vector3(0, 0.12f, 0));
            if (!plane.Raycast(ray, out float d)) return null;
            var p = ray.GetPoint(d);
            int x = Mathf.FloorToInt(p.x), y = Tactics.ROWS - 1 - Mathf.FloorToInt(p.z);
            return b.InBounds(x, y) ? ((int, int)?)(x, y) : null;
        }

        void UpdateBattle(float dt)
        {
            if (bannerT > 0) bannerT -= dt;
            if (bumpU != null) { bumpT += dt * 6; if (bumpT >= 1) bumpU = null; }
            // Place unit views.
            foreach (var kv in views)
            {
                var u = kv.Key;
                if (u.Hp <= 0) continue;
                var p = u == animU ? animPos : TilePos(u.X, u.Y);
                if (u == bumpU) p += bumpDir * Mathf.Sin(bumpT * Mathf.PI) * 0.35f;
                kv.Value.transform.localPosition = p;
                rings[u].transform.localPosition = new Vector3(p.x, p.y + 0.01f, p.z);
                bool acted = u.Acted && u.Team == 'P';
                Prim.SetColor(rings[u], acted ? Js.Hex("#777777") : u.Team == 'P' ? Js.Hex("#3b8dff") : Js.Hex("#ff4d4d"));
            }
            hover = PickTile();
            DrawOverlay();

            bool click = In.MouseDown(0) && !OverGuiButton();
            bool cancel = In.Back || In.MouseDown(1);
            if (phase != 'P' || mode == Mode.Busy || mode == Mode.Over || animU != null)
            {
                if (In.Back && mode != Mode.Busy && mode != Mode.Over && phase != 'E') ExitToHub();
                return;
            }
            if (In.Down(KeyCode.E)) { EndTurn(); return; }
            if (cancel)
            {
                if (mode == Mode.Target) { OpenMenu(); return; }
                if (mode == Mode.Menu && sel != null) { sel.X = origin.x; sel.Y = origin.y; Select(sel); return; }
                if (mode == Mode.Idle && In.Back) { ExitToHub(); return; }
                Deselect();
                return;
            }
            if (!click || !hover.HasValue || mode == Mode.Menu) return;
            var tile = hover.Value;
            var cu = b.UnitAt(tile.x, tile.y);
            if (mode == Mode.Idle)
            {
                if (cu != null && cu.Team == 'P' && !cu.Acted) Select(cu);
                return;
            }
            if (mode == Mode.Moving && sel != null)
            {
                if (cu != null && cu.Team == 'P' && cu != sel && !cu.Acted) { Select(cu); return; }
                if (!reach.TryGet(Tactics.Key(tile.x, tile.y), out var n) || n.Blocked) return;
                var path = b.PathTo(reach, tile.x, tile.y);
                mode = Mode.Busy;
                StartCoroutine(MoveThenMenu(sel, path));
                return;
            }
            if (mode == Mode.Target && sel != null && cu != null)
            {
                if (Targets(sel, targetKind).Contains(cu)) StartCoroutine(PlayerAct(cu));
            }
        }

        IEnumerator MoveThenMenu(Unit s, List<(int x, int y)> path)
        {
            yield return MoveAlong(s, path);
            reach = null;
            OpenMenu();
        }

        bool OverGuiButton()
        {
            var m = In.GuiMouse;
            if (endBtn.Contains(m)) return true;
            foreach (var it in menu) if (it.r.Contains(m)) return true;
            return m.x > Gui.W * (1f - PanelFrac);
        }

        // ── GUI ──

        void OnGUI()
        {
            Gui.Begin();
            switch (screen)
            {
                case Scr.Title:
                    Gui.TitleCard("萌友戰棋・八方對決", "回合制戰略　四對四戰棋", null, t, Js.Hex("#ffe8b0"));
                    break;
                case Scr.Select: DrawSelect(); break;
                case Scr.Battle: DrawBattle(); break;
                case Scr.Over: DrawOver(); break;
            }
        }

        void DrawSelect()
        {
            float W = Gui.W, H = Gui.H, cx = W / 2;
            Gui.Label($"選出 4 名出戰角色（{picks.Count}/4）— 其餘四位將成為對手", cx, 40, 30, Js.Hex("#ffe8b0"), 0.5f, 0.5f, Js.Hex("#2a1c10"));
            for (int i = 0; i < Tactics.CHARS.Length; i++)
            {
                var c = Tactics.CHARS[i];
                var foot = GuiAt(lineup[i].transform.position);
                var head = GuiAt(lineup[i].transform.position + Vector3.up * 1.5f);
                float cw = Mathf.Min(150, W / 8.6f);
                var card = new Rect(foot.x - cw / 2, foot.y + 8, cw, 120);
                int pk = picks.IndexOf(c.Key);
                var hit = new Rect(foot.x - cw / 2, head.y, cw, card.yMax - head.y);
                bool hov = Gui.Hover(hit);
                Gui.Panel(card, pk >= 0 ? Js.Hex("#1d4a7a", 0.93f) : Js.Hex("#1c1428", 0.87f), pk >= 0 ? Js.Hex("#5cc8ff") : hov ? Js.Hex("#ffe066") : Js.Hex("#c9a46a", 0.53f));
                Gui.Label($"{c.Name}｜{c.Role}", card.center.x, card.y + 16, 16, Color.white);
                Gui.Label($"HP {c.Hp} 攻 {c.Atk} 防 {c.Def}", card.center.x, card.y + 38, 13, Js.Hex("#ffe8b0"));
                Gui.Label($"移動 {c.Mov} 射程 {c.Rmin}-{c.Rmax}", card.center.x, card.y + 56, 13, Js.Hex("#ffe8b0"));
                Gui.Label(c.Desc, card.center.x, card.y + 72, 11, Js.Hex("#cccccc"), 0.5f, 0f, null, cw - 10);
                if (pk >= 0)
                {
                    Gui.Circle(card.x + 14, card.y + 14, 13, Js.Hex("#5cc8ff"));
                    Gui.Label((pk + 1).ToString(), card.x + 14, card.y + 14, 16, Color.white);
                }
                if (Gui.Clicked(hit))
                {
                    if (pk >= 0) picks.RemoveAt(pk);
                    else if (picks.Count < 4) { picks.Add(c.Key); lineup[i].Act("hop", 0.35f); }
                    Sfx.Beep(pk >= 0 ? 440 : 700, 0.05f, Wave.Square, 0.2f);
                }
            }
            Gui.Label("戰場：", cx - 330, H - 120, 22, Color.white, 1f, 0.5f);
            for (int i = 0; i < Tactics.MAPS.Length; i++)
                if (Gui.Button(new Rect(cx - 320 + i * 220, H - 146, 200, 52), Tactics.MAPS[i].Name, 22, null, null, mapIndex == i)) mapIndex = i;
            bool ready = picks.Count == 4;
            if (Gui.Button(new Rect(cx - 150, H - 78, 300, 58), ready ? "出擊！" : "請選滿 4 人", 28, ready ? Js.Hex("#b8742a") : Js.Hex("#555a70"), ready ? Js.Hex("#e0923a") : Js.Hex("#555a70")) && ready) StartBattle();
        }

        void DrawBattle()
        {
            if (b == null) return;
            float W = Gui.W, H = Gui.H;
            // HP bars over units.
            foreach (var kv in views)
            {
                var u = kv.Key;
                if (u.Hp <= 0) continue;
                var p = Gui.WorldToGui(Cam, kv.Value.transform.position + Vector3.up * 1.05f);
                if (p.z < 0) continue;
                float r = (float)u.Hp / u.C.Hp;
                Gui.Rect(p.x - 24, p.y, 48, 6, new Color(0, 0, 0, 0.7f));
                Gui.Rect(p.x - 23, p.y + 1, 46 * r, 4, r > 0.5f ? Js.Hex("#5cffb0") : r > 0.25f ? Js.Hex("#ffd23f") : Js.Hex("#ff5f3a"));
            }
            float boardRight = W * (1f - PanelFrac);
            Gui.Label($"{b.Map.Name}　第 {turn} 回合　{(phase == 'P' ? "我方行動" : "敵方行動")}", 20, 30, 26, Js.Hex("#ffe8b0"), 0f, 0.5f, Js.Hex("#2a1c10"));
            foreach (var m in menu)
                if (Gui.Button(m.r, m.text, 22, m.enabled ? Js.Hex("#3b6fd8") : Js.Hex("#4a4f5a"), m.enabled ? Js.Hex("#5a8cf0") : Js.Hex("#4a4f5a")) && m.enabled && mode == Mode.Menu)
                {
                    m.fn();
                    break;
                }

            // Right panel.
            Gui.Rect(boardRight, 0, W - boardRight, H, Js.Hex("#1a120c"));
            float px = boardRight + 16, pw = W - px - 16, by = 20;
            Unit hov = hover.HasValue ? b.UnitAt(hover.Value.x, hover.Value.y) : null;
            var u2 = hov ?? sel;
            Gui.Panel(new Rect(px, by, pw, 250), Js.Hex("#1c1428", 0.93f), Js.Hex("#c9a46a"));
            if (u2 != null)
            {
                Gui.Circle(px + 50, by + 60, 34, Js.Hex(u2.C.Color));
                Gui.Label(u2.C.Name.Substring(0, 1), px + 50, by + 60, 30, Color.white, 0.5f, 0.5f, Color.black);
                float tx = px + 100;
                Gui.Label($"{u2.C.Name}｜{u2.C.Title}", tx, by + 30, 22, u2.Team == 'P' ? Js.Hex("#9fd2ff") : Js.Hex("#ffb0b0"), 0f, 0.5f);
                Gui.Label($"{u2.C.Role}　HP {u2.Hp}/{u2.C.Hp}", tx, by + 62, 18, Color.white, 0f, 0.5f);
                Gui.Label($"攻 {u2.C.Atk}　防 {u2.C.Def}　移 {u2.C.Mov}　射程 {u2.C.Rmin}-{u2.C.Rmax}", px + 16, by + 110, 16, Js.Hex("#ffe8b0"), 0f, 0.5f);
                Gui.Label(u2.C.Desc, px + 16, by + 130, 15, Js.Hex("#dddddd"), 0f, 0f, null, pw - 32);
                var tr = b.TerrainAt(u2.X, u2.Y);
                Gui.Label($"地形：{tr.Name}（防禦 +{tr.Def}）", px + 16, by + 220, 15, Js.Hex("#b8e0a0"), 0f, 0.5f);
            }
            else if (hover.HasValue)
            {
                var tr = b.TerrainAt(hover.Value.x, hover.Value.y);
                Gui.Label($"{tr.Name}　移動消耗 {(tr.Cost >= 99 ? "不可通行" : tr.Cost.ToString())}　防禦 +{tr.Def}{(tr.Heal > 0 ? $"　每回合恢復 {tr.Heal}" : "")}", px + 16, by + 30, 16, Color.white, 0f, 0f, null, pw - 32);
            }
            // Forecast.
            string fc = "";
            if (mode == Mode.Target && sel != null && hov != null)
            {
                var a = sel;
                if (targetKind == "atk" && hov.Team != a.Team && b.InRange(a, hov))
                {
                    int dmg = b.CalcDamage(a, hov).Dmg;
                    bool kill = dmg >= hov.Hp;
                    int counter = !kill && b.InRange(hov, a) ? b.CalcDamage(hov, a).Dmg : 0;
                    fc = $"預測：造成 {dmg} 傷害{(kill ? "（擊倒！）" : "")}{(a.C.Crit > 0 ? "、30% 爆擊" : "")}\n{(counter > 0 ? $"反擊：受到 {counter} 傷害" : "對方無法反擊")}";
                }
                else if (targetKind == "heal" && hov.Team == a.Team) fc = $"治療：恢復 {Mathf.Min(a.C.Heal, hov.C.Hp - hov.Hp)} HP";
            }
            Gui.Panel(new Rect(px, by + 266, pw, 130), Js.Hex("#1c1428", 0.93f), Js.Hex("#c9a46a"));
            Gui.Label(fc != "" ? fc : hint, px + 16, by + 331, 17, fc != "" ? Js.Hex("#ffe066") : Color.white, 0f, 0.5f, null, pw - 32);
            Gui.Label($"我方 {b.Team('P').Count} 人　敵方 {b.Team('E').Count} 人", px + 16, by + 420, 20, Color.white, 0f, 0.5f);
            endBtn = new Rect(px, by + 450, pw, 56);
            if (Gui.Button(endBtn, "結束回合（E）", 24, Js.Hex("#b8742a"), Js.Hex("#e0923a"))) EndTurn();
            Gui.Label("森林 +2、山岳 +3、民房 +1 防禦；民房每回合恢復 5 HP", px + 16, by + 540, 14, Js.Hex("#cccccc"), 0f, 0f, null, pw - 20);
            Gui.Label("Esc：取消 / 回到大廳", px + 16, H - 24, 14, new Color(1, 1, 1, 0.6f), 0f, 0.5f);
            if (bannerT > 0)
            {
                float a = Mathf.Clamp01(bannerT * 3);
                var bc = bannerCol;
                bc.a = 0.85f * a;
                Gui.Rect(0, H / 2 - 50, W, 100, bc);
                Gui.Label(bannerText, W / 2, H / 2, 48, new Color(1, 1, 1, a), 0.5f, 0.5f, Color.black);
            }
        }

        void DrawOver()
        {
            float W = Gui.W, H = Gui.H, cx = W / 2;
            Gui.Label(resultWin ? "勝利！" : "戰敗…", cx, 110, 90, resultWin ? Js.Hex("#ffd23f") : Js.Hex("#ff8a80"), 0.5f, 0.5f, Color.black);
            Gui.Label($"共 {resultTurns} 回合　存活 {survivors.Count} 人", cx, 200, 28, Color.white, 0.5f, 0.5f, Color.black);
            if (lineup != null && Mathf.Repeat(t, 0.8f) < Time.deltaTime)
                foreach (var c in lineup) if (c) c.Act(resultWin ? "win" : "lose", 0.7f);
            if (Gui.Button(new Rect(cx - 250, H - 110, 230, 60), "再戰一次", 26, Js.Hex("#b8742a"), Js.Hex("#e0923a"))) GoBattle();
            if (Gui.Button(new Rect(cx + 20, H - 110, 230, 60), "重新編隊", 26)) GoSelect();
        }
    }
}
