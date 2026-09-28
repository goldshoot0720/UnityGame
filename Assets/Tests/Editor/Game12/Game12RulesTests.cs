// Port of Game12/cloud/src/verify.ts (map, blasts, pillars, crates, chains, danger, pathing, bots, items, match).
using System;
using System.Collections.Generic;
using System.Linq;
using MoeGames.Game12;
using NUnit.Framework;

namespace MoeGames.Tests
{
    public class Game12RulesTests
    {
        static Cell[,] Open()
        {
            var g = new Cell[Rules.ROWS, Rules.COLS];
            for (int r = 0; r < Rules.ROWS; r++) for (int c = 0; c < Rules.COLS; c++) g[r, c] = c % 2 == 1 && r % 2 == 1 ? Cell.Solid : Cell.Empty;
            return g;
        }

        static Cell[,] Copy(Cell[,] g) => (Cell[,])g.Clone();
        static List<BalloonLike> None => new List<BalloonLike>();

        [Test]
        public void Map()
        {
            Assert.IsTrue(Rules.HEROES.Length == 8 && Rules.HEROES.All(h => Cast.IndexOf(h.Id) >= 0 && h.Balloons + h.Range + h.Speed == 4), "eight heroes with art and equal stat budgets");
            var g = Rules.MakeGrid(42);
            Assert.IsTrue(g.GetLength(0) == Rules.ROWS && g.GetLength(1) == Rules.COLS && g[1, 1] == Cell.Solid && g[3, 5] == Cell.Solid && g[0, 1] != Cell.Solid, "15×13 grid with pillars on odd/odd cells");
            Assert.IsTrue(Rules.SPAWNS.All(s => g[s.R, s.C] == Cell.Empty) && g[0, 1] == Cell.Empty && g[1, 0] == Cell.Empty && g[Rules.ROWS - 1, Rules.COLS - 2] == Cell.Empty, "spawn corners and their neighbours are clear");
            Assert.Greater(g.Cast<Cell>().Count(x => x == Cell.Box), 60, "plenty of breakable boxes");
            Assert.IsTrue(g.Cast<Cell>().SequenceEqual(Rules.MakeGrid(42).Cast<Cell>()), "same seed → same map");
        }

        [Test]
        public void Blasts()
        {
            var open = Open();
            var b1 = Rules.Blast(open, None, 2, 2, 2);
            Assert.IsTrue(b1.Tiles.Count == 9 && b1.Tiles.Any(t => t.C == 4 && t.R == 2) && b1.Tiles.Any(t => t.C == 2 && t.R == 0), "blast: cross of 1 + 4×range on an open row");
            var b2 = Rules.Blast(open, None, 1, 2, 3);
            Assert.IsTrue(!b2.Tiles.Any(t => t.C == 1 && t.R == 1) && !b2.Tiles.Any(t => t.C == 1 && t.R == 3), "blast is stopped by pillars");
            var withBox = Copy(open); withBox[2, 4] = Cell.Box; withBox[2, 5] = Cell.Box;
            var b3 = Rules.Blast(withBox, None, 2, 2, 5);
            Assert.IsTrue(b3.Boxes.Count == 1 && b3.Boxes[0].C == 4 && !b3.Tiles.Any(t => t.C == 5), "blast soaks only the first box in a line");
            var bs = new List<BalloonLike> { new BalloonLike(2, 2, 1, 3), new BalloonLike(4, 2, 9, 2), new BalloonLike(4, 4, 9, 1) };
            var ch = Rules.Chain(open, bs, 0);
            Assert.IsTrue(ch.Balloons.Count == 3 && ch.Tiles.Any(t => t.C == 5 && t.R == 4), "chain reaction: a blast sets off balloons it reaches");
            var dm = Rules.DangerMap(open, bs);
            Assert.IsTrue(dm[Rules.Key(4, 5)] == 1 && float.IsPositiveInfinity(dm[Rules.Key(10, 10)]), "danger map: chained balloons inherit the short fuse; far cells safe");
            Assert.IsTrue(!Rules.Passable(open, bs, 4, 2) && Rules.Passable(open, bs, 3, 2), "balloons block walking");
            var path = Rules.Bfs(open, None, new Pt(0, 0), (c, r) => c == 4 && r == 0);
            Assert.IsTrue(path != null && path.Count == 4, "BFS finds the straight route");
        }

