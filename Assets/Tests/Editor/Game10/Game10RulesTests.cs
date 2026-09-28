// Port of Game10/cloud/src/verify.ts (terrain, craters, ballistics, damage, AI aim, standings, ammo).
using System;
using System.Collections.Generic;
using System.Linq;
using MoeGames.Game10;
using NUnit.Framework;

namespace MoeGames.Tests
{
    public class Game10RulesTests
    {
        [Test]
        public void RosterAndWeapons()
        {
            Assert.IsTrue(Rules.HEROES.Length == 8 && Rules.HEROES.All(h => Cast.IndexOf(h.Id) >= 0) && new HashSet<string>(Rules.HEROES.Select(h => h.Special.Kind + ":" + h.Special.Count)).Count == 8, "eight drivers, each with art and a distinct special");
            Assert.IsTrue(Rules.BASE_WEAPONS.Length == 3 && Rules.BASE_WEAPONS[0].Uses == -1 && Rules.BASE_WEAPONS.Skip(1).All(w => w.Uses > 0), "three base weapons, standard is unlimited");
            Assert.IsTrue(Rules.TANK_R > 10 && Rules.TANK_R < 40, "tank radius sane");
        }

        [Test]
        public void TerrainAndCraters()
        {
            var t = Rules.MakeTerrain(1234);
            Assert.IsTrue(t.Length == Rules.FIELD_W + 1 && Rules.MakeTerrain(1234)[900] == t[900] && Rules.MakeTerrain(99)[900] != t[900], "terrain spans the field and is deterministic");
            Assert.IsTrue(t.Any(y => y < 450) && t.Any(y => y > Rules.WATER_Y - 60), "terrain has land above water and a valley near it");
            var xs = Rules.SpawnXs(t, 4);
            Assert.IsTrue(xs.Count == 4 && xs.All(x => Rules.Surface(t, x) < Rules.WATER_Y - 30) && xs[3] - xs[0] > Rules.FIELD_W * 0.5, "four spawns on dry land, spread out");
            var t2 = (double[])t.Clone();
            double y0 = Rules.Surface(t2, 1200);
            Rules.Carve(t2, 1200, y0, 50);
            Assert.IsTrue(Rules.Surface(t2, 1200) > y0 + 40 && Rules.Surface(t2, 1200) >= Rules.Surface(t2, 1240), "explosion carves a crater (lower in the middle than at the rim)");
            var t3 = (double[])t.Clone();
            Rules.Carve(t3, 1200, y0, 50, 2.4);
            Assert.Greater(Rules.Surface(t3, 1200), Rules.Surface(t2, 1200), "drill digs deeper than a normal blast");
        }

        [Test]
        public void Ballistics()
        {
            var v = Rules.LaunchVelocity(1, 45, 100);
            Assert.IsTrue(v.vx > 0 && v.vy < 0 && Math.Abs(v.vx + v.vy) < 1e-6, "launch at 45° goes up and to the facing side");
            Assert.Less(Rules.LaunchVelocity(-1, 0, 10, ShotKind.Laser).vx, -1000, "laser ignores power");
            var flat = Enumerable.Repeat(500.0, Rules.FIELD_W + 1).ToArray();
            var none = new List<BodyPos>();
            var noWind = Rules.Simulate(flat, 400, 460, 300, -300, 0, none, -1);
            var tail = Rules.Simulate(flat, 400, 460, 300, -300, 8, none, -1);
            Assert.IsTrue(noWind.Hit == -1 && tail.Hit == -1 && tail.X > noWind.X + 20, "shell lands on the ground; tail wind carries it further");
            var tanks = new List<BodyPos> { new BodyPos { X = 400, Y = 500, Alive = true }, new BodyPos { X = noWind.X, Y = 500, Alive = true } };
            Assert.AreEqual(1, Rules.Simulate(flat, 400, 460, 300, -300, 0, tanks, 0).Hit, "shell can hit a tank directly");
            double D(double ex, double ey, bool direct) => Rules.BlastDamage(30, 50, ex, ey, 0, 0, direct);
            Assert.IsTrue(D(0, 0, true) > D(0, -18, false) && D(0, -18, false) > D(40, -18, false) && D(200, -18, false) == 0, "blast damage: direct > centre > rim > outside");
            Assert.IsTrue(Rules.FallDamage(30) == 0 && Rules.FallDamage(130) == 20, "fall damage only for long drops");
            var wall = (double[])flat.Clone();
            for (int x = 600; x <= 700; x++) wall[x] = 300;
            Assert.IsTrue(Rules.CanDrive(flat, 500, 501) && !Rules.CanDrive(wall, 599, 600), "tanks cannot drive up a cliff");
        }

        [Test]
        public void AiAims()
        {
            var flat = Enumerable.Repeat(500.0, Rules.FIELD_W + 1).ToArray();
            var foes = new List<BodyPos> { new BodyPos { X = 500, Y = 500, Alive = true }, new BodyPos { X = 1300, Y = 500, Alive = true } };
            var calm = Rules.AiAim(flat, foes, 0, 1, 0, 50);
            var windy = Rules.AiAim(flat, foes, 0, 1, -7, 50);
            Assert.IsTrue(calm.Miss < 40 && windy.Miss < 40 && calm.Facing == 1, "AI finds a shot within 40px of the target (calm and windy)");
            var back = Rules.AiAim(flat, new List<BodyPos> { new BodyPos { X = 1300, Y = 500, Alive = true }, new BodyPos { X = 500, Y = 500, Alive = true } }, 0, 1, 0, 50);
            Assert.IsTrue(back.Facing == -1 && back.Miss < 40, "AI faces left when the target is left");
        }

        [Test]
        public void StandingsAndAmmo()
        {
            var rows = new List<(bool alive, double hp, int diedAt, string n)> { (false, 0, 3, "a"), (true, 20, 0, "b"), (false, 0, 7, "c") };
            Assert.AreEqual("bca", string.Concat(Rules.Standings(rows, r => r.alive, r => r.hp, r => r.diedAt).Select(r => r.n)), "standings: survivors first, then latest eliminated");
            var u = new[] { -1, 0, 3, 1 };
            Assert.IsTrue(string.Join(",", Rules.Resupply(u, new[] { 0, 1, 2, 3 })) == "1,3" && string.Join(",", u) == $"-1,1,{Rules.AMMO_CAPS[2]},2" && Rules.Resupply(u, new[] { 3 }).Count == 0, "ammo crate restocks every slot up to its cap, never the unlimited shell");
            Assert.IsTrue(string.Join(",", Rules.AutoSupplySlots(3)) == "1" && string.Join(",", Rules.AutoSupplySlots(5)) == "2" && string.Join(",", Rules.AutoSupplySlots(15)) == "1,2" && Rules.AutoSupplySlots(4).Count == 0, "automatic resupply: triple every 3rd turn, heavy every 5th");
        }

        [Test]
        public void BotsFinishABattle()
        {
            // Extra smoke test: the human tank just holds fire briefly each turn; the battle ends.
            var b = new Battle("whale", Rand.Seeded(4));
            bool done = false;
            int held = 0;
            for (int i = 0; i < 60 * 60 * 30 && !done; i++)
            {
                var k = new TankInput();
                if (b.Phase == Phase.Aim && b.Tk.You) { held++; k.FirePressed = held == 1; k.FireHeld = held < 50; }
                else held = 0;
                done = b.Update(1 / 60.0, k);
            }
            Assert.IsTrue(done && b.Results.Count == 4, "battle finishes with standings");
        }
    }
}
