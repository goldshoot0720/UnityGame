// 萌友瘋狂坦克 — terrain, ballistics and AI ported 1:1 from Game10/cloud/src/rules.ts, plus the
// battle state machine from scenes/play.ts (turns, charging, shells, crates, settling) without drawing.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MoeGames.Game10
{
    public enum ShotKind { Normal, Cluster, Laser, Digger, Teleport, Freeze, Heal, Wave }

    public class Weapon
    {
        public string Id, Name, Color, Desc;
        public ShotKind Kind;
        public double Dmg, Radius, Spread;
        public int Count, Uses;
        public Weapon With(ShotKind k) { var w = (Weapon)MemberwiseClone(); w.Kind = k; return w; }
    }

    public class Hero { public string Id, Name, Title, Color; public Weapon Special; }

    public class MapStyle { public string Name, Top, Dirt, Deep, Sky0, Sky1; }

    public struct Impact { public double X, Y; public int Hit, Steps; }

    public class BodyPos { public double X, Y; public bool Alive; }

    public struct AiShot { public double Elev, Power; public int Facing; public double Miss; }

    public static class Rules
    {
        public static readonly Weapon[] BASE_WEAPONS =
        {
            new Weapon { Id = "std", Name = "標準彈", Kind = ShotKind.Normal, Dmg = 28, Radius = 52, Count = 1, Spread = 0, Uses = -1, Color = "#ffd84a", Desc = "無限使用" },
            new Weapon { Id = "tri", Name = "三連彈", Kind = ShotKind.Normal, Dmg = 16, Radius = 38, Count = 3, Spread = 0.07, Uses = 2, Color = "#7fe0ff", Desc = "三顆散射" },
            new Weapon { Id = "heavy", Name = "重砲", Kind = ShotKind.Normal, Dmg = 46, Radius = 84, Count = 1, Spread = 0, Uses = 1, Color = "#ff7a5a", Desc = "大爆炸" },
        };

        static Weapon Sp(string id, string name, ShotKind kind, double dmg, double radius, string color, string desc, int count = 1, double spread = 0)
            => new Weapon { Id = id, Name = name, Kind = kind, Dmg = dmg, Radius = radius, Count = count, Spread = spread, Uses = 1, Color = color, Desc = desc };

        public static readonly Hero[] HEROES =
        {
            new Hero { Id = "whale", Name = "汐音", Title = "鯨魚女僕", Color = "#4f8dff", Special = Sp("wave", "巨浪彈", ShotKind.Wave, 30, 96, "#6fd0ff", "大範圍並把坦克沖開") },
            new Hero { Id = "penguin", Name = "小冰", Title = "企鵝少女", Color = "#9adfff", Special = Sp("freeze", "冰封彈", ShotKind.Freeze, 22, 64, "#cff6ff", "被打中會凍結一回合") },
            new Hero { Id = "glasses", Name = "光哉", Title = "眼鏡學長", Color = "#d8b98a", Special = Sp("laser", "光學雷射", ShotKind.Laser, 42, 40, "#ff5ad0", "直線飛行、不受風影響") },
            new Hero { Id = "tshirt", Name = "阿翔", Title = "T恤少年", Color = "#b0b0b0", Special = Sp("spread", "街頭散彈", ShotKind.Normal, 14, 36, "#ffffff", "五顆扇形散射", 5, 0.11) },
            new Hero { Id = "calico", Name = "小花", Title = "夾克三花貓", Color = "#f0a24a", Special = Sp("drill", "貓爪鑽地彈", ShotKind.Digger, 34, 56, "#ffb347", "鑽入地面挖出深坑") },
            new Hero { Id = "whitecat", Name = "書白", Title = "圖書館貓", Color = "#f4efe6", Special = Sp("heal", "治癒之書", ShotKind.Heal, 40, 0, "#8dff9a", "立刻回復 40 HP") },
            new Hero { Id = "redcat", Name = "緋音", Title = "紅髮貓耳少女", Color = "#e8413c", Special = Sp("cluster", "火焰集束彈", ShotKind.Cluster, 17, 40, "#ff6a3a", "最高點分裂成五顆") },
            new Hero { Id = "sailor", Name = "澪", Title = "水手服少女", Color = "#7fb3e6", Special = Sp("tele", "傳送彈", ShotKind.Teleport, 15, 32, "#c9a0ff", "把自己傳送到落點") },
        };

        public static readonly string[] TANK_COLORS = { "#39c6ff", "#ff6fa8", "#7ee05a", "#ffc83a" };

        // ── Tuning ──
        public const int FIELD_W = 2400, FIELD_H = 720, WATER_Y = 690;
        public const double GRAVITY = 520, SPEED_PER_POWER = 9.2, WIND_ACC = 9, MAX_WIND = 10, MAX_HP = 100, FUEL = 120, DRIVE_SPEED = 70, MAX_CLIMB = 3.2;
        public const double TURN_TIME = 25, TANK_R = 24, LASER_SPEED = 1500, FALL_SAFE = 50, CRATE_CHANCE = 0.45, AMMO_SHARE = 0.55;
        /// <summary>Max stock per weapon slot (standard is unlimited).</summary>
        public static readonly int[] AMMO_CAPS = { -1, 4, 3, 2 };
        /// <summary>Every N of your own turns you get a free shell of that slot.</summary>
        public static readonly (int slot, int every)[] AUTO_SUPPLY = { (1, 3), (2, 5) };

        /// <summary>Add ammo to slots (capped); returns the slots that actually went up.</summary>
        public static List<int> Resupply(int[] uses, IEnumerable<int> slots)
        {
            var got = new List<int>();
            foreach (var s in slots) if (uses[s] >= 0 && uses[s] < AMMO_CAPS[s]) { uses[s]++; got.Add(s); }
            return got;
        }

        /// <summary>Slots auto-refilled at the start of a tank's n-th own turn.</summary>
        public static List<int> AutoSupplySlots(int n) => AUTO_SUPPLY.Where(a => n > 0 && n % a.every == 0).Select(a => a.slot).ToList();

        public static readonly Dictionary<string, MapStyle> MAPS = new Dictionary<string, MapStyle>
        {
            ["grass"] = new MapStyle { Name = "草原戰場", Top = "#7ed957", Dirt = "#b07a4a", Deep = "#8a5a34", Sky0 = "#7fd3ff", Sky1 = "#dff4ff" },
            ["snow"] = new MapStyle { Name = "雪山戰場", Top = "#ffffff", Dirt = "#9fb8d8", Deep = "#7390b8", Sky0 = "#a9c8ff", Sky1 = "#f0f6ff" },
            ["desert"] = new MapStyle { Name = "沙漠戰場", Top = "#ffe08a", Dirt = "#e0a95a", Deep = "#bf8440", Sky0 = "#ffb27a", Sky1 = "#fff0c8" },
        };

        /// <summary>Small deterministic xorshift RNG (same bits as the TS version).</summary>
        public static Func<double> Rng(long seed)
        {
            uint s = (uint)seed;
            if (s == 0) s = 1;
            return () => { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s % 100000) / 100000.0; };
        }

        /// <summary>Heightmap: terrain[x] = surface y at column x (bigger = lower).</summary>
        public static double[] MakeTerrain(long seed)
        {
            var r = Rng(seed);
            var waves = new (double a, double f, double p)[5];
            for (int i = 0; i < 5; i++)
            {
                double a = (60 - i * 10) * (0.5 + r());
                double f = (0.8 + i * 1.7 + r()) / FIELD_W * Math.PI * 2;
                double p = r() * 6.28;
                waves[i] = (a, f, p);
            }
            var t = new double[FIELD_W + 1];
            for (int x = 0; x <= FIELD_W; x++)
            {
                double y = 470;
                foreach (var w in waves) y += w.a * Math.Sin(x * w.f + w.p);
                // Two valleys dip toward the water for drama.
                y += 120 * Math.Exp(-Math.Pow((x - FIELD_W * 0.35) / 90, 2)) + 110 * Math.Exp(-Math.Pow((x - FIELD_W * 0.7) / 80, 2));
                t[x] = Math.Max(260, Math.Min(WATER_Y + 20, Js.Round(y)));
            }
            return t;
        }

        public static double Surface(double[] t, double x) => t[(int)Math.Max(0, Math.Min(FIELD_W, Js.Round(x)))];

        /// <summary>Blast a round crater centred at (cx, cy). depth&gt;1 digs deeper (drill).</summary>
        public static void Carve(double[] t, double cx, double cy, double r, double depth = 1)
        {
            for (int x = (int)Math.Max(0, Math.Floor(cx - r)); x <= Math.Min(FIELD_W, Math.Ceiling(cx + r)); x++)
            {
                double dy = Math.Sqrt(Math.Max(0, r * r - (x - cx) * (x - cx))) * depth;
                double bottom = cy + dy;
                if (t[x] < bottom) t[x] = Math.Min(FIELD_H + 10, Js.Round(bottom));
            }
        }

        /// <summary>Spawn columns spread across the field on dry land.</summary>
        public static List<double> SpawnXs(double[] t, int n)
        {
            var xs = new List<double>();
            for (int i = 0; i < n; i++)
            {
                double x = Js.Round(FIELD_W * (0.1 + 0.8 * i / Math.Max(1, n - 1)));
                for (int k = 0; k < 200 && Surface(t, x) > WATER_Y - 40; k++) x += (k % 2 == 1 ? 1 : -1) * k * 4;
                xs.Add(Math.Max(40, Math.Min(FIELD_W - 40, x)));
            }
            return xs;
        }

        public static (double vx, double vy) LaunchVelocity(int facing, double elev, double power, ShotKind kind = ShotKind.Normal)
        {
            double s = kind == ShotKind.Laser ? LASER_SPEED : power * SPEED_PER_POWER, a = elev * Math.PI / 180;
            return (facing * Math.Cos(a) * s, -Math.Sin(a) * s);
        }

        /// <summary>Integrate one shell until it hits the ground, a tank (index in Hit), or leaves the field (-2).</summary>
        public static Impact Simulate(double[] t, double x, double y, double vx, double vy, double wind, IList<BodyPos> tanks, int shooter, bool laser = false, double dt = 1.0 / 120)
        {
            for (int i = 0; i < 2400; i++)
            {
                if (!laser) { vx += wind * WIND_ACC * dt; vy += GRAVITY * dt; }
                x += vx * dt; y += vy * dt;
                if (x < -60 || x > FIELD_W + 60 || y > FIELD_H + 40) return new Impact { X = x, Y = y, Hit = -2, Steps = i };
                if (x >= 0 && x <= FIELD_W && y >= Surface(t, x)) return new Impact { X = x, Y = y, Hit = -1, Steps = i };
                for (int k = 0; k < tanks.Count; k++)
                {
                    var b = tanks[k];
                    if (!b.Alive || (k == shooter && i < 40)) continue;
                    if (Js.Hypot(x - b.X, y - (b.Y - 18)) < TANK_R) return new Impact { X = x, Y = y, Hit = k, Steps = i };
                }
            }
            return new Impact { X = x, Y = y, Hit = -2, Steps = 2400 };
        }

        /// <summary>Blast damage — full at the centre, 40% at the rim, 0 outside.</summary>
        public static double BlastDamage(double dmg, double radius, double ex, double ey, double bx, double by, bool direct)
        {
            if (direct) return Js.Round(dmg * 1.2);
            double d = Js.Hypot(ex - bx, ey - (by - 18));
            if (d >= radius + TANK_R * 0.5) return 0;
            return Math.Max(3, Js.Round(dmg * (1 - Math.Min(1, d / (radius + TANK_R * 0.5)) * 0.6)));
        }

        public static double FallDamage(double drop) => drop <= FALL_SAFE ? 0 : Js.Round((drop - FALL_SAFE) / 4);

        /// <summary>Can a tank drive from x to nx without climbing a wall?</summary>
        public static bool CanDrive(double[] t, double x, double nx) => Surface(t, x) - Surface(t, nx) <= MAX_CLIMB && nx > 20 && nx < FIELD_W - 20;

        /// <summary>Search elevation × power for the shell that lands nearest the target (wind included).</summary>
        public static AiShot AiAim(double[] t, IList<BodyPos> tanks, int me, int target, double wind, double radius, bool laser = false)
        {
            var m = tanks[me];
            var tg = tanks[target];
            int facing = tg.X >= m.X ? 1 : -1;
            var best = new AiShot { Elev = 45, Power = 60, Facing = facing, Miss = double.PositiveInfinity };
            var powers = laser ? new[] { 100.0 } : Enumerable.Range(0, 30).Select(i => 25 + i * 2.6).ToArray();
            for (double elev = laser ? 0 : 12; elev <= 84; elev += laser ? 1 : 3)
            {
                foreach (var power in powers)
                {
                    var v = LaunchVelocity(facing, elev, power, laser ? ShotKind.Laser : ShotKind.Normal);
                    double mx = m.X + facing * 16, my = m.Y - 40;
                    var imp = Simulate(t, mx, my, v.vx, v.vy, wind, tanks, me, laser, 1.0 / 60);
                    double miss = imp.Hit == target ? 0 : Js.Hypot(imp.X - tg.X, imp.Y - (tg.Y - 18));
                    if (imp.Hit == -2) miss += 800;
                    if (Js.Hypot(imp.X - m.X, imp.Y - m.Y) < radius + 30) miss += 1000; // never shell yourself
                    if (miss < best.Miss) best = new AiShot { Elev = elev, Power = power, Facing = facing, Miss = miss };
                }
            }
            return best;
        }

        /// <summary>Final standings: survivors first (by HP), then the most recently eliminated.</summary>
        public static List<T> Standings<T>(IEnumerable<T> ps, Func<T, bool> alive, Func<T, double> hp, Func<T, int> diedAt)
            => Js.Sorted(ps, (a, b) =>
            {
                if (alive(a) != alive(b)) return alive(b) ? 1 : -1;
                return alive(a) ? hp(b).CompareTo(hp(a)) : diedAt(b) - diedAt(a);
            });
    }

    // ── battle (scenes/play.ts) ──

    public class Tank
    {
        public int I;
        public Hero Hero;
        public bool You;
        public string Color;
        public double X, Y, Hp, Elev = 45, Power, LastPower, Fuel, FallFrom, Dmg, Flash;
        public bool Alive = true, Frozen;
        public int Facing, Weapon, DiedAt, Turns;
        public Weapon[] Weapons;
        public int[] Uses;
    }

    public class Shell { public double X, Y, Vx, Vy, Age; public Weapon W; public Tank Owner; public bool Split; public List<(double x, double y)> Trail = new List<(double, double)>(); }

    public class Crate { public double X, Y, Vy; public bool Landed; public string Kind; }

    public class Result { public string Id, Name, Color; public bool You, Alive; public double Hp, Dmg; }

    public enum Phase { Intro, Aim, Flight, Settle, Over }

    public struct TankInput { public bool Left, Right, Up, Down, FirePressed, FireHeld, PanL, PanR; public int Pick; /* 1..4, 0 = none */ }

    public class Battle
    {
        public double[] Terrain;
        public string Map = "grass";
        public readonly List<Tank> Tanks = new List<Tank>();
        public int Cur, Turn = 1;
        public double Wind, Timer, Clock = Rules.TURN_TIME;
        public Phase Phase = Phase.Intro;
        public readonly List<Shell> Shells = new List<Shell>();
        public List<Crate> Crates = new List<Crate>();
        public bool Charging;
        public double Look;
        public string Msg = "";
        public double MsgT;
        public string Reason = "";
        public List<Result> Results = new List<Result>();
        /// <summary>Incremented whenever the terrain changes (the view rebuilds its mesh).</summary>
        public int TerrainVersion;
        (double think, double elev, double power, int facing, int weapon, bool charging)? plan;
        readonly Dictionary<string, int> ranging = new Dictionary<string, int>();
        readonly Func<double> rng;

        public event Action<string> Sound;                                  // fire boom hurt heal freeze splash tick pick
        public event Action<double, double, string, string> Float;          // x, y, text, colour
        public event Action<double, double, string[], int> Burst;           // x, y, colours, count
        public event Action<float> Shake;

        public Battle(string hero, Func<double> rng = null)
        {
            this.rng = rng ?? Rand.Default;
            long seed = 1 + (long)Math.Floor(this.rng() * 1e6);
            Terrain = Rules.MakeTerrain(seed);
            Map = new[] { "grass", "snow", "desert" }[seed % 3];
            var me = Rules.HEROES.FirstOrDefault(h => h.Id == hero) ?? Rules.HEROES[0];
            var rivals = Rules.HEROES.Where(h => h != me).ToList();
            Rand.Shuffle(rivals, this.rng);
            var heroes = new[] { me }.Concat(rivals.Take(3)).ToList();
            var xs = Rules.SpawnXs(Terrain, 4);
            var order = new List<int> { 0, 1, 2, 3 };
            Rand.Shuffle(order, this.rng); // random spawn columns
            for (int i = 0; i < heroes.Count; i++)
            {
                double x = xs[order[i]];
                var weapons = Rules.BASE_WEAPONS.Concat(new[] { heroes[i].Special }).ToArray();
                Tanks.Add(new Tank
                {
                    I = i, Hero = heroes[i], You = i == 0, Color = Rules.TANK_COLORS[i], X = x, Y = Rules.Surface(Terrain, x), Hp = Rules.MAX_HP,
                    Facing = x < Rules.FIELD_W / 2.0 ? 1 : -1, Fuel = Rules.FUEL, Weapons = weapons, Uses = weapons.Select(w => w.Uses).ToArray(),
                });
            }
            Cur = 0;
            NewWind();
            Phase = Phase.Intro;
            Timer = 2;
        }

        public Tank Tk => Tanks[Cur];
        void Say(string s, double t = 2.5) { Msg = s; MsgT = t; }
        void DoFloat(double x, double y, string text, string color) => Float?.Invoke(x, y, text, color);
        void NewWind() => Wind = Js.Round((rng() * 2 - 1) * Rules.MAX_WIND);

        // ── turn flow ──
        void StartTurn()
        {
            var t = Tk;
            t.Fuel = Rules.FUEL; t.Power = 0; t.Turns++;
            var auto = Rules.Resupply(t.Uses, Rules.AutoSupplySlots(t.Turns));
            if (auto.Count > 0) { DoFloat(t.X, t.Y - 130, "定期補給：" + string.Join("、", auto.Select(s => t.Weapons[s].Name + " +1")), "#ffe066"); Sound?.Invoke("pick"); }
            Clock = Rules.TURN_TIME; Charging = false; Look = 0;
            if (t.Uses[t.Weapon] == 0) t.Weapon = 0;
            Phase = Phase.Aim;
            plan = null;
            if (!t.You) PlanAi(t);
            Say(t.You ? "輪到你了！按住 Space 蓄力，放開發射" : $"{t.Hero.Name} 的回合", 2);
            if (rng() < Rules.CRATE_CHANCE) Crates.Add(new Crate { X = 150 + rng() * (Rules.FIELD_W - 300), Y = -40, Kind = rng() < Rules.AMMO_SHARE ? "ammo" : "heal" });
        }

        void NextTurn()
        {
            var alive = Tanks.Where(t => t.Alive).ToList();
            if (!Tanks[0].Alive) { Finish("你的坦克被擊毀了……"); return; }
            if (alive.Count <= 1) { Finish("最後的坦克就是你！"); return; }
            if (Turn >= 48) { Finish("回合用完，依剩餘血量排名"); return; }
            Turn++;
            NewWind();
            for (int k = 0; k < 8; k++)
            {
                Cur = (Cur + 1) % Tanks.Count;
                var t = Tk;
                if (!t.Alive) continue;
                if (t.Frozen) { t.Frozen = false; DoFloat(t.X, t.Y - 110, "凍結中，跳過！", "#bff4ff"); continue; }
                break;
            }
            StartTurn();
        }

        void Finish(string reason)
        {
            Phase = Phase.Over; Timer = 2.6;
            Reason = reason;
            Results = Rules.Standings(Tanks, t => t.Alive, t => t.Hp, t => t.DiedAt)
                .Select(t => new Result { Id = t.Hero.Id, Name = t.Hero.Name, You = t.You, Alive = t.Alive, Hp = Math.Max(0, t.Hp), Dmg = t.Dmg, Color = t.Color }).ToList();
        }

        // ── firing ──
        void Fire(Tank t)
        {
            var w = t.Weapons[t.Weapon];
            if (t.Uses[t.Weapon] == 0) { t.Weapon = 0; Fire(t); return; }
            if (t.Uses[t.Weapon] > 0) t.Uses[t.Weapon]--;
            t.LastPower = t.Power;
            Charging = false; Look = 0;
            if (w.Kind == ShotKind.Heal)
            {
                double before = t.Hp;
                t.Hp = Math.Min(Rules.MAX_HP, t.Hp + w.Dmg);
                DoFloat(t.X, t.Y - 110, $"+{t.Hp - before} HP", "#8dff9a");
                Burst?.Invoke(t.X, t.Y - 40, new[] { "#8dff9a", "#ffffff" }, 30);
                Sound?.Invoke("heal");
                Phase = Phase.Settle; Timer = 1.0;
                return;
            }
            double a = t.Elev * Math.PI / 180;
            double mx = t.X + t.Facing * Math.Cos(a) * 34, my = t.Y - 34 - Math.Sin(a) * 34;
            for (int k = 0; k < w.Count; k++)
            {
                double off = w.Count > 1 ? (k - (w.Count - 1) / 2.0) * w.Spread * (180 / Math.PI) : 0;
                var v = Rules.LaunchVelocity(t.Facing, t.Elev + off, t.Power, w.Kind);
                Shells.Add(new Shell { X = mx, Y = my, Vx = v.vx, Vy = v.vy, W = w, Owner = t });
            }
            Sound?.Invoke("fire");
            Burst?.Invoke(mx, my, new[] { "#ffffff", "#ffe08a", "#bbbbbb" }, 14);
            Phase = Phase.Flight;
        }

        void Explode(Shell s, double x, double y, int hit)
        {
            var w = s.W;
            double r = w.Radius;
            Rules.Carve(Terrain, x, y, w.Kind == ShotKind.Digger ? r * 0.8 : r * 0.75, w.Kind == ShotKind.Digger ? 2.4 : 1);
            TerrainVersion++;
            foreach (var t in Tanks)
            {
                if (!t.Alive) continue;
                double dmg = Rules.BlastDamage(w.Dmg, w.Radius, x, y, t.X, t.Y, hit == t.I);
                if (dmg <= 0) continue;
                t.Hp -= dmg; t.Flash = 0.25;
                if (t != s.Owner) s.Owner.Dmg += dmg;
                DoFloat(t.X, t.Y - 100, $"-{dmg}", "#ff5a5a");
                if (t.You) Sound?.Invoke("hurt");
                if (w.Kind == ShotKind.Freeze) { t.Frozen = true; DoFloat(t.X, t.Y - 130, "凍結！", "#bff4ff"); Sound?.Invoke("freeze"); }
                if (w.Kind == ShotKind.Wave)
                {
                    double dx = t.X - x;
                    if (dx == 0) dx = 1;
                    double push = (1 - Math.Min(1, Math.Abs(dx) / (r + 40))) * 130 * Math.Sign(dx);
                    t.X = Js.Clamp(t.X + push, 20, Rules.FIELD_W - 20);
                }
            }
            if (w.Kind == ShotKind.Teleport && s.Owner.Alive) { s.Owner.X = Js.Clamp(x, 30, Rules.FIELD_W - 30); s.Owner.Y = Math.Min(s.Owner.Y, y); DoFloat(x, y - 60, "傳送！", "#c9a0ff"); }
            foreach (var c in Crates) if (Js.Hypot(c.X - x, c.Y - y) < r + 20) PickCrate(c, s.Owner);
            Crates = Crates.Where(c => c.Y > -1000).ToList();
            var colors = w.Kind == ShotKind.Freeze ? new[] { "#ffffff", "#bff4ff", "#6fd0ff" } : w.Kind == ShotKind.Wave ? new[] { "#ffffff", "#6fd0ff", "#2a7fff" } : new[] { "#ffffff", "#ffe08a", w.Color, "#ff7a3a" };
            Burst?.Invoke(x, y, colors, 20 + (int)Js.Round(r / 2));
            Burst?.Invoke(x, y, new[] { Rules.MAPS[Map].Dirt, Rules.MAPS[Map].Top }, 18);
            Sound?.Invoke("boom");
            Shake?.Invoke((float)Math.Min(10, r / 8));
        }

        void PickCrate(Crate c, Tank t)
        {
            if (!t.Alive) return;
            if (c.Kind == "ammo")
            {
                var got = Rules.Resupply(t.Uses, new[] { 1, 2, 3 });
                DoFloat(c.X, c.Y - 40, got.Count > 0 ? $"{t.Hero.Name} 彈藥補給：{string.Join("、", got.Select(s => t.Weapons[s].Name))} +1" : $"{t.Hero.Name} 彈藥已滿", "#ffe066");
            }
            else
            {
                double before = t.Hp;
                t.Hp = Math.Min(Rules.MAX_HP, t.Hp + 30);
                DoFloat(c.X, c.Y - 40, $"{t.Hero.Name} +{t.Hp - before} HP", "#8dff9a");
            }
            Sound?.Invoke("pick");
            c.Y = -5000;
        }

        List<BodyPos> Bodies() => Tanks.Select(t => new BodyPos { X = t.X, Y = t.Y, Alive = t.Alive }).ToList();

        // ── AI ──
        void PlanAi(Tank t)
        {
            var foes = Tanks.Where(o => o.Alive && o != t).ToList();
            double Score(Tank a) => Math.Abs(a.X - t.X) - (a.You ? 250 : 0) + a.Hp * 3;
            var target = Js.Sorted(foes, (a, b) => Score(a).CompareTo(Score(b)))[0];
            int weapon = 0;
            var sp = t.Weapons[3];
            if (t.Uses[3] > 0 && ((sp.Kind == ShotKind.Heal && t.Hp < 60) || (sp.Kind != ShotKind.Heal && sp.Kind != ShotKind.Teleport && rng() < 0.4))) weapon = 3;
            else if (t.Uses[2] > 0 && target.Hp > 40 && rng() < 0.45) weapon = 2;
            else if (t.Uses[1] > 0 && rng() < 0.35) weapon = 1;
            var w = t.Weapons[weapon];
            if (w.Kind == ShotKind.Heal) { plan = (1.1, t.Elev, 0, t.Facing, weapon, false); return; }
            var aim = Rules.AiAim(Terrain, Bodies(), t.I, target.I, Wind, w.Radius, w.Kind == ShotKind.Laser);
            // Bots "range in": the first shot at a target is loose, each follow-up tightens (never perfect).
            string key = $"{t.I}>{target.I}";
            ranging.TryGetValue(key, out int n);
            ranging[key] = n + 1;
            double err = Math.Max(0.35, 1.3 - n * 0.35) * (target.You ? 1.15 : 1);
            plan = (1.1 + rng() * 0.6, Js.Clamp(aim.Elev + (rng() - 0.5) * 6 * err, 0, 88),
                Js.Clamp(aim.Power + (rng() < 0.5 ? -1 : 1) * (2 + rng() * 6) * err, 5, 100), aim.Facing, weapon, false);
        }

        void RunAi(Tank t, double dt)
        {
            if (!plan.HasValue) return;
            var p = plan.Value;
            if (p.think > 0) { p.think -= dt; plan = p; return; }
            t.Weapon = p.weapon; t.Facing = p.facing;
            if (t.Weapons[t.Weapon].Kind == ShotKind.Heal) { Fire(t); return; }
            if (Math.Abs(t.Elev - p.elev) > 0.8) { t.Elev += Math.Sign(p.elev - t.Elev) * Math.Min(Math.Abs(p.elev - t.Elev), 50 * dt); return; }
            t.Elev = p.elev;
            if (t.Weapons[t.Weapon].Kind == ShotKind.Laser) { t.Power = 100; Fire(t); return; }
            p.charging = true;
            plan = p;
            t.Power = Math.Min(p.power, t.Power + 62 * dt);
            if (t.Power >= p.power) Fire(t);
        }

        // ── update ──

        /// <summary>Advance one frame; returns true when the post-battle delay has elapsed.</summary>
        public bool Update(double dt, TankInput k)
        {
            dt = Math.Min(dt, 1.0 / 30);
            Timer -= dt; MsgT -= dt;
            foreach (var t0 in Tanks) if (t0.Flash > 0) t0.Flash -= dt;
            UpdateCrates(dt);
            var t = Tk;
            switch (Phase)
            {
                case Phase.Intro: if (Timer <= 0) StartTurn(); break;
                case Phase.Aim:
                    Clock -= dt;
                    if (t.You) Control(t, dt, k); else RunAi(t, dt);
                    if (Clock <= 0 && Phase == Phase.Aim)
                    {
                        if (t.You && Charging) Fire(t);
                        else { Say($"{t.Hero.Name} 時間到，跳過回合"); Phase = Phase.Settle; Timer = 0.5; }
                    }
                    break;
                case Phase.Flight: UpdateShells(dt); if (Shells.Count == 0) { Phase = Phase.Settle; Timer = 0.9; } break;
                case Phase.Settle: if (Settle(dt) && Timer <= 0) { CheckDeaths(); if (Phase == Phase.Settle) NextTurn(); } break;
                case Phase.Over: Settle(dt); if (Timer <= 0) return true; break;
            }
            return false;
        }

        void Control(Tank t, double dt, TankInput k)
        {
            if (k.Pick >= 1 && k.Pick <= 4) Pick(t, k.Pick - 1);
            if (k.PanL) Look -= 700 * dt;
            if (k.PanR) Look += 700 * dt;
            Look = Js.Clamp(Look, -Rules.FIELD_W, Rules.FIELD_W);
            if (!Charging)
            {
                int dir = (k.Right ? 1 : 0) - (k.Left ? 1 : 0);
                if (dir != 0)
                {
                    t.Facing = dir; Look = 0;
                    if (t.Fuel > 0)
                    {
                        double nx = t.X + dir * Rules.DRIVE_SPEED * dt;
                        if (Rules.CanDrive(Terrain, t.X, nx)) { t.X = nx; t.Y = Rules.Surface(Terrain, t.X); t.Fuel = Math.Max(0, t.Fuel - Rules.DRIVE_SPEED * dt); }
                    }
                }
                if (k.Up) t.Elev = Math.Min(88, t.Elev + 40 * dt);
                if (k.Down) t.Elev = Math.Max(0, t.Elev - 40 * dt);
            }
            if (k.FirePressed) { Charging = true; t.Power = 0; }
            if (Charging && k.FireHeld) t.Power = Math.Min(100, t.Power + 62 * dt);
            if (Charging && !k.FireHeld) Fire(t);
        }

        public void Pick(Tank t, int i)
        {
            if (t.Uses[i] == 0) { Say("這個武器用完了！", 1.2); return; }
            t.Weapon = i; Sound?.Invoke("tick");
        }

        void UpdateShells(double dt)
        {
            const int sub = 4;
            double h = dt / sub;
            for (int n = 0; n < sub; n++)
            {
                for (int i = Shells.Count - 1; i >= 0; i--)
                {
                    if (i >= Shells.Count) continue;
                    var s = Shells[i];
                    s.Age += h;
                    if (s.W.Kind != ShotKind.Laser) { s.Vx += Wind * Rules.WIND_ACC * h; s.Vy += Rules.GRAVITY * h; }
                    s.X += s.Vx * h; s.Y += s.Vy * h;
                    if (s.W.Kind == ShotKind.Cluster && !s.Split && s.Vy > 0)
                    {
                        s.Split = true;
                        for (int c = 0; c < 5; c++)
                            Shells.Add(new Shell { X = s.X, Y = s.Y, Vx = s.Vx + (c - 2) * 70, Vy = -60 + Math.Abs(c - 2) * 20, W = s.W.With(ShotKind.Normal), Owner = s.Owner, Split = true });
                        Shells.RemoveAt(i);
                        Burst?.Invoke(s.X, s.Y, new[] { "#ff6a3a", "#ffe08a" }, 16);
                        continue;
                    }
                    int hit = -3;
                    if (s.X < -60 || s.X > Rules.FIELD_W + 60 || s.Y > Rules.FIELD_H + 40) hit = -2;
                    else if (s.X >= 0 && s.X <= Rules.FIELD_W && s.Y >= Rules.Surface(Terrain, s.X)) hit = -1;
                    else
                        foreach (var t in Tanks)
                            if (t.Alive && !(t == s.Owner && s.Age < 0.35) && Js.Hypot(s.X - t.X, s.Y - (t.Y - 18)) < Rules.TANK_R) { hit = t.I; break; }
                    if (hit == -2)
                    {
                        if (s.Y > Rules.WATER_Y) { Sound?.Invoke("splash"); Burst?.Invoke(s.X, Rules.WATER_Y, new[] { "#ffffff", "#6fd0ff" }, 16); }
                        Shells.RemoveAt(i);
                        continue;
                    }
                    if (hit >= -1) { Explode(s, s.X, s.Y, hit); Shells.RemoveAt(i); }
                }
            }
            foreach (var s in Shells) { s.Trail.Add((s.X, s.Y)); if (s.Trail.Count > 14) s.Trail.RemoveAt(0); }
        }

        void UpdateCrates(double dt)
        {
            foreach (var c in Crates.ToList())
            {
                if (c.Landed) c.Y = Rules.Surface(Terrain, c.X);
                else
                {
                    c.Vy = Math.Min(300, c.Vy + 400 * dt); c.Y += c.Vy * dt;
                    if (c.Y >= Rules.Surface(Terrain, c.X)) { c.Y = Rules.Surface(Terrain, c.X); c.Landed = true; }
                }
                if (c.Y > Rules.WATER_Y - 4) c.Y = -5000;
                foreach (var t in Tanks) if (t.Alive && c.Y > 0 && Math.Abs(t.X - c.X) < 34 && Math.Abs(t.Y - c.Y) < 40) PickCrate(c, t);
            }
            Crates = Crates.Where(c => c.Y > -1000).ToList();
        }

        /// <summary>Drop tanks onto the (possibly blasted) ground; true when everyone has landed.</summary>
        bool Settle(double dt)
        {
            bool done = true;
            foreach (var t in Tanks)
            {
                if (!t.Alive) continue;
                double g = Rules.Surface(Terrain, t.X);
                if (t.Y < g - 0.5)
                {
                    if (t.FallFrom == 0) t.FallFrom = t.Y;
                    t.Y = Math.Min(g, t.Y + 360 * dt); done = false;
                }
                else
                {
                    t.Y = g;
                    if (t.FallFrom != 0)
                    {
                        double dmg = Rules.FallDamage(g - t.FallFrom);
                        if (dmg > 0) { t.Hp -= dmg; DoFloat(t.X, t.Y - 100, $"摔落 -{dmg}", "#ffb06a"); }
                        t.FallFrom = 0;
                    }
                }
            }
            return done;
        }

        void CheckDeaths()
        {
            foreach (var t in Tanks)
            {
                if (!t.Alive) continue;
                bool drowned = t.Y >= Rules.WATER_Y - 4;
                if (t.Hp > 0 && !drowned) continue;
                t.Alive = false; t.Hp = Math.Max(0, t.Hp); t.DiedAt = Turn;
                Say(drowned ? $"{t.Hero.Name} 掉進水裡了！" : $"{t.Hero.Name} 的坦克被擊毀！", 2.5);
                Burst?.Invoke(t.X, t.Y - 20, new[] { "#ffffff", "#ffe08a", "#ff7a3a", t.Color }, 50);
                Sound?.Invoke(drowned ? "splash" : "boom");
                Shake?.Invoke(10);
            }
        }
    }
}
