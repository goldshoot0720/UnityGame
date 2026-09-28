// 萌友棒球對決 — pure logic ported 1:1 from Game1/cloud/src (data.ts, rules.ts, sim.ts).
using System;
using System.Collections.Generic;

namespace MoeGames.Game1
{
    public enum PitchName { 直球, 滑球, 曲球, 指叉, 伸卡, 變速 }

    public class PitchType
    {
        /// <summary>speed multiplier on the pitcher's velocity</summary>
        public double Speed;
        /// <summary>break at the plate, in strike-zone half-widths (x: + = to catcher's right, y: + = down)</summary>
        public double Dx, Dy;
        /// <summary>how late the break happens (higher = later)</summary>
        public double Curve;
        public string Color;
    }

    public class Player
    {
        public string Id, Name, Title;
        /// <summary>batting: contact, power, speed (1-100)</summary>
        public int Meet, Power, Speed;
        /// <summary>pitching: velocity km/h, control 1-100</summary>
        public int Velo, Control;
        public PitchName[] Pitches;
    }

    public class Team
    {
        public string Id; // "whale" | "cat"
        public string Name, Color, Dark, Pitcher;
        public string[] Lineup;
    }

    public enum PitchOutcome { Ball, Strike, Foul, Groundout, Flyout, Lineout, Single, Double, Triple, Homerun }

    public static class Data
    {
        public static readonly Dictionary<PitchName, PitchType> PITCH_TYPES = new Dictionary<PitchName, PitchType>
        {
            [PitchName.直球] = new PitchType { Speed = 1.0, Dx = 0, Dy = -0.05, Curve = 2, Color = "#ffffff" },
            [PitchName.滑球] = new PitchType { Speed = 0.88, Dx = 0.9, Dy = 0.15, Curve = 2.4, Color = "#4fc3ff" },
            [PitchName.曲球] = new PitchType { Speed = 0.78, Dx = 0.55, Dy = 0.8, Curve = 1.8, Color = "#7dff6a" },
            [PitchName.指叉] = new PitchType { Speed = 0.86, Dx = 0, Dy = 0.95, Curve = 3.2, Color = "#ff6ad5" },
            [PitchName.伸卡] = new PitchType { Speed = 0.92, Dx = -0.75, Dy = 0.45, Curve = 2.4, Color = "#ffb13d" },
            [PitchName.變速] = new PitchType { Speed = 0.78, Dx = -0.15, Dy = 0.45, Curve = 2, Color = "#d6a4ff" },
        };

        public static readonly Dictionary<string, Player> PLAYERS = new Dictionary<string, Player>
        {
            ["whale"] = new Player { Id = "whale", Name = "汐音", Title = "鯨魚女僕", Meet = 58, Power = 50, Speed = 55, Velo = 146, Control = 74, Pitches = new[] { PitchName.直球, PitchName.滑球, PitchName.指叉, PitchName.伸卡 } },
            ["penguin"] = new Player { Id = "penguin", Name = "小冰", Title = "企鵝少女", Meet = 78, Power = 35, Speed = 88, Velo = 128, Control = 60, Pitches = new[] { PitchName.直球, PitchName.曲球, PitchName.變速 } },
            ["glasses"] = new Player { Id = "glasses", Name = "光哉", Title = "眼鏡學長", Meet = 85, Power = 55, Speed = 50, Velo = 132, Control = 82, Pitches = new[] { PitchName.直球, PitchName.曲球, PitchName.伸卡 } },
            ["tshirt"] = new Player { Id = "tshirt", Name = "阿翔", Title = "T恤少年", Meet = 62, Power = 88, Speed = 60, Velo = 140, Control = 55, Pitches = new[] { PitchName.直球, PitchName.滑球 } },
            ["calico"] = new Player { Id = "calico", Name = "小花", Title = "夾克三花貓", Meet = 70, Power = 72, Speed = 70, Velo = 138, Control = 66, Pitches = new[] { PitchName.直球, PitchName.滑球, PitchName.變速 } },
            ["whitecat"] = new Player { Id = "whitecat", Name = "書白", Title = "圖書館貓", Meet = 64, Power = 45, Speed = 66, Velo = 150, Control = 70, Pitches = new[] { PitchName.直球, PitchName.曲球, PitchName.指叉, PitchName.變速 } },
            ["redcat"] = new Player { Id = "redcat", Name = "緋音", Title = "紅髮貓耳少女", Meet = 74, Power = 66, Speed = 78, Velo = 134, Control = 60, Pitches = new[] { PitchName.直球, PitchName.滑球, PitchName.曲球 } },
            ["sailor"] = new Player { Id = "sailor", Name = "澪", Title = "水手服少女", Meet = 80, Power = 58, Speed = 72, Velo = 130, Control = 78, Pitches = new[] { PitchName.直球, PitchName.伸卡, PitchName.變速 } },
        };

