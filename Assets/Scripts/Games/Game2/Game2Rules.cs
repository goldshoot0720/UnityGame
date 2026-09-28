// 萌友街頭 3x3 — pure rules ported 1:1 from Game2/cloud/src (data.ts, rules.ts, court.ts).
using System;
using System.Linq;

namespace MoeGames.Game2
{
    public class Baller
    {
        public string Id, Name, Title, Color;
        /// <summary>1-10 ratings</summary>
        public int Shoot, Three, Speed, Defense, Jump;
    }

    public static class Data
    {
        public static readonly Baller[] BALLERS =
        {
            new Baller { Id = "whale", Name = "汐音", Title = "鯨魚女僕", Shoot = 7, Three = 6, Speed = 6, Defense = 6, Jump = 6, Color = "#4f8dff" },
            new Baller { Id = "penguin", Name = "小冰", Title = "企鵝少女", Shoot = 6, Three = 5, Speed = 9, Defense = 7, Jump = 5, Color = "#9adfff" },
            new Baller { Id = "glasses", Name = "光哉", Title = "眼鏡學長", Shoot = 8, Three = 9, Speed = 5, Defense = 5, Jump = 4, Color = "#d8b98a" },
            new Baller { Id = "tshirt", Name = "阿翔", Title = "T恤少年", Shoot = 7, Three = 5, Speed = 7, Defense = 7, Jump = 9, Color = "#b0b0b0" },
            new Baller { Id = "calico", Name = "小花", Title = "夾克三花貓", Shoot = 6, Three = 6, Speed = 8, Defense = 8, Jump = 7, Color = "#f0a24a" },
            new Baller { Id = "whitecat", Name = "書白", Title = "圖書館貓", Shoot = 7, Three = 8, Speed = 6, Defense = 6, Jump = 5, Color = "#f4efe6" },
            new Baller { Id = "redcat", Name = "緋音", Title = "紅髮貓耳少女", Shoot = 8, Three = 7, Speed = 8, Defense = 5, Jump = 6, Color = "#e8413c" },
            new Baller { Id = "sailor", Name = "澪", Title = "水手服少女", Shoot = 6, Three = 7, Speed = 7, Defense = 8, Jump = 6, Color = "#7fb3e6" },
        };

        public static Baller Get(string id) => BALLERS.FirstOrDefault(b => b.Id == id) ?? BALLERS[0];

        // ── TUNING (3x3 rules) ──
        public const int WIN_SCORE = 21;          // first to 21 wins (official 3x3)
        public const double GAME_TIME = 240;      // seconds on the game clock (4:00 arcade length)
        public const double SHOT_CLOCK = 12;      // 3x3 shot clock
        public const double RUN_SPEED = 190;      // floor px/s at speed 5
        public const double DEPTH_FACTOR = 0.62;  // vertical (depth) movement is slower
        public const double PASS_SPEED = 620;     // floor px/s
        public const double STEAL_RANGE = 34;
        public const double BLOCK_RANGE = 44;
        public const double METER_TIME = 0.9;     // seconds for the shot meter to fill
        public static readonly double[] METER_SWEET = { 0.78, 0.92 };
    }

    /// <summary>3x3 match rules — score, clocks, possession and the "clear the ball" rule.
    /// Side 0 = user team, 1 = CPU team.</summary>
    public class Match
    {
        public int[] Score = { 0, 0 };
        public double Clock = Data.GAME_TIME;
        public double ShotClock = Data.SHOT_CLOCK;
        public int Possession;
        /// <summary>After a defensive rebound or steal the ball must be taken behind the arc.</summary>
        public bool MustClear;
        public bool Over;
        public bool Overtime;

        /// <summary>Points for a made shot (3x3: 1 inside the arc, 2 beyond).</summary>
        public static int Points(bool three) => three ? 2 : 1;

        public int Made(int side, bool three)
        {
            int pts = Points(three);
            Score[side] += pts;
            if (Score[side] >= Data.WIN_SCORE) Over = true;
            if (Overtime) Over = true; // sudden death: first basket in OT wins
            // Other team checks the ball from the top.
            Possession = 1 - side;
            MustClear = false;
            ShotClock = Data.SHOT_CLOCK;
            return pts;
        }

