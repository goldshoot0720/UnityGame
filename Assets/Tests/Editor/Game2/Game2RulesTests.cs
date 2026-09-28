// Port of Game2/cloud/src/verify.ts (rules, shooting, court).
using System.Linq;
using MoeGames.Game2;
using NUnit.Framework;

namespace MoeGames.Tests
{
    public class Game2RulesTests
    {
        [Test]
        public void ThreeByThreeRules()
        {
            var m = new Match { Possession = 0 };
            Assert.IsTrue(Match.Points(false) == 1 && Match.Points(true) == 2, "inside-arc basket = 1 point, beyond = 2");
            m.Made(0, true);
            Assert.IsTrue(m.Score[0] == 2 && m.Possession == 1 && !m.MustClear, "made basket scores and flips possession");
            m.Gain(0);
            Assert.IsTrue(m.Possession == 0 && m.MustClear, "defensive rebound/steal requires clearing the ball");
            m.Cleared();
            Assert.IsFalse(m.MustClear, "clearing lifts the restriction");
            m.ShotClock = 0.05;
            Assert.IsTrue(m.Tick(0.1, true) && m.Possession == 1 && m.ShotClock == Data.SHOT_CLOCK, "shot clock violation flips possession");
            double c = m.Clock;
            m.Tick(1, false);
            Assert.AreEqual(c, m.Clock, "clock does not run on a dead ball");
            var w = new Match();
            w.Score[0] = Data.WIN_SCORE - 1;
            w.Made(0, false);
            Assert.IsTrue(w.Over && w.Winner() == 0, "reaching 21 ends the game");
            var t = new Match { Clock = 0.01 };
            t.Tick(0.05, true);
            Assert.IsTrue(t.Overtime && !t.Over, "tied at the buzzer goes to sudden-death overtime");
            t.Made(1, false);
            Assert.IsTrue(t.Over && t.Winner() == 1, "first basket in overtime wins");
        }

        [Test]
        public void Shooting()
        {
            Assert.Greater(Match.ShotChance(30, false, 6, 0.8, 0), Match.ShotChance(260, true, 6, 0.8, 0), "layups are likelier than threes");
            Assert.Less(Match.ShotChance(150, false, 7, 0.8, 1), Match.ShotChance(150, false, 7, 0.8, 0), "contest lowers the odds");
            Assert.Greater(Match.ShotChance(200, false, 7, 1, 0), Match.ShotChance(200, false, 7, 0, 0), "good timing beats bad timing");
            Assert.IsTrue(Match.ShotChance(10, false, 10, 1, 0) <= 0.95 && Match.ShotChance(900, true, 1, 0, 1) >= 0.03, "chance stays within 3%..95%");
            Assert.Greater(Match.ShotChance(30, false, 6, 0, Data.USER_CONTEST_SCALE), 0.4, "a tapped, contested user layup still goes in often (balance)");
            Assert.AreEqual(1, Match.MeterQuality((Data.METER_SWEET[0] + Data.METER_SWEET[1]) / 2, Data.METER_SWEET), "meter sweet spot is perfect");
            Assert.Less(Match.MeterQuality(0.2, Data.METER_SWEET), 0.2, "meter far off is poor");
        }

        [Test]
        public void CourtGeometry()
        {
            Assert.IsFalse(Court.IsThree(Court.HOOP_X, Court.HOOP_Y + 20), "under the hoop is inside the arc");
            Assert.IsTrue(Court.IsThree(Court.TOP_OF_KEY_X, Court.TOP_OF_KEY_Y), "check-ball spot is beyond the arc");
            Assert.AreEqual(0, Court.HoopDist(Court.HOOP_X, Court.HOOP_Y), "hoop distance is 0 at the hoop");
            double x = -500, y = 9999;
            Court.ClampToCourt(ref x, ref y);
            Assert.IsTrue(y == Court.BOTTOM_Y && x > 0, "positions clamp onto the floor");
            Assert.IsTrue(Data.BALLERS.Length == 8 && Data.BALLERS.All(b => new[] { b.Shoot, b.Three, b.Speed, b.Defense, b.Jump }.All(v => v >= 1 && v <= 10)), "eight ballers with ratings 1..10");
        }

        [Test]
        public void SimulationRunsAFullGameHeadless()
        {
            // Extra smoke test: an unattended match (user idle) always terminates.
            var sim = new Sim(new[] { "whale", "penguin", "tshirt" }, new[] { "calico", "redcat", "sailor" }, Rand.Seeded(7));
            int frames = 0;
            while (sim.Update(1 / 30.0, default) && frames < 30 * 60 * 20) frames++;
            Assert.IsTrue(sim.M.Over, "match ends");
        }
    }
}