        public static readonly Dictionary<string, Team> TEAMS = new Dictionary<string, Team>
        {
            ["whale"] = new Team { Id = "whale", Name = "藍鯨隊", Color = "#2a6fdb", Dark = "#0d2f6e", Pitcher = "whale", Lineup = new[] { "penguin", "glasses", "tshirt", "whale" } },
            ["cat"] = new Team { Id = "cat", Name = "貓咪隊", Color = "#e0503a", Dark = "#6e1a10", Pitcher = "whitecat", Lineup = new[] { "redcat", "sailor", "calico", "whitecat" } },
        };

        // ── TUNING ──
        public const int INNINGS = 3;
        public const double ZONE_W = 120;           // strike zone width (world units)
        public const double ZONE_H = 140;           // strike zone height
        public const double MEET_R = 34;            // batting cursor radius (scaled by meet)
        public const double CURSOR_SPEED = 520;     // batting / aiming cursor speed (units/s)
        public const double PITCH_TIME_BASE = 0.62; // seconds for a 150 km/h pitch to reach the plate
        // Balance (Unity port): the 3D catcher view makes timing harder than the 2D original, and CPU
        // batters were far too good (≈ .54 AVG, 2 % K in simulation). User swings get a wider timing
        // window, a bigger meet circle and slightly slower pitches; CPU batters miss more and fielders
        // turn some of their singles/doubles into outs (≈ .265 AVG, 23 % K).
        public const double SWING_WINDOW = 0.11;      // CPU / base timing window (s)
        public const double USER_SWING_WINDOW = 0.17;
        public const double USER_MEET_BONUS = 0.12;   // extra meet radius for the user (zone units)
        public const double USER_PITCH_SLOW = 1.2;    // pitch flight time multiplier when the user bats
        public const double CPU_AIM_SPREAD = 1.6, CPU_TIMING_SPREAD = 1.8, CPU_HIT_TO_OUT = 0.4;

        public static readonly Dictionary<PitchOutcome, string> OUTCOME_TEXT = new Dictionary<PitchOutcome, string>
        {
            [PitchOutcome.Ball] = "壞球", [PitchOutcome.Strike] = "好球", [PitchOutcome.Foul] = "界外球",
            [PitchOutcome.Groundout] = "滾地球出局", [PitchOutcome.Flyout] = "高飛球出局", [PitchOutcome.Lineout] = "平飛球出局",
            [PitchOutcome.Single] = "一壘安打！", [PitchOutcome.Double] = "二壘安打！", [PitchOutcome.Triple] = "三壘安打！", [PitchOutcome.Homerun] = "全壘打！！",
        };

        /// <summary>Bases gained by a hit (0 = not a hit).</summary>
        public static int HitBases(PitchOutcome o) => o switch
        {
            PitchOutcome.Single => 1, PitchOutcome.Double => 2, PitchOutcome.Triple => 3, PitchOutcome.Homerun => 4, _ => 0,
        };

