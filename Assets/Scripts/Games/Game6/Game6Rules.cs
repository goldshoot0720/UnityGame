// Board rules for 萌友戰棋・八方對決 — 1:1 port of Game6/cloud/src/tactics.ts (pure logic).
using System;
using System.Collections.Generic;
using System.Linq;

namespace MoeGames.Game6
{
    public class CharDef
    {
        public string Key, Name, Title, Role, Color, Desc;
        public int Hp, Atk, Def, Mov, Rmin, Rmax;
        public bool Swim, Magic;
        public int Heal;       // 0 = cannot heal
        public double Crit;    // 0 = never crits
    }

    public class Terrain
    {
        public string Name;
        public int Cost, Def, Heal;
        public Terrain(string name, int cost, int def, int heal = 0) { Name = name; Cost = cost; Def = def; Heal = heal; }
    }

    public class MapDef
    {
        public string Name;
        public string[] Rows;
        public (int x, int y)[] P, E;
    }

    public class Unit
    {
        public int Id;
        public CharDef C;
        public char Team; // 'P' or 'E'
        public int X, Y, Hp;
        public bool Acted;
    }

    public class Node
    {
        public int X, Y, C;
        public string Prev;
        public bool Blocked;
    }

    public struct Damage { public int Dmg; public bool Crit; }

    public struct CombatResult
    {
        public int Dmg, Counter;
        public bool Crit, Killed, CounterCrit, Died;
    }

    public class AiPlan
    {
        public (int x, int y) Dest;
        public List<(int x, int y)> Path;
        public string Type; // "atk" | "heal" | "move"
        public Unit Target;
    }

    public static class Tactics
    {
        public static readonly CharDef[] CHARS =
        {
            new CharDef { Key = "whale", Name = "汐音", Title = "鯨之女僕", Role = "治療師", Hp = 22, Atk = 5, Def = 3, Mov = 4, Rmin = 1, Rmax = 2, Swim = true, Heal = 8, Color = "#5ab0ff", Desc = "可為友軍恢復 8 HP，並能在水面上行走。" },
            new CharDef { Key = "penguin", Name = "小冰", Title = "企鵝衛士", Role = "重裝", Hp = 32, Atk = 7, Def = 7, Mov = 3, Rmin = 1, Rmax = 1, Color = "#f2c14e", Desc = "血量與防禦極高的前線坦克。" },
            new CharDef { Key = "glasses", Name = "光哉", Title = "眼鏡軍師", Role = "弓手", Hp = 20, Atk = 8, Def = 3, Mov = 4, Rmin = 2, Rmax = 3, Color = "#d2b48c", Desc = "射程 2-3 的遠程攻擊，無法反擊貼身敵人。" },
            new CharDef { Key = "tshirt", Name = "阿翔", Title = "熱血少年", Role = "格鬥家", Hp = 27, Atk = 9, Def = 4, Mov = 5, Rmin = 1, Rmax = 1, Color = "#9aa0a6", Desc = "攻守均衡、耐打的近戰突擊手。" },
            new CharDef { Key = "calico", Name = "小花", Title = "街頭貓俠", Role = "盜賊", Hp = 21, Atk = 8, Def = 3, Mov = 6, Rmin = 1, Rmax = 1, Crit = 0.3, Color = "#f08a3c", Desc = "移動力 6，攻擊有 30% 機率造成雙倍爆擊。" },
            new CharDef { Key = "whitecat", Name = "書白", Title = "書庫魔導", Role = "魔法師", Hp = 18, Atk = 10, Def = 2, Mov = 4, Rmin = 1, Rmax = 2, Magic = true, Color = "#b57bff", Desc = "魔法攻擊無視目標一半的防禦。" },
            new CharDef { Key = "redcat", Name = "緋音", Title = "紅焰劍士", Role = "劍士", Hp = 24, Atk = 10, Def = 4, Mov = 5, Rmin = 1, Rmax = 1, Color = "#e0443e", Desc = "攻擊力出眾的近戰劍士。" },
            new CharDef { Key = "sailor", Name = "澪", Title = "水手長槍", Role = "槍兵", Hp = 25, Atk = 8, Def = 5, Mov = 4, Rmin = 1, Rmax = 2, Color = "#3a5ba0", Desc = "射程 1-2 的長槍，攻守兼備。" },
        };

