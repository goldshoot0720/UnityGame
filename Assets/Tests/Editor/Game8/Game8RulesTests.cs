// Port of Game8/cloud/src/verify.ts (openers, keywords, taunt, counter damage, AI games, fatigue).
using System;
using System.Collections.Generic;
using System.Linq;
using MoeGames.Game8;
using NUnit.Framework;

namespace MoeGames.Tests
{
    public class Game8RulesTests
    {
        long seed;
        double Rng() { seed = seed * 16807 % 2147483647; return seed / 2147483647.0; }

        [SetUp]
        public void Seed() => seed = 7;

        [Test]
        public void Opening()
        {
            var b = new Battle(Rng, 0);
            b.Start();
            Assert.IsTrue(b.Players[0].Hand.Count == 4 && b.Players[1].Hand.Count == 4, "opening hands 3+1 draw vs 4");
            Assert.IsTrue(b.Players[0].Mana == 3 && b.Players[0].MaxMana == 3, "first turn gives 3 mana");
            Assert.IsTrue(b.Players[0].Deck.Count == 12 && b.Players[1].Deck.Count == 12, "decks are 16 cards minus draws");
            Assert.Greater(b.PlayableCards(0).Count, 0, "first player can play a card immediately");
        }

        [Test]
        public void RandomisedOpeners()
        {
            bool openingPlayable = true, secondPlayable = true, deckCountsPreserved = true;
            for (int i = 0; i < 500; i++)
            {
                var b = new Battle(Rng, i % 2);
                b.Start();
                openingPlayable &= b.PlayableCards(b.FirstPlayer).Count > 0;
                int second = b.OpponentOf(b.FirstPlayer);
                b.EndTurn(b.FirstPlayer);
                secondPlayable &= b.PlayableCards(second).Count > 0;
                foreach (var p in b.Players)
                {
                    var counts = p.Deck.Concat(p.Hand.Select(c => c.CardId)).GroupBy(x => x).Select(g => g.Count());
                    deckCountsPreserved &= counts.All(n => n <= RULES.COPIES_PER_CARD);
                }
            }
            Assert.IsTrue(openingPlayable, "500 randomized first-player openers have a playable card");
            Assert.IsTrue(secondPlayable, "500 randomized second-player openers have a playable card");
            Assert.IsTrue(deckCountsPreserved, "opening adjustment preserves deck copies");
        }

        [Test]
        public void Keywords()
        {
            var b = new Battle(Rng, 0);
            b.Start();
            var p0 = b.Players[0];
            var p1 = b.Players[1];
            p0.Mana = p0.MaxMana = 10;
            p0.Hand = new List<HandCard> { new HandCard { Uid = "a", CardId = "tshirt" }, new HandCard { Uid = "b", CardId = "redcat" }, new HandCard { Uid = "c", CardId = "whale" } };
            b.PlayCard(0, "a");
            Assert.IsTrue(b.CanAttack(0, "a"), "charge minions can attack at once");
            b.PlayCard(0, "b");
            Assert.AreEqual(RULES.HERO_HP - 2, p1.Hp, "battlecry deals 2 to enemy hero");
            Assert.IsFalse(b.CanAttack(0, "b"), "non-charge minions are asleep");
            p0.Hp = 20;
            b.PlayCard(0, "c");
            Assert.AreEqual(24, p0.Hp, "汐音 heals own hero 4");
            p1.Board.Add(new Minion { Uid = "T", CardId = "penguin", Atk = 1, Hp = 4, MaxHp = 4, Taunt = true });
            var vt = b.ValidTargets(0, "a");
            Assert.IsTrue(vt.Count == 1 && vt[0] == "T", "taunt forces targets");
            b.Attack(0, "a", "T");
            Assert.IsTrue(!p0.Board.Any(m => m.Uid == "a") && p1.Board[0].Hp == 2, "attacker takes counter damage and dies");
            Assert.AreEqual(10 - 1 - 3 - 4, p0.Mana, "mana spent");
        }

        [Test]
        public void BoardBlocksHero()
        {
            var b = new Battle(Rng, 0);
            b.Start();
            b.Players[0].Board = new List<Minion> { new Minion { Uid = "A", CardId = "tshirt", Atk = 2, Hp = 1, MaxHp = 1, Charge = true } };
            b.Players[1].Board = new List<Minion> { new Minion { Uid = "D", CardId = "redcat", Atk = 3, Hp = 2, MaxHp = 2 } };
            Assert.IsTrue(string.Join(",", b.ValidTargets(0, "A")) == "D" && !b.ValidTargets(0, "A").Contains(Battle.HeroId(1)), "an enemy card blocks direct hero attacks even without taunt");
            b.Attack(0, "A", "D");
            b.Players[0].Board = new List<Minion> { new Minion { Uid = "B", CardId = "tshirt", Atk = 2, Hp = 1, MaxHp = 1, Charge = true } };
            Assert.IsTrue(b.Players[1].Board.Count == 0 && string.Join(",", b.ValidTargets(0, "B")) == Battle.HeroId(1), "enemy hero becomes target only after all enemy cards are cleared");
        }

        [Test]
        public void AiGamesFinish()
        {
            int finished = 0, turns = 0;
            for (int g = 0; g < 40; g++)
            {
                var b = new Battle(Rng, g % 2);
                b.Start();
                int guard = 0;
                while (!b.IsOver && guard++ < 2000) Cpu.Apply(b, b.Current, Cpu.Choose(b, b.Current));
                if (b.IsOver) finished++;
                turns += b.Turn;
            }
            Assert.AreEqual(40, finished, "40 CPU-vs-CPU games all finish");
            Assert.IsTrue(turns / 40.0 > 8 && turns / 40.0 < 40, $"games last a sensible number of turns (avg {turns / 40.0:F1})");
        }

        [Test]
        public void Fatigue()
        {
            var b = new Battle(Rng, 0);
            b.Start();
            b.Players[0].Deck = new List<string>();
            b.EndTurn(0);
            b.EndTurn(1);
            Assert.IsTrue(b.Players[0].Fatigue == 1 && b.Players[0].Hp == RULES.HERO_HP - 1, "empty deck deals growing fatigue");
            Assert.AreEqual("H1", Battle.HeroId(1), "hero id helper");
        }
    }
}
