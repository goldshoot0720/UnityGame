// The stage from Game4/cloud/src/stage.ts without drawing: tile platforming, enemies, weapons
// and the boss room (incl. the fortress rush and the three-form citadel guardian).
// Coordinates are world px (y down), 32 px tiles, 14 rows.
using System;
using System.Collections.Generic;
using System.Linq;
using static MoeGames.Game4.Levels;

namespace MoeGames.Game4
{
    public class Body { public double X, Y, W, H, Vx, Vy; public bool OnGround, HitWall; }

    public class Mover { public double X, Y, W, X0, Y0, Range, T, Dx, Dy; public char Axis; }

    public class Shot
    {
        public double X, Y, Vx, Vy, R;
        public WeaponKind Kind;
        public int Dmg;
        public bool Pierce, Enemy;
        public double Life, T;
        public string Color;
        public int Charge;
        public double BaseY;
        public string Weapon;
        public HashSet<object> Hits = new HashSet<object>();
    }

    public class Enemy { public string Type; public Body B; public int Hp, Dir, Spawn; public double T, Fire, BaseY, Flash; }

    public class Pickup { public double X, Y, T; public char Kind; public bool Big; public int Spawn; }

    public class Boss { public Hero Hero; public Body B; public int Hp; public string State; public double T; public int Facing; public double Iframes; public int ShotsLeft, Phase; }

    public struct PadInput
    {
        public bool Left, Right, Up, Down, JumpPressed, JumpHeld, ShootPressed, ShootHeld, Prev, Next, UseTank, UseMTank, Pause, Quit;
    }

    public class Stage
    {
        public Hero HeroDef;
        public string StageKey = "penguin";
        public Level Lv;
        public Palette Pal;
        public Body P;
        public int Facing = 1;
        public int Hp = Data.PLAYER.maxHp;
        public int Lives = Data.PLAYER.lives;
        public int ETanks, MTanks;
        public bool Paused;
        public readonly List<(double left, Action callback)> PendingView = new List<(double, Action)>();
        List<(double left, Action callback)> pending = new List<(double, Action)>();
        public Dictionary<string, double> Ammo = new Dictionary<string, double>();
        public List<string> Weapons = new List<string> { "buster" };
        public int Wi;
        public bool Climbing;
        public double SlideT, Coyote, Charge, Iframes, Knock, Dead, ShootAnim;
        public (double x, double y) Checkpoint;
        public readonly List<Mover> Movers = new List<Mover>();
        public List<Shot> Shots = new List<Shot>();
        public List<Enemy> Enemies = new List<Enemy>();
        public readonly List<Pickup> Pickups = new List<Pickup>();
        readonly HashSet<int> killed = new HashSet<int>();
        readonly HashSet<int> taken = new HashSet<int>();
        public Boss Boss;
        public List<string> BossQueue = new List<string>();
        public int FinalPhase, FinalPhaseDone;
        public bool InRoom, DoorClosed;
        public double BossBar;
        public string Msg = "";
        public double MsgT;
        public string RewardMsg = "";
        public double RewardT;
        public double EndT = -1;
        public double T;
        public double CamX;
        /// <summary>Viewport width in world px (depends on the screen aspect).</summary>
        public double ViewW = 800;
        public HashSet<string> RushDone = new HashSet<string>();
        /// <summary>Bumped on each respawn; delayed calls from an older life are ignored.</summary>
        public int Life;
        readonly Func<double> rng;

        public event Action<string> Sound;               // shoot charged hit hurt jump land pick boom door win
        public event Action<string> Music;               // "stage" | "boss" | null (stop)
        public event Action<double, double, string, int> Burst; // x, y, colour, count
        public event Action<float> Shake;
        /// <summary>Scene change: "stages", "play" (next stage — Run.Stage), "gameover" (ending).</summary>
        public event Action<string> Go;
        /// <summary>Test hook: replaces NextBoss when set.</summary>
        public Action NextBossOverride;

        /// <summary>Bare instance (for tests that poke at single methods, like verify.ts does).</summary>
        public Stage(Func<double> rng = null) { this.rng = rng ?? Rand.Default; }

        /// <summary>Set up a stage for Run.Hero / Run.Stage (stage.ts setup()).</summary>
        public static Stage Create(Func<double> rng = null)
        {
            var s = new Stage(rng);
            s.Setup();
            return s;
        }

