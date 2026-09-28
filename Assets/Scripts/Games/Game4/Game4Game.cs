// 萌友洛克英雄 — 2.5D presentation of the Game4 stage sim (title → hero select → stage select
// → stage → boss → weapon get → … → fortress → citadel → ending). Tiles are 1 world unit.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoeGames.Game4
{
    [MoeGame(4)]
    public class Game4Game : MiniGame
    {
        public override TouchLayout Touch => new TouchLayout().Button("跳", KeyCode.Z).Button("射擊", KeyCode.X).Button("武器", KeyCode.E).Button("E罐", KeyCode.R).Button("M罐", KeyCode.M).Button("暫停", KeyCode.P);

        protected override string Backdrop => "bg_final";

        enum Scr { Title, Heroes, Stages, Play, Ending }

        class PrefsStore : IProgressStore
        {
            const string Key = "moe-rock-heroes-v1";
            public string Load() => PlayerPrefs.GetString(Key, "");
            public void Save(string d) { PlayerPrefs.SetString(Key, d); PlayerPrefs.Save(); }
        }

        const float PX = 1f / Data.TILE;

        Scr scr;
        float t;
        int heroIdx, stageIdx;
        string note = "";
        Chibi[] lineup;
        Stage st;
        string pendingGo;

        // stage views
        Transform tilesRoot, dyn, doorRoot;
        Chibi playerView, bossView;
        GameObject guardianView;
        string bossViewKey;
        readonly List<Transform> moverViews = new List<Transform>();
        readonly List<GameObject> flagViews = new List<GameObject>();
        readonly Dictionary<Enemy, Transform> enemyViews = new Dictionary<Enemy, Transform>();
        readonly Dictionary<Pickup, Transform> pickupViews = new Dictionary<Pickup, Transform>();
        readonly Dictionary<Shot, Transform> shotViews = new Dictionary<Shot, Transform>();
        Transform chargeGlow;
        bool wasGround = true;

        protected override void Begin()
        {
            Progress.UseStore(new PrefsStore());
            Sfx.Define("shoot", Tone.Beep(900, 0.06f, Wave.Square, 0.2f, 500));
            Sfx.Define("charged", Tone.Beep(300, 0.25f, Wave.Sawtooth, 0.25f, 1200));
            Sfx.Define("hit", Tone.Beep(300, 0.08f, Wave.Square, 0.25f, 120));
            Sfx.Define("hurt", Tone.Beep(500, 0.25f, Wave.Square, 0.35f, 90));
            Sfx.Define("jump", Tone.Beep(300, 0.08f, Wave.Square, 0.12f, 600));
            Sfx.Define("land", Tone.Noise(0.05f, 0.15f, FilterType.Lowpass, 500, -1, false));
            Sfx.Define("pick", Tone.Melody("C6 E6 G6 C7", 0.04f, Wave.Square, 0.2f));
            Sfx.Define("boom", Tone.Noise(0.5f, 0.5f, FilterType.Lowpass, 1600, 100));
            Sfx.Define("door", Tone.Beep(80, 0.5f, Wave.Sawtooth, 0.3f));
            Sfx.Define("win", Tone.Melody("C5 E5 G5 C6 E6 G6 C7:4", 0.1f, Wave.Square, 0.35f));
            Sfx.Define("deny", Tone.Beep(200, 0.15f, Wave.Square, 0.25f));
            Rig.Background(Js.Hex("#0a0a14"));
            GoTitle();
        }

        static string Model(string key) => Data.ModelId(key);

        // ── screens ──

        void GoTitle()
        {
            scr = Scr.Title;
            Sfx.StopMusic();
            lineup = ShowLineup(Data.CHARACTERS.Select(c => Model(c.Key)).ToList(), "#2a1040", "#fff3a0");
        }

        void GoHeroes()
        {
            scr = Scr.Heroes;
            heroIdx = Mathf.Max(0, System.Array.FindIndex(Data.CHARACTERS, c => c.Key == (Progress.LastHero ?? "whale")));
            lineup = ShowLineup(Data.CHARACTERS.Select(c => Model(c.Key)).ToList(), "#2a1040", "#fff3a0", 1.35f, 1.4f);
        }

        void GoStages()
        {
            scr = Scr.Stages;
            note = Run.Outcome == "win" ? Run.Message : Run.Outcome == "lose" ? "GAME OVER……再挑戰一次！" : "";
            Run.Outcome = "none";
            st = null;
            RenderSettings.fog = false;
            Rig.Background(Js.Hex("#0a0a14"));
            Sfx.MusicByName("stage_music", 0.22f);
            BuildStageSelectWorld();
        }

        List<string> Keys() => StageSelect.Keys(Run.Hero);

        void BuildStageSelectWorld()
        {
            var keys = Keys();
            string sel = keys[Mathf.Clamp(stageIdx, 0, keys.Count - 1)];
            var ids = new List<string> { Model(Run.Hero) };
            if (sel != "final") ids.Add(Model(sel));
            lineup = ShowLineup(ids, "#2a1040", "#fff3a0", 4.2f, 1.8f);
            if (lineup.Length > 1)
            {
                lineup[0].Face(Vector3.right);
                lineup[1].Face(Vector3.left);
                if (Progress.IsBeaten(Run.Hero, sel)) lineup[1].KnockDown(true);
            }
        }

        void GoPlay()
        {
            scr = Scr.Play;
            st = Stage.Create();
            st.Sound += n => Sfx.Play(n);
            st.Music += m => { if (m == null) Sfx.StopMusic(); else Sfx.MusicByName(m == "boss" ? "boss_music" : "stage_music", 0.3f); };
            st.Burst += (x, y, col, n) => Fx.Burst(W2(x, y), n, Js.Hex(col), 5f, 0.8f, 0.15f, 2f);
            st.Shake += d => Rig.Shake(0.15f, d);
            st.Go += s => pendingGo = s;
            BuildStage();
            Sfx.MusicByName("stage_music", 0.3f);
        }

        void GoEnding()
        {
            scr = Scr.Ending;
            t = 0;
            st = null;
            RenderSettings.fog = false;
            Sfx.StopMusic();
            Sfx.Notes("C5 E5 G5 C6 G5 E5 G5 C6:4", 0.14f, Wave.Square, 0.35f);
            lineup = ShowLineup(Data.CHARACTERS.Select(c => Model(c.Key)).ToList(), "#2a1040", "#fff3a0");
        }

        // ── stage building ──

        static Vector3 W2(double x, double y, float z = 0) => new Vector3((float)x * PX, (float)(Data.VIEW_H - y) * PX, z);

        void BuildStage()
        {
            ClearWorld();
            moverViews.Clear();
            flagViews.Clear();
            enemyViews.Clear();
            pickupViews.Clear();
            shotViews.Clear();
            bossView = null;
            guardianView = null;
            bossViewKey = null;
            var pal = st.Pal;
            var lv = st.Lv;
            Rig.Background(Js.Hex(pal.Sky));
            // Backdrop: sky gradient bands and distant pillars.
            float len = lv.Cols;
            Prim.Box(World, new Vector3(len / 2, 7, 8), new Vector3(len + 60, 30, 0.2f), Js.Hex(pal.Sky));
            Prim.Box(World, new Vector3(len / 2, 2, 7.8f), new Vector3(len + 60, 8, 0.2f), Js.Hex(pal.Sky2));
            // Original stage background, tiled along the level (falls back to boxes).
            var bgTex = Art.Tex("bg_" + lv.Key);
            if (bgTex)
            {
                float bh = 20f, bw = bh * bgTex.width / bgTex.height;
                for (float x = -bw; x < len + bw; x += bw * 0.999f) Art.Backdrop(World, "bg_" + lv.Key, new Vector3(x, 7f, 7.5f), bh);
            }
            var rnd = new System.Random(lv.Key.GetHashCode());
            for (float x = -10; x < len + 10 && !bgTex; x += 3 + (float)rnd.NextDouble() * 4)
            {
                float h = 3 + (float)rnd.NextDouble() * 9;
                Prim.Box(World, new Vector3(x, h / 2 - 1, 5 + (float)rnd.NextDouble() * 2), new Vector3(1.2f + (float)rnd.NextDouble() * 2, h, 1), Color.Lerp(Js.Hex(pal.Sky2), Js.Hex(pal.Detail), 0.5f));
            }
            // Tiles, merged per material.
            tilesRoot = Prim.Empty("Tiles", World).transform;
            var groups = new Dictionary<string, List<CombineInstance>>();
            Mesh cube = CubeMesh();
            void Add(string hex, Vector3 pos, Vector3 size, Quaternion? rot = null)
            {
                if (!groups.TryGetValue(hex, out var l)) groups[hex] = l = new List<CombineInstance>();
                l.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(pos, rot ?? Quaternion.identity, size) });
            }
            for (int r = 0; r < Data.ROWS; r++)
                for (int c = 0; c < lv.Cols; c++)
                {
                    int tt = lv.Tiles[r][c];
                    if (tt == Levels.T_EMPTY) continue;
                    if (c == lv.RoomCol && lv.DoorRows.Contains(r)) continue; // door handled separately
                    var p = new Vector3(c + 0.5f, Data.ROWS - r - 0.5f, 0);
                    switch (tt)
                    {
                        case Levels.T_GROUND: Add(pal.Ground, p, new Vector3(1, 1, 2)); break;
                        case Levels.T_WALL: Add(pal.Detail, p, new Vector3(1, 1, 2)); break;
                        case Levels.T_TOP:
                            Add(pal.Ground, p + Vector3.down * 0.12f, new Vector3(1, 0.76f, 2));
                            Add(pal.Top, p + Vector3.up * 0.38f, new Vector3(1, 0.26f, 2.02f));
                            break;
                        case Levels.T_SPIKE:
                            Add("#555566", p + Vector3.down * 0.4f, new Vector3(1, 0.2f, 2));
                            for (int k = 0; k < 4; k++)
                                for (int zz = -1; zz <= 1; zz++)
                                    Add("#e6e6f0", new Vector3(c + 0.125f + k * 0.25f, Data.ROWS - r - 0.7f, zz * 0.6f), new Vector3(0.18f, 0.18f, 0.18f), Quaternion.Euler(45, 0, 45));
                            break;
                        case Levels.T_LADDER:
                        case Levels.T_LADDER_TOP:
                            Add(pal.Accent, p + new Vector3(-0.3f, 0, -0.6f), new Vector3(0.1f, 1, 0.1f));
                            Add(pal.Accent, p + new Vector3(0.3f, 0, -0.6f), new Vector3(0.1f, 1, 0.1f));
                            for (int k = 0; k < 3; k++) Add(pal.Accent, p + new Vector3(0, -0.33f + k * 0.33f, -0.6f), new Vector3(0.6f, 0.08f, 0.08f));
                            break;
                        case Levels.T_PLATFORM:
                            Add(pal.Top, p + Vector3.up * 0.34f, new Vector3(1, 0.3f, 1.6f));
                            break;
                    }
                }
            foreach (var kv in groups)
            {
                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                var go = new GameObject("tiles_" + kv.Key);
                go.transform.SetParent(tilesRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Get(kv.Key);
            }
            // Boss door.
            doorRoot = Prim.Empty("Door", World).transform;
            foreach (int r in lv.DoorRows) Prim.Box(doorRoot, new Vector3(lv.RoomCol + 0.5f, Data.ROWS - r - 0.5f, 0), new Vector3(1, 1, 2), Js.Hex(pal.Accent));
            // Movers, checkpoints.
            foreach (var m in st.Movers)
            {
                var mv = Prim.Box(World, Vector3.zero, new Vector3((float)m.W * PX, 18 * PX, 1.6f), Js.Hex(pal.Accent)).transform;
                moverViews.Add(mv);
            }
            foreach (var c in lv.Checkpoints)
            {
                var root = Prim.Empty("flag", World, new Vector3(c.col + 0.5f, Data.ROWS - c.row - 1, 0.6f));
                Prim.Box(root.transform, new Vector3(0, 0.75f, 0), new Vector3(0.12f, 1.5f, 0.12f), Js.Hex("#dddddd"));
                var flag = Prim.Box(root.transform, new Vector3(0.4f, 1.3f, 0), new Vector3(0.7f, 0.45f, 0.05f), Js.Hex("#ff6b6b"));
                flagViews.Add(flag);
            }
            dyn = Prim.Empty("Dynamic", World).transform;
            playerView = SpawnChar(Model(st.HeroDef.Key), Vector3.zero, 2f, dyn);
            chargeGlow = Prim.Sphere(dyn, Vector3.zero, 0.4f, Js.Hex("#9ff7ff")).transform;
            Prim.SetColor(chargeGlow.gameObject, Js.Hex("#9ff7ff"), true);
            // Perspective camera that frames the 14-row world height.
            Cam.orthographic = false;
            Cam.fieldOfView = 20f;
            RenderSettings.fog = false;
        }

        static Mesh cubeMesh;
        static Mesh CubeMesh()
        {
            if (cubeMesh) return cubeMesh;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            return cubeMesh;
        }

        // ── update ──

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            switch (scr)
            {
                case Scr.Title:
                    if (In.Back) { ExitToHub(); return; }
                    if (In.Down(KeyCode.Z, KeyCode.Space, KeyCode.Return) || In.MouseDown(0)) GoHeroes();
                    break;
                case Scr.Heroes:
                    if (In.LeftDown) heroIdx = (heroIdx + 7) % 8;
                    if (In.RightDown) heroIdx = (heroIdx + 1) % 8;
                    if (In.UpDown || In.DownDown) heroIdx = (heroIdx + 4) % 8;
                    if (In.Down(KeyCode.Space, KeyCode.Return, KeyCode.Z)) PickHero();
                    if (In.Back) GoTitle();
                    break;
                case Scr.Stages:
                    {
                        int n = 8, before = stageIdx;
                        if (In.LeftDown) stageIdx = (stageIdx + n - 1) % n;
                        if (In.RightDown) stageIdx = (stageIdx + 1) % n;
                        if (In.UpDown) stageIdx = (stageIdx + n - 3) % n;
                        if (In.DownDown) stageIdx = (stageIdx + 3) % n;
                        if (stageIdx != before) BuildStageSelectWorld();
                        if (In.Down(KeyCode.Space, KeyCode.Return, KeyCode.Z)) GoStage(Keys()[stageIdx]);
                        if (In.Back) GoHeroes();
                        if (In.Down(KeyCode.R)) { Progress.Reset(Run.Hero); note = "進度已重置"; BuildStageSelectWorld(); }
                        break;
                    }
                case Scr.Play:
                    UpdatePlay(dt);
                    break;
                case Scr.Ending:
                    if (t > 2 && (In.Down(KeyCode.Z, KeyCode.Space, KeyCode.Return) || In.MouseDown(0))) GoTitle();
                    if (lineup != null && Mathf.Repeat(t, 0.8f) < dt) foreach (var c in lineup) if (c) c.Act("win", 0.7f);
                    break;
            }
        }

        void PickHero()
        {
            var h = Data.CHARACTERS[heroIdx];
            Progress.SetHero(h.Key);
            Run.Hero = h.Key;
            Run.Outcome = "none";
            Sfx.Notes("G5 C6", 0.06f, Wave.Square, 0.3f);
            stageIdx = 0;
            GoStages();
        }

        void GoStage(string key)
        {
            if (key == "final" && !StageSelect.FinalUnlocked(Run.Hero)) { note = "先擊敗全部七位頭目！"; Sfx.Play("deny"); return; }
            Run.Stage = StageSelect.Resolve(Run.Hero, key);
            Sfx.Notes("C5 E5 G5 C6", 0.06f, Wave.Square, 0.3f);
            GoPlay();
        }

        void UpdatePlay(float dt)
        {
            st.ViewW = Gui.W / Gui.H * Data.VIEW_H;
            var k = new PadInput
            {
                Left = In.Held(KeyCode.LeftArrow, KeyCode.A),
                Right = In.Held(KeyCode.RightArrow, KeyCode.D),
                Up = In.Held(KeyCode.UpArrow, KeyCode.W),
                Down = In.Held(KeyCode.DownArrow, KeyCode.S),
                JumpPressed = In.Down(KeyCode.Z, KeyCode.Space, KeyCode.K),
                JumpHeld = In.Held(KeyCode.Z, KeyCode.Space, KeyCode.K),
                ShootPressed = In.Down(KeyCode.X, KeyCode.J),
                ShootHeld = In.Held(KeyCode.X, KeyCode.J),
                Prev = In.Down(KeyCode.Q),
                Next = In.Down(KeyCode.E, KeyCode.Tab),
                UseTank = In.Down(KeyCode.R),
                UseMTank = In.Down(KeyCode.M),
                Pause = In.Down(KeyCode.P),
                Quit = In.Back,
            };
            st.Update(dt, k);
            if (pendingGo != null)
            {
                string g = pendingGo;
                pendingGo = null;
                if (g == "stages") GoStages();
                else if (g == "play") GoPlay();
                else GoEnding();
                return;
            }
            SyncViews(dt);
        }

        void SyncViews(float dt)
        {
            float time = (float)st.T;
            doorRoot.gameObject.SetActive(st.DoorClosed);
            for (int i = 0; i < st.Movers.Count; i++)
            {
                var m = st.Movers[i];
                moverViews[i].localPosition = W2(m.X + m.W / 2, m.Y + 9);
            }
            for (int i = 0; i < st.Lv.Checkpoints.Count; i++)
                Prim.SetColor(flagViews[i], st.Checkpoint.x >= st.Lv.Checkpoints[i].col * Data.TILE ? Js.Hex("#5cffb0") : Js.Hex("#ff6b6b"));

            // Player.
            var p = st.P;
            bool alive = st.Dead <= 0;
            bool blink = st.Iframes > 0 && Mathf.FloorToInt(time * 20) % 2 == 0;
            playerView.gameObject.SetActive(alive);
            playerView.SetVisible(!blink);
            playerView.transform.localPosition = W2(p.X + p.W / 2, p.Y + p.H);
            var faceDir = new Vector3(st.Facing, 0, 0);
            if (st.Climbing) playerView.transform.localRotation = Quaternion.LookRotation(Vector3.forward);
            else if (st.SlideT > 0) playerView.transform.localRotation = Quaternion.LookRotation(faceDir) * Quaternion.Euler(70, 0, 0);
            else playerView.transform.localRotation = Quaternion.Slerp(playerView.transform.localRotation, Quaternion.LookRotation(faceDir + new Vector3(0, 0, -0.35f)), 1 - Mathf.Exp(-18 * dt));
            bool running = p.OnGround && System.Math.Abs(p.Vx) > 1 && st.SlideT <= 0;
            playerView.SetLoop(running ? Chibi.Loop.Run : Chibi.Loop.Idle, 1.3f);
            if (!p.OnGround && wasGround && p.Vy < 0) playerView.Act("jump", 0.5f);
            if (st.ShootAnim > 0.19) playerView.Act("attack", 0.2f);
            wasGround = p.OnGround;
            bool charging = st.WeaponKey == "buster" && st.Charge > Data.PLAYER.chargeMid;
            chargeGlow.gameObject.SetActive(alive && charging);
            if (charging)
            {
                chargeGlow.localPosition = W2(st.Facing > 0 ? p.X + p.W + 6 : p.X - 6, p.Y + 22, -0.3f);
                float s = st.Charge > Data.PLAYER.chargeFull ? 0.6f : 0.35f;
                chargeGlow.localScale = Vector3.one * s * (1 + Mathf.Sin(time * 30) * 0.15f);
                Prim.SetColor(chargeGlow.gameObject, st.Charge > Data.PLAYER.chargeFull ? Js.Hex("#9ff7ff") : Js.Hex("#fff3a0"), true);
            }

            SyncEnemies();
            SyncBoss(time);
            SyncPickups(time);
            SyncShots(time);

            // Camera.
            float viewUnits = (float)st.ViewW * PX;
            float cx = (float)st.CamX * PX + viewUnits / 2;
            float dist = 7f / Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Rig.Set(new Vector3(cx, 7.3f, -dist), new Vector3(cx, 7f, 0), Cam.fieldOfView);
        }

        void SyncEnemies()
        {
            foreach (var e in st.Enemies)
            {
                if (!enemyViews.TryGetValue(e, out var v))
                {
                    v = MakeEnemy(e.Type).transform;
                    enemyViews[e] = v;
                }
                v.localPosition = W2(e.B.X + e.B.W / 2, e.B.Y + e.B.H);
                var sr = v.GetComponentInChildren<SpriteRenderer>();
                if (sr)
                {
                    sr.flipX = e.Dir > 0;
                    sr.color = e.Flash > 0 ? Js.Hex("#ff8080") : Color.white;
                    continue;
                }
                v.localRotation = Quaternion.LookRotation(new Vector3(e.Dir, 0, -0.3f));
                var body = v.GetChild(0).gameObject;
                Prim.SetColor(body, e.Flash > 0 ? Js.Hex("#ff8080") : Js.Hex(e.Type == "W" ? "#e0a030" : e.Type == "F" ? "#50c0e0" : "#a070e0"));
            }
            foreach (var dead in enemyViews.Keys.Where(e => !st.Enemies.Contains(e)).ToList())
            {
                Destroy(enemyViews[dead].gameObject);
                enemyViews.Remove(dead);
            }
        }

        GameObject MakeEnemy(string type)
        {
            var root = Prim.Empty("enemy", dyn);
            string artName = type == "W" ? "walker" : type == "F" ? "flyer" : "turret";
            var art = Art.Standee(root.transform, artName, Vector3.zero, type == "W" ? 1.25f : type == "F" ? 0.94f : 1.06f, false);
            if (art) { Prim.Box(root.transform, Vector3.zero, Vector3.one * 0.01f, Color.white); art.transform.SetAsFirstSibling(); return root; }
            switch (type)
            {
                case "W":
                    Prim.Box(root.transform, new Vector3(0, 0.55f, 0), new Vector3(0.9f, 0.9f, 0.9f), Js.Hex("#e0a030"));
                    Prim.Sphere(root.transform, new Vector3(0, 0.65f, 0.45f), 0.3f, Color.white);
                    Prim.Sphere(root.transform, new Vector3(0, 0.65f, 0.58f), 0.14f, Color.black);
                    Prim.Box(root.transform, new Vector3(-0.25f, 0.08f, 0), new Vector3(0.25f, 0.16f, 0.6f), Js.Hex("#333344"));
                    Prim.Box(root.transform, new Vector3(0.25f, 0.08f, 0), new Vector3(0.25f, 0.16f, 0.6f), Js.Hex("#333344"));
                    break;
                case "F":
                    Prim.Sphere(root.transform, new Vector3(0, 0.45f, 0), 0.8f, Js.Hex("#50c0e0"));
                    Prim.Box(root.transform, new Vector3(0.6f, 0.55f, 0), new Vector3(0.6f, 0.08f, 0.35f), Color.white);
                    Prim.Box(root.transform, new Vector3(-0.6f, 0.55f, 0), new Vector3(0.6f, 0.08f, 0.35f), Color.white);
                    Prim.Sphere(root.transform, new Vector3(0, 0.5f, 0.38f), 0.22f, Js.Hex("#ff4040"));
                    break;
                default:
                    Prim.Cyl(root.transform, new Vector3(0, 0.45f, 0), 0.9f, 0.9f, Js.Hex("#a070e0"));
                    var barrel = Prim.Cyl(root.transform, new Vector3(0, 0.6f, 0.45f), 0.25f, 0.7f, Js.Hex("#444455"));
                    barrel.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    break;
            }
            return root;
        }

        void SyncBoss(float time)
        {
            var B = st.Boss;
            string key = B == null || B.State == "dead" ? null : B.Phase > 0 ? "phase" + B.Phase : B.Hero.Key;
            if (key != bossViewKey)
            {
                if (bossView) Destroy(bossView.gameObject);
                if (guardianView) Destroy(guardianView);
                bossView = null;
                guardianView = null;
                bossViewKey = key;
                if (key != null)
                {
                    if (B.Phase > 0)
                    {
                        var form = Data.FINAL_BOSS_PHASES[B.Phase - 1];
                        guardianView = Prim.Empty("guardian", dyn);
                        float sc = 1 + (B.Phase - 1) * 0.12f;
                        if (Art.Standee(guardianView.transform, "bossforms_" + B.Phase, Vector3.zero, (float)Data.BOSS.h / Data.TILE * sc, false)) { }
                        else {
                        var core = Prim.Box(guardianView.transform, new Vector3(0, 1.6f * sc, 0), Vector3.one * 1.6f * sc, Js.Hex(form.Color));
                        core.transform.localRotation = Quaternion.Euler(45, 0, 45);
                        Prim.SetColor(core, Js.Hex(form.Color), true);
                        Prim.Sphere(guardianView.transform, new Vector3(0, 1.6f * sc, -0.9f * sc), 0.5f * sc, Color.white);
                        for (int i = 0; i < 2 + B.Phase; i++)
                        {
                            var shard = Prim.Box(guardianView.transform, new Vector3(Mathf.Cos(i * 2.1f) * 1.6f, 1.6f * sc + Mathf.Sin(i * 2.1f) * 1.6f, 0), new Vector3(0.3f, 0.9f, 0.3f), Js.Hex(form.Color));
                            shard.transform.localRotation = Quaternion.Euler(0, 0, i * 60);
                        }
                        }
                    }
                    else
                    {
                        bossView = SpawnChar(Model(B.Hero.Key), Vector3.zero, 3.25f, dyn);
                    }
                }
            }
            if (B == null) return;
            bool blink = B.Iframes > 0 && Mathf.FloorToInt(time * 20) % 2 == 0;
            var pos = W2(B.B.X + B.B.W / 2, B.B.Y + B.B.H + 6);
            if (bossView)
            {
                bossView.transform.localPosition = pos;
                bossView.SetVisible(!blink);
                bossView.Face(new Vector3(B.Facing, 0, -0.35f));
                bossView.SetLoop(B.State == "dash" ? Chibi.Loop.Run : Chibi.Loop.Idle, 1.6f);
                if (B.State == "shoot" && B.T < 0.02) bossView.Act("attack", 0.3f);
                if (B.State == "jump" && B.T < 0.02) bossView.Act("jump", 0.6f);
            }
            if (guardianView)
            {
                guardianView.transform.localPosition = pos;
                guardianView.SetActive(!blink);
                if (!guardianView.GetComponentInChildren<SpriteRenderer>()) guardianView.transform.localRotation = Quaternion.Euler(0, time * 60, 0);
                else guardianView.GetComponentInChildren<SpriteRenderer>().flipX = B.Facing < 0;
            }
        }

        void SyncPickups(float time)
        {
            foreach (var k in st.Pickups)
            {
                if (!pickupViews.TryGetValue(k, out var v))
                {
                    string col = k.Kind == 'h' ? "#ff5f7a" : k.Kind == 'e' ? "#65ffc4" : k.Kind == 'm' ? "#e8aaff" : "#5cc8ff";
                    var go = Prim.Box(dyn, Vector3.zero, Vector3.one * (k.Big ? 0.55f : 0.35f), Js.Hex(col));
                    Prim.SetColor(go, Js.Hex(col), true);
                    v = go.transform;
                    pickupViews[k] = v;
                }
                v.localPosition = W2(k.X, k.Y + Mathf.Sin((float)k.T * 5) * 2);
                v.localRotation = Quaternion.Euler(0, time * 90, 0);
            }
            foreach (var gone in pickupViews.Keys.Where(k => !st.Pickups.Contains(k)).ToList())
            {
                Destroy(pickupViews[gone].gameObject);
                pickupViews.Remove(gone);
            }
        }

        void SyncShots(float time)
        {
            foreach (var s in st.Shots)
            {
                if (!shotViews.TryGetValue(s, out var v))
                {
                    GameObject go;
                    var col = Js.Hex(s.Color);
                    if (s.Kind == WeaponKind.Book) go = Prim.Box(dyn, Vector3.zero, new Vector3(0.6f, 0.45f, 0.15f), col);
                    else if (s.Kind == WeaponKind.Claw)
                    {
                        go = Prim.Empty("claw", dyn);
                        for (int i = -1; i <= 1; i++)
                        {
                            var slash = Prim.Box(go.transform, new Vector3(i * 0.22f, 0, 0), new Vector3(0.08f, 0.8f, 0.1f), col);
                            slash.transform.localRotation = Quaternion.Euler(0, 0, -25);
                        }
                    }
                    else if (s.Kind == WeaponKind.Whirl) go = Prim.Cyl(dyn, Vector3.zero, (float)s.R * 2 * PX, 0.5f, col);
                    else go = Prim.Sphere(dyn, Vector3.zero, (float)s.R * 2 * PX, col);
                    Prim.SetColor(go, col, true);
                    v = go.transform;
                    shotViews[s] = v;
                }
                v.localPosition = W2(s.X, s.Y, -0.2f);
                if (s.Kind == WeaponKind.Book) v.localRotation = Quaternion.Euler(0, 0, (float)s.T * 700);
                if (s.Kind == WeaponKind.Whirl) v.localRotation = Quaternion.Euler(0, (float)s.T * 900, 0);
            }
            foreach (var gone in shotViews.Keys.Where(s => !st.Shots.Contains(s)).ToList())
            {
                Destroy(shotViews[gone].gameObject);
                shotViews.Remove(gone);
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
                    Gui.TitleCard("萌友洛克英雄", "擊敗七位頭目，奪取她們的武器！", "按 Z / Space 或點擊開始", t, Js.Hex("#fff3a0"));
                    break;
                case Scr.Heroes: DrawHeroes(W, H); break;
                case Scr.Stages: DrawStages(W, H); break;
                case Scr.Play: DrawBars(W, H); break;
                case Scr.Ending:
                    {
                        var h = Data.GetHero(Run.Hero);
                        Gui.Label("恭喜通關！", cx, 60, 48, Js.Hex("#ffe066"), 0.5f, 0.5f, Js.Hex("#2a1040"));
                        Gui.Label($"{h.Name}突破七連戰，擊敗終焉守護者的三段變形，萌友們又能一起玩了！", cx, 116, 20, Color.white, 0.5f, 0.5f, Color.black);
                        if (t > 2) Gui.Label("按 Z 回到標題", cx, H - 18, 16, Js.Hex("#cccccc"));
                        break;
                    }
            }
        }

        void DrawHeroes(float W, float H)
        {
            float cx = W / 2;
            Gui.Label("選擇你的英雄", cx, 36, 32, Color.white, 0.5f, 0.5f, Js.Hex("#2a1040"));
            float cw = Mathf.Min(150, W / 8.6f);
            for (int i = 0; i < 8; i++)
            {
                var c = Data.CHARACTERS[i];
                if (!lineup[i]) continue;
                var foot = GuiAt(lineup[i].transform.position);
                var head = GuiAt(lineup[i].transform.position + Vector3.up * 1.4f);
                var card = new Rect(foot.x - cw / 2, foot.y + 8, cw, 70);
                var hit = new Rect(card.x, head.y, cw, card.yMax - head.y);
                bool sel = heroIdx == i;
                Gui.Panel(card, sel ? Js.Hex("#3a2a70", 0.93f) : Js.Hex("#0d1433", 0.8f), sel ? Js.Hex("#ffe066") : new Color(1, 1, 1, 0.27f));
                Gui.Label(c.Name, card.center.x, card.y + 18, 18, sel ? Js.Hex("#ffe066") : Color.white);
                Gui.Label(c.Weapon.Name, card.center.x, card.y + 42, 13, Js.Hex(c.Weapon.Color));
                if (Progress.IsCleared(c.Key)) Gui.Label("★通關", card.x + 8, card.y + 60, 12, Js.Hex("#ffe066"), 0f, 0.5f);
                if (Gui.Clicked(hit)) { if (heroIdx == i) PickHero(); else { heroIdx = i; lineup[i].Act("hop", 0.35f); } }
            }
            var h = Data.CHARACTERS[heroIdx];
            Gui.Label($"{h.Name}｜{h.Title}　招牌武器：{h.Weapon.Name} — {h.Weapon.Desc}", cx, H - 20, 16, Color.white, 0.5f, 0.5f, Color.black);
        }

        void DrawStages(float W, float H)
        {
            float cx = W / 2;
            var keys = Keys();
            string sel = keys[stageIdx];
            Gui.Label("STAGE SELECT", cx, 22, 24, Js.Hex("#ffe066"), 0.5f, 0.5f, Js.Hex("#2a1040"));
            const float cw = 150, ch = 100, gap = 10;
            float gx = cx - (3 * cw + 2 * gap) / 2, gy = 44;
            int[] order = { 0, 1, 2, 3, -1, 4, 5, 6, 7 };
            for (int cell = 0; cell < 9; cell++)
            {
                int ki = order[cell];
                var r = new Rect(gx + (cell % 3) * (cw + gap), gy + (cell / 3) * (ch + gap), cw, ch);
                if (ki < 0)
                {
                    Gui.Panel(r, Js.Hex("#1d3a6a", 0.8f), Js.Hex("#9ff7ff"));
                    Gui.Label(Data.GetHero(Run.Hero).Name, r.center.x, r.center.y, 22, Js.Hex("#9ff7ff"));
                    continue;
                }
                string key = keys[ki];
                bool isSel = ki == stageIdx;
                bool beaten = key != "final" && Progress.IsBeaten(Run.Hero, key);
                bool locked = key == "final" && !StageSelect.FinalUnlocked(Run.Hero);
                Gui.Panel(r, isSel ? Js.Hex("#3a2a70", 0.93f) : Js.Hex("#0d1433", 0.8f), isSel ? (Mathf.FloorToInt(t * 6) % 2 == 1 ? Js.Hex("#ffe066") : Color.white) : new Color(1, 1, 1, 0.27f));
                if (key == "final")
                {
                    Gui.Label(locked ? "🔒" : "☠", r.center.x, r.y + 40, 36, locked ? Js.Hex("#888888") : Js.Hex("#ff5fd2"));
                    Gui.Label(Progress.CitadelCheckpoint(Run.Hero) >= 0 ? Data.CITADEL_STAGE : Data.FINAL_STAGE, r.center.x, r.yMax - 18, 15, locked ? Js.Hex("#888888") : Color.white);
                }
                else
                {
                    var h = Data.GetHero(key);
                    Gui.Circle(r.center.x, r.y + 38, 24, beaten ? Js.Hex("#666666") : Js.Hex(h.Weapon.Color));
                    Gui.Label(h.Name.Substring(0, 1), r.center.x, r.y + 38, 24, Color.white, 0.5f, 0.5f, Color.black);
                    Gui.Label(h.Name, r.center.x, r.yMax - 16, 15, Color.white);
                    if (beaten) Gui.Label("擊敗", r.center.x, r.y + 40, 22, Js.Hex("#ff6b6b"), 0.5f, 0.5f, Color.black);
                }
                if (Gui.Clicked(r)) { if (stageIdx == ki) GoStage(key); else { stageIdx = ki; BuildStageSelectWorld(); } }
            }
            string info;
            if (sel == "final")
            {
                int phase = Progress.CitadelCheckpoint(Run.Hero);
                info = phase >= 0 ? $"{Data.CITADEL_STAGE}：從終焉守護者第 {phase + 1} 形態續戰" : $"{Data.FINAL_STAGE}：{Data.FINAL_TITLE}（七位頭目連戰）";
            }
            else
            {
                var h = Data.GetHero(sel);
                string weak = Data.WeaknessFor(sel, Run.Hero);
                bool b = Progress.IsBeaten(Run.Hero, weak);
                string hint = weak == "charge" ? "蓄力射擊" : b ? Data.GetHero(weak).Weapon.Name : "？？？";
                info = $"{h.Stage}　頭目：{h.Name}　弱點：{hint}";
            }
            Gui.Label(info, cx, H - 40, 17, Color.white, 0.5f, 0.5f, Color.black);
            Gui.Label(note != "" ? note : "方向鍵選擇　Z 出擊　Esc 換英雄　R 重置進度", cx, H - 16, 14, note != "" ? Js.Hex("#ffe066") : Js.Hex("#cccccc"), 0.5f, 0.5f, Color.black);
        }

        void DrawBars(float W, float H)
        {
            if (st == null) return;
            void Bar(float x, double v, double max, string col)
            {
                Gui.Rect(x - 2, 14, 18, 176, new Color(0, 0, 0, 0.75f));
                int n = 28;
                float seg = 172f / n;
                int on = Js.RoundInt(v / max * n);
                for (int i = 0; i < n; i++)
                    Gui.Rect(x, 16 + (n - 1 - i) * seg, 14, seg - 1, i < on ? Js.Hex(col) : new Color(1, 1, 1, 0.12f));
            }
            Bar(18, System.Math.Max(0, st.Hp), Data.PLAYER.maxHp, "#fff3a0");
            string wk = st.WeaponKey;
            if (wk != "buster") Bar(40, st.Ammo[wk], Data.PLAYER.maxAmmo, Data.GetHero(wk).Weapon.Color);
            if (st.StageKey == "citadel")
            {
                for (int i = 0; i < Data.FINAL_BOSS_PHASES.Length; i++)
                {
                    var form = Data.FINAL_BOSS_PHASES[i];
                    int phase = i + 1;
                    double v = phase <= st.FinalPhaseDone ? 0 : st.Boss != null && phase == st.Boss.Phase ? (st.Boss.State == "intro" ? st.BossBar : st.Boss.Hp) : form.Hp;
                    Bar(W - 36 - i * 24, v, form.Hp, form.Color);
                }
                if (st.Boss != null) Gui.Label($"終焉守護者 {st.Boss.Phase}/3", W - 130, 204, 14, Color.white, 0.5f, 0.5f, Color.black);
            }
            else if (st.Boss != null && st.Boss.State != "dead")
                Bar(W - 36, st.Boss.State == "intro" ? st.BossBar : st.Boss.Hp, st.StageKey == "final" ? Data.BOSS.rushBossHp : Data.BOSS.maxHp, "#ff6b6b");
            string wname = wk == "buster" ? Data.BUSTER.Name : Data.GetHero(wk).Weapon.Name;
            Gui.Label($"{wname}　殘機 ×{Mathf.Max(0, st.Lives)}　E ×{st.ETanks}　M ×{st.MTanks}", 66, 22, 16, Color.white, 0f, 0.5f, Color.black);
            if (st.Weapons.Count > 1) Gui.Label("Q/E 切換武器　R E 罐　M M 罐　P 暫停", 66, 44, 13, Js.Hex("#cccccc"), 0f, 0.5f, Color.black);
            if (st.MsgT > 0) { var c = Js.Hex("#ffe066"); c.a = Mathf.Clamp01((float)st.MsgT * 2); Gui.Label(st.Msg, W / 2, H * 0.35f, 34, c, 0.5f, 0.5f, Js.Hex("#1b1040")); }
            if (st.RewardT > 0) { var c = Js.Hex("#96ffbf"); c.a = Mathf.Clamp01((float)st.RewardT * 2); Gui.Label(st.RewardMsg, W / 2, H * 0.45f, 26, c, 0.5f, 0.5f, Js.Hex("#1b1040")); }
            if (st.T < 6) Gui.Label("←→ 移動　Z 跳　X 射擊　↓+Z 滑行　R E 罐　M M 罐　P 暫停　Esc 離開", W / 2, H - 16, 15, Color.white, 0.5f, 0.5f, Color.black);
            if (st.Paused)
            {
                Gui.Rect(0, 0, W, H, Js.Hex("#070b20", 0.78f));
                Gui.Label("遊戲暫停", W / 2, H * 0.42f, 44, Js.Hex("#ffe8a0"), 0.5f, 0.5f, Js.Hex("#1b1040"));
                Gui.Label("按 P 繼續", W / 2, H * 0.56f, 22, Color.white, 0.5f, 0.5f, Color.black);
            }
        }
    }
}
