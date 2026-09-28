// 萌友戰機 2026～2027 — top-down 3D presentation of the Game11 sortie (title → pilot → sortie → result).
// Bullets and items are drawn with GPU instancing (Instanced) since there can be hundreds at once.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SfxWave = MoeGames.Wave;

namespace MoeGames.Game11
{
    [MoeGame(11)]
    public class Game11Game : MiniGame
    {
        public override TouchLayout Touch => TouchLayout.TapOnly.Button("炸彈", KeyCode.X).Button("慢速", KeyCode.LeftShift);

        protected override string Backdrop => "space";

        enum Scr { Title, Select, Sortie, Over }

        const float U = 1f / 50f;
        const string HiKey = "moe-strikers-2026-hi";

        Scr scr;
        float t;
        string pilot = "whale";
        CastPicker picker;
        Chibi[] lineup;
        Sortie s;
        int finalScore, hi;
        bool won;
        int stageReached;

        Transform ship;
        Chibi pilotView;
        readonly Dictionary<Enemy, Transform> enemyViews = new Dictionary<Enemy, Transform>();
        Transform bgRoot;
        int bgStage = -1;
        float scroll;

        protected override void Begin()
        {
            Sfx.Define("shot", Tone.Beep(1400, 0.03f, SfxWave.Square, 0.04f, 900));
            Sfx.Define("boom", Tone.Noise(0.35f, 0.35f, FilterType.Lowpass, 1600, 100));
            Sfx.Define("big", Tone.Noise(1.1f, 0.6f, FilterType.Lowpass, 900, 60));
            Sfx.Define("pop", Tone.Beep(600, 0.06f, SfxWave.Square, 0.12f, 200));
            Sfx.Define("item", Tone.Melody("E6 B6", 0.05f, SfxWave.Square, 0.22f));
            Sfx.Define("power", Tone.Melody("C5 E5 G5 C6 E6", 0.05f, SfxWave.Square, 0.28f));
            Sfx.Define("bomb", new Tone { Type = SfxWave.Noise, Duration = 1.4f, Volume = 0.5f, Filter = FilterType.Bandpass, FilterFreq = 300, FilterFreqEnd = 2000, Q = 0.8f });
            Sfx.Define("die", Tone.Melody("G4 D4 A3 D3:3", 0.08f, SfxWave.Sawtooth, 0.35f));
            Sfx.Define("warn", Tone.Melody("A5 E5 A5 E5 A5 E5", 0.16f, SfxWave.Square, 0.3f));
            Sfx.Define("extend", Tone.Melody("C6 G5 C6 E6 G6 C7", 0.07f, SfxWave.Square, 0.35f));
            hi = PlayerPrefs.GetInt(HiKey, 0);
            Rig.Background(Js.Hex("#07071a"));
            Sfx.MusicByName("music", 0.3f);
            GoTitle();
        }

        void GoTitle() { scr = Scr.Title; lineup = ShowLineup(Cast.Ids, "#1a1a4a", "#7fd8ff"); Rig.Background(Js.Hex("#07071a")); }

        void GoSelect()
        {
            scr = Scr.Select;
            var ids = Rules.PILOTS.Select(p => p.Id).ToList();
            picker = new CastPicker(ids, 1);
            picker.Picks.Add(pilot);
            picker.Cursor = ids.IndexOf(pilot);
            lineup = ShowLineup(ids, "#1a1a4a", "#7fd8ff", 1.35f, 1.4f);
        }

        void GoSortie()
        {
            scr = Scr.Sortie;
            s = new Sortie(pilot);
            s.Sound += n => Sfx.Play(n);
            s.Explosion += (x, y, big) => Fx.Burst(W(x, y) + Vector3.up * 0.5f, big ? 40 : 12, big ? Js.Hex("#ff9a3a") : Js.Hex("#ffe08a"), big ? 8f : 4f, big ? 1.0f : 0.5f, big ? 0.2f : 0.1f, 0);
            s.Shake += d => Rig.Shake(0.15f, d);
            ClearWorld();
            enemyViews.Clear();
            bgStage = -1;
            ship = Prim.Empty("ship", World).transform;
            var hull = Js.Hex(s.Pilot.Color);
            Prim.Box(ship, new Vector3(0, 0.1f, 0), new Vector3(0.35f, 0.18f, 1.0f), hull);
            Prim.Box(ship, new Vector3(0, 0.08f, -0.1f), new Vector3(1.3f, 0.06f, 0.4f), Color.Lerp(hull, Color.white, 0.3f));
            Prim.Box(ship, new Vector3(0, 0.25f, -0.4f), new Vector3(0.05f, 0.35f, 0.3f), hull);
            pilotView = SpawnChar(pilot, new Vector3(0, 0.15f, 0.1f), 0.7f, ship);
            pilotView.Face(Vector3.forward);
            float fov = 42f, dist = (float)Rules.PF_H * U / 2 / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) + 1f;
            Rig.Set(new Vector3(0, dist, -3.2f), new Vector3(0, 0, 0.2f), fov);
        }

