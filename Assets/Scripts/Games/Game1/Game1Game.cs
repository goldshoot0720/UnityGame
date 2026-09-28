// 萌友棒球對決 — 3D catcher's-view presentation (title → team select → play → result).
// Mirrors scenes/play.ts: the user steers a meet cursor and times swings when batting, and
// picks / aims / throws pitches when fielding.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoeGames.Game1
{
    [MoeGame(1)]
    public class Game1Game : MiniGame
    {
        public override TouchLayout Touch => TouchLayout.TapOnly.Button("揮棒\n投球", KeyCode.Space);

        protected override string Backdrop => "stadium";

        enum Scr { Title, Select, Play, Over }
        enum Phase { Ready, Aim, Windup, Flight, Result, Half }

        const float WINDUP = 0.7f;
        const float RESULT_TIME = 1.9f;
        // Strike zone in world units (at the plate plane z = 0).
        const float ZoneHalfW = 0.26f, ZoneHalfH = 0.32f, ZoneCY = 0.78f;
        static readonly Vector3 Mound = new Vector3(0, 0.25f, 9.5f);

        Scr scr;
        float t;
        string userTeam = "whale";
        GameState gs;
        GameState last;

        // play state
        Phase phase = Phase.Half;
        float timer;
        Pitch pitch;
        float flightT;
        Vector2 cur;
        bool swung;
        float swingAt = -1;
        PitchOutcome? cpuResult;
        bool cpuSwingShown;
        PitchOutcome? outcome;
        string note = "";
        float hitT = -1, hitAng;
        PitchOutcome hitKind;
        PitchName selPitch = PitchName.直球;
        Vector2 lastMouse;
        string halfBanner = "";
        bool halfChanged;
        float swingAnim = -1;

        // 3D
        Chibi[] lineup;
        Chibi pitcherView, batterView;
        readonly Chibi[] runners = new Chibi[3];
        readonly List<Chibi> fielders = new List<Chibi>();
        Transform bat, batPivot, ball, hitBall;
        string shownPitcher, shownBatter;

        static readonly Vector3[] BasePos = { new Vector3(7.5f, 0.05f, 7.5f), new Vector3(0, 0.05f, 15f), new Vector3(-7.5f, 0.05f, 7.5f) };

        bool UserBatting => gs.BattingTeam.Id == userTeam;

        protected override void Begin()
        {
            Sfx.Define("crack", Tone.Noise(0.12f, 0.9f, FilterType.Highpass, 1800), Tone.Beep(1200, 0.08f, Wave.Square, 0.3f, 300));
            Sfx.Define("mitt", Tone.Noise(0.09f, 0.7f, FilterType.Lowpass, 900));
            Sfx.Define("whoosh", new Tone { Type = Wave.Noise, Duration = 0.25f, Volume = 0.25f, Filter = FilterType.Bandpass, FilterFreq = 600, FilterFreqEnd = 2400, Q = 2 });
            Sfx.Define("cheer", new Tone { Type = Wave.Noise, Duration = 1.4f, Attack = 0.2f, Volume = 0.35f, Filter = FilterType.Bandpass, FilterFreq = 1400, Q = 0.7f });
            Rig.Background(Js.Hex("#1b2a4a"));
            GoTitle();
        }

        // ── screens ──

        void GoTitle()
        {
            scr = Scr.Title;
            lineup = ShowLineup(Cast.Ids, "#2f7d32", "#e8d8b0");
        }

        static List<string> Members(Team team) => new[] { team.Pitcher }.Concat(team.Lineup.Where(m => m != team.Pitcher)).ToList();

        void GoSelect()
        {
            scr = Scr.Select;
            var ids = Members(Data.TEAMS["whale"]).Concat(Members(Data.TEAMS["cat"])).ToList();
            lineup = ShowLineup(ids, "#2f7d32", "#e8d8b0", 1.3f);
            for (int i = 0; i < lineup.Length; i++) lineup[i].transform.localPosition += new Vector3(i < 4 ? -0.5f : 0.5f, 0, 0);
        }

        void GoPlay()
        {
            scr = Scr.Play;
            gs = GameState.ForUser(userTeam);
            BuildField();
            Sfx.MusicByName("music", 0.35f);
            StartHalf();
        }

        void GoOver()
        {
            scr = Scr.Over;
            last = gs;
            string w = gs.Winner();
            var winTeam = w == "tie" ? null : w == "home" ? gs.Home : gs.Away;
            lineup = ShowLineup(winTeam != null ? Members(winTeam) : Cast.Ids.ToList(), "#2f7d32", "#e8d8b0", 1.4f);
            bool won = UserWon();
            if (won) Sfx.Notes("C5 E5 G5 C6 G5 C6:3", 0.1f, Wave.Square, 0.4f);
            else Sfx.Notes("G4 F4 E4 D4 C4:3", 0.14f, Wave.Triangle, 0.4f);
        }

        bool UserWon()
        {
            if (last == null) return false;
            string w = last.Winner();
            return w != "tie" && (w == "home" ? last.Home : last.Away).Id == userTeam;
        }

        // ── field ──

        void BuildField()
        {
            ClearWorld();
            fielders.Clear();
            var f = World;
            var grass = Prim.Box(f, new Vector3(0, -0.05f, 20), new Vector3(90, 0.1f, 90), Js.Hex("#3f9a3a"));
            if (!Mats.TryApply(grass, "field_grass", Scope, new Vector2(12, 12)))
                for (int i = 0; i < 9; i++) Prim.Box(f, new Vector3(0, -0.04f, 2 + i * 6), new Vector3(90, 0.1f, 3), Js.Hex("#46a641"));
            var dirt = Prim.Box(f, new Vector3(0, -0.02f, 7.5f), new Vector3(12.5f, 0.1f, 12.5f), Js.Hex("#c8955a"));
            dirt.transform.localRotation = Quaternion.Euler(0, 45, 0);
            var infield = Prim.Box(f, new Vector3(0, -0.01f, 7.5f), new Vector3(9.2f, 0.1f, 9.2f), Js.Hex("#46a641"));
            infield.transform.localRotation = Quaternion.Euler(0, 45, 0);
            Prim.Cyl(f, new Vector3(0, 0, 0), 4.2f, 0.08f, Js.Hex("#c8955a"));
            Prim.Cyl(f, Mound + Vector3.down * 0.2f, 3f, 0.3f, Js.Hex("#c8955a"));
            Prim.Box(f, new Vector3(0, 0.06f, 0), new Vector3(0.45f, 0.04f, 0.45f), Color.white);
            foreach (var bp in BasePos) Prim.Box(f, bp + Vector3.up * 0.02f, new Vector3(0.45f, 0.08f, 0.45f), Color.white);
            // Foul lines and outfield wall.
            var l1 = Prim.Box(f, new Vector3(20, 0.03f, 20), new Vector3(0.1f, 0.02f, 56.6f), Color.white);
            l1.transform.localRotation = Quaternion.Euler(0, 45, 0);
            var l2 = Prim.Box(f, new Vector3(-20, 0.03f, 20), new Vector3(0.1f, 0.02f, 56.6f), Color.white);
            l2.transform.localRotation = Quaternion.Euler(0, -45, 0);
            for (int i = -8; i <= 8; i++)
            {
                float a = i * 5.5f * Mathf.Deg2Rad;
                var wall = Prim.Box(f, new Vector3(Mathf.Sin(a) * 42, 1.5f, Mathf.Cos(a) * 42), new Vector3(4.2f, 3f, 0.4f), Js.Hex("#1f4f8a"));
                wall.transform.localRotation = Quaternion.Euler(0, i * 5.5f, 0);
                Prim.Box(f, new Vector3(Mathf.Sin(a) * 46, 5f, Mathf.Cos(a) * 46), new Vector3(4.2f, 7f, 3f), Js.Hex(i % 2 == 0 ? "#2a3a66" : "#34467a")).transform.localRotation = Quaternion.Euler(0, i * 5.5f, 0);
            }
            Art.Backdrop(f, "stadium", new Vector3(0, 14f, 58f), 42f);
            ball = Prim.Sphere(f, Vector3.zero, 0.12f, Color.white).transform;
            ball.gameObject.SetActive(false);
            hitBall = Prim.Sphere(f, Vector3.zero, 0.14f, Color.white).transform;
            hitBall.gameObject.SetActive(false);
            batPivot = Prim.Empty("BatPivot", f, new Vector3(-0.55f, 1.05f, 0.05f)).transform;
            var batGo = Prim.Cyl(batPivot, new Vector3(0, 0, 0.45f), 0.07f, 0.9f, Js.Hex("#e0a96a"));
            batGo.transform.localRotation = Quaternion.Euler(90, 0, 0);
            bat = batGo.transform;
            shownPitcher = shownBatter = null;
            Rig.Set(new Vector3(0, 1.45f, -3.2f), new Vector3(0, 1.05f, 6f), 42f);
        }

        void RefreshPlayers()
        {
            if (shownPitcher != gs.Pitcher.Id)
            {
                if (pitcherView) Destroy(pitcherView.gameObject);
                pitcherView = SpawnChar(gs.Pitcher.Id, Mound + Vector3.up * 0.05f, 1.6f);
                pitcherView.Face(Vector3.back);
                shownPitcher = gs.Pitcher.Id;
                foreach (var fl in fielders) if (fl) Destroy(fl.gameObject);
                fielders.Clear();
                var others = gs.FieldingTeam.Lineup.Where(m => m != gs.FieldingTeam.Pitcher).ToList();
                var spots = new[] { new Vector3(9, 0.05f, 11), new Vector3(-4, 0.05f, 15), new Vector3(3, 0.05f, 28) };
                for (int i = 0; i < others.Count && i < spots.Length; i++)
                {
                    var fl = SpawnChar(others[i], spots[i], 1.6f);
                    fl.Face(-spots[i]);
                    fielders.Add(fl);
                }
                for (int i = 0; i < 3; i++) { if (runners[i]) Destroy(runners[i].gameObject); runners[i] = null; }
            }
            if (shownBatter != gs.Batter.Id)
            {
                if (batterView) Destroy(batterView.gameObject);
                batterView = SpawnChar(gs.Batter.Id, new Vector3(-0.95f, 0.05f, 0.1f), 1.6f);
                batterView.Face(Vector3.right);
                shownBatter = gs.Batter.Id;
            }
            // Runners stand on occupied bases (ids are the previous batters, for show).
            var lu = gs.BattingTeam.Lineup;
            int ord = gs.Order[gs.BattingSide];
            for (int i = 0; i < 3; i++)
            {
                string want = gs.Bases[i] ? lu[((ord - 1 - i) % lu.Length + lu.Length) % lu.Length] : null;
                if (runners[i] && (want == null || runners[i].Id != want)) { Destroy(runners[i].gameObject); runners[i] = null; }
                if (want != null && !runners[i])
                {
                    runners[i] = SpawnChar(want, BasePos[i] + new Vector3(-0.6f, 0, 0), 1.5f);
                    runners[i].Face(i == 0 ? Vector3.forward : i == 1 ? Vector3.left : Vector3.back);
                }
            }
        }

        Vector3 ZoneToWorld(double x, double y) => new Vector3((float)x * ZoneHalfW, ZoneCY - (float)y * ZoneHalfH, 0);

        Vector2 WorldToZone(Vector3 w) => new Vector2(w.x / ZoneHalfW, (ZoneCY - w.y) / ZoneHalfH);

        Vector2? MouseZone()
        {
            var ray = Cam.ScreenPointToRay(In.MousePos);
            var plane = new Plane(Vector3.back, Vector3.zero);
            if (!plane.Raycast(ray, out float d)) return null;
            return WorldToZone(ray.GetPoint(d));
        }

        // ── play flow ──

        void StartHalf()
        {
            halfBanner = $"{gs.Inning} 局{(gs.Top ? "上" : "下")}　{gs.BattingTeam.Name} 進攻";
            phase = Phase.Half;
            timer = 1.8f;
            pitch = null;
            hitT = -1;
            RefreshPlayers();
        }

        void NewAtBatPitch()
        {
            pitch = null;
            outcome = null;
            hitT = -1;
            swingAt = -1;
            swung = false;
            swingAnim = -1;
            cpuResult = null;
            cpuSwingShown = false;
            RefreshPlayers();
            if (UserBatting)
            {
                phase = Phase.Ready;
                timer = 0.9f;
            }
            else
            {
                phase = Phase.Aim;
                if (!gs.Pitcher.Pitches.Contains(selPitch)) selPitch = gs.Pitcher.Pitches[0];
            }
        }

        void Release(PitchName type, double aimX, double aimY)
        {
            pitch = Sim.MakePitch(gs.Pitcher, type, aimX, aimY, Rand.Default);
            phase = Phase.Windup;
            timer = WINDUP;
            flightT = 0;
            pitcherView.Act("throw", WINDUP + 0.2f);
            if (UserBatting) pitch.Time *= Data.USER_PITCH_SLOW;
            else cpuResult = Sim.CpuBat(gs.Batter, pitch, Rand.Default);
        }

        void FinishPitch(PitchOutcome o)
        {
            string beforeHalf = $"{gs.Inning}{gs.Top}";
            outcome = o;
            gs.Apply(o);
            note = gs.LastNote != "" ? gs.LastNote : Data.OUTCOME_TEXT[o];
            phase = Phase.Result;
            bool inPlay = Data.HitBases(o) > 0 || Data.IsOut(o);
            timer = inPlay ? RESULT_TIME + 0.4f : 1.1f;
            bool contact = o != PitchOutcome.Ball && o != PitchOutcome.Strike;
            ball.gameObject.SetActive(false);
            if (contact)
            {
                Sfx.Play("crack");
                var c = ZoneToWorld(pitch?.EndX ?? 0, pitch?.EndY ?? 0);
                Fx.Burst(c, 20, Js.Hex("#ffe066"), 3f, 0.4f, 0.05f);
                hitT = 0;
                hitKind = o;
                hitAng = o == PitchOutcome.Foul ? (Rand.Value < 0.5 ? -1.1f : 1.1f) : (float)(Rand.Value - 0.5) * 1.2f;
                hitBall.gameObject.SetActive(true);
            }
            else Sfx.Play("mitt");
            if (o == PitchOutcome.Homerun) { Sfx.Play("cheer"); Rig.Shake(0.15f, 0.5f); batterView.Act("win", 1.2f); }
            else if (Data.HitBases(o) > 0) { Sfx.Play("cheer"); batterView.Act("hop", 0.5f); }
            if (gs.LastNote.StartsWith("三振")) { Sfx.Notes("E5 C5", 0.09f, Wave.Triangle, 0.4f); batterView.Act("lose", 0.8f); }
            halfChanged = beforeHalf != $"{gs.Inning}{gs.Top}";
        }

        bool CpuSwings(PitchOutcome o)
        {
            if (o == PitchOutcome.Ball) return false;
            if (o != PitchOutcome.Strike) return true;
            return !Sim.InZone(pitch.EndX, pitch.EndY) || System.Math.Abs(pitch.EndX * 10) % 2 > 1;
        }

        void Swing(float at)
        {
            if (swung) return;
            swingAt = at;
            swung = true;
            swingAnim = 0;
            Sfx.Play("whoosh");
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
                    if (In.Confirm || In.MouseDown(0)) { Sfx.Notes("C5 E5 G5 C6:2", 0.07f, Wave.Square, 0.4f); GoSelect(); }
                    break;
                case Scr.Select:
                    if (In.LeftDown) userTeam = "whale";
                    if (In.RightDown) userTeam = "cat";
                    if (In.Confirm) { Sfx.Notes("G4 C5 E5 G5:2", 0.07f, Wave.Square, 0.4f); GoPlay(); }
                    if (In.Back) GoTitle();
                    break;
                case Scr.Play:
                    if (In.Back) { GoTitle(); return; }
                    UpdatePlay(dt);
                    break;
                case Scr.Over:
                    if (In.Confirm) GoPlay();
                    else if (In.Back) GoTitle();
                    if (lineup != null && Mathf.Repeat(t, 0.8f) < dt)
                        foreach (var c in lineup) if (c) c.Act("hop", 0.5f);
                    break;
            }
        }

        void UpdatePlay(float dt)
        {
            bool click = In.MouseDown(0) && !OverPitchButtons();
            bool act = In.Confirm;

            bool canMove = (UserBatting && (phase == Phase.Ready || phase == Phase.Windup || (phase == Phase.Flight && !swung)))
                || (!UserBatting && phase == Phase.Aim);
            if (canMove)
            {
                float sp = (float)(Data.CURSOR_SPEED * dt * 2 / Data.ZONE_W);
                float ratio = (float)(Data.ZONE_W / Data.ZONE_H);
                if (In.LeftHeld) cur.x -= sp;
                if (In.RightHeld) cur.x += sp;
                if (In.UpHeld) cur.y -= sp * ratio;
                if (In.DownHeld) cur.y += sp * ratio;
                var mp = In.MousePos;
                if ((mp - lastMouse).sqrMagnitude > 0.25f)
                {
                    var z = MouseZone();
                    if (z.HasValue) cur = z.Value;
                }
                float lim = UserBatting ? 1.35f : 1.7f;
                cur.x = Mathf.Clamp(cur.x, -lim, lim);
                cur.y = Mathf.Clamp(cur.y, -lim, lim);
            }
            lastMouse = In.MousePos;

            switch (phase)
            {
                case Phase.Half:
                    timer -= dt;
                    if (timer <= 0) NewAtBatPitch();
                    break;
                case Phase.Ready:
                    timer -= dt;
                    if (timer <= 0)
                    {
                        var c = Sim.CpuPitch(gs.Pitcher, gs.Balls, gs.Strikes, Rand.Default);
                        Release(c.type, c.aimX, c.aimY);
                    }
                    break;
                case Phase.Aim:
                    {
                        var ps = gs.Pitcher.Pitches;
                        int dgt = In.DigitDown;
                        if (dgt >= 1 && dgt <= ps.Length) selPitch = ps[dgt - 1];
                        if (act || click) Release(selPitch, cur.x, cur.y);
                        break;
                    }
                case Phase.Windup:
                    timer -= dt;
                    if (UserBatting && (act || click)) Swing(-timer);
                    if (timer <= 0) { phase = Phase.Flight; Sfx.Play("whoosh"); }
                    break;
                case Phase.Flight:
                    {
                        flightT += dt;
                        var pt = pitch;
                        if (UserBatting)
                        {
                            if (!swung && (act || click)) Swing(flightT);
                            if (swung && flightT >= pt.Time && System.Math.Abs(swingAt - pt.Time) <= Data.USER_SWING_WINDOW)
                            {
                                double dist = Js.Hypot(cur.x - pt.EndX, cur.y - pt.EndY);
                                FinishPitch(Sim.ResolveSwing(gs.Batter, swingAt - pt.Time, dist, Rand.Default, Data.USER_SWING_WINDOW, Data.USER_MEET_BONUS));
                            }
                            else if (flightT >= pt.Time + Data.USER_SWING_WINDOW + 0.01)
                                FinishPitch(swung ? PitchOutcome.Strike : Sim.InZone(pt.EndX, pt.EndY) ? PitchOutcome.Strike : PitchOutcome.Ball);
                        }
                        else
                        {
                            var o = cpuResult.Value;
                            if (CpuSwings(o) && !cpuSwingShown && flightT >= pt.Time - 0.06) { cpuSwingShown = true; swingAnim = 0; }
                            if (flightT >= pt.Time) FinishPitch(o);
                        }
                        break;
                    }
                case Phase.Result:
                    timer -= dt;
                    if (hitT >= 0) hitT += dt;
                    if (timer <= 0)
                    {
                        hitBall.gameObject.SetActive(false);
                        if (gs.Over) { GoOver(); return; }
                        if (halfChanged) StartHalf(); else NewAtBatPitch();
                    }
                    break;
            }
            if (swingAnim >= 0) swingAnim += dt;
            Animate3D();
        }

        void Animate3D()
        {
            // Bat swing: from cocked over the shoulder to through the zone.
            float sw = swingAnim >= 0 ? Mathf.Clamp01(swingAnim / 0.2f) : 0;
            batPivot.localRotation = Quaternion.Euler(Mathf.Lerp(-60f, 5f, sw), Mathf.Lerp(-100f, 70f, sw), 0);
            if (batterView) batterView.transform.localRotation = Quaternion.Euler(0, 90 + Mathf.Lerp(0, 50, sw), 0);

            // Pitched ball.
            if (pitch != null && phase == Phase.Flight)
            {
                ball.gameObject.SetActive(true);
                float tt = Mathf.Clamp((float)(flightT / pitch.Time), 0, 1.15f);
                var zp = Sim.PitchPos(pitch, Mathf.Min(tt, 1));
                var end = ZoneToWorld(zp.x, zp.y);
                var rel = Mound + new Vector3(0.3f, 1.7f, -0.3f);
                float e = Mathf.Pow(tt, 1.35f);
                var pos = Vector3.LerpUnclamped(rel, end, e);
                if (tt > 1) pos.z = Mathf.Lerp(0, -1.2f, (tt - 1) / 0.15f);
                ball.localPosition = pos;
                Prim.SetColor(ball.gameObject, Js.Hex(Data.PITCH_TYPES[pitch.Type].Color));
            }
            else if (phase != Phase.Result) ball.gameObject.SetActive(false);

            // Batted ball flying away.
            if (hitT >= 0 && pitch != null)
            {
                var st = ZoneToWorld(pitch.EndX, pitch.EndY);
                float q = Mathf.Clamp01(hitT / 1.4f);
                float far = hitKind == PitchOutcome.Homerun ? 1.15f : hitKind == PitchOutcome.Groundout ? 0.45f : hitKind == PitchOutcome.Foul ? 0.7f : 0.75f;
                var target = new Vector3(Mathf.Sin(hitAng) * 42 * far, 0.1f, Mathf.Cos(hitAng) * 42 * far);
                float arc = hitKind == PitchOutcome.Groundout ? 0 : Mathf.Sin(q * Mathf.PI) * (hitKind == PitchOutcome.Lineout ? 3f : 14f);
                hitBall.localPosition = Vector3.Lerp(st, target, q) + Vector3.up * arc;
                // Follow the ball with the camera a little.
                Rig.Follow(new Vector3(0, 1.45f + q * 2f, -3.2f), Vector3.Lerp(new Vector3(0, 1.05f, 6f), hitBall.position, 0.6f), 4f, Time.deltaTime);
            }
            else Rig.Follow(new Vector3(0, 1.45f, -3.2f), new Vector3(0, 1.05f, 6f), 6f, Time.deltaTime);
        }

        readonly List<Rect> pitchBtns = new List<Rect>();

        bool OverPitchButtons()
        {
            var m = In.GuiMouse;
            foreach (var r in pitchBtns) if (r.Contains(m)) return true;
            return false;
        }

        // ── GUI ──

        void OnGUI()
        {
            Gui.Begin();
            switch (scr)
            {
                case Scr.Title:
                    Gui.TitleCard("萌友棒球對決", "藍鯨隊 vs 貓咪隊　三局制一球決勝負", null, t);
                    break;
                case Scr.Select: DrawSelect(); break;
                case Scr.Play: DrawPlay(); break;
                case Scr.Over: DrawOver(); break;
            }
        }

        void DrawSelect()
        {
            float W = Gui.W, H = Gui.H, cx = W / 2;
            Gui.Label("選擇你的球隊", cx, 50, 44, Color.white, 0.5f, 0.5f, Js.Hex("#1b2a6b"));
            string[] ids = { "whale", "cat" };
            for (int i = 0; i < 2; i++)
            {
                var team = Data.TEAMS[ids[i]];
                bool sel = userTeam == team.Id;
                float bw = Mathf.Min(560, W * 0.44f);
                var r = new Rect(cx + (i == 0 ? -bw - 16 : 16), H - 250, bw, 170);
                Gui.Panel(r, sel ? Js.Hex(team.Dark, 0.93f) : Js.Hex("#0d1433", 0.8f), sel ? Js.Hex("#ffe066") : new Color(1, 1, 1, 0.27f));
                Gui.Label(team.Name, r.center.x, r.y + 26, 32, sel ? Js.Hex("#ffe066") : Color.white, 0.5f, 0.5f, Color.black);
                var members = Members(team);
                for (int j = 0; j < members.Count; j++)
                {
                    var pl = Data.PLAYERS[members[j]];
                    float x = r.x + r.width / 4 * (j + 0.5f);
                    Gui.Label(pl.Name, x, r.y + 60, 20, Color.white);
                    Gui.Label(members[j] == team.Pitcher ? "投手" : $"第{System.Array.IndexOf(team.Lineup, members[j]) + 1}棒", x, r.y + 84, 15, Js.Hex("#9fd2ff"));
                    Gui.Label($"巧{pl.Meet} 力{pl.Power}", x, r.y + 104, 14, Js.Hex("#dddddd"));
                }
                var ace = Data.PLAYERS[team.Pitcher];
                Gui.Label($"王牌投手 {ace.Name}｜{ace.Velo} km/h｜控球 {ace.Control}", r.center.x, r.y + 132, 16, Color.white);
                Gui.Label($"球種：{string.Join("、", ace.Pitches)}", r.center.x, r.y + 154, 16, Js.Hex(Data.PITCH_TYPES[ace.Pitches[1]].Color));
                if (Gui.Clicked(r)) { userTeam = team.Id; Sfx.Beep(660, 0.06f, Wave.Square, 0.3f); }
            }
            if (Gui.Button(new Rect(cx - 150, H - 72, 300, 56), "比賽開始！", 28, Js.Hex("#d9452b"), Js.Hex("#ff6a47"))) GoPlay();
            Gui.Label("← → 選擇　Space 確認　Esc 返回", cx, H - 8, 16, Js.Hex("#cccccc"), 0.5f, 1f);
        }

        void DrawPlay()
        {
            if (gs == null) return;
            float W = Gui.W, H = Gui.H;
            // Strike zone (3×3 grid) projected from world space.
            var tl = Gui.WorldToGui(Cam, ZoneToWorld(-1, -1));
            var br = Gui.WorldToGui(Cam, ZoneToWorld(1, 1));
            float zx = tl.x, zy = tl.y, zw = br.x - tl.x, zh = br.y - tl.y;
            Gui.Rect(zx, zy, zw, zh, new Color(1, 1, 1, 0.08f));
            for (int i = 0; i <= 3; i++)
            {
                bool edge = i % 3 == 0;
                var c = new Color(1, 1, 1, edge ? 0.8f : 0.3f);
                Gui.Line(zx + zw * i / 3, zy, zx + zw * i / 3, zy + zh, edge ? 3 : 1, c);
                Gui.Line(zx, zy + zh * i / 3, zx + zw, zy + zh * i / 3, edge ? 3 : 1, c);
            }
            // Cursor.
            var cw = Gui.WorldToGui(Cam, ZoneToWorld(cur.x, cur.y));
            if (UserBatting && phase != Phase.Half)
            {
                float rr = (float)(Sim.MeetRadius(gs.Batter, Data.USER_MEET_BONUS) * zw / 2);
                Gui.Circle(cw.x, cw.y, rr, Js.Hex("#ffe066", 0.22f));
                Gui.Ring(cw.x, cw.y, rr, 3, Js.Hex("#ffcc00", 0.95f));
                Gui.Circle(cw.x, cw.y, 4, Js.Hex("#ff3300"));
                // Timing aid: a ring closes in on the ball and turns green while a swing can connect.
                if (phase == Phase.Flight && pitch != null && !swung && ball.gameObject.activeSelf)
                {
                    float tt = Mathf.Clamp01((float)(flightT / pitch.Time));
                    var bp = Gui.WorldToGui(Cam, ball.position);
                    bool inWindow = System.Math.Abs(flightT - pitch.Time) <= Data.USER_SWING_WINDOW;
                    Gui.Ring(bp.x, bp.y, Mathf.Lerp(zw * 0.9f, zw * 0.07f, tt), inWindow ? 5 : 3,
                        inWindow ? Js.Hex("#44ff66", 0.95f) : new Color(1, 1, 1, 0.55f));
                    if (inWindow) Gui.Label("揮棒！", cw.x, cw.y - rr - 18, 22, Js.Hex("#44ff66"), 0.5f, 0.5f, Color.black);
                }
            }
            else if (!UserBatting && phase == Phase.Aim)
            {
                var pt = Data.PITCH_TYPES[selPitch];
                var col = Js.Hex(pt.Color);
                Gui.Ring(cw.x, cw.y, 16, 3, col);
                Gui.Line(cw.x - 24, cw.y, cw.x + 24, cw.y, 2, col);
                Gui.Line(cw.x, cw.y - 24, cw.x, cw.y + 24, 2, col);
                Gui.Line(cw.x - (float)pt.Dx * zw * 0.5f, cw.y - (float)pt.Dy * zh * 0.5f, cw.x, cw.y, 2, new Color(col.r, col.g, col.b, 0.5f));
            }
            DrawPanels(W, H);
        }

        void DrawPanels(float W, float H)
        {
            // Scoreboard.
            Gui.Panel(new Rect(16, 16, 300, 124));
            string[] sides = { "away", "home" };
            for (int i = 0; i < 2; i++)
            {
                float y = 44 + i * 42;
                var team = sides[i] == "away" ? gs.Away : gs.Home;
                Gui.Rect(28, y - 14, 10, 28, Js.Hex(team.Color));
                bool batting = gs.BattingSide == sides[i];
                Gui.Label($"{(batting ? "▶" : "　")}{team.Name}{(team.Id == userTeam ? "（你）" : "")}", 46, y, 22, batting ? Js.Hex("#ffe066") : Color.white, 0f, 0.5f);
                Gui.Label(gs.Runs[sides[i]].ToString(), 296, y, 30, Color.white, 1f, 0.5f);
            }
            Gui.Label($"{gs.Inning} 局{(gs.Top ? "上" : "下")}（共 {gs.Innings} 局）", 166, 124, 18, Js.Hex("#9fd2ff"));
            // Count + outs.
            Gui.Panel(new Rect(16, 156, 160, 106));
            void Dots(float y, string name, int n, int max, string col)
            {
                Gui.Label(name, 34, y, 22, Color.white);
                for (int i = 0; i < max; i++) Gui.Circle(62 + i * 26, y, 9, i < n ? Js.Hex(col) : new Color(1, 1, 1, 0.13f));
            }
            Dots(180, "B", gs.Balls, 3, "#3ddc84");
            Dots(210, "S", gs.Strikes, 2, "#ffd23f");
            Dots(240, "O", gs.Outs, 2, "#ff4d4d");
            // Bases diamond.
            Gui.Panel(new Rect(190, 156, 126, 106));
            void Base(float x, float y, bool on)
            {
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(45, new Vector2(x, y) * Gui.Scale);
                Gui.Rect(x - 10, y - 10, 20, 20, on ? Js.Hex("#ffcf4a") : new Color(1, 1, 1, 0.2f));
                GUI.matrix = m;
            }
            Base(283, 214, gs.Bases[0]);
            Base(253, 184, gs.Bases[1]);
            Base(223, 214, gs.Bases[2]);
            // Batter / pitcher card.
            var bt = gs.Batter;
            var pit = gs.Pitcher;
            Gui.Panel(new Rect(W - 336, 16, 320, 140));
            Gui.Label($"打者　{bt.Name}（{bt.Title}）", W - 320, 42, 21, Color.white, 0f, 0.5f);
            Gui.Label($"巧打 {bt.Meet}　力量 {bt.Power}　跑速 {bt.Speed}", W - 320, 70, 17, Js.Hex("#cfe6ff"), 0f, 0.5f);
            Gui.Label($"投手　{pit.Name}　控球 {pit.Control}", W - 320, 102, 21, Color.white, 0f, 0.5f);
            if (pitch != null && (phase == Phase.Flight || phase == Phase.Result))
                Gui.Label($"{pitch.Type}　{pitch.Kmh} km/h", W - 320, 132, 19, Js.Hex(Data.PITCH_TYPES[pitch.Type].Color), 0f, 0.5f);

            // Pitch selector when the user pitches.
            pitchBtns.Clear();
            if (!UserBatting && phase == Phase.Aim)
            {
                var ps = gs.Pitcher.Pitches;
                float bw = 124, gap = 10, total = ps.Length * bw + (ps.Length - 1) * gap;
                for (int i = 0; i < ps.Length; i++)
                {
                    var r = new Rect(W / 2 - total / 2 + i * (bw + gap), H - 74, bw, 54);
                    pitchBtns.Add(r);
                    if (Gui.Button(r, $"{i + 1} {ps[i]}", 22, null, null, ps[i] == selPitch)) selPitch = ps[i];
                }
                Gui.Label("滑鼠/方向鍵瞄準　數字鍵選球種　點擊或 Space 投球", W / 2, H - 96, 18, Color.white, 0.5f, 0.5f, Color.black);
            }
            else if (UserBatting && (phase == Phase.Ready || phase == Phase.Windup || phase == Phase.Flight))
                Gui.Label("滑鼠/方向鍵移動打擊圈　點擊或 Space 揮棒", W / 2, H - 30, 20, Color.white, 0.5f, 0.5f, Color.black);

            if (phase == Phase.Half)
                Gui.Banner(halfBanner, UserBatting ? "你的攻擊！看準球路揮棒" : "你的防守！選球種、瞄準、投球");
            if (phase == Phase.Result && outcome.HasValue)
            {
                var o = outcome.Value;
                bool big = Data.HitBases(o) > 0;
                bool bad = Data.IsOut(o) || note.StartsWith("三振");
                var col = o == PitchOutcome.Homerun ? Js.Hex("#ff5fa2") : big ? Js.Hex("#ffe066") : bad ? Js.Hex("#ff6b6b") : Color.white;
                Gui.Label(note, W / 2, H * 0.3f, big ? 64 : 48, col, 0.5f, 0.5f, Js.Hex("#1a1030"));
            }
        }

        void DrawOver()
        {
            var g = last;
            if (g == null) return;
            float W = Gui.W, H = Gui.H, cx = W / 2;
            string w = g.Winner();
            bool won = UserWon();
            Gui.Label(w == "tie" ? "平手！" : won ? "比賽獲勝！" : "惜敗……", cx, 80, 72, won ? Js.Hex("#ffe066") : Color.white, 0.5f, 0.5f, Js.Hex("#1b2a6b"));
            int cols = Mathf.Max(g.Line["away"].Count, Mathf.Max(g.Line["home"].Count, g.Innings));
            float cwid = 52, tw = 200 + cols * cwid + 2 * cwid + 20;
            float x0 = cx - tw / 2, y0 = H - 250;
            Gui.Panel(new Rect(x0, y0, tw, 130));
            for (int i = 0; i < cols; i++) Gui.Label((i + 1).ToString(), x0 + 200 + i * cwid + cwid / 2, y0 + 24, 20, Js.Hex("#9fd2ff"));
            Gui.Label("R", x0 + 200 + cols * cwid + cwid / 2, y0 + 24, 20, Js.Hex("#ffe066"));
            Gui.Label("H", x0 + 200 + (cols + 1) * cwid + cwid / 2, y0 + 24, 20, Js.Hex("#9fd2ff"));
            string[] sides = { "away", "home" };
            for (int r = 0; r < 2; r++)
            {
                float y = y0 + 64 + r * 40;
                var team = sides[r] == "away" ? g.Away : g.Home;
                Gui.Rect(x0 + 14, y - 13, 8, 26, Js.Hex(team.Color));
                Gui.Label(team.Name, x0 + 30, y, 22, Color.white, 0f, 0.5f);
                var line = g.Line[sides[r]];
                for (int i = 0; i < cols; i++)
                {
                    string v = i < line.Count ? line[i].ToString() : sides[r] == "home" && i == g.Innings - 1 ? "X" : "-";
                    Gui.Label(v, x0 + 200 + i * cwid + cwid / 2, y, 22, Color.white);
                }
                Gui.Label(g.Runs[sides[r]].ToString(), x0 + 200 + cols * cwid + cwid / 2, y, 26, Js.Hex("#ffe066"));
                Gui.Label(g.Hits[sides[r]].ToString(), x0 + 200 + (cols + 1) * cwid + cwid / 2, y, 22, Color.white);
            }
            if (Gui.Button(new Rect(cx - 250, H - 100, 230, 62), "再比一場", 28, Js.Hex("#d9452b"), Js.Hex("#ff6a47"))) GoPlay();
            if (Gui.Button(new Rect(cx + 20, H - 100, 230, 62), "回到標題", 28)) GoTitle();
        }
    }
}
