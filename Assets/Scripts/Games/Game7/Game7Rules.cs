// 萌友大亂鬥 — data + arena helpers ported 1:1 from Game7/cloud/src/data.ts, and the match
// simulation from scenes/arena.ts (8-player free-for-all, bot AI, weapons, pickups) without drawing.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MoeGames.Game7
{
    public enum Kind { Bubble, Smg, Sniper, Shotgun, Boomerang, Book, Fire3, Grenade }

    public class Weapon { public string Name; public Kind Kind; public double Dmg, Rate, Speed, Range, R, Spread; public int Pellets; public string Color; public double Pref; }

    public class Fighter { public string Id, Name, Title, Color; public Weapon Weapon; }

    public static class Data
    {
        public static readonly Fighter[] FIGHTERS =
        {
            new Fighter { Id = "whale", Name = "汐音", Title = "鯨魚女僕", Color = "#4f8dff", Weapon = new Weapon { Name = "泡泡大砲", Kind = Kind.Bubble, Dmg = 22, Rate = 0.42, Speed = 380, Range = 620, R = 14, Spread = 0.04, Pellets = 1, Color = "#7fd8ff", Pref = 330 } },
            new Fighter { Id = "penguin", Name = "小冰", Title = "企鵝少女", Color = "#9adfff", Weapon = new Weapon { Name = "冰晶衝鋒槍", Kind = Kind.Smg, Dmg = 7, Rate = 0.085, Speed = 820, Range = 560, R = 5, Spread = 0.12, Pellets = 1, Color = "#dff7ff", Pref = 260 } },
            new Fighter { Id = "glasses", Name = "光哉", Title = "眼鏡學長", Color = "#d8b98a", Weapon = new Weapon { Name = "光學狙擊槍", Kind = Kind.Sniper, Dmg = 48, Rate = 1.05, Speed = 1700, Range = 1100, R = 5, Spread = 0.01, Pellets = 1, Color = "#ffe27a", Pref = 560 } },
            new Fighter { Id = "tshirt", Name = "阿翔", Title = "T恤少年", Color = "#b0b0b0", Weapon = new Weapon { Name = "街頭散彈", Kind = Kind.Shotgun, Dmg = 9, Rate = 0.75, Speed = 760, Range = 380, R = 5, Spread = 0.32, Pellets = 6, Color = "#ffffff", Pref = 170 } },
            new Fighter { Id = "calico", Name = "小花", Title = "夾克三花貓", Color = "#f0a24a", Weapon = new Weapon { Name = "貓爪迴力鏢", Kind = Kind.Boomerang, Dmg = 20, Rate = 0.55, Speed = 640, Range = 440, R = 12, Spread = 0, Pellets = 1, Color = "#ffb347", Pref = 240 } },
            new Fighter { Id = "whitecat", Name = "書白", Title = "圖書館貓", Color = "#f4efe6", Weapon = new Weapon { Name = "魔法書頁", Kind = Kind.Book, Dmg = 13, Rate = 0.22, Speed = 620, Range = 640, R = 8, Spread = 0.05, Pellets = 1, Color = "#f2e2b6", Pref = 340 } },
            new Fighter { Id = "redcat", Name = "緋音", Title = "紅髮貓耳少女", Color = "#e8413c", Weapon = new Weapon { Name = "火焰三連", Kind = Kind.Fire3, Dmg = 11, Rate = 0.42, Speed = 600, Range = 480, R = 8, Spread = 0.22, Pellets = 3, Color = "#ff7a3a", Pref = 250 } },
            new Fighter { Id = "sailor", Name = "澪", Title = "水手服少女", Color = "#7fb3e6", Weapon = new Weapon { Name = "旋風手雷", Kind = Kind.Grenade, Dmg = 38, Rate = 1.0, Speed = 520, Range = 520, R = 10, Spread = 0, Pellets = 1, Color = "#c8f0ff", Pref = 330 } },
        };

        // ── ARENA ──
        public const double ARENA_W = 2400, ARENA_H = 1600;
        /// <summary>Crates (solid) as [x, y, w, h].</summary>
        public static readonly double[][] CRATES =
        {
            new double[] { 560, 360, 96, 96 }, new double[] { 656, 360, 96, 96 }, new double[] { 1152, 700, 96, 96 }, new double[] { 1152, 796, 96, 96 }, new double[] { 1740, 360, 96, 96 }, new double[] { 1740, 456, 96, 96 },
            new double[] { 400, 1080, 96, 96 }, new double[] { 496, 1080, 96, 96 }, new double[] { 1850, 1100, 96, 96 }, new double[] { 1946, 1100, 96, 96 }, new double[] { 960, 300, 96, 96 }, new double[] { 1350, 1250, 96, 96 },
            new double[] { 800, 760, 96, 96 }, new double[] { 1500, 760, 96, 96 }, new double[] { 1152, 180, 96, 96 }, new double[] { 1152, 1320, 96, 96 },
        };
        public static readonly (double x, double y)[] BUSHES = { (300, 300), (2100, 300), (300, 1300), (2100, 1300), (1200, 520), (1200, 1080), (760, 1300), (1640, 300) };
        public static readonly (double x, double y)[] SPAWNS = { (200, 200), (2200, 200), (200, 1400), (2200, 1400), (1200, 120), (1200, 1480), (120, 800), (2280, 800), (700, 800), (1700, 800) };
        public static readonly (double x, double y, string kind)[] PICKUP_SPOTS =
        {
            (1200, 640, "bolt"), (1200, 960, "heart"), (420, 800, "heart"), (1980, 800, "heart"), (900, 1200, "bolt"), (1500, 400, "bolt"),
        };
        public const double MAX_HP = 100;
        public const double SPEED = 260;
        public const double RADIUS = 22;
        public const double DASH_SPEED = 820, DASH_TIME = 0.16, DASH_CD = 1.8;
        public const double RESPAWN = 3;
        public const double SHIELD = 2;
        public const int KILL_TARGET = 10;
        public const double MATCH_TIME = 180;
        public const double PICKUP_RESPAWN = 12;
        public const double POWER_TIME = 8;

        /// <summary>Circle vs axis-aligned box: returns the push-out vector or null.</summary>
        public static (double x, double y)? CircleBox(double cx, double cy, double r, double[] b)
        {
            double nx = Math.Max(b[0], Math.Min(cx, b[0] + b[2])), ny = Math.Max(b[1], Math.Min(cy, b[1] + b[3]));
            double dx = cx - nx, dy = cy - ny, d2 = dx * dx + dy * dy;
            if (d2 >= r * r) return null;
            if (d2 == 0)
            {
                // centre inside: push out along the shallowest axis
                double l = cx - b[0], rr = b[0] + b[2] - cx, t = cy - b[1], bb = b[1] + b[3] - cy;
                double m = Math.Min(Math.Min(l, rr), Math.Min(t, bb));
                return m == l ? (-(l + r), 0) : m == rr ? (rr + r, 0) : m == t ? (0, -(t + r)) : (0, bb + r);
            }
            double d = Math.Sqrt(d2);
            return (dx / d * (r - d), dy / d * (r - d));
        }

        /// <summary>Does the segment a→b cross any crate? (line of sight)</summary>
        public static bool Blocked(double ax, double ay, double bx, double by)
        {
            int steps = (int)Math.Ceiling(Js.Hypot(bx - ax, by - ay) / 24);
            for (int i = 1; i < steps; i++)
            {
                double x = ax + (bx - ax) * i / steps, y = ay + (by - ay) * i / steps;
                foreach (var c in CRATES) if (x > c[0] && x < c[0] + c[2] && y > c[1] && y < c[1] + c[3]) return true;
            }
            return false;
        }

        /// <summary>Scoreboard order: kills desc, then deaths asc.</summary>
        public static List<T> Ranking<T>(IEnumerable<T> ps, Func<T, int> kills, Func<T, int> deaths)
            => Js.Sorted(ps, (a, b) => kills(b) != kills(a) ? kills(b) - kills(a) : deaths(a) - deaths(b));
    }

    public class P
    {
        public Fighter F;
        public bool You;
        public double X, Y, Vx, Vy, Aim;
        public double Hp, Dead, Shield, Cd, Dash, DashCd, DashX, DashY, Power, Flash;
        public int Kills, Deaths;
        // bot brain
        public P Target;
        public double Think, GoalX, GoalY;
        public int Strafe;
        public P LastHit;
    }

    public class Shot { public P Owner; public double X, Y, Vx, Vy, R, Dmg, Life, T; public Kind Kind; public string Color; public HashSet<P> Hits = new HashSet<P>(); public bool Back; }

    public class Pickup { public double X, Y, Wait; public string Kind; }

    public class FeedItem { public P A, B; public double T; }

    public struct PlayerInput { public double Dx, Dy; public double Aim; public bool Fire, DashPressed; }

    public class Stat { public string Id; public int Kills, Deaths; public bool You; }

    public class ArenaSim
    {
        public readonly List<P> Ps = new List<P>();
        public readonly P Me;
        public readonly List<Shot> Shots = new List<Shot>();
        public readonly List<Pickup> Pickups;
        public List<FeedItem> Feed = new List<FeedItem>();
        public double Clock = Data.MATCH_TIME;
        public double Over = -1;
        public double T;
        public string Reason = "";
        public List<Stat> Stats = new List<Stat>();
        readonly Func<double> rng;

        public event Action<string> Sound;                       // pew boom hurt ko pick dash
        public event Action<double, double, string, int> Burst;  // x, y, colour, count
        public event Action<float> Shake;

        public ArenaSim(string hero, Func<double> rng = null)
        {
            this.rng = rng ?? Rand.Default;
            var order = new List<Fighter> { Data.FIGHTERS.FirstOrDefault(f => f.Id == hero) ?? Data.FIGHTERS[0] };
            order.AddRange(Data.FIGHTERS.Where(f => f.Id != hero));
            for (int i = 0; i < order.Count; i++)
            {
                var s = Data.SPAWNS[i % Data.SPAWNS.Length];
                Ps.Add(new P
                {
                    F = order[i], You = i == 0, X = s.x, Y = s.y, Hp = Data.MAX_HP, Shield = Data.SHIELD, Think = this.rng(),
                    GoalX = s.x, GoalY = s.y, Strafe = this.rng() < 0.5 ? 1 : -1,
                });
            }
            Me = Ps[0];
            Pickups = Data.PICKUP_SPOTS.Select(p => new Pickup { X = p.x, Y = p.y, Kind = p.kind }).ToList();
        }

        public List<P> Ranking() => Data.Ranking(Ps, p => p.Kills, p => p.Deaths);

        // ── movement & collisions ──

        static void Collide(P p, double r)
        {
            foreach (var c in Data.CRATES)
            {
                var push = Data.CircleBox(p.X, p.Y, r, c);
                if (push.HasValue) { p.X += push.Value.x; p.Y += push.Value.y; }
            }
            p.X = Js.Clamp(p.X, r, Data.ARENA_W - r);
            p.Y = Js.Clamp(p.Y, r, Data.ARENA_H - r);
        }

        (double x, double y) RespawnPoint()
        {
            var best = Data.SPAWNS[0];
            double bd = -1;
            foreach (var s in Data.SPAWNS)
            {
                double m = double.PositiveInfinity;
                foreach (var p in Ps) if (p.Dead <= 0) m = Math.Min(m, Js.Hypot(p.X - s.x, p.Y - s.y));
                m += rng() * 200;
                if (m > bd) { bd = m; best = s; }
            }
            return best;
        }

        void Fire(P p)
        {
            var w = p.F.Weapon;
            if (p.Cd > 0 || p.Dead > 0) return;
            p.Cd = w.Rate * (p.Power > 0 ? 0.6 : 1);
            p.Shield = 0;
            double mx = p.X + Math.Cos(p.Aim) * 30, my = p.Y + Math.Sin(p.Aim) * 30;
            double dmg = w.Dmg * (p.Power > 0 ? 1.5 : 1);
            for (int i = 0; i < w.Pellets; i++)
            {
                double a = w.Pellets > 1
                    ? p.Aim + ((double)i / (w.Pellets - 1) - 0.5) * w.Spread * 2 + (w.Kind == Kind.Shotgun ? (rng() - 0.5) * 0.1 : 0)
                    : p.Aim + (rng() - 0.5) * w.Spread;
                Shots.Add(new Shot { Owner = p, X = mx, Y = my, Vx = Math.Cos(a) * w.Speed, Vy = Math.Sin(a) * w.Speed, R = w.R, Dmg = dmg, Life = w.Range / w.Speed, Kind = w.Kind, Color = w.Color });
            }
            if (p.You || Js.Hypot(p.X - Me.X, p.Y - Me.Y) < 700) Sound?.Invoke("pew");
        }

        void Damage(P v, double amt, P by)
        {
            if (v.Dead > 0 || v.Shield > 0 || v == by) return;
            v.Hp -= amt;
            v.Flash = 0.12;
            v.LastHit = by;
            if (v.You) { Sound?.Invoke("hurt"); Shake?.Invoke(0.15f); }
            if (v.Hp <= 0) Kill(v, by);
        }

        void Kill(P v, P by)
        {
            v.Hp = 0; v.Dead = Data.RESPAWN; v.Deaths++;
            if (by != null && by != v) { by.Kills++; if (by.You) Sound?.Invoke("ko"); }
            Feed.Insert(0, new FeedItem { A = by, B = v, T = 5 });
            if (Feed.Count > 5) Feed.RemoveAt(Feed.Count - 1);
            Burst?.Invoke(v.X, v.Y, v.F.Color, 30);
            Sound?.Invoke("boom");
            if (by != null && by.Kills >= Data.KILL_TARGET) EndMatch($"{by.F.Name} 先拿到 {Data.KILL_TARGET} 殺！");
        }

        void EndMatch(string reason)
        {
            if (Over >= 0) return;
            Over = 0;
            Reason = reason;
            Stats = Ranking().Select(p => new Stat { Id = p.F.Id, Kills = p.Kills, Deaths = p.Deaths, You = p.You }).ToList();
        }

        /// <summary>Advance one frame; returns true once the post-match delay has elapsed.</summary>
        public bool Update(double dt, PlayerInput input)
        {
            dt = Math.Min(dt, 1.0 / 30);
            T += dt;
            if (Over >= 0) { Over += dt; if (Over > 2.5) return true; }
            else
            {
                Clock -= dt;
                if (Clock <= 0) { Clock = 0; EndMatch("時間到！"); }
            }
            foreach (var f in Feed) f.T -= dt;
            Feed = Feed.Where(f => f.T > 0).ToList();
            foreach (var p in Ps)
            {
                if (p.Flash > 0) p.Flash -= dt;
                if (p.Cd > 0) p.Cd -= dt;
                if (p.DashCd > 0) p.DashCd -= dt;
                if (p.Power > 0) p.Power -= dt;
                if (p.Shield > 0) p.Shield -= dt;
                if (p.Dead > 0)
                {
                    p.Dead -= dt;
                    if (p.Dead <= 0) { var s = RespawnPoint(); p.X = s.x; p.Y = s.y; p.Hp = Data.MAX_HP; p.Shield = Data.SHIELD; p.Power = 0; }
                    continue;
                }
                if (Over >= 0) continue;
                if (p.You) ControlPlayer(p, input); else Bot(p, dt);
                if (p.Dash > 0) { p.Dash -= dt; p.Vx = p.DashX * Data.DASH_SPEED; p.Vy = p.DashY * Data.DASH_SPEED; }
                p.X += p.Vx * dt; p.Y += p.Vy * dt;
                Collide(p, Data.RADIUS);
            }
            // Players push apart.
            for (int i = 0; i < Ps.Count; i++)
                for (int j = i + 1; j < Ps.Count; j++)
                {
                    var a = Ps[i];
                    var b = Ps[j];
                    if (a.Dead > 0 || b.Dead > 0) continue;
                    double dx = b.X - a.X, dy = b.Y - a.Y, d = Js.Hypot(dx, dy);
                    if (d > 0 && d < Data.RADIUS * 2)
                    {
                        double k = (Data.RADIUS * 2 - d) / 2 / d;
                        a.X -= dx * k; a.Y -= dy * k; b.X += dx * k; b.Y += dy * k;
                    }
                }
            UpdateShots(dt);
            UpdatePickups(dt);
            return false;
        }

        void ControlPlayer(P p, PlayerInput k)
        {
            double dx = k.Dx, dy = k.Dy;
            double n = Js.Hypot(dx, dy);
            if (n == 0) n = 1;
            dx /= n; dy /= n;
            p.Vx = dx * Data.SPEED; p.Vy = dy * Data.SPEED;
            p.Aim = k.Aim;
            if (k.DashPressed && p.DashCd <= 0)
            {
                bool moving = dx != 0 || dy != 0;
                p.Dash = Data.DASH_TIME; p.DashCd = Data.DASH_CD;
                p.DashX = moving ? dx : Math.Cos(p.Aim);
                p.DashY = moving ? dy : Math.Sin(p.Aim);
                Sound?.Invoke("dash");
            }
            if (k.Fire) Fire(p);
        }

        void Bot(P p, double dt)
        {
            var w = p.F.Weapon;
            p.Think -= dt;
            if (p.Think <= 0)
            {
                p.Think = 0.25 + rng() * 0.2;
                // Pick the nearest visible enemy (prefer whoever just hit me).
                P best = null;
                double bd = double.PositiveInfinity;
                foreach (var o in Ps)
                {
                    if (o == p || o.Dead > 0) continue;
                    double d = Js.Hypot(o.X - p.X, o.Y - p.Y) - (o == p.LastHit ? 250 : 0) - (o.You ? 60 : 0);
                    if (d < bd && d < 900 && !Data.Blocked(p.X, p.Y, o.X, o.Y)) { bd = d; best = o; }
                }
                p.Target = best;
                if (best == null)
                {
                    // Wander toward a pickup (hearts when hurt) or a random spot.
                    var want = Pickups.Where(q => q.Wait <= 0 && (q.Kind == "bolt" || p.Hp < 70)).ToList();
                    var q0 = Js.Sorted(want, (a, b) => Js.Hypot(a.X - p.X, a.Y - p.Y).CompareTo(Js.Hypot(b.X - p.X, b.Y - p.Y))).FirstOrDefault();
                    if (q0 != null && rng() < 0.7) { p.GoalX = q0.X; p.GoalY = q0.Y; }
                    else if (Js.Hypot(p.GoalX - p.X, p.GoalY - p.Y) < 60 || rng() < 0.1) { p.GoalX = 150 + rng() * (Data.ARENA_W - 300); p.GoalY = 150 + rng() * (Data.ARENA_H - 300); }
                }
                if (rng() < 0.15) p.Strafe = -p.Strafe;
            }
            var t = p.Target;
            if (t != null && t.Dead <= 0)
            {
                double dx = t.X - p.X, dy = t.Y - p.Y, d = Js.Hypot(dx, dy);
                if (d == 0) d = 1;
                double lead = d / w.Speed;
                double ax = t.X + t.Vx * lead * 0.7, ay = t.Y + t.Vy * lead * 0.7;
                p.Aim = Math.Atan2(ay - p.Y, ax - p.X) + (rng() - 0.5) * 0.3;
                int fwd = d > w.Pref + 60 ? 1 : d < w.Pref - 60 ? -1 : 0;
                p.Vx = (dx / d * fwd + -dy / d * p.Strafe * 0.8) * Data.SPEED * 0.9;
                p.Vy = (dy / d * fwd + dx / d * p.Strafe * 0.8) * Data.SPEED * 0.9;
                if (d < w.Range * 0.9 && rng() < 0.8) Fire(p);
                if (p.Hp < 35 && p.DashCd <= 0 && rng() < 0.03) { p.Dash = Data.DASH_TIME; p.DashCd = Data.DASH_CD; p.DashX = -dx / d; p.DashY = -dy / d; }
            }
            else
            {
                double dx = p.GoalX - p.X, dy = p.GoalY - p.Y, d = Js.Hypot(dx, dy);
                if (d == 0) d = 1;
                p.Vx = dx / d * Data.SPEED * 0.8; p.Vy = dy / d * Data.SPEED * 0.8;
                p.Aim = Math.Atan2(dy, dx);
            }
            // Nudge around crates.
            foreach (var c in Data.CRATES)
            {
                double cx = c[0] + c[2] / 2, cy = c[1] + c[3] / 2;
                double ox = p.X - cx, oy = p.Y - cy, od = Js.Hypot(ox, oy);
                if (od < 110) { p.Vx += ox / od * 90; p.Vy += oy / od * 90; }
            }
        }

        void UpdateShots(double dt)
        {
            for (int i = Shots.Count - 1; i >= 0; i--)
            {
                var s = Shots[i];
                s.T += dt; s.Life -= dt;
                if (s.Kind == Kind.Boomerang && !s.Back && s.Life < 0.35) { s.Back = true; s.Life = 1.2; }
                if (s.Back)
                {
                    var o = s.Owner;
                    double dx = o.X - s.X, dy = o.Y - s.Y, d = Js.Hypot(dx, dy);
                    if (d == 0) d = 1;
                    s.Vx += dx / d * 2600 * dt; s.Vy += dy / d * 2600 * dt;
                    double sp = Js.Hypot(s.Vx, s.Vy);
                    if (sp > 700) { s.Vx *= 700 / sp; s.Vy *= 700 / sp; }
                    if (d < 30) s.Life = 0;
                }
                if (s.Kind == Kind.Grenade) { s.Vx *= 1 - 1.4 * dt; s.Vy *= 1 - 1.4 * dt; }
                s.X += s.Vx * dt; s.Y += s.Vy * dt;
                bool dead = s.Life <= 0 || s.X < 0 || s.Y < 0 || s.X > Data.ARENA_W || s.Y > Data.ARENA_H;
                if (!dead && s.Kind != Kind.Boomerang)
                    foreach (var c in Data.CRATES) if (s.X > c[0] && s.X < c[0] + c[2] && s.Y > c[1] && s.Y < c[1] + c[3]) { dead = true; break; }
                if (!dead)
                {
                    foreach (var p in Ps)
                    {
                        if (p == s.Owner || p.Dead > 0 || s.Hits.Contains(p)) continue;
                        if (Js.Hypot(p.X - s.X, p.Y - s.Y) < Data.RADIUS + s.R)
                        {
                            if (s.Kind == Kind.Grenade) { dead = true; break; }
                            s.Hits.Add(p);
                            Damage(p, s.Dmg, s.Owner);
                            Burst?.Invoke(s.X, s.Y, s.Color, 6);
                            if (s.Kind != Kind.Boomerang && s.Kind != Kind.Sniper) { dead = true; break; }
                        }
                    }
                }
                if (dead)
                {
                    if (s.Kind == Kind.Grenade)
                    {
                        Burst?.Invoke(s.X, s.Y, "#c8f0ff", 30);
                        Sound?.Invoke("boom");
                        foreach (var p in Ps)
                        {
                            double d = Js.Hypot(p.X - s.X, p.Y - s.Y);
                            if (d < 110) Damage(p, s.Dmg * (1 - d / 220), s.Owner);
                        }
                    }
                    Shots.RemoveAt(i);
                }
            }
        }

        void UpdatePickups(double dt)
        {
            foreach (var q in Pickups)
            {
                if (q.Wait > 0) { q.Wait -= dt; continue; }
                foreach (var p in Ps)
                {
                    if (p.Dead > 0 || Js.Hypot(p.X - q.X, p.Y - q.Y) > 40) continue;
                    if (q.Kind == "heart") { if (p.Hp >= Data.MAX_HP) continue; p.Hp = Math.Min(Data.MAX_HP, p.Hp + 40); }
                    else p.Power = Data.POWER_TIME;
                    q.Wait = Data.PICKUP_RESPAWN;
                    if (p.You) Sound?.Invoke("pick");
                    break;
                }
            }
        }
    }
}