        public static readonly Dictionary<char, Terrain> TERRAIN = new Dictionary<char, Terrain>
        {
            ['.'] = new Terrain("草地", 1, 0),
            ['F'] = new Terrain("森林", 2, 2),
            ['W'] = new Terrain("水域", 99, 0),
            ['M'] = new Terrain("山岳", 3, 3),
            ['H'] = new Terrain("民房", 1, 1, 5),
        };

        public static readonly MapDef[] MAPS =
        {
            new MapDef { Name = "綠野平原", Rows = new[] { "..F....M..F.", ".FF..W....F.", "....WW..H...", "M...W...FF..", "..F.......M.", "..FF...W....", "...H..WW....", ".F....W..FF.", ".F..M....F.." },
                P = new[] { (1, 2), (0, 4), (1, 6), (0, 7) }, E = new[] { (11, 1), (10, 3), (11, 5), (10, 7) } },
            new MapDef { Name = "湖畔小鎮", Rows = new[] { "...F..H...F.", ".H..WWWW..F.", "...WWWWWW...", "F..WW..WW..M", "..F...H...F.", "M..WW..WW..F", "...WWWWWW...", ".F..WWWW..H.", ".F...H..F..." },
                P = new[] { (0, 1), (1, 4), (0, 6), (1, 8) }, E = new[] { (11, 0), (10, 3), (11, 6), (10, 8) } },
            new MapDef { Name = "霧隱山谷", Rows = new[] { "MMM..F..MMMM", "M...FF....MM", "..F....F....", ".F..MMM..H..", "....M.M.....", "..H..MMM..F.", "....F....F..", "MM....FF...M", "MMMM..F..MMM" },
                P = new[] { (4, 0), (1, 2), (0, 4), (2, 6) }, E = new[] { (7, 8), (10, 6), (11, 4), (9, 2) } },
        };

        public const int COLS = 12, ROWS = 9;
        public static readonly (int dx, int dy)[] DIRS = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        public static CharDef Char(string key) => CHARS.First(c => c.Key == key);
        public static string Key(int x, int y) => x + "," + y;
    }

    public class Board
    {
        public readonly List<Unit> Units = new List<Unit>();
        public readonly MapDef Map;

        public Board(MapDef map) { Map = map; }