        void Setup()
        {
            HeroDef = Data.GetHero(Run.Hero);
            StageKey = Run.Stage;
            bool resumeGuardian = StageKey == "citadel" && Run.FortressCarry == null && Progress.CitadelCheckpoint(HeroDef.Key) >= 0;
            if (StageKey == "citadel")
            {
                Progress.MarkCitadelReached(HeroDef.Key);
                FinalPhaseDone = Math.Max(0, Progress.CitadelCheckpoint(HeroDef.Key));
                FinalPhase = FinalPhaseDone;
            }
            Lv = BuildLevel(StageKey);
            Pal = StageKey == "final" ? Data.FINAL_PALETTE : StageKey == "citadel" ? Data.CITADEL_PALETTE : Data.GetHero(StageKey).Palette;
            // Weapons: buster + every beaten boss's weapon.
            foreach (var b in Progress.Beaten(HeroDef.Key)) Weapons.Add(b);
            foreach (var w in Weapons) Ammo[w] = Data.PLAYER.maxAmmo;
            Music?.Invoke("stage");
            for (int i = 0; i < Lv.Entities.Count; i++)
            {
                var e = Lv.Entities[i];
                if (e.Type == "M" || e.Type == "N" || e.Type == "V")
                {
                    char axis = e.Type == "V" ? 'y' : 'x';
                    double range = e.Type == "M" ? 7 * Data.TILE : e.Type == "N" ? 12 * Data.TILE : 5 * Data.TILE;
                    Movers.Add(new Mover { X = e.Col * Data.TILE, Y = e.Row * Data.TILE, W = 3 * Data.TILE, X0 = e.Col * Data.TILE, Y0 = e.Row * Data.TILE, Range = range, Axis = axis });
                }
                else if (e.Type == "h" || e.Type == "a")
                    Pickups.Add(new Pickup { X = e.Col * Data.TILE + 16, Y = e.Row * Data.TILE + 16, Kind = e.Type[0], Big = true, Spawn = i });
            }
            if (StageKey == "final") BossQueue = Data.CHARACTERS.Where(c => c.Key != HeroDef.Key).Select(c => c.Key).ToList();
            else if (StageKey == "citadel") BossQueue = new List<string>();
            else BossQueue = new List<string> { StageKey };
            Checkpoint = (Lv.Start.col * Data.TILE, Lv.Start.row * Data.TILE);
            if (resumeGuardian) Checkpoint = ((Lv.RoomCol + 3) * Data.TILE, 10 * Data.TILE);
            SpawnPlayer();
            if (StageKey == "citadel" && Run.FortressCarry != null)
            {
                Hp = Run.FortressCarry.Hp;
                Lives = Run.FortressCarry.Lives;
                Ammo = new Dictionary<string, double>(Run.FortressCarry.Ammo);
                ETanks = Run.FortressCarry.ETanks;
                MTanks = Run.FortressCarry.MTanks;
                Run.FortressCarry = null;
            }
            if (StageKey == "citadel")
            {
                var tanks = Data.CitadelTankGrant(ETanks, MTanks);
                ETanks = tanks.eTanks;
                MTanks = tanks.mTanks;
            }
            Say(StageKey == "final" ? $"{Data.FINAL_STAGE}　READY" : StageKey == "citadel" ? $"{Data.CITADEL_STAGE}　READY" : $"{Data.GetHero(StageKey).Stage}　READY", 1.6);
        }

        public Action<string, double> SayOverride;
        void Say(string s, double t = 1.4) { if (SayOverride != null) { SayOverride(s, t); return; } Msg = s; MsgT = t; }

        public Action<double, Action> ScheduleOverride;
        public void Schedule(double delay, Action callback)
        {
            if (ScheduleOverride != null) { ScheduleOverride(delay, callback); return; }
            pending.Add((delay, callback));
        }
        public int PendingCount => pending.Count;
        public double PendingLeft(int i) => pending[i].left;

        public void TickPending(double dt)
        {
            for (int i = 0; i < pending.Count; i++) pending[i] = (pending[i].left - dt, pending[i].callback);
            var due = pending.Where(p => p.left <= 0).ToList();
            pending = pending.Where(p => p.left > 0).ToList();
            foreach (var p in due) p.callback();
        }

        public void UseTank()
        {
            if (ETanks <= 0 || Hp >= Data.PLAYER.maxHp || Dead > 0 || EndT >= 0) return;
            ETanks--;
            Hp = Data.PLAYER.maxHp;
            RewardMsg = "E 罐：生命全滿";
            RewardT = 1.5;
            Sound?.Invoke("pick");
        }

        public void UseMTank()
        {
            if (MTanks <= 0 || Dead > 0 || EndT >= 0) return;
            bool needsAmmo = Weapons.Any(w => w != "buster" && Ammo[w] < Data.PLAYER.maxAmmo);
            if (Hp >= Data.PLAYER.maxHp && !needsAmmo) return;
            MTanks--;
            Hp = Data.PLAYER.maxHp;
            foreach (var w in Weapons) if (w != "buster") Ammo[w] = Data.PLAYER.maxAmmo;
            RewardMsg = "M 罐：生命與武器全滿";
            RewardT = 1.8;
            Sound?.Invoke("pick");
        }

        double LevelW => Lv.Cols * Data.TILE;

        void SpawnPlayer()
        {
            P = new Body { X = Checkpoint.x + 6, Y = Checkpoint.y + Data.TILE - Data.PLAYER.hitH, W = Data.PLAYER.hitW, H = Data.PLAYER.hitH };
            Hp = Data.PLAYER.maxHp;
            Iframes = 1; Knock = 0; SlideT = 0; Climbing = false; Charge = 0; Dead = 0;
            Enemies = new List<Enemy>(); Shots = new List<Shot>(); killed.Clear();
            InRoom = false; DoorClosed = false; Boss = null; BossBar = 0;
            RewardT = 0;
            // Refill the boss queue on every (re)spawn so dying in the boss room never loses the boss.
            if (StageKey == "final") BossQueue = Data.CHARACTERS.Where(c => c.Key != HeroDef.Key && !RushDone.Contains(c.Key)).Select(c => c.Key).ToList();
            else if (StageKey == "citadel") { BossQueue = new List<string>(); FinalPhase = FinalPhaseDone; }
            else BossQueue = new List<string> { StageKey };
            Life++;
            // Reopen the door.
            foreach (int r in Lv.DoorRows) Lv.Tiles[r][Lv.RoomCol] = T_EMPTY;
        }

        // ── tiles ──

        public int Tile(int c, int r)
        {
            if (c < 0 || c >= Lv.Cols) return T_WALL;
            if (r < 0 || r >= Data.ROWS) return T_EMPTY;
            return Lv.Tiles[r][c];
        }

