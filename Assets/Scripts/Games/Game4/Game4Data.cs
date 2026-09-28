// 萌友洛克英雄 — data, level builder and save progress ported 1:1 from Game4/cloud/src
// (data.ts, levels.ts, progress.ts). Pure logic.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MoeGames.Game4
{
    public enum WeaponKind { Buster, Bubble, Ice, Bounce, Rapid, Claw, Book, Fire3, Whirl }

    public class Palette { public string Sky, Sky2, Ground, Top, Accent, Detail; }

    public class Weapon { public string Name; public WeaponKind Kind; public string Color; public double Cost; public string Desc; }

    public class Hero
    {
        public string Key, Name, Title, Stage, WeakTo;
        public Palette Palette;
        public Weapon Weapon;
    }

    public class BossPhase { public string Name; public int Hp; public string WeakTo; public WeaponKind AttackKind; public string Color; }

    public static class Data
    {
        public const int TILE = 32;
        public const int ROWS = 14;
        public const int VIEW_H = ROWS * TILE; // 448 — the world height
        public const double GRAVITY = 1150;

        public static class PLAYER
        {
            public const double h = 64, hitW = 20, hitH = 50, slideH = 24;
            public const double run = 165, jump = 510, jumpCut = 160, coyote = 0.09;
            public const double slideSpeed = 320, slideTime = 0.32, climb = 120;
            public const int maxHp = 28, maxAmmo = 28, lives = 3;
            public const double iframes = 1.3, knockback = 0.38;
            public const double chargeStart = 0.26, chargeMid = 0.7, chargeFull = 1.4;
        }

        public static class BOSS
        {
            public const double h = 104, hitW = 40, hitH = 92;
            public const int maxHp = 28, rushHp = 14, rushBossHp = 14, contact = 4;
            public const double iframes = 0.45;
        }

        public static class E_TANK { public const int max = 9, citadelGrant = 3; }
        public static class M_TANK { public const int max = 9, citadelGrant = 3; }

        public static (int eTanks, int mTanks) CitadelTankGrant(int eTanks, int mTanks)
            => (Math.Min(E_TANK.max, eTanks + E_TANK.citadelGrant), Math.Min(M_TANK.max, mTanks + M_TANK.citadelGrant));

        public static class FORTRESS_REWARDS
        {
            public static readonly int heal = (int)Math.Ceiling(PLAYER.maxHp * 0.6);
            public const int lifeEvery = 3;
        }

        public static (int hp, int lives, bool extraLife) FortressBossReward(int hp, int lives, int defeated)
        {
            bool extraLife = defeated > 0 && defeated % FORTRESS_REWARDS.lifeEvery == 0;
            return (Math.Min(PLAYER.maxHp, hp + FORTRESS_REWARDS.heal), lives + (extraLife ? 1 : 0), extraLife);
        }

        static Palette Pal(string sky, string sky2, string ground, string top, string accent, string detail)
            => new Palette { Sky = sky, Sky2 = sky2, Ground = ground, Top = top, Accent = accent, Detail = detail };

        public static readonly Hero[] CHARACTERS =
        {
            new Hero { Key = "whale", Name = "汐音", Title = "鯨魚女僕", Stage = "鯨歌海底神殿", WeakTo = "penguin",
                Palette = Pal("#0b2a4a", "#14568a", "#1d4f7a", "#5fc6e8", "#9ee7ff", "#0e3656"),
                Weapon = new Weapon { Name = "泡泡水柱", Kind = WeaponKind.Bubble, Color = "#6fd6ff", Cost = 1, Desc = "緩緩漂浮前進的水泡。" } },
            new Hero { Key = "penguin", Name = "小冰", Title = "企鵝少女", Stage = "冰原企鵝基地", WeakTo = "calico",
                Palette = Pal("#2a3f66", "#8fb5dd", "#6f8fb8", "#e8f6ff", "#bfe9ff", "#4b6a93"),
                Weapon = new Weapon { Name = "冰晶飛鏢", Kind = WeaponKind.Ice, Color = "#bff4ff", Cost = 1, Desc = "高速貫穿敵人的冰晶。" } },
            new Hero { Key = "glasses", Name = "光哉", Title = "眼鏡學長", Stage = "光學研究所", WeakTo = "sailor",
                Palette = Pal("#1b1f2b", "#3b4458", "#6d6552", "#d9c9a0", "#ffe27a", "#4a4436"),
                Weapon = new Weapon { Name = "反射光彈", Kind = WeaponKind.Bounce, Color = "#ffe27a", Cost = 1, Desc = "碰到牆壁與地面會反彈的光球。" } },
            new Hero { Key = "tshirt", Name = "阿翔", Title = "T恤少年", Stage = "霓虹街區", WeakTo = "glasses",
                Palette = Pal("#1a1030", "#3d2a6b", "#4a4f5c", "#9aa3b5", "#ff5fa8", "#2c3039"),
                Weapon = new Weapon { Name = "連射飛彈", Kind = WeaponKind.Rapid, Color = "#ffffff", Cost = 0.5, Desc = "射速極快的小型飛彈。" } },
            new Hero { Key = "calico", Name = "小花", Title = "夾克三花貓", Stage = "廢棄工廠", WeakTo = "library",
                Palette = Pal("#1a1210", "#5a3420", "#4d3a2e", "#d49a5a", "#ffb347", "#2e221b"),
                Weapon = new Weapon { Name = "貓爪衝擊波", Kind = WeaponKind.Claw, Color = "#ffb347", Cost = 2, Desc = "沿著地面奔馳的巨大爪痕。" } },
            new Hero { Key = "library", Name = "書白", Title = "圖書館貓", Stage = "無盡圖書館", WeakTo = "redcat",
                Palette = Pal("#2b1d14", "#6b4a2b", "#5b3a22", "#c58b4e", "#f2e2b6", "#3a2515"),
                Weapon = new Weapon { Name = "迴旋書本", Kind = WeaponKind.Book, Color = "#f2e2b6", Cost = 1, Desc = "飛出後會折返的厚重書本。" } },
            new Hero { Key = "redcat", Name = "緋音", Title = "紅髮貓耳少女", Stage = "熔岩火山", WeakTo = "whale",
                Palette = Pal("#2a0a0a", "#7a1f12", "#3a1f1f", "#ff6a2a", "#ffc04a", "#241212"),
                Weapon = new Weapon { Name = "火焰三連", Kind = WeaponKind.Fire3, Color = "#ff7a3a", Cost = 2, Desc = "同時向前方射出三道火焰。" } },
            new Hero { Key = "sailor", Name = "澪", Title = "水手服少女", Stage = "暴風港灣", WeakTo = "tshirt",
                Palette = Pal("#2d4b73", "#9cc3e6", "#3e5b7a", "#dfeefa", "#a8e0ff", "#2a4059"),
                Weapon = new Weapon { Name = "旋風", Kind = WeaponKind.Whirl, Color = "#c8f0ff", Cost = 2, Desc = "斜向上升的旋風。" } },
        };

        public const string FINAL_STAGE = "最終要塞", FINAL_TITLE = "連續頭目戰";
        public static readonly Palette FINAL_PALETTE = Pal("#0a0a14", "#2a1840", "#2f2f3f", "#b28bff", "#ff5fd2", "#1c1c28");
        public const string CITADEL_STAGE = "要塞核心", CITADEL_TITLE = "終焉守護者・三段變形";
        public static readonly Palette CITADEL_PALETTE = Pal("#090b20", "#2a1950", "#292941", "#78dcef", "#ffca68", "#151529");

        public static readonly BossPhase[] FINAL_BOSS_PHASES =
        {
            new BossPhase { Name = "晶盾型態", Hp = 28, WeakTo = "whale", AttackKind = WeaponKind.Ice, Color = "#66dfff" },
            new BossPhase { Name = "翼刃型態", Hp = 32, WeakTo = "penguin", AttackKind = WeaponKind.Bounce, Color = "#ffcb62" },
            new BossPhase { Name = "熾核型態", Hp = 36, WeakTo = "redcat", AttackKind = WeaponKind.Fire3, Color = "#ff6b64" },
        };

        public static int FinalBossDamage(int phase, string weaponKey, int chargeLevel)
        {
            if (phase < 1 || phase > FINAL_BOSS_PHASES.Length) throw new ArgumentException($"Unknown final boss phase: {phase}");
            var form = FINAL_BOSS_PHASES[phase - 1];
            if (weaponKey == form.WeakTo) return 4;
            if (weaponKey == "buster") return chargeLevel >= 2 ? 3 : chargeLevel == 1 ? 2 : 1;
            return 1;
        }

        public static readonly Weapon BUSTER = new Weapon { Name = "基本射擊", Kind = WeaponKind.Buster, Color = "#fff3a0", Cost = 0 };

        public static Hero GetHero(string key) => CHARACTERS.FirstOrDefault(c => c.Key == key) ?? CHARACTERS[0];
        public static List<Hero> BossesFor(string heroKey) => CHARACTERS.Where(c => c.Key != heroKey).ToList();

        /// <summary>Weapon key that deals weakness damage to bossKey, or "charge" when that weapon is the hero's own.</summary>
        public static string WeaknessFor(string bossKey, string heroKey)
        {
            string weak = GetHero(bossKey).WeakTo;
            return weak == heroKey ? "charge" : weak;
        }

        /// <summary>Damage a player shot deals to a boss.</summary>
        public static int BossDamage(string bossKey, string heroKey, string weaponKey, int chargeLevel)
        {
            string weak = WeaknessFor(bossKey, heroKey);
            if (weaponKey == "buster")
            {
                if (weak == "charge" && chargeLevel >= 2) return 4;
                return chargeLevel >= 2 ? 3 : chargeLevel == 1 ? 2 : 1;
            }
            return weaponKey == weak ? 4 : 1;
        }

        /// <summary>Character model id for a hero key (the library cat is "whitecat" in the cast).</summary>
        public static string ModelId(string key) => key == "library" ? "whitecat" : key;
    }

    // ── levels ──

    public class Spawn { public string Type; public int Col, Row; }

    public class Level
    {
        public string Key;
        public int Cols, Rows;
        public int[][] Tiles;
        public List<Spawn> Entities;
        public (int col, int row) Start;
        public List<(int col, int row)> Checkpoints;
        public int RoomCol;
        public int[] DoorRows;
    }

    /// <summary>Legend: . empty  # solid  ^ spikes  H ladder  - jump-through platform
    /// W walker  F flyer  T turret  M/N sideways platform (7/12 tiles)  V lift
    /// C checkpoint  h health  a weapon energy  P player start</summary>
    public static class Levels
    {
        public const int T_EMPTY = -1, T_GROUND = 0, T_TOP = 1, T_SPIKE = 2, T_LADDER = 3, T_LADDER_TOP = 4, T_PLATFORM = 5, T_WALL = 6;

        static string[] Floor(int w, params string[] above)
        {
            var f = new string('#', w);
            return above.Concat(new[] { f, f, f }).ToArray();
        }

        public static readonly Dictionary<string, string[]> CHUNKS = new Dictionary<string, string[]>
        {
            ["start"] = Floor(12, ".P.........."),
            ["flat"] = Floor(16, "......W........."),
            ["gauntlet"] = Floor(20, "...W.....W.....W...."),
            ["steps"] = Floor(18, "........W.........", "......######......", "...############...", "...############..."),
            ["pit"] = new[] { "......F.......", "..............", "..............", "..............", "..............", "#####...######", "#####...######", "#####...######" },
            ["doublePit"] = new[] { ".......F..........", "..................", "..................", "..................", "..................", "####...###...#####", "####...###...#####", "####...###...#####" },
            ["spikePit"] = new[] { "................", "......---.......", "................", "................", "###^^^^^^^^^####", "################", "################" },
            ["spikeRun"] = new[] { "....#...#...#.......", "....#...#...#.......", "##^^#^^^#^^^#^^#####", "####################", "####################" },
            ["mover"] = new[] { "....................", "####.M..........####", "####............####", "####............####" },
            ["lift"] = Floor(16, "...........T....", "..........######", "..........######", "..........######", "..........######", "......V...######"),
            ["ladderUp"] = Floor(18, "..........W.......", "..####H###########", "......H.....######", "......H.....######", "......H.....######", "......H.....######", "......H.....######"),
            ["turretWall"] = Floor(16, ".........T......", "........#####...", "........#####..."),
            ["flyers"] = Floor(20, "......F.......F.....", "....................", "....................", "..........---.......", "....................", "....---.............", "...................."),
            ["lowTunnel"] = Floor(18, "....######........", "....######........", "....######........", "....######........", "....######........", "....######........", "....######........", ".................."),
            ["checkpoint"] = Floor(10, "....C....."),
            ["health"] = Floor(10, "....h..a.."),
            ["bigMover"] = new[] { "..........F.............", "........................", "........................", "........................", "###..N..............####", "###.................####", "###.................####" },
            ["towers"] = Floor(20, "....F.....T.....F...", ".........###........", ".........###........", ".....#...###...#....", ".....#...###...#...."),
            ["ladderTall"] = Floor(16, "......W.........", "...######H######", ".........H...###", ".........H...###", "..---....H...###", ".........H...###", ".........H...###", ".........H...###", ".........H...###"),
        };

        public static readonly Dictionary<string, string[]> STAGE_LAYOUTS = new Dictionary<string, string[]>
        {
            ["whale"] = new[] { "start", "flat", "pit", "mover", "checkpoint", "flyers", "bigMover", "health", "doublePit", "checkpoint", "mover", "lowTunnel", "health" },
            ["penguin"] = new[] { "start", "flat", "steps", "spikePit", "checkpoint", "lowTunnel", "gauntlet", "health", "pit", "checkpoint", "spikeRun", "towers", "health" },
            ["glasses"] = new[] { "start", "turretWall", "flat", "lift", "checkpoint", "towers", "lowTunnel", "health", "turretWall", "checkpoint", "lift", "spikePit", "health" },
            ["tshirt"] = new[] { "start", "gauntlet", "pit", "mover", "checkpoint", "doublePit", "turretWall", "health", "gauntlet", "checkpoint", "bigMover", "steps", "health" },
            ["calico"] = new[] { "start", "flat", "spikeRun", "gauntlet", "checkpoint", "spikePit", "steps", "health", "lowTunnel", "checkpoint", "spikeRun", "turretWall", "health" },
            ["library"] = new[] { "start", "flyers", "ladderUp", "flat", "checkpoint", "ladderTall", "flyers", "health", "steps", "checkpoint", "ladderUp", "flyers", "health" },
            ["redcat"] = new[] { "start", "flat", "spikePit", "pit", "checkpoint", "spikeRun", "lift", "health", "mover", "checkpoint", "spikePit", "towers", "health" },
            ["sailor"] = new[] { "start", "flat", "pit", "flyers", "checkpoint", "doublePit", "ladderUp", "health", "bigMover", "checkpoint", "pit", "gauntlet", "health" },
            ["final"] = new[] { "start", "gauntlet", "spikeRun", "lift", "checkpoint", "bigMover", "ladderTall", "health", "towers", "lowTunnel", "checkpoint", "spikePit", "doublePit", "mover", "health", "checkpoint", "turretWall", "health" },
            ["citadel"] = new[] { "start", "flat", "spikeRun", "checkpoint", "turretWall", "mover", "health", "checkpoint", "health" },
        };

        public const int ROOM_COLS = 25;
        static readonly string[] CORRIDOR = { "..........", "..C.......", "##########", "##########", "##########" };
        static readonly HashSet<char> ENTITY = new HashSet<char> { 'W', 'F', 'T', 'M', 'N', 'V', 'C', 'h', 'a', 'P' };

        static string[] Pad(string[] rows, string name)
        {
            int w = rows[0].Length;
            for (int i = 0; i < rows.Length; i++) if (rows[i].Length != w) throw new Exception($"chunk {name} row {i} width {rows[i].Length} != {w}");
            if (rows.Length > Data.ROWS) throw new Exception($"chunk {name} too tall");
            return Enumerable.Repeat(new string('.', w), Data.ROWS - rows.Length).Concat(rows).ToArray();
        }

        static string[] BossRoom()
        {
            var rows = new List<string>();
            for (int r = 0; r < Data.ROWS; r++)
            {
                if (r == 0 || r >= 11) rows.Add(new string('#', ROOM_COLS));
                else if (r >= 8) rows.Add(new string('.', ROOM_COLS - 1) + "#");
                else rows.Add("#" + new string('.', ROOM_COLS - 2) + "#");
            }
            return rows.ToArray();
        }

        static string[] Concat(IEnumerable<string[]> parts)
        {
            var sb = new StringBuilder[Data.ROWS];
            for (int r = 0; r < Data.ROWS; r++) sb[r] = new StringBuilder();
            foreach (var rows in parts) for (int r = 0; r < Data.ROWS; r++) sb[r].Append(rows[r]);
            return sb.Select(s => s.ToString()).ToArray();
        }

        static int TileFor(string[] rows, int r, int c, int roomCol)
        {
            char ch = rows[r][c];
            char above = r > 0 ? rows[r - 1][c] : '.';
            switch (ch)
            {
                case '#':
                    if (c >= roomCol && (c == roomCol || c == rows[r].Length - 1 || r == 0)) return T_WALL;
                    return above == '#' ? T_GROUND : T_TOP;
                case '^': return T_SPIKE;
                case 'H': return above == 'H' ? T_LADDER : T_LADDER_TOP;
                case '-': return T_PLATFORM;
                default: return T_EMPTY;
            }
        }

        public static Level BuildLevel(string key)
        {
            if (!STAGE_LAYOUTS.TryGetValue(key, out var layout)) throw new Exception($"unknown stage {key}");
            var body = Concat(layout.Select(n =>
            {
                if (!CHUNKS.TryGetValue(n, out var c)) throw new Exception($"unknown chunk {n}");
                return Pad(c, n);
            }).Concat(new[] { Pad(CORRIDOR, "corridor") }));
            int roomCol = body[0].Length;
            var rows = Concat(new[] { body, BossRoom() });
            int cols = rows[0].Length;
            var tiles = new int[Data.ROWS][];
            for (int r = 0; r < Data.ROWS; r++)
            {
                tiles[r] = new int[cols];
                for (int c = 0; c < cols; c++) tiles[r][c] = TileFor(rows, r, c, roomCol);
            }
            var entities = new List<Spawn>();
            var checkpoints = new List<(int col, int row)>();
            (int col, int row)? start = null;
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                {
                    char ch = rows[r][c];
                    if (!ENTITY.Contains(ch)) continue;
                    if (ch == 'P') start = (c, r);
                    else if (ch == 'C') checkpoints.Add((c, r));
                    else entities.Add(new Spawn { Type = ch.ToString(), Col = c, Row = r });
                }
            if (!start.HasValue) throw new Exception($"stage {key} has no start");
            checkpoints = Js.Sorted(checkpoints, (a, b) => a.col.CompareTo(b.col));
            return new Level { Key = key, Cols = cols, Rows = Data.ROWS, Tiles = tiles, Entities = entities, Start = start.Value, Checkpoints = checkpoints, RoomCol = roomCol, DoorRows = new[] { 8, 9, 10 } };
        }

        public static bool IsSolid(int t) => t == T_GROUND || t == T_TOP || t == T_WALL;
        public static bool IsOneWay(int t) => t == T_PLATFORM || t == T_LADDER_TOP;
        public static bool IsLadder(int t) => t == T_LADDER || t == T_LADDER_TOP;
    }

    // ── save progress ──

    public interface IProgressStore
    {
        string Load();
        void Save(string data);
    }

    public class MemoryStore : IProgressStore
    {
        string data;
        public string Load() => data;
        public void Save(string d) => data = d;
    }

    /// <summary>Per-hero save: beaten bosses, cleared flag, citadel checkpoint (-1 = none).</summary>
    public static class Progress
    {
        class HeroProgress { public List<string> Beaten = new List<string>(); public bool Cleared; public int? CitadelPhase; }

        static IProgressStore store = new MemoryStore();
        static string lastHero;
        static Dictionary<string, HeroProgress> heroes;

        /// <summary>Swap the persistence backend (the Unity view uses PlayerPrefs).</summary>
        public static void UseStore(IProgressStore s) { store = s; heroes = null; }

        static Dictionary<string, HeroProgress> S()
        {
            if (heroes != null) return heroes;
            heroes = new Dictionary<string, HeroProgress>();
            lastHero = null;
            try
            {
                var raw = store.Load();
                if (!string.IsNullOrEmpty(raw))
                {
                    // Format: lastHero\nkey|beaten,beaten|cleared(0/1)|phase(-1..2)\n...
                    var lines = raw.Split('\n');
                    lastHero = lines[0] == "" ? null : lines[0];
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var f = lines[i].Split('|');
                        if (f.Length < 4) continue;
                        var hp = new HeroProgress { Cleared = f[2] == "1" };
                        hp.Beaten.AddRange(f[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                        int ph = int.Parse(f[3]);
                        hp.CitadelPhase = ph >= 0 ? ph : (int?)null;
                        heroes[f[0]] = hp;
                    }
                }
            }
            catch { heroes.Clear(); lastHero = null; }
            return heroes;
        }

        static void SaveNow()
        {
            try
            {
                var sb = new StringBuilder(lastHero ?? "");
                foreach (var kv in S()) sb.Append('\n').Append(kv.Key).Append('|').Append(string.Join(",", kv.Value.Beaten)).Append('|').Append(kv.Value.Cleared ? "1" : "0").Append('|').Append(kv.Value.CitadelPhase ?? -1);
                store.Save(sb.ToString());
            }
            catch { /* not persisted */ }
        }

        static HeroProgress Rec(string h)
        {
            var st = S();
            if (!st.TryGetValue(h, out var r)) st[h] = r = new HeroProgress();
            return r;
        }

        public static string LastHero { get { S(); return lastHero; } }
        public static void SetHero(string h) { S(); lastHero = h; Rec(h); SaveNow(); }
        public static List<string> Beaten(string h) => new List<string>(Rec(h).Beaten);
        public static bool IsBeaten(string h, string b) => Rec(h).Beaten.Contains(b);
        public static void MarkBeaten(string h, string b) { var r = Rec(h); if (!r.Beaten.Contains(b)) r.Beaten.Add(b); SaveNow(); }
        public static void MarkCitadelReached(string h) { var r = Rec(h); if (!r.CitadelPhase.HasValue) r.CitadelPhase = 0; SaveNow(); }
        public static int CitadelCheckpoint(string h) => Rec(h).CitadelPhase ?? -1;
        public static void MarkCitadelPhase(string h, int phase) { var r = Rec(h); r.CitadelPhase = Math.Max(r.CitadelPhase ?? 0, Math.Min(2, phase)); SaveNow(); }
        public static void MarkCleared(string h) { Rec(h).Cleared = true; SaveNow(); }
        public static bool IsCleared(string h) => Rec(h).Cleared;
        public static void Reset(string h) { S()[h] = new HeroProgress(); SaveNow(); }
    }

    public class FortressCarry { public int Hp, Lives, ETanks, MTanks; public Dictionary<string, double> Ammo; }

    /// <summary>Scene-to-scene hand-off.</summary>
    public static class Run
    {
        public static string Hero = "whale";
        public static string Stage = "penguin";
        public static string Outcome = "none"; // none | win | lose | ending
        public static string Message = "";
        public static FortressCarry FortressCarry;
    }

    public static class StageSelect
    {
        public static List<string> Keys(string hero) => Data.BossesFor(hero).Select(b => b.Key).Concat(new[] { "final" }).ToList();
        public static bool FinalUnlocked(string hero) => Data.BossesFor(hero).All(b => Progress.IsBeaten(hero, b.Key));
        /// <summary>stages.ts go(): the fortress slot resumes at the citadel once its checkpoint exists.</summary>
        public static string Resolve(string hero, string key) => key == "final" && Progress.CitadelCheckpoint(hero) >= 0 ? "citadel" : key;
    }
}
