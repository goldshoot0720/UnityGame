// 萌友卡丁車 GP — pure data/rules ported 1:1 from Game3/cloud/src/data.ts (+ fmtTime from ui.ts),
// plus Road: a re-implementation of the engine pack's createRoad curve/hill profile (the
// engine pack is not in the repository, so easing is an approximation).
using System;
using System.Collections.Generic;
using System.Linq;

namespace MoeGames.Game3
{
    public class Racer
    {
        public string Id, Name, Title, Color;
        /// <summary>Stats are multipliers around 1.0.</summary>
        public double Speed, Accel, Handling;
    }

    public class TrackPiece
    {
        public double Length, Curve, Hill;
        public TrackPiece(double length, double curve = 0, double hill = 0) { Length = length; Curve = curve; Hill = hill; }
    }

    public class Track
    {
        public string Id, Name, Subtitle, Prop; // prop: palm | snowman | lamp
        public double Grip;                      // steering grip multiplier (ice < 1)
        public string[] Grass, Road, Rumble;
        public string Lane, Fog;
        public TrackPiece[] Pieces;
    }

    public enum ItemId { None, Fish, Banana, Bubble, Star }

    public static class Data
    {
        public static readonly Racer[] RACERS =
        {
            new Racer { Id = "whale", Name = "汐音", Title = "鯨魚女僕", Speed = 1.0, Accel = 1.0, Handling = 1.0, Color = "#4f8dff" },
            new Racer { Id = "penguin", Name = "小冰", Title = "企鵝少女", Speed = 0.96, Accel = 1.12, Handling = 1.12, Color = "#9adfff" },
            new Racer { Id = "glasses", Name = "光哉", Title = "眼鏡學長", Speed = 1.02, Accel = 0.94, Handling = 1.04, Color = "#d8b98a" },
            new Racer { Id = "tshirt", Name = "阿翔", Title = "T恤少年", Speed = 1.06, Accel = 0.9, Handling = 0.94, Color = "#b0b0b0" },
            new Racer { Id = "calico", Name = "小花", Title = "夾克三花貓", Speed = 1.0, Accel = 1.08, Handling = 0.98, Color = "#f0a24a" },
            new Racer { Id = "whitecat", Name = "書白", Title = "圖書館貓", Speed = 0.98, Accel = 1.0, Handling = 1.1, Color = "#f4efe6" },
            new Racer { Id = "redcat", Name = "緋音", Title = "紅髮貓耳少女", Speed = 1.05, Accel = 1.02, Handling = 0.92, Color = "#e8413c" },
            new Racer { Id = "sailor", Name = "澪", Title = "水手服少女", Speed = 1.01, Accel = 0.98, Handling = 1.06, Color = "#7fb3e6" },
        };

        /// <summary>Tracks are authored short and stretched to race length (hills scale too).</summary>
        static TrackPiece[] Stretch(TrackPiece[] ps, double k = 2.2)
            => ps.Select(p => new TrackPiece(Js.Round(p.Length * k / 200) * 200, p.Curve, p.Hill)).ToArray();

        public static readonly Track[] TRACKS =
        {
            new Track
            {
                Id = "ocean", Name = "海洋城市", Subtitle = "陽光沙灘 × 寬闊彎道", Prop = "palm", Grip = 1.0,
                Grass = new[] { "#f1d9a0", "#e8cb85" }, Road = new[] { "#5a6474", "#525c6b" }, Rumble = new[] { "#ffffff", "#e53945" }, Lane = "#ffffff", Fog = "#bfe6ff",
                Pieces = Stretch(new[]
                {
                    new TrackPiece(8000), new TrackPiece(9000, 2.2), new TrackPiece(6000, 0, 1500), new TrackPiece(8000, -3),
                    new TrackPiece(7000, 0, -1500), new TrackPiece(9000, 2.8), new TrackPiece(6000), new TrackPiece(8000, -2.2, 800),
                    new TrackPiece(7000, 3.4), new TrackPiece(7000, 0, -800),
                }),
            },
            new Track
            {
                Id = "snow", Name = "冰雪企鵝村", Subtitle = "結冰路面 × 連續髮夾彎", Prop = "snowman", Grip = 0.82,
                Grass = new[] { "#f4f8ff", "#dfe9f7" }, Road = new[] { "#9fb4cf", "#93a8c3" }, Rumble = new[] { "#ffffff", "#3a7bd5" }, Lane = "#e8f4ff", Fog = "#fde6ee",
                Pieces = Stretch(new[]
                {
                    new TrackPiece(7000), new TrackPiece(6000, -3.2), new TrackPiece(5000, 3.6), new TrackPiece(6000, 0, 1800),
                    new TrackPiece(7000, -4), new TrackPiece(5000, 0, -1800), new TrackPiece(6000, 4.2), new TrackPiece(5000),
                    new TrackPiece(6000, -3.6, 1000), new TrackPiece(7000, 2.6, -1000), new TrackPiece(5000),
                }),
            },
            new Track
            {
                Id = "neon", Name = "霓虹夜城", Subtitle = "夜晚街道 × 大起伏", Prop = "lamp", Grip = 0.95,
                Grass = new[] { "#2a1f4a", "#231a40" }, Road = new[] { "#3a3552", "#34304a" }, Rumble = new[] { "#ff4fd8", "#39e6ff" }, Lane = "#ffe066", Fog = "#402a6a",
                Pieces = Stretch(new[]
                {
                    new TrackPiece(7000), new TrackPiece(6000, 0, 2400), new TrackPiece(7000, 4.4), new TrackPiece(6000, 0, -2400),
                    new TrackPiece(6000, -4.8), new TrackPiece(7000, 2, 1600), new TrackPiece(6000, 5), new TrackPiece(6000, 0, -1600),
                    new TrackPiece(7000, -3), new TrackPiece(6000),
                }),
            },
        };

