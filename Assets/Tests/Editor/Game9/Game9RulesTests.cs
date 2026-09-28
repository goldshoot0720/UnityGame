// Port of Game9/cloud/src/verify.ts (board layout, movement, economy, perks, liquidation, AI).
using System;
using System.Collections.Generic;
using System.Linq;
using MoeGames.Game9;
using NUnit.Framework;

namespace MoeGames.Tests
{
    public class Game9RulesTests
    {
        static readonly Func<double> rnd = () => 0.5;

        [Test]
        public void Layout()
        {
            Assert.IsTrue(Rules.HEROES.Length == 8 && Rules.HEROES.All(h => Cast.IndexOf(h.Id) >= 0) && new HashSet<PerkId>(Rules.HEROES.Select(h => h.Perk)).Count == 8, "eight heroes with art and distinct perks");
            var board = Rules.MakeBoard();
            var props = board.Where(t => t.Kind == Kind.Prop).ToList();
            Assert.IsTrue(board.Count == Rules.BOARD_SIZE && props.Count == 18 && Enumerable.Range(0, 6).All(g => props.Count(t => t.Group == g) == 3), "28 spaces, 18 deeds in 6 districts of 3");
            Assert.IsTrue(board[0].Kind == Kind.Start && board[7].Kind == Kind.Rest && board[14].Kind == Kind.Pot && board[21].Kind == Kind.Jail, "corners are start / rest / pot / jail");
            var cells = new HashSet<(int, int)>(board.Select(t => Rules.GridOf(t.I)));
            Assert.IsTrue(cells.Count == 28 && board.All(t => { var g = Rules.GridOf(t.I); return g.gx == 0 || g.gy == 0 || g.gx == 7 || g.gy == 7; }), "ring layout: every space on a distinct edge cell");
        }

        [Test]
        public void Economy()
        {
            var board = Rules.MakeBoard();
            var ps = Rules.CreatePlayers("whale", rnd);
            Assert.IsTrue(ps.Count == 4 && !ps[0].Ai && ps.Skip(1).All(p => p.Ai) && new HashSet<string>(ps.Select(p => p.Hero.Id)).Count == 4 && ps[0].Cash == Rules.START_CASH, "you plus three distinct rivals");
            Assert.AreEqual(Rules.START_CASH + 300, Rules.CreatePlayers("sailor", rnd)[0].Cash, "sailor perk: +300 starting cash");
            var me = ps[0];
            var b1 = board[1];
            me.Pos = 26;
            int c0 = me.Cash;
            Assert.IsTrue(Rules.Advance(me, 4) && me.Pos == 2 && me.Cash == c0 + Rules.PASS_START + 100, "passing start pays the bonus (+100 whale perk)");
            Rules.Hop(me, -1); Rules.Hop(me, -1); Rules.Hop(me, -1);
            Assert.IsTrue(me.Pos == 27 && me.Cash == c0 + Rules.PASS_START + 100, "moving backwards over start pays nothing");
            int w0 = Rules.Worth(me, board);
            Assert.IsTrue(Rules.Buy(me, b1) && b1.Owner == 0 && Rules.Worth(me, board) == w0, "buying a deed keeps net worth, changes owner");
            Assert.IsFalse(Rules.Buy(ps[1], b1), "cannot buy an owned deed");
            var rival = ps[1];
            int r0 = Rules.RentOf(b1, board);
            Assert.IsTrue(r0 == Rules.BaseRent(b1) && r0 == 10, "base toll is 10% of price");
            Rules.Buy(me, board[2]); Rules.Buy(me, board[4]);
            Assert.AreEqual(r0 * 2, Rules.RentOf(b1, board), "owning a whole district doubles the empty-lot toll");
            Assert.IsTrue(Rules.Upgrade(me, b1) && Rules.RentOf(b1, board) == Rules.BaseRent(b1) * 4 && Rules.Upgrade(me, b1) && Rules.Upgrade(me, b1) && b1.Level == Rules.MAX_LEVEL && !Rules.Upgrade(me, b1) && Rules.RentOf(b1, board) == Rules.BaseRent(b1) * 16, "upgrading raises toll 4× then up to a hotel");
            var glasses = Rules.CreatePlayers("glasses", rnd)[0];
            var tshirt = Rules.CreatePlayers("tshirt", rnd)[0];
            Assert.IsTrue(Rules.LandCost(board[27], glasses.Hero.Perk) == (int)(Js.Round(board[27].Price * 0.9 / 10) * 10) && Rules.UpgradeCost(board[27], tshirt.Hero.Perk) < Rules.UpgradeCost(board[27]), "land and build discounts");
            Assert.IsTrue(Rules.TaxFor(Rules.CreatePlayers("penguin", rnd)[0]) == 75 && Rules.TaxFor(me) == 150, "penguin pays half tax");
            var calico = Rules.CreatePlayers("calico", rnd)[0];
            Assert.IsTrue(Rules.CardCash(100, calico) == 200 && Rules.CardCash(-80, calico) == -80, "calico doubles windfalls only");
            int beforeOwner = me.Cash, beforePayer = rival.Cash;
            var pr = Rules.PayRent(rival, me, b1, board);
            Assert.IsTrue(pr.Paid == pr.Amount && me.Cash == beforeOwner + pr.Amount && rival.Cash == beforePayer - pr.Amount, "toll moves cash from payer to owner");
        }