        static BotView V(int c, int r, List<BalloonLike> balloons = null, Cell[,] grid = null, List<Pt> items = null, int range = 2)
            => new BotView
            {
                Grid = grid ?? Open(), Balloons = balloons ?? None, Items = items ?? new List<Pt>(),
                Me = new Me { C = c, R = r, Left = 1, Range = range, Speed = 4 }, Foes = new List<Foe> { new Foe { C = 14, R = 12 } },
            };

        [Test]
        public void Bots()
        {
            var danger = new List<BalloonLike> { new BalloonLike(2, 2, Rules.FUSE, 2) };
            var flee = Rules.BotDecide(V(2, 2, danger));
            Assert.IsTrue(flee.Kind == BotKind.Move && flee.Path.Count > 0 && float.IsPositiveInfinity(Rules.DangerMap(Open(), danger)[Rules.Key(flee.Path[flee.Path.Count - 1].C, flee.Path[flee.Path.Count - 1].R)]), "bot standing in a blast zone runs to a safe cell");
            var boxy = Open(); boxy[0, 2] = Cell.Box;
            var bomb = Rules.BotDecide(V(1, 0, null, boxy), 0.1);
            Assert.IsTrue(bomb.Kind == BotKind.Bomb && bomb.Path.Count > 0, "bot drops a balloon next to a box and plans an escape");
            var trapBox = Open(); trapBox[0, 1] = Cell.Box; trapBox[1, 0] = Cell.Box;
            var noSuicide = Rules.BotDecide(V(0, 0, null, trapBox, null, 1), 0.1);
            Assert.AreNotEqual(BotKind.Bomb, noSuicide.Kind, "bot never bombs itself into a dead end");
            var grab = Rules.BotDecide(V(0, 0, null, null, new List<Pt> { new Pt(3, 0) }), 0.9);
            Assert.IsTrue(grab.Kind == BotKind.Move && grab.Path[grab.Path.Count - 1].C == 3, "bot walks to a nearby item");
            var popView = V(0, 0);
            popView.Foes = new List<Foe> { new Foe { C = 4, R = 0, Trapped = true } };
            var pop = Rules.BotDecide(popView);
            Assert.IsTrue(pop.Kind == BotKind.Move && pop.Path[pop.Path.Count - 1].C == 4, "bot goes to pop a trapped rival");
        }

        [Test]
        public void ItemsAndMatch()
        {
            Assert.IsTrue(Rules.DropFor(0.05) == ItemKind.Balloon && Rules.DropFor(0.2) == ItemKind.Potion && Rules.DropFor(0.3) == ItemKind.Skate && Rules.DropFor(0.45) == ItemKind.Ultra && Rules.DropFor(0.9) == null, "item table: common upgrades, rare ultra, some empties");
            Assert.Greater(Rules.SpeedOf(3), Rules.SpeedOf(0), "speed grows with skates");
            Assert.IsTrue(Rules.MatchWinner(new[] { 2, 0, 0, 0 }, 2) == 0 && Rules.MatchWinner(new[] { 1, 1, 0, 0 }, 2) == null && Rules.MatchWinner(new[] { 1, 1, 1, 0 }, 3) == null && Rules.MatchWinner(new[] { 1, 1, 1, 1 }, 5) == -1, "match: first to 2 wins, tie-break rounds, draw after 5");
        }

        [Test]
        public void BotsPlayAMatch()
        {
            // Extra smoke test: an idle human plus three bots — the match reaches a decision.
            var m = new Match("whale", Rand.Seeded(12));
            bool done = false;
            for (int i = 0; i < 60 * 60 * 20 && !done; i++) done = m.Update(1 / 60.0, default);
            Assert.IsTrue(done && m.Results != null && m.Results.Count == 4, "match finishes");
        }
    }
}