        static int F(double v) => (int)Math.Floor(v);
        bool SolidAt(double x, double y) => IsSolid(Tile(F(x / Data.TILE), F(y / Data.TILE)));

        int Move(Body b, double dt, bool drop = false)
        {
            const int T = Data.TILE;
            b.HitWall = false;
            // X
            b.X += b.Vx * dt;
            int r0 = F(b.Y / T), r1 = F((b.Y + b.H - 0.01) / T);
            if (b.Vx > 0)
            {
                int c = F((b.X + b.W - 0.01) / T);
                for (int r = r0; r <= r1; r++) if (IsSolid(Tile(c, r))) { b.X = c * T - b.W; b.Vx = 0; b.HitWall = true; break; }
            }
            else if (b.Vx < 0)
            {
                int c = F(b.X / T);
                for (int r = r0; r <= r1; r++) if (IsSolid(Tile(c, r))) { b.X = (c + 1) * T; b.Vx = 0; b.HitWall = true; break; }
            }
            // Y
            double prevBottom = b.Y + b.H;
            b.Y += b.Vy * dt;
            bool wasGround = b.OnGround;
            b.OnGround = false;
            int c0 = F(b.X / T), c1 = F((b.X + b.W - 0.01) / T);
            double carry = 0;
            if (b.Vy >= 0)
            {
                int r = F((b.Y + b.H - 0.01) / T);
                for (int c = c0; c <= c1; c++)
                {
                    int t = Tile(c, r);
                    if (IsSolid(t) || (!drop && IsOneWay(t) && prevBottom <= r * T + 0.5)) { b.Y = r * T - b.H; b.Vy = 0; b.OnGround = true; break; }
                }
                foreach (var m in Movers)
                {
                    if (b.X + b.W > m.X && b.X < m.X + m.W && prevBottom <= m.Y + 1 + Math.Max(0, m.Dy) && b.Y + b.H >= m.Y)
                    {
                        b.Y = m.Y - b.H; b.Vy = 0; b.OnGround = true; carry = m.Dx;
                    }
                }
            }
            else
            {
                int r = F(b.Y / T);
                for (int c = c0; c <= c1; c++) if (IsSolid(Tile(c, r))) { b.Y = (r + 1) * T; b.Vy = 0; break; }
            }
            if (carry != 0) b.X += carry;
            return b.OnGround && !wasGround ? 1 : 0;
        }

        static bool Overlaps(double ax, double ay, double aw, double ah, Body b) => ax < b.X + b.W && ax + aw > b.X && ay < b.Y + b.H && ay + ah > b.Y;
        static bool Overlaps(Body a, Body b) => Overlaps(a.X, a.Y, a.W, a.H, b);

        // ── update ──

        public void Update(double dt, PadInput k)
        {
            dt = Math.Min(dt, 1.0 / 30);
            if (k.Pause) { Paused = !Paused; return; }
            if (Paused) return;
            T += dt;
            if (MsgT > 0) MsgT -= dt;
            if (RewardT > 0) RewardT -= dt;
            if (k.Quit) { Run.Outcome = "none"; Go?.Invoke("stages"); return; }
            TickPending(dt);
            foreach (var m in Movers)
            {
                m.T += dt;
                double kk = (1 - Math.Cos(m.T / 4 * Math.PI * 2)) / 2;
                double nx = m.Axis == 'x' ? m.X0 + kk * m.Range : m.X0;
                double ny = m.Axis == 'y' ? m.Y0 - kk * m.Range : m.Y0;
                m.Dx = nx - m.X; m.Dy = ny - m.Y; m.X = nx; m.Y = ny;
            }
            if (EndT >= 0)
            {
                EndT += dt;
                if (EndT > 3.5 && EndT - dt <= 3.5) Finish();
                UpdateShots(dt);
                return;
            }
            if (Dead > 0)
            {
                Dead += dt;
                if (Dead > 2.2)
                {
                    Lives--;
                    if (Lives < 0) { Run.Outcome = "lose"; Run.Message = "GAME OVER"; Go?.Invoke("stages"); return; }
                    SpawnPlayer();
                }
                return;
            }
            if (k.UseTank) UseTank();
            if (k.UseMTank) UseMTank();
            UpdatePlayer(dt, k);
            SpawnEnemies();
            UpdateEnemies(dt);
            UpdateBoss(dt);
            UpdateShots(dt);
            UpdatePickups(dt);
            // Camera.
            double W = ViewW;
            if (InRoom)
            {
                double roomX = Lv.RoomCol * Data.TILE;
                CamX += (Js.Clamp(roomX + (ROOM_COLS * Data.TILE - W) / 2, 0, LevelW - W) - CamX) * Math.Min(1, dt * 6);
            }
            else CamX = Js.Clamp(P.X + P.W / 2 - W / 2, 0, Math.Max(0, Lv.RoomCol * Data.TILE + 4 * Data.TILE - W));
        }

        public string WeaponKey => Weapons[Wi];