        [Test]
        public void LiquidationAndBankruptcy()
        {
            var b2 = Rules.MakeBoard();
            var two = Rules.CreatePlayers("tshirt", rnd);
            var a = two[0];
            var bb = two[1];
            Rules.Buy(a, b2[27]);
            a.Cash = 10;
            var lq = Rules.DoCharge(a, 50, b2);
            Assert.IsTrue(lq.Sold.Count == 1 && a.Alive && b2[27].Owner == -1 && a.Cash == 10 - 50 + (int)Math.Floor(b2[27].Price * 0.6), "short on cash: sells a deed at 60% before going bankrupt");
            Rules.Buy(bb, b2[1]);
            bb.Cash = 0;
            var bk = Rules.DoCharge(bb, 5000, b2);
            Assert.IsTrue(bk.Bankrupt && !bb.Alive && bb.Cash == 0 && bk.Paid == (int)Math.Floor(b2[1].Price * 0.6) && b2.All(t => t.Owner != bb.Id), "bankruptcy: pays what it can and releases deeds");
            Assert.AreSame(a, Rules.Ranking(new[] { bb, a }, b2)[0], "ranking puts survivors first, then by worth");
        }

        [Test]
        public void AiAndCards()
        {
            var ai = Rules.CreatePlayers("whale", rnd)[1];
            ai.Cash = 150;
            Assert.IsFalse(Rules.AiWantsBuy(ai, Rules.MakeBoard()[27], Rules.MakeBoard()), "AI keeps a cash reserve before buying");
            ai.Cash = 2000;
            Assert.IsTrue(Rules.AiWantsBuy(ai, Rules.MakeBoard()[27], Rules.MakeBoard()), "AI buys with enough cash");
            Assert.IsTrue(Rules.CARDS.Length >= 10 && new HashSet<CardKind>(Rules.CARDS.Select(c => c.Kind)).Count >= 6, "chance deck has variety");
        }

        [Test]
        public void FullGameWithAutoHuman()
        {
            // Extra smoke test: the human always rolls/buys/ends; the game reaches a result.
            var g = new Game("whale", Rand.Seeded(9));
            bool done = false;
            for (int i = 0; i < 200000 && !done; i++)
            {
                if (!g.P.Ai)
                {
                    if (g.Phase == Phase.Await) g.Roll();
                    else if (g.Phase == Phase.Decide) g.Decide(true);
                    else if (g.Phase == Phase.Card) g.CardOk();
                    else if (g.Phase == Phase.End) g.EndTurnNow();
                }
                done = g.Update(0.05);
            }
            Assert.IsTrue(done && g.Results.Count == 4, "game finishes with standings");
        }
    }
}
