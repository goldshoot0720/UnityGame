// 萌友瘋狂坦克 — 2.5D presentation of the Game10 battle (title → select → battle → result).
// The heightmap becomes an extruded mesh that is rebuilt whenever an explosion carves it.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoeGames.Game10
{
    [MoeGame(10)]
    public class Game10Game : MiniGame
    {
        public override TouchLayout Touch => new TouchLayout().Button("發射", KeyCode.Space);

        enum Scr { Title, Select, Battle, Over }

        const float U = 1f / 50f;
        const int STEP = 4; // terrain mesh column width in field px

        Scr scr;
        float t;
        string hero = "whale";
        CastPicker picker;
        Chibi[] lineup;
        Battle b;
        List<Result> results = new List<Result>();
        string reason = "";
        int meshVersion = -1;
        MeshFilter dirtMesh, topMesh;
        readonly Dictionary<Tank, Transform> tankViews = new Dictionary<Tank, Transform>();
        readonly Dictionary<Tank, Transform> barrels = new Dictionary<Tank, Transform>();
        readonly Dictionary<Tank, Chibi> drivers = new Dictionary<Tank, Chibi>();
        readonly Dictionary<Shell, Transform> shellViews = new Dictionary<Shell, Transform>();
        readonly Dictionary<Crate, Transform> crateViews = new Dictionary<Crate, Transform>();
        float camX;

        protected override void Begin()
        {
            Sfx.Define("fire", Tone.Noise(0.25f, 0.35f, FilterType.Lowpass, 1800, 300, false));
            Sfx.Define("boom", Tone.Noise(0.5f, 0.5f, FilterType.Lowpass, 1200, 80));
            Sfx.Define("hurt", Tone.Beep(320, 0.14f, Wave.Square, 0.25f, 110));
            Sfx.Define("heal", Tone.Melody("C5 E5 G5 C6", 0.06f, Wave.Triangle, 0.3f));
            Sfx.Define("freeze", Tone.Melody("E6 B5 G#5", 0.05f, Wave.Triangle, 0.3f));
            Sfx.Define("splash", new Tone { Type = Wave.Noise, Duration = 0.6f, Volume = 0.35f, Filter = FilterType.Bandpass, FilterFreq = 900, FilterFreqEnd = 300, Q = 1 });
            Sfx.Define("tick", Tone.Beep(880, 0.03f, Wave.Square, 0.1f));
            Sfx.Define("pick", Tone.Melody("G5 C6 E6", 0.05f, Wave.Square, 0.25f));
            Rig.Background(Js.Hex("#7fd3ff"));
            Sfx.MusicByName("music", 0.28f);
            GoTitle();
        }

        void GoTitle() { scr = Scr.Title; lineup = ShowLineup(Cast.Ids, "#3a5a2a", "#ffd84a"); Rig.Background(Js.Hex("#7fd3ff")); }

        void GoSelect()
        {
            scr = Scr.Select;
            var ids = Rules.HEROES.Select(h => h.Id).ToList();
            picker = new CastPicker(ids, 1);
            picker.Picks.Add(hero);
            picker.Cursor = ids.IndexOf(hero);
            lineup = ShowLineup(ids, "#3a5a2a", "#ffd84a", 1.35f, 1.4f);
        }

        void GoBattle()
        {
            scr = Scr.Battle;
            b = new Battle(hero);
            b.Sound += n => Sfx.Play(n);
            b.Float += (x, y, text, col) => Popups.Add(W(x, y), text, Js.Hex(col), 22f, 1.6f, 0.9f);
            b.Burst += (x, y, cols, n) => { foreach (var c in cols) Fx.Burst(W(x, y), Mathf.Max(2, n / cols.Length), Js.Hex(c), 5f, 0.6f, 0.12f, 6f); };
            b.Shake += a => Rig.Shake(a * 0.02f, 0.3f);
            BuildField();
        }

        void GoOver()
        {
            scr = Scr.Over;
            results = b.Results;
            reason = b.Reason;
            lineup = ShowLineup(results.Select(r => r.Id).ToList(), "#3a5a2a", "#ffd84a", 1.5f, 1.6f);
            bool won = results.Count > 0 && results[0].You;
            if (won) Sfx.Notes("C5 E5 G5 C6 G5 C6:3", 0.1f, Wave.Square, 0.4f);
            else Sfx.Notes("G4 F4 E4 D4 C4:3", 0.14f, Wave.Triangle, 0.4f);
        }

        static Vector3 W(double x, double y, float z = 0) => new Vector3((float)x * U, (float)(Rules.FIELD_H - y) * U, z);

        void BuildField()
        {
            ClearWorld();
            tankViews.Clear(); barrels.Clear(); drivers.Clear(); shellViews.Clear(); crateViews.Clear();
            meshVersion = -1;
            var style = Rules.MAPS[b.Map];
            Rig.Background(Js.Hex(style.Sky0));
            Prim.Sphere(World, new Vector3(Rules.FIELD_W * U * 0.8f, 12, 30), 5, Js.Hex("#fff6c0"));
            dirtMesh = NewMeshObject("dirt", style.Dirt);
            topMesh = NewMeshObject("top", style.Top);
            // Water.
            float wy = (Rules.FIELD_H - Rules.WATER_Y) * U;
            Prim.Box(World, new Vector3(Rules.FIELD_W * U / 2, wy / 2 - 0.5f, 0), new Vector3(Rules.FIELD_W * U + 20, wy + 1, 3.4f), Js.Hex("#3a8fe0"));
            // Distant hills.
            for (int i = 0; i < 10; i++) Prim.Sphere(World, new Vector3(i * 5.5f, -1, 14 + (i % 3) * 2), 10 + (i % 4) * 2, Color.Lerp(Js.Hex(style.Top), Js.Hex(style.Sky0), 0.55f));
            foreach (var tk in b.Tanks)
            {
                var root = Prim.Empty("tank" + tk.I, World).transform;
                var col = Js.Hex(tk.Color);
                Prim.Box(root, new Vector3(0, 0.2f, 0), new Vector3(1.0f, 0.35f, 0.8f), col);
                Prim.Box(root, new Vector3(0, 0.06f, 0), new Vector3(1.1f, 0.14f, 0.9f), Js.Hex("#333344"));
                Prim.Sphere(root, new Vector3(0, 0.42f, 0), 0.5f, Color.Lerp(col, Color.white, 0.3f));
                var pivot = Prim.Empty("barrel", root, new Vector3(0, 0.5f, -0.2f)).transform;
                var barrel = Prim.Cyl(pivot, new Vector3(0, 0, 0.35f), 0.12f, 0.7f, Js.Hex("#444455"));
                barrel.transform.localRotation = Quaternion.Euler(90, 0, 0);
                barrels[tk] = pivot;
                var d = SpawnChar(tk.Hero.Id, new Vector3(0, 0.35f, 0.25f), 0.75f, root);
                d.Face(Vector3.back);
                drivers[tk] = d;
                tankViews[tk] = root;
            }
            camX = b.Tanks[0].X.ToFloat();
        }

        MeshFilter NewMeshObject(string name, string hex)
        {
            var go = new GameObject(name);
            go.transform.SetParent(World, false);
            var mf = go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Get(hex);
            mf.sharedMesh = new Mesh { name = name };
            return mf;
        }

        /// <summary>Extrude the heightmap: a front face + top surface (dirt), and a thin top band (grass/snow/sand).</summary>
        void RebuildTerrain()
        {
            var tr = b.Terrain;
            const float depth = 1.6f;
            BuildStrip(dirtMesh.sharedMesh, x => W(x, Rules.Surface(tr, x)).y - 0.12f, x => -1.5f, depth);
            BuildStrip(topMesh.sharedMesh, x => W(x, Rules.Surface(tr, x)).y, x => W(x, Rules.Surface(tr, x)).y - 0.14f, depth + 0.02f);
        }

        static void BuildStrip(Mesh mesh, System.Func<int, float> top, System.Func<int, float> bottom, float depth)
        {
            var v = new List<Vector3>();
            var tri = new List<int>();
            for (int x = 0; x < Rules.FIELD_W; x += STEP)
            {
                int x2 = Mathf.Min(Rules.FIELD_W, x + STEP);
                float ax = x * U, bx = x2 * U, at = top(x), bt = top(x2), ab = bottom(x), bb = bottom(x2);
                int i = v.Count;
                // Front face (z = -depth).
                v.Add(new Vector3(ax, ab, -depth)); v.Add(new Vector3(ax, at, -depth)); v.Add(new Vector3(bx, bt, -depth)); v.Add(new Vector3(bx, bb, -depth));
                tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                // Top face.
                i = v.Count;
                v.Add(new Vector3(ax, at, -depth)); v.Add(new Vector3(ax, at, depth)); v.Add(new Vector3(bx, bt, depth)); v.Add(new Vector3(bx, bt, -depth));
                tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
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
                    if (picker.UpdateKeys(lineup)) StartBattle();
                    break;
                case Scr.Battle:
                    if (In.Back) { GoTitle(); return; }
                    var k = new TankInput
                    {
                        Left = In.Held(KeyCode.A, KeyCode.LeftArrow), Right = In.Held(KeyCode.D, KeyCode.RightArrow),
                        Up = In.Held(KeyCode.W, KeyCode.UpArrow), Down = In.Held(KeyCode.S, KeyCode.DownArrow),
                        FirePressed = In.Down(KeyCode.Space), FireHeld = In.Held(KeyCode.Space),
                        PanL = In.Held(KeyCode.Q), PanR = In.Held(KeyCode.E), Pick = In.DigitDown,
                    };
                    if (b.Update(dt, k)) { GoOver(); return; }
                    SyncViews(dt);
                    break;
                case Scr.Over:
                    if (In.Confirm) GoBattle();
                    else if (In.Back) GoSelect();
                    if (lineup != null && lineup.Length > 0 && Mathf.Repeat(t, 0.8f) < dt) lineup[0].Act("win", 0.7f);
                    break;
            }
        }

        void StartBattle()
        {
            if (picker.Picks.Count > 0) hero = picker.Picks[0];
            Sfx.Notes("G4 C5 E5 G5", 0.06f, Wave.Square, 0.3f);
            GoBattle();
        }

        void SyncViews(float dt)
        {
            if (meshVersion != b.TerrainVersion) { meshVersion = b.TerrainVersion; RebuildTerrain(); }
            foreach (var tk in b.Tanks)
            {
                var v = tankViews[tk];
                v.gameObject.SetActive(tk.Alive);
                if (!tk.Alive) continue;
                v.localPosition = W(tk.X, tk.Y);
                // Tilt with the ground slope.
                double slope = (Rules.Surface(b.Terrain, tk.X + 12) - Rules.Surface(b.Terrain, tk.X - 12)) / 24.0;
                v.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan((float)slope) * Mathf.Rad2Deg);
                barrels[tk].localRotation = Quaternion.LookRotation(new Vector3(tk.Facing * Mathf.Cos((float)tk.Elev * Mathf.Deg2Rad), Mathf.Sin((float)tk.Elev * Mathf.Deg2Rad), 0));
                if (tk.Flash > 0.2) drivers[tk].Act("hit", 0.3f);
                Prim.SetColor(v.GetChild(0).gameObject, tk.Frozen ? Js.Hex("#bff4ff") : Js.Hex(tk.Color), tk.Frozen);
            }
            foreach (var s in b.Shells)
            {
                if (!shellViews.TryGetValue(s, out var tr))
                {
                    var go = Prim.Sphere(World, Vector3.zero, s.W.Kind == ShotKind.Laser ? 0.3f : 0.25f, Js.Hex(s.W.Color));
                    Prim.SetColor(go, Js.Hex(s.W.Color), true);
                    tr = go.transform;
                    shellViews[s] = tr;
                }
                tr.localPosition = W(s.X, s.Y);
                if (Random.value < 0.4f) Fx.Burst(tr.position, 1, Js.Hex(s.W.Color), 0.3f, 0.3f, 0.08f, 0);
            }
            foreach (var gone in shellViews.Keys.Where(s => !b.Shells.Contains(s)).ToList()) { Destroy(shellViews[gone].gameObject); shellViews.Remove(gone); }
            foreach (var c in b.Crates)
            {
                if (!crateViews.TryGetValue(c, out var tr))
                {
                    var go = Prim.Box(World, Vector3.zero, Vector3.one * 0.6f, c.Kind == "ammo" ? Js.Hex("#ffd84a") : Js.Hex("#8dff9a"));
                    Prim.Box(go.transform, Vector3.up * 0.7f, new Vector3(1.4f, 0.05f, 1.4f), Color.white);
                    tr = go.transform;
                    crateViews[c] = tr;
                }
                tr.localPosition = W(c.X, c.Y) + Vector3.up * 0.3f;
                tr.GetChild(0).gameObject.SetActive(!c.Landed);
            }
            foreach (var gone in crateViews.Keys.Where(c => !b.Crates.Contains(c)).ToList()) { Destroy(crateViews[gone].gameObject); crateViews.Remove(gone); }
            // Camera follows the current tank (or the shell in flight).
            float focus = (float)(b.Tk.X + b.Look);
            if (b.Shells.Count > 0) focus = (float)b.Shells[0].X;
            float viewW = Gui.W / Gui.H * Rules.FIELD_H;
            float target = Mathf.Clamp(focus - viewW / 2, 0, Mathf.Max(0, Rules.FIELD_W - viewW)) + viewW / 2;
            camX += (target - camX) * Mathf.Min(1, dt * (b.Shells.Count > 0 ? 6 : 3));
            float fov = 34f, dist = Rules.FIELD_H * U / 2 / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            var c0 = new Vector3(camX * U, Rules.FIELD_H * U / 2, 0);
            Rig.Set(c0 + new Vector3(0, 0.8f, -dist), c0, fov);
        }

        // ── GUI ──

        void OnGUI()
        {
            Gui.Begin();
            float W0 = Gui.W, H = Gui.H, cx = W0 / 2;
            switch (scr)
            {
                case Scr.Title:
                    Gui.TitleCard("萌友瘋狂坦克", "回合制砲擊　四輛坦克混戰　最後存活者獲勝", null, t, Js.Hex("#ffe066"));
                    break;
                case Scr.Select:
                    Gui.Label("選擇你的駕駛員", cx, 44, 40, Color.white, 0.5f, 0.5f, Js.Hex("#3a5a2a"));
                    picker.Draw(Cam, lineup, 80, (r, i) =>
                    {
                        var h = Rules.HEROES[i];
                        Gui.Label(h.Name, r.center.x, r.y + 16, 18, Color.white);
                        Gui.Label(h.Special.Name, r.center.x, r.y + 36, 13, Js.Hex(h.Special.Color));
                        Gui.Label(h.Special.Desc, r.center.x, r.y + 52, 11, Js.Hex("#dddddd"), 0.5f, 0f, null, r.width - 8);
                    }, "#ffd84a");
                    if (picker.Picks.Count > 0) hero = picker.Picks[0];
                    if (Gui.Button(new Rect(cx - 150, H - 70, 300, 56), "出擊！", 28, Js.Hex("#d9452b"))) StartBattle();
                    break;
                case Scr.Battle: DrawHud(W0, H); break;
                case Scr.Over: DrawOver(W0, H); break;
            }
        }

        void DrawHud(float W0, float H)
        {
            float cx = W0 / 2;
            foreach (var tk in b.Tanks)
            {
                if (!tk.Alive) continue;
                var sp = Gui.WorldToGui(Cam, W(tk.X, tk.Y - 70));
                if (sp.z < 0) continue;
                Gui.Rect(sp.x - 28, sp.y, 56, 7, new Color(0, 0, 0, 0.6f));
                float r = Mathf.Clamp01((float)(tk.Hp / Rules.MAX_HP));
                Gui.Rect(sp.x - 27, sp.y + 1, 54 * r, 5, r > 0.5f ? Js.Hex("#5cffb0") : r > 0.25f ? Js.Hex("#ffd23f") : Js.Hex("#ff5f3a"));
                Gui.Label((tk.You ? "▼" : "") + tk.Hero.Name, sp.x, sp.y - 12, 14, tk == b.Tk ? Js.Hex("#ffe066") : Color.white, 0.5f, 0.5f, Color.black);
            }
            var cur = b.Tk;
            // Wind.
            Gui.Panel(new Rect(cx - 110, 10, 220, 60));
            Gui.Label($"風 {(b.Wind > 0 ? "→" : b.Wind < 0 ? "←" : "・")} {System.Math.Abs(b.Wind)}", cx, 28, 20, Color.white);
            Gui.Bar(new Rect(cx - 90, 46, 180, 10), 0, Color.white);
            float wf = (float)(b.Wind / Rules.MAX_WIND);
            Gui.Rect(cx + Mathf.Min(0, wf) * 90, 46, Mathf.Abs(wf) * 90, 10, Js.Hex("#7fe0ff"));
            // Current tank panel.
            Gui.Panel(new Rect(16, H - 150, 360, 134));
            Gui.Label($"{cur.Hero.Name}{(cur.You ? "（你）" : "")}　第 {b.Turn} 回合", 30, H - 132, 18, Js.Hex(cur.Color), 0f, 0.5f);
            Gui.Label($"角度 {cur.Elev:F0}°　燃料 {cur.Fuel:F0}　時間 {Mathf.Max(0, Mathf.CeilToInt((float)b.Clock))}", 30, H - 106, 15, Color.white, 0f, 0.5f);
            Gui.Label("力道", 30, H - 80, 15, Color.white, 0f, 0.5f);
            Gui.Bar(new Rect(70, H - 88, 290, 16), (float)(cur.Power / 100), Js.Hex("#ffb13d"));
            if (cur.LastPower > 0) Gui.Rect(70 + 290 * (float)(cur.LastPower / 100) - 1, H - 92, 3, 24, Color.white);
            for (int i = 0; i < 4; i++)
            {
                var w = cur.Weapons[i];
                var r = new Rect(30 + i * 84, H - 64, 80, 42);
                string uses = cur.Uses[i] < 0 ? "∞" : cur.Uses[i].ToString();
                if (Gui.Button(r, $"{i + 1}.{w.Name}\n{uses}", 12, cur.Uses[i] == 0 ? Js.Hex("#444455") : Js.Hex("#26304f"), null, cur.Weapon == i) && cur.You) b.Pick(cur, i);
            }
            if (b.MsgT > 0) { var c = Color.white; c.a = Mathf.Clamp01((float)b.MsgT * 2); Gui.Label(b.Msg, cx, 100, 26, c, 0.5f, 0.5f, Color.black); }
            Gui.Label("A/D 移動　W/S 角度　按住 Space 蓄力放開發射　1–4 武器　Q/E 看地圖", cx, H - 10, 14, Color.white, 0.5f, 1f, Color.black);
        }

        void DrawOver(float W0, float H)
        {
            float cx = W0 / 2;
            int place = results.FindIndex(r => r.You) + 1;
            Gui.Label(place == 1 ? "勝利！最後的坦克！" : $"第 {place} 名", cx, 50, 50, place == 1 ? Js.Hex("#ffe066") : Color.white, 0.5f, 0.5f, Js.Hex("#3a5a2a"));
            Gui.Label(reason, cx, 100, 22, Color.white, 0.5f, 0.5f, Color.black);
            var r0 = new Rect(W0 - 380, 150, 350, 34 * results.Count + 20);
            Gui.Panel(r0);
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                Gui.Label($"{i + 1}. {r.Name}{(r.You ? "（你）" : "")}", r0.x + 16, r0.y + 26 + i * 34, 17, r.You ? Js.Hex("#39c6ff") : Color.white, 0f, 0.5f);
                Gui.Label($"HP {r.Hp:F0}　傷害 {r.Dmg:F0}", r0.xMax - 16, r0.y + 26 + i * 34, 17, Js.Hex("#ffe8a0"), 1f, 0.5f);
            }
            if (Gui.Button(new Rect(cx - 250, H - 100, 230, 62), "再戰一場", 26, Js.Hex("#d9452b"))) GoBattle();
            if (Gui.Button(new Rect(cx + 20, H - 100, 230, 62), "換駕駛員", 26)) GoSelect();
        }
    }

    static class DoubleExt { public static float ToFloat(this double d) => (float)d; }
}
