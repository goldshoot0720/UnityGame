// 萌友格鬥王 — pure logic ported 1:1 from Game5/cloud/src (roster.ts, moves.ts, sim.ts, ai.ts).
// 60 Hz frame-stepped fight: frame data, hitstop, pushback, combo scaling, motion inputs.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MoeGames.Game5
{
    public enum Level { High, Mid, Low, Overhead }

    public class Box { public double X, Y, W, H; public Box(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; } }

    public class Projectile { public double Speed, W, H; public int Life; public string Style; public int Color; public int? Hits, HitEvery; }

    /// <summary>Frame data. Boxes are for a 320px-tall fighter and scale per character.
    /// box.X: distance in front of the fighter's centre; box.Y: height above the feet.
    /// Nullable fields mirror TS optional fields (undefined).</summary>
    public class Move
    {
        public string Name;
        public string Pose;
        public int Startup, Active, Recovery;
        public double Damage;
        public int Hitstun, Blockstun;
        public Level Level;
        public Box Box;
        public double? Lunge;
        public bool Chain, Cancelable, Knockdown;
        public int? Hits, HitEvery;
        public string Motion;
        public char Button; // 'P' | 'K' | '\0'
        public double? Chip;
        public double? DashVx, JumpVy, Hover;
        public int? Invuln;
        public Projectile Projectile;
        public bool LowProfile, ProjInvuln, UntilLand, Fire, Counter;
    }

    public class Fighter
    {
        public string Id, Name, Title, Desc;
        public bool FaceLeft;
        public int Color;
        public double Height, Health, Walk, Jump, Power, Reach;
        public Move Special, Super;
        /// <summary>Cast model id (the roster is in the same order as the eight characters).</summary>
        public string Model;
    }

    public static class Moves
    {
        static Move Normal(Move o) { o.Cancelable = true; o.Hits ??= 1; return o; }
        public static Move Special(Move o) { o.Hits ??= 1; o.Chip ??= 0.25; o.Lunge ??= 0; return o; }

        public static readonly Dictionary<string, Dictionary<string, Move>> NORMALS = new Dictionary<string, Dictionary<string, Move>>
        {
            ["stand"] = new Dictionary<string, Move>
            {
                ["lp"] = Normal(new Move { Pose = "punch", Startup = 4, Active = 3, Recovery = 7, Damage = 30, Hitstun = 14, Blockstun = 9, Level = Level.High, Box = new Box(105, 225, 90, 44), Lunge = 10, Chain = true }),
                ["hp"] = Normal(new Move { Pose = "punch", Startup = 7, Active = 4, Recovery = 16, Damage = 70, Hitstun = 20, Blockstun = 14, Level = Level.High, Box = new Box(120, 220, 120, 54), Lunge = 26 }),
                ["lk"] = Normal(new Move { Pose = "kick", Startup = 5, Active = 3, Recovery = 9, Damage = 35, Hitstun = 15, Blockstun = 10, Level = Level.Mid, Box = new Box(100, 120, 110, 50), Lunge = 10, Chain = true }),
                ["hk"] = Normal(new Move { Pose = "kick", Startup = 9, Active = 4, Recovery = 18, Damage = 80, Hitstun = 22, Blockstun = 15, Level = Level.Mid, Box = new Box(125, 170, 130, 64), Lunge = 30 }),
            },
            ["crouch"] = new Dictionary<string, Move>
            {
                ["lp"] = Normal(new Move { Pose = "low", Startup = 4, Active = 3, Recovery = 7, Damage = 25, Hitstun = 13, Blockstun = 8, Level = Level.Mid, Box = new Box(100, 130, 90, 44), Lunge = 8, Chain = true }),
                ["hp"] = Normal(new Move { Pose = "uppercut", Startup = 6, Active = 5, Recovery = 17, Damage = 65, Hitstun = 20, Blockstun = 13, Level = Level.Mid, Box = new Box(85, 230, 100, 140), Lunge = 14 }),
                ["lk"] = Normal(new Move { Pose = "low", Startup = 5, Active = 3, Recovery = 9, Damage = 25, Hitstun = 13, Blockstun = 8, Level = Level.Low, Box = new Box(105, 25, 120, 40), Lunge = 8, Chain = true }),
                ["hk"] = Normal(new Move { Pose = "low", Startup = 8, Active = 5, Recovery = 22, Damage = 70, Hitstun = 20, Blockstun = 14, Level = Level.Low, Box = new Box(130, 25, 150, 44), Lunge = 30, Knockdown = true }),
            },
            ["air"] = new Dictionary<string, Move>
            {
                ["lp"] = Normal(new Move { Pose = "air", Startup = 4, Active = 8, Recovery = 4, Damage = 30, Hitstun = 14, Blockstun = 9, Level = Level.Overhead, Box = new Box(85, 110, 90, 70), Lunge = 0 }),
                ["hp"] = Normal(new Move { Pose = "air", Startup = 6, Active = 6, Recovery = 6, Damage = 65, Hitstun = 20, Blockstun = 13, Level = Level.Overhead, Box = new Box(95, 90, 110, 90), Lunge = 0 }),
                ["lk"] = Normal(new Move { Pose = "airkick", Startup = 4, Active = 9, Recovery = 4, Damage = 35, Hitstun = 15, Blockstun = 9, Level = Level.Overhead, Box = new Box(90, 50, 100, 70), Lunge = 0 }),
                ["hk"] = Normal(new Move { Pose = "airkick", Startup = 6, Active = 7, Recovery = 6, Damage = 75, Hitstun = 20, Blockstun = 13, Level = Level.Overhead, Box = new Box(105, 45, 120, 80), Lunge = 0 }),
            },
        };

        public static readonly Move COUNTER_STRIKE = new Move
        {
            Name = "水月返・斬", Pose = "punch", Startup = 2, Active = 5, Recovery = 16, Damage = 130, Hitstun = 30, Blockstun = 16,
            Level = Level.Mid, Box = new Box(140, 190, 200, 180), Lunge = 70, Knockdown = true, Hits = 1, Invuln = 10,
        };

        /// <summary>Numpad notation relative to facing: 6 forward, 4 back, 2 down.</summary>
        public static readonly Dictionary<string, (int[] seq, string label)> MOTIONS = new Dictionary<string, (int[], string)>
        {
            ["qcf"] = (new[] { 2, 3, 6 }, "↓↘→"),
            ["qcb"] = (new[] { 2, 1, 4 }, "↓↙←"),
            ["dp"] = (new[] { 6, 2, 3 }, "→↓↘"),
            ["super"] = (new[] { 2, 3, 6, 2, 3, 6 }, "↓↘→↓↘→"),
        };

        /// <summary>Does the direction history (newest last) end with the motion, within window frames?</summary>
        public static bool MatchMotion(IList<(int dir, int f)> history, int[] seq, int now, int window = 22)
        {
            int si = seq.Length - 1;
            for (int i = history.Count - 1; i >= 0 && si >= 0; i--)
            {
                var h = history[i];
                if (now - h.f > window + seq.Length * 4) break;
                if (h.dir == seq[si]) si--;
            }
            return si < 0;
        }
    }

    public static class Roster
    {
        static Move S(Move m) => Moves.Special(m);

        public static readonly Fighter[] ROSTER =
        {
            new Fighter { Id = "shione", Model = "whale", Name = "汐音", Title = "鯨歌女僕", Color = 0x4f8dff, Height = 300, Health = 1000, Walk = 3.0, Jump = 1.0, Power = 1.0, Reach = 1.0, Desc = "以鯨浪波牽制遠距離的均衡型角色。",
                Special = S(new Move { Name = "鯨浪波", Motion = "qcf", Button = 'P', Pose = "cast", Startup = 12, Active = 1, Recovery = 24, Damage = 80, Hitstun = 22, Blockstun = 16, Level = Level.Mid,
                    Projectile = new Projectile { Speed = 7.5, W = 90, H = 80, Life = 160, Style = "wave", Color = 0x5ec8ff } }),
                Super = S(new Move { Name = "深海鯨嘯", Pose = "cast", Startup = 10, Active = 1, Recovery = 30, Damage = 50, Hitstun = 26, Blockstun = 12, Level = Level.Mid, Knockdown = true, Invuln = 12,
                    Projectile = new Projectile { Speed = 6.5, W = 200, H = 220, Life = 200, Style = "bigwave", Color = 0x3aa0ff, Hits = 6, HitEvery = 7 } }) },
            new Fighter { Id = "koori", Model = "penguin", Name = "小冰", Title = "企鵝連帽少女", Color = 0x9adfff, Height = 262, Health = 1080, Walk = 2.6, Jump = 0.92, Power = 1.08, Reach = 0.9, Desc = "身材嬌小卻很耐打，企鵝滑壘可以鑽過飛行道具。",
                Special = S(new Move { Name = "企鵝滑壘", Motion = "qcf", Button = 'K', Pose = "slide", Startup = 8, Active = 26, Recovery = 18, Damage = 90, Hitstun = 24, Blockstun = 16,
                    Level = Level.Low, Box = new Box(70, 30, 170, 60), DashVx = 10.5, Knockdown = true, LowProfile = true }),
                Super = S(new Move { Name = "冰原特快車", Pose = "slide", Startup = 6, Active = 44, Recovery = 22, Damage = 38, Hitstun = 20, Blockstun = 8,
                    Level = Level.Low, Box = new Box(70, 40, 190, 80), DashVx = 12, Hits = 7, HitEvery = 6, Knockdown = true, Invuln = 14, LowProfile = true }) },
            new Fighter { Id = "shoheng", Model = "glasses", Name = "光哉", Title = "書卷系學長", Color = 0xd8b98a, Height = 345, Health = 1000, Walk = 3.0, Jump = 1.0, Power = 1.05, Reach = 1.1, Desc = "手長腳長，知識昇龍拳發動瞬間無敵，是最強的對空技。",
                Special = S(new Move { Name = "知識昇龍拳", Motion = "dp", Button = 'P', Pose = "rise", Startup = 3, Active = 16, Recovery = 22, Damage = 120, Hitstun = 30, Blockstun = 18,
                    Level = Level.Mid, Box = new Box(55, 260, 110, 200), JumpVy = -15, DashVx = 2.5, Knockdown = true, Invuln = 9 }),
                Super = S(new Move { Name = "真・博學昇龍", Pose = "rise", Startup = 4, Active = 26, Recovery = 26, Damage = 55, Hitstun = 26, Blockstun = 10,
                    Level = Level.Mid, Box = new Box(55, 260, 130, 240), JumpVy = -18, DashVx = 3, Hits = 5, HitEvery = 5, Knockdown = true, Invuln = 16 }) },
            new Fighter { Id = "ashou", Model = "tshirt", Name = "阿翔", Title = "街頭少年", Color = 0xb0b0b0, Height = 335, Health = 980, Walk = 3.4, Jump = 1.05, Power = 1.0, Reach = 1.0, Desc = "腳步輕快的街頭少年，旋風腿能穿越對手的飛行道具。",
                Special = S(new Move { Name = "旋風腿", Motion = "qcb", Button = 'K', Pose = "spin", Startup = 7, Active = 30, Recovery = 16, Damage = 40, Hitstun = 18, Blockstun = 10,
                    Level = Level.High, Box = new Box(40, 190, 240, 70), DashVx = 5, Hover = 60, Hits = 3, HitEvery = 10, Knockdown = true, ProjInvuln = true }),
                Super = S(new Move { Name = "疾風大旋風", Pose = "spin", Startup = 5, Active = 48, Recovery = 20, Damage = 34, Hitstun = 18, Blockstun = 8,
                    Level = Level.Mid, Box = new Box(40, 180, 260, 140), DashVx = 6.5, Hover = 70, Hits = 8, HitEvery = 6, Knockdown = true, Invuln = 12 }) },
            new Fighter { Id = "hanamaki", Model = "calico", Name = "小花", Title = "三花街貓", Color = 0xf0a24a, Height = 330, Health = 900, Walk = 3.8, Jump = 1.08, Power = 0.92, Reach = 0.95, Desc = "速度最快的貓，出招快、連段多，但比較不耐打。",
                Special = S(new Move { Name = "三連貓爪", Motion = "qcf", Button = 'P', Pose = "claw", Startup = 6, Active = 18, Recovery = 14, Damage = 36, Hitstun = 16, Blockstun = 8,
                    Level = Level.High, Box = new Box(95, 200, 120, 110), DashVx = 7, Hits = 3, HitEvery = 6 }),
                Super = S(new Move { Name = "百裂貓爪亂舞", Pose = "claw", Startup = 4, Active = 50, Recovery = 20, Damage = 26, Hitstun = 16, Blockstun = 6,
                    Level = Level.High, Box = new Box(95, 200, 140, 150), DashVx = 5, Hits = 10, HitEvery = 5, Knockdown = true, Invuln = 10 }) },
            new Fighter { Id = "yukidama", Model = "whitecat", Name = "書白", Title = "圖書館白貓", FaceLeft = true, Color = 0xf4efe6, Height = 335, Health = 950, Walk = 3.4, Jump = 1.12, Power = 0.98, Reach = 1.0, Desc = "身手敏捷的白貓，飛撲貓掌從空中攻擊，必須站著防禦。",
                Special = S(new Move { Name = "飛撲貓掌", Motion = "qcf", Button = 'K', Pose = "pounce", Startup = 6, Active = 60, Recovery = 12, Damage = 95, Hitstun = 24, Blockstun = 16,
                    Level = Level.Overhead, Box = new Box(80, 90, 130, 120), JumpVy = -13, DashVx = 7.5, UntilLand = true, Knockdown = true }),
                Super = S(new Move { Name = "雪崩飛撲", Pose = "pounce", Startup = 4, Active = 60, Recovery = 16, Damage = 48, Hitstun = 22, Blockstun = 10,
                    Level = Level.Overhead, Box = new Box(80, 110, 170, 180), JumpVy = -15, DashVx = 9, UntilLand = true, Hits = 5, HitEvery = 5, Knockdown = true, Invuln = 12 }) },
            new Fighter { Id = "hirin", Model = "redcat", Name = "緋音", Title = "緋紅貓耳少女", Color = 0xe8413c, Height = 318, Health = 960, Walk = 3.3, Jump = 1.05, Power = 1.05, Reach = 1.0, Desc = "腳技華麗的貓耳少女，緋焰月輪腳帶著火焰翻身踢上天空。",
                Special = S(new Move { Name = "緋焰月輪腳", Motion = "dp", Button = 'K', Pose = "flip", Startup = 4, Active = 22, Recovery = 20, Damage = 50, Hitstun = 26, Blockstun = 12,
                    Level = Level.Mid, Box = new Box(75, 200, 150, 200), JumpVy = -14, DashVx = 4, Hits = 2, HitEvery = 8, Knockdown = true, Invuln = 7, Fire = true }),
                Super = S(new Move { Name = "鈴音・緋焰天舞", Pose = "flip", Startup = 4, Active = 34, Recovery = 24, Damage = 42, Hitstun = 24, Blockstun = 8,
                    Level = Level.Mid, Box = new Box(75, 220, 190, 240), JumpVy = -17, DashVx = 5, Hits = 6, HitEvery = 5, Knockdown = true, Invuln = 14, Fire = true }) },
            new Fighter { Id = "mio", Model = "sailor", Name = "澪", Title = "水手服少女", Color = 0x7fb3e6, Height = 325, Health = 1000, Walk = 3.1, Jump = 1.0, Power = 1.0, Reach = 1.05, Desc = "冷靜的反擊專家，水月返可以擋下任何攻擊並立刻反擊。",
                Special = S(new Move { Name = "水月返", Motion = "qcb", Button = 'P', Pose = "counter", Startup = 3, Active = 26, Recovery = 20, Damage = 0, Hitstun = 0, Blockstun = 0, Level = Level.Mid, Counter = true }),
                Super = S(new Move { Name = "明鏡止水・連掌", Pose = "claw", Startup = 5, Active = 42, Recovery = 22, Damage = 40, Hitstun = 20, Blockstun = 8,
                    Level = Level.Mid, Box = new Box(100, 200, 150, 170), DashVx = 6.5, Hits = 7, HitEvery = 6, Knockdown = true, Invuln = 12 }) },
        };

        public static readonly string[] STAGES = { "櫻花道場", "夕陰港灣", "霓虹天台" };
        public const int ROUND_TIME = 99;
        public const int ROUNDS_TO_WIN = 2;
        public const double METER_MAX = 100;
        public const double GROUND_Y = 650;
        public const double GRAVITY = 0.8;      // px / frame²
        public const double JUMP_VY = -17;      // px / frame
        public static readonly string[] DIFFS = { "簡單", "普通", "困難" };
        public static string Hex(int n) => "#" + n.ToString("x6");
    }

    public struct Input
    {
        public bool Left, Right, Up, Down, Lp, Hp, Lk, Hk, Sp, Su;

        public bool Get(string k) => k switch
        {
            "left" => Left, "right" => Right, "up" => Up, "down" => Down, "lp" => Lp, "hp" => Hp, "lk" => Lk, "hk" => Hk, "sp" => Sp, "su" => Su, _ => false,
        };

        public void Set(string k, bool v)
        {
            switch (k)
            {
                case "left": Left = v; break;
                case "right": Right = v; break;
                case "up": Up = v; break;
                case "down": Down = v; break;
                case "lp": Lp = v; break;
                case "hp": Hp = v; break;
                case "lk": Lk = v; break;
                case "hk": Hk = v; break;
                case "sp": Sp = v; break;
                case "su": Su = v; break;
            }
        }
    }

    public enum State { Idle, Walk, Crouch, Air, Attack, Hitstun, Blockstun, Down, Ko, Win }

    public class FShot
    {
        public int Owner;
        public double X, Y, Vx, W, H;
        public int Life;
        public string Style;
        public int Color;
        public Move Move;
        public int HitsLeft, HitEvery, Cd;
    }

    public class HitEvent { public double X, Y; public bool Big, Blocked; public int Color; }

    public class Events
    {
        public List<HitEvent> Hit = new List<HitEvent>();
        public List<string> Say = new List<string>();
        public List<string> Sfx = new List<string>();
    }

    public struct Rect2 { public double X, Y, W, H; public Rect2(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; } }

    public class Body
    {
        public double X, Y = Roster.GROUND_Y, Vx, Vy;
        public int Facing;
        public double Hp, Meter;
        public State State = State.Idle;
        public Move Move;
        public string MoveKind = "";
        public int Frame, Stun, Invuln;
        public int HitsDone, LastHitF = -99;
        public bool Connected;
        public int Combo;
        public List<(int dir, int f)> Hist = new List<(int, int)>();
        public Input Prev;
        public bool Blocking;
        public bool ShotFired;
        public readonly Fighter F;

        public Body(Fighter f, double x, int facing) { F = f; X = x; Facing = facing; Hp = f.Health; }

        public double Scale => F.Height / 320;
        public bool Grounded => Y >= Roster.GROUND_Y;
        public bool Crouching => State == State.Crouch || (State == State.Blockstun && Prev.Down) || (State == State.Attack && MoveKind == "crouch");

        /// <summary>Hurtbox (world rect).</summary>
        public Rect2 Hurt()
        {
            double w = 110 * Scale;
            double h = F.Height * (Crouching ? 0.62 : 0.92);
            if (Move != null && Move.LowProfile && State == State.Attack) h = F.Height * 0.32;
            if (State == State.Down) h = F.Height * 0.25;
            return new Rect2(X - w / 2, Y - h, w, h);
        }

        /// <summary>"startup" | "active" | "recovery" | null</summary>
        public string Phase()
        {
            var m = Move;
            if (m == null || State != State.Attack) return null;
            if (Frame < m.Startup) return "startup";
            if (Frame < m.Startup + m.Active || (m.UntilLand && !Grounded && Frame >= m.Startup)) return "active";
            return "recovery";
        }

        public Rect2? Hitbox()
        {
            var m = Move;
            if (m?.Box == null || Phase() != "active") return null;
            double s = Scale, r = F.Reach;
            double cx = X + Facing * m.Box.X * s * r, cy = Y - m.Box.Y * s;
            double w = m.Box.W * s * r, h = m.Box.H * s;
            return new Rect2(cx - w / 2, cy - h / 2, w, h);
        }
    }

    public class Fight
    {
        public Body[] P;
        public List<FShot> Shots = new List<FShot>();
        public int FrameNo;
        public int Hitstop;
        public double Left = 60, Right = 1220;
        public Events Ev = new Events();

        public Fight(Fighter a, Fighter b, double width = 1280)
        {
            Right = width - 60;
            P = new[] { new Body(a, width / 2 - 220, 1), new Body(b, width / 2 + 220, -1) };
        }

        static bool Overlap(Rect2 a, Rect2 b) => a.X < b.X + b.W && a.X + a.W > b.X && a.Y < b.Y + b.H && a.Y + a.H > b.Y;

        /// <summary>Numpad direction relative to facing.</summary>
        public static int Numpad(Input i, int facing)
        {
            bool fwd = facing > 0 ? i.Right : i.Left, back = facing > 0 ? i.Left : i.Right;
            int h = fwd ? 1 : back ? -1 : 0, v = i.Up ? 1 : i.Down ? -1 : 0;
            return 5 + h + v * 3;
        }

        public void Step(Input a, Input b)
        {
            FrameNo++;
            Ev = new Events();
            if (Hitstop > 0) { Hitstop--; return; }
            Control(0, a);
            Control(1, b);
            for (int i = 0; i < 2; i++) Physics(i);
            PushApart();
            StepShots();
            ResolveHits();
            P[0].Prev = a;
            P[1].Prev = b;
        }

        void Start(Body b, Move m, string kind)
        {
            b.State = State.Attack; b.Move = m; b.MoveKind = kind; b.Frame = 0; b.HitsDone = 0; b.LastHitF = -99; b.Connected = false; b.ShotFired = false;
            b.Invuln = m.Invuln ?? 0;
            if (kind != "air") b.Vx = 0;
            Ev.Sfx.Add(kind == "super" ? "super" : kind == "special" ? "special" : "swing");
            if (kind == "super" || kind == "special") Ev.Say.Add($"{(b == P[0] ? "P1" : "P2")}:{m.Name ?? ""}");
        }

        void Control(int i, Input inp)
        {
            var b = P[i];
            var o = P[1 - i];
            int dir = Numpad(inp, b.Facing);
            if (b.Hist.Count == 0 || b.Hist[b.Hist.Count - 1].dir != dir)
            {
                b.Hist.Add((dir, FrameNo));
                if (b.Hist.Count > 16) b.Hist.RemoveAt(0);
            }
            var prev = b.Prev;
            bool Pressed(string k) => inp.Get(k) && !prev.Get(k);
            string btn = Pressed("hp") ? "hp" : Pressed("hk") ? "hk" : Pressed("lp") ? "lp" : Pressed("lk") ? "lk" : null;
            b.Blocking = false;
            if (b.State == State.Ko || b.State == State.Win) return;
            if (b.Invuln > 0) b.Invuln--;
            if (b.State == State.Hitstun || b.State == State.Blockstun) { if (--b.Stun <= 0) b.State = b.Grounded ? State.Idle : State.Air; return; }
            if (b.State == State.Down) { if (--b.Stun <= 0) { b.State = State.Idle; b.Invuln = 20; } return; }
            // Specials / supers (motion or shortcut), allowed from neutral or when cancelling a connected normal.
            bool canAct = b.State == State.Idle || b.State == State.Walk || b.State == State.Crouch;
            bool canCancel = b.State == State.Attack && b.Connected && b.Move != null && b.Move.Cancelable && b.Phase() != "startup";
            if ((canAct || canCancel) && b.Grounded)
            {
                bool wantSuper = Pressed("su") || (btn != null && Moves.MatchMotion(b.Hist, Moves.MOTIONS["super"].seq, FrameNo, 40));
                if (wantSuper && b.Meter >= Roster.METER_MAX) { b.Meter = 0; Start(b, b.F.Super, "super"); Hitstop = 10; return; }
                var sm = b.F.Special;
                char btnType = btn == "lp" || btn == "hp" ? 'P' : btn != null ? 'K' : '\0';
                bool motionOk = btnType != '\0' && btnType == sm.Button && sm.Motion != null && Moves.MatchMotion(b.Hist, Moves.MOTIONS[sm.Motion].seq, FrameNo);
                if (Pressed("sp") || motionOk) { Start(b, sm, "special"); return; }
            }
            if (canCancel && btn != null && b.Move.Chain)
            {
                string k = b.MoveKind == "crouch" ? "crouch" : "stand";
                Start(b, Moves.NORMALS[k][btn], k);
                return;
            }
            if (b.State == State.Attack) return;
            if (b.State == State.Air)
            {
                if (btn != null && b.Move == null) Start(b, Moves.NORMALS["air"][btn], "air");
                return;
            }
            // Grounded neutral.
            b.Facing = o.X >= b.X ? 1 : -1;
            bool back = b.Facing > 0 ? inp.Left : inp.Right;
            b.Blocking = back;
            if (btn != null) { string k = inp.Down ? "crouch" : "stand"; Start(b, Moves.NORMALS[k][btn], k); return; }
            if (inp.Up)
            {
                b.State = State.Air;
                b.Vy = Roster.JUMP_VY * b.F.Jump;
                b.Vx = (inp.Right ? 1 : inp.Left ? -1 : 0) * 4.2;
                b.Move = null;
                Ev.Sfx.Add("jump");
                return;
            }
            if (inp.Down) { b.State = State.Crouch; b.Vx = 0; return; }
            int hh = (inp.Right ? 1 : 0) - (inp.Left ? 1 : 0);
            b.Vx = hh * b.F.Walk * (hh == b.Facing ? 1 : 0.8);
            b.State = hh != 0 ? State.Walk : State.Idle;
        }

        void Physics(int i)
        {
            var b = P[i];
            if (b.State == State.Attack && b.Move != null)
            {
                var m = b.Move;
                string ph = b.Phase();
                if (b.Frame == 0 && m.Lunge.HasValue && m.Lunge.Value != 0) b.X += b.Facing * m.Lunge.Value * 0.3 * b.Scale;
                if (ph == "active")
                {
                    if (b.Frame == m.Startup && m.JumpVy.HasValue && m.JumpVy.Value != 0) { b.Vy = m.JumpVy.Value; b.Y -= 1; }
                    if (m.DashVx.HasValue && m.DashVx.Value != 0) b.Vx = b.Facing * m.DashVx.Value;
                    if (m.Hover.HasValue) { b.Y = Roster.GROUND_Y - m.Hover.Value; b.Vy = 0; }
                    if (m.Projectile != null && !b.ShotFired)
                    {
                        b.ShotFired = true;
                        var pr = m.Projectile;
                        Shots.Add(new FShot
                        {
                            Owner = i, X = b.X + b.Facing * 90 * b.Scale, Y = b.Y - b.F.Height * 0.55, Vx = b.Facing * pr.Speed, W = pr.W, H = pr.H, Life = pr.Life,
                            Style = pr.Style, Color = pr.Color, Move = m, HitsLeft = pr.Hits ?? 1, HitEvery = pr.HitEvery ?? 1, Cd = 0,
                        });
                    }
                }
                else if (ph == "recovery" && b.MoveKind != "air")
                {
                    if (!m.Hover.HasValue || m.Hover.Value == 0) b.Vx *= 0.7;
                }
                b.Frame++;
                int total = m.Startup + m.Active + m.Recovery;
                if (b.MoveKind == "air")
                {
                    if (b.Grounded) { b.State = State.Idle; b.Move = null; b.Vx = 0; }
                    else if (b.Frame >= total) b.State = State.Air;
                }
                else if (b.Frame >= total && b.Grounded)
                {
                    b.State = State.Idle; b.Move = null; b.Vx = 0;
                }
            }
            // Gravity.
            bool hovering = b.State == State.Attack && b.Move != null && b.Move.Hover.HasValue && b.Phase() == "active";
            if (!b.Grounded && !hovering) b.Vy += Roster.GRAVITY;
            b.X += b.Vx;
            b.Y += b.Vy;
            if (b.Y >= Roster.GROUND_Y)
            {
                bool wasAir = b.Vy > 0;
                b.Y = Roster.GROUND_Y; b.Vy = 0;
                if (b.State == State.Air) { b.State = State.Idle; b.Vx = 0; b.Move = null; if (wasAir) Ev.Sfx.Add("land"); }
                if (b.State == State.Attack && b.Move != null && b.Move.UntilLand && b.Frame > b.Move.Startup + 2) b.Frame = Math.Max(b.Frame, b.Move.Startup + b.Move.Active);
                if (b.State == State.Hitstun && b.Stun > 900) { b.State = State.Down; b.Stun = 40; b.Vx = 0; }
            }
            if (b.State == State.Ko && b.Grounded) b.Vx *= 0.85;
            b.X = Math.Max(Left, Math.Min(Right, b.X));
        }

        void PushApart()
        {
            var a = P[0];
            var b = P[1];
            double min = 70 * (a.Scale + b.Scale) / 2;
            double d = b.X - a.X;
            if (Math.Abs(d) < min && Math.Abs(a.Y - b.Y) < 200)
            {
                double push = (min - Math.Abs(d)) / 2 * Math.Sign(d != 0 ? d : 1);
                a.X -= push; b.X += push;
                a.X = Math.Max(Left, Math.Min(Right, a.X));
                b.X = Math.Max(Left, Math.Min(Right, b.X));
            }
        }

        void StepShots()
        {
            for (int i = Shots.Count - 1; i >= 0; i--)
            {
                var s = Shots[i];
                s.X += s.Vx; s.Life--; if (s.Cd > 0) s.Cd--;
                if (s.Life <= 0 || s.X < -200 || s.X > Right + 260) Shots.RemoveAt(i);
            }
            // Clash.
            for (int i = 0; i < Shots.Count; i++)
                for (int j = i + 1; j < Shots.Count; j++)
                {
                    var a = Shots[i];
                    var b = Shots[j];
                    if (a.Owner != b.Owner && Math.Abs(a.X - b.X) < (a.W + b.W) / 2 && Math.Abs(a.Y - b.Y) < (a.H + b.H) / 2)
                    {
                        a.HitsLeft--; b.HitsLeft--;
                        a.Life = a.HitsLeft > 0 ? a.Life : 0;
                        b.Life = b.HitsLeft > 0 ? b.Life : 0;
                    }
                }
            Shots = Shots.Where(s => s.Life > 0).ToList();
        }

        void ResolveHits()
        {
            for (int i = 0; i < 2; i++)
            {
                var a = P[i];
                var d = P[1 - i];
                var hb = a.Hitbox();
                var m = a.Move;
                if (hb.HasValue && m != null && m.Damage > 0)
                {
                    int hits = m.Hits ?? 1, every = m.HitEvery ?? 99;
                    if (a.HitsDone < hits && FrameNo - a.LastHitF >= every && Overlap(hb.Value, d.Hurt()))
                    {
                        a.HitsDone++; a.LastHitF = FrameNo;
                        ApplyHit(i, m, a.HitsDone == hits, a.X);
                    }
                }
            }
            foreach (var s in Shots)
            {
                var d = P[1 - s.Owner];
                if (s.Cd > 0) continue;
                if (d.State == State.Attack && d.Move != null && d.Move.ProjInvuln && d.Phase() == "active") continue;
                var box = new Rect2(s.X - s.W / 2, s.Y - s.H / 2, s.W, s.H);
                if (Overlap(box, d.Hurt()))
                {
                    s.HitsLeft--; s.Cd = s.HitEvery;
                    ApplyHit(s.Owner, s.Move, s.HitsLeft <= 0, s.X - Math.Sign(s.Vx) * 100);
                    if (s.HitsLeft <= 0) s.Life = 0;
                }
            }
            Shots = Shots.Where(s => s.Life > 0).ToList();
        }

        void ApplyHit(int ai, Move m, bool last, double fromX)
        {
            var a = P[ai];
            var d = P[1 - ai];
            if (d.Invuln > 0 || d.State == State.Ko || d.State == State.Down) return;
            // Parry stance.
            if (d.State == State.Attack && d.Move != null && d.Move.Counter && d.Phase() == "active")
            {
                d.Facing = a.X >= d.X ? 1 : -1;
                Start(d, Moves.COUNTER_STRIKE, "counter");
                d.Frame = 0;
                Hitstop = 12;
                Ev.Say.Add($"{(d == P[0] ? "P1" : "P2")}:水月返・斬");
                return;
            }
            int dir = fromX < d.X ? 1 : -1;
            bool awayHeld = dir > 0 ? d.Prev.Right : d.Prev.Left;
            bool canBlock = d.Grounded && (d.State == State.Idle || d.State == State.Walk || d.State == State.Crouch || d.State == State.Blockstun) && awayHeld;
            bool crouch = d.Prev.Down;
            bool blockedOk = canBlock && !(m.Level == Level.Low && !crouch) && !(m.Level == Level.Overhead && crouch);
            a.Connected = true;
            double hy = d.Y - d.F.Height * (crouch ? 0.35 : 0.6);
            if (blockedOk)
            {
                double chip = Js.Round(m.Damage * (m.Chip ?? 0) * a.F.Power);
                d.Hp = Math.Max(1, d.Hp - chip);
                d.State = State.Blockstun; d.Stun = m.Blockstun; d.Vx = dir * 3;
                d.X += dir * 6;
                a.Meter = Math.Min(Roster.METER_MAX, a.Meter + 3); d.Meter = Math.Min(Roster.METER_MAX, d.Meter + 2);
                Hitstop = 5;
                Ev.Hit.Add(new HitEvent { X = d.X - dir * 30, Y = hy, Big = false, Blocked = true, Color = 0x9fd8ff });
                Ev.Sfx.Add("block");
                return;
            }
            double scale = Math.Max(0.4, 1 - d.Combo * 0.1);
            double dmg = Js.Round(m.Damage * a.F.Power * scale);
            d.Hp = Math.Max(0, d.Hp - dmg);
            d.Combo++;
            a.Meter = Math.Min(Roster.METER_MAX, a.Meter + Js.Round(m.Damage / 8)); d.Meter = Math.Min(Roster.METER_MAX, d.Meter + Js.Round(m.Damage / 14));
            d.Move = null;
            bool launch = (m.Knockdown && last) || !d.Grounded || d.Hp <= 0;
            if (launch) { d.State = State.Hitstun; d.Stun = 999; d.Vy = -9; d.Vx = dir * 5; d.Y -= 1; }
            else { d.State = State.Hitstun; d.Stun = m.Hitstun; d.Vx = dir * 4; }
            Hitstop = dmg >= 60 ? 9 : 6;
            Ev.Hit.Add(new HitEvent { X = d.X - dir * 30, Y = hy, Big = dmg >= 60, Blocked = false, Color = a.F.Color });
            Ev.Sfx.Add(dmg >= 60 ? "hitBig" : "hit");
            if (d.Hp <= 0) { d.State = State.Ko; d.Stun = 0; d.Vy = -12; d.Vx = dir * 6; }
        }

        /// <summary>Reset combo counters once the victim recovers.</summary>
        public void Settle()
        {
            foreach (var b in P) if (b.State != State.Hitstun && b.State != State.Down && b.State != State.Ko) b.Combo = 0;
        }
    }

    /// <summary>CPU opponent: produces an Input every frame from the fight state.</summary>
    public class Cpu
    {
        Input plan;
        int hold;
        bool tap;
        readonly int idx, level;
        readonly Func<double> rng;

        public Cpu(int idx, int level, Func<double> rng = null) { this.idx = idx; this.level = level; this.rng = rng ?? Rand.Default; }

        public Input Think(Fight f)
        {
            var me = f.P[idx];
            var o = f.P[1 - idx];
            string fwd = me.Facing > 0 ? "right" : "left", back = me.Facing > 0 ? "left" : "right";
            double dist = Math.Abs(o.X - me.X);
            double react = new[] { 0.25, 0.55, 0.85 }[level];
            double r = rng();
            // Release single-frame button taps.
            if (tap)
            {
                tap = false;
                var k = plan;
                k.Lp = k.Hp = k.Lk = k.Hk = k.Sp = k.Su = false;
                plan = k;
                return k;
            }
            // Block incoming attacks / projectiles.
            bool threat = (o.State == State.Attack && o.Phase() != "recovery" && dist < 330) || f.Shots.Any(s => s.Owner != idx && Math.Abs(s.X - me.X) < 260);
            if (threat && me.State != State.Attack && rng() < react * 0.35)
            {
                bool low = (o.Move != null && o.Move.Level == Level.Low) || o.MoveKind == "crouch";
                var bi = new Input();
                bi.Set(back, true);
                bi.Down = low;
                hold = 6;
                plan = bi;
                return bi;
            }
            if (hold-- > 0) return plan;
            var inp = new Input();
            // Anti-air.
            if (!o.Grounded && o.State != State.Hitstun && dist < 260 && r < react * 0.6)
            {
                if (me.F.Special.Pose == "rise" || me.F.Special.Pose == "flip") inp.Sp = true; else { inp.Down = true; inp.Hp = true; }
                tap = true; plan = inp; return inp;
            }
            if (me.Meter >= Roster.METER_MAX && dist < 360 && r < 0.05 + react * 0.05) { inp.Su = true; tap = true; plan = inp; return inp; }
            if (dist > 420)
            {
                if (me.F.Special.Projectile != null && r < 0.02 + react * 0.03) { inp.Sp = true; tap = true; }
                else if (r < 0.01) { inp.Up = true; inp.Set(fwd, true); hold = 30; }
                else { inp.Set(fwd, true); hold = 10 + (int)Math.Floor(rng() * 20); }
            }
            else if (dist > 200)
            {
                if (r < 0.04 + react * 0.04)
                {
                    inp.Sp = (me.F.Special.DashVx.HasValue && me.F.Special.DashVx.Value != 0) || me.F.Special.Projectile != null;
                    if (!inp.Sp) inp.Set(fwd, true);
                    tap = inp.Sp;
                    hold = inp.Sp ? 0 : 12;
                }
                else if (r < 0.08) { inp.Up = true; inp.Set(fwd, true); hold = 30; }
                else { inp.Set(fwd, true); hold = 8; }
            }
            else
            {
                if (r < 0.06 + react * 0.12)
                {
                    double pick = rng();
                    if (pick < 0.3) inp.Lp = true; else if (pick < 0.5) inp.Lk = true; else if (pick < 0.7) { inp.Down = true; inp.Hk = true; } else if (pick < 0.85) inp.Hp = true; else inp.Sp = true;
                    tap = true;
                }
                else if (r < 0.2) { inp.Set(back, true); hold = 10; }
                else hold = 4;
            }
            plan = inp;
            return inp;
        }
    }
}