        public static bool IsOut(PitchOutcome o) => o == PitchOutcome.Groundout || o == PitchOutcome.Flyout || o == PitchOutcome.Lineout;
    }

    /// <summary>Count, outs, bases, score and innings.</summary>
    public class GameState
    {
        public int Inning = 1;
        public bool Top = true;
        public int Outs, Balls, Strikes;
        public bool[] Bases = { false, false, false };
        public readonly Dictionary<string, int> Runs = new Dictionary<string, int> { ["away"] = 0, ["home"] = 0 };
        public readonly Dictionary<string, int> Hits = new Dictionary<string, int> { ["away"] = 0, ["home"] = 0 };
        public readonly Dictionary<string, List<int>> Line = new Dictionary<string, List<int>> { ["away"] = new List<int> { 0 }, ["home"] = new List<int>() };
        public readonly Dictionary<string, int> Order = new Dictionary<string, int> { ["away"] = 0, ["home"] = 0 };
        public bool Over;
        /// <summary>A short announcement produced by the last event (e.g. "三振！", "得分 +2").</summary>
        public string LastNote = "";

        public readonly Team Away, Home;
        public readonly int Innings;

        public GameState(Team away, Team home, int innings = Data.INNINGS) { Away = away; Home = home; Innings = innings; }

        public static GameState ForUser(string userTeam)
        {
            string cpu = userTeam == "whale" ? "cat" : "whale";
            // The user always bats second (home), giving them the last word.
            return new GameState(Data.TEAMS[cpu], Data.TEAMS[userTeam]);
        }

        public string BattingSide => Top ? "away" : "home";
        public string FieldingSide => Top ? "home" : "away";
        public Team BattingTeam => Top ? Away : Home;
        public Team FieldingTeam => Top ? Home : Away;
        public Player Batter
        {
            get
            {
                var t = BattingTeam;
                return Data.PLAYERS[t.Lineup[Order[BattingSide] % t.Lineup.Length]];
            }
        }
        public Player Pitcher => Data.PLAYERS[FieldingTeam.Pitcher];

        void Score(int n)
        {
            if (n <= 0) return;
            string side = BattingSide;
            Runs[side] += n;
            var arr = Line[side];
            arr[arr.Count - 1] += n;
        }

        void NextBatter()
        {
            Balls = 0;
            Strikes = 0;
            Order[BattingSide]++;
        }

        void Out(string note)
        {
            Outs++;
            LastNote = note;
            NextBatter();
            if (Outs >= 3) EndHalf();
        }

        void EndHalf()
        {
            Outs = 0;
            Bases = new[] { false, false, false };
            if (Top)
            {
                // Home already leads after the top of the last inning → game over.
                if (Inning >= Innings && Runs["home"] > Runs["away"]) { Over = true; return; }
                Top = false;
                Line["home"].Add(0);
            }
            else
            {
                if (Inning >= Innings && Runs["home"] != Runs["away"]) { Over = true; return; }
                if (Inning >= Innings + 3) { Over = true; return; } // tie after 3 extra innings
                Inning++;
                Top = true;
                Line["away"].Add(0);
            }
        }

        /// <summary>Advance runners by n bases (batter included); returns runs scored.</summary>
        int Advance(int n)
        {
            int runs = 0;
            var b = Bases;
            var next = new[] { false, false, false };
            for (int i = 2; i >= 0; i--)
            {
                if (!b[i]) continue;
                int to = i + n;
                if (to >= 3) runs++; else next[to] = true;
            }
            if (n >= 4) runs++; else next[n - 1] = true;
            Bases = next;
            return runs;
        }

        int Walk()
        {
            var b = Bases;
            int runs = 0;
            if (b[0])
            {
                if (b[1])
                {
                    if (b[2]) runs = 1;
                    b[2] = true;
                }
                b[1] = true;
            }
            b[0] = true;
            return runs;
        }

        void WalkOffCheck()
        {
            if (!Top && Inning >= Innings && Runs["home"] > Runs["away"]) Over = true;
        }