        void UpdatePlayer(double dt, PadInput k)
        {
            const int T = Data.TILE;
            var p = P;
            if (Iframes > 0) Iframes -= dt;
            if (ShootAnim > 0) ShootAnim -= dt;
            if (k.Prev) { Wi = (Wi + Weapons.Count - 1) % Weapons.Count; Sound?.Invoke("pick"); }
            if (k.Next) { Wi = (Wi + 1) % Weapons.Count; Sound?.Invoke("pick"); }
            int dir = (k.Right ? 1 : 0) - (k.Left ? 1 : 0);
            int cx = F((p.X + p.W / 2) / T);
            int midR = F((p.Y + p.H / 2) / T);
            int footR = F((p.Y + p.H + 2) / T);
            if (Knock > 0)
            {
                Knock -= dt;
                p.Vx = -Facing * 70;
                p.Vy = Math.Min(p.Vy + Data.GRAVITY * dt, 600);
                Move(p, dt);
            }
            else if (Climbing)
            {
                p.Vx = 0;
                p.Vy = (k.Down ? 1 : 0) * Data.PLAYER.climb - (k.Up ? 1 : 0) * Data.PLAYER.climb;
                p.Y += p.Vy * dt;
                bool onLadder = IsLadder(Tile(cx, midR)) || IsLadder(Tile(cx, F((p.Y + p.H - 1) / T)));
                if (!onLadder || k.JumpPressed) Climbing = false;
                if (p.Vy > 0 && IsSolid(Tile(cx, F((p.Y + p.H) / T)))) { Climbing = false; p.Y = F((p.Y + p.H) / T) * T - p.H; }
                // Climbed out of the top.
                if (p.Vy < 0 && !IsLadder(Tile(cx, F((p.Y + p.H - 4) / T))))
                {
                    Climbing = false;
                    p.Y = F((p.Y + p.H) / T) * T - p.H;
                }
            }
            else if (SlideT > 0)
            {
                SlideT -= dt;
                p.Vx = Facing * Data.PLAYER.slideSpeed;
                p.Vy = Math.Min(p.Vy + Data.GRAVITY * dt, 600);
                Move(p, dt);
                bool blocked = SolidAt(p.X + 1, p.Y + p.H - Data.PLAYER.hitH) || SolidAt(p.X + p.W - 1, p.Y + p.H - Data.PLAYER.hitH);
                if ((SlideT <= 0 || !p.OnGround || (k.JumpPressed && !k.Down)) && !blocked)
                {
                    SlideT = 0;
                    p.Y = p.Y + p.H - Data.PLAYER.hitH; p.H = Data.PLAYER.hitH;
                }
                else if (SlideT <= 0) SlideT = 0.05;
            }
            else
            {
                // Ladders.
                if ((k.Up && IsLadder(Tile(cx, midR))) || (k.Down && p.OnGround && Tile(cx, footR) == T_LADDER_TOP))
                {
                    Climbing = true;
                    p.X = cx * T + T / 2.0 - p.W / 2;
                    if (k.Down && p.OnGround) p.Y += 10;
                    p.Vy = 0;
                    return;
                }
                p.Vx = dir * Data.PLAYER.run;
                if (dir != 0) Facing = dir;
                if (p.OnGround) Coyote = Data.PLAYER.coyote; else Coyote -= dt;
                if (k.JumpPressed)
                {
                    if (k.Down && p.OnGround)
                    {
                        SlideT = Data.PLAYER.slideTime;
                        p.Y = p.Y + p.H - Data.PLAYER.slideH; p.H = Data.PLAYER.slideH;
                    }
                    else if (Coyote > 0) { p.Vy = -Data.PLAYER.jump; Coyote = 0; Sound?.Invoke("jump"); }
                }
                if (!k.JumpHeld && p.Vy < -Data.PLAYER.jumpCut) p.Vy = -Data.PLAYER.jumpCut;
                p.Vy = Math.Min(p.Vy + Data.GRAVITY * dt, 620);
                if (Move(p, dt) == 1) Sound?.Invoke("land");
            }
            // Keep out of the closed boss door / level edges.
            p.X = Js.Clamp(p.X, 0, LevelW - p.W);
            if (InRoom) p.X = Math.Max(p.X, (Lv.RoomCol + 1) * T);
            // Shooting.
            string wk = WeaponKey;
            if (k.ShootPressed && SlideT <= 0) Fire(0);
            if (wk == "buster")
            {
                if (k.ShootHeld) Charge += dt;
                else
                {
                    if (Charge >= Data.PLAYER.chargeMid) Fire(Charge >= Data.PLAYER.chargeFull ? 2 : 1);
                    Charge = 0;
                }
            }
            else Charge = 0;
            // Hazards.
            int feetT = Tile(F((p.X + p.W / 2) / T), F((p.Y + p.H - 2) / T));
            if (feetT == T_SPIKE || (Tile(F((p.X + 3) / T), F((p.Y + p.H + 1) / T)) == T_SPIKE && p.OnGround)) Die();
            if (p.Y > Data.ROWS * T + 40) Die();
            // Checkpoints.
            foreach (var c in Lv.Checkpoints)
                if (p.X > c.col * T && Checkpoint.x < c.col * T) { Checkpoint = (c.col * T, c.row * T); Say("中繼點", 0.8); }
            // Boss room entry.
            if (!InRoom && p.X > (Lv.RoomCol + 2) * T) EnterRoom();
        }

