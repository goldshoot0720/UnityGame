// 萌友街頭 3x3 — 3D presentation (title → pick 3 → play → result) of the Game2 simulation.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoeGames.Game2
{
    [MoeGame(2)]
    public class Game2Game : MiniGame
    {
        public override TouchLayout Touch => new TouchLayout().Button("投籃", KeyCode.Space).Button("傳/抄", KeyCode.K).Button("切換", KeyCode.Q).Button("衝刺", KeyCode.LeftShift);

        protected override string Backdrop => "court";

        enum Scr { Title, Select, Play, Over }

        // floor px → metres: the 3-pt arc (222 px) is 6.75 m; the rim (132 px) is 3.05 m.
        const float K = 6.75f / 222f, KZ = 3.05f / 132f, DEPTH = (float)(Court.ARC_RX / Court.ARC_RY);

        Scr scr;
        float t;
        List<string> team = new List<string> { "whale", "penguin", "tshirt" };
        List<string> cpu = new List<string> { "calico", "redcat", "sailor" };
        CastPicker picker;
        Chibi[] lineup;
        Sim sim;
        int[] finalScore = { 0, 0 };
        bool win;

        readonly Dictionary<P, Chibi> views = new Dictionary<P, Chibi>();
        readonly Dictionary<P, GameObject> rings = new Dictionary<P, GameObject>();
        readonly Dictionary<P, Vector3> lastPos = new Dictionary<P, Vector3>();
        Transform ballT, marker;

        protected override void Begin()
        {
            Sfx.Define("bounce", Tone.Beep(140, 0.08f, Wave.Sine, 0.5f, 70));
            Sfx.Define("swish", new Tone { Type = Wave.Noise, Duration = 0.3f, Volume = 0.35f, Filter = FilterType.Bandpass, FilterFreq = 3000, FilterFreqEnd = 1200, Q = 1.5f });
            Sfx.Define("rim", Tone.Beep(520, 0.25f, Wave.Triangle, 0.4f, 380));
            Sfx.Define("whistle", Tone.Beep(2600, 0.35f, Wave.Sine, 0.25f));
            Sfx.Define("steal", Tone.Melody("A5 E6", 0.05f, Wave.Square, 0.3f));
            Sfx.Define("block", Tone.Noise(0.15f, 0.6f, FilterType.Lowpass, 700, -1, false));
            Sfx.Define("shoot", Tone.Beep(300, 0.12f, Wave.Sine, 0.2f, 600));
            Sfx.Define("pass", Tone.Beep(500, 0.05f, Wave.Square, 0.15f));
            Rig.Background(Js.Hex("#241436"));
            Sfx.MusicByName("music", 0.3f);
            GoTitle();
        }

        void GoTitle()
        {
            scr = Scr.Title;
            lineup = ShowLineup(Cast.Ids, "#3a1250", "#ffe8a0");
        }

        void GoSelect()
        {
            scr = Scr.Select;
            picker = new CastPicker(Data.BALLERS.Select(b => b.Id).ToList(), 3);
            lineup = ShowLineup(Data.BALLERS.Select(b => b.Id).ToList(), "#3a1250", "#ffe8a0", 1.35f, 1.4f);
        }

        void GoPlay()
        {
            scr = Scr.Play;
            sim = new Sim(team, cpu);
            sim.Sound += n => Sfx.Play(n);
            sim.Swish += side => Fx.Burst(new Vector3(0, 3.05f, 0), 26, side == 0 ? Js.Hex("#5cc8ff") : Js.Hex("#ff6b6b"), 3f, 0.6f, 0.07f);
            sim.Shake += d => Rig.Shake(0.12f, d);
            BuildCourt();
        }

        void GoOver()
        {
            scr = Scr.Over;
            finalScore = (int[])sim.M.Score.Clone();
            win = sim.M.Winner() == 0;
            lineup = ShowLineup(win ? team : cpu, "#3a1250", "#ffe8a0", 1.5f, 1.6f);
            if (win) Sfx.Notes("C5 E5 G5 C6 G5 C6:3", 0.1f, Wave.Square, 0.4f);
            else Sfx.Notes("G4 F4 E4 D4 C4:3", 0.14f, Wave.Triangle, 0.4f);
        }

        // ── court ──

        static Vector3 ToW(double x, double y, double z = 0) => new Vector3((float)(x - 512) * K, (float)z * KZ, -(float)(y - Court.HOOP_Y) * DEPTH * K);

        void BuildCourt()
        {
            ClearWorld();
            views.Clear();
            rings.Clear();
            lastPos.Clear();
            var f = World;
            Prim.Box(f, new Vector3(0, -0.1f, -8), new Vector3(34, 0.2f, 26), Js.Hex("#3b3346"));
            var floor = Prim.Box(f, new Vector3(0, -0.02f, -6.5f), new Vector3(18, 0.06f, 15.5f), Js.Hex("#c77b3a"));
            Mats.TryApply(floor, "court_floor", Scope, new Vector2(1, 1));
            // Key (paint) and free-throw circle.
            Prim.Box(f, new Vector3(0, 0.02f, -2.9f), new Vector3(4.9f, 0.02f, 5.8f), Js.Hex("#3a5ba0"));
            Prim.Cyl(f, new Vector3(0, 0.025f, -5.8f), 3.6f, 0.02f, Js.Hex("#3a5ba0"));
            // Three-point arc (6.75 m) as short segments.
            for (int i = 0; i <= 36; i++)
            {
                float a = Mathf.Lerp(-90f, 90f, i / 36f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(a) * 6.75f, 0.035f, -Mathf.Cos(a) * 6.75f);
                var seg = Prim.Box(f, p, new Vector3(0.62f, 0.02f, 0.08f), Color.white);
                seg.transform.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0);
            }
            // Sidelines follow the court trapezoid.
            for (int s = -1; s <= 1; s += 2)
            {
                var a = ToW(512 + s * (Court.HalfWidth(Court.TOP_Y) - 18), Court.TOP_Y);
                var b = ToW(512 + s * (Court.HalfWidth(Court.BOTTOM_Y) - 18), Court.BOTTOM_Y);
                var line = Prim.Box(f, (a + b) / 2 + Vector3.up * 0.035f, new Vector3(0.1f, 0.02f, Vector3.Distance(a, b)), Color.white);
                line.transform.localRotation = Quaternion.LookRotation(b - a);
            }
            var top = ToW(512, Court.TOP_Y);
            Prim.Box(f, top + Vector3.up * 0.035f, new Vector3(16, 0.02f, 0.1f), Color.white);
            // Hoop: pole, backboard, rim, net.
            Prim.Box(f, new Vector3(0, 1.8f, 1.6f), new Vector3(0.25f, 3.6f, 0.25f), Js.Hex("#444455"));
            Prim.Box(f, new Vector3(0, 3.5f, 1.2f), new Vector3(0.2f, 0.2f, 0.9f), Js.Hex("#444455"));
            Prim.Box(f, new Vector3(0, 3.45f, 0.72f), new Vector3(1.8f, 1.05f, 0.06f), Js.Hex("#f4f4f4"));
            Prim.Box(f, new Vector3(0, 3.3f, 0.68f), new Vector3(0.6f, 0.45f, 0.02f), Js.Hex("#e0443e"));
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2;
                Prim.Box(f, new Vector3(Mathf.Cos(a) * 0.23f, 3.05f, 0.23f + Mathf.Sin(a) * 0.23f - 0.23f), new Vector3(0.07f, 0.03f, 0.07f), Js.Hex("#ff6a1a"));
                Prim.Box(f, new Vector3(Mathf.Cos(a) * 0.17f, 2.85f, Mathf.Sin(a) * 0.17f), new Vector3(0.02f, 0.4f, 0.02f), Color.white);
            }
            Art.Backdrop(f, "court", new Vector3(0, 6f, 9f), 16f);
            // Street backdrop.
            for (int i = -3; i <= 3; i++)
                Prim.Box(f, new Vector3(i * 5f, 3f, 5f + Mathf.Abs(i) * 0.5f), new Vector3(4.6f, 6f + (i % 2) * 2f, 1f), Js.Hex(i % 2 == 0 ? "#4a2d6a" : "#5a3a7a"));

            foreach (var p in sim.Ps)
            {
                var c = SpawnChar(p.B.Id, ToW(p.X, p.Y), 1.7f);
                views[p] = c;
                rings[p] = Prim.Cyl(f, ToW(p.X, p.Y) + Vector3.up * 0.04f, 0.9f, 0.02f, p.Side == 0 ? Js.Hex("#39c6ff") : Js.Hex("#ff4d5e"));
                lastPos[p] = ToW(p.X, p.Y);
            }
            ballT = Prim.Sphere(f, Vector3.zero, 0.26f, Js.Hex("#f07a1e")).transform;
            marker = Prim.Box(f, Vector3.zero, new Vector3(0.25f, 0.25f, 0.25f), Js.Hex("#39c6ff")).transform;
            Prim.SetColor(marker.gameObject, Js.Hex("#39c6ff"), true);
            Rig.Set(new Vector3(0, 7.5f, -21f), new Vector3(0, 1.2f, -5.5f), 42f);
        }

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
                    if (In.Back) { GoTitle(); return; }
                    if (picker.UpdateKeys(lineup)) StartMatch();
                    break;
                case Scr.Play:
                    if (In.Back) { GoTitle(); return; }
                    var k = new Controls
                    {
                        Dx = (In.RightHeld ? 1 : 0) - (In.LeftHeld ? 1 : 0),
                        // Screen-down (towards the camera) is +y in floor px.
                        Dy = (In.DownHeld ? 1 : 0) - (In.UpHeld ? 1 : 0),
                        ShootPressed = In.Down(KeyCode.Space, KeyCode.J),
                        ShootHeld = In.Held(KeyCode.Space, KeyCode.J),
                        PassPressed = In.Down(KeyCode.K, KeyCode.X),
                        SwitchPressed = In.Down(KeyCode.Q, KeyCode.L),
                        Sprint = In.Held(KeyCode.LeftShift, KeyCode.RightShift),
                    };
                    if (!sim.Update(dt, k)) { GoOver(); return; }
                    SyncViews(dt);
                    break;
                case Scr.Over:
                    if (In.Confirm) GoPlay();
                    else if (In.Back) GoSelect();
                    if (lineup != null && Mathf.Repeat(t, 0.8f) < dt)
                        foreach (var c in lineup) if (c) c.Act(win ? "win" : "hop", 0.6f);
                    break;
            }
        }

        void StartMatch()
        {
            team = new List<string>(picker.Picks);
            cpu = Data.BALLERS.Select(b => b.Id).Where(id => !team.Contains(id)).Take(3).ToList();
            Sfx.Notes("G4 C5 E5 G5:2", 0.07f, Wave.Square, 0.4f);
            GoPlay();
        }

        void SyncViews(float dt)
        {
            var hoop = new Vector3(0, 0, 0);
            foreach (var p in sim.Ps)
            {
                var c = views[p];
                var pos = ToW(p.X, p.Y, p.Z);
                var ground = ToW(p.X, p.Y);
                var vel = ground - lastPos[p];
                lastPos[p] = ground;
                c.transform.localPosition = pos;
                rings[p].transform.localPosition = ground + Vector3.up * 0.04f;
                if (vel.sqrMagnitude > 1e-6f) c.FaceSmooth(vel, 12f, dt);
                else if (sim.M.Possession == p.Side) c.FaceSmooth(hoop - ground, 6f, dt);
                else c.FaceSmooth(ToW(sim.Ball.X, sim.Ball.Y) - ground, 6f, dt);
                c.SetLoop(p.Moving ? Chibi.Loop.Run : Chibi.Loop.Idle, 1.2f);
                if (p.Windup > 0 && !c.Busy) c.Act("jump", 0.45f);
            }
            var b = sim.Ball;
            ballT.gameObject.SetActive(!(b.Mode == BallMode.Dead && sim.Pause <= 0));
            ballT.localPosition = ToW(b.X, b.Y, b.Z);
            ballT.Rotate(300 * dt, 0, 0);
            var ctrl = sim.Ctrl;
            marker.localPosition = ToW(ctrl.X, ctrl.Y, ctrl.Z) + Vector3.up * 2.1f + Vector3.up * Mathf.Sin(t * 6) * 0.08f;
            marker.localRotation = Quaternion.Euler(45, t * 120, 45);
            // Follow the play a little.
            float bx = (float)(b.X - 512) * K;
            Rig.Follow(new Vector3(bx * 0.35f, 7.5f, -21f), new Vector3(bx * 0.5f, 1.2f, -5.5f), 3f, dt);
        }

        // ── GUI ──

        void OnGUI()
        {
            Gui.Begin();
            float W = Gui.W, H = Gui.H, cx = W / 2;
            switch (scr)
            {
                case Scr.Title:
                    Gui.TitleCard("萌友街頭 3x3", "三對三半場鬥牛｜先得 21 分獲勝", null, t, Js.Hex("#ffe8a0"));
                    break;
                case Scr.Select:
                    Gui.Label($"選出 3 名隊員（{picker.Picks.Count}/3）", cx, 46, 40, Color.white, 0.5f, 0.5f, Js.Hex("#3a1250"));
                    picker.Draw(Cam, lineup, 130, (r, i) =>
                    {
                        var bl = Data.BALLERS[i];
                        Gui.Label(bl.Name, r.center.x, r.y + 16, 18, Color.white);
                        Gui.Label(bl.Title, r.center.x, r.y + 34, 12, Js.Hex("#9fd2ff"));
                        CastPicker.Stat(r, r.y + 54, "投籃", bl.Shoot);
                        CastPicker.Stat(r, r.y + 69, "三分", bl.Three);
                        CastPicker.Stat(r, r.y + 84, "速度", bl.Speed);
                        CastPicker.Stat(r, r.y + 99, "防守", bl.Defense);
                        CastPicker.Stat(r, r.y + 114, "彈跳", bl.Jump);
                    });
                    bool ready = picker.Ready;
                    if (Gui.Button(new Rect(cx - 150, H - 74, 300, 56), ready ? "上場比賽！" : "請選滿 3 人", 28, ready ? Js.Hex("#d9452b") : Js.Hex("#555a70"), ready ? Js.Hex("#ff6a47") : Js.Hex("#555a70")) && ready) StartMatch();
                    Gui.Label("點擊角色或方向鍵＋Space 選擇　Enter 開始　Esc 返回", cx, H - 10, 15, Js.Hex("#dddddd"), 0.5f, 1f);
                    break;
                case Scr.Play: DrawHud(W, H); break;
                case Scr.Over:
                    bool draw = finalScore[0] == finalScore[1];
                    Gui.Label(draw ? "平手" : win ? "勝利！街頭王者！" : "落敗…下次再戰！", cx, 90, 64, win ? Js.Hex("#ffe066") : Color.white, 0.5f, 0.5f, Js.Hex("#3a1250"));
                    Gui.Label($"{finalScore[0]}  :  {finalScore[1]}", cx, 180, 72, Color.white, 0.5f, 0.5f, Color.black);
                    if (Gui.Button(new Rect(cx - 250, H - 100, 230, 62), "再戰一場", 28, Js.Hex("#d9452b"), Js.Hex("#ff6a47"))) GoPlay();
                    if (Gui.Button(new Rect(cx + 20, H - 100, 230, 62), "重選隊員", 28)) GoSelect();
                    break;
            }
        }

        void DrawHud(float W, float H)
        {
            var m = sim.M;
            float cx = W / 2;
            // Shot meter beside the controlled shooter.
            if (sim.Meter >= 0)
            {
                var c = sim.Ctrl;
                var sp = Gui.WorldToGui(Cam, ToW(c.X, c.Y, c.Z) + new Vector3(0.6f, 1.9f, 0));
                float mh = 90, mx = sp.x, my = sp.y - mh / 2;
                var sw = Data.METER_SWEET;
                Gui.Rect(mx - 2, my - 2, 16, mh + 4, new Color(0, 0, 0, 0.7f));
                Gui.Rect(mx, my + mh * (1 - (float)sw[1]), 12, mh * (float)(sw[1] - sw[0]), Js.Hex("#5cffb0", 0.6f));
                float fill = Mathf.Clamp((float)sim.Meter, 0, 1.15f);
                Gui.Rect(mx, my + mh * (1 - Mathf.Min(fill, 1)), 12, mh * Mathf.Min(fill, 1), fill > sw[1] ? Js.Hex("#ff5f5f") : Js.Hex("#ffe066"));
            }
            Gui.Panel(new Rect(cx - 250, 10, 500, 92));
            Gui.Rect(cx - 250, 10, 8, 92, Js.Hex("#39c6ff"));
            Gui.Rect(cx + 242, 10, 8, 92, Js.Hex("#ff4d5e"));
            Gui.Label("你的隊伍", cx - 160, 34, 20, Js.Hex("#9fe3ff"));
            Gui.Label("電腦隊", cx + 160, 34, 20, Js.Hex("#ffb0b8"));
            Gui.Label(m.Score[0].ToString(), cx - 160, 72, 44, Color.white);
            Gui.Label(m.Score[1].ToString(), cx + 160, 72, 44, Color.white);
            int mm = (int)(m.Clock / 60), ss = (int)(m.Clock % 60);
            Gui.Label(m.Overtime ? "驟死延長" : $"{mm}:{ss:00}", cx, 36, 28, Js.Hex("#ffe066"));
            int sc = (int)System.Math.Ceiling(m.ShotClock);
            Gui.Label($"進攻 {sc}", cx, 76, 22, sc <= 4 ? Js.Hex("#ff5f5f") : Color.white);
            Gui.Label("先得 21 分獲勝　弧內 1 分／弧外 2 分", cx, 120, 16, Color.white, 0.5f, 0.5f, Color.black);
            string pos = m.Possession == 0 ? "我方球權" : "對方球權";
            Gui.Label(m.MustClear ? $"{pos}｜需清球" : pos, cx, 146, 18, m.Possession == 0 ? Js.Hex("#9fe3ff") : Js.Hex("#ffb0b8"), 0.5f, 0.5f, Color.black);
            if (sim.MsgT > 0)
            {
                var col = Js.Hex(sim.MsgCol);
                col.a = Mathf.Clamp01((float)sim.MsgT * 2);
                Gui.Label(sim.Msg, cx, H * 0.36f, 46, col, 0.5f, 0.5f, Js.Hex("#16102a"));
            }
            string help = m.Possession == 0
                ? "方向鍵移動　Shift 衝刺　按住 Space 蓄力投籃（綠區放開）　K 傳球（方向鍵選人）"
                : "方向鍵移動　Space 跳起封蓋　K 抄球　Q 切換球員　Esc 離開";
            Gui.Label(help, cx, H - 22, 17, Color.white, 0.5f, 0.5f, Color.black);
        }
    }
}