        /// <summary>Apply one pitch's outcome.</summary>
        public void Apply(PitchOutcome o)
        {
            if (Over) return;
            LastNote = "";
            string side = BattingSide;
            switch (o)
            {
                case PitchOutcome.Ball:
                    Balls++;
                    if (Balls >= 4)
                    {
                        int r = Walk();
                        Score(r);
                        LastNote = r > 0 ? $"四壞保送 押回 {r} 分" : "四壞保送";
                        NextBatter();
                    }
                    break;
                case PitchOutcome.Strike:
                    Strikes++;
                    if (Strikes >= 3) Out("三振！");
                    break;
                case PitchOutcome.Foul:
                    if (Strikes < 2) Strikes++;
                    break;
                case PitchOutcome.Groundout:
                case PitchOutcome.Flyout:
                case PitchOutcome.Lineout:
                    // Sacrifice fly: a fly out with a runner on third and < 2 outs scores him.
                    if (o == PitchOutcome.Flyout && Bases[2] && Outs < 2)
                    {
                        Bases[2] = false;
                        Score(1);
                        Out("高飛犧牲打 得 1 分");
                    }
                    else Out(Data.OUTCOME_TEXT[o]);
                    break;
                default:
                    {
                        int n = Data.HitBases(o);
                        if (n == 0) n = 1;
                        Hits[side]++;
                        int r = Advance(n);
                        Score(r);
                        LastNote = r > 0 ? $"{Data.OUTCOME_TEXT[o]} 得 {r} 分" : Data.OUTCOME_TEXT[o];
                        NextBatter();
                        break;
                    }
            }
            WalkOffCheck();
        }

        /// <summary>"home", "away" or "tie".</summary>
        public string Winner() => Runs["home"] > Runs["away"] ? "home" : Runs["away"] > Runs["home"] ? "away" : "tie";
    }

    public class Pitch
    {
        public PitchName Type;
        /// <summary>where the pitch was aimed, in zone units (-1..1 is inside the zone)</summary>
        public double AimX, AimY;
        /// <summary>final location at the plate after control error and break (zone units)</summary>
        public double EndX, EndY;
        /// <summary>seconds from release to the plate</summary>
        public double Time;
        public double Kmh;
    }

    /// <summary>Pitch flight & contact resolution with an injectable RNG.</summary>
    public static class Sim
    {
        public static Pitch MakePitch(Player p, PitchName type, double aimX, double aimY, Func<double> rng)
        {
            var pt = Data.PITCH_TYPES[type];
            double err = (1 - p.Control / 100.0) * 0.55;
            double endX = aimX + (rng() * 2 - 1) * err;
            double endY = aimY + (rng() * 2 - 1) * err;
            double kmh = Js.Round(p.Velo * pt.Speed - rng() * 3);
            return new Pitch { Type = type, AimX = aimX, AimY = aimY, EndX = endX, EndY = endY, Time = Data.PITCH_TIME_BASE * (150 / kmh), Kmh = kmh };
        }

        /// <summary>Ball position in zone units at flight progress t (0..1): it starts displaced
        /// opposite its break and bends onto (endX, endY) late in the flight.</summary>
        public static (double x, double y) PitchPos(Pitch p, double t)
        {
            var pt = Data.PITCH_TYPES[p.Type];
            double bend = 1 - Math.Pow(t, pt.Curve);
            return (p.EndX - pt.Dx * bend, p.EndY - pt.Dy * bend);
        }

        public static bool InZone(double x, double y) => Math.Abs(x) <= 1 && Math.Abs(y) <= 1;

        /// <summary>Resolve a swing. timing = swing error in seconds (negative = early),
        /// dist = distance from the meet cursor centre to the ball (zone units).</summary>
        public static double MeetRadius(Player batter, double bonus = 0) => 0.35 + batter.Meet / 220.0 + bonus;

