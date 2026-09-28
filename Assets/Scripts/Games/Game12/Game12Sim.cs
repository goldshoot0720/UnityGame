// The round loop from Game12/cloud/src/scenes/play.ts without drawing: grid movement with corner
// sliding, balloons/chains/water, bubbles (trap → pop / needle / burst), items, bots, match flow.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MoeGames.Game12
{
    public class Balloon : BalloonLike
    {
        public int Owner;
        public Balloon(int c, int r, float t, int range, int owner) : base(c, r, t, range) { Owner = owner; }
    }

    public class PlayerState
    {
        public int I;
        public Hero Hero;
        public bool You;
        public string Color;
        public double X, Y, DeadT, Trapped, Think, Stuck, LastX, LastY, Walk;
        public int Face = 1, Max, Range, SpeedLv, Needles, Wins;
        public bool Dead;
        public List<Pt> Path = new List<Pt>();
    }

    public class MatchResult { public string Id, Name, Color; public bool You; public int Wins; }

    public enum RoundPhase { Intro, Play, End }

    public struct MoveInput { public int Dx, Dy; public bool Drop, Needle; }

    public class Match
    {
        public Cell[,] Grid;
        public List<Balloon> Balloons = new List<Balloon>();
        public readonly OrderedMap<int, double> Water = new OrderedMap<int, double>();
        public readonly OrderedMap<int, (ItemKind kind, double safe)> Items = new OrderedMap<int, (ItemKind, double)>();
        public readonly List<PlayerState> Ps = new List<PlayerState>();
        public int Round;
        public double Clock = Rules.ROUND_TIME;
        public RoundPhase Phase = RoundPhase.Intro;
        public double Timer;
        public string RoundMsg = "";
        public int? Winner;
        public List<MatchResult> Results;
        /// <summary>Incremented when the grid layout changes (new round or boxes broken).</summary>
        public int GridVersion;
        readonly Func<double> rng;

        public event Action<string> Sound;                         // place splash trap popped item free go
        public event Action<double, double, string, string> Float; // c, r, text, colour
        public event Action<int, int> BoxBroken;
        public event Action<PlayerState> Popped;

        public Match(string hero, Func<double> rng = null)
        {
            this.rng = rng ?? Rand.Default;
            var me = Rules.HEROES.FirstOrDefault(h => h.Id == hero) ?? Rules.HEROES[0];
            var rivals = Rules.HEROES.Where(h => h != me).ToList();
            Rand.Shuffle(rivals, this.rng);
            var heroes = new[] { me }.Concat(rivals.Take(3)).ToList();
            for (int i = 0; i < heroes.Count; i++)
                Ps.Add(new PlayerState { I = i, Hero = heroes[i], You = i == 0, Color = Rules.PLAYER_COLORS[i] });
            NewRound();
        }

        void NewRound()
        {
            Round++;
            Grid = Rules.MakeGrid((uint)(1 + Math.Floor(rng() * 1e6)));
            GridVersion++;
            Balloons = new List<Balloon>(); Water.Clear(); Items.Clear();
            for (int i = 0; i < Ps.Count; i++)
            {
                var p = Ps[i];
                var s = Rules.SPAWNS[i];
                p.X = s.C; p.Y = s.R; p.Dead = false; p.DeadT = 0; p.Trapped = 0; p.Max = p.Hero.Balloons; p.Range = p.Hero.Range;
                p.SpeedLv = p.Hero.Speed; p.Needles = 1; p.Path = new List<Pt>(); p.Think = 0.3 + i * 0.1; p.Stuck = 0;
            }
            Clock = Rules.ROUND_TIME; Phase = RoundPhase.Intro; Timer = 2.2;
            Sound?.Invoke("go");
        }

        // ── helpers ──
        bool Open(int c, int r) => Rules.Passable(Grid, Balloons.Cast<BalloonLike>().ToList(), c, r);
        public static Pt TileOf(PlayerState p) => new Pt((int)Js.Round(p.X), (int)Js.Round(p.Y));
        int Active(PlayerState p) => Balloons.Count(b => b.Owner == p.I);

        static double Get(PlayerState p, char axis) => axis == 'x' ? p.X : p.Y;
        static void Set(PlayerState p, char axis, double v) { if (axis == 'x') p.X = v; else p.Y = v; }

        /// <summary>Grid movement with corner sliding: align to the lane first, then advance until the next cell is blocked.</summary>
        public void Move(PlayerState p, int dx, int dy, double dt)
        {
            if (dx == 0 && dy == 0) return;
            double sp = (p.Trapped > 0 ? 0.55 : Rules.SpeedOf(p.SpeedLv)) * dt;
            int cc = (int)Js.Round(p.X), cr = (int)Js.Round(p.Y);
            if (dx != 0) p.Face = dx;
            char along = dx != 0 ? 'x' : 'y', across = dx != 0 ? 'y' : 'x';
            int d = dx != 0 ? dx : dy;
            int cur = along == 'x' ? cc : cr, lane = across == 'y' ? cr : cc;
            double off = Get(p, across) - lane;
            bool Ahead(int ln) => along == 'x' ? Open(cc + d, ln) : Open(ln, cr + d);
            bool Here(int ln) => along == 'x' ? Open(cc, ln) : Open(ln, cr);
            int target = lane;
            if (!Ahead(lane) && Math.Abs(off) > 0.12) { int alt = lane + Math.Sign(off); if (Ahead(alt) && Here(alt)) target = alt; }
            double gap = target - Get(p, across);
            if (Math.Abs(gap) > 0.001)
            {
                double m = Math.Min(Math.Abs(gap), sp);
                Set(p, across, Get(p, across) + Math.Sign(gap) * m);
                sp -= m;
                if (Math.Abs(target - Get(p, across)) > 0.001) return;
                Set(p, across, target);
            }
            double n = Get(p, along) + d * sp;
            int laneNow = (int)Js.Round(Get(p, across));
            bool blocked = along == 'x' ? !Open(cur + d, laneNow) : !Open(laneNow, cur + d);
            if (blocked) n = d > 0 ? Math.Min(n, cur) : Math.Max(n, cur);
            Set(p, along, Js.Clamp(n, 0, along == 'x' ? Rules.COLS - 1 : Rules.ROWS - 1));
            p.Walk += dt;
        }

        public void Drop(PlayerState p)
        {
            var t = TileOf(p);
            if (p.Trapped > 0 || p.Dead || Active(p) >= p.Max || Grid[t.R, t.C] != Cell.Empty || Balloons.Any(b => b.C == t.C && b.R == t.R)) return;
            Balloons.Add(new Balloon(t.C, t.R, Rules.FUSE, p.Range, p.I));
            Sound?.Invoke("place");
        }

        void Explode(int i)
        {
            var res = Rules.Chain(Grid, Balloons.Cast<BalloonLike>().ToList(), i);
            foreach (var t in res.Tiles)
            {
                int k = Rules.Key(t.C, t.R);
                Water.Set(k, Rules.WATER_TIME);
                if (Items.TryGet(k, out var it) && it.safe <= 0) Items.Delete(k);
            }
            foreach (var t in res.Boxes)
            {
                Grid[t.R, t.C] = Cell.Empty;
                var kind = Rules.DropFor(rng());
                if (kind.HasValue) Items.Set(Rules.Key(t.C, t.R), (kind.Value, Rules.WATER_TIME + 0.05));
                BoxBroken?.Invoke(t.C, t.R);
            }
            if (res.Boxes.Count > 0) GridVersion++;
            var gone = new HashSet<int>(res.Balloons);
            Balloons = Balloons.Where((_, j) => !gone.Contains(j)).ToList();
            Sound?.Invoke("splash");
        }

        /// <summary>Advance one frame; returns true when the match is decided and its end delay has passed.</summary>
        public bool Update(double dt, MoveInput k)
        {
            dt = Math.Min(dt, 1.0 / 30);
            Timer -= dt;
            if (Phase == RoundPhase.Intro) { if (Timer <= 0) Phase = RoundPhase.Play; return false; }
            if (Phase == RoundPhase.End)
            {
                StepWorld(dt);
                if (Timer <= 0)
                {
                    var w = Rules.MatchWinner(Ps.Select(p => p.Wins).ToArray(), Round);
                    if (w == null) NewRound();
                    else { Finish(w.Value); return true; }
                }
                return false;
            }
            Clock -= dt;
            var me = Ps[0];
            if (!me.Dead)
            {
                if (k.Dx != 0) Move(me, k.Dx, 0, dt); else if (k.Dy != 0) Move(me, 0, k.Dy, dt);
                if (k.Drop) Drop(me);
                if (k.Needle) UseNeedle(me);
            }
            foreach (var p in Ps) if (!p.You && !p.Dead) Bot(p, dt);
            StepWorld(dt);
            CheckRoundEnd();
            return false;
        }

        void StepWorld(double dt)
        {
            foreach (var b in Balloons) b.T -= (float)dt;
            for (int guard = 0; guard < 20; guard++)
            {
                int i = Balloons.FindIndex(b => b.T <= 0);
                if (i < 0) break;
                Explode(i);
            }
            foreach (var kv in Water.ToList()) { if (kv.Value - dt <= 0) Water.Delete(kv.Key); else Water.Set(kv.Key, kv.Value - dt); }
            foreach (var kv in Items.ToList()) Items.Set(kv.Key, (kv.Value.kind, kv.Value.safe - dt));
            foreach (var p in Ps)
            {
                if (p.Dead) { p.DeadT += dt; continue; }
                var t = TileOf(p);
                if (p.Trapped > 0)
                {
                    p.Trapped -= dt;
                    if (p.Trapped <= 0) Kill(p, null);
                    continue;
                }
                int key = Rules.Key(t.C, t.R);
                if (Water.Has(key)) { p.Trapped = Rules.TRAP_TIME; p.Path = new List<Pt>(); Sound?.Invoke("trap"); Float?.Invoke(t.C, t.R, "被水泡困住！", "#9fe3ff"); continue; }
                if (Items.TryGet(key, out var it))
                {
                    Items.Delete(key);
                    switch (it.kind)
                    {
                        case ItemKind.Balloon: p.Max = Math.Min(Rules.MAX_BALLOONS, p.Max + 1); break;
                        case ItemKind.Potion: p.Range = Math.Min(Rules.MAX_RANGE, p.Range + 1); break;
                        case ItemKind.Ultra: p.Range = Rules.MAX_RANGE; break;
                        case ItemKind.Skate: p.SpeedLv = Math.Min(Rules.MAX_SPEED_LV, p.SpeedLv + 1); break;
                        default: p.Needles++; break;
                    }
                    Float?.Invoke(t.C, t.R, Rules.ItemName(it.kind), "#ffe066");
                    if (p.You) Sound?.Invoke("item");
                }
            }
            // Rivals touching a bubble pop it.
            foreach (var p in Ps)
            {
                if (p.Dead || p.Trapped <= 0) continue;
                foreach (var q in Ps)
                    if (q != p && !q.Dead && q.Trapped <= 0 && Js.Hypot(q.X - p.X, q.Y - p.Y) < 0.7) { Kill(p, q); break; }
            }
        }

        void UseNeedle(PlayerState p)
        {
            if (p.Trapped <= 0 || p.Needles <= 0) return;
            p.Needles--; p.Trapped = 0;
            var t = TileOf(p);
            Float?.Invoke(t.C, t.R, "用針脫困！", "#ffb0d0");
            Sound?.Invoke("free");
        }

        void Kill(PlayerState p, PlayerState by)
        {
            p.Dead = true; p.Trapped = 0; p.DeadT = 0;
            var t = TileOf(p);
            Float?.Invoke(t.C, t.R, by != null ? $"被 {by.Hero.Name} 戳破！" : "水泡爆掉了！", "#ff8aa0");
            Popped?.Invoke(p);
            Sound?.Invoke("popped");
        }

        void CheckRoundEnd()
        {
            var alive = Ps.Where(p => !p.Dead).ToList();
            bool decided = false;
            PlayerState winner = null;
            if (alive.Count == 0) decided = true;
            else if (alive.Count == 1 && alive[0].Trapped <= 0) { decided = true; winner = alive[0]; }
            else if (Clock <= 0) decided = true;
            if (!decided) return;
            if (winner != null) { winner.Wins++; RoundMsg = winner.You ? "你贏了這一回合！" : $"{winner.Hero.Name} 贏得這一回合"; }
            else RoundMsg = Clock <= 0 ? "時間到，平手！" : "同歸於盡，平手！";
            Phase = RoundPhase.End; Timer = 3;
            Sound?.Invoke(winner != null && winner.You ? "free" : "trap");
        }

        void Finish(int w)
        {
            Winner = w;
            Results = Js.Sorted(Ps, (a, b) => b.Wins - a.Wins).Select(p => new MatchResult { Id = p.Hero.Id, Name = p.Hero.Name, You = p.You, Wins = p.Wins, Color = p.Color }).ToList();
        }

        // ── bots ──
        void Bot(PlayerState p, double dt)
        {
            if (p.Trapped > 0) { if (p.Needles > 0 && p.Trapped < Rules.TRAP_TIME - 0.6) UseNeedle(p); return; }
            p.Think -= dt;
            var t = TileOf(p);
            if (p.Think <= 0 || p.Path.Count == 0)
            {
                p.Think = 0.35 + rng() * 0.2;
                var view = new BotView
                {
                    Grid = Grid, Balloons = Balloons.Cast<BalloonLike>().ToList(), Wet = Water.Keys.ToList(),
                    Items = Items.Keys.Select(k2 => new Pt(k2 % Rules.COLS, k2 / Rules.COLS)).ToList(),
                    Me = new Me { C = t.C, R = t.R, Left = p.Max - Active(p), Range = p.Range, Speed = Rules.SpeedOf(p.SpeedLv) },
                    Foes = Ps.Where(o => o != p && !o.Dead).Select(o => new Foe { C = (int)Js.Round(o.X), R = (int)Js.Round(o.Y), Trapped = o.Trapped > 0 }).ToList(),
                };
                var act = Rules.BotDecide(view, rng());
                if (act.Kind == BotKind.Bomb) { Drop(p); p.Path = act.Path; p.Think = 0.6; }
                else if (act.Kind == BotKind.Move) p.Path = act.Path;
                else p.Path = new List<Pt>();
            }
            if (p.Path.Count == 0) return;
            var next = p.Path[0];
            if (Math.Abs(next.C - p.X) > 0.05) Move(p, Math.Sign(next.C - p.X), 0, dt);
            else if (Math.Abs(next.R - p.Y) > 0.05) Move(p, 0, Math.Sign(next.R - p.Y), dt);
            else { p.X = next.C; p.Y = next.R; p.Path.RemoveAt(0); }
            // Unstick if blocked (a balloon or box appeared on the route).
            if (Js.Hypot(p.X - p.LastX, p.Y - p.LastY) < 0.001) { p.Stuck += dt; if (p.Stuck > 0.4) { p.Path = new List<Pt>(); p.Stuck = 0; } }
            else p.Stuck = 0;
            p.LastX = p.X; p.LastY = p.Y;
        }
    }
}
