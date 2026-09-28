// 萌友戰機 2026～2027 — shot types, enemies, waves and bullet maths ported 1:1 from
// Game11/cloud/src/rules.ts, plus the sortie simulation from scenes/play.ts without drawing.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MoeGames.Game11
{
    public enum ShotType { Spread, Laser, Homing, Vulcan, Options, Orbit, Flame, Backfire }
    public enum BulletKind { Shot, Laser, Missile, Flame, Bubble }
    public enum EnemyKind { Drone, Fighter, Bomber, Midboss, Boss1, Boss2 }
    public enum PathKind { Down, Sine, SwoopL, SwoopR, Hover, SideL, SideR }

    public class Pilot { public string Id, Name, Title, Color, ShotName, Desc; public ShotType Shot; }

    public class ShotSpec
    {
        public double Dx, Dy = -20, Ang, Speed = 820, Dmg = 1, R = 5, Life = 1.2;
        public bool Pierce, Homing;
        public BulletKind Kind = BulletKind.Shot;
        public string Color = "#fff27a";
    }

    public class EnemyInfo { public double Hp, R, Size; public int Score; }

    public class Wave { public double T; public EnemyKind Kind; public PathKind Path; public double X; public int Count; public double Gap; }

    public class StageInfo { public string Year, Name, Bg; public double BulletSpeed, FireMul; public EnemyKind Boss; }

    public static class Rules
    {
        public const double PF_W = 540, PF_H = 720, SPEED = 330, FOCUS_SPEED = 165, HITBOX = 4, FIRE_RATE = 0.09, MISSILE_RATE = 0.5;
        public const int MAX_POWER = 4, START_LIVES = 3, START_BOMBS = 3, MAX_BOMBS = 6;
        public const double INVULN = 3, BOMB_TIME = 2.5, BOMB_DMG = 40, COLLECT_LINE = 190;
        public static readonly int[] EXTENDS = { 60000, 180000 };

        public static readonly Pilot[] PILOTS =
        {
            new Pilot { Id = "whale", Name = "汐音", Title = "鯨魚女僕", Color = "#4f8dff", Shot = ShotType.Spread, ShotName = "泡泡散射", Desc = "廣角扇形彈幕，清雜兵最強" },
            new Pilot { Id = "penguin", Name = "小冰", Title = "企鵝少女", Color = "#9adfff", Shot = ShotType.Laser, ShotName = "冰晶雷射", Desc = "貫穿敵機的直線雷射" },
            new Pilot { Id = "glasses", Name = "光哉", Title = "眼鏡學長", Color = "#d8b98a", Shot = ShotType.Homing, ShotName = "追蹤飛彈", Desc = "主砲＋自動追蹤飛彈" },
            new Pilot { Id = "tshirt", Name = "阿翔", Title = "T恤少年", Color = "#b0b0b0", Shot = ShotType.Vulcan, ShotName = "重火力機槍", Desc = "集中前方的高傷害機槍" },
            new Pilot { Id = "calico", Name = "小花", Title = "夾克三花貓", Color = "#f0a24a", Shot = ShotType.Options, ShotName = "貓爪僚機", Desc = "兩架僚機從側面支援" },
            new Pilot { Id = "whitecat", Name = "書白", Title = "圖書館貓", Color = "#f4efe6", Shot = ShotType.Orbit, ShotName = "書頁結界", Desc = "環繞書頁會擋下子彈" },
            new Pilot { Id = "redcat", Name = "緋音", Title = "紅髮貓耳少女", Color = "#e8413c", Shot = ShotType.Flame, ShotName = "火焰噴射", Desc = "近距離超高傷害火焰" },
            new Pilot { Id = "sailor", Name = "澪", Title = "水手服少女", Color = "#7fb3e6", Shot = ShotType.Backfire, ShotName = "前後夾擊", Desc = "前方多向＋後方射擊" },
        };

        static ShotSpec S(double dx, double ang, Action<ShotSpec> o = null)
        {
            var s = new ShotSpec { Dx = dx, Ang = ang };
            o?.Invoke(s);
            return s;
        }

        /// <summary>The main volley a pilot fires every FIRE_RATE seconds at power 1..4 (degrees, 0 = up).</summary>
        public static List<ShotSpec> Volley(ShotType type, int level)
        {
            int L = Math.Max(1, Math.Min(MAX_POWER, level));
            var o = new List<ShotSpec>();
            switch (type)
            {
                case ShotType.Spread:
                    {
                        int n = 2 * L + 1;
                        double span = 10 + 8 * L;
                        for (int i = 0; i < n; i++) o.Add(S(0, -span / 2 + span * i / (n - 1), s => { s.Speed = 640; s.Dmg = 0.75; s.R = 6; s.Kind = BulletKind.Bubble; s.Color = "#7fd8ff"; }));
                        break;
                    }
                case ShotType.Laser:
                    for (int i = 0; i < L; i++) o.Add(S((i - (L - 1) / 2.0) * 11, 0, s => { s.Speed = 1150; s.Dmg = 1.6; s.R = 5; s.Pierce = true; s.Kind = BulletKind.Laser; s.Color = "#cff6ff"; }));
                    break;
                case ShotType.Homing:
                    o.Add(S(-8, 0)); o.Add(S(8, 0));
                    if (L >= 2) o.Add(S(0, 0));
                    if (L >= 3) { o.Add(S(-18, -4)); o.Add(S(18, 4)); }
                    break;
                case ShotType.Vulcan:
                    for (int i = 0; i < L + 2; i++) o.Add(S((i - (L + 1) / 2.0) * 7, (i - (L + 1) / 2.0) * 0.8, s => { s.Speed = 960; s.Dmg = 0.9; s.Color = "#ffe066"; }));
                    break;
                case ShotType.Options:
                    o.Add(S(-7, 0)); o.Add(S(7, 0));
                    if (L >= 3) o.Add(S(0, 0));
                    foreach (int side in new[] { -1, 1 })
                    {
                        void Opt(ShotSpec s) { s.Dy = 6; s.Dmg = 0.8; s.Color = "#ffb347"; }
                        o.Add(S(side * 42, side * 4, Opt));
                        if (L >= 2) o.Add(S(side * 42, side * 14, Opt));
                        if (L >= 4) o.Add(S(side * 42, -side * 2, Opt));
                    }
                    break;
                case ShotType.Orbit:
                    for (int i = 0; i <= L; i++) o.Add(S((i - L / 2.0) * 9, 0, s => { s.Dmg = 1.1; s.Color = "#f2e2b6"; }));
                    break;
                case ShotType.Flame:
                    {
                        int n = 4 + 2 * L;
                        double span = 22 + 6 * L;
                        for (int i = 0; i < n; i++) o.Add(S(0, -span / 2 + span * i / (n - 1), s => { s.Speed = 540; s.Dmg = 0.6; s.R = 9; s.Life = 0.36; s.Kind = BulletKind.Flame; s.Color = "#ff7a3a"; }));
                        break;
                    }
                case ShotType.Backfire:
                    {
                        double[] fwd = L >= 3 ? new double[] { -16, -8, 0, 8, 16 } : L >= 2 ? new double[] { -10, 0, 10 } : new double[] { 0 };
                        foreach (var a in fwd) o.Add(S(0, a, s => s.Color = "#c8f0ff"));
                        void Back(ShotSpec s) { s.Dy = 20; s.Dmg = 0.9; s.Color = "#c8f0ff"; }
                        o.Add(S(0, 180, Back));
                        if (L >= 4) { o.Add(S(0, 165, Back)); o.Add(S(0, 195, Back)); }
                        break;
                    }
            }
            return o;
        }

        /// <summary>Homing missiles (glasses only), every MISSILE_RATE seconds.</summary>
        public static List<ShotSpec> Missiles(ShotType type, int level)
        {
            if (type != ShotType.Homing) return new List<ShotSpec>();
            return Enumerable.Range(0, level).Select(i => S((i % 2 == 1 ? 1 : -1) * (14 + i * 6), (i % 2 == 1 ? 1 : -1) * 30,
                s => { s.Speed = 460; s.Dmg = 2.2; s.R = 6; s.Homing = true; s.Life = 2.2; s.Kind = BulletKind.Missile; s.Color = "#ff9a5a"; })).ToList();
        }

        /// <summary>Whitecat's orbiting pages: count by level.</summary>
        public static int Orbiters(ShotType type, int level) => type == ShotType.Orbit ? level + 1 : 0;

        /// <summary>Rough damage per second of a pilot's main gun at a level.</summary>
        public static double Dps(ShotType type, int level)
            => Volley(type, level).Sum(b => b.Dmg) / FIRE_RATE + Missiles(type, level).Sum(b => b.Dmg) / MISSILE_RATE;

        public static readonly Dictionary<EnemyKind, EnemyInfo> ENEMY = new Dictionary<EnemyKind, EnemyInfo>
        {
            [EnemyKind.Drone] = new EnemyInfo { Hp = 3, R = 18, Score = 100, Size = 46 },
            [EnemyKind.Fighter] = new EnemyInfo { Hp = 10, R = 22, Score = 300, Size = 64 },
            [EnemyKind.Bomber] = new EnemyInfo { Hp = 70, R = 44, Score = 2500, Size = 130 },
            [EnemyKind.Midboss] = new EnemyInfo { Hp = 420, R = 62, Score = 12000, Size = 190 },
            [EnemyKind.Boss1] = new EnemyInfo { Hp = 1500, R = 88, Score = 50000, Size = 330 },
            [EnemyKind.Boss2] = new EnemyInfo { Hp = 1900, R = 100, Score = 80000, Size = 300 },
        };

        /// <summary>Position of a path-following enemy age seconds after spawning at column x0 (0..1).</summary>
        public static (double x, double y) PathPos(PathKind path, double x0, double age, int idx)
        {
            double X = x0 * PF_W;
            switch (path)
            {
                case PathKind.Down: return (X + Math.Sin(age * 2 + idx) * 12, -40 + age * 150);
                case PathKind.Sine: return (X + Math.Sin(age * 2.2) * 110, -40 + age * 130);
                case PathKind.SwoopL: return (X + age * 150, -40 + 270 * age - 62 * age * age);
                case PathKind.SwoopR: return (X - age * 150, -40 + 270 * age - 62 * age * age);
                case PathKind.Hover:
                    {
                        double y = age < 1.4 ? -40 + age * 150 : age < 5 ? 170 + Math.Sin(age * 2) * 8 : 170 + (age - 5) * 220;
                        return (X + Math.Sin(age) * 30, y);
                    }
                case PathKind.SideL: return (-40 + age * 190, 80 + x0 * 200 + age * 35);
                default: return (PF_W + 40 - age * 190, 80 + x0 * 200 + age * 35);
            }
        }

        public static readonly StageInfo[] STAGES =
        {
            new StageInfo { Year = "2026", Name = "蒼藍海洋航線", Bg = "ocean", BulletSpeed = 1, FireMul = 1, Boss = EnemyKind.Boss1 },
            new StageInfo { Year = "2027", Name = "銀河星雲決戰", Bg = "space", BulletSpeed = 1.18, FireMul = 1.25, Boss = EnemyKind.Boss2 },
        };

        public const double MIDBOSS_T = 38, BOSS_T = 80;

        /// <summary>Deterministic wave schedule for a stage (0 or 1).</summary>
        public static List<Wave> StageWaves(int stage)
        {
            var seq = new (EnemyKind k, PathKind p, double x, int c, double g)[]
            {
                (EnemyKind.Drone, PathKind.Sine, 0.3, 5, 0.32), (EnemyKind.Drone, PathKind.Sine, 0.7, 5, 0.32), (EnemyKind.Fighter, PathKind.SwoopL, 0.12, 4, 0.38), (EnemyKind.Fighter, PathKind.SwoopR, 0.88, 4, 0.38),
                (EnemyKind.Drone, PathKind.SideL, 0.2, 6, 0.28), (EnemyKind.Fighter, PathKind.Hover, 0.3, 1, 0), (EnemyKind.Fighter, PathKind.Hover, 0.7, 1, 0), (EnemyKind.Bomber, PathKind.Down, 0.5, 1, 0),
                (EnemyKind.Drone, PathKind.SideR, 0.4, 6, 0.28), (EnemyKind.Drone, PathKind.Down, 0.2, 4, 0.3), (EnemyKind.Drone, PathKind.Down, 0.8, 4, 0.3), (EnemyKind.Fighter, PathKind.SwoopL, 0.2, 5, 0.3),
            };
            double step = stage == 0 ? 2.8 : 2.2;
            var o = new List<Wave>();
            int i = stage * 3;
            Wave W((EnemyKind k, PathKind p, double x, int c, double g) s, double t) => new Wave { T = t, Kind = s.k, Path = s.p, X = s.x, Count = s.c, Gap = s.g };
            for (double t = 2; t < MIDBOSS_T - 3; t += step) o.Add(W(seq[i++ % seq.Length], t));
            o.Add(new Wave { T = MIDBOSS_T, Kind = EnemyKind.Midboss, Path = PathKind.Hover, X = 0.5, Count = 1 });
            for (double t = MIDBOSS_T + 14; t < BOSS_T - 4; t += step) o.Add(W(seq[i++ % seq.Length], t));
            if (stage == 1)
            {
                o.Add(new Wave { T = 60, Kind = EnemyKind.Bomber, Path = PathKind.Down, X = 0.25, Count = 1 });
                o.Add(new Wave { T = 60, Kind = EnemyKind.Bomber, Path = PathKind.Down, X = 0.75, Count = 1 });
            }
            o.Add(new Wave { T = BOSS_T, Kind = STAGES[stage].Boss, Path = PathKind.Hover, X = 0.5, Count = 1 });
            return Js.Sorted(o, (a, b) => a.T.CompareTo(b.T));
        }

        /// <summary>Evenly spaced ring of n angles (radians) starting at start.</summary>
        public static double[] Ring(int n, double start = 0) => Enumerable.Range(0, n).Select(i => start + i * Math.PI * 2 / n).ToArray();
        /// <summary>Fan of n angles centred on mid spanning span radians.</summary>
        public static double[] Fan(int n, double mid, double span) => n == 1 ? new[] { mid } : Enumerable.Range(0, n).Select(i => mid - span / 2 + span * i / (n - 1)).ToArray();
        public static double AimAt(double fx, double fy, double tx, double ty) => Math.Atan2(ty - fy, tx - fx);
        public static bool Hit(double ax, double ay, double ar, double bx, double by, double br) { double dx = ax - bx, dy = ay - by, r = ar + br; return dx * dx + dy * dy < r * r; }
        /// <summary>Boss phase from remaining HP fraction: 0 (&gt;66%), 1 (33–66%), 2 (&lt;33%).</summary>
        public static int BossPhase(double hp, double max) { double f = hp / max; return f > 0.66 ? 0 : f > 0.33 ? 1 : 2; }
        /// <summary>Lives earned by passing extend thresholds between two scores.</summary>
        public static int ExtendsBetween(int before, int after) => EXTENDS.Count(e => before < e && after >= e);
    }

    // ── sortie simulation (scenes/play.ts) ──

    public class PB { public double X, Y, Vx, Vy, Dmg, R, Life; public bool Pierce, Homing; public BulletKind Kind; public string Color; public HashSet<Enemy> Hits; }
    public class EB { public double X, Y, Vx, Vy, R; public string Color; }
    public class Enemy { public EnemyKind Kind; public PathKind Path; public double X0; public int Idx; public double X, Y, Hp, Max, R, Age, Fire, Flash, Spin; public bool Boss; public int T2; }
    public class Item { public double X, Y, Vy, T; public char Kind; }

    public enum Phase { Intro, Play, Warning, Clear, Over, Win }

    public struct ShipInput { public int Dx, Dy; public bool Slow, Bomb; public bool Drag; public double DragX, DragY; }

    public class Sortie
    {
        public readonly Pilot Pilot;
        public double Px = Rules.PF_W / 2, Py = Rules.PF_H - 100;
        public int Lives = Rules.START_LIVES, Bombs = Rules.START_BOMBS, Power = 1, Score;
        public double Invuln = 2, Dead, BombT, FireT, MissileT;
        public List<PB> Pbs = new List<PB>();
        public List<EB> Ebs = new List<EB>();
        public List<Enemy> Enemies = new List<Enemy>();
        public List<Item> Items = new List<Item>();
        List<Wave> waves = new List<Wave>();
        readonly List<(double at, Wave w, int idx)> spawnQ = new List<(double, Wave, int)>();
        public int Stage;
        public double St;
        public Phase Phase = Phase.Intro;
        public double Timer = 2.8;
        public int Kills;
        public Enemy Boss;
        public double T;
        public int Bonus;
        public bool Focus;
        public bool Won;
        public bool God;
        readonly Func<double> rng;

        public event Action<string> Sound;                        // shot boom big pop item power bomb die warn extend
        public event Action<double, double, bool> Explosion;      // x, y, big
        public event Action<float> Shake;

        public Sortie(string pilot, Func<double> rng = null)
        {
            this.rng = rng ?? Rand.Default;
            Pilot = Rules.PILOTS.FirstOrDefault(p => p.Id == pilot) ?? Rules.PILOTS[0];
            StartStage(0);
        }

        void StartStage(int s)
        {
            Stage = s; St = 0; waves = Rules.StageWaves(s); spawnQ.Clear();
            Phase = Phase.Intro; Timer = 2.8; Boss = null;
            Ebs = new List<EB>(); Enemies = new List<Enemy>();
            Px = Rules.PF_W / 2; Py = Rules.PF_H - 110;
        }

        void Spawn(Wave w, int idx)
        {
            var e = Rules.ENEMY[w.Kind];
            bool boss = w.Kind == EnemyKind.Boss1 || w.Kind == EnemyKind.Boss2 || w.Kind == EnemyKind.Midboss;
            double x = w.Path == PathKind.Down && w.Count > 1 ? Js.Clamp(w.X + (idx - (w.Count - 1) / 2.0) * 0.14, 0.08, 0.92) : w.X;
            var en = new Enemy { Kind = w.Kind, Path = w.Path, X0 = x, Idx = idx, X = x * Rules.PF_W, Y = -60, Hp = e.Hp * (1 + Stage * 0.25), Max = e.Hp * (1 + Stage * 0.25), R = e.R, Fire = 1 + rng(), Boss = boss };
            Enemies.Add(en);
            if (w.Kind == EnemyKind.Boss1 || w.Kind == EnemyKind.Boss2) Boss = en;
        }

        void EShoot(double x, double y, double ang, double speed, string color = "#ff5ad0", double r = 6)
        {
            if (Ebs.Count > 700) return;
            double sp = speed * Rules.STAGES[Stage].BulletSpeed;
            Ebs.Add(new EB { X = x, Y = y, Vx = Math.Cos(ang) * sp, Vy = Math.Sin(ang) * sp, R = r, Color = color });
        }

        /// <summary>Advance one frame. Returns true when the sortie is over (game over or all clear).</summary>
        public bool Update(double dt, ShipInput k)
        {
            dt = Math.Min(dt, 1.0 / 30);
            T += dt; Timer -= dt;
            switch (Phase)
            {
                case Phase.Intro: if (Timer <= 0) Phase = Phase.Play; break;
                case Phase.Warning: St += dt; if (Timer <= 0) Phase = Phase.Play; break;
                case Phase.Clear:
                    if (Timer <= 0)
                    {
                        if (Stage + 1 < Rules.STAGES.Length) StartStage(Stage + 1);
                        else { Phase = Phase.Win; Timer = 3; Won = true; }
                    }
                    break;
                case Phase.Over:
                case Phase.Win:
                    if (Timer <= 0) return true;
                    break;
                case Phase.Play: St += dt; Schedule(); break;
            }
            UpdatePlayer(dt, k);
            UpdatePlayerBullets(dt);
            UpdateEnemies(dt);
            UpdateEnemyBullets(dt);
            UpdateItems(dt);
            return false;
        }

        void Schedule()
        {
            while (waves.Count > 0 && waves[0].T <= St)
            {
                var w = waves[0];
                waves.RemoveAt(0);
                if (w.T >= Rules.BOSS_T) { Phase = Phase.Warning; Timer = 2.4; Sound?.Invoke("warn"); }
                for (int i = 0; i < w.Count; i++) spawnQ.Add((St + (w.T >= Rules.BOSS_T ? 2.4 : 0) + i * w.Gap, w, i));
            }
            for (int i = spawnQ.Count - 1; i >= 0; i--)
                if (spawnQ[i].at <= St) { Spawn(spawnQ[i].w, spawnQ[i].idx); spawnQ.RemoveAt(i); }
        }

        public bool Alive => Dead <= 0 && Phase != Phase.Over;

        void UpdatePlayer(double dt, ShipInput k)
        {
            if (Invuln > 0) Invuln -= dt;
            if (BombT > 0) BombT -= dt;
            if (Dead > 0)
            {
                Dead -= dt;
                if (Dead <= 0) { Px = Rules.PF_W / 2; Py = Rules.PF_H - 90; Invuln = Rules.INVULN; }
                return;
            }
            if (Phase == Phase.Over) return;
            Focus = k.Slow;
            double sp = Focus ? Rules.FOCUS_SPEED : Rules.SPEED;
            double dx = k.Dx, dy = k.Dy;
            double n = Js.Hypot(dx, dy);
            if (n == 0) n = 1;
            dx /= n; dy /= n;
            Px += dx * sp * dt; Py += dy * sp * dt;
            // Touch / mouse: drag the ship (it sits a little above the finger).
            if (k.Drag)
            {
                double tx = k.DragX, ty = k.DragY - 70, ddx = tx - Px, ddy = ty - Py, d = Js.Hypot(ddx, ddy);
                if (d > 2) { double m = Math.Min(d, Rules.SPEED * 1.3 * dt); Px += ddx / d * m; Py += ddy / d * m; }
            }
            Px = Js.Clamp(Px, 16, Rules.PF_W - 16); Py = Js.Clamp(Py, 30, Rules.PF_H - 24);
            if (k.Bomb) UseBomb();
            // Auto-fire.
            if (Phase == Phase.Play || Phase == Phase.Warning)
            {
                FireT -= dt; MissileT -= dt;
                if (FireT <= 0)
                {
                    FireT += Rules.FIRE_RATE;
                    foreach (var s in Rules.Volley(Pilot.Shot, Power)) AddShot(s);
                    if (Math.Floor(T / Rules.FIRE_RATE) % 2 == 0) Sound?.Invoke("shot");
                }
                if (MissileT <= 0) { MissileT = Rules.MISSILE_RATE; foreach (var s in Rules.Missiles(Pilot.Shot, Power)) AddShot(s); }
            }
        }

        void AddShot(ShotSpec s)
        {
            double a = s.Ang * Math.PI / 180;
            double sx = Px + s.Dx, sy = Py + (s.Ang > 90 ? -s.Dy : s.Dy);
            Pbs.Add(new PB { X = sx, Y = sy, Vx = Math.Sin(a) * s.Speed, Vy = -Math.Cos(a) * s.Speed, Dmg = s.Dmg, R = s.R, Pierce = s.Pierce, Homing = s.Homing, Life = s.Life, Kind = s.Kind, Color = s.Color, Hits = s.Pierce ? new HashSet<Enemy>() : null });
        }

        void UseBomb()
        {
            if (Bombs <= 0 || BombT > 0 || !Alive) return;
            Bombs--; BombT = Rules.BOMB_TIME; Invuln = Math.Max(Invuln, Rules.BOMB_TIME + 0.3);
            foreach (var b in Ebs) if (rng() < 0.25) Items.Add(new Item { X = b.X, Y = b.Y, Kind = 'M' });
            Ebs = new List<EB>();
            foreach (var e in Enemies.ToList()) if (e.Y > -20) Damage(e, e.Boss ? Rules.BOMB_DMG * 1.5 : Rules.BOMB_DMG);
            Sound?.Invoke("bomb");
            Shake?.Invoke(0.6f);
        }

        void UpdatePlayerBullets(double dt)
        {
            for (int i = Pbs.Count - 1; i >= 0; i--)
            {
                var b = Pbs[i];
                b.Life -= dt;
                if (b.Homing)
                {
                    Enemy best = null;
                    double bd = double.PositiveInfinity;
                    foreach (var e in Enemies) { if (e.Y < -10) continue; double d = Js.Hypot(e.X - b.X, e.Y - b.Y); if (d < bd) { bd = d; best = e; } }
                    if (best != null)
                    {
                        double want = Math.Atan2(best.Y - b.Y, best.X - b.X), cur = Math.Atan2(b.Vy, b.Vx);
                        double diff = want - cur;
                        while (diff > Math.PI) diff -= Math.PI * 2;
                        while (diff < -Math.PI) diff += Math.PI * 2;
                        double na = cur + Js.Clamp(diff, -6 * dt, 6 * dt), sp = Js.Hypot(b.Vx, b.Vy) + 500 * dt;
                        b.Vx = Math.Cos(na) * sp; b.Vy = Math.Sin(na) * sp;
                    }
                }
                b.X += b.Vx * dt; b.Y += b.Vy * dt;
                bool gone = b.Life <= 0 || b.Y < -30 || b.Y > Rules.PF_H + 30 || b.X < -30 || b.X > Rules.PF_W + 30;
                if (!gone)
                    foreach (var e in Enemies.ToList())
                    {
                        if (e.Hp <= 0 || e.Y < -e.R) continue;
                        if (b.Hits != null && b.Hits.Contains(e)) continue;
                        if (Rules.Hit(b.X, b.Y, b.R, e.X, e.Y, e.R))
                        {
                            Damage(e, b.Dmg);
                            if (b.Pierce) b.Hits.Add(e); else { gone = true; break; }
                        }
                    }
                if (gone && i < Pbs.Count) Pbs.RemoveAt(i);
            }
            // Orbiting pages chew through bullets and enemies.
            int n = Rules.Orbiters(Pilot.Shot, Power);
            if (n > 0 && Alive)
                for (int k = 0; k < n; k++)
                {
                    var (ox, oy) = OrbiterPos(k, n);
                    Ebs = Ebs.Where(b => !Rules.Hit(b.X, b.Y, b.R, ox, oy, 11)).ToList();
                    foreach (var e in Enemies.ToList()) if (Rules.Hit(ox, oy, 12, e.X, e.Y, e.R)) Damage(e, 14 * dt);
                }
        }

        public (double x, double y) OrbiterPos(int k, int n)
        {
            double a = T * 3.2 + k * Math.PI * 2 / n;
            return (Px + Math.Cos(a) * 48, Py + Math.Sin(a) * 48);
        }

        void Damage(Enemy e, double amt)
        {
            if (e.Hp <= 0) return;
            e.Hp -= amt; e.Flash = 0.05;
            if (e.Hp <= 0) KillEnemy(e);
        }

        void AddScore(int n)
        {
            int before = Score;
            Score += n;
            int ex = Rules.ExtendsBetween(before, Score);
            if (ex > 0) { Lives += ex; Sound?.Invoke("extend"); }
        }

        void KillEnemy(Enemy e)
        {
            var info = Rules.ENEMY[e.Kind];
            AddScore(info.Score); Kills++;
            bool big = e.Boss || e.Kind == EnemyKind.Bomber;
            Explosion?.Invoke(e.X, e.Y, big);
            Sound?.Invoke(big ? "big" : "boom");
            if (big) Shake?.Invoke(e.Boss ? 0.8f : 0.3f);
            void Drop(char kind, int count = 1) { for (int i = 0; i < count; i++) Items.Add(new Item { X = e.X + (rng() - 0.5) * e.R, Y = e.Y, Vy = -120, Kind = kind }); }
            if (e.Kind == EnemyKind.Fighter && rng() < 0.12) Drop('P');
            if (e.Kind == EnemyKind.Bomber) { Drop('P'); if (rng() < 0.5) Drop('B'); Drop('M', 4); }
            if (e.Kind == EnemyKind.Midboss) { Drop('P', 2); Drop('B'); Drop('M', 10); }
            if (e.Kind == EnemyKind.Drone && rng() < 0.25) Drop('M');
            if (Kills % 30 == 0) Drop('P');
            if (e == Boss)
            {
                Ebs = new List<EB>();
                foreach (var o in Enemies) if (o != e) o.Hp = 0;
                Enemies = new List<Enemy>();
                Bonus = 20000 * (Stage + 1) + Lives * 10000 + Bombs * 5000;
                AddScore(Bonus);
                Phase = Phase.Clear; Timer = 5;
                Boss = null;
            }
        }

        void UpdateEnemies(double dt)
        {
            var st = Rules.STAGES[Stage];
            foreach (var e in Enemies.ToList())
            {
                e.Age += dt; e.Flash -= dt;
                if (e.Boss) MoveBoss(e, dt);
                else { var p = Rules.PathPos(e.Path, e.X0, e.Age, e.Idx); e.X = p.x; e.Y = p.y; }
                if (e.Y < 0 || e.Y > Rules.PF_H - 60 || (!Alive && Dead > 0.8)) continue;
                e.Fire -= dt * st.FireMul;
                if (e.Fire > 0) continue;
                double aim = Rules.AimAt(e.X, e.Y, Px, Py);
                switch (e.Kind)
                {
                    case EnemyKind.Drone: e.Fire = 2.4 + rng() * 2; if (rng() < 0.5 + Stage * 0.2) EShoot(e.X, e.Y, aim, 190); break;
                    case EnemyKind.Fighter: e.Fire = 1.6 + rng(); foreach (var a in Rules.Fan(3, aim, 0.4)) EShoot(e.X, e.Y, a, 220, "#ffb0ff"); break;
                    case EnemyKind.Bomber:
                        e.Fire = 1.5;
                        foreach (var a in Rules.Ring(12, e.Age)) EShoot(e.X, e.Y, a, 150, "#ff7a5a", 7);
                        foreach (var a in Rules.Fan(5, aim, 0.6)) EShoot(e.X, e.Y, a, 230);
                        break;
                    default: BossFire(e, aim); break;
                }
            }
            Enemies = Enemies.Where(e => e.Hp > 0 && e.Y < Rules.PF_H + 120 && e.X > -140 && e.X < Rules.PF_W + 140 && !(e.Age > 3 && e.Y < -80)).ToList();
            if (Alive && Invuln <= 0)
                foreach (var e in Enemies) if (Rules.Hit(Px, Py, Rules.HITBOX + 6, e.X, e.Y, e.R * 0.8)) { PlayerHit(); break; }
        }

        void MoveBoss(Enemy e, double dt)
        {
            double targetY = e.Kind == EnemyKind.Midboss ? 150 : 160;
            if (e.Y < targetY) { e.Y = Math.Min(targetY, e.Y + 90 * dt); e.X = Rules.PF_W / 2; return; }
            if (e.Kind == EnemyKind.Midboss) { e.X = Rules.PF_W / 2 + Math.Sin(e.Age * 0.8) * 150; if (e.Age > 26) e.Y += 120 * dt; }
            else if (e.Kind == EnemyKind.Boss1) e.X = Rules.PF_W / 2 + Math.Sin(e.Age * 0.45) * 120;
            else { e.X = Rules.PF_W / 2 + Math.Sin(e.Age * 0.6) * 130; e.Y = targetY + Math.Sin(e.Age * 1.2) * 30; }
        }

        void BossFire(Enemy e, double aim)
        {
            if (e.Y < 100) { e.Fire = 0.5; return; }
            int ph = Rules.BossPhase(e.Hp, e.Max);
            e.T2 += 1;
            if (e.Kind == EnemyKind.Midboss)
            {
                e.Fire = 1.2;
                foreach (var a in Rules.Ring(16, e.T2 * 0.2)) EShoot(e.X, e.Y, a, 150, "#ff7a5a", 7);
                foreach (var a in Rules.Fan(5, aim, 0.5)) EShoot(e.X, e.Y + 30, a, 240);
                return;
            }
            if (e.Kind == EnemyKind.Boss1)
            {
                if (ph == 0)
                {
                    e.Fire = 0.9;
                    foreach (var a in Rules.Fan(5, aim, 0.55)) EShoot(e.X, e.Y + 60, a, 230);
                    if (e.T2 % 3 == 0) foreach (int side in new[] { -1, 1 }) foreach (var a in Rules.Ring(14, e.T2)) EShoot(e.X + side * 60, e.Y, a, 140, "#ffb03a", 7);
                }
                else if (ph == 1)
                {
                    e.Fire = 0.08; e.Spin += 0.19;
                    foreach (var a in new[] { e.Spin, e.Spin + Math.PI }) EShoot(e.X, e.Y + 20, a, 170, "#ff5ad0");
                    if (e.T2 % 15 == 0) foreach (var a in Rules.Fan(3, aim, 0.3)) EShoot(e.X, e.Y + 60, a, 280, "#ffffff");
                }
                else
                {
                    e.Fire = 0.65;
                    foreach (var a in Rules.Ring(22, e.T2 % 2 * 0.14)) EShoot(e.X, e.Y + 20, a, 160, "#ff4a4a", 7);
                    foreach (var a in Rules.Fan(3, aim, 0.18)) EShoot(e.X, e.Y + 60, a, 320, "#ffffff");
                }
                return;
            }
            // boss2 — the 2027 mothership.
            if (ph == 0)
            {
                e.Fire = 0.12; e.Spin += 0.12;
                foreach (var a in Rules.Ring(5, e.Spin)) EShoot(e.X, e.Y, a, 160, "#c87aff");
            }
            else if (ph == 1)
            {
                e.Fire = 1.3;
                for (int k = 0; k < 9; k++) EShoot(e.X, e.Y + 40, aim, 200 + k * 26, "#7ad8ff");
                foreach (var a in Rules.Ring(18, e.T2 * 0.1)) EShoot(e.X, e.Y, a, 130, "#ff5ad0", 7);
            }
            else
            {
                e.Fire = 0.09; e.Spin += 0.23;
                foreach (var a in new[] { e.Spin, e.Spin + Math.PI * 2 / 3, e.Spin + Math.PI * 4 / 3 }) EShoot(e.X, e.Y, a, 175, "#ff5ad0");
                foreach (var a in new[] { -e.Spin, -e.Spin + Math.PI }) EShoot(e.X, e.Y, a, 140, "#7ad8ff");
                if (e.T2 % 14 == 0) foreach (var a in Rules.Ring(24, 0)) EShoot(e.X, e.Y, a, 120, "#ffffff", 7);
            }
        }

        void UpdateEnemyBullets(double dt)
        {
            foreach (var b in Ebs) { b.X += b.Vx * dt; b.Y += b.Vy * dt; }
            Ebs = Ebs.Where(b => b.X > -20 && b.X < Rules.PF_W + 20 && b.Y > -20 && b.Y < Rules.PF_H + 20).ToList();
            if (Alive && Invuln <= 0)
                foreach (var b in Ebs) if (Rules.Hit(Px, Py, Rules.HITBOX, b.X, b.Y, b.R * 0.8)) { PlayerHit(); break; }
        }

        void PlayerHit()
        {
            if (God) { Invuln = 0.5; return; }
            Lives--;
            Dead = 1.3;
            Sound?.Invoke("die");
            Shake?.Invoke(0.5f);
            Explosion?.Invoke(Px, Py, true);
            Ebs = Ebs.Where(b => Js.Hypot(b.X - Px, b.Y - Py) > 160).ToList();
            if (Power > 1) { Power--; Items.Add(new Item { X = Px, Y = Py - 20, Vy = -160, Kind = 'P' }); }
            Bombs = Math.Max(Bombs, Rules.START_BOMBS);
            if (Lives < 0) { Lives = 0; Phase = Phase.Over; Timer = 2.5; Won = false; }
        }

        void UpdateItems(double dt)
        {
            bool collectAll = Alive && Py < Rules.COLLECT_LINE;
            foreach (var it in Items)
            {
                it.T += dt;
                double d = Js.Hypot(Px - it.X, Py - it.Y);
                if (Alive && (collectAll || d < 70 || (it.T > 0 && BombT > 0)))
                {
                    double s = Math.Max(500, d * 6) * dt;
                    double dd = d == 0 ? 1 : d;
                    it.X += (Px - it.X) / dd * s; it.Y += (Py - it.Y) / dd * s;
                }
                else { it.Vy = Math.Min(110, it.Vy + 260 * dt); it.Y += it.Vy * dt; it.X += Math.Sin(it.T * 3) * 20 * dt; }
                if (Alive && d < 26)
                {
                    it.Y = Rules.PF_H + 999;
                    if (it.Kind == 'P') { if (Power < Rules.MAX_POWER) { Power++; Sound?.Invoke("power"); } else { AddScore(5000); Sound?.Invoke("item"); } }
                    else if (it.Kind == 'B') { Bombs = Math.Min(Rules.MAX_BOMBS, Bombs + 1); Sound?.Invoke("power"); }
                    else { AddScore(500); Sound?.Invoke("item"); }
                }
            }
            Items = Items.Where(it => it.Y < Rules.PF_H + 30).ToList();
        }
    }
}
