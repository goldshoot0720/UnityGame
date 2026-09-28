// Port of Game4/cloud/src/verify.ts (weakness loop, fortress rewards, levels, boss-defeat flow,
// citadel checkpoint, E/M tanks, pause, guardian forms). The TS test stubs scene internals on a
// prototype object; here the Stage exposes the same state and override hooks.
using System;
using System.Collections.Generic;
using System.Linq;
using MoeGames.Game4;
using NUnit.Framework;

namespace MoeGames.Tests
{
    public class Game4RulesTests
    {
        [SetUp]
        public void FreshSave()
        {
            Progress.UseStore(new MemoryStore());
            Run.FortressCarry = null;
        }

        [Test]
        public void WeaknessLoop()
        {
            var weak = Data.CHARACTERS.Select(c => c.WeakTo).ToList();
            Assert.IsTrue(new HashSet<string>(weak).Count == 8 && weak.All(w => Data.CHARACTERS.Any(c => c.Key == w)) && Data.CHARACTERS.All(c => c.WeakTo != c.Key), "weakness chart is a permutation of the cast");
            foreach (var hero in Data.CHARACTERS)
            {
                var bosses = Data.BossesFor(hero.Key);
                Assert.IsTrue(bosses.Count == 7 && !bosses.Any(b => b.Key == hero.Key), $"{hero.Key}: 7 bosses, hero excluded");
                var chargeBoss = bosses.FirstOrDefault(b => b.WeakTo == hero.Key);
                Assert.IsTrue(chargeBoss != null && Data.WeaknessFor(chargeBoss.Key, hero.Key) == "charge", $"{hero.Key}: the charge-weak boss exists");
                Assert.AreEqual(4, Data.BossDamage(chargeBoss.Key, hero.Key, "buster", 2), $"{hero.Key}: full charge deals weakness damage to it");
            }
            Assert.IsTrue(Data.BossDamage("whale", "glasses", "penguin", 0) == 4 && Data.BossDamage("whale", "glasses", "redcat", 0) == 1, "weakness weapon deals 4, others 1");
            Assert.IsTrue(Data.BossDamage("whale", "glasses", "buster", 0) == 1 && Data.BossDamage("whale", "glasses", "buster", 1) == 2, "buster damage scales with charge");
        }

        [Test]
        public void FortressRewards()
        {
            Assert.Greater(Data.FORTRESS_REWARDS.heal, Data.PLAYER.maxHp / 2.0, "fortress reward is over half a health bar");
            var r = Data.FortressBossReward(1, 2, 1);
            Assert.IsTrue(r.hp == 1 + Data.FORTRESS_REWARDS.heal && r.lives == 2 && !r.extraLife, "first boss heals and does not grant a life");
            Assert.AreEqual(Data.PLAYER.maxHp, Data.FortressBossReward(Data.PLAYER.maxHp - 1, 2, 2).hp, "healing respects max HP");
            var third = Data.FortressBossReward(4, 2, 3);
            var sixth = Data.FortressBossReward(4, third.lives, 6);
            Assert.IsTrue(third.lives == 3 && sixth.lives == 4 && third.extraLife && sixth.extraLife, "third and sixth bosses grant one life each");
            Assert.IsTrue(!Data.FortressBossReward(4, 2, 4).extraLife && !Data.FortressBossReward(4, 2, 7).extraLife, "fourth and seventh bosses do not grant a life");
        }

        [Test]
        public void LevelsBuild([Values("whale", "penguin", "glasses", "tshirt", "calico", "library", "redcat", "sailor", "final", "citadel")] string key)
        {
            var lv = Levels.BuildLevel(key);
            Assert.IsTrue(lv.Rows == 14 && lv.Tiles.Length == 14, $"{key}: builds 14 rows with a start");
            Assert.GreaterOrEqual(lv.Checkpoints.Count, 2, $"{key}: has checkpoints");
            Assert.IsTrue(Levels.IsSolid(lv.Tiles[lv.Start.row + 1][lv.Start.col]), $"{key}: player starts on solid ground");
            Assert.AreEqual(Levels.T_WALL, lv.Tiles[5][lv.Cols - 1], $"{key}: boss room walled on the right");
            Assert.IsTrue(lv.DoorRows.All(r => !Levels.IsSolid(lv.Tiles[r][lv.RoomCol])), $"{key}: boss door is open");
        }

