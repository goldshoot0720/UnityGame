// Port of Game6/cloud/src/verify.ts (rules part; the Phaser boot-option checks do not apply).
using System.Linq;
using MoeGames.Game6;
using NUnit.Framework;

namespace MoeGames.Tests
{
    public class Game6RulesTests
    {
        static readonly string[] keys = Tactics.CHARS.Select(c => c.Key).ToArray();

        [Test]
        public void MapsAreValid([Values(0, 1, 2)] int mi)
        {
            var m = Tactics.MAPS[mi];
            Assert.IsTrue(m.Rows.Length == Tactics.ROWS && m.Rows.All(r => r.Length == Tactics.COLS && r.All(ch => Tactics.TERRAIN.ContainsKey(ch))), $"{m.Name}: {Tactics.ROWS}×{Tactics.COLS} of known terrain");
            Assert.IsTrue(m.P.Concat(m.E).All(p => m.Rows[p.y][p.x] != 'W' && m.Rows[p.y][p.x] != 'M'), $"{m.Name}: every spawn is on walkable land");
            var b = Board.Create(m, keys.Take(4).ToList(), keys.Skip(4).ToList());
            var field = b.DistanceField(b.Team('P')[1], b.Team('E'));
            Assert.IsTrue(b.Team('P').All(u => field.Has(Tactics.Key(u.X, u.Y))), $"{m.Name}: the armies can reach each other");
        }

        [Test]
        public void BoardRules()
        {
            var b = Board.Create(Tactics.MAPS[0], new[] { "whale", "penguin", "glasses", "tshirt" }, new[] { "calico", "whitecat", "redcat", "sailor" });
            var p = b.Team('P');
            var whale = p[0]; var penguin = p[1]; var glasses = p[2]; var tshirt = p[3];
            var e = b.Team('E');
            var calico = e[0]; var whitecat = e[1]; var redcat = e[2];

            var r = b.Reachable(tshirt);
            Assert.IsTrue(r.Values.All(n => n.C <= tshirt.C.Mov), "reach respects movement points");

            Assert.IsTrue(b.MoveCost(penguin, 5, 1) >= 99 && b.MoveCost(whale, 5, 1) == 1, "water blocks normal units");

            redcat.X = 5; redcat.Y = 4; tshirt.X = 4; tshirt.Y = 4;
            Assert.AreEqual(10 - 4, b.CalcDamage(redcat, tshirt).Dmg, "damage = atk − (def + terrain)");
            whitecat.X = 3; whitecat.Y = 4;
            Assert.AreEqual(10 - 4 / 2, b.CalcDamage(whitecat, tshirt).Dmg, "magic halves defence");
            Assert.GreaterOrEqual(b.CalcDamage(whale, penguin).Dmg, 1, "damage is at least 1");

            glasses.X = 8; glasses.Y = 4; calico.X = 9; calico.Y = 4;
            int hpBefore = calico.Hp;
            var res = b.Combat(calico, glasses, () => 0.99);
            Assert.IsTrue(res.Counter == 0 && calico.Hp == hpBefore, "archer cannot counter at range 1");

            Assert.AreEqual(2 * b.CalcDamage(calico, tshirt).Dmg, b.CalcDamage(calico, tshirt, () => 0.1).Dmg, "rogue crit doubles damage");

            penguin.Hp = penguin.C.Hp - 3;
            Assert.IsTrue(b.Heal(whale, penguin) == 3 && penguin.Hp == penguin.C.Hp, "healing is capped at max HP");

            tshirt.X = 8; tshirt.Y = 2; tshirt.Hp = 10;
            var healed = b.PhaseHeal('P');
            Assert.IsTrue(healed.Any(h => h.u == tshirt && h.amt == 5) && tshirt.Hp == 15, "houses restore 5 HP at phase start");

            tshirt.Hp = 1; tshirt.X = 6; tshirt.Y = 4;
            redcat.X = 7; redcat.Y = 5;
            var plan = b.Plan(redcat);
            Assert.IsTrue(plan.Type == "atk" && plan.Target == tshirt, "AI goes for the kill");

            foreach (var u in b.Team('E')) u.Hp = 0;
            Assert.AreEqual('P', b.Winner(), "wiping the enemy wins");
        }
    }
}
