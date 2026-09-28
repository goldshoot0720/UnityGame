// Port of Game7/cloud/src/verify.ts (fighters, arena layout, collision, line of sight, ranking).
using System.Collections.Generic;
using System.Linq;
using MoeGames.Game7;
using NUnit.Framework;

namespace MoeGames.Tests
{
    public class Game7RulesTests
    {
        static bool InCrate(double x, double y, double r) => Data.CRATES.Any(c => Data.CircleBox(x, y, r, c) != null);

        [Test]
        public void FightersAndWeapons()
        {
            Assert.IsTrue(Data.FIGHTERS.Length == 8 && Data.FIGHTERS.All(f => Cast.IndexOf(f.Id) >= 0) && new HashSet<Kind>(Data.FIGHTERS.Select(f => f.Weapon.Kind)).Count == 8, "eight fighters, each with sprite and a distinct weapon");
            Assert.IsTrue(Data.FIGHTERS.All(f => f.Weapon.Dmg > 0 && f.Weapon.Rate > 0.05 && f.Weapon.Range > 200 && f.Weapon.Speed > 100), "every weapon has sane numbers");
        }

        [Test]
        public void ArenaLayout()
        {
            double R = Data.RADIUS;
            Assert.IsTrue(Data.SPAWNS.All(s => s.x > R && s.y > R && s.x < Data.ARENA_W - R && s.y < Data.ARENA_H - R && !InCrate(s.x, s.y, R)), "spawns are inside the arena and clear of crates");
            Assert.IsTrue(Data.PICKUP_SPOTS.All(p => !InCrate(p.x, p.y, 20)), "pickups are clear of crates");
            Assert.GreaterOrEqual(Data.SPAWNS.Length, 8, "enough spawns for 8 players");
        }

        [Test]
        public void CollisionAndSight()
        {
            var push = Data.CircleBox(600, 350, 22, new double[] { 560, 360, 96, 96 });
            Assert.IsTrue(push.HasValue && push.Value.y < 0, "circle touching a crate is pushed out upward");
            Assert.IsNull(Data.CircleBox(100, 100, 22, new double[] { 560, 360, 96, 96 }), "circle far away is not pushed");
            Assert.IsTrue(Data.Blocked(500, 408, 820, 408) && !Data.Blocked(100, 100, 300, 100), "line of sight is blocked by a crate");
        }

        [Test]
        public void RankingOrder()
        {
            var rows = new List<(int kills, int deaths, string n)> { (3, 5, "a"), (5, 9, "b"), (3, 1, "c") };
            var r = Data.Ranking(rows, x => x.kills, x => x.deaths);
            Assert.AreEqual("bca", string.Concat(r.Select(x => x.n)), "ranking: kills desc, then fewer deaths");
        }

        [Test]
        public void BotsPlayAMatch()
        {
            // Extra smoke test: 7 bots + an idle player; the match ends on time or kills.
            var sim = new ArenaSim("whale", Rand.Seeded(5));
            bool done = false;
            for (int i = 0; i < 30 * 200 && !done; i++) done = sim.Update(1 / 30.0, default);
            Assert.IsTrue(done && sim.Stats.Count == 8, "match ends with a scoreboard");
            Assert.Greater(sim.Ps.Sum(p => p.Kills), 0, "bots score kills");
        }
    }
}
