// Port of Game1/cloud/src/verify.ts (rules + sim; the Phaser boot-option checks do not apply).
using System;
using System.Linq;
using MoeGames.Game1;
using NUnit.Framework;

namespace MoeGames.Tests
{
    public class Game1RulesTests
    {
        [Test]
        public void CountBasesAndHalfInnings()
        {
            var g = GameState.ForUser("whale");
            Assert.IsTrue(g.Home.Id == "whale" && g.Top && g.BattingTeam.Id == "cat", "user team bats last (home)");
            g.Apply(PitchOutcome.Strike); g.Apply(PitchOutcome.Foul); g.Apply(PitchOutcome.Foul);
            Assert.AreEqual(2, g.Strikes, "foul with two strikes keeps the count");
            g.Apply(PitchOutcome.Strike);
            Assert.IsTrue(g.Outs == 1 && g.Strikes == 0 && g.Order["away"] == 1, "three strikes is an out and a new batter");
            for (int i = 0; i < 4; i++) g.Apply(PitchOutcome.Ball);
            Assert.IsTrue(g.Bases[0] && !g.Bases[1] && g.Balls == 0, "four balls is a walk to first");
            g.Apply(PitchOutcome.Homerun);
            Assert.IsTrue(g.Runs["away"] == 2 && !g.Bases.Any(b => b), "two-run homer scores 2 and clears bases");
            g.Apply(PitchOutcome.Double);
            Assert.IsTrue(g.Bases[1] && !g.Bases[0], "double puts runner on second");
            g.Apply(PitchOutcome.Single);
            Assert.IsTrue(g.Bases[0] && g.Bases[2] && !g.Bases[1], "single advances runner one base");
            g.Apply(PitchOutcome.Flyout);
            Assert.IsTrue(g.Runs["away"] == 3 && g.Outs == 2, "sac fly scores from third");
            g.Apply(PitchOutcome.Groundout);
            Assert.IsTrue(!g.Top && g.Outs == 0 && g.BattingTeam.Id == "whale", "third out flips to bottom half");
            Assert.AreEqual(3, g.Line["away"][0], "line score tracks the top of the 1st");
        }

        [Test]
        public void BasesLoadedWalkForcesARun()
        {
            var g = GameState.ForUser("cat");
            for (int b = 0; b < 3; b++) for (int i = 0; i < 4; i++) g.Apply(PitchOutcome.Ball);
            for (int i = 0; i < 4; i++) g.Apply(PitchOutcome.Ball);
            Assert.IsTrue(g.Runs["away"] == 1 && g.Bases.All(b => b), "bases-loaded walk forces in a run");
        }

        [Test]
        public void GamesEnd()
        {
            var g = GameState.ForUser("whale");
            int guard = 0;
            while (!g.Over && guard++ < 500) g.Apply(PitchOutcome.Groundout);
            Assert.IsTrue(g.Over && g.Inning == Data.INNINGS + 3, "a scoreless game goes to extras and ends");
            var h = GameState.ForUser("whale");
            while (!h.Over && guard++ < 1000) { if (!h.Top && h.Inning == Data.INNINGS) h.Apply(PitchOutcome.Homerun); else h.Apply(PitchOutcome.Groundout); }
            Assert.IsTrue(h.Over && h.Winner() == "home" && h.Inning == Data.INNINGS, "walk-off homer ends the game with home winning");
        }

        [Test]
        public void SwingsAndPitches()
        {
            var b = Data.PLAYERS["tshirt"];
            Assert.AreEqual(PitchOutcome.Homerun, Sim.ResolveSwing(b, 0, 0, Rand.Seq(0.5)), "sweet-spot perfect timing on a slugger is a homer");
            Assert.AreEqual(PitchOutcome.Strike, Sim.ResolveSwing(b, 0, 2, Rand.Seq(0.5)), "swing far off the ball whiffs");
            Assert.AreEqual(PitchOutcome.Strike, Sim.ResolveSwing(b, 0.2, 0, Rand.Seq(0.5)), "very late swing whiffs");
            var p = Sim.MakePitch(Data.PLAYERS["whale"], PitchName.指叉, 0, 0, Rand.Seq(0.5));
            var end = Sim.PitchPos(p, 1);
            Assert.IsTrue(Math.Abs(end.x) < 1e-9 && Math.Abs(end.y) < 1e-9, "pitch ends where it was aimed (no error at rng 0.5)");
            var start = Sim.PitchPos(p, 0);
            Assert.IsTrue(start.y < end.y - 0.5, "splitter starts high and drops");
            Assert.IsTrue(p.Time > 0.4 && p.Time < 1.2, "pitch flight time is sensible");
            Assert.AreEqual(PitchOutcome.Ball, Sim.CpuBat(Data.PLAYERS["glasses"], Sim.MakePitch(Data.PLAYERS["whale"], PitchName.直球, 3, 3, Rand.Seq(0.5)), Rand.Seq(0.99)), "far-outside pitch is a ball when taken");
            Assert.IsTrue(Sim.InZone(0.9, -0.9) && !Sim.InZone(1.2, 0), "zone test");
            Assert.IsTrue(Data.TEAMS.Values.All(t => t.Lineup.Length == 4 && Data.PLAYERS[t.Pitcher].Pitches.All(n => Data.PITCH_TYPES.ContainsKey(n))), "every team has 4 players & a pitcher with pitches");
        }

        [Test]
        public void CpuBattingIsBeatable()
        {
            // Balance guard: CPU batters facing typical pitches should hit roughly .20–.33 and strike out sometimes.
            var rng = Rand.Seeded(7);
            var batters = Data.PLAYERS.Values.ToArray();
            int pa = 0, hits = 0, ks = 0;
            for (int n = 0; n < 6000; n++)
            {
                var batter = batters[n % batters.Length];
                var pitcher = Data.PLAYERS[n % 2 == 0 ? "whale" : "whitecat"];
                int balls = 0, strikes = 0;
                while (true)
                {
                    var type = pitcher.Pitches[(int)(rng() * pitcher.Pitches.Length)];
                    var o = Sim.CpuBat(batter, Sim.MakePitch(pitcher, type, (rng() * 2 - 1) * 0.85, (rng() * 2 - 1) * 0.85, rng), rng);
                    if (o == PitchOutcome.Ball) { if (++balls == 4) break; continue; }
                    if (o == PitchOutcome.Strike) { if (++strikes == 3) { ks++; break; } continue; }
                    if (o == PitchOutcome.Foul) { strikes = Math.Min(2, strikes + 1); continue; }
                    if (Data.HitBases(o) > 0) hits++;
                    break;
                }
                pa++;
            }
            double avg = (double)hits / pa, k = (double)ks / pa;
            Assert.IsTrue(avg > 0.2 && avg < 0.33, $"CPU hit rate {avg:F3}");
            Assert.IsTrue(k > 0.12 && k < 0.35, $"CPU strikeout rate {k:F3}");
            Assert.IsTrue(Sim.ResolveSwing(Data.PLAYERS["penguin"], 0.15, 0, Rand.Seq(0.5), Data.USER_SWING_WINDOW, Data.USER_MEET_BONUS) != PitchOutcome.Strike, "user window forgives a 0.15 s timing error");
        }
    }
}
