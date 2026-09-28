// Port of Game11/cloud/src/verify.ts (pilots, DPS balance, shots, waves, paths, patterns, bosses).
using System;
using System.Linq;
using MoeGames.Game11;
using NUnit.Framework;

namespace MoeGames.Tests
{
    public class Game11RulesTests
    {
        [Test]
        public void Pilots()
        {
            Assert.IsTrue(Rules.PILOTS.Length == 8 && Rules.PILOTS.All(p => Cast.IndexOf(p.Id) >= 0) && Rules.PILOTS.Select(p => p.Shot).Distinct().Count() == 8, "eight pilots with art and eight distinct shot types");
            Assert.IsTrue(Rules.PILOTS.All(p => Enumerable.Range(1, Rules.MAX_POWER - 1).All(l => Rules.Dps(p.Shot, l + 1) > Rules.Dps(p.Shot, l))), "every shot type gets stronger with power");
            var d4 = Rules.PILOTS.Select(p => Rules.Dps(p.Shot, 4)).ToList();
            Assert.Less(d4.Max() / d4.Min(), 2, "max-power DPS is balanced within 2× across pilots");
            Assert.IsTrue(Rules.Volley(ShotType.Spread, 9).Count == Rules.Volley(ShotType.Spread, 4).Count && Rules.Volley(ShotType.Spread, 0).Count == Rules.Volley(ShotType.Spread, 1).Count, "power is clamped to 1..4");
            Assert.IsTrue(Rules.Volley(ShotType.Laser, 2).All(s => s.Pierce) && Rules.Volley(ShotType.Flame, 1).All(s => s.Life < 0.5) && Rules.Volley(ShotType.Backfire, 1).Any(s => s.Ang == 180), "laser pierces, flame is short-ranged, backfire shoots behind");
            Assert.IsTrue(Rules.Missiles(ShotType.Homing, 3).Count == 3 && Rules.Missiles(ShotType.Spread, 3).Count == 0 && Rules.Orbiters(ShotType.Orbit, 2) == 3 && Rules.Orbiters(ShotType.Laser, 4) == 0, "only the homing pilot fires missiles; only whitecat has pages");
        }

        [Test]
        public void Waves([Values(0, 1)] int s)
        {
            var w = Rules.StageWaves(s);
            bool sorted = w.Select((x, i) => i == 0 || w[i - 1].T <= x.T).All(b => b);
            Assert.IsTrue(sorted && w.Count(x => x.Kind == EnemyKind.Midboss) == 1 && w[w.Count - 1].Kind == Rules.STAGES[s].Boss && w[w.Count - 1].T == Rules.BOSS_T, $"stage {s + 1}: sorted waves, one midboss, boss last");
            Assert.GreaterOrEqual(w.Count(x => x.T < Rules.BOSS_T && x.Kind != EnemyKind.Midboss), 20, $"stage {s + 1}: at least 20 regular waves");
        }

        [Test]
        public void StagesPathsAndMaths()
        {
            Assert.IsTrue(Rules.STAGES[1].BulletSpeed > Rules.STAGES[0].BulletSpeed && Rules.STAGES[1].FireMul > Rules.STAGES[0].FireMul && Rules.StageWaves(1).Count > Rules.StageWaves(0).Count, "stage 2 is harder");
            var p0 = Rules.PathPos(PathKind.SwoopL, 0.1, 0, 0);
            var p2 = Rules.PathPos(PathKind.SwoopL, 0.1, 2.2, 0);
            var p5 = Rules.PathPos(PathKind.SwoopL, 0.1, 5, 0);
            Assert.IsTrue(p0.y < 0 && p2.y > 200 && p5.y < p2.y && p5.x > p0.x, "swoop enters from the top, dives, then retreats upward");
            Assert.IsTrue(Rules.PathPos(PathKind.SideL, 0.2, 0, 0).x < 0 && Rules.PathPos(PathKind.SideL, 0.2, 4, 0).x > Rules.PF_W, "side paths cross the whole field");
            Assert.IsTrue(Math.Abs(Rules.Ring(4)[1] - Math.PI / 2) < 1e-9 && Math.Abs(Rules.Fan(3, 1, 0.4)[1] - 1) < 1e-9 && Rules.Fan(1, 2, 1)[0] == 2, "ring spreads evenly, fan is centred");
            Assert.Less(Math.Abs(Rules.AimAt(0, 0, 0, 10) - Math.PI / 2), 1e-9, "aim points at the target");
            Assert.IsTrue(Rules.Hit(0, 0, 4, 7, 0, 4) && !Rules.Hit(0, 0, 4, 9, 0, 4), "circle hit test");
            Assert.IsTrue(Rules.BossPhase(1000, 1000) == 0 && Rules.BossPhase(500, 1000) == 1 && Rules.BossPhase(100, 1000) == 2, "boss phases by HP");
            Assert.IsTrue(Rules.ExtendsBetween(59000, 61000) == 1 && Rules.ExtendsBetween(0, 200000) == 2 && Rules.ExtendsBetween(61000, 62000) == 0, "extends at 60k and 180k");
            var E = Rules.ENEMY;
            Assert.IsTrue(E[EnemyKind.Boss2].Hp > E[EnemyKind.Boss1].Hp && E[EnemyKind.Boss1].Hp > E[EnemyKind.Midboss].Hp * 3 && E[EnemyKind.Midboss].Hp > E[EnemyKind.Bomber].Hp * 5, "bosses are much tougher than grunts");
        }

        [Test]
        public void GodModeClearsBothStages()
        {
            // Mirrors the ?god headless play-test: an untouchable ship auto-fires through both stages.
            var s = new Sortie("tshirt", Rand.Seeded(11)) { God = true };
            bool done = false;
            for (int i = 0; i < 60 * 60 * 6 && !done; i++)
            {
                double target = s.Enemies.Count > 0 ? s.Enemies[0].X : 270;
                int dx = Math.Abs(target - s.Px) > 8 ? Math.Sign(target - s.Px) : 0;
                done = s.Update(1 / 60.0, new ShipInput { Dx = dx });
            }
            Assert.IsTrue(done && s.Won, "both stages cleared");
        }
    }
}