        [Test]
        public void GuardianForms()
        {
            var ph = Data.FINAL_BOSS_PHASES;
            Assert.IsTrue(ph.Length == 3 && ph.All(p => p.Hp > 0) && new HashSet<string>(ph.Select(p => p.WeakTo)).Count == 3, "guardian has three separate HP bars and distinct weak weapons");
            for (int i = 0; i < 3; i++)
                Assert.IsTrue(Data.FinalBossDamage(i + 1, ph[i].WeakTo, 0) == 4 && Data.FinalBossDamage(i + 1, ph[(i + 1) % 3].WeakTo, 0) == 1, "each guardian form takes weakness damage only from its own weapon");
        }

        static Stage BareStage(string stageKey, string hero)
        {
            var s = new Stage { StageKey = stageKey, HeroDef = Data.GetHero(hero) };
            s.SayOverride = (m, t) => { };
            return s;
        }

        [Test]
        public void ThirdFortressBossRewards()
        {
            var stage = BareStage("final", "sailor");
            var pending = new List<(double delay, Action cb)>();
            int nextBossCalls = 0;
            stage.Boss = new Boss { Hero = Data.GetHero("penguin"), B = new Body { X = 100, Y = 100, W = 40, H = 92 }, Hp = 1, State = "idle" };
            stage.BossQueue = new List<string> { "calico" };
            stage.RushDone = new HashSet<string> { "whale", "glasses" };
            stage.Life = 1; stage.Hp = 4; stage.Lives = 2;
            stage.ScheduleOverride = (d, cb) => pending.Add((d, cb));
            stage.NextBossOverride = () => nextBossCalls++;
            stage.HitBoss(new Shot { Weapon = "buster", Charge = 2 });
            pending.First(p => p.delay == 1.2).cb();
            Assert.IsTrue(stage.Hp == 4 + Data.FORTRESS_REWARDS.heal && stage.Lives == 3 && stage.RushDone.Count == 3 && nextBossCalls == 1 && stage.Boss == null,
                "third fortress boss defeat restores HP and adds one life before the next boss");
        }

        [Test]
        public void SevenBossClearCarriesIntoCitadel()
        {
            var stage = BareStage("final", Data.CHARACTERS[0].Key);
            var pending = new List<(double delay, Action cb)>();
            string destination = "";
            stage.Boss = new Boss { Hero = Data.CHARACTERS[7], B = new Body { W = 40, H = 92 }, Hp = 1, State = "idle", Phase = 0 };
            stage.BossQueue = new List<string>();
            stage.RushDone = new HashSet<string>(Data.CHARACTERS.Skip(1).Take(6).Select(c => c.Key));
            stage.Life = 1; stage.Hp = 9; stage.Lives = 4;
            stage.Ammo = new Dictionary<string, double> { ["buster"] = 28, ["penguin"] = 12 };
            stage.ETanks = 2; stage.MTanks = 1;
            stage.ScheduleOverride = (d, cb) => pending.Add((d, cb));
            stage.Go += name => destination = name;
            stage.HitBoss(new Shot { Weapon = "buster", Charge = 2 });
            pending.First(p => p.delay == 1.2).cb();
            var c = Run.FortressCarry;
            Assert.IsTrue(destination == "play" && Run.Stage == "citadel" && c != null && c.Hp == 26 && c.Lives == 4 && c.Ammo["penguin"] == 12 && c.ETanks == 2 && c.MTanks == 1
                && Progress.CitadelCheckpoint(Data.CHARACTERS[0].Key) == 0, "seven-boss clear enters the new stage with earned HP, lives and ammo");
            Run.FortressCarry = null;
        }

        [Test]
        public void CitadelCheckpoint()
        {
            const string hero = "whale";
            Progress.MarkCitadelPhase(hero, 1);
            Progress.MarkCitadelPhase(hero, 2);
            Assert.IsTrue(StageSelect.Resolve(hero, "final") == "citadel" && Progress.CitadelCheckpoint(hero) == 2, "fortress select resumes at guardian rather than the seven-boss rush");
            var stage = BareStage("citadel", hero);
            stage.Lv = new Level { RoomCol = 20 };
            stage.Life = 1;
            stage.ScheduleOverride = (d, cb) => { };
            stage.EnterRoom();
            Assert.IsTrue(stage.Checkpoint.x == 23 * 32 && stage.Checkpoint.y == 10 * 32, "entering guardian room updates respawn point to the guardian");
            Progress.Reset(hero);
            Assert.AreEqual(-1, Progress.CitadelCheckpoint(hero), "reset removes the guardian checkpoint");
        }