        public static Board Create(MapDef map, IList<string> pKeys, IList<string> eKeys)
        {
            var b = new Board(map);
            int id = 0;
            for (int i = 0; i < pKeys.Count; i++) b.Units.Add(new Unit { Id = id++, C = Tactics.Char(pKeys[i]), Team = 'P', X = map.P[i].x, Y = map.P[i].y });
            for (int i = 0; i < eKeys.Count; i++) b.Units.Add(new Unit { Id = id++, C = Tactics.Char(eKeys[i]), Team = 'E', X = map.E[i].x, Y = map.E[i].y });
            foreach (var u in b.Units) u.Hp = u.C.Hp;
            return b;
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Tactics.COLS && y < Tactics.ROWS;
        public Terrain TerrainAt(int x, int y) => Tactics.TERRAIN.TryGetValue(Map.Rows[y][x], out var t) ? t : Tactics.TERRAIN['.'];
        public int TerrainDef(int x, int y) => TerrainAt(x, y).Def;

        public int MoveCost(Unit u, int x, int y)
        {
            char t = Map.Rows[y][x];
            if (t == 'W') return u.C.Swim ? 1 : 99;
            return TerrainAt(x, y).Cost;
        }

        public Unit UnitAt(int x, int y) => Units.FirstOrDefault(u => u.Hp > 0 && u.X == x && u.Y == y);
        public List<Unit> Team(char t) => Units.Where(u => u.Team == t && u.Hp > 0).ToList();
        public static int Dist(int ax, int ay, int bx, int by) => Math.Abs(ax - bx) + Math.Abs(ay - by);
        public int Dist(Unit a, Unit b) => Dist(a.X, a.Y, b.X, b.Y);

        /// <summary>Dijkstra over terrain costs; enemies block, allies can be passed but not stopped on.</summary>
        public OrderedMap<string, Node> Reachable(Unit u)
        {
            var output = new OrderedMap<string, Node>();
            var start = new Node { X = u.X, Y = u.Y, C = 0, Prev = null, Blocked = false };
            output.Set(Tactics.Key(u.X, u.Y), start);
            var q = new List<Node> { start };
            while (q.Count > 0)
            {
                var cur = Js.ShiftMin(q, n => n.C);
                foreach (var (dx, dy) in Tactics.DIRS)
                {
                    int nx = cur.X + dx, ny = cur.Y + dy;
                    if (!InBounds(nx, ny)) continue;
                    var o = UnitAt(nx, ny);
                    if (o != null && o.Team != u.Team) continue;
                    int nc = cur.C + MoveCost(u, nx, ny);
                    if (nc > u.C.Mov) continue;
                    string k = Tactics.Key(nx, ny);
                    if (output.TryGet(k, out var ex) && ex.C <= nc) continue;
                    var n = new Node { X = nx, Y = ny, C = nc, Prev = Tactics.Key(cur.X, cur.Y), Blocked = o != null && o != u };
                    output.Set(k, n);
                    q.Add(n);
                }
            }
            return output;
        }

        public List<(int x, int y)> PathTo(OrderedMap<string, Node> reach, int x, int y)
        {
            var path = new List<(int x, int y)>();
            string k = Tactics.Key(x, y);
            while (k != null)
            {
                if (!reach.TryGet(k, out var n)) break;
                path.Insert(0, (n.X, n.Y));
                k = n.Prev;
            }
            return path;
        }

        public bool InRange(Unit a, int tx, int ty, int fx, int fy)
        {
            int d = Math.Abs(fx - tx) + Math.Abs(fy - ty);
            return d >= a.C.Rmin && d <= a.C.Rmax;
        }
        public bool InRange(Unit a, Unit t) => InRange(a, t.X, t.Y, a.X, a.Y);
        public bool InRange(Unit a, Unit t, int fx, int fy) => InRange(a, t.X, t.Y, fx, fy);

        public Damage CalcDamage(Unit a, Unit d, Func<double> rng = null) => CalcDamage(a, d, rng, d.X, d.Y);

        public Damage CalcDamage(Unit a, Unit d, Func<double> rng, int dx, int dy)
        {
            int dv = d.C.Def + TerrainDef(dx, dy);
            if (a.C.Magic) dv = (int)Math.Floor(dv / 2.0);
            int dmg = Math.Max(1, a.C.Atk - dv);
            bool crit = rng != null && a.C.Crit > 0 && rng() < a.C.Crit;
            if (crit) dmg *= 2;
            return new Damage { Dmg = dmg, Crit = crit };
        }

        /// <summary>Attack + possible counter. Mutates hp; returns what happened.</summary>
        public CombatResult Combat(Unit a, Unit d, Func<double> rng)
        {
            var r1 = CalcDamage(a, d, rng);
            d.Hp = Math.Max(0, d.Hp - r1.Dmg);
            int counter = 0;
            bool counterCrit = false;
            if (d.Hp > 0 && InRange(d, a))
            {
                var r2 = CalcDamage(d, a, rng);
                counter = r2.Dmg;
                counterCrit = r2.Crit;
                a.Hp = Math.Max(0, a.Hp - counter);
            }
            return new CombatResult { Dmg = r1.Dmg, Crit = r1.Crit, Killed = d.Hp <= 0, Counter = counter, CounterCrit = counterCrit, Died = a.Hp <= 0 };
        }

        public int Heal(Unit a, Unit t)
        {
            int amt = Math.Min(a.C.Heal, t.C.Hp - t.Hp);
            t.Hp += amt;
            return amt;
        }

        /// <summary>Units of <paramref name="team"/> standing on houses recover at the start of their phase.</summary>
        public List<(Unit u, int amt)> PhaseHeal(char team)
        {
            var output = new List<(Unit u, int amt)>();
            foreach (var u in Team(team))
            {
                int h = TerrainAt(u.X, u.Y).Heal;
                if (h > 0 && u.Hp < u.C.Hp)
                {
                    int amt = Math.Min(h, u.C.Hp - u.Hp);
                    u.Hp += amt;
                    output.Add((u, amt));
                }
            }
            return output;
        }

        /// <summary>'P', 'E' or '\0' (no winner yet).</summary>
        public char Winner()
        {
            if (Team('E').Count == 0) return 'P';
            if (Team('P').Count == 0) return 'E';
            return '\0';
        }

        /// <summary>Enemy AI: score every reachable tile × action, else march along a terrain distance field.</summary>
        public AiPlan Plan(Unit e)
        {
            var reach = Reachable(e);
            var foes = Team(e.Team == 'E' ? 'P' : 'E');
            var allies = Team(e.Team).Where(o => o != e).ToList();
            double bestS = 0;
            Node bestN = null;
            string bestType = null;
            Unit bestT = null;
            foreach (var n in reach.Values)
            {
                if (n.Blocked) continue;
                double tb = TerrainDef(n.X, n.Y) * 1.5 - n.C * 0.05;
                if (e.C.Heal > 0)
                {
                    foreach (var al in allies)
                    {
                        int miss = al.C.Hp - al.Hp;
                        if (miss <= 0 || !InRange(e, al, n.X, n.Y)) continue;
                        double s = Math.Min(e.C.Heal, miss) * 2.5 + (al.Hp < al.C.Hp * 0.5 ? 15 : 0) + tb;
                        if (bestN == null || s > bestS) { bestS = s; bestN = n; bestType = "heal"; bestT = al; }
                    }
                }
                foreach (var f in foes)
                {
                    if (!InRange(e, f, n.X, n.Y)) continue;
                    int dmg = CalcDamage(e, f).Dmg;
                    bool kill = dmg >= f.Hp;
                    int counter = !kill && InRange(f, n.X, n.Y, f.X, f.Y) ? CalcDamage(f, e, null, n.X, n.Y).Dmg : 0;
                    double s = dmg + (kill ? 40 : 0) - counter * 0.7 + tb + (f.C.Heal > 0 ? 4 : 0) + (1 - (double)f.Hp / f.C.Hp) * 6;
                    if (bestN == null || s > bestS) { bestS = s; bestN = n; bestType = "atk"; bestT = f; }
                }
            }
            if (bestN != null)
                return new AiPlan { Dest = (bestN.X, bestN.Y), Path = PathTo(reach, bestN.X, bestN.Y), Type = bestType, Target = bestT };
            var field = DistanceField(e, foes);
            Node dest = null;
            int destV = 0;
            foreach (var n in reach.Values)
            {
                if (n.Blocked) continue;
                int v = field.Get(Tactics.Key(n.X, n.Y), 999);
                if (dest == null || v < destV || (v == destV && n.C < dest.C)) { destV = v; dest = n; }
            }
            int dxp = dest != null ? dest.X : e.X, dyp = dest != null ? dest.Y : e.Y;
            return new AiPlan { Dest = (dxp, dyp), Path = PathTo(reach, dxp, dyp), Type = "move", Target = null };
        }

        public OrderedMap<string, int> DistanceField(Unit u, List<Unit> targets)
        {
            var d = new OrderedMap<string, int>();
            var q = new List<(int x, int y, int c)>();
            foreach (var t in targets) { d.Set(Tactics.Key(t.X, t.Y), 0); q.Add((t.X, t.Y, 0)); }
            while (q.Count > 0)
            {
                var cur = Js.ShiftMin(q, a => a.c);
                foreach (var (dx, dy) in Tactics.DIRS)
                {
                    int nx = cur.x + dx, ny = cur.y + dy;
                    if (!InBounds(nx, ny)) continue;
                    int cost = MoveCost(u, nx, ny);
                    if (cost >= 99) continue;
                    int nc = cur.c + cost;
                    string k = Tactics.Key(nx, ny);
                    if (d.TryGet(k, out int old) && old <= nc) continue;
                    d.Set(k, nc);
                    q.Add((nx, ny, nc));
                }
            }
            return d;
        }
    }
}