        public static readonly Dictionary<ItemId, (string name, string color)> ITEMS = new Dictionary<ItemId, (string, string)>
        {
            [ItemId.Fish] = ("衝刺魚", "#5cc8ff"),
            [ItemId.Banana] = ("香蕉皮", "#ffe066"),
            [ItemId.Bubble] = ("泡泡彈", "#9fe3ff"),
            [ItemId.Star] = ("無敵星", "#ffb3f0"),
        };

        /// <summary>Weighted item roll — racers further back get better items. place 0 = leader.</summary>
        public static ItemId RollItem(int place, int total, double r)
        {
            double back = total > 1 ? (double)place / (total - 1) : 0;
            var table = new (ItemId id, double w)[]
            {
                (ItemId.Banana, 3 - back * 2),
                (ItemId.Fish, 2 + back * 1),
                (ItemId.Bubble, 1.5 + back * 1.5),
                (ItemId.Star, 0.2 + back * 1.8),
            };
            double sum = table.Sum(e => e.w);
            double x = r * sum;
            foreach (var (id, w) in table) { if ((x -= w) <= 0) return id; }
            return ItemId.Banana;
        }

        // ── TUNING ──
        public const int LAPS = 3;
        public const double MAX_SPEED = 7200;      // world units / s (≈ kart top speed)
        public const double ACCEL = 3000;
        public const double BRAKE = 7000;
        public const double COAST = 1400;
        public const double OFFROAD_MAX = 0.45;    // fraction of top speed on the verge
        public const double STEER = 1.9;           // half-widths / s at full speed
        public const double CENTRIFUGAL = 0.2;
        public const double BOOST_MUL = 1.35;
        public const double SEGMENT = 200;
        public const double ROAD_W = 1000;
        public const double KART_W = 420;          // world width of a kart
        public const double KMH = 0.02;            // units/s → displayed km/h

        /// <summary>Race order: finishers by time, then everyone else by distance.</summary>
        public static List<T> Standings<T>(IEnumerable<T> rs, Func<T, double> total, Func<T, double> finishT)
            => Js.Sorted(rs, (a, b) =>
            {
                double fa = finishT(a), fb = finishT(b);
                if (fa >= 0 && fb >= 0) return fa.CompareTo(fb);
                if (fa >= 0) return -1;
                if (fb >= 0) return 1;
                return total(b).CompareTo(total(a));
            });

        public static string FmtTime(double s)
        {
            int m = (int)Math.Floor(s / 60);
            double sec = s - m * 60;
            return $"{m}:{(sec < 10 ? "0" : "")}{sec.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}";
        }
    }

    /// <summary>Segmented road built from track pieces (curve and hill per segment).</summary>
    public class Road
    {
        public readonly double[] Curve;   // per segment
        public readonly double[] Height;  // world units at the segment start
        public readonly double Length;
        public int Count => Curve.Length;

        static double EaseIn(double a, double b, double p) => a + (b - a) * p * p;
        static double EaseOut(double a, double b, double p) => a + (b - a) * (1 - (1 - p) * (1 - p));
        static double EaseInOut(double a, double b, double p) => a + (b - a) * (-Math.Cos(p * Math.PI) / 2 + 0.5);

        public Road(IEnumerable<TrackPiece> pieces)
        {
            var curve = new List<double>();
            var height = new List<double>();
            double y = 0;
            foreach (var p in pieces)
            {
                int n = Math.Max(1, (int)Math.Round(p.Length / Data.SEGMENT));
                int enter = Math.Max(1, n / 4), leave = Math.Max(1, n / 4);
                for (int i = 0; i < n; i++)
                {
                    double c;
                    if (i < enter) c = EaseIn(0, p.Curve, (double)i / enter);
                    else if (i >= n - leave) c = EaseOut(p.Curve, 0, (double)(i - (n - leave)) / leave);
                    else c = p.Curve;
                    curve.Add(c);
                    height.Add(y + EaseInOut(0, p.Hill, (double)i / n));
                }
                y += p.Hill;
            }
            Curve = curve.ToArray();
            Height = height.ToArray();
            Length = Curve.Length * Data.SEGMENT;
        }

        int Seg(double z)
        {
            int i = (int)Math.Floor(z / Data.SEGMENT) % Count;
            return i < 0 ? i + Count : i;
        }

        public double CurveAt(double z) => Curve[Seg(z)];

        public double HeightAt(double z)
        {
            double f = z / Data.SEGMENT;
            int i = Seg(z), j = (i + 1) % Count;
            double k = f - Math.Floor(f);
            return Height[i] + (Height[j] - Height[i]) * k;
        }
    }
}
