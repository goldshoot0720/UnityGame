// 萌友格鬥王 — 2.5D presentation of the Game5 fight sim (title → select → stage → fight → result),
// 1P vs CPU (3 difficulties) or 2P local versus. 60 Hz fixed-step like scenes/fight.ts.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoeGames.Game5
{
    [MoeGame(5)]
    public class Game5Game : MiniGame
    {
        public override TouchLayout Touch => new TouchLayout().Button("輕拳", KeyCode.J).Button("重拳", KeyCode.K).Button("必殺", KeyCode.L).Button("輕腳", KeyCode.U).Button("重腳", KeyCode.I).Button("超必", KeyCode.O);

        protected override string Backdrop => "stages_1";

        enum Scr { Title, Select, Fight, Over }
        enum Phase { Intro, Fight, Ko }

        const float STEP = 1f / 60f;
        const float U = 1f / 110f; // world units per sim px

        Scr scr;
        float t;
        // session
        string mode = "cpu";
        int p1, p2 = 1, stage, difficulty = 1, winner = -1;
        int[] score = { 0, 0 };
        int titleSel;
        // select
        int selStep;
        int[] cur = { 0, 1 };
        Chibi[] lineup;
        // fight
        Fight fight;
        Cpu cpu;
        float acc, timer;
        int round = 1;
        int[] wins = { 0, 0 };
        Phase phase;
        float phaseT;
        string banner = "", sub = "";
        (string text, int side, float t)? callout;
        float superFlash, koFlash;
        readonly Transform[] roots = new Transform[2];
        readonly Transform[] pivots = new Transform[2];
        readonly Chibi[] views = new Chibi[2];
        readonly GameObject[] shadows = new GameObject[2];
        readonly GameObject[] swooshes = new GameObject[2];
        readonly State[] lastState = new State[2];
        readonly Dictionary<FShot, Transform> shotViews = new Dictionary<FShot, Transform>();

        protected override void Begin()
        {
            Sfx.Define("swing", new Tone { Type = Wave.Noise, Duration = 0.08f, Volume = 0.2f, Filter = FilterType.Bandpass, FilterFreq = 1500, FilterFreqEnd = 3000, Q = 1 });
            Sfx.Define("hit", Tone.Noise(0.1f, 0.6f, FilterType.Lowpass, 1800), Tone.Beep(220, 0.08f, Wave.Square, 0.3f, 80));
            Sfx.Define("hitBig", Tone.Noise(0.22f, 0.8f, FilterType.Lowpass, 1200, 200), Tone.Beep(160, 0.15f, Wave.Square, 0.35f, 50));
            Sfx.Define("block", Tone.Beep(900, 0.06f, Wave.Square, 0.25f, 600));
            Sfx.Define("special", Tone.Beep(300, 0.25f, Wave.Sawtooth, 0.25f, 900));
            Sfx.Define("super", Tone.Melody("C5 G5 C6 G6", 0.05f, Wave.Sawtooth, 0.3f));
            Sfx.Define("jump", Tone.Beep(300, 0.06f, Wave.Square, 0.1f, 500));
            Sfx.Define("land", Tone.Noise(0.05f, 0.12f, FilterType.Lowpass, 400, -1, false));
            Sfx.Define("bell", Tone.Melody("C6 G5 C6", 0.12f, Wave.Triangle, 0.4f));
            Sfx.Define("fight", Tone.Beep(880, 0.25f, Wave.Square, 0.3f));
            Rig.Background(Js.Hex("#140a1e"));
            Sfx.MusicByName("music", 0.3f);
            GoTitle();
        }

        static string Model(int i) => Roster.ROSTER[i].Model;

        // ── screens ──

        void GoTitle()
        {
            scr = Scr.Title;
            lineup = ShowLineup(Roster.ROSTER.Select(r => r.Model).ToList(), "#3a0a1a", "#fff3a0");
        }

        void GoSelect()
        {
            scr = Scr.Select;
            selStep = 0;
            cur = new[] { p1, p2 };
            lineup = ShowLineup(Roster.ROSTER.Select(r => r.Model).ToList(), "#3a0a1a", "#fff3a0", 1.35f, 1.4f);
        }

        void ShowStagePick()
        {
            ClearWorld();
            BuildStage(stage);
            var a = SpawnChar(Model(p1), new Vector3(-2.2f, 0, 0), (float)Roster.ROSTER[p1].Height * U);
            var b = SpawnChar(Model(p2), new Vector3(2.2f, 0, 0), (float)Roster.ROSTER[p2].Height * U);
            a.Face(new Vector3(1, 0, -0.4f));
            b.Face(new Vector3(-1, 0, -0.4f));
            Rig.Set(new Vector3(0, 2.2f, -10f), new Vector3(0, 1.6f, 0), 40f);
        }

        void GoFight()
        {
            scr = Scr.Fight;
            wins = new[] { 0, 0 };
            round = 1;
            ClearWorld();
            BuildStage(stage);
            for (int i = 0; i < 2; i++)
            {
                int idx = i == 0 ? p1 : p2;
                roots[i] = Prim.Empty("fighter" + i, World).transform;
                pivots[i] = Prim.Empty("pivot", roots[i]).transform;
                views[i] = SpawnChar(Model(idx), Vector3.zero, (float)Roster.ROSTER[idx].Height * U, pivots[i]);
                shadows[i] = Prim.Cyl(World, Vector3.zero, 1f, 0.02f, new Color(0, 0, 0, 1));
                swooshes[i] = Prim.Box(World, Vector3.zero, Vector3.one, Js.Hex(Roster.Hex(Roster.ROSTER[idx].Color)));
                Prim.SetColor(swooshes[i], Js.Hex(Roster.Hex(Roster.ROSTER[idx].Color)), true);
            }
            NewRound();
        }

        void GoOver()
        {
            scr = Scr.Over;
            int wi = winner == 0 ? p1 : p2;
            lineup = ShowLineup(new List<string> { Model(wi) }, "#3a0a1a", "#fff3a0", 1f, 2.2f);
            bool youWin = mode == "vs" || winner == 0;
            if (youWin) Sfx.Notes("C5 E5 G5 C6 G5 C6:3", 0.1f, Wave.Square, 0.4f);
            else Sfx.Notes("G4 F4 E4 D4 C4:3", 0.14f, Wave.Triangle, 0.4f);
        }

        // ── stage ──

        void BuildStage(int s)
        {
            var w = World;
            // Original stage painting behind a simple floor when available.
            if (Art.Backdrop(w, "stages_" + (s + 1), new Vector3(0, 5.5f, 10f), 17f))
            {
                string[] floors = { "#b07a48", "#8a7a6a", "#3a3552" };
                Prim.Box(w, new Vector3(0, -0.1f, 2), new Vector3(30, 0.2f, 12), Js.Hex(floors[s]));
                return;
            }
            switch (s)
            {
                case 0: // 櫻花道場
                    Rig.Background(Js.Hex("#f6d6e6"));
                    Prim.Box(w, new Vector3(0, -0.1f, 2), new Vector3(30, 0.2f, 12), Js.Hex("#b07a48"));
                    for (int i = -6; i <= 6; i++) Prim.Box(w, new Vector3(i * 2, 0.005f, 2), new Vector3(0.05f, 0.01f, 12), Js.Hex("#8a5a30"));
                    Prim.Box(w, new Vector3(0, 3, 7), new Vector3(30, 6, 0.3f), Js.Hex("#f4ead8"));
                    for (int i = -3; i <= 3; i++) Prim.Box(w, new Vector3(i * 4, 3, 6.8f), new Vector3(0.3f, 6, 0.3f), Js.Hex("#6a3a20"));
                    for (int i = -2; i <= 2; i++)
                    {
                        var p = new Vector3(i * 5.5f, 0, 10);
                        Prim.Cyl(w, p + Vector3.up * 1.5f, 0.4f, 3, Js.Hex("#5a3a2a"));
                        Prim.Sphere(w, p + Vector3.up * 3.6f, 3.2f, Js.Hex("#ffb7d0"));
                    }
                    break;
                case 1: // 夕陰港灣
                    Rig.Background(Js.Hex("#ff9a6a"));
                    Prim.Box(w, new Vector3(0, -0.1f, 2), new Vector3(30, 0.2f, 12), Js.Hex("#8a7a6a"));
                    Prim.Box(w, new Vector3(0, -0.4f, 14), new Vector3(80, 0.2f, 16), Js.Hex("#3a5a8a"));
                    for (int i = -3; i <= 3; i++) Prim.Box(w, new Vector3(i * 3.5f + 1, 0.6f, 6.5f), new Vector3(1.2f, 1.2f, 1.2f), Js.Hex(i % 2 == 0 ? "#c0703a" : "#3a7ac0"));
                    Prim.Sphere(w, new Vector3(8, 4, 30), 6, Js.Hex("#ffd070"));
                    App.I.Sun.color = new Color(1f, 0.8f, 0.6f);
                    break;
                default: // 霓虹天台
                    Rig.Background(Js.Hex("#140a2e"));
                    Prim.Box(w, new Vector3(0, -0.1f, 2), new Vector3(30, 0.2f, 12), Js.Hex("#3a3552"));
                    for (int i = -5; i <= 5; i++)
                    {
                        float h = 4 + Mathf.Abs(i * 1.7f % 5);
                        Prim.Box(w, new Vector3(i * 3, h / 2 - 2, 14 + (i % 3)), new Vector3(2.4f, h, 2), Js.Hex("#231a40"));
                        var sign = Prim.Box(w, new Vector3(i * 3, h - 2.5f, 12.9f + (i % 3)), new Vector3(1.6f, 0.3f, 0.05f), Js.Hex(i % 2 == 0 ? "#ff4fd8" : "#39e6ff"));
                        Prim.SetColor(sign, Js.Hex(i % 2 == 0 ? "#ff4fd8" : "#39e6ff"), true);
                    }
                    App.I.Sun.intensity = 0.6f;
                    RenderSettings.ambientLight = new Color(0.4f, 0.35f, 0.55f);
                    break;
            }
        }

        void NewRound()
        {
            fight = new Fight(Roster.ROSTER[p1], Roster.ROSTER[p2], 1280);
            cpu = mode == "cpu" ? new Cpu(1, difficulty) : null;
            timer = Roster.ROUND_TIME;
            phase = Phase.Intro;
            phaseT = 0;
            banner = wins[0] == Roster.ROUNDS_TO_WIN - 1 && wins[1] == Roster.ROUNDS_TO_WIN - 1 ? "FINAL ROUND" : $"ROUND {round}";
            sub = "";
            Sfx.Play("bell");
            for (int i = 0; i < 2; i++) { views[i].KnockDown(false); lastState[i] = State.Idle; }
            foreach (var v in shotViews.Values) if (v) Destroy(v.gameObject);
            shotViews.Clear();
        }

        Input ReadP1()
        {
            bool solo = mode == "cpu";
            return new Input
            {
                Left = In.Held(KeyCode.A) || (solo && In.Held(KeyCode.LeftArrow)),
                Right = In.Held(KeyCode.D) || (solo && In.Held(KeyCode.RightArrow)),
                Up = In.Held(KeyCode.W) || (solo && In.Held(KeyCode.UpArrow)),
                Down = In.Held(KeyCode.S) || (solo && In.Held(KeyCode.DownArrow)),
                Lp = In.Held(KeyCode.J) || (solo && In.Held(KeyCode.Z)),
                Hp = In.Held(KeyCode.K) || (solo && In.Held(KeyCode.X)),
                Lk = In.Held(KeyCode.U) || (solo && In.Held(KeyCode.C)),
                Hk = In.Held(KeyCode.I) || (solo && In.Held(KeyCode.V)),
                Sp = In.Held(KeyCode.L) || (solo && In.Held(KeyCode.B)),
                Su = In.Held(KeyCode.O) || (solo && In.Held(KeyCode.N)),
            };
        }

        Input ReadP2() => new Input
        {
            Left = In.Held(KeyCode.LeftArrow), Right = In.Held(KeyCode.RightArrow), Up = In.Held(KeyCode.UpArrow), Down = In.Held(KeyCode.DownArrow),
            Lp = In.Held(KeyCode.Keypad1, KeyCode.Comma), Hp = In.Held(KeyCode.Keypad2, KeyCode.Period), Sp = In.Held(KeyCode.Keypad3, KeyCode.Slash),
            Lk = In.Held(KeyCode.Keypad4, KeyCode.Semicolon), Hk = In.Held(KeyCode.Keypad5, KeyCode.Quote), Su = In.Held(KeyCode.Keypad6, KeyCode.RightBracket),
        };

        // ── update ──

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            switch (scr)
            {
                case Scr.Title: UpdateTitle(); break;
                case Scr.Select: UpdateSelect(); break;
                case Scr.Fight:
                    if (In.Back) { GoSelect(); return; }
                    acc = Mathf.Min(acc + dt, 0.1f);
                    while (acc >= STEP && scr == Scr.Fight) { acc -= STEP; Tick(); }
                    if (scr != Scr.Fight) return;
                    if (callout.HasValue) { var c = callout.Value; c.t -= dt; callout = c.t <= 0 ? null : ((string, int, float)?)c; }
                    if (superFlash > 0) superFlash -= dt;
                    if (koFlash > 0) koFlash -= dt;
                    SyncViews();
                    break;
                case Scr.Over:
                    if (In.Confirm) GoFight();
                    else if (In.Back) GoSelect();
                    if (lineup != null && lineup.Length > 0 && Mathf.Repeat(t, 0.9f) < dt) lineup[0].Act(mode == "vs" || winner == 0 ? "win" : "hop", 0.8f);
                    break;
            }
        }

        void UpdateTitle()
        {
            if (In.Back) { ExitToHub(); return; }
            if (In.UpDown) titleSel = (titleSel + 2) % 3;
            if (In.DownDown) titleSel = (titleSel + 1) % 3;
            if (In.Down(KeyCode.Space, KeyCode.Return, KeyCode.J)) TitlePick(titleSel);
        }

        void TitlePick(int i)
        {
            if (i == 2) { difficulty = (difficulty + 1) % 3; Sfx.Beep(660, 0.05f, Wave.Square, 0.2f); return; }
            mode = i == 0 ? "cpu" : "vs";
            Sfx.Notes("C5 E5 G5", 0.07f, Wave.Square, 0.3f);
            GoSelect();
        }

        int Who() => selStep == 1 && mode == "vs" ? 1 : 0;

        void SelectConfirm()
        {
            Sfx.Notes("G4 C5", 0.06f, Wave.Square, 0.3f);
            if (selStep == 0)
            {
                p1 = cur[0];
                if (mode == "cpu") { selStep = 1; cur[1] = (p1 + 1 + Rand.Int(7)) % 8; }
                else selStep = 1;
            }
            else if (selStep == 1) { p2 = cur[1]; selStep = 2; ShowStagePick(); }
            else GoFight();
        }

        void UpdateSelect()
        {
            if (In.Back)
            {
                if (selStep == 0) { GoTitle(); return; }
                if (selStep == 2) lineup = ShowLineup(Roster.ROSTER.Select(r => r.Model).ToList(), "#3a0a1a", "#fff3a0", 1.35f, 1.4f);
                selStep--;
                return;
            }
            if (selStep == 2)
            {
                if (In.LeftDown || In.Down(KeyCode.LeftArrow)) { stage = (stage + 2) % 3; ShowStagePick(); }
                if (In.RightDown || In.Down(KeyCode.RightArrow)) { stage = (stage + 1) % 3; ShowStagePick(); }
                if (In.Down(KeyCode.J, KeyCode.Space, KeyCode.Return)) SelectConfirm();
                return;
            }
            int w = Who();
            bool solo = mode == "cpu";
            bool left = w == 1 ? In.Down(KeyCode.LeftArrow) : In.Down(KeyCode.A) || (solo || selStep == 0) && In.Down(KeyCode.LeftArrow);
            bool right = w == 1 ? In.Down(KeyCode.RightArrow) : In.Down(KeyCode.D) || (solo || selStep == 0) && In.Down(KeyCode.RightArrow);
            bool upDown = w == 1 ? In.Down(KeyCode.UpArrow, KeyCode.DownArrow) : In.Down(KeyCode.W, KeyCode.S) || (solo || selStep == 0) && In.Down(KeyCode.UpArrow, KeyCode.DownArrow);
            int ci = selStep == 0 ? 0 : 1;
            if (left) cur[ci] = (cur[ci] + 7) % 8;
            if (right) cur[ci] = (cur[ci] + 1) % 8;
            if (upDown) cur[ci] = (cur[ci] + 4) % 8;
            bool ok = w == 1 ? In.Down(KeyCode.Comma, KeyCode.Keypad1, KeyCode.Return) : In.Down(KeyCode.J, KeyCode.Space, KeyCode.Return);
            if (ok) SelectConfirm();
        }

        void Tick()
        {
            phaseT += STEP;
            Input a = default, b = default;
            if (phase == Phase.Intro)
            {
                if (phaseT > 1.2f && banner != "FIGHT!") { banner = "FIGHT!"; Sfx.Play("fight"); }
                if (phaseT > 1.9f) { phase = Phase.Fight; banner = ""; }
            }
            else if (phase == Phase.Fight)
            {
                a = ReadP1();
                b = cpu != null ? cpu.Think(fight) : ReadP2();
                if (fight.Hitstop == 0) timer -= STEP;
            }
            fight.Step(a, b);
            fight.Settle();
            foreach (var h in fight.Ev.Hit)
            {
                var col = h.Blocked ? Js.Hex("#9fd8ff") : Js.Hex(Roster.Hex(h.Color));
                Fx.Burst(W3(h.X, h.Y) + Vector3.back * 0.3f, h.Big ? 26 : h.Blocked ? 8 : 14, col, h.Big ? 6f : 4f, 0.3f, h.Big ? 0.12f : 0.08f, 2f);
                if (h.Big) Rig.Shake(0.15f, 0.2f);
            }
            foreach (var s in fight.Ev.Sfx) Sfx.Play(s);
            foreach (var s in fight.Ev.Say)
            {
                var parts = s.Split(':');
                callout = (parts.Length > 1 ? parts[1] : "", parts[0] == "P1" ? 0 : 1, 1.2f);
                if (fight.Hitstop >= 10) superFlash = 0.5f;
            }
            if (phase == Phase.Fight)
            {
                var pa = fight.P[0];
                var pb = fight.P[1];
                bool ko = pa.Hp <= 0 || pb.Hp <= 0;
                if (ko || timer <= 0)
                {
                    phase = Phase.Ko; phaseT = 0;
                    int w = -1;
                    if (pa.Hp <= 0 && pb.Hp > 0) w = 1;
                    else if (pb.Hp <= 0 && pa.Hp > 0) w = 0;
                    else if (!ko) w = pa.Hp / pa.F.Health > pb.Hp / pb.F.Health ? 0 : pb.Hp / pb.F.Health > pa.Hp / pa.F.Health ? 1 : -1;
                    if (w >= 0) { wins[w]++; fight.P[w].State = State.Win; }
                    banner = ko ? "K.O." : "TIME UP";
                    sub = w < 0 ? "平手" : $"{fight.P[w].F.Name} 勝利！";
                    Sfx.Play(ko ? "hitBig" : "bell");
                    if (ko) koFlash = 0.25f;
                }
            }
            else if (phase == Phase.Ko && phaseT > 3)
            {
                if (wins[0] >= Roster.ROUNDS_TO_WIN || wins[1] >= Roster.ROUNDS_TO_WIN)
                {
                    winner = wins[0] >= Roster.ROUNDS_TO_WIN ? 0 : 1;
                    score = (int[])wins.Clone();
                    GoOver();
                    return;
                }
                round++;
                NewRound();
            }
        }

        static Vector3 W3(double x, double y, float z = 0) => new Vector3((float)(x - 640) * U, (float)(Roster.GROUND_Y - y) * U, z);

        void SyncViews()
        {
            for (int i = 0; i < 2; i++)
            {
                var b = fight.P[i];
                float rot = 0, sx = 1, sy = 1, dx = 0;
                string ph = b.Phase();
                string pose = b.Move?.Pose ?? "";
                bool act = ph == "active";
                var ch = views[i];
                switch (b.State)
                {
                    case State.Crouch: sy = 0.68f; sx = 1.08f; break;
                    case State.Walk: rot = Mathf.Sin(t * 14) * 0.04f; break;
                    case State.Air: rot = b.Vy < 0 ? -0.1f * b.Facing : 0.08f * b.Facing; break;
                    case State.Hitstun: rot = -0.25f * b.Facing; dx = -b.Facing * 6; break;
                    case State.Blockstun: dx = -b.Facing * 4; break;
                    case State.Win: sy = 1 + Mathf.Abs(Mathf.Sin(t * 6)) * 0.05f; break;
                    case State.Attack:
                        {
                            float lean = ph == "startup" ? -0.08f : act ? 0.16f : 0.05f;
                            if (pose == "punch" || pose == "claw" || pose == "cast") { rot = lean * b.Facing; dx = act ? b.Facing * 18 : 0; }
                            else if (pose == "kick" || pose == "airkick") { rot = (act ? -0.35f : -0.1f) * b.Facing; dx = act ? b.Facing * 14 : 0; }
                            else if (pose == "low") { sy = 0.66f; sx = 1.1f; rot = (act ? 0.12f : 0) * b.Facing; }
                            else if (pose == "uppercut" || pose == "rise") { sy = act ? 1.12f : 0.8f; rot = (act ? -0.1f : 0.1f) * b.Facing; }
                            else if (pose == "slide") { rot = 1.25f * b.Facing; sy = 0.9f; }
                            else if (pose == "spin") { rot = t * 30 % (Mathf.PI * 2); }
                            else if (pose == "flip") { rot = act ? -(b.Frame * 0.35f % (Mathf.PI * 2)) * b.Facing : 0; }
                            else if (pose == "pounce") { rot = 0.5f * b.Facing; }
                            else if (pose == "air") { rot = 0.2f * b.Facing; }
                            if (b.Move != null && b.Move.Fire && act && Random.value < 0.5f) Fx.Burst(W3(b.X, b.Y - b.F.Height * 0.4), 2, Js.Hex("#ff7a3a"), 2f, 0.4f, 0.12f, -2f);
                            break;
                        }
                }
                // Clip-driven actions on state changes.
                if (b.State != lastState[i])
                {
                    switch (b.State)
                    {
                        case State.Attack:
                            ch.Act(pose == "cast" || pose == "counter" ? "cast" : pose == "rise" || pose == "flip" ? "jump" : pose == "kick" || pose == "airkick" || pose == "low" ? "attack" : "attack", 0.35f);
                            break;
                        case State.Hitstun: case State.Blockstun: ch.Act("hit", 0.3f); break;
                        case State.Down: case State.Ko: ch.KnockDown(true); break;
                        case State.Win: ch.Act("win", 2.5f); break;
                        case State.Air: ch.Act("jump", 0.6f); break;
                        case State.Idle: if (lastState[i] == State.Down) ch.KnockDown(false); break;
                    }
                    lastState[i] = b.State;
                }
                ch.SetLoop(b.State == State.Walk ? Chibi.Loop.Run : Chibi.Loop.Idle, 0.9f);
                bool flash = b.Invuln > 0 && b.State != State.Attack && Mathf.FloorToInt(t * 20) % 2 == 1;
                ch.SetVisible(!flash);
                roots[i].localPosition = W3(b.X + dx, b.Y);
                // 2D-style lean in the screen plane (the original rotates the sprite).
                pivots[i].localRotation = Quaternion.Euler(0, 0, -rot * Mathf.Rad2Deg);
                pivots[i].localScale = new Vector3(sx, sy, sx);
                ch.transform.localRotation = Quaternion.LookRotation(new Vector3(b.Facing, 0, -0.45f));
                float sc = Mathf.Clamp((float)(1 - (Roster.GROUND_Y - b.Y) / 400), 0.4f, 1);
                shadows[i].transform.localPosition = new Vector3(roots[i].localPosition.x, 0.01f, 0);
                shadows[i].transform.localScale = new Vector3(1.3f * sc * (float)b.Scale, 0.01f, 0.5f * sc);
                // Attack swoosh on active frames.
                var hb = b.Hitbox();
                swooshes[i].SetActive(hb.HasValue);
                if (hb.HasValue)
                {
                    var r = hb.Value;
                    var c = W3(r.X + r.W / 2, r.Y + r.H / 2, -0.5f);
                    swooshes[i].transform.localPosition = c;
                    swooshes[i].transform.localScale = new Vector3((float)r.W * U, Mathf.Max(0.06f, (float)r.H * U * 0.25f), 0.05f);
                    swooshes[i].transform.localRotation = Quaternion.Euler(0, 0, b.Facing * 12);
                }
            }
            // Projectiles.
            foreach (var s in fight.Shots)
            {
                if (!shotViews.TryGetValue(s, out var v))
                {
                    var go = Prim.Sphere(World, Vector3.zero, 1, Js.Hex(Roster.Hex(s.Color)));
                    Prim.SetColor(go, Js.Hex(Roster.Hex(s.Color)), true);
                    v = go.transform;
                    shotViews[s] = v;
                }
                float wob = Mathf.Sin(t * 20) * 4;
                v.localPosition = W3(s.X, s.Y, -0.3f);
                v.localScale = Vector3.one * (float)(s.H + wob) * U;
            }
            foreach (var gone in shotViews.Keys.Where(s => !fight.Shots.Contains(s)).ToList())
            {
                Destroy(shotViews[gone].gameObject);
                shotViews.Remove(gone);
            }
            // Camera frames both fighters.
            float mid = (roots[0].localPosition.x + roots[1].localPosition.x) / 2;
            float gap = Mathf.Abs(roots[0].localPosition.x - roots[1].localPosition.x);
            float dist = Mathf.Clamp(7.5f + gap * 0.55f, 8f, 13f);
            Rig.Follow(new Vector3(mid, 2.4f, -dist), new Vector3(mid, 2f, 0), 6f, Time.deltaTime);
        }

        // ── GUI ──

        void OnGUI()
        {
            Gui.Begin();
            float W = Gui.W, H = Gui.H, cx = W / 2;
            switch (scr)
            {
                case Scr.Title:
                    {
                        Gui.Label("萌友格鬥王", cx, H * 0.2f, 64, Js.Hex("#fff3a0"), 0.5f, 0.5f, Js.Hex("#6a1010"));
                        string[] labels = { "單人對戰電腦", "雙人對戰", $"電腦難度：{Roster.DIFFS[difficulty]}" };
                        for (int i = 0; i < 3; i++)
                        {
                            var r = new Rect(cx - 160, H * 0.3f + i * 64, 320, 54);
                            if (Gui.Hover(r)) titleSel = i;
                            if (Gui.Button(r, labels[i], 26, null, null, i == titleSel)) TitlePick(i);
                        }
                        Gui.Label("↑↓ 選擇　Space 確認　Esc 回到遊戲大廳", cx, H - 16, 16, Js.Hex("#cccccc"), 0.5f, 0.5f, Color.black);
                        break;
                    }
                case Scr.Select: DrawSelect(W, H); break;
                case Scr.Fight: DrawHud(W, H); break;
                case Scr.Over:
                    {
                        int wi = winner == 0 ? p1 : p2;
                        string head = mode == "cpu" ? (winner == 0 ? "你贏了！" : "你輸了……") : $"{(winner == 0 ? "1P" : "2P")} 獲勝！";
                        Gui.Label(head, cx, 80, 72, Js.Hex("#ffe066"), 0.5f, 0.5f, Js.Hex("#6a1010"));
                        Gui.Label($"{score[0]} - {score[1]}", cx, 160, 44, Color.white, 0.5f, 0.5f, Color.black);
                        Gui.Label($"「{Roster.ROSTER[wi].Name}：還要再來一場嗎？」", cx, H - 150, 26, Color.white, 0.5f, 0.5f, Color.black);
                        if (Gui.Button(new Rect(cx - 250, H - 100, 230, 62), "再戰一場", 26, Js.Hex("#d9452b"), Js.Hex("#ff6a47"))) GoFight();
                        if (Gui.Button(new Rect(cx + 20, H - 100, 230, 62), "重選角色", 26)) GoSelect();
                        break;
                    }
            }
        }

        void DrawSelect(float W, float H)
        {
            float cx = W / 2;
            if (selStep == 2)
            {
                Gui.Label($"舞台：{Roster.STAGES[stage]}", cx, 60, 44, Color.white, 0.5f, 0.5f, Color.black);
                Gui.Label("← → 切換舞台　J / Space 開戰！", cx, H - 40, 24, Js.Hex("#ffe066"), 0.5f, 0.5f, Color.black);
                if (Gui.Button(new Rect(cx - 330, H * 0.5f - 30, 60, 60), "◀", 28)) { stage = (stage + 2) % 3; ShowStagePick(); }
                if (Gui.Button(new Rect(cx + 270, H * 0.5f - 30, 60, 60), "▶", 28)) { stage = (stage + 1) % 3; ShowStagePick(); }
                if (Gui.Button(new Rect(cx - 120, H - 120, 240, 56), "開戰！", 28, Js.Hex("#d9452b"), Js.Hex("#ff6a47"))) SelectConfirm();
                return;
            }
            string title = selStep == 0 ? "1P 選擇角色" : mode == "cpu" ? "選擇對手（電腦）" : "2P 選擇角色";
            Gui.Label(title, cx, 40, 40, selStep == 0 ? Js.Hex("#5cc8ff") : Js.Hex("#ff6b6b"), 0.5f, 0.5f, Color.black);
            int ci = selStep == 0 ? 0 : 1;
            float cw = Mathf.Min(150, W / 8.6f);
            for (int i = 0; i < 8; i++)
            {
                if (!lineup[i]) continue;
                var foot = GuiAt(lineup[i].transform.position);
                var head = GuiAt(lineup[i].transform.position + Vector3.up * 1.4f);
                var card = new Rect(foot.x - cw / 2, foot.y + 8, cw, 40);
                var hit = new Rect(card.x, head.y, cw, card.yMax - head.y);
                bool sel = cur[ci] == i;
                bool other = selStep == 1 && p1 == i;
                Gui.Panel(card, sel ? Js.Hex(ci == 0 ? "#1d4a7a" : "#7a1d2a", 0.93f) : Js.Hex("#0d1433", 0.8f), sel ? Js.Hex("#ffe066") : other ? Js.Hex("#5cc8ff") : new Color(1, 1, 1, 0.27f));
                Gui.Label(Roster.ROSTER[i].Name, card.center.x, card.center.y, 17, Color.white);
                if (Gui.Clicked(hit)) { if (cur[ci] == i) SelectConfirm(); else { cur[ci] = i; lineup[i].Act("hop", 0.35f); } }
            }
            var r = Roster.ROSTER[cur[ci]];
            float y0 = H * 0.68f;
            Gui.Label($"{r.Name}｜{r.Title}", cx, y0, 24, Js.Hex(Roster.Hex(r.Color)), 0.5f, 0.5f, Color.black);
            Gui.Label(r.Desc, cx, y0 + 34, 18, Color.white, 0.5f, 0.5f, Color.black, 620);
            string mo = r.Special.Motion != null ? $"{Moves.MOTIONS[r.Special.Motion].label}+{(r.Special.Button == 'P' ? "拳" : "腳")}" : "";
            Gui.Label($"必殺技「{r.Special.Name}」{mo}（捷徑鍵 L/B）", cx, y0 + 70, 17, Js.Hex("#ffe066"), 0.5f, 0.5f, Color.black);
            Gui.Label($"超必殺「{r.Super.Name}」{Moves.MOTIONS["super"].label}+拳腳（滿氣，O/N）", cx, y0 + 98, 17, Js.Hex("#5cffb0"), 0.5f, 0.5f, Color.black);
            Gui.Label(mode == "vs" && selStep == 1 ? "2P：方向鍵選擇　, 或 Numpad1 確認" : "方向鍵 / WASD 選擇　J / Space 確認　Esc 返回", cx, H - 16, 16, Js.Hex("#cccccc"), 0.5f, 0.5f, Color.black);
        }

        void DrawHud(float W, float H)
        {
            if (superFlash > 0) Gui.Rect(0, 0, W, H, new Color(0, 0, 0, Mathf.Clamp(superFlash * 1.4f, 0, 0.6f)));
            float bw = Mathf.Min(480, W * 0.36f);
            for (int i = 0; i < 2; i++)
            {
                var b = fight.P[i];
                bool left = i == 0;
                float x = left ? 40 : W - 40 - bw;
                Gui.Rect(x - 4, 26, bw + 8, 34, new Color(0, 0, 0, 0.7f));
                float r = Mathf.Clamp01((float)(b.Hp / b.F.Health));
                Gui.Rect(x, 30, bw, 26, Js.Hex("#5a1010"));
                float fillW = bw * r;
                Gui.Rect(left ? x + bw - fillW : x, 30, fillW, 26, r < 0.25f ? Js.Hex("#ff5f3a") : Js.Hex("#ffd23f"));
                Gui.Label($"{b.F.Name}｜{b.F.Title}", left ? x : x + bw, 76, 22, Color.white, left ? 0f : 1f, 0.5f, Color.black);
                for (int k = 0; k < Roster.ROUNDS_TO_WIN; k++) Gui.Circle(left ? x + bw - 14 - k * 26 : x + 14 + k * 26, 76, 9, k < wins[i] ? Js.Hex("#ffe066") : new Color(1, 1, 1, 0.2f));
                float mw = Mathf.Min(300, W * 0.24f), mx = left ? 40 : W - 40 - mw, my = H - 40;
                Gui.Rect(mx - 3, my - 3, mw + 6, 20, new Color(0, 0, 0, 0.7f));
                float mr = (float)(b.Meter / Roster.METER_MAX);
                Gui.Rect(left ? mx : mx + mw - mw * mr, my, mw * mr, 14, mr >= 1 ? (Mathf.FloorToInt(t * 8) % 2 == 1 ? Js.Hex("#5cffb0") : Color.white) : Js.Hex("#3a8dff"));
                Gui.Label(mr >= 1 ? "超必殺 OK！" : "SUPER", left ? mx : mx + mw, my - 14, 16, mr >= 1 ? Js.Hex("#5cffb0") : Js.Hex("#cccccc"), left ? 0f : 1f, 0.5f, Color.black);
                if (b.Combo >= 2) Gui.Label($"{b.Combo} HIT COMBO", left ? W - 60 : 60, 170, 30, Js.Hex("#ffe066"), left ? 1f : 0f, 0.5f, Js.Hex("#6a1010"));
            }
            Gui.Label(Mathf.Max(0, Mathf.CeilToInt(timer)).ToString(), W / 2, 44, 44, Color.white, 0.5f, 0.5f, Color.black);
            if (callout.HasValue)
            {
                var c = callout.Value;
                var col = Color.white;
                col.a = Mathf.Clamp01(c.t * 2);
                Gui.Label(c.text, c.side == 0 ? 60 : W - 60, 230, 38, col, c.side == 0 ? 0f : 1f, 0.5f, Js.Hex(Roster.Hex(fight.P[c.side].F.Color)));
            }
            if (banner != "") Gui.Label(banner, W / 2, H * 0.36f, 96, Js.Hex("#ffe066"), 0.5f, 0.5f, Js.Hex("#3a0a0a"));
            if (sub != "") Gui.Label(sub, W / 2, H * 0.48f, 40, Color.white, 0.5f, 0.5f, Color.black);
            if (round == 1 && phase != Phase.Ko)
            {
                var sp = fight.P[0].F.Special;
                string mo = sp.Motion != null ? $"{Moves.MOTIONS[sp.Motion].label}+{(sp.Button == 'P' ? "拳" : "腳")}" : "";
                string keys = mode == "cpu" ? "方向鍵/WASD 移動　Z/J 輕拳　X/K 重拳　C/U 輕腳　V/I 重腳　B/L 必殺　N/O 超必殺　後=防禦" : "P1：WASD + J K U I L O　P2：方向鍵 + , . ; ' / ] 或數字鍵 1-6";
                Gui.Label($"{keys}　必殺技「{sp.Name}」{mo}", W / 2, H - 70, 15, Color.white, 0.5f, 0.5f, Color.black);
            }
            if (koFlash > 0) Gui.Rect(0, 0, W, H, new Color(1, 1, 1, koFlash * 3));
        }
    }
}