        void Fire(int charge)
        {
            var p = P;
            string wk = WeaponKey;
            double hx = Facing > 0 ? p.X + p.W + 6 : p.X - 6;
            double hy = p.Y + 22;
            if (wk == "buster")
            {
                if (charge == 0 && Shots.Count(s => !s.Enemy && s.Kind == WeaponKind.Buster) >= 3) return;
                Shots.Add(new Shot
                {
                    X = hx, Y = hy, Vx = Facing * 430, Vy = 0, R = charge == 2 ? 14 : charge == 1 ? 9 : 5, Kind = WeaponKind.Buster,
                    Dmg = charge == 2 ? 3 : charge == 1 ? 2 : 1, Pierce = charge == 2, Enemy = false, Life = 1.6, Color = charge > 0 ? "#9ff7ff" : Data.BUSTER.Color,
                    Charge = charge, BaseY = hy, Weapon = wk,
                });
                Sound?.Invoke(charge > 0 ? "charged" : "shoot");
            }
            else
            {
                var h = Data.GetHero(wk);
                if (Ammo[wk] < h.Weapon.Cost) return;
                if (h.Weapon.Kind == WeaponKind.Rapid && Shots.Count(s => !s.Enemy && s.Kind == WeaponKind.Rapid) >= 6) return;
                Ammo[wk] -= h.Weapon.Cost;
                Shots.AddRange(WeaponShots(h, hx, hy, Facing, false));
                Sound?.Invoke("shoot");
            }
            ShootAnim = 0.2;
        }

        /// <summary>Shots for a boss weapon (used by both the player and the bosses).</summary>
        List<Shot> WeaponShots(Hero h, double x, double y, int dir, bool enemy)
        {
            string c = h.Weapon.Color;
            var kind = h.Weapon.Kind;
            Shot Base(double vx, double vy, double r, int dmg, bool pierce, double life = 1.8)
                => new Shot { X = x, Y = y, Vx = vx, Vy = vy, R = r, Kind = kind, Dmg = dmg, Pierce = pierce, Enemy = enemy, Life = life, Color = c, BaseY = y, Weapon = h.Key };
            double sp = enemy ? 0.75 : 1;
            switch (kind)
            {
                case WeaponKind.Bubble: return new List<Shot> { Base(dir * 170 * sp, 0, 10, 2, false, 3) };
                case WeaponKind.Ice: return new List<Shot> { Base(dir * 520 * sp, 0, 7, 2, true) };
                case WeaponKind.Bounce: return new List<Shot> { Base(dir * 260 * sp, -220, 8, 2, false, 2.6) };
                case WeaponKind.Rapid: return new List<Shot> { Base(dir * 480 * sp, (rng() - 0.5) * 30, 4, 1, false) };
                case WeaponKind.Claw: return new List<Shot> { Base(dir * 330 * sp, 0, 14, 3, true, 1.6) };
                case WeaponKind.Book: return new List<Shot> { Base(dir * 380 * sp, 0, 10, 2, true, 1.6) };
                case WeaponKind.Fire3: return new[] { -0.25, 0, 0.25 }.Select(a => Base(Math.Cos(a) * dir * 360 * sp, Math.Sin(a) * 360 * sp, 7, 2, false)).ToList();
                case WeaponKind.Whirl: return new List<Shot> { Base(dir * 220 * sp, -170 * sp, 12, 2, true, 1.8) };
                default: return new List<Shot> { Base(dir * 430, 0, 5, 1, false) };
            }
        }

        void Hurt(int dmg, double fromX)
        {
            if (Iframes > 0 || Dead > 0 || EndT >= 0) return;
            Hp -= dmg;
            Iframes = Data.PLAYER.iframes;
            Knock = Data.PLAYER.knockback;
            Facing = fromX > P.X ? 1 : -1;
            Climbing = false;
            Sound?.Invoke("hurt");
            if (Hp <= 0) Die();
        }

        void Die()
        {
            if (Dead > 0) return;
            Hp = 0;
            Dead = 0.001;
            Sound?.Invoke("boom");
            Burst?.Invoke(P.X + P.W / 2, P.Y + P.H / 2, "#9ff7ff", 32);
            Music?.Invoke(null);
            Schedule(2.3, () => Music?.Invoke("stage"));
        }

        // ── enemies ──

        void SpawnEnemies()
        {
            const int T = Data.TILE;
            double x0 = CamX - T, x1 = CamX + ViewW + T;
            for (int i = 0; i < Lv.Entities.Count; i++)
            {
                var e = Lv.Entities[i];
                if (e.Type != "W" && e.Type != "F" && e.Type != "T") continue;
                double ex = e.Col * T;
                if (ex < x0 || ex > x1 || killed.Contains(i) || Enemies.Any(q => q.Spawn == i)) continue;
                // Only spawn at the screen edge (classic), not in the player's face.
                if (ex > CamX + T * 2 && ex < CamX + ViewW - T * 2 && this.T > 0.2) continue;
                (double w, double h) size = e.Type == "W" ? (30, 34) : e.Type == "F" ? (34, 26) : (32, 30);
                var b = new Body { X = ex, Y = e.Row * T + T - size.h, W = size.w, H = size.h };
                if (e.Type == "F") b.Y = e.Row * T;
                Enemies.Add(new Enemy { Type = e.Type, B = b, Hp = e.Type == "W" ? 2 : e.Type == "F" ? 1 : 3, Dir = -1, T = rng() * 3, Fire = 1 + rng(), BaseY = b.Y, Spawn = i });
            }
        }