        void GoOver()
        {
            scr = Scr.Over;
            finalScore = s.Score;
            won = s.Won;
            stageReached = s.Stage;
            if (finalScore > hi) { hi = finalScore; PlayerPrefs.SetInt(HiKey, hi); }
            lineup = ShowLineup(new List<string> { pilot }, "#1a1a4a", "#7fd8ff", 1f, 2.2f);
            if (won) Sfx.Notes("C5 E5 G5 C6 G5 C6:3", 0.1f, SfxWave.Square, 0.4f);
            else Sfx.Notes("G4 F4 E4 D4 C4:3", 0.14f, SfxWave.Triangle, 0.4f);
        }

        static Vector3 W(double x, double y, float h = 0) => new Vector3((float)(x - Rules.PF_W / 2) * U, h, (float)(Rules.PF_H / 2 - y) * U);

        void BuildBackground(int stage)
        {
            if (bgRoot) Destroy(bgRoot.gameObject);
            bgRoot = Prim.Empty("bg", World).transform;
            bool space = Rules.STAGES[stage].Bg == "space";
            Rig.Background(Js.Hex(space ? "#07071a" : "#0a2a4a"));
            float w = (float)Rules.PF_W * U, h = (float)Rules.PF_H * U;
            Prim.Box(bgRoot, new Vector3(0, -1.2f, 0), new Vector3(w, 0.1f, h * 3), Js.Hex(space ? "#0d0d2a" : "#1f6fb0"));
            // Side walls hide the area outside the playfield.
            Prim.Box(bgRoot, new Vector3(-w / 2 - 3, 0, 0), new Vector3(6, 3, h * 3), Js.Hex("#05050f"));
            Prim.Box(bgRoot, new Vector3(w / 2 + 3, 0, 0), new Vector3(6, 3, h * 3), Js.Hex("#05050f"));
            // Original scrolling background (two stacked copies, wrapped in SyncViews).
            bgTiles.Clear();
            for (int i = 0; i < 2; i++)
            {
                var d = Art.Decal(bgRoot, space ? "space" : "ocean", new Vector3(0, -1.1f, 0), w, 0);
                if (!d) break;
                d.sortingOrder = -50;
                bgTiles.Add(d.transform);
            }
            bgStage = stage;
        }

        readonly List<Transform> bgTiles = new List<Transform>();