        /// <summary>A change of possession on a live ball (rebound / steal / block recovery).</summary>
        public void Gain(int side)
        {
            if (side != Possession)
            {
                Possession = side;
                MustClear = true;
            }
            ShotClock = Data.SHOT_CLOCK;
        }

        /// <summary>Offensive rebound off the rim resets the shot clock but keeps possession.</summary>
        public void OffensiveRebound() => ShotClock = Data.SHOT_CLOCK;
        public void Cleared() => MustClear = false;

        /// <summary>Tick the clocks; returns true if the shot clock expired this tick.</summary>
        public bool Tick(double dt, bool ballLive)
        {
            if (Over || !ballLive) return false;
            Clock = Math.Max(0, Clock - dt);
            if (Clock <= 0)
            {
                if (Score[0] == Score[1]) Overtime = true;
                else { Over = true; return false; }
            }
            ShotClock -= dt;
            if (ShotClock <= 0)
            {
                Possession = 1 - Possession;
                MustClear = false;
                ShotClock = Data.SHOT_CLOCK;
                return true;
            }
            return false;
        }

        /// <summary>0, 1 or -1 (tie).</summary>
        public int Winner() => Score[0] > Score[1] ? 0 : Score[1] > Score[0] ? 1 : -1;

        /// <summary>Make probability for a shot. dist: real distance to hoop (floor px), three: beyond
        /// the arc, skill: 1-10, timing: 0..1 meter quality, contest: 0 (open) .. 1 (hand in face).</summary>
        public static double ShotChance(double dist, bool three, double skill, double timing, double contest)
        {
            double b;
            if (dist < 60) b = 0.72;            // layup range
            else if (!three) b = 0.62 - (dist - 60) / 600;
            else b = 0.44 - Math.Max(0, dist - 240) / 500;
            double sk = (skill - 5) * 0.035;
            double tm = (timing - 0.6) * 0.45;
            double p = (b + sk + tm) * (1 - contest * 0.45);
            return Math.Max(0.03, Math.Min(0.95, p));
        }

        /// <summary>Meter quality: 1 at the sweet window, falling off either side.</summary>
        public static double MeterQuality(double v, double[] sweet)
        {
            if (v >= sweet[0] && v <= sweet[1]) return 1;
            double d = v < sweet[0] ? sweet[0] - v : v - sweet[1];
            return Math.Max(0, 1 - d * 3.2);
        }
    }

    /// <summary>Court geometry in "floor px" (the 1024×614 court image space).</summary>
    public static class Court
    {
        public const double HOOP_X = 512, HOOP_Y = 404;     // the floor point under the rim
        public const double RIM_Y = 272;                    // the rim in image space
        public const double RIM_HEIGHT = HOOP_Y - RIM_Y;    // px of rim height at the hoop's depth
        public const double TOP_Y = 408;                    // baseline (just in front of the hoop post)
        public const double BOTTOM_Y = 604;                 // the far edge of the playable half
        public const double ARC_CX = 512, ARC_CY = 404, ARC_RX = 222, ARC_RY = 100; // three-point line
        public const double TOP_OF_KEY_X = 512, TOP_OF_KEY_Y = 540; // check-ball spot beyond the arc

        /// <summary>Horizontal half-width of the floor at depth y (trapezoid edges).</summary>
        public static double HalfWidth(double y) => 237 + ((y - 400) * (392 - 237)) / 145;

        public static void ClampToCourt(ref double x, ref double y)
        {
            y = Js.Clamp(y, TOP_Y, BOTTOM_Y);
            double hw = HalfWidth(y) - 18;
            x = Js.Clamp(x, 512 - hw, 512 + hw);
        }

        /// <summary>Outside the three-point arc (an ellipse centred on the hoop).</summary>
        public static bool IsThree(double x, double y)
        {
            double u = (x - ARC_CX) / ARC_RX, v = (y - ARC_CY) / ARC_RY;
            return u * u + v * v > 1;
        }

        /// <summary>"Real" distance to the hoop, compensating for the squashed depth axis.</summary>
        public static double HoopDist(double x, double y) => Js.Hypot(x - HOOP_X, (y - HOOP_Y) * (ARC_RX / ARC_RY));
        public static double FloorDist(double ax, double ay, double bx, double by) => Js.Hypot(ax - bx, (ay - by) * (ARC_RX / ARC_RY));
    }
}
