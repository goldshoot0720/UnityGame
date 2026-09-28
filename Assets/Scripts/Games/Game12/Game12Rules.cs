// Grid, balloons, water blasts, danger map, path-finding and bot brain — pure logic ported from Game12/cloud/src/rules.ts.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MoeGames.Game12
{
    public enum Cell { Empty = 0, Solid = 1, Box = 2 }
    public enum ItemKind { Balloon, Potion, Skate, Needle, Ultra }

    public class Hero
    {
        public string Id, Name, Title, Color;
        public int Balloons, Range, Speed;
        public Hero(string id, string name, string title, string color, int balloons, int range, int speed)
        { Id = id; Name = name; Title = title; Color = color; Balloons = balloons; Range = range; Speed = speed; }
    }

    public class BalloonLike
    {
        public int C, R, Range; public float T;
        public BalloonLike(int c, int r, float t, int range) { C = c; R = r; T = t; Range = range; }
    }

    public struct Pt : IEquatable<Pt>
    {
        public int C, R;
        public Pt(int c, int r) { C = c; R = r; }
        public bool Equals(Pt o) => C == o.C && R == o.R;
        public override bool Equals(object o) => o is Pt p && Equals(p);
        public override int GetHashCode() => R * 64 + C;
    }

    public class Foe { public int C, R; public bool Trapped; }
    public class Me { public int C, R, Left, Range; public float Speed; }

    public class BotView
    {
        public Cell[,] Grid; public List<BalloonLike> Balloons; public List<int> Wet = new List<int>();
        public List<Pt> Items = new List<Pt>(); public Me Me; public List<Foe> Foes = new List<Foe>();
    }

    public enum BotKind { Move, Bomb, Idle }
    public class BotAction
    {
        public BotKind Kind; public List<Pt> Path;
        public static BotAction Idle => new BotAction { Kind = BotKind.Idle };
    }

    public static class Rules
    {
        public const int COLS = 15, ROWS = 13;
        public const float FUSE = 2.6f, WATER_TIME = 0.55f, TRAP_TIME = 3.5f, BASE_SPEED = 3.0f, SPEED_STEP = 0.45f;
        public const int MAX_SPEED_LV = 8, MAX_BALLOONS = 8, MAX_RANGE = 8, WINS_NEEDED = 2;
        public const float ROUND_TIME = 150f;
        public static readonly Pt[] SPAWNS = { new Pt(0, 0), new Pt(COLS - 1, ROWS - 1), new Pt(COLS - 1, 0), new Pt(0, ROWS - 1) };
        public static readonly string[] PLAYER_COLORS = { "#39c6ff", "#ff6fa8", "#7ee05a", "#ffc83a" };

        /// <summary>Each hero starts with slightly different stats (total budget is equal).</summary>
        public static readonly Hero[] HEROES =
        {
            new Hero("whale", "汐音", "鯨魚女僕", "#4f8dff", 1, 2, 1),
            new Hero("penguin", "小冰", "企鵝少女", "#9adfff", 2, 1, 1),
            new Hero("glasses", "光哉", "眼鏡學長", "#d8b98a", 1, 1, 2),
            new Hero("tshirt", "阿翔", "T恤少年", "#b0b0b0", 2, 1, 1),
            new Hero("calico", "小花", "夾克三花貓", "#f0a24a", 1, 1, 2),
            new Hero("whitecat", "書白", "圖書館貓", "#f4efe6", 1, 2, 1),
            new Hero("redcat", "緋音", "紅髮貓耳少女", "#e8413c", 2, 1, 1),
            new Hero("sailor", "澪", "水手服少女", "#7fb3e6", 1, 2, 1),
        };

        public static string ItemName(ItemKind k) => k switch
        {
            ItemKind.Balloon => "水球 +1", ItemKind.Potion => "水柱 +1", ItemKind.Skate => "速度 +1",
            ItemKind.Needle => "救命針", _ => "水柱最大",
        };

        /// <summary>Item table for a broken box: roll in [0,1).</summary>
        public static ItemKind? DropFor(double roll)
        {
            if (roll < 0.14) return ItemKind.Balloon;
            if (roll < 0.28) return ItemKind.Potion;
            if (roll < 0.39) return ItemKind.Skate;
            if (roll < 0.44) return ItemKind.Needle;
            if (roll < 0.47) return ItemKind.Ultra;
            return null;
        }

        /// <summary>xorshift32 identical to the TS version, returns values in [0,1).</summary>
        public static Func<double> Rng(uint seed)
        {
            uint s = seed == 0 ? 1u : seed;
            return () => { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s % 100000) / 100000.0; };
        }

        public static int Key(int c, int r) => r * COLS + c;
        public static bool InBounds(int c, int r) => c >= 0 && r >= 0 && c < COLS && r < ROWS;

        /// <summary>Pillars on odd/odd cells, random boxes elsewhere, spawn corners kept clear. Indexed [r, c].</summary>
        public static Cell[,] MakeGrid(uint seed, double density = 0.72)
        {
            var rnd = Rng(seed);
            var g = new Cell[ROWS, COLS];
            for (int y = 0; y < ROWS; y++)
                for (int x = 0; x < COLS; x++)
                {
                    if (x % 2 == 1 && y % 2 == 1) { g[y, x] = Cell.Solid; continue; }
                    bool nearSpawn = SPAWNS.Any(s => Math.Abs(s.C - x) + Math.Abs(s.R - y) <= 2);
                    g[y, x] = !nearSpawn && rnd() < density ? Cell.Box : Cell.Empty;
                }
            return g;
        }

        public static int BalloonAt(IList<BalloonLike> bs, int c, int r)
        {
            for (int i = 0; i < bs.Count; i++) if (bs[i].C == c && bs[i].R == r) return i;
            return -1;
        }

        public static bool Passable(Cell[,] g, IList<BalloonLike> bs, int c, int r) =>
            InBounds(c, r) && g[r, c] == Cell.Empty && BalloonAt(bs, c, r) < 0;

        static readonly Pt[] DIRS = { new Pt(1, 0), new Pt(-1, 0), new Pt(0, 1), new Pt(0, -1) };

        public class BlastResult { public List<Pt> Tiles = new List<Pt>(), Boxes = new List<Pt>(); public List<int> Chained = new List<int>(); }

        /// <summary>Water from one balloon: centre + 4 arms. Arms stop at pillars, soak (and stop at) the first box, and stop on another balloon (which chains).</summary>
        public static BlastResult Blast(Cell[,] g, IList<BalloonLike> bs, int bc, int br, int range)
        {
            var res = new BlastResult();
            res.Tiles.Add(new Pt(bc, br));
            foreach (var d in DIRS)
                for (int k = 1; k <= range; k++)
                {
                    int c = bc + d.C * k, r = br + d.R * k;
                    if (!InBounds(c, r) || g[r, c] == Cell.Solid) break;
                    res.Tiles.Add(new Pt(c, r));
                    if (g[r, c] == Cell.Box) { res.Boxes.Add(new Pt(c, r)); break; }
                    int j = BalloonAt(bs, c, r);
                    if (j >= 0) { res.Chained.Add(j); break; }
                }
            return res;
        }
        public static BlastResult Blast(Cell[,] g, IList<BalloonLike> bs, BalloonLike b) => Blast(g, bs, b.C, b.R, b.Range);

        public class ChainResult { public List<int> Balloons; public List<Pt> Tiles, Boxes; }

        /// <summary>Explode balloon i and everything it chains into.</summary>
        public static ChainResult Chain(Cell[,] g, IList<BalloonLike> bs, int i)
        {
            var done = new List<int> { i }; var queue = new Queue<int>(); queue.Enqueue(i);
            var tiles = new Dictionary<int, Pt>(); var boxes = new Dictionary<int, Pt>();
            var tileOrder = new List<int>(); var boxOrder = new List<int>();
            while (queue.Count > 0)
            {
                int b = queue.Dequeue();
                var res = Blast(g, bs, bs[b]);
                foreach (var t in res.Tiles) { int k = Key(t.C, t.R); if (!tiles.ContainsKey(k)) tileOrder.Add(k); tiles[k] = t; }
                foreach (var t in res.Boxes) { int k = Key(t.C, t.R); if (!boxes.ContainsKey(k)) boxOrder.Add(k); boxes[k] = t; }
                foreach (int j in res.Chained) if (!done.Contains(j)) { done.Add(j); queue.Enqueue(j); }
            }
            return new ChainResult
            {
                Balloons = done,
                Tiles = tileOrder.Select(k => tiles[k]).ToList(),
                Boxes = boxOrder.Select(k => boxes[k]).ToList(),
            };
        }

        /// <summary>Seconds until each cell gets wet (Infinity = safe). Chain reactions shorten fuses; wet cells are 0.</summary>
        public static float[] DangerMap(Cell[,] g, IList<BalloonLike> bs, IEnumerable<int> wet = null)
        {
            var te = bs.Select(b => b.T).ToArray();
            for (int pass = 0; pass < bs.Count; pass++)
            {
                bool changed = false;
                for (int i = 0; i < bs.Count; i++)
                    foreach (int j in Blast(g, bs, bs[i]).Chained)
                        if (te[i] < te[j]) { te[j] = te[i]; changed = true; }
                if (!changed) break;
            }
            var d = new float[COLS * ROWS];
            for (int k = 0; k < d.Length; k++) d[k] = float.PositiveInfinity;
            for (int i = 0; i < bs.Count; i++)
                foreach (var t in Blast(g, bs, bs[i]).Tiles) { int k = Key(t.C, t.R); d[k] = Math.Min(d[k], te[i]); }
            if (wet != null) foreach (int k in wet) d[k] = 0;
            return d;
        }

        /// <summary>Breadth-first path (cells after `from`) to the nearest cell matching goal; ok(c, r, steps) filters walkable cells.</summary>
        public static List<Pt> Bfs(Cell[,] g, IList<BalloonLike> bs, Pt from, Func<int, int, bool> goal, Func<int, int, int, bool> ok = null, int maxSteps = 60)
        {
            ok ??= (c, r, s) => true;
            var prev = new Dictionary<int, int>(); int start = Key(from.C, from.R);
            prev[start] = -1;
            var frontier = new List<Pt> { from };
            if (goal(from.C, from.R)) return new List<Pt>();
            for (int steps = 1; steps <= maxSteps && frontier.Count > 0; steps++)
            {
                var next = new List<Pt>();
                foreach (var p in frontier)
                    foreach (var d in DIRS)
                    {
                        int nc = p.C + d.C, nr = p.R + d.R, k = Key(nc, nr);
                        if (prev.ContainsKey(k) || !Passable(g, bs, nc, nr) || !ok(nc, nr, steps)) continue;
                        prev[k] = Key(p.C, p.R);
                        if (goal(nc, nr))
                        {
                            var path = new List<Pt>();
                            for (int q = k; q != start; q = prev[q]) path.Insert(0, new Pt(q % COLS, q / COLS));
                            return path;
                        }
                        next.Add(new Pt(nc, nr));
                    }
                frontier = next;
            }
            return null;
        }

        /// <summary>Is it safe to stand on (c,r) after `steps` moves at `speed` tiles/s given danger d?</summary>
        static bool SafeAt(float[] d, int c, int r, int steps, float speed)
        {
            float arrive = steps / speed, t = d[Key(c, r)];
            return float.IsPositiveInfinity(t) || arrive > t + WATER_TIME + 0.15f || arrive < t - 0.45f;
        }

        public static BotAction BotDecide(BotView v, double roll = 0.5)
        {
            var g = v.Grid; var bs = v.Balloons; var me = v.Me;
            var from = new Pt(me.C, me.R);
            var d = DangerMap(g, bs, v.Wet);
            bool Walk(int c, int r, int s) => SafeAt(d, c, r, s, me.Speed);
            bool Safe(float[] dd, int c, int r) => float.IsPositiveInfinity(dd[Key(c, r)]);
            // 1) Get out of the splash zone.
            if (!Safe(d, me.C, me.R))
            {
                var p = Bfs(g, bs, from, (c, r) => Safe(d, c, r), Walk, 12);
                return p != null ? new BotAction { Kind = BotKind.Move, Path = p } : BotAction.Idle;
            }
            // 2) Pop a trapped rival.
            var trapped = v.Foes.Where(f => f.Trapped).ToList();
            if (trapped.Count > 0)
            {
                var p = Bfs(g, bs, from, (c, r) => trapped.Any(f => f.C == c && f.R == r), (c, r, s) => Walk(c, r, s) && Safe(d, c, r), 14);
                if (p != null) return new BotAction { Kind = BotKind.Move, Path = p };
            }
            // 3) Drop a balloon if it would soak a box or a rival AND there is an escape.
            if (me.Left > 0)
            {
                var hypo = new List<BalloonLike>(bs) { new BalloonLike(me.C, me.R, FUSE, me.Range) };
                var res = Blast(g, bs, me.C, me.R, me.Range);
                bool hitsFoe = v.Foes.Any(f => !f.Trapped && res.Tiles.Any(t => t.C == f.C && t.R == f.R));
                bool worth = hitsFoe || res.Boxes.Count > 0;
                if (worth && (hitsFoe || roll < 0.85))
                {
                    var d2 = DangerMap(g, hypo, v.Wet);
                    var esc = Bfs(g, hypo, from, (c, r) => Safe(d2, c, r), (c, r, s) => SafeAt(d2, c, r, s, me.Speed) && s / me.Speed < FUSE - 0.5f, 10);
                    if (esc != null && esc.Count > 0) return new BotAction { Kind = BotKind.Bomb, Path = esc };
                }
            }
            bool SafeWalk(int c, int r, int s) => Walk(c, r, s) && Safe(d, c, r);
            // 4) Grab a nearby item.
            var item = Bfs(g, bs, from, (c, r) => v.Items.Any(it => it.C == c && it.R == r), SafeWalk, 9);
            if (item != null) return new BotAction { Kind = BotKind.Move, Path = item };
            // 5) Walk next to a box, or toward the nearest rival.
            bool NextToBox(int c, int r) => DIRS.Any(dd => InBounds(c + dd.C, r + dd.R) && g[r + dd.R, c + dd.C] == Cell.Box);
            List<Pt> target = roll < 0.35 && v.Foes.Count > 0
                ? Bfs(g, bs, from, (c, r) => v.Foes.Any(f => Math.Abs(f.C - c) + Math.Abs(f.R - r) <= 1), SafeWalk, 40)
                : Bfs(g, bs, from, NextToBox, SafeWalk, 40) ?? Bfs(g, bs, from, (c, r) => v.Foes.Any(f => Math.Abs(f.C - c) + Math.Abs(f.R - r) <= 2), SafeWalk, 40);
            if (target != null && target.Count > 0) return new BotAction { Kind = BotKind.Move, Path = target.Take(6).ToList() };
            return BotAction.Idle;
        }

        public static float SpeedOf(int level) => BASE_SPEED + Math.Min(MAX_SPEED_LV, level) * SPEED_STEP;

        /// <summary>Match winner: first to WINS_NEEDED; after 3 rounds a sole leader wins; still tied after 5 → -1 (draw); otherwise null (keep playing).</summary>
        public static int? MatchWinner(int[] wins, int roundsPlayed)
        {
            int w = Array.FindIndex(wins, n => n >= WINS_NEEDED);
            if (w >= 0) return w;
            if (roundsPlayed < 3) return null;
            int best = wins.Max();
            var top = Enumerable.Range(0, wins.Length).Where(i => wins[i] == best).ToList();
            if (top.Count == 1 && best > 0) return top[0];
            return roundsPlayed >= 5 ? -1 : (int?)null;
        }
    }
}
