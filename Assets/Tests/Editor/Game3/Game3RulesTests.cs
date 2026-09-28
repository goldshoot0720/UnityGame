// Port of Game3/cloud/src/verify.ts (data, items, standings, time format).
using System;
using System.Linq;
using MoeGames.Game3;
using NUnit.Framework;

namespace MoeGames.Tests
{
    public class Game3RulesTests
    {
        [Test]
        public void RacersAndTracks()
        {
            Assert.IsTrue(Data.RACERS.Length == 8 && Data.RACERS.All(r => Cast.IndexOf(r.Id) >= 0), "eight racers, each a known character");
            Assert.IsTrue(Data.TRACKS.Length == 3 && Data.TRACKS.All(t => t.Pieces.Length >= 8), "three tracks with pieces");
            foreach (var t in Data.TRACKS)
            {
                double len = t.Pieces.Sum(p => p.Length);
                double lapSecs = len / (Data.MAX_SPEED * 0.9);
                Assert.IsTrue(lapSecs > 15 && lapSecs < 45, $"{t.Id}: a lap takes 15–45 s at race pace ({lapSecs:F1} s)");
                Assert.IsTrue(t.Pieces.All(p => p.Length % Data.SEGMENT == 0), $"{t.Id}: every piece is whole segments");
                Assert.Less(Math.Abs(t.Pieces.Sum(p => p.Hill)), 1, $"{t.Id}: hills net to ~zero so the loop closes");
                var road = new Road(t.Pieces);
                Assert.AreEqual(len, road.Length, $"{t.Id}: road length matches the pieces");
            }
            Assert.AreEqual(3, Data.LAPS, "race is 3 laps");
        }

        [Test]
        public void ItemsFavourBackMarkers()
        {
            int Stars(int place) { int n = 0; for (int i = 0; i < 1000; i++) if (Data.RollItem(place, 8, i / 1000.0) == ItemId.Star) n++; return n; }
            Assert.Greater(Stars(7), Stars(0), "back markers get more stars than the leader");
            Assert.IsTrue(new[] { 0, 0.25, 0.5, 0.9999 }.All(r => Data.RollItem(3, 8, r) != ItemId.None), "rollItem always returns an item");
        }

        [Test]
        public void StandingsAndTime()
        {
            var rows = new (double total, double finishT, string n)[] { (100.0, -1.0, "a"), (900.0, -1.0, "b"), (50.0, 40.0, "c"), (60.0, 38.0, "d") };
            var s = Data.Standings(rows, r => r.total, r => r.finishT);
            Assert.AreEqual("dcba", string.Concat(s.Select(x => x.n)), "finishers rank by time, then racers by distance");
            Assert.IsTrue(Data.FmtTime(75.5) == "1:15.50" && Data.FmtTime(9.25) == "0:09.25", "time formatting");
        }

        [Test]
        public void RaceFinishesHeadless()
        {
            // Extra smoke test: holding the gas, the player finishes 3 laps and results are produced.
            var sim = new RaceSim(0, "whale", Rand.Seeded(3));
            bool done = false;
            sim.Done += (order, place, time) => done = order.Count == 8 && time > 0;
            for (int i = 0; i < 60 * 60 * 4 && !done; i++) sim.Update(1 / 60.0, new DriveInput { Gas = true });
            Assert.IsTrue(done, "race completes");
        }
    }
}