        [Test]
        public void Tanks()
        {
            Assert.IsTrue(Data.E_TANK.citadelGrant == 3 && Data.M_TANK.citadelGrant == 3 && Data.CitadelTankGrant(0, 0).eTanks == 3 && Data.CitadelTankGrant(0, 0).mTanks == 3, "E and M tanks each grant three on citadel entry");
            Assert.IsTrue(Data.CitadelTankGrant(8, 9).eTanks == 9 && Data.CitadelTankGrant(8, 9).mTanks == 9, "citadel tank grants cap both inventories");

            var e = new Stage { Hp = 5, ETanks = 3, Dead = 0, EndT = -1 };
            e.UseTank();
            Assert.IsTrue(e.Hp == Data.PLAYER.maxHp && e.ETanks == 2, "E tank restores full HP and consumes one");
            e.UseTank();
            Assert.AreEqual(2, e.ETanks, "E tank cannot be wasted at full HP");
            e.Hp = 1; e.Dead = 0.1; e.UseTank();
            Assert.IsTrue(e.ETanks == 2 && e.Hp == 1, "E tank cannot be used after death");

            var m = new Stage { Hp = 4, MTanks = 3, Dead = 0, EndT = -1 };
            m.Weapons = new List<string> { "buster", "penguin", "redcat" };
            m.Ammo = new Dictionary<string, double> { ["buster"] = 28, ["penguin"] = 1, ["redcat"] = 12 };
            m.UseMTank();
            Assert.IsTrue(m.Hp == Data.PLAYER.maxHp && m.Ammo["penguin"] == Data.PLAYER.maxAmmo && m.Ammo["redcat"] == Data.PLAYER.maxAmmo && m.MTanks == 2, "M tank restores life and every special weapon");
            m.UseMTank();
            Assert.AreEqual(2, m.MTanks, "M tank cannot be wasted when everything is full");
            m.Hp = 1; m.Dead = 0.1; m.UseMTank();
            Assert.IsTrue(m.MTanks == 2 && m.Hp == 1, "M tank cannot be used after death");
        }

        [Test]
        public void PauseFreezesScheduledEvents()
        {
            var stage = new Stage { Paused = true };
            int calls = 0;
            stage.Schedule(0.5, () => calls++);
            if (!stage.Paused) stage.TickPending(1);
            Assert.IsTrue(calls == 0 && stage.PendingLeft(0) == 0.5, "pause freezes scheduled events");
            stage.Paused = false;
            stage.TickPending(0.5);
            Assert.IsTrue(calls == 1 && stage.PendingCount == 0, "resume advances scheduled events");
        }

        [Test]
        public void GuardianTransforms()
        {
            var stage = BareStage("citadel", "whale");
            stage.Lv = new Level { RoomCol = 10 };
            stage.FinalPhase = 0;
            var seen = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                stage.NextBoss();
                seen.Add($"{stage.Boss.Phase}:{stage.Boss.Hp}:{stage.Boss.Hero.Weapon.Kind.ToString().ToLowerInvariant()}");
            }
            Assert.AreEqual("1:28:ice|2:32:bounce|3:36:fire3", string.Join("|", seen), "final guardian transforms through three distinct attack forms");
        }

        [Test]
        public void StagePlaysHeadless()
        {
            // Extra smoke test: a real stage runs (holding right + shoot) without throwing.
            Run.Hero = "whale"; Run.Stage = "penguin";
            var s = Stage.Create(Rand.Seeded(1));
            for (int i = 0; i < 60 * 20; i++) s.Update(1 / 60.0, new PadInput { Right = true, ShootPressed = i % 10 == 0, JumpPressed = i % 45 == 0, JumpHeld = i % 45 < 20 });
            Assert.Greater(s.P.X, 100, "player advanced");
        }
    }
}
