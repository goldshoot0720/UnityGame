// Port of Game5/cloud/src/verify.ts — headless fights driven through the pure simulation.
using System;
using System.Linq;
using MoeGames.Game5;
using NUnit.Framework;

namespace MoeGames.Tests
{
    public class Game5RulesTests
    {
        static readonly Input NO = new Input();

        static void Run(Fight f, int frames, Func<int, Input> a, Func<int, Input> b = null)
        {
            for (int n = 0; n < frames; n++) f.Step(a(n), b != null ? b(n) : NO);
        }

        static Fighter R(int i) => Roster.ROSTER[i];

        [Test]
        public void Roster8()
        {
            Assert.IsTrue(Roster.ROSTER.Length == 8 && Roster.ROSTER.All(r => r.Special != null && r.Super != null), "eight fighters with a special and a super");
            Assert.AreEqual(Roster.GROUND_Y, new Fight(R(0), R(1)).P[0].Y, "fighters rest on the ground");
        }

        [Test]
        public void WalkingAndFacing()
        {
            var f = new Fight(R(0), R(1));
            double x0 = f.P[0].X;
            Run(f, 30, n => new Input { Right = true });
            Assert.Greater(f.P[0].X, x0 + 40, "walking forward moves the fighter");
            Assert.IsTrue(f.P[0].Facing == 1 && f.P[1].Facing == -1, "fighters face each other");
        }

        [Test]
        public void JabConnects()
        {
            var f = new Fight(R(0), R(1));
            f.P[0].X = 600; f.P[1].X = 700;
            Run(f, 2, n => NO);
            double hp = f.P[1].Hp;
            Run(f, 25, n => new Input { Lp = n == 0 });
            Assert.Less(f.P[1].Hp, hp, "a close light punch deals damage");
            Assert.Greater(f.P[0].Meter, 0, "attacker gains meter");
        }

        [Test]
        public void HoldingBackBlocks()
        {
            var f = new Fight(R(0), R(1));
            f.P[0].X = 600; f.P[1].X = 700;
            Run(f, 2, n => NO, n => new Input { Right = true });
            double hp = f.P[1].Hp;
            Run(f, 25, n => new Input { Hp = n == 0 }, n => new Input { Right = true });
            Assert.AreEqual(hp, f.P[1].Hp, "holding back blocks a normal (no damage)");
        }

        [Test]
        public void LowBeatsStandingBlock()
        {
            var f = new Fight(R(0), R(1));
            f.P[0].X = 600; f.P[1].X = 700;
            Run(f, 2, n => NO, n => new Input { Right = true });
            double hp = f.P[1].Hp;
            Run(f, 30, n => new Input { Down = true, Hk = n == 0 }, n => new Input { Right = true });
            Assert.Less(f.P[1].Hp, hp, "a sweep hits a standing blocker");
        }

        [Test]
        public void MotionInputs()
        {
            var hist = new System.Collections.Generic.List<(int dir, int f)> { (2, 1), (3, 3), (6, 5) };
            Assert.IsTrue(Moves.MatchMotion(hist, Moves.MOTIONS["qcf"].seq, 6), "qcf motion is recognised");
            Assert.IsFalse(Moves.MatchMotion(hist, Moves.MOTIONS["qcf"].seq, 200), "an old motion is ignored");
            var f = new Fight(R(0), R(1));
            Run(f, 2, n => NO);
            var seq = new[] { new Input { Down = true }, new Input { Down = true, Right = true }, new Input { Right = true }, new Input { Right = true, Lp = true } };
            Run(f, 4, n => seq[n]);
            Run(f, 20, n => NO);
            Assert.IsTrue(f.Shots.Count == 1 || f.P[1].Hp < f.P[1].F.Health, "汐音 qcf+P throws 鯨浪波");
        }

        [Test]
        public void SuperAndKo()
        {
            var f = new Fight(R(2), R(3));
            f.P[0].X = 600; f.P[1].X = 690;
            Run(f, 2, n => NO);
            Run(f, 3, n => new Input { Su = n == 0 });
            Assert.AreNotEqual(State.Attack, f.P[0].State, "super needs a full meter");
            f.P[0].Meter = Roster.METER_MAX;
            Run(f, 2, n => NO);
            Run(f, 80, n => new Input { Su = n == 0 });
            Assert.IsTrue(f.P[0].Meter < Roster.METER_MAX && f.P[1].Hp < f.P[1].F.Health, "super spends the meter and hits");
            f.P[1].Hp = 5;
            f.P[0].X = 600; f.P[1].X = 690;
            Run(f, 60, n => NO);
            Run(f, 30, n => new Input { Lp = n == 0 });
            Assert.IsTrue(f.P[1].Hp == 0 && f.P[1].State == State.Ko, "reaching 0 hp is a KO");
        }

        [Test]
        public void ParryCounters()
        {
            var f = new Fight(R(0), R(7));
            f.P[0].X = 600; f.P[1].X = 700;
            Run(f, 2, n => NO);
            double hp1 = f.P[1].Hp;
            Run(f, 40, n => new Input { Hp = n == 6 }, n => new Input { Sp = n == 0 });
            Assert.IsTrue(f.P[1].Hp == hp1 && f.P[0].Hp < f.P[0].F.Health, "水月返 parries and counters");
        }

        [Test]
        public void CpuVsCpuFightEnds()
        {
            // Extra smoke test: two CPUs fight until someone is KO'd or the frame budget ends.
            var f = new Fight(R(4), R(5));
            var a = new Cpu(0, 2, Rand.Seeded(1));
            var b = new Cpu(1, 2, Rand.Seeded(2));
            for (int n = 0; n < 60 * 99 && f.P.All(p => p.Hp > 0); n++) { f.Step(a.Think(f), b.Think(f)); f.Settle(); }
            Assert.IsTrue(f.P.Any(p => p.Hp < p.F.Health), "someone took damage");
        }
    }
}
