// The 3-on-3 half-court simulation from Game2/cloud/src/scenes/play.ts, separated from
// drawing: players, ball, user input, AI (drive/shoot/pass, off-ball rotation, man defense,
// steals, blocks, rebounds). Coordinates are "floor px" (see Court); z is height in px.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MoeGames.Game2
{
    public class P
    {
        public Baller B;
        public int Side, Idx;
        public double X, Y, Z, Vz;
        public int Facing = 1;
        public double Stun;       // can't act (failed steal / after being stripped)
        public double Windup;     // > 0 while gathering a shot (AI) — seconds left
        public double SpotX, SpotY;
        public double Retarget;
        public bool Moving;
    }

    public enum BallMode { Held, Pass, Shot, Loose, Dead }

    public class Ball
    {
        public BallMode Mode = BallMode.Dead;
        public P Holder;
        public double X = 512, Y = 540, Z;
        public double Vx, Vy, Vz;
        public double FromX, FromY, FromZ;
        public P Target;
        public double T, Dur = 1;
        public bool Make, Three, Blocked;
        public P Shooter;
    }

    public struct Controls
    {
        public int Dx, Dy;
        public bool ShootPressed, ShootHeld, PassPressed, SwitchPressed, Sprint;
    }

    public class Sim
    {
        const double GRAV = 900;
        static readonly (double x, double y)[] OFF_SPOTS =
        {
            (318, 468), (706, 468), (512, 568), (420, 430), (604, 430), (250, 420), (774, 420),
        };

        public readonly Match M = new Match();
        public readonly List<P> Ps = new List<P>();
        public readonly Ball Ball = new Ball();
        public P Ctrl;
        public string Msg = "";
        public double MsgT;
        public string MsgCol = "#ffffff";
        /// <summary>User's shot meter (0..1.15), -1 idle.</summary>
        public double Meter = -1;
        /// <summary>Meter value at which the CPU defender jumps at the user's shot (9 = no contest).</summary>
        double defJumpAt = 9;
        /// <summary>Dead-ball pause before a check.</summary>
        public double Pause;
        public double T;
        double aiTick;
        readonly Func<double> rng;

        /// <summary>Named sound cues: bounce, swish, rim, whistle, steal, block, shoot, pass.</summary>
        public event Action<string> Sound;
        /// <summary>Made basket (side) — sparks at the rim.</summary>
        public event Action<int> Swish;
        public event Action<float> Shake;

        public Sim(IList<string> team, IList<string> cpu, Func<double> rng = null)
        {
            this.rng = rng ?? Rand.Default;
            P Mk(string id, int side, int idx) => new P { B = Data.Get(id), Side = side, Idx = idx, X = 512, Y = 500, SpotX = 512, SpotY = 500 };
            for (int i = 0; i < team.Count; i++) Ps.Add(Mk(team[i], 0, i));
            for (int i = 0; i < cpu.Count; i++) Ps.Add(Mk(cpu[i], 1, i));
            Ctrl = Ps[0];
            M.Possession = this.rng() < 0.5 ? 0 : 1;
            CheckBall("跳球開賽！");
        }

        // ── flow ──

        void Say(string s, string col = "#ffffff", double t = 1.4) { Msg = s; MsgCol = col; MsgT = t; }
        public List<P> Team(int side) => Ps.Where(p => p.Side == side).ToList();

        /// <summary>Reset to a check at the top of the key for the possession team.</summary>
        void CheckBall(string note)
        {
            var off = Team(M.Possession);
            var def = Team(1 - M.Possession);
            for (int i = 0; i < off.Count; i++)
            {
                var p = off[i];
                var s = i == 0 ? (Court.TOP_OF_KEY_X, Court.TOP_OF_KEY_Y) : OFF_SPOTS[i - 1];
                p.X = s.Item1; p.Y = s.Item2 + (i == 0 ? 20 : 0); p.Z = 0; p.Vz = 0; p.Windup = 0; p.Stun = 0;
            }
            for (int i = 0; i < def.Count; i++)
            {
                var p = def[i];
                var o = off[i];
                p.X = Js.Lerp(o.X, Court.HOOP_X, 0.22); p.Y = Js.Lerp(o.Y, Court.HOOP_Y, 0.22); p.Z = 0; p.Vz = 0; p.Windup = 0; p.Stun = 0;
            }
            Give(off[0]);
            M.MustClear = false;
            M.ShotClock = 12;
            Pause = 1.1;
            Meter = -1;
            Say(note, "#ffe066", 1.1);
            if (M.Possession == 0) Ctrl = off[0];
        }

        void Give(P p)
        {
            var b = Ball;
            b.Mode = BallMode.Held; b.Holder = p; b.Target = null; b.Shooter = null;
            if (p.Side == 0 && p != Ctrl) { Ctrl = p; Meter = -1; }
        }

        public P Holder() => Ball.Mode == BallMode.Held ? Ball.Holder : null;

        // ── actions ──

        void StartShot(P p)
        {
            if (M.MustClear && p.Side == M.Possession)
            {
                if (p.Side == 0) Say("先運到三分線外清球！", "#ff9a3c", 1.0);
                return;
            }
            p.Windup = 0.42;
        }

        static double FD(P a, P b) => Court.FloorDist(a.X, a.Y, b.X, b.Y);
        static double FD(P a, Ball b) => Court.FloorDist(a.X, a.Y, b.X, b.Y);

        void Release(P p, double timing)
        {
            var b = Ball;
            if (b.Holder != p) return;
            bool three = Court.IsThree(p.X, p.Y);
            double dist = Court.HoopDist(p.X, p.Y);
            int skill = three ? p.B.Three : p.B.Shoot;
            double contest = 0;
            P blocker = null;
            foreach (var d in Team(1 - p.Side))
            {
                double fd = FD(d, p);
                contest = Math.Max(contest, Js.Clamp(1 - fd / 90, 0, 1) * (d.Z > 8 ? 1.25 : 1));
                if (d.Z > 12 && fd < Data.BLOCK_RANGE && blocker == null) blocker = d;
            }
            p.Z = 0; p.Vz = 260 + p.B.Jump * 12; // jump shot
            b.Mode = BallMode.Shot; b.Holder = null; b.Shooter = p; b.Three = three;
            b.FromX = p.X; b.FromY = p.Y; b.FromZ = 70;
            b.X = p.X; b.Y = p.Y; b.Z = 70; b.T = 0;
            b.Dur = 0.55 + dist / 900;
            b.Blocked = false;
            if (p.Side == 0) contest *= Data.USER_CONTEST_SCALE;
            double blockP = blocker == null ? 0 : p.Side == 0
                ? Data.USER_BLOCK_BASE + blocker.B.Jump * Data.USER_BLOCK_PER_JUMP
                : 0.3 + blocker.B.Jump * 0.04;
            if (blocker != null && rng() < blockP)
            {
                b.Blocked = true;
                b.Dur = 0.18;
            }
            b.Make = !b.Blocked && rng() < Match.ShotChance(dist, three, skill, Js.Clamp(timing, 0, 1), Js.Clamp(contest, 0, 1));
            Sound?.Invoke("shoot");
        }

        void Pass(P from, P to)
        {
            var b = Ball;
            if (b.Holder != from || from == to) return;
            b.Mode = BallMode.Pass; b.Holder = null; b.Target = to;
            b.FromX = from.X; b.FromY = from.Y; b.FromZ = 55;
            b.X = from.X; b.Y = from.Y; b.Z = 55; b.T = 0;
            b.Dur = Math.Max(0.22, FD(from, to) / Data.PASS_SPEED);
            Sound?.Invoke("pass");
        }

        void TrySteal(P p)
        {
            var h = Holder();
            if (h == null || h.Side == p.Side || p.Stun > 0) return;
            if (FD(p, h) > Data.STEAL_RANGE) { p.Stun = 0.25; return; }
            if (rng() < 0.22 + (p.B.Defense - 5) * 0.045 - (h.B.Speed - 5) * 0.02)
            {
                M.Gain(p.Side);
                Give(p);
                h.Stun = 0.6;
                Sound?.Invoke("steal");
                Say($"{p.B.Name} 抄截！", "#5cffb0");
            }
            else p.Stun = 0.55;
        }

        void Jump(P p) { if (p.Z <= 0 && p.Stun <= 0) p.Vz = 300 + p.B.Jump * 22; }

        /// <summary>Best pass target for p: teammate most in the stick direction, or the most open.</summary>
        P PassTarget(P p, double dx, double dy)
        {
            var mates = Team(p.Side).Where(q => q != p).ToList();
            P best = mates[0];
            double bestScore = double.NegativeInfinity;
            foreach (var q in mates)
            {
                double sc;
                if (dx != 0 || dy != 0)
                {
                    double ax = q.X - p.X, ay = (q.Y - p.Y) * 2;
                    sc = (ax * dx + ay * dy) / (Js.Hypot(ax, ay) + 1);
                }
                else sc = Openness(q);
                if (sc > bestScore) { bestScore = sc; best = q; }
            }
            return best;
        }

        double Openness(P q)
        {
            double m = 999;
            foreach (var d in Team(1 - q.Side)) m = Math.Min(m, FD(d, q));
            return m;
        }

        // ── update ──

        /// <summary>Advance one frame. Returns false once the match is over.</summary>
        public bool Update(double dt, Controls k)
        {
            T += dt;
            dt = Math.Min(dt, 1.0 / 30);
            if (MsgT > 0) MsgT -= dt;
            if (M.Over) return false;
            if (Pause > 0)
            {
                Pause -= dt;
                Physics2(dt);
                return true;
            }
            bool live = Ball.Mode != BallMode.Dead;
            if (M.Tick(dt, live))
            {
                Sound?.Invoke("whistle");
                CheckBall("12 秒進攻違例！球權轉換");
                return true;
            }
            UserInput(dt, k);
            aiTick -= dt;
            bool think = aiTick <= 0;
            if (think) aiTick = 0.12;
            foreach (var p in Ps) if (p != Ctrl) Ai(p, dt, think);
            Physics2(dt);
            UpdateBall(dt);
            // Clear-the-ball rule.
            var h = Holder();
            if (h != null && M.MustClear && h.Side == M.Possession && Court.IsThree(h.X, h.Y))
            {
                M.Cleared();
                if (h.Side == 0) Say("清球完成，可以進攻！", "#9fd2ff", 0.9);
            }
            return !M.Over;
        }

        void UserInput(double dt, Controls k)
        {
            var c = Ctrl;
            if (c.Stun > 0) { c.Stun -= dt; c.Moving = false; return; }
            int dx = k.Dx, dy = k.Dy;
            bool hasBall = Ball.Holder == c && Ball.Mode == BallMode.Held;
            if (hasBall)
            {
                if (k.ShootPressed && Meter < 0 && c.Z <= 0)
                {
                    if (M.MustClear) Say("先運到三分線外清球！", "#ff9a3c", 1.0);
                    else { Meter = 0; defJumpAt = rng() < Data.CPU_CONTEST_RATE ? 0.45 + rng() * 0.35 : 9; }
                }
                if (Meter >= 0)
                {
                    dx = 0; dy = 0;
                    Meter += dt / Data.METER_TIME;
                    if (!k.ShootHeld || Meter >= 1.15)
                    {
                        double q = Match.MeterQuality(Meter, Data.METER_SWEET);
                        if (q >= 0.99) Say("完美出手！", "#5cffb0", 0.7);
                        Release(c, q);
                        Meter = -1;
                    }
                }
                else if (k.PassPressed) Pass(c, PassTarget(c, dx, dy));
            }
            else
            {
                Meter = -1;
                bool def = M.Possession == 1 || (Ball.Mode != BallMode.Held && Ball.Shooter != null && Ball.Shooter.Side == 1);
                if (k.ShootPressed) Jump(c);
                if (k.PassPressed && def) TrySteal(c);
                if (k.PassPressed && !def && Ball.Mode == BallMode.Held && Ball.Holder != null && Ball.Holder.Side == 0)
                    Pass(Ball.Holder, c); // Call for the ball.
                if (k.SwitchPressed) SwitchCtrl();
            }
            double sp = Data.RUN_SPEED * (0.7 + c.B.Speed * 0.06) * (k.Sprint ? 1.3 : 1) * (hasBall ? 0.92 : 1);
            if (dx != 0 || dy != 0)
            {
                double n = Js.Hypot(dx, dy);
                c.X += dx / n * sp * dt;
                c.Y += dy / n * sp * Data.DEPTH_FACTOR * dt;
                if (dx != 0) c.Facing = dx > 0 ? 1 : -1;
            }
            c.Moving = dx != 0 || dy != 0;
            Court.ClampToCourt(ref c.X, ref c.Y);
        }

        void SwitchCtrl()
        {
            double tx, ty;
            if (Ball.Mode == BallMode.Held && Ball.Holder != null) { tx = Ball.Holder.X; ty = Ball.Holder.Y; }
            else { tx = Ball.X; ty = Ball.Y; }
            P best = Ctrl;
            double bd = double.PositiveInfinity;
            foreach (var p in Team(0))
            {
                if (p == Ctrl) continue;
                double d = Court.FloorDist(p.X, p.Y, tx, ty);
                if (d < bd) { bd = d; best = p; }
            }
            Ctrl = best;
        }

        void MoveToward(P p, double tx, double ty, double dt, double speedMul = 1)
        {
            double dx = tx - p.X, dy = (ty - p.Y) / Data.DEPTH_FACTOR;
            double d = Js.Hypot(dx, dy);
            p.Moving = d > 6;
            if (d < 3) return;
            double sp = Data.RUN_SPEED * (0.7 + p.B.Speed * 0.06) * speedMul * dt;
            double k = Math.Min(1, sp / d);
            p.X += dx * k;
            p.Y += dy * k * Data.DEPTH_FACTOR;
            if (Math.Abs(dx) > 4) p.Facing = dx > 0 ? 1 : -1;
            Court.ClampToCourt(ref p.X, ref p.Y);
        }

        void Ai(P p, double dt, bool think)
        {
            if (p.Stun > 0) { p.Stun -= dt; p.Moving = false; return; }
            var b = Ball;
            bool offense = p.Side == M.Possession;
            // Loose ball: nearest two of each side chase it.
            if (b.Mode == BallMode.Loose)
            {
                var mine = Js.Sorted(Team(p.Side), (a, c) => FD(a, b).CompareTo(FD(c, b)));
                if (mine.IndexOf(p) < 2) { MoveToward(p, b.X, b.Y, dt, 1.1); return; }
            }
            if (p.Windup > 0)
            {
                p.Windup -= dt;
                p.Moving = false;
                if (p.Windup <= 0) Release(p, 0.55 + rng() * 0.4);
                return;
            }
            if (offense && b.Mode == BallMode.Held && b.Holder == p)
            {
                // Ball handler.
                if (M.MustClear) { MoveToward(p, p.X < 512 ? 330 : 694, 560, dt); return; }
                if (think)
                {
                    double open = Openness(p);
                    double dist = Court.HoopDist(p.X, p.Y);
                    bool three = Court.IsThree(p.X, p.Y);
                    int skill = three ? p.B.Three : p.B.Shoot;
                    bool want = (dist < 70 && open > 25) || (open > 70 && (three ? skill >= 6 : dist < 260)) || M.ShotClock < 2.2;
                    if (want && rng() < 0.55) { StartShot(p); return; }
                    if (open < 40 && rng() < 0.28) { Pass(p, PassTarget(p, 0, 0)); return; }
                    if (rng() < 0.06) { Pass(p, PassTarget(p, 0, 0)); return; }
                    // Drive: aim at the rim with a lateral wobble.
                    p.SpotX = Court.HOOP_X + (rng() - 0.5) * 200;
                    p.SpotY = Court.HOOP_Y + 30 + rng() * 60;
                }
                MoveToward(p, p.SpotX, p.SpotY, dt, 0.9);
                return;
            }
            if (offense)
            {
                // Off-ball: rotate between spots.
                p.Retarget -= dt;
                if (p.Retarget <= 0)
                {
                    p.Retarget = 1.5 + rng() * 2;
                    var s = OFF_SPOTS[(int)Math.Floor(rng() * OFF_SPOTS.Length)];
                    p.SpotX = s.x + (rng() - 0.5) * 40;
                    p.SpotY = s.y + (rng() - 0.5) * 20;
                }
                MoveToward(p, p.SpotX, p.SpotY, dt, 0.85);
                return;
            }
            // Defense: guard the same-index attacker, between him and the rim.
            var opp = Team(1 - p.Side)[p.Idx];
            double tx = Js.Lerp(opp.X, Court.HOOP_X, 0.2), ty = Js.Lerp(opp.Y, Court.HOOP_Y, 0.2);
            MoveToward(p, tx, ty, dt, 1.0);
            var h = Holder();
            if (h == opp && think)
            {
                if (h.Side == 0 && Meter >= 0)
                {
                    // One timed jump per user shot (may be early or late), not a jump every tick.
                    if (Meter >= defJumpAt && FD(p, h) < Data.BLOCK_RANGE + 10) { Jump(p); defJumpAt = 9; }
                }
                else if (h.Windup > 0) { if (FD(p, h) < Data.BLOCK_RANGE + 10 && rng() < 0.5) Jump(p); }
                else if (FD(p, h) < Data.STEAL_RANGE && rng() < 0.05 + p.B.Defense * 0.006) TrySteal(p);
            }
            if (b.Mode == BallMode.Shot && b.Shooter == opp && b.T < 0.15 && FD(p, opp) < Data.BLOCK_RANGE && think) Jump(p);
        }

        /// <summary>Vertical (jump) physics for players.</summary>
        void Physics2(double dt)
        {
            foreach (var p in Ps)
            {
                if (p.Z > 0 || p.Vz > 0)
                {
                    p.Vz -= GRAV * dt;
                    p.Z += p.Vz * dt;
                    if (p.Z <= 0) { p.Z = 0; p.Vz = 0; }
                }
            }
        }

        void UpdateBall(double dt)
        {
            var b = Ball;
            switch (b.Mode)
            {
                case BallMode.Held:
                    {
                        var h = b.Holder;
                        b.X = h.X + h.Facing * 16; b.Y = h.Y + 2;
                        b.Z = h.Z + (h.Windup > 0 || Meter >= 0 ? 70 : Math.Abs(Math.Sin(T * 8)) * 40 + 8);
                        if (h.Moving && Math.Abs(Math.Sin(T * 8)) < 0.12 && Math.Abs(Math.Sin((T - dt) * 8)) >= 0.12) Sound?.Invoke("bounce");
                        break;
                    }
                case BallMode.Pass:
                    {
                        var to = b.Target;
                        b.T += dt / b.Dur;
                        double k = Math.Min(1, b.T);
                        b.X = Js.Lerp(b.FromX, to.X, k); b.Y = Js.Lerp(b.FromY, to.Y, k); b.Z = Js.Lerp(b.FromZ, 55, k) + Math.Sin(k * Math.PI) * 25;
                        // Interception by defenders near the lane.
                        foreach (var d in Team(1 - to.Side))
                        {
                            if (d.Stun <= 0 && FD(d, b) < 20 && k > 0.2 && k < 0.85 && rng() < 0.08 + d.B.Defense * 0.01)
                            {
                                M.Gain(d.Side);
                                Give(d);
                                Sound?.Invoke("steal");
                                Say($"{d.B.Name} 抄截！", "#5cffb0");
                                return;
                            }
                        }
                        if (b.T >= 1) Give(to);
                        break;
                    }
                case BallMode.Shot:
                    {
                        b.T += dt / b.Dur;
                        double k = Math.Min(1, b.T);
                        if (b.Blocked)
                        {
                            if (k >= 1)
                            {
                                Sound?.Invoke("block");
                                Say("大蓋火鍋！", "#ff5fa2");
                                Shake?.Invoke(0.25f);
                                b.Mode = BallMode.Loose; b.Vx = (rng() - 0.5) * 420; b.Vy = 120 + rng() * 120; b.Vz = 120;
                            }
                            else { b.X = b.FromX; b.Y = b.FromY; b.Z = b.FromZ + k * 40; }
                            break;
                        }
                        b.X = Js.Lerp(b.FromX, Court.HOOP_X, k); b.Y = Js.Lerp(b.FromY, Court.HOOP_Y, k);
                        b.Z = Js.Lerp(b.FromZ, Court.RIM_HEIGHT, k) + Math.Sin(k * Math.PI) * (90 + Court.HoopDist(b.FromX, b.FromY) * 0.25);
                        if (k >= 1)
                        {
                            var sh = b.Shooter;
                            if (b.Make)
                            {
                                int pts = M.Made(sh.Side, b.Three);
                                Sound?.Invoke("swish");
                                Swish?.Invoke(sh.Side);
                                b.Mode = BallMode.Dead;
                                if (!M.Over) CheckBall($"{sh.B.Name} {(pts == 2 ? "兩分球！" : "進球！")}（{pts} 分）");
                                Say($"{sh.B.Name} {(pts == 2 ? "外線兩分！" : "得分！")} +{pts}", sh.Side == 0 ? "#5cc8ff" : "#ff6b6b", 1.3);
                            }
                            else
                            {
                                Sound?.Invoke("rim");
                                b.Mode = BallMode.Loose;
                                double a = rng() * Math.PI;
                                b.Vx = Math.Cos(a) * 230; b.Vy = Math.Sin(a) * 160 + 40; b.Vz = 180 + rng() * 120;
                            }
                        }
                        break;
                    }
                case BallMode.Loose:
                    {
                        b.Vz -= GRAV * dt;
                        b.X += b.Vx * dt; b.Y += b.Vy * dt * Data.DEPTH_FACTOR; b.Z += b.Vz * dt;
                        if (b.Z <= 0) { b.Z = 0; if (Math.Abs(b.Vz) > 60) Sound?.Invoke("bounce"); b.Vz = -b.Vz * 0.55; b.Vx *= 0.8; b.Vy *= 0.8; }
                        if (b.Y < Court.TOP_Y) { b.Y = Court.TOP_Y; b.Vy = Math.Abs(b.Vy); }
                        if (b.Y > Court.BOTTOM_Y) { b.Y = Court.BOTTOM_Y; b.Vy = -Math.Abs(b.Vy); }
                        double hw = Court.HalfWidth(b.Y) - 10;
                        if (b.X < 512 - hw) { b.X = 512 - hw; b.Vx = Math.Abs(b.Vx); }
                        if (b.X > 512 + hw) { b.X = 512 + hw; b.Vx = -Math.Abs(b.Vx); }
                        foreach (var p in Ps)
                        {
                            if (p.Stun > 0) continue;
                            if (FD(p, b) < 26 && b.Z < 60 + p.Z)
                            {
                                int shooterSide = b.Shooter != null ? b.Shooter.Side : M.Possession;
                                if (p.Side == shooterSide && p.Side == M.Possession) { M.OffensiveRebound(); Say($"{p.B.Name} 進攻籃板！", "#9fd2ff", 0.9); }
                                else { M.Gain(p.Side); Say($"{p.B.Name} 搶到籃板！", "#9fd2ff", 0.9); }
                                Give(p);
                                if (p.Side == 0) Ctrl = p;
                                break;
                            }
                        }
                        break;
                    }
            }
        }
    }
}
