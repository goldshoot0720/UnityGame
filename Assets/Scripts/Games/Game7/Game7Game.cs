// 萌友大亂鬥 — 3D top-down presentation of the Game7 arena sim (title → select → arena → result).
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoeGames.Game7
{
    [MoeGame(7)]
    public class Game7Game : MiniGame
    {
        public override TouchLayout Touch => new TouchLayout().Button("射擊", KeyCode.J).Button("衝刺", KeyCode.Space);

        enum Scr { Title, Select, Arena, Over }

        const float U = 1f / 50f; // world units per arena px

        Scr scr;
        float t;
        string hero = "whale";
        CastPicker picker;
        Chibi[] lineup;
        ArenaSim sim;
        List<Stat> stats = new List<Stat>();
        string reason = "";

        readonly Dictionary<P, Chibi> views = new Dictionary<P, Chibi>();
        readonly Dictionary<P, GameObject> rings = new Dictionary<P, GameObject>();
        readonly Dictionary<P, Transform> guns = new Dictionary<P, Transform>();
        readonly Dictionary<P, GameObject> shields = new Dictionary<P, GameObject>();
        readonly Dictionary<Shot, Transform> shotViews = new Dictionary<Shot, Transform>();
        readonly List<GameObject> pickupViews = new List<GameObject>();
        Transform reticle;

        protected override void Begin()
        {
            Sfx.Define("pew", Tone.Beep(900, 0.06f, Wave.Square, 0.12f, 400));
            Sfx.Define("boom", Tone.Noise(0.35f, 0.45f, FilterType.Lowpass, 1400, 100));
            Sfx.Define("hurt", Tone.Beep(300, 0.1f, Wave.Square, 0.25f, 120));
            Sfx.Define("ko", Tone.Melody("G5 C5", 0.08f, Wave.Square, 0.3f));
            Sfx.Define("pick", Tone.Melody("C6 E6 G6", 0.05f, Wave.Square, 0.25f));
            Sfx.Define("dash", new Tone { Type = Wave.Noise, Duration = 0.15f, Volume = 0.2f, Filter = FilterType.Bandpass, FilterFreq = 800, FilterFreqEnd = 2400, Q = 1.5f });
            Rig.Background(Js.Hex("#2a2140"));
            Sfx.MusicByName("music", 0.3f);
            GoTitle();
        }

        void GoTitle()
        {
            scr = Scr.Title;
            lineup = ShowLineup(Cast.Ids, "#6b2a8a", "#ffd6e4");
        }

        void GoSelect()
        {
            scr = Scr.Select;
            var ids = Data.FIGHTERS.Select(f => f.Id).ToList();
            picker = new CastPicker(ids, 1);
            picker.Picks.Add(hero);
            picker.Cursor = ids.IndexOf(hero);
            lineup = ShowLineup(ids, "#6b2a8a", "#ffd6e4", 1.35f, 1.4f);
        }

        void GoArena()
        {
            scr = Scr.Arena;
            sim = new ArenaSim(hero);
            sim.Sound += n => Sfx.Play(n);
            sim.Burst += (x, y, c, n) => Fx.Burst(ToW(x, y) + Vector3.up * 0.6f, n, Js.Hex(c), 4f, 0.5f, 0.1f);
            sim.Shake += d => Rig.Shake(0.12f, d);
            BuildArena();
        }

        void GoOver()
        {
            scr = Scr.Over;
            stats = sim.Stats;
            reason = sim.Reason;
            lineup = ShowLineup(stats.Take(3).Select(s => s.Id).ToList(), "#6b2a8a", "#ffd6e4", 1.5f, 1.6f);
            bool won = stats.Count > 0 && stats[0].You;
            if (won) Sfx.Notes("C5 E5 G5 C6 G5 C6:3", 0.1f, Wave.Square, 0.4f);
            else Sfx.Notes("G4 F4 E4 D4 C4:3", 0.14f, Wave.Triangle, 0.4f);
        }

        static Vector3 ToW(double x, double y, float h = 0) => new Vector3((float)x * U, h, -(float)y * U);

        void BuildArena()
        {
            ClearWorld();
            views.Clear(); rings.Clear(); guns.Clear(); shields.Clear(); shotViews.Clear(); pickupViews.Clear();
            float aw = (float)Data.ARENA_W * U, ah = (float)Data.ARENA_H * U;
            var center = new Vector3(aw / 2, 0, -ah / 2);
            Prim.Box(World, center + Vector3.down * 0.2f, new Vector3(aw + 20, 0.2f, ah + 20), Js.Hex("#2a2140"));
            // Pastel checker floor (80 px tiles).
            float T = 80 * U;
            for (int ty = 0; ty < Data.ARENA_H / 80; ty++)
                for (int tx = 0; tx < Data.ARENA_W / 80; tx++)
                    if ((tx + ty) % 2 == 0) Prim.Tile(World, ToW(tx * 80 + 40, ty * 80 + 40), T, T, Js.Hex("#fde7ef"), 0.02f);
            Prim.Tile(World, center + Vector3.down * 0.01f, aw, ah, Js.Hex("#f8dbe8"), 0.01f);
            Prim.Cyl(World, center + Vector3.up * 0.03f, 7.4f, 0.02f, Js.Hex("#ff9fc4"));
            Prim.Cyl(World, center + Vector3.up * 0.035f, 7.0f, 0.02f, Js.Hex("#ffd6e4"));
            // Border walls.
            var wall = Js.Hex("#6b4a9a");
            Prim.Box(World, new Vector3(aw / 2, 0.4f, 0.2f), new Vector3(aw + 0.8f, 0.8f, 0.4f), wall);
            Prim.Box(World, new Vector3(aw / 2, 0.4f, -ah - 0.2f), new Vector3(aw + 0.8f, 0.8f, 0.4f), wall);
            Prim.Box(World, new Vector3(-0.2f, 0.4f, -ah / 2), new Vector3(0.4f, 0.8f, ah), wall);
            Prim.Box(World, new Vector3(aw + 0.2f, 0.4f, -ah / 2), new Vector3(0.4f, 0.8f, ah), wall);
            foreach (var c in Data.CRATES)
            {
                var pos = ToW(c[0] + c[2] / 2, c[1] + c[3] / 2, 0.75f);
                var crate = Prim.Box(World, pos, new Vector3((float)c[2] * U * 0.98f, 1.5f, (float)c[3] * U * 0.98f), Js.Hex("#b58a5a"));
                Art.Apply(crate, "crate", Vector2.one);
                Prim.Box(World, pos + Vector3.up * 0.76f, new Vector3((float)c[2] * U * 0.7f, 0.02f, (float)c[3] * U * 0.12f), Js.Hex("#8a6038"));
            }
            foreach (var b in Data.BUSHES)
            {
                if (Art.Standee(World, "bush", ToW(b.x, b.y), 2.2f)) continue;
                Prim.Sphere(World, ToW(b.x, b.y, 0.5f), 1.6f, Js.Hex("#5aa84a"));
                Prim.Sphere(World, ToW(b.x + 25, b.y - 20, 0.7f), 1.2f, Js.Hex("#6cbc58"));
            }
            foreach (var q in sim.Pickups)
            {
                var art = Art.Standee(World, q.Kind, ToW(q.X, q.Y, 0.3f), 1.0f);
                if (art) { pickupViews.Add(art.gameObject); continue; }
                var go = q.Kind == "heart" ? Prim.Sphere(World, ToW(q.X, q.Y, 0.6f), 0.55f, Js.Hex("#ff5f7a")) : Prim.Box(World, ToW(q.X, q.Y, 0.6f), new Vector3(0.25f, 0.7f, 0.25f), Js.Hex("#ffe066"));
                Prim.SetColor(go, q.Kind == "heart" ? Js.Hex("#ff5f7a") : Js.Hex("#ffe066"), true);
                pickupViews.Add(go);
            }
            foreach (var p in sim.Ps)
            {
                views[p] = SpawnChar(p.F.Id, ToW(p.X, p.Y), 1.45f);
                rings[p] = Prim.Cyl(World, ToW(p.X, p.Y, 0.02f), 1.0f, 0.02f, p.You ? Js.Hex("#39c6ff") : Js.Hex(p.F.Color));
                var gun = Prim.Box(World, Vector3.zero, new Vector3(0.12f, 0.12f, 0.6f), Js.Hex(p.F.Weapon.Color)).transform;
                guns[p] = gun;
                shields[p] = Prim.Cyl(World, Vector3.zero, 1.6f, 0.02f, Js.Hex("#9fe3ff"));
                Prim.SetColor(shields[p], Js.Hex("#9fe3ff"), true);
            }
            reticle = Prim.Cyl(World, Vector3.zero, 0.4f, 0.02f, Js.Hex("#ff3b7a")).transform;
            Prim.SetColor(reticle.gameObject, Js.Hex("#ff3b7a"), true);
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
                    if (picker.UpdateKeys(lineup)) StartArena();
                    break;
                case Scr.Arena:
                    if (In.Back) { GoTitle(); return; }
                    UpdateArena(dt);
                    break;
                case Scr.Over:
                    if (In.Confirm) GoArena();
                    else if (In.Back) GoSelect();
                    if (lineup != null && lineup.Length > 0 && Mathf.Repeat(t, 0.8f) < dt) lineup[0].Act("win", 0.7f);
                    break;
            }
        }

        void StartArena()
        {
            if (picker.Picks.Count > 0) hero = picker.Picks[0];
            Sfx.Notes("G4 C5 E5 G5", 0.06f, Wave.Square, 0.3f);
            GoArena();
        }

        void UpdateArena(float dt)
        {
            var me = sim.Me;
            double aim = me.Aim;
            var ray = Cam.ScreenPointToRay(In.MousePos);
            if (TouchPad.Active)
            {
                // Phones: auto-aim at the nearest rival in sight.
                P best = null;
                double bd = double.PositiveInfinity;
                foreach (var o in sim.Ps)
                {
                    if (o == me || o.Dead > 0) continue;
                    double dd = Js.Hypot(o.X - me.X, o.Y - me.Y);
                    if (dd < bd && !Data.Blocked(me.X, me.Y, o.X, o.Y)) { bd = dd; best = o; }
                }
                if (best != null) aim = System.Math.Atan2(best.Y - me.Y, best.X - me.X);
                else if (In.Axis.sqrMagnitude > 0) aim = System.Math.Atan2(-In.Axis.y, In.Axis.x);
            }
            else if (new Plane(Vector3.up, Vector3.up * 0.6f).Raycast(ray, out float d))
            {
                var hit = ray.GetPoint(d);
                double mx = hit.x / U, my = -hit.z / U;
                aim = System.Math.Atan2(my - me.Y, mx - me.X);
            }
            var input = new PlayerInput
            {
                Dx = (In.Held(KeyCode.D, KeyCode.RightArrow) ? 1 : 0) - (In.Held(KeyCode.A, KeyCode.LeftArrow) ? 1 : 0),
                Dy = (In.Held(KeyCode.S, KeyCode.DownArrow) ? 1 : 0) - (In.Held(KeyCode.W, KeyCode.UpArrow) ? 1 : 0),
                Aim = aim,
                Fire = In.MouseHeld(0) || In.Held(KeyCode.J),
                DashPressed = In.Down(KeyCode.Space, KeyCode.LeftShift),
            };
            if (sim.Update(dt, input)) { GoOver(); return; }
            SyncViews(dt);
        }

        void SyncViews(float dt)
        {
            foreach (var p in sim.Ps)
            {
                var v = views[p];
                bool alive = p.Dead <= 0;
                v.gameObject.SetActive(alive);
                rings[p].SetActive(alive);
                guns[p].gameObject.SetActive(alive);
                shields[p].SetActive(alive && p.Shield > 0);
                if (!alive) continue;
                var pos = ToW(p.X, p.Y);
                v.transform.localPosition = pos;
                var aimDir = new Vector3((float)System.Math.Cos(p.Aim), 0, -(float)System.Math.Sin(p.Aim));
                v.Face(aimDir);
                v.SetLoop(Js.Hypot(p.Vx, p.Vy) > 10 ? Chibi.Loop.Run : Chibi.Loop.Idle, 1.4f);
                rings[p].transform.localPosition = pos + Vector3.up * 0.02f;
                shields[p].transform.localPosition = pos + Vector3.up * 0.8f;
                guns[p].localPosition = pos + Vector3.up * 0.75f + aimDir * 0.5f;
                guns[p].localRotation = Quaternion.LookRotation(aimDir);
                if (p.Flash > 0.1) v.Act("hit", 0.2f);
                if (p.Power > 0 && Random.value < 0.2f) Fx.Burst(pos + Vector3.up * 0.8f, 1, Js.Hex("#fff3a0"), 1f, 0.3f, 0.06f, -1f);
            }
            // Shots.
            foreach (var s in sim.Shots)
            {
                if (!shotViews.TryGetValue(s, out var tr))
                {
                    var col = Js.Hex(s.Color);
                    GameObject go = s.Kind == Kind.Sniper ? Prim.Box(World, Vector3.zero, new Vector3(0.08f, 0.08f, 0.9f), col)
                        : s.Kind == Kind.Boomerang ? Prim.Box(World, Vector3.zero, new Vector3(0.45f, 0.08f, 0.18f), col)
                        : s.Kind == Kind.Book ? Prim.Box(World, Vector3.zero, new Vector3(0.3f, 0.05f, 0.22f), col)
                        : Prim.Sphere(World, Vector3.zero, (float)s.R * 2 * U, col);
                    Prim.SetColor(go, col, true);
                    tr = go.transform;
                    shotViews[s] = tr;
                }
                tr.localPosition = ToW(s.X, s.Y, 0.75f);
                var dir = new Vector3((float)s.Vx, 0, -(float)s.Vy);
                if (s.Kind == Kind.Sniper && dir.sqrMagnitude > 0) tr.localRotation = Quaternion.LookRotation(dir);
                else if (s.Kind == Kind.Boomerang || s.Kind == Kind.Book) tr.localRotation = Quaternion.Euler(0, (float)s.T * 900, 0);
            }
            foreach (var gone in shotViews.Keys.Where(s => !sim.Shots.Contains(s)).ToList())
            {
                Destroy(shotViews[gone].gameObject);
                shotViews.Remove(gone);
            }
            for (int i = 0; i < sim.Pickups.Count; i++)
            {
                var q = sim.Pickups[i];
                pickupViews[i].SetActive(q.Wait <= 0);
                pickupViews[i].transform.localPosition = ToW(q.X, q.Y, 0.6f + Mathf.Sin(t * 4 + (float)q.X) * 0.1f);
                if (!pickupViews[i].GetComponent<Billboard>()) pickupViews[i].transform.localRotation = Quaternion.Euler(0, t * 90, 0);
            }
            var me = sim.Me;
            reticle.gameObject.SetActive(me.Dead <= 0);
            reticle.localPosition = ToW(me.X + System.Math.Cos(me.Aim) * 70, me.Y + System.Math.Sin(me.Aim) * 70, 0.05f);
            var mp = ToW(me.X, me.Y);
            Rig.Follow(mp + new Vector3(0, 12f, -8f), mp, 8f, dt);
            Cam.fieldOfView = 45f;
        }

        // ── GUI ──

        void OnGUI()
        {
            Gui.Begin();
            float W = Gui.W, H = Gui.H, cx = W / 2;
            switch (scr)
            {
                case Scr.Title:
                    Gui.TitleCard("萌友大亂鬥", "八人混戰｜先拿 10 殺或 3 分鐘內最多擊殺者獲勝", null, t, Color.white);
                    break;
                case Scr.Select:
                    Gui.Label("選擇你的鬥士", cx, 44, 40, Color.white, 0.5f, 0.5f, Js.Hex("#6b2a8a"));
                    picker.Draw(Cam, lineup, 96, (r, i) =>
                    {
                        var f = Data.FIGHTERS[i];
                        Gui.Label(f.Name, r.center.x, r.y + 16, 18, Color.white);
                        Gui.Label(f.Weapon.Name, r.center.x, r.y + 38, 13, Js.Hex(f.Weapon.Color));
                        CastPicker.Stat(r, r.y + 60, "傷害", (float)(f.Weapon.Dmg * f.Weapon.Pellets / f.Weapon.Rate), 150);
                        CastPicker.Stat(r, r.y + 78, "射程", (float)f.Weapon.Range, 1100);
                    }, "#d9458b");
                    if (picker.Picks.Count > 0) hero = picker.Picks[0];
                    if (Gui.Button(new Rect(cx - 150, H - 70, 300, 56), "進入競技場！", 28, Js.Hex("#d9458b"), Js.Hex("#ff6aa8"))) StartArena();
                    break;
                case Scr.Arena: DrawHud(W, H); break;
                case Scr.Over: DrawOver(W, H); break;
            }
        }

        void DrawHud(float W, float H)
        {
            float cx = W / 2;
            var me = sim.Me;
            // Name tags + HP bars.
            foreach (var p in sim.Ps)
            {
                if (p.Dead > 0) continue;
                var sp = Gui.WorldToGui(Cam, ToW(p.X, p.Y, 1.8f));
                if (sp.z < 0) continue;
                Gui.Rect(sp.x - 26, sp.y, 52, 7, new Color(0, 0, 0, 0.6f));
                float r = Mathf.Clamp01((float)(p.Hp / Data.MAX_HP));
                Gui.Rect(sp.x - 25, sp.y + 1, 50 * r, 5, p.Hp > 50 ? Js.Hex("#5cffb0") : p.Hp > 25 ? Js.Hex("#ffd23f") : Js.Hex("#ff5f3a"));
                Gui.Label(p.You ? $"▼{p.F.Name}" : p.F.Name, sp.x, sp.y - 12, 14, p.You ? Js.Hex("#39c6ff") : Color.white, 0.5f, 0.5f, Color.black);
            }
            Gui.Panel(new Rect(cx - 120, 10, 240, 56));
            int m = (int)(sim.Clock / 60), s = (int)(sim.Clock % 60);
            Gui.Label($"{m}:{s:00}", cx, 30, 26, Js.Hex("#ffe066"));
            var lead = sim.Ranking()[0];
            Gui.Label($"領先：{lead.F.Name} {lead.Kills} 殺（目標 {Data.KILL_TARGET}）", cx, 55, 14, Color.white);
            // Player card.
            Gui.Panel(new Rect(16, H - 96, 300, 80));
            Gui.Circle(52, H - 56, 26, Js.Hex(me.F.Color));
            Gui.Label(me.F.Name.Substring(0, 1), 52, H - 56, 26, Color.white, 0.5f, 0.5f, Color.black);
            Gui.Label($"{me.F.Name}｜{me.F.Weapon.Name}", 90, H - 80, 16, Color.white, 0f, 0.5f);
            Gui.Bar(new Rect(90, H - 64, 210, 12), (float)(me.Hp / Data.MAX_HP), Js.Hex("#5cffb0"));
            Gui.Label($"擊殺 {me.Kills}　死亡 {me.Deaths}{(me.Power > 0 ? "　⚡強化中" : "")}{(me.DashCd > 0 ? "" : "　衝刺 OK")}", 90, H - 34, 14, Js.Hex("#ffe8a0"), 0f, 0.5f);
            // Kill feed.
            for (int i = 0; i < sim.Feed.Count; i++)
            {
                var f = sim.Feed[i];
                float y = 20 + i * 34, x = W - 20;
                string txt = f.A != null && f.A != f.B ? $"{f.A.F.Name}  →  {f.B.F.Name}" : $"{f.B.F.Name} 倒下了";
                bool involved = (f.A != null && f.A.You) || f.B.You;
                Gui.Rect(x - 250, y - 14, 250, 28, involved ? Js.Hex("#39c6ff", 0.5f) : new Color(0, 0, 0, 0.45f));
                var c = Color.white;
                c.a = Mathf.Clamp01((float)f.T);
                Gui.Label(txt, x - 125, y, 14, c);
            }
            // Minimap.
            float mw = 180, mh = 120, mx = W - mw - 16, my = H - mh - 16;
            Gui.Rect(mx - 2, my - 2, mw + 4, mh + 4, new Color(1, 1, 1, 0.5f));
            Gui.Rect(mx, my, mw, mh, Js.Hex("#2a2140", 0.85f));
            foreach (var c in Data.CRATES) Gui.Rect(mx + (float)(c[0] / Data.ARENA_W) * mw, my + (float)(c[1] / Data.ARENA_H) * mh, (float)(c[2] / Data.ARENA_W) * mw, (float)(c[3] / Data.ARENA_H) * mh, Js.Hex("#b58a5a"));
            foreach (var p in sim.Ps) if (p.Dead <= 0) Gui.Circle(mx + (float)(p.X / Data.ARENA_W) * mw, my + (float)(p.Y / Data.ARENA_H) * mh, p.You ? 4 : 3, p.You ? Js.Hex("#39c6ff") : Js.Hex(p.F.Color));
            if (me.Dead > 0 && sim.Over < 0) Gui.Label($"被擊倒了！{Mathf.CeilToInt((float)me.Dead)} 秒後復活", cx, H * 0.4f, 36, Js.Hex("#ff6b6b"), 0.5f, 0.5f, Color.black);
            if (In.Held(KeyCode.Tab) || sim.Over >= 0)
            {
                var rank = sim.Ranking();
                Gui.Panel(new Rect(cx - 230, 110, 460, 60 + rank.Count * 38));
                Gui.Label(sim.Over >= 0 ? sim.Reason : "記分板", cx, 138, 22, Js.Hex("#ffe066"));
                for (int i = 0; i < rank.Count; i++)
                {
                    var p = rank[i];
                    float y = 184 + i * 38;
                    Gui.Circle(cx - 190, y, 14, Js.Hex(p.F.Color));
                    Gui.Label($"{i + 1}. {p.F.Name}{(p.You ? "（你）" : "")}", cx - 160, y, 18, p.You ? Js.Hex("#39c6ff") : Color.white, 0f, 0.5f);
                    Gui.Label($"{p.Kills} 殺 / {p.Deaths} 死", cx + 200, y, 18, Js.Hex("#ffe8a0"), 1f, 0.5f);
                }
            }
            if (sim.T < 5) Gui.Label("WASD 移動　滑鼠瞄準　按住左鍵射擊　Space 衝刺　Tab 記分板", cx, H - 120, 18, Color.white, 0.5f, 0.5f, Color.black);
        }

        void DrawOver(float W, float H)
        {
            float cx = W / 2;
            int place = stats.FindIndex(s => s.You) + 1;
            Gui.Label(place == 1 ? "冠軍！你是大亂鬥之王！" : $"第 {place} 名", cx, 50, 50, place == 1 ? Js.Hex("#ffe066") : Color.white, 0.5f, 0.5f, Js.Hex("#6b2a8a"));
            Gui.Label(reason, cx, 100, 22, Color.white, 0.5f, 0.5f, Js.Hex("#2a2140"));
            var r = new Rect(W - 360, 150, 330, 30 * stats.Count + 20);
            Gui.Panel(r);
            for (int i = 0; i < stats.Count; i++)
            {
                var s = stats[i];
                var col = s.You ? Js.Hex("#39c6ff") : Color.white;
                Gui.Label($"{i + 1}. {Cast.Name(s.Id)}{(s.You ? "（你）" : "")}", r.x + 20, r.y + 24 + i * 30, 18, col, 0f, 0.5f);
                Gui.Label($"{s.Kills} 殺　{s.Deaths} 死", r.xMax - 20, r.y + 24 + i * 30, 18, Js.Hex("#ffe8a0"), 1f, 0.5f);
            }
            if (Gui.Button(new Rect(cx - 250, H - 100, 230, 62), "再戰一場", 26, Js.Hex("#d9458b"), Js.Hex("#ff6aa8"))) GoArena();
            if (Gui.Button(new Rect(cx + 20, H - 100, 230, 62), "換角色", 26)) GoSelect();
        }
    }
}