        public static PitchOutcome ResolveSwing(Player batter, double timing, double dist, Func<double> rng,
            double window = Data.SWING_WINDOW, double meetBonus = 0)
        {
            double meetR = MeetRadius(batter, meetBonus);       // zone units covered by the cursor
            if (dist > meetR || Math.Abs(timing) > window) return PitchOutcome.Strike;
            double aim = 1 - dist / meetR;                      // 0 edge … 1 sweet spot
            double time = 1 - Math.Abs(timing) / window;
            double q = aim * 0.55 + time * 0.45;                // contact quality 0..1
            if (q < 0.22) return PitchOutcome.Foul;
            // Launch: early swings pull, late ones slice foul more often.
            if (Math.Abs(timing) > window * 0.73 && rng() < 0.55) return PitchOutcome.Foul;
            double power = q * (0.55 + batter.Power / 160.0) + (rng() - 0.5) * 0.18;
            double r = rng();
            if (power > 1.02) return PitchOutcome.Homerun;
            if (power > 0.88) return r < 0.35 ? PitchOutcome.Homerun : r < 0.6 ? PitchOutcome.Double : PitchOutcome.Flyout;
            if (power > 0.72) return r < 0.12 + batter.Speed / 900.0 ? PitchOutcome.Triple : r < 0.45 ? PitchOutcome.Double : r < 0.75 ? PitchOutcome.Single : PitchOutcome.Flyout;
            if (power > 0.52) return r < 0.5 ? PitchOutcome.Single : r < 0.75 ? PitchOutcome.Lineout : PitchOutcome.Groundout;
            return r < 0.25 + batter.Speed / 400.0 ? PitchOutcome.Single : r < 0.6 ? PitchOutcome.Groundout : PitchOutcome.Flyout;
        }

        /// <summary>CPU batter: decides whether to swing and how well, given the pitch.</summary>
        public static PitchOutcome CpuBat(Player batter, Pitch pitch, Func<double> rng)
        {
            bool zone = InZone(pitch.EndX, pitch.EndY);
            double edge = Math.Max(Math.Abs(pitch.EndX), Math.Abs(pitch.EndY));
            var pt = Data.PITCH_TYPES[pitch.Type];
            double breakAmt = Js.Hypot(pt.Dx, pt.Dy);
            // Swing more at strikes; chase breaking balls just off the plate.
            double swingP = zone ? 0.72 : Math.Max(0.03, 0.45 - (edge - 1) * 0.8 + breakAmt * 0.12);
            if (rng() >= swingP) return zone ? PitchOutcome.Strike : PitchOutcome.Ball;
            // Harder to square up fast pitches, big breaks and corners.
            double difficulty = (pitch.Kmh - 120) / 60 + breakAmt * 0.25 + (zone ? edge * 0.25 : 0.5);
            double dist = Math.Max(0, (rng() * 0.9) * (0.55 + difficulty * 0.5) * Data.CPU_AIM_SPREAD - batter.Meet / 400.0);
            double timing = (rng() * 2 - 1) * (0.05 + difficulty * 0.05) * Data.CPU_TIMING_SPREAD;
            var o = ResolveSwing(batter, timing, dist, rng);
            // Fielders get to some of the CPU's balls in play.
            if (o == PitchOutcome.Single && rng() < Data.CPU_HIT_TO_OUT) return PitchOutcome.Groundout;
            if (o == PitchOutcome.Double && rng() < Data.CPU_HIT_TO_OUT * 0.5) return PitchOutcome.Flyout;
            return o;
        }

        /// <summary>CPU pitcher: picks a pitch and an aim point.</summary>
        public static (PitchName type, double aimX, double aimY) CpuPitch(Player p, int balls, int strikes, Func<double> rng)
        {
            var type = p.Pitches[(int)Math.Floor(rng() * p.Pitches.Length)];
            double nibble = strikes >= 2 && balls < 3 ? 1.15 : balls >= 3 ? 0.6 : 0.85;
            return (type, (rng() * 2 - 1) * nibble, (rng() * 2 - 1) * nibble);
        }
    }
}