        void UpdateEnemies(double dt)
        {
            const int T = Data.TILE;
            var p = P;
            for (int i = Enemies.Count - 1; i >= 0; i--)
            {
                var e = Enemies[i];
                var b = e.B;
                e.T += dt;
                if (e.Flash > 0) e.Flash -= dt;
                if (b.X < CamX - ViewW || b.X > CamX + ViewW * 2) { Enemies.RemoveAt(i); continue; }
                if (e.Type == "W")
                {
                    b.Vx = e.Dir * 60;
                    b.Vy = Math.Min(b.Vy + Data.GRAVITY * dt, 600);
                    Move(b, dt);
                    double aheadX = e.Dir > 0 ? b.X + b.W + 2 : b.X - 2;
                    int below = Tile(F(aheadX / T), F((b.Y + b.H + 2) / T));
                    bool ledge = b.OnGround && !IsSolid(below) && !IsOneWay(below);
                    if (b.HitWall || ledge) e.Dir = -e.Dir;
                    if (b.Y > Data.ROWS * T) { Enemies.RemoveAt(i); continue; }
                }
                else if (e.Type == "F")
                {
                    double dx = p.X - b.X, dy = p.Y - b.Y;
                    if (Math.Abs(dx) < 260) { b.X += Math.Sign(dx) * 70 * dt; b.Y += Math.Sign(dy) * 40 * dt; }
                    else b.Y = e.BaseY + Math.Sin(e.T * 2.5) * 20;
                    e.Dir = dx > 0 ? 1 : -1;
                }
                else
                {
                    e.Dir = p.X > b.X ? 1 : -1;
                    e.Fire -= dt;
                    if (e.Fire <= 0 && Math.Abs(p.X - b.X) < 420)
                    {
                        e.Fire = 1.8;
                        double ang = Math.Atan2(p.Y + 20 - (b.Y + 10), p.X - b.X);
                        Shots.Add(new Shot { X = b.X + b.W / 2, Y = b.Y + 10, Vx = Math.Cos(ang) * 200, Vy = Math.Sin(ang) * 200, R = 5, Kind = WeaponKind.Buster, Dmg = 3, Enemy = true, Life = 3, Color = "#ff6b6b", Weapon = "enemy" });
                    }
                }
                if (Dead <= 0 && Overlaps(b, p)) Hurt(3, b.X);
            }
        }

        void KillEnemy(int i)
        {
            var e = Enemies[i];
            killed.Add(e.Spawn);
            Enemies.RemoveAt(i);
            Sound?.Invoke("boom");
            Burst?.Invoke(e.B.X + e.B.W / 2, e.B.Y + e.B.H / 2, "#ff9a3c", 14);
            double r = rng();
            if (r < 0.01) Pickups.Add(new Pickup { X = e.B.X + e.B.W / 2, Y = e.B.Y, Kind = 'm', Big = true, Spawn = -1 });
            else if (r < 0.04) Pickups.Add(new Pickup { X = e.B.X + e.B.W / 2, Y = e.B.Y, Kind = 'e', Big = true, Spawn = -1 });
            else if (r < 0.29) Pickups.Add(new Pickup { X = e.B.X + e.B.W / 2, Y = e.B.Y, Kind = r < 0.17 ? 'h' : 'a', Big = false, Spawn = -1 });
        }

        // ── boss ──

        public void EnterRoom()
        {
            InRoom = true;
            if (StageKey == "citadel") Checkpoint = ((Lv.RoomCol + 3) * Data.TILE, 10 * Data.TILE);
            Sound?.Invoke("door");
            int life = Life;
            Schedule(0.6, () =>
            {
                if (life != Life || Dead > 0) return;
                foreach (int r in Lv.DoorRows) Lv.Tiles[r][Lv.RoomCol] = T_WALL;
                DoorClosed = true;
                Music?.Invoke("boss");
                NextBoss();
            });
        }

        public void NextBoss()
        {
            if (NextBossOverride != null) { NextBossOverride(); return; }
            Hero h;
            int phase = 0;
            int hp;
            if (StageKey == "citadel")
            {
                if (FinalPhase >= Data.FINAL_BOSS_PHASES.Length) return;
                phase = ++FinalPhase;
                var form = Data.FINAL_BOSS_PHASES[phase - 1];
                h = new Hero
                {
                    Key = "citadelBoss", Name = "終焉守護者", Title = form.Name, Stage = Data.CITADEL_STAGE,
                    WeakTo = form.WeakTo, Palette = Data.CITADEL_PALETTE,
                    Weapon = new Weapon { Name = form.Name, Kind = form.AttackKind, Color = form.Color, Cost = 0, Desc = "" },
                };
                hp = form.Hp;
                Shots = Shots.Where(s => !s.Enemy).ToList();
            }
            else
            {
                if (BossQueue.Count == 0) return;
                string key = BossQueue[0];
                BossQueue.RemoveAt(0);
                h = Data.GetHero(key);
                hp = StageKey == "final" ? Data.BOSS.rushBossHp : Data.BOSS.maxHp;
            }
            double roomX = Lv.RoomCol * Data.TILE;
            Boss = new Boss
            {
                Hero = h, B = new Body { X = roomX + ROOM_COLS * Data.TILE - 5 * Data.TILE, Y = Data.TILE + 2, W = Data.BOSS.hitW, H = Data.BOSS.hitH },
                Hp = hp, State = "intro", Facing = -1, Phase = phase,
            };
            BossBar = 0;
            Say(phase > 0 ? $"{h.Name}　第{phase}形態・{h.Title}" : $"{h.Name}　{h.Title}", 1.8);
        }

        int BossMaxHp(Boss b) => b.Phase > 0 ? Data.FINAL_BOSS_PHASES[b.Phase - 1].Hp : StageKey == "final" ? Data.BOSS.rushBossHp : Data.BOSS.maxHp;

