// The race simulation from Game3/cloud/src/scenes/race.ts without drawing: 3 laps vs 7 CPU
// karts, drift mini-turbos, rubber-band AI, items, kart collisions. Track space: z along the
// road (wrapped), x in road half-widths.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MoeGames.Game3
{
    public class Kart
    {
        public Racer R;
        public bool You;
        public double Z, X, Speed, Total;
        public int Lap;
        public double FinishT = -1;
        public double Boost, Spin, Star;
        public double Drift;
        public int DriftDir;
        public ItemId Item;
        public double ItemT;
        public double Skill, Line, HitCd;
    }

    public class Hazard { public double Z, X; }
    public class Shot { public double Z, X, Speed, Life; public Kart Owner; }
    public class Box { public double Z, X, Respawn; }

    public struct DriveInput { public int Steer; public bool Gas, Brake, Drift, ItemPressed; }

    public class RaceResultRow { public string Id; public double Time; public bool You; }

    public class RaceSim
    {
        public readonly Track Tr;
        public readonly Road Road;
        public readonly double Len;
        public readonly List<Kart> Karts = new List<Kart>();
        public readonly Kart Me;
        public readonly List<Hazard> Hazards = new List<Hazard>();
        public readonly List<Shot> Shots = new List<Shot>();
        public readonly List<Box> Boxes = new List<Box>();
        public readonly List<(double z, double x)> Props = new List<(double, double)>();
        public double T;            // race clock (after GO)
        public double Count = 3.6;  // countdown
        public bool Finished;
        public double EndT;
        public string Msg = "";
        public double MsgT;
        readonly Func<double> rng;

        public event Action<string> Sound;          // box, boost, spin, beep, go, bump, lap, finish, star, bubble
        public event Action<float> Shake;
        public event Action<List<RaceResultRow>, int, double> Done; // order, place, my time

        public RaceSim(int track, string racer, Func<double> rng = null)
        {
            this.rng = rng ?? Rand.Default;
            Tr = Data.TRACKS[Math.Max(0, Math.Min(Data.TRACKS.Length - 1, track))];
            Road = new Road(Tr.Pieces);
            Len = Road.Length;
            // Grid: the player starts 5th.
            var order = Data.RACERS.Where(r => r.Id != racer).ToList();
            order.Insert(4, Data.RACERS.FirstOrDefault(r => r.Id == racer) ?? Data.RACERS[0]);
            for (int i = 0; i < order.Count; i++)
            {
                var r = order[i];
                Karts.Add(new Kart
                {
                    R = r, You = r.Id == racer,
                    Z = Len - 600 - i * 520, X = i % 2 == 1 ? 0.45 : -0.45, Total = -600 - i * 520,
                    Skill = 0.86 + (7 - i) * 0.016 + this.rng() * 0.02, Line = (this.rng() - 0.5) * 0.5,
                });
            }
            Me = Karts.First(k => k.You);
            // Item box rows every quarter lap, props along the verge.
            for (int q = 1; q <= 4; q++) foreach (var x in new[] { -0.55, 0, 0.55 }) Boxes.Add(new Box { Z = Len * (q - 0.5) / 4, X = x });
            for (double z = 0; z < Len; z += Data.SEGMENT * 6)
            {
                Props.Add((z, -1.6 - this.rng() * 0.6));
                if ((z / (Data.SEGMENT * 6)) % 2 == 0) Props.Add((z + Data.SEGMENT * 3, 1.6 + this.rng() * 0.6));
            }
        }

        void Say(string s, double t = 1.2) { Msg = s; MsgT = t; }
        public double Wrap(double z) => ((z % Len) + Len) % Len;
        public List<Kart> Standings() => Data.Standings(Karts, k => k.Total, k => k.FinishT);
        public int Place(Kart k) => Standings().IndexOf(k);

        // ── update ──

        public void Update(double dt, DriveInput input)
        {
            dt = Math.Min(dt, 1.0 / 30);
            if (MsgT > 0) MsgT -= dt;
            if (Count > 0)
            {
                double before = Math.Ceiling(Count - 0.6);
                Count -= dt;
                double after = Math.Ceiling(Count - 0.6);
                if (after != before) Sound?.Invoke(after <= 0 ? "go" : "beep");
                // Rocket start: holding gas right at GO.
                if (Count <= 0 && input.Gas) { Me.Boost = 1.0; Say("火箭起步！", 0.9); }
                return;
            }
            T += dt;
            PlayerDrive(dt, input);
            foreach (var k in Karts) if (!k.You) AiDrive(k, dt);
            foreach (var k in Karts) Integrate(k, dt);
            Collide();
            UpdateItems(dt);
            if (Finished)
            {
                EndT += dt;
                if (EndT > 3.2 && EndT - dt <= 3.2) FinishRace();
            }
        }

        public double MaxFor(Kart k)
        {
            double m = Data.MAX_SPEED * k.R.Speed;
            if (Math.Abs(k.X) > 1.05) m *= Data.OFFROAD_MAX;
            if (k.Boost > 0 || k.Star > 0) m *= Data.BOOST_MUL;
            return m;
        }

        void PlayerDrive(double dt, DriveInput keys)
        {
            var k = Me;
            if (k.FinishT >= 0) { k.Speed = Js.Lerp(k.Speed, Data.MAX_SPEED * 0.5, dt); return; }
            if (k.Spin > 0) return;
            int steer = keys.Steer;
            double sp = k.Speed / Data.MAX_SPEED;
            double max = MaxFor(k);
            if (keys.Gas || k.Boost > 0) k.Speed += Data.ACCEL * k.R.Accel * (k.Boost > 0 ? 2 : 1) * dt;
            else if (keys.Brake) k.Speed -= Data.BRAKE * dt;
            else k.Speed -= Data.COAST * dt;
            if (k.Speed > max) k.Speed = Math.Max(max, k.Speed - Data.BRAKE * 0.6 * dt);
            k.Speed = Math.Max(0, k.Speed);
            // Drift: hold drift + steer at speed to charge a mini-turbo.
            if (keys.Drift && steer != 0 && sp > 0.5)
            {
                if (k.DriftDir == 0) k.DriftDir = steer;
                k.Drift += dt;
            }
            else if (k.DriftDir != 0)
            {
                if (k.Drift > 1.6) { k.Boost = 1.4; Sound?.Invoke("boost"); Say("超級迦轉加速！", 0.8); }
                else if (k.Drift > 0.8) { k.Boost = 0.8; Sound?.Invoke("boost"); }
                k.Drift = 0; k.DriftDir = 0;
            }
            double grip = Tr.Grip * k.R.Handling;
            bool drifting = k.DriftDir != 0;
            k.X += steer * Data.STEER * grip * (drifting ? 1.3 : 1) * Math.Min(1, sp * 1.4) * dt;
            if (drifting) k.X += k.DriftDir * 0.35 * dt;
            k.X -= Road.CurveAt(k.Z) * sp * sp * Data.CENTRIFUGAL * (drifting ? 0.55 : 1) / grip * dt * 2;
            if (keys.ItemPressed) UseItem(k);
        }

        void AiDrive(Kart k, double dt)
        {
            if (k.Spin > 0) return;
            // Racing line: lean into the upcoming corner, dodge hazards.
            double ahead = Road.CurveAt(k.Z + 2400);
            double tx = Js.Clamp(-ahead * 0.1 + k.Line, -0.75, 0.75);
            foreach (var h in Hazards)
            {
                double dz = Wrap(h.Z - k.Z);
                if (dz < 1800 && Math.Abs(h.X - tx) < 0.3) tx = h.X > 0 ? h.X - 0.45 : h.X + 0.45;
            }
            k.X += Js.Clamp(tx - k.X, -1, 1) * 1.6 * dt;
            // Rubber band around the player.
            double gap = k.Total - Me.Total;
            double band = gap > 4000 ? 0.93 : gap < -4000 ? 1.07 : 1;
            double target = MaxFor(k) * (k.FinishT >= 0 ? 0.6 : k.Skill * band);
            k.Speed += Js.Clamp(target - k.Speed, -Data.BRAKE * dt, Data.ACCEL * k.R.Accel * dt);
            // Items.
            if (k.Item != ItemId.None)
            {
                k.ItemT -= dt;
                if (k.ItemT <= 0)
                {
                    if (k.Item == ItemId.Bubble) { var tgt = NextAhead(k); if (tgt != null && tgt.Total - k.Total < 5000) UseItem(k); }
                    else UseItem(k);
                }
            }
        }

        void Integrate(Kart k, double dt)
        {
            if (k.Spin > 0) { k.Spin -= dt; k.Speed = Math.Max(0, k.Speed - Data.BRAKE * 0.5 * dt); }
            if (k.Boost > 0) k.Boost -= dt;
            if (k.Star > 0) k.Star -= dt;
            if (k.HitCd > 0) k.HitCd -= dt;
            k.X = Js.Clamp(k.X, -2.0, 2.0);
            double dz = k.Speed * dt;
            double prevLap = Math.Floor(k.Total / Len);
            k.Z = Wrap(k.Z + dz);
            k.Total += dz;
            double lap = Math.Floor(k.Total / Len);
            if (lap > prevLap && lap >= 1)
            {
                k.Lap = (int)lap;
                if (lap >= Data.LAPS && k.FinishT < 0)
                {
                    k.FinishT = T;
                    if (k.You) { Finished = true; Say($"衝線！第 {Place(k) + 1} 名", 3); Sound?.Invoke("finish"); }
                }
                else if (k.You)
                {
                    Say(lap == Data.LAPS - 1 ? "最後一圈！" : $"第 {lap + 1} 圈", 1.3);
                    Sound?.Invoke("lap");
                }
            }
        }

        void Hit(Kart k, string why)
        {
            if (k.Star > 0 || k.Spin > 0) return;
            k.Spin = 1.0;
            k.Speed *= 0.35;
            k.Drift = 0; k.DriftDir = 0;
            if (k.You) { Sound?.Invoke("spin"); Say(why, 1); Shake?.Invoke(0.3f); }
        }

        void Collide()
        {
            var ks = Karts;
            for (int i = 0; i < ks.Count; i++)
                for (int j = i + 1; j < ks.Count; j++)
                {
                    var a = ks[i];
                    var b = ks[j];
                    double dz = b.Total - a.Total;
                    if (Math.Abs(dz) > 260 || Math.Abs(a.X - b.X) > 0.34) continue;
                    var back = dz > 0 ? a : b;
                    var front = dz > 0 ? b : a;
                    if (back.Star > 0 && front.Star <= 0) { Hit(front, "被無敵星撞飛了！"); continue; }
                    if (front.Star > 0 && back.Star <= 0) { Hit(back, "被無敵星撞飛了！"); continue; }
                    back.Speed = Math.Min(back.Speed, front.Speed * 0.92);
                    int push = a.X < b.X ? -1 : 1;
                    a.X += push * 0.04; b.X -= push * 0.04;
                    if ((a.You || b.You) && a.HitCd <= 0 && b.HitCd <= 0) { Sound?.Invoke("bump"); a.HitCd = b.HitCd = 0.4; }
                }
        }

        Kart NextAhead(Kart k)
        {
            Kart best = null;
            double bd = double.PositiveInfinity;
            foreach (var o in Karts)
            {
                double d = o.Total - k.Total;
                if (o != k && d > 0 && d < bd) { bd = d; best = o; }
            }
            return best;
        }

        void UseItem(Kart k)
        {
            var it = k.Item;
            if (it == ItemId.None) return;
            k.Item = ItemId.None;
            switch (it)
            {
                case ItemId.Fish: k.Boost = 1.6; if (k.You) Sound?.Invoke("boost"); break;
                case ItemId.Star: k.Star = 5; if (k.You) { Sound?.Invoke("star"); Say("無敵狀態！", 1); } break;
                case ItemId.Banana: Hazards.Add(new Hazard { Z = Wrap(k.Z - 500), X = k.X }); break;
                case ItemId.Bubble:
                    Shots.Add(new Shot { Z = Wrap(k.Z + 300), X = k.X, Speed = k.Speed + 4500, Owner = k, Life = 4 });
                    if (k.You) Sound?.Invoke("bubble");
                    break;
            }
        }

        void UpdateItems(double dt)
        {
            // Boxes.
            foreach (var b in Boxes)
            {
                if (b.Respawn > 0) { b.Respawn -= dt; continue; }
                foreach (var k in Karts)
                {
                    double dz = Wrap(k.Z - b.Z);
                    if ((dz < 260 || dz > Len - 60) && Math.Abs(k.X - b.X) < 0.3)
                    {
                        b.Respawn = 3;
                        if (k.Item == ItemId.None)
                        {
                            k.Item = Data.RollItem(Place(k), Karts.Count, rng());
                            k.ItemT = 1 + rng() * 3;
                            if (k.You) Sound?.Invoke("box");
                        }
                        break;
                    }
                }
            }
            // Bananas.
            for (int i = Hazards.Count - 1; i >= 0; i--)
            {
                var h = Hazards[i];
                foreach (var k in Karts)
                {
                    double dz = Wrap(k.Z - h.Z);
                    if ((dz < 200 || dz > Len - 60) && Math.Abs(k.X - h.X) < 0.22)
                    {
                        Hazards.RemoveAt(i);
                        Hit(k, "踩到香蕉皮！");
                        break;
                    }
                }
            }
            // Bubble shots home onto the next kart ahead.
            for (int i = Shots.Count - 1; i >= 0; i--)
            {
                var s = Shots[i];
                s.Life -= dt;
                s.Z = Wrap(s.Z + s.Speed * dt);
                Kart hitK = null;
                double bd = double.PositiveInfinity;
                foreach (var k in Karts)
                {
                    if (k == s.Owner) continue;
                    double dz = Wrap(k.Z - s.Z);
                    double fwd = dz < Len / 2 ? dz : dz - Len;
                    if (fwd > -150 && fwd < 3500 && fwd < bd) { bd = fwd; hitK = k; }
                }
                if (hitK != null) s.X += Js.Clamp(hitK.X - s.X, -1, 1) * 3 * dt;
                if (hitK != null && Math.Abs(bd) < 200 && Math.Abs(hitK.X - s.X) < 0.3) { Hit(hitK, "被泡泡彈打中！"); Shots.RemoveAt(i); continue; }
                if (s.Life <= 0) Shots.RemoveAt(i);
            }
        }

        void FinishRace()
        {
            // Karts still racing are ranked by where they are.
            double Est(Kart k) => k.FinishT >= 0 ? k.FinishT : T + (Data.LAPS * Len - k.Total) / Math.Max(3000, k.Speed);
            var order = Js.Sorted(Karts, (a, b) => Est(a).CompareTo(Est(b)));
            int place = order.IndexOf(Me);
            Done?.Invoke(order.Select(k => new RaceResultRow { Id = k.R.Id, Time = Est(k), You = k.You }).ToList(), place, Me.FinishT);
        }
    }
}