        void ScrollBackground()
        {
            if (bgTiles.Count < 2) return;
            float w = (float)Rules.PF_W * U;
            var sr = bgTiles[0].GetComponent<SpriteRenderer>();
            float h = w * sr.sprite.rect.height / sr.sprite.rect.width;
            float off = (scroll * U) % h;
            // Tile 1 covers the field at off = 0; both slide down and wrap seamlessly after one tile height.
            float top = (float)Rules.PF_H * U * 0.5f;
            for (int i = 0; i < 2; i++) bgTiles[i].localPosition = new Vector3(0, -1.1f, top + h / 2 - off - i * h + (h < top * 2 ? 0 : 0));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            switch (scr)
            {
                case Scr.Title:
                    if (In.Back) { ExitToHub(); return; }
                    if (In.Confirm || In.MouseDown(0)) { Sfx.Notes("C5 E5 G5", 0.07f, SfxWave.Square, 0.3f); GoSelect(); }
                    break;
                case Scr.Select:
                    if (In.Back) { GoTitle(); return; }
                    if (picker.UpdateKeys(lineup)) Launch();
                    break;
                case Scr.Sortie:
                    if (In.Back) { GoTitle(); return; }
                    var k = new ShipInput
                    {
                        Dx = (In.Held(KeyCode.RightArrow, KeyCode.D) ? 1 : 0) - (In.Held(KeyCode.LeftArrow, KeyCode.A) ? 1 : 0),
                        Dy = (In.Held(KeyCode.DownArrow, KeyCode.S) ? 1 : 0) - (In.Held(KeyCode.UpArrow, KeyCode.W) ? 1 : 0),
                        Slow = In.Held(KeyCode.LeftShift, KeyCode.RightShift),
                        Bomb = In.Down(KeyCode.X, KeyCode.Space),
                    };
                    if (In.MouseHeld(0))
                    {
                        var ray = Cam.ScreenPointToRay(In.MousePos);
                        if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float d))
                        {
                            var p = ray.GetPoint(d);
                            k.Drag = true;
                            k.DragX = p.x / U + Rules.PF_W / 2;
                            k.DragY = Rules.PF_H / 2 - p.z / U + 70;
                        }
                    }
                    if (s.Update(dt, k)) { GoOver(); return; }
                    SyncViews(dt);
                    break;
                case Scr.Over:
                    if (In.Confirm) GoSortie();
                    else if (In.Back) GoSelect();
                    if (lineup != null && lineup.Length > 0 && Mathf.Repeat(t, 0.9f) < dt) lineup[0].Act(won ? "win" : "lose", 0.8f);
                    break;
            }
        }

        void Launch()
        {
            if (picker.Picks.Count > 0) pilot = picker.Picks[0];
            Sfx.Notes("G4 C5 E5 G5", 0.06f, SfxWave.Square, 0.3f);
            GoSortie();
        }

        void SyncViews(float dt)
        {
            if (bgStage != s.Stage) BuildBackground(s.Stage);
            bool space = Rules.STAGES[s.Stage].Bg == "space";
            scroll += dt * (space ? 55 : 40);
            ScrollBackground();
            // Scrolling scenery (clouds or stars) drawn instanced.
            for (int i = 0; i < (space ? 40 : 6); i++)
            {
                float sy = space ? (i * 97 + t * (120 + i % 3 * 90)) % (float)Rules.PF_H : ((i * 173 + t * 130) % ((float)Rules.PF_H + 200)) - 100;
                float sx = (i * 131) % (float)Rules.PF_W;
                if (space) Instanced.Add(W(sx, sy, -0.8f), new Vector3(0.04f, 0.04f, 0.12f + i % 3 * 0.06f), Color.white, true, true);
                else Instanced.Add(W((i * 211) % (float)Rules.PF_W, sy, -0.6f), new Vector3(1.6f, 0.3f, 1.0f), new Color(1, 1, 1, 1), false);
            }
            // Ship.
            bool alive = s.Alive;
            bool blink = s.Invuln > 0 && Mathf.FloorToInt(t * 20) % 2 == 0;
            ship.gameObject.SetActive(alive);
            ship.localPosition = W(s.Px, s.Py, 0.3f);
            ship.localRotation = Quaternion.Euler(0, 0, -((In.Held(KeyCode.RightArrow, KeyCode.D) ? 1 : 0) - (In.Held(KeyCode.LeftArrow, KeyCode.A) ? 1 : 0)) * 20);
            foreach (var r in ship.GetComponentsInChildren<Renderer>()) r.enabled = !blink;
            if (alive && s.Focus) Instanced.Add(W(s.Px, s.Py, 0.9f), Vector3.one * 0.16f, Color.white);
            // Orbiting pages.
            int n = Rules.Orbiters(s.Pilot.Shot, s.Power);
            for (int k = 0; k < n && alive; k++) { var (ox, oy) = s.OrbiterPos(k, n); Instanced.Add(W(ox, oy, 0.4f), new Vector3(0.35f, 0.05f, 0.45f), Js.Hex("#f2e2b6"), true, true, Quaternion.Euler(0, t * 200, 0)); }
            // Bullets & items.
            foreach (var b in s.Pbs)
            {
                float r = (float)b.R * 2 * U;
                if (b.Kind == BulletKind.Laser) Instanced.Add(W(b.X, b.Y, 0.35f), new Vector3(r, r, r * 5), Js.Hex(b.Color), true, true);
                else Instanced.Add(W(b.X, b.Y, 0.35f), Vector3.one * r, Js.Hex(b.Color));
            }
            foreach (var b in s.Ebs) Instanced.Add(W(b.X, b.Y, 0.45f), Vector3.one * (float)b.R * 2.2f * U, Js.Hex(b.Color));
            foreach (var it in s.Items)
                Instanced.Add(W(it.X, it.Y, 0.4f), it.Kind == 'M' ? Vector3.one * 0.35f : Vector3.one * 0.5f, it.Kind == 'P' ? Js.Hex("#ff4a6a") : it.Kind == 'B' ? Js.Hex("#3aa0ff") : Js.Hex("#ffd23a"), true, it.Kind != 'M', Quaternion.Euler(0, t * 180, 0));
            // Enemies.
            foreach (var e in s.Enemies)
            {
                if (!enemyViews.TryGetValue(e, out var v)) { v = MakeEnemy(e.Kind).transform; enemyViews[e] = v; }
                v.localPosition = W(e.X, e.Y, 0.3f);
                var decal = v.GetComponentInChildren<SpriteRenderer>();
                if (decal) decal.color = e.Flash > 0 ? Js.Hex("#ff9090") : Color.white;
                else v.localRotation = Quaternion.Euler(0, e.Boss ? Mathf.Sin(t) * 5 : 180, 0);
                if (e.Flash > 0) Instanced.Add(W(e.X, e.Y, 0.8f), Vector3.one * (float)e.R * 1.6f * U, new Color(1, 1, 1, 1));
            }
            foreach (var gone in enemyViews.Keys.Where(e => !s.Enemies.Contains(e)).ToList()) { Destroy(enemyViews[gone].gameObject); enemyViews.Remove(gone); }
        }

        GameObject MakeEnemy(EnemyKind kind)
        {
            var root = Prim.Empty(kind.ToString(), World);
            float size = (float)Rules.ENEMY[kind].Size * U;
            // Original ship art (fighter/bomber sheets hold two stacked copies → take the first frame).
            string artName = kind == EnemyKind.Midboss ? "bomber" : kind.ToString().ToLowerInvariant();
            int frames = kind == EnemyKind.Fighter || kind == EnemyKind.Bomber || kind == EnemyKind.Midboss ? 2 : 1;
            if (Art.Decal(root.transform, artName, Vector3.zero, size, 0, null, frames, 0)) return root;
            switch (kind)
            {
                case EnemyKind.Drone:
                    Prim.Sphere(root.transform, Vector3.zero, size * 0.7f, Js.Hex("#7a8aa0"));
                    Prim.Sphere(root.transform, new Vector3(0, 0.1f, -size * 0.25f), size * 0.3f, Js.Hex("#ff5a5a"));
                    break;
                case EnemyKind.Fighter:
                    Prim.Box(root.transform, Vector3.zero, new Vector3(size * 0.3f, size * 0.2f, size * 0.9f), Js.Hex("#c04a6a"));
                    Prim.Box(root.transform, new Vector3(0, 0, 0.1f), new Vector3(size, size * 0.08f, size * 0.35f), Js.Hex("#e07a9a"));
                    break;
                case EnemyKind.Bomber:
                    Prim.Box(root.transform, Vector3.zero, new Vector3(size * 0.45f, size * 0.25f, size * 0.9f), Js.Hex("#5a5a7a"));
                    Prim.Box(root.transform, Vector3.zero, new Vector3(size, size * 0.1f, size * 0.4f), Js.Hex("#8a8aaa"));
                    break;
                default:
                    {
                        var col = kind == EnemyKind.Midboss ? Js.Hex("#8a4aa0") : kind == EnemyKind.Boss1 ? Js.Hex("#2a6a8a") : Js.Hex("#6a2a8a");
                        Prim.Sphere(root.transform, Vector3.zero, size * 0.55f, col);
                        Prim.Box(root.transform, Vector3.zero, new Vector3(size, size * 0.12f, size * 0.35f), Color.Lerp(col, Color.white, 0.3f));
                        var core = Prim.Sphere(root.transform, new Vector3(0, size * 0.2f, -size * 0.15f), size * 0.22f, Js.Hex("#ff5ad0"));
                        Prim.SetColor(core, Js.Hex("#ff5ad0"), true);
                        for (int i = -1; i <= 1; i += 2) Prim.Cyl(root.transform, new Vector3(i * size * 0.3f, 0, -size * 0.3f), size * 0.12f, size * 0.3f, Js.Hex("#333344"));
                        break;
                    }
            }
            return root;
        }

        // ── GUI ──

        void OnGUI()
        {
            Gui.Begin();
            float W0 = Gui.W, H = Gui.H, cx = W0 / 2;
            switch (scr)
            {
                case Scr.Title:
                    Gui.TitleCard("萌友戰機 2026～2027", "縱向捲軸射擊　2026 海洋 → 2027 宇宙", null, t, Js.Hex("#7fd8ff"));
                    Gui.Label($"HI {hi:N0}", cx, Gui.H * 0.44f, 22, Js.Hex("#ffe066"), 0.5f, 0.5f, Color.black);
                    break;
                case Scr.Select:
                    Gui.Label("選擇你的駕駛員", cx, 44, 40, Color.white, 0.5f, 0.5f, Js.Hex("#1a1a4a"));
                    picker.Draw(Cam, lineup, 80, (r, i) =>
                    {
                        var p = Rules.PILOTS[i];
                        Gui.Label(p.Name, r.center.x, r.y + 16, 18, Color.white);
                        Gui.Label(p.ShotName, r.center.x, r.y + 36, 13, Js.Hex(p.Color));
                        Gui.Label(p.Desc, r.center.x, r.y + 52, 11, Js.Hex("#dddddd"), 0.5f, 0f, null, r.width - 8);
                    }, "#7fd8ff");
                    if (picker.Picks.Count > 0) pilot = picker.Picks[0];
                    if (Gui.Button(new Rect(cx - 150, H - 70, 300, 56), "出擊！", 28, Js.Hex("#2a6fdb"))) Launch();
                    break;
                case Scr.Sortie: DrawHud(W0, H); break;
                case Scr.Over:
                    Gui.Label(won ? "任務完成！" : "GAME OVER", cx, 70, 64, won ? Js.Hex("#ffe066") : Js.Hex("#ff8a80"), 0.5f, 0.5f, Color.black);
                    Gui.Label($"SCORE {finalScore:N0}　HI {hi:N0}", cx, 140, 30, Color.white, 0.5f, 0.5f, Color.black);
                    Gui.Label($"到達：{Rules.STAGES[stageReached].Year} {Rules.STAGES[stageReached].Name}", cx, 180, 20, Js.Hex("#9fd2ff"), 0.5f, 0.5f, Color.black);
                    if (Gui.Button(new Rect(cx - 250, H - 100, 230, 62), "再出擊", 26, Js.Hex("#2a6fdb"))) GoSortie();
                    if (Gui.Button(new Rect(cx + 20, H - 100, 230, 62), "換駕駛員", 26)) GoSelect();
                    break;
            }
        }

        void DrawHud(float W0, float H)
        {
            var tl = Gui.WorldToGui(Cam, W(0, 0));
            var br = Gui.WorldToGui(Cam, W(Rules.PF_W, Rules.PF_H));
            float lx = tl.x - 10, rx = br.x + 10;
            Gui.Label($"SCORE {s.Score:N0}", 20, 30, 24, Color.white, 0f, 0.5f, Color.black);
            Gui.Label($"HI {Mathf.Max(hi, s.Score):N0}", 20, 60, 18, Js.Hex("#ffe066"), 0f, 0.5f, Color.black);
            Gui.Label($"殘機 {new string('♥', Mathf.Max(0, s.Lives))}", 20, 100, 20, Js.Hex("#ff6b8a"), 0f, 0.5f, Color.black);
            Gui.Label($"炸彈 {new string('●', s.Bombs)}", 20, 130, 20, Js.Hex("#7fd8ff"), 0f, 0.5f, Color.black);
            Gui.Label($"火力 Lv.{s.Power}{(s.Power >= Rules.MAX_POWER ? " MAX" : "")}", 20, 160, 20, Js.Hex("#ffb13d"), 0f, 0.5f, Color.black);
            Gui.Label($"{Rules.STAGES[s.Stage].Year}　{Rules.STAGES[s.Stage].Name}", W0 - 20, 30, 18, Js.Hex("#9fd2ff"), 1f, 0.5f, Color.black);
            Gui.Label($"{s.Pilot.Name}｜{s.Pilot.ShotName}", W0 - 20, 58, 16, Color.white, 1f, 0.5f, Color.black);
            var boss = s.Enemies.FirstOrDefault(e => e.Boss);
            if (boss != null)
            {
                float bw = Mathf.Max(200, br.x - tl.x - 40);
                Gui.Bar(new Rect(W0 / 2 - bw / 2, 16, bw, 12), (float)(boss.Hp / boss.Max), Js.Hex("#ff5ad0"));
            }
            if (s.Phase == Phase.Intro) Gui.Banner($"{Rules.STAGES[s.Stage].Year}　{Rules.STAGES[s.Stage].Name}", "方向鍵移動　Shift 精準移動　X／Space 炸彈　自動射擊");
            if (s.Phase == Phase.Warning && Mathf.FloorToInt(t * 4) % 2 == 0) Gui.Banner("WARNING", "巨大敵機接近中！", -1, Js.Hex("#ff4a4a"));
            if (s.Phase == Phase.Clear) Gui.Banner("STAGE CLEAR", $"通關獎勵 +{s.Bonus:N0}");
            if (s.Phase == Phase.Over) Gui.Banner("GAME OVER", null, -1, Js.Hex("#ff8a80"));
        }
    }
}