        void UpdateBoss(double dt)
        {
            var B = Boss;
            if (B == null) return;
            var b = B.B;
            var p = P;
            B.T += dt;
            if (B.Iframes > 0) B.Iframes -= dt;
            int maxHp = BossMaxHp(B);
            bool rush = B.Hp <= maxHp / 2.0;
            double speed = (rush ? 1.35 : 1) * (B.Phase > 0 ? 1 + (B.Phase - 1) * 0.15 : 1);
            if (B.State == "intro")
            {
                b.Vy = Math.Min(b.Vy + Data.GRAVITY * dt, 700);
                Move(b, dt);
                if (b.OnGround) BossBar = Math.Min(maxHp, BossBar + dt * 30);
                if (BossBar >= maxHp) { B.State = "idle"; B.T = 0; }
                return;
            }
            if (B.State == "dead") return;
            B.Facing = p.X > b.X ? 1 : -1;
            b.Vy = Math.Min(b.Vy + Data.GRAVITY * dt, 700);
            switch (B.State)
            {
                case "idle":
                    b.Vx = 0;
                    if (B.T > 0.8 / speed)
                    {
                        double r = rng();
                        B.T = 0;
                        if (r < 0.4) { B.State = "jump"; b.Vy = -560; b.Vx = B.Facing * (140 + rng() * 120) * speed; }
                        else if (r < 0.8) { B.State = "shoot"; B.ShotsLeft = rush ? 3 : 2; }
                        else { B.State = "dash"; b.Vx = B.Facing * 300 * speed; }
                    }
                    break;
                case "jump":
                    if (b.OnGround && B.T > 0.2) { B.State = "idle"; B.T = 0; if (rng() < 0.5) { B.State = "shoot"; B.ShotsLeft = 1; } }
                    break;
                case "shoot":
                    b.Vx = 0;
                    if (B.T > 0.45 / speed)
                    {
                        B.T = 0;
                        Shots.AddRange(WeaponShots(B.Hero, b.X + b.W / 2 + B.Facing * 20, b.Y + 40, B.Facing, true));
                        Sound?.Invoke("shoot");
                        if (--B.ShotsLeft <= 0) B.State = "idle";
                    }
                    break;
                case "dash":
                    if (b.HitWall || B.T > 0.9) { B.State = "idle"; B.T = 0; b.Vx = 0; }
                    break;
            }
            Move(b, dt);
            double roomX = Lv.RoomCol * Data.TILE;
            b.X = Js.Clamp(b.X, roomX + Data.TILE, roomX + (ROOM_COLS - 1) * Data.TILE - b.W);
            if (Dead <= 0 && Overlaps(b, p)) Hurt(Data.BOSS.contact + (B.Phase > 0 ? B.Phase - 1 : 0), b.X);
        }

        public void HitBoss(Shot s)
        {
            var B = Boss;
            if (B == null || B.State == "intro" || B.State == "dead" || B.Iframes > 0) return;
            int dmg = B.Phase > 0 ? Data.FinalBossDamage(B.Phase, s.Weapon, s.Charge) : Data.BossDamage(B.Hero.Key, HeroDef.Key, s.Weapon == "buster" ? "buster" : s.Weapon, s.Charge);
            B.Hp -= dmg;
            B.Iframes = Data.BOSS.iframes;
            Sound?.Invoke("hit");
            if (dmg >= 4) { Say("弱點命中！", 0.6); Shake?.Invoke(0.2f); }
            if (B.Hp <= 0)
            {
                B.Hp = 0; B.State = "dead";
                Sound?.Invoke("boom");
                double cx = B.B.X + B.B.W / 2, cy = B.B.Y + B.B.H / 2;
                for (int i = 0; i < 3; i++) Schedule(i * 0.25, () => Burst?.Invoke(cx, cy, B.Hero.Weapon.Color, 20));
                int life = Life;
                Schedule(1.2, () =>
                {
                    if (life != Life) return;
                    if (StageKey == "citadel")
                    {
                        FinalPhaseDone = Math.Max(FinalPhaseDone, B.Phase);
                        if (FinalPhaseDone < Data.FINAL_BOSS_PHASES.Length) Progress.MarkCitadelPhase(HeroDef.Key, FinalPhaseDone);
                        Boss = null;
                        if (FinalPhaseDone < Data.FINAL_BOSS_PHASES.Length) { NextBoss(); return; }
                        Win();
                        return;
                    }
                    bool wasDefeated = RushDone.Contains(B.Hero.Key);
                    RushDone.Add(B.Hero.Key);
                    if (StageKey == "final" && !wasDefeated)
                    {
                        var reward = Data.FortressBossReward(Hp, Lives, RushDone.Count);
                        Hp = reward.hp;
                        Lives = reward.lives;
                        RewardMsg = $"生命 +{Data.FORTRESS_REWARDS.heal}{(reward.extraLife ? "　殘機 +1" : "")}";
                        RewardT = 2.4;
                        Sound?.Invoke("pick");
                    }
                    Boss = null;
                    if (BossQueue.Count > 0) { NextBoss(); return; }
                    if (StageKey == "final")
                    {
                        Progress.MarkCitadelReached(HeroDef.Key);
                        Run.FortressCarry = new FortressCarry { Hp = Hp, Lives = Lives, Ammo = new Dictionary<string, double>(Ammo), ETanks = ETanks, MTanks = MTanks };
                        Run.Stage = "citadel";
                        Music?.Invoke(null);
                        Go?.Invoke("play");
                        return;
                    }
                    Win();
                });
            }
        }

        void Win()
        {
            Music?.Invoke(null);
            Sound?.Invoke("win");
            EndT = 0;
            if (StageKey == "citadel")
            {
                Progress.MarkCleared(HeroDef.Key);
                Run.Outcome = "ending";
                Say("要塞攻破！", 3);
            }
            else
            {
                Progress.MarkBeaten(HeroDef.Key, StageKey);
                var h = Data.GetHero(StageKey);
                Run.Outcome = "win";
                Run.Message = $"取得武器：{h.Weapon.Name}！";
                Say(Run.Message, 3);
            }
        }

