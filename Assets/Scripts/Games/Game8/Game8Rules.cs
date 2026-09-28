// 萌友卡牌對決 — cards + rules engine + CPU ported 1:1 from Game8/cloud/src (cards.ts, battle.ts).
// Every action mutates state and returns events that the UI replays.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MoeGames.Game8
{
    public enum Effect { None, HealOwnHero, Draw, DamageEnemyHero, DamageAllEnemies, EndTurnBuffOthersAtk }

    public class Card
    {
        public string Id, Name, Text, Color;
        public int Cost, Atk, Hp;
        public bool Taunt, Charge;
        public Effect Battlecry; public int BattlecryAmount;
        public Effect EndOfTurn; public int EndOfTurnAmount;
    }

    public static class Cards
    {
        public static readonly Card[] CARDS =
        {
            new Card { Id = "whale", Name = "鯨歌女僕 汐音", Cost = 4, Atk = 3, Hp = 5, Text = "登場：回復己方英雄 4 點生命", Battlecry = Effect.HealOwnHero, BattlecryAmount = 4, Color = "#4f8dff" },
            new Card { Id = "penguin", Name = "企鵝少女 小冰", Cost = 2, Atk = 1, Hp = 4, Text = "嘲諷", Taunt = true, Color = "#9adfff" },
            new Card { Id = "glasses", Name = "眼鏡學長 光哉", Cost = 3, Atk = 2, Hp = 3, Text = "登場：抽 1 張牌", Battlecry = Effect.Draw, BattlecryAmount = 1, Color = "#d8b98a" },
            new Card { Id = "tshirt", Name = "陽光少年 阿翔", Cost = 1, Atk = 2, Hp = 1, Text = "衝鋒", Charge = true, Color = "#b0b0b0" },
            new Card { Id = "calico", Name = "街頭三花貓 小花", Cost = 5, Atk = 4, Hp = 4, Text = "衝鋒", Charge = true, Color = "#f0a24a" },
            new Card { Id = "whitecat", Name = "書房白貓 書白", Cost = 6, Atk = 4, Hp = 7, Text = "回合結束時：其他友方角色 +1 攻擊", EndOfTurn = Effect.EndTurnBuffOthersAtk, EndOfTurnAmount = 1, Color = "#f4efe6" },
            new Card { Id = "redcat", Name = "赤焰貓耳 緋音", Cost = 3, Atk = 3, Hp = 2, Text = "登場：對敵方英雄造成 2 點傷害", Battlecry = Effect.DamageEnemyHero, BattlecryAmount = 2, Color = "#e8413c" },
            new Card { Id = "sailor", Name = "水手服少女 澪", Cost = 7, Atk = 6, Hp = 6, Text = "登場：對所有敵方角色造成 1 點傷害", Battlecry = Effect.DamageAllEnemies, BattlecryAmount = 1, Color = "#7fb3e6" },
        };

        static readonly Dictionary<string, Card> byId = CARDS.ToDictionary(c => c.Id);
        public static Card Get(string id) => byId.TryGetValue(id, out var c) ? c : throw new ArgumentException($"Unknown card {id}");
    }

    public static class RULES
    {
        public const int HERO_HP = 30, MAX_MANA = 10, HAND_MAX = 8, BOARD_MAX = 5, COPIES_PER_CARD = 2, START_HAND_FIRST = 3, START_HAND_SECOND = 4, START_MANA = 3;
    }

    public class HandCard { public string Uid, CardId; }

    public class Minion { public string Uid, CardId; public int Atk, Hp, MaxHp; public bool Taunt, Charge, Sleeping, Attacked; }

    public class PlayerState
    {
        public int Id, Hp, MaxHp, Mana, MaxMana, Fatigue;
        public List<string> Deck;
        public List<HandCard> Hand = new List<HandCard>();
        public List<Minion> Board = new List<Minion>();
    }

    /// <summary>An event for the UI (type + whichever fields apply).</summary>
    public class Ev
    {
        public string Type;
        public int Player = -1, Amount, Hp, Atk, Turn, Owner = -1;
        public string Uid, CardId, Target, Source, Attacker;
        public int? Winner; // -1 = draw
        public override string ToString() => $"{Type}:{Player}:{Uid ?? Target ?? Source}";
    }

    public class Battle
    {
        public int NextUid = 1;
        public int Turn;
        public int Current;
        /// <summary>null = ongoing, 0/1 = winner, -1 = draw.</summary>
        public int? Winner;
        public readonly PlayerState[] Players;
        public readonly Func<double> Rng;
        public readonly int FirstPlayer;

        public static string HeroId(int p) => "H" + p;

        public Battle(Func<double> rng = null, int firstPlayer = 0)
        {
            Rng = rng ?? Rand.Default;
            FirstPlayer = firstPlayer;
            Current = firstPlayer;
            Players = new[] { 0, 1 }.Select(id => new PlayerState { Id = id, Hp = RULES.HERO_HP, MaxHp = RULES.HERO_HP, Mana = 0, MaxMana = RULES.START_MANA - 1, Deck = BuildDeck() }).ToArray();
        }

        public bool IsOver => Winner.HasValue;

        public List<string> BuildDeck()
        {
            var deck = Cards.CARDS.SelectMany(c => Enumerable.Repeat(c.Id, RULES.COPIES_PER_CARD)).ToList();
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = (int)Math.Floor(Rng() * (i + 1));
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
            return deck;
        }

        public int OpponentOf(int p) => 1 - p;

        public class Character { public int Owner; public bool IsHero; public Minion Minion; public PlayerState Hero; public int Hp { get => IsHero ? Hero.Hp : Minion.Hp; set { if (IsHero) Hero.Hp = value; else Minion.Hp = value; } } public int MaxHp => IsHero ? Hero.MaxHp : Minion.MaxHp; }

        public Character FindCharacter(string id)
        {
            if (id == "H0" || id == "H1") { int owner = id[1] - '0'; return new Character { Owner = owner, IsHero = true, Hero = Players[owner] }; }
            foreach (var p in Players)
            {
                var m = p.Board.FirstOrDefault(x => x.Uid == id);
                if (m != null) return new Character { Owner = p.Id, IsHero = false, Minion = m };
            }
            throw new ArgumentException($"No character {id}");
        }

        public bool CanPlay(int player, string uid)
        {
            if (IsOver || player != Current) return false;
            var p = Players[player];
            var hc = p.Hand.FirstOrDefault(c => c.Uid == uid);
            return hc != null && Cards.Get(hc.CardId).Cost <= p.Mana && p.Board.Count < RULES.BOARD_MAX;
        }

        public List<HandCard> PlayableCards(int player) => Players[player].Hand.Where(c => CanPlay(player, c.Uid)).ToList();

        public bool CanAttack(int player, string uid)
        {
            if (IsOver || player != Current) return false;
            var m = Players[player].Board.FirstOrDefault(x => x.Uid == uid);
            return m != null && !m.Sleeping && !m.Attacked && m.Atk > 0;
        }

        public List<Minion> ReadyAttackers(int player) => Players[player].Board.Where(m => CanAttack(player, m.Uid)).ToList();

        public List<string> ValidTargets(int player, string uid)
        {
            if (!CanAttack(player, uid)) return new List<string>();
            var enemy = Players[OpponentOf(player)];
            var taunts = enemy.Board.Where(m => m.Taunt).ToList();
            if (taunts.Count > 0) return taunts.Select(m => m.Uid).ToList();
            return enemy.Board.Count > 0 ? enemy.Board.Select(m => m.Uid).ToList() : new List<string> { HeroId(enemy.Id) };
        }

        public bool HasAnyAction(int player) => PlayableCards(player).Count > 0 || ReadyAttackers(player).Count > 0;

        public List<Ev> Start()
        {
            var ev = new List<Ev>();
            int second = OpponentOf(FirstPlayer);
            for (int i = 0; i < RULES.START_HAND_FIRST; i++) Draw(FirstPlayer, ev);
            for (int i = 0; i < RULES.START_HAND_SECOND; i++) Draw(second, ev);
            EnsureOpeningCard(FirstPlayer);
            EnsureOpeningCard(second);
            StartTurn(FirstPlayer, ev);
            return ev;
        }

        void EnsureOpeningCard(int player)
        {
            var p = Players[player];
            if (p.Hand.Any(c => Cards.Get(c.CardId).Cost <= RULES.START_MANA)) return;
            int i = p.Deck.FindIndex(id => Cards.Get(id).Cost <= RULES.START_MANA);
            if (i < 0) throw new InvalidOperationException("Deck has no affordable opening card");
            string old = p.Hand[0].CardId;
            p.Hand[0].CardId = p.Deck[i];
            p.Deck[i] = old;
        }

        public List<Ev> PlayCard(int player, string uid)
        {
            if (!CanPlay(player, uid)) throw new InvalidOperationException("Illegal play");
            var ev = new List<Ev>();
            var p = Players[player];
            int idx = p.Hand.FindIndex(c => c.Uid == uid);
            var hc = p.Hand[idx];
            p.Hand.RemoveAt(idx);
            var card = Cards.Get(hc.CardId);
            p.Mana -= card.Cost;
            var m = new Minion { Uid = hc.Uid, CardId = card.Id, Atk = card.Atk, Hp = card.Hp, MaxHp = card.Hp, Taunt = card.Taunt, Charge = card.Charge, Sleeping = !card.Charge, Attacked = false };
            p.Board.Add(m);
            ev.Add(new Ev { Type = "play", Player = player, Uid = uid, CardId = card.Id });
            if (card.Battlecry != Effect.None) DoBattlecry(player, m, card.Battlecry, card.BattlecryAmount, ev);
            ResolveDeaths(ev);
            return ev;
        }

        public List<Ev> Attack(int player, string attackerUid, string targetId)
        {
            if (!ValidTargets(player, attackerUid).Contains(targetId)) throw new InvalidOperationException("Illegal attack");
            var ev = new List<Ev>();
            var a = FindCharacter(attackerUid).Minion;
            var t = FindCharacter(targetId);
            a.Attacked = true;
            ev.Add(new Ev { Type = "attack", Attacker = attackerUid, Target = targetId });
            int counter = t.IsHero ? 0 : t.Minion.Atk;
            Damage(targetId, a.Atk, ev);
            if (counter > 0) Damage(attackerUid, counter, ev);
            ResolveDeaths(ev);
            return ev;
        }

        public List<Ev> EndTurn(int player)
        {
            if (IsOver || player != Current) throw new InvalidOperationException("Not your turn");
            var ev = new List<Ev> { new Ev { Type = "turnEnd", Player = player } };
            var board = Players[player].Board;
            foreach (var s in board)
            {
                var c = Cards.Get(s.CardId);
                if (c.EndOfTurn != Effect.EndTurnBuffOthersAtk) continue;
                ev.Add(new Ev { Type = "ability", Source = s.Uid, CardId = s.CardId, Player = player });
                foreach (var m in board)
                    if (m != s) { m.Atk += c.EndOfTurnAmount; ev.Add(new Ev { Type = "buff", Target = m.Uid, Atk = m.Atk }); }
            }
            ResolveDeaths(ev);
            if (!IsOver) StartTurn(OpponentOf(player), ev);
            return ev;
        }

        void StartTurn(int player, List<Ev> ev)
        {
            Current = player;
            Turn++;
            var p = Players[player];
            p.MaxMana = Math.Min(RULES.MAX_MANA, p.MaxMana + 1);
            p.Mana = p.MaxMana;
            foreach (var m in p.Board) { m.Sleeping = false; m.Attacked = false; }
            ev.Add(new Ev { Type = "turnStart", Player = player, Turn = Turn });
            Draw(player, ev);
            ResolveDeaths(ev);
        }

        public void Draw(int player, List<Ev> ev)
        {
            var p = Players[player];
            if (p.Deck.Count == 0)
            {
                p.Fatigue++;
                ev.Add(new Ev { Type = "fatigue", Player = player, Amount = p.Fatigue });
                Damage(HeroId(player), p.Fatigue, ev);
                return;
            }
            string cardId = p.Deck[p.Deck.Count - 1];
            p.Deck.RemoveAt(p.Deck.Count - 1);
            if (p.Hand.Count >= RULES.HAND_MAX) { ev.Add(new Ev { Type = "burn", Player = player, CardId = cardId }); return; }
            p.Hand.Add(new HandCard { Uid = "c" + NextUid++, CardId = cardId });
            ev.Add(new Ev { Type = "draw", Player = player, CardId = cardId });
        }

        public void Damage(string id, int amount, List<Ev> ev)
        {
            var e = FindCharacter(id);
            e.Hp -= amount;
            ev.Add(new Ev { Type = "damage", Target = id, Amount = amount, Hp = e.Hp });
        }

        public void Heal(string id, int amount, List<Ev> ev)
        {
            var e = FindCharacter(id);
            int h = Math.Min(amount, e.MaxHp - e.Hp);
            e.Hp += h;
            ev.Add(new Ev { Type = "heal", Target = id, Amount = h, Hp = e.Hp });
        }

        void DoBattlecry(int player, Minion m, Effect effect, int amount, List<Ev> ev)
        {
            int enemy = OpponentOf(player);
            ev.Add(new Ev { Type = "ability", Source = m.Uid, CardId = m.CardId, Player = player });
            if (effect == Effect.HealOwnHero) Heal(HeroId(player), amount, ev);
            else if (effect == Effect.Draw) for (int i = 0; i < amount; i++) Draw(player, ev);
            else if (effect == Effect.DamageEnemyHero) Damage(HeroId(enemy), amount, ev);
            else if (effect == Effect.DamageAllEnemies)
            {
                foreach (var x in Players[enemy].Board.ToList()) Damage(x.Uid, amount, ev);
                Damage(HeroId(enemy), amount, ev);
            }
        }

        void ResolveDeaths(List<Ev> ev)
        {
            foreach (var p in Players)
            {
                var dead = p.Board.Where(m => m.Hp <= 0).ToList();
                p.Board = p.Board.Where(m => m.Hp > 0).ToList();
                foreach (var m in dead) ev.Add(new Ev { Type = "death", Target = m.Uid, Owner = p.Id });
            }
            if (IsOver) return;
            var lost = Players.Where(p => p.Hp <= 0).Select(p => p.Id).ToList();
            if (lost.Count == 0) return;
            Winner = lost.Count == 2 ? -1 : OpponentOf(lost[0]);
            ev.Add(new Ev { Type = "gameOver", Winner = Winner });
        }
    }

    // ── CPU ──

    public enum ActionType { Play, Attack, End }

    public struct CpuAction { public ActionType Type; public string Uid, Attacker, Target; }

    public static class Cpu
    {
        public static double MinionValue(Minion m)
        {
            double v = m.Atk * 2 + m.Hp;
            if (m.Taunt) v += 2;
            if (Cards.Get(m.CardId).EndOfTurn != Effect.None) v += 4;
            return v;
        }

        static double TradeScore(Minion a, Minion d)
        {
            bool kills = a.Atk >= d.Hp, survives = d.Atk < a.Hp;
            if (!kills) return d.Taunt ? 0 : double.NegativeInfinity;
            if (survives) return MinionValue(d) + 10;
            double gain = MinionValue(d) - MinionValue(a);
            return gain >= 0 ? gain + 2 : double.NegativeInfinity;
        }

        public static CpuAction Choose(Battle b, int player)
        {
            var playable = b.PlayableCards(player);
            if (playable.Count > 0)
            {
                int Val(HandCard c) { var k = Cards.Get(c.CardId); return k.Cost * 10 + (k.Battlecry != Effect.None ? 1 : 0); }
                var best = playable[0];
                foreach (var y in playable.Skip(1)) if (Val(y) > Val(best)) best = y;
                return new CpuAction { Type = ActionType.Play, Uid = best.Uid };
            }
            var attackers = Js.Sorted(b.ReadyAttackers(player), (x, y) => y.Atk - x.Atk);
            string enemyHero = Battle.HeroId(b.OpponentOf(player));
            var enemy = b.Players[b.OpponentOf(player)];
            bool lethal = enemy.Board.Count == 0 && attackers.Sum(m => m.Atk) >= enemy.Hp;
            foreach (var a in attackers)
            {
                var targets = b.ValidTargets(player, a.Uid);
                if (lethal && targets.Contains(enemyHero)) return new CpuAction { Type = ActionType.Attack, Attacker = a.Uid, Target = enemyHero };
                string best = null;
                double bestScore = double.NegativeInfinity;
                foreach (var id in targets)
                {
                    if (id == enemyHero) continue;
                    double s = TradeScore(a, b.FindCharacter(id).Minion);
                    if (s > bestScore) { best = id; bestScore = s; }
                }
                bool mustTaunt = !targets.Contains(enemyHero);
                if (best != null && (bestScore > double.NegativeInfinity || mustTaunt)) return new CpuAction { Type = ActionType.Attack, Attacker = a.Uid, Target = best };
                if (targets.Contains(enemyHero)) return new CpuAction { Type = ActionType.Attack, Attacker = a.Uid, Target = enemyHero };
            }
            return new CpuAction { Type = ActionType.End };
        }

        public static List<Ev> Apply(Battle b, int player, CpuAction a)
        {
            if (a.Type == ActionType.Play) return b.PlayCard(player, a.Uid);
            if (a.Type == ActionType.Attack) return b.Attack(player, a.Attacker, a.Target);
            return b.EndTurn(player);
        }
    }
}