        void Finish()
        {
            if (Run.Outcome == "ending") Go?.Invoke("gameover");
            else Go?.Invoke("stages");
        }

        // ── shots & pickups ──

        void UpdateShots(double dt)
        {
            const int T = Data.TILE;
            var p = P;
            for (int i = Shots.Count - 1; i >= 0; i--)
            {
                var s = Shots[i];
                s.T += dt; s.Life -= dt;
                switch (s.Kind)
                {
                    case WeaponKind.Bubble: s.X += s.Vx * dt; s.Y = s.BaseY + Math.Sin(s.T * 6) * 14; break;
                    case WeaponKind.Bounce:
                        s.Vy += 500 * dt;
                        s.X += s.Vx * dt; if (SolidAt(s.X, s.Y)) { s.Vx = -s.Vx; s.X += s.Vx * dt * 2; }
                        s.Y += s.Vy * dt; if (SolidAt(s.X, s.Y + s.R)) { s.Vy = -Math.Abs(s.Vy) * 0.9 - 120; s.Y -= 2; }
                        break;
                    case WeaponKind.Claw:
                        {
                            s.X += s.Vx * dt;
                            double gy = s.Y;
                            for (int r = F(s.BaseY / T); r < Data.ROWS; r++)
                                if (IsSolid(Tile(F(s.X / T), r)) || Tile(F(s.X / T), r) == T_TOP) { gy = r * T - s.R; break; }
                            s.Y += (gy - s.Y) * Math.Min(1, dt * 12);
                            break;
                        }
                    case WeaponKind.Book:
                        if (s.T > 0.5)
                        {
                            double tx = s.Enemy && Boss != null ? Boss.B.X : p.X;
                            s.Vx += Math.Sign(tx - s.X) * 1400 * dt;
                            s.Vx = Js.Clamp(s.Vx, -420, 420);
                        }
                        s.X += s.Vx * dt;
                        if (!s.Enemy && s.T > 0.8 && Math.Abs(s.X - p.X) < 20) s.Life = 0;
                        break;
                    default: s.X += s.Vx * dt; s.Y += s.Vy * dt; break;
                }
                if (s.Kind != WeaponKind.Bounce && s.Kind != WeaponKind.Book && s.Kind != WeaponKind.Claw && s.Kind != WeaponKind.Whirl && SolidAt(s.X, s.Y)) s.Life = 0;
                if (s.X < CamX - 60 || s.X > CamX + ViewW + 60) s.Life = 0;
                if (s.Life <= 0) { Shots.RemoveAt(i); continue; }
                double bx = s.X - s.R, by = s.Y - s.R, bw = s.R * 2;
                if (s.Enemy)
                {
                    if (Dead <= 0 && Overlaps(bx, by, bw, bw, p)) { Hurt(s.Dmg + (Boss != null ? 1 : 0), s.X); Shots.RemoveAt(i); }
                    continue;
                }
                bool used = false;
                for (int j = Enemies.Count - 1; j >= 0; j--)
                {
                    var e = Enemies[j];
                    if (s.Hits.Contains(e) || !Overlaps(bx, by, bw, bw, e.B)) continue;
                    s.Hits.Add(e);
                    e.Hp -= s.Dmg; e.Flash = 0.1;
                    Sound?.Invoke("hit");
                    if (e.Hp <= 0) KillEnemy(j);
                    if (!s.Pierce) { used = true; break; }
                }
                if (!used && Boss != null && !s.Hits.Contains(Boss) && Overlaps(bx, by, bw, bw, Boss.B))
                {
                    s.Hits.Add(Boss);
                    HitBoss(s);
                    if (!s.Pierce) used = true;
                }
                if (used) Shots.RemoveAt(i);
            }
        }

        void UpdatePickups(double dt)
        {
            const int T = Data.TILE;
            var p = P;
            for (int i = Pickups.Count - 1; i >= 0; i--)
            {
                var k = Pickups[i];
                k.T += dt;
                if (k.Spawn >= 0 && taken.Contains(k.Spawn)) { Pickups.RemoveAt(i); continue; }
                if (k.Spawn < 0)
                {
                    // Dropped pickups fall to the floor and fade after 6 s.
                    if (!SolidAt(k.X, k.Y + 8) && !IsOneWay(Tile(F(k.X / T), F((k.Y + 8) / T)))) k.Y += 200 * dt;
                    if (k.T > 6) { Pickups.RemoveAt(i); continue; }
                }
                if (Overlaps(k.X - 10, k.Y - 10, 20, 20, p))
                {
                    int amt = k.Big ? 10 : 4;
                    if (k.Kind == 'h') Hp = Math.Min(Data.PLAYER.maxHp, Hp + amt);
                    else if (k.Kind == 'e') ETanks = Math.Min(Data.E_TANK.max, ETanks + 1);
                    else if (k.Kind == 'm') MTanks = Math.Min(Data.M_TANK.max, MTanks + 1);
                    else
                    {
                        string w = WeaponKey != "buster" ? WeaponKey : Weapons.FirstOrDefault(x => x != "buster" && Ammo[x] < Data.PLAYER.maxAmmo);
                        if (w != null) Ammo[w] = Math.Min(Data.PLAYER.maxAmmo, Ammo[w] + amt);
                    }
                    if (k.Spawn >= 0) taken.Add(k.Spawn);
                    Pickups.RemoveAt(i);
                    Sound?.Invoke("pick");
                }
            }
        }
    }
}
