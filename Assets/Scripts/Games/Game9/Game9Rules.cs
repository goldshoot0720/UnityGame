// 萌友大富翁 — board economy + AI ported 1:1 from Game9/cloud/src/rules.ts, and the turn state
// machine from scenes/play.ts (roll → hop → land → buy/upgrade/toll/card → end turn) without drawing.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MoeGames.Game9
{
    public enum PerkId { Start, Tax, Land, Build, Luck, Discount, Toll, Rich }

    public class Hero { public string Id, Name, Title, Color, PerkText; public PerkId Perk; }

    public enum Kind { Start, Prop, Chance, Tax, Rest, Jail, Pot }

    public class Tile { public int I; public Kind Kind; public string Name; public int Group, Price, Owner = -1, Level; }

    public class Player { public int Id; public Hero Hero; public string Name; public int Cash, Pos, Skip; public bool Alive = true, Ai; public string Color; }

    public class Charge { public int Paid, Amount; public List<string> Sold = new List<string>(); public bool Bankrupt; }

    public enum CardKind { Cash, Move, ToStart, Collect, Repair, Jail, FreeBuild }

    public class Card { public string Text; public CardKind Kind; public int Amount, Steps, Per; }

    public static class Rules
    {
        public static readonly Hero[] HEROES =
        {
            new Hero { Id = "whale", Name = "汐音", Title = "鯨魚女僕", Color = "#4f8dff", Perk = PerkId.Start, PerkText = "經過起點多領 $100" },
            new Hero { Id = "penguin", Name = "小冰", Title = "企鵝少女", Color = "#9adfff", Perk = PerkId.Tax, PerkText = "繳稅與罰款減半" },
            new Hero { Id = "glasses", Name = "光哉", Title = "眼鏡學長", Color = "#d8b98a", Perk = PerkId.Land, PerkText = "購買土地打 9 折" },
            new Hero { Id = "tshirt", Name = "阿翔", Title = "T恤少年", Color = "#b0b0b0", Perk = PerkId.Build, PerkText = "蓋房子打 8 折" },
            new Hero { Id = "calico", Name = "小花", Title = "夾克三花貓", Color = "#f0a24a", Perk = PerkId.Luck, PerkText = "機會卡的獎金加倍" },
            new Hero { Id = "whitecat", Name = "書白", Title = "圖書館貓", Color = "#f4efe6", Perk = PerkId.Discount, PerkText = "過路費只付 8 成" },
            new Hero { Id = "redcat", Name = "緋音", Title = "紅髮貓耳少女", Color = "#e8413c", Perk = PerkId.Toll, PerkText = "收過路費多收 2 成" },
            new Hero { Id = "sailor", Name = "澪", Title = "水手服少女", Color = "#7fb3e6", Perk = PerkId.Rich, PerkText = "起始資金多 $300" },
        };

        public static readonly string[] PLAYER_COLORS = { "#39c6ff", "#ff6fa8", "#7ee05a", "#ffc83a" };
        public const int START_CASH = 1500, PASS_START = 200, MAX_ROUNDS = 20, MAX_LEVEL = 3, TAX = 150, BOARD_SIZE = 28, REST_TILE = 7;
        public static readonly int[] RENT_MULT = { 1, 4, 9, 16 };

        public static readonly (string name, string color)[] GROUPS =
        {
            ("海灣區", "#35b6e8"), ("櫻花區", "#ff8fbf"), ("學園區", "#9b7bff"), ("山城區", "#52c77a"), ("星河區", "#ffb43a"), ("皇冠區", "#ff5a5a"),
        };

        static readonly string[][] STREETS =
        {
            new[] { "貝殼路", "燈塔街", "珊瑚港" }, new[] { "櫻花道", "花見坂", "和菓子街" }, new[] { "學園路", "書店街", "圖書館" },
            new[] { "松林道", "溫泉鄉", "雲海台" }, new[] { "天文台", "流星街", "銀河站" }, new[] { "城堡路", "王冠廣場", "萌友塔" },
        };

        static readonly Dictionary<int, (Kind kind, string name)> SPECIAL = new Dictionary<int, (Kind, string)>
        {
            [0] = (Kind.Start, "起點"), [3] = (Kind.Chance, "機會"), [7] = (Kind.Rest, "露營區"), [10] = (Kind.Chance, "命運"), [12] = (Kind.Tax, "所得稅"),
            [14] = (Kind.Pot, "幸運池"), [17] = (Kind.Chance, "機會"), [21] = (Kind.Jail, "前往警局"), [24] = (Kind.Chance, "命運"), [26] = (Kind.Tax, "奢侈稅"),
        };

        public static List<Tile> MakeBoard()
        {
            var b = new List<Tile>();
            int n = 0;
            for (int i = 0; i < BOARD_SIZE; i++)
            {
                if (SPECIAL.TryGetValue(i, out var s)) { b.Add(new Tile { I = i, Kind = s.kind, Name = s.name, Group = -1 }); continue; }
                int g = n / 3, k = n % 3;
                n++;
                b.Add(new Tile { I = i, Kind = Kind.Prop, Name = STREETS[g][k], Group = g, Price = 100 + g * 50 + k * 20 });
            }
            return b;
        }

        static int R10(double v) => (int)(Js.Round(v / 10) * 10);
        static int R5(double v) => (int)(Js.Round(v / 5) * 5);

        public static int LandCost(Tile t, PerkId? perk = null) => perk == PerkId.Land ? R10(t.Price * 0.9) : t.Price;
        public static int UpgradeCost(Tile t, PerkId? perk = null) { int c = R10(t.Price * 0.5); return perk == PerkId.Build ? R10(c * 0.8) : c; }
        public static int TileValue(Tile t) => t.Owner < 0 ? 0 : t.Price + t.Level * UpgradeCost(t);
        public static bool OwnsGroup(List<Tile> board, int owner, int group) => board.Where(t => t.Group == group).All(t => t.Owner == owner);
        public static int BaseRent(Tile t) => R5(t.Price * 0.1);

        public static int RentOf(Tile t, List<Tile> board)
        {
            if (t.Kind != Kind.Prop || t.Owner < 0) return 0;
            int r = BaseRent(t) * RENT_MULT[t.Level];
            return t.Level == 0 && OwnsGroup(board, t.Owner, t.Group) ? r * 2 : r;
        }

        /// <summary>You + three random rivals.</summary>
        public static List<Player> CreatePlayers(string heroId, Func<double> rnd = null)
        {
            rnd ??= Rand.Default;
            var me = HEROES.FirstOrDefault(h => h.Id == heroId) ?? HEROES[0];
            var keyed = HEROES.Where(h => h != me).Select(h => (h, k: rnd())).ToList();
            var rivals = Js.Sorted(keyed, (a, b) => a.k.CompareTo(b.k)).Take(3).Select(x => x.h);
            var all = new[] { me }.Concat(rivals).ToList();
            return all.Select((hero, id) => new Player
            {
                Id = id, Hero = hero, Name = hero.Name, Cash = START_CASH + (hero.Perk == PerkId.Rich ? 300 : 0), Ai = id != 0, Color = PLAYER_COLORS[id],
            }).ToList();
        }

        public static int PassBonus(Player p) => PASS_START + (p.Hero.Perk == PerkId.Start ? 100 : 0);

        /// <summary>Move one space; returns true when this hop lands on (passes) the start.</summary>
        public static bool Hop(Player p, int dir)
        {
            p.Pos = (p.Pos + dir + BOARD_SIZE) % BOARD_SIZE;
            if (dir > 0 && p.Pos == 0) { p.Cash += PassBonus(p); return true; }
            return false;
        }

        public static bool Advance(Player p, int steps)
        {
            bool passed = false;
            for (int i = 0; i < steps; i++) passed = Hop(p, 1) || passed;
            return passed;
        }

        public static int Worth(Player p, List<Tile> board) => p.Cash + board.Where(t => t.Owner == p.Id).Sum(TileValue);

        public static bool Buy(Player p, Tile t)
        {
            int cost = LandCost(t, p.Hero.Perk);
            if (t.Kind != Kind.Prop || t.Owner != -1 || p.Cash < cost) return false;
            p.Cash -= cost; t.Owner = p.Id;
            return true;
        }

        public static bool Upgrade(Player p, Tile t)
        {
            int cost = UpgradeCost(t, p.Hero.Perk);
            if (t.Owner != p.Id || t.Level >= MAX_LEVEL || p.Cash < cost) return false;
            p.Cash -= cost; t.Level++;
            return true;
        }

        /// <summary>Take money; sells the cheapest deeds at 60% value if short; bankrupt (deeds released) if still short.</summary>
        public static Charge DoCharge(Player p, int amount, List<Tile> board)
        {
            p.Cash -= amount;
            var sold = new List<string>();
            while (p.Cash < 0)
            {
                var own = Js.Sorted(board.Where(t => t.Owner == p.Id), (a, b) => TileValue(a) - TileValue(b));
                if (own.Count == 0) break;
                var t = own[0];
                p.Cash += (int)Math.Floor(TileValue(t) * 0.6);
                t.Owner = -1; t.Level = 0;
                sold.Add(t.Name);
            }
            int paid = amount;
            if (p.Cash < 0)
            {
                paid = amount + p.Cash;
                p.Cash = 0; p.Alive = false;
                foreach (var t in board) if (t.Owner == p.Id) { t.Owner = -1; t.Level = 0; }
            }
            return new Charge { Paid = paid, Sold = sold, Bankrupt = !p.Alive };
        }

        public static int TollFor(Player payer, Player owner, Tile t, List<Tile> board)
        {
            double amt = RentOf(t, board);
            if (owner.Hero.Perk == PerkId.Toll) amt *= 1.2;
            if (payer.Hero.Perk == PerkId.Discount) amt *= 0.8;
            return R5(amt);
        }

        public static Charge PayRent(Player payer, Player owner, Tile t, List<Tile> board)
        {
            int amount = TollFor(payer, owner, t, board);
            var c = DoCharge(payer, amount, board);
            owner.Cash += c.Paid;
            c.Amount = amount;
            return c;
        }

        public static int TaxFor(Player p) => p.Hero.Perk == PerkId.Tax ? TAX / 2 : TAX;

        public static readonly Card[] CARDS =
        {
            new Card { Text = "商店街抽獎中了頭獎！獲得 $200", Kind = CardKind.Cash, Amount = 200 },
            new Card { Text = "幫學長搬書，收到謝禮 $100", Kind = CardKind.Cash, Amount = 100 },
            new Card { Text = "不小心打翻咖啡，賠償 $80", Kind = CardKind.Cash, Amount = -80 },
            new Card { Text = "帶貓咪看醫生，花了 $120", Kind = CardKind.Cash, Amount = -120 },
            new Card { Text = "搭上順風車，前進 3 格", Kind = CardKind.Move, Steps = 3 },
            new Card { Text = "忘了帶錢包，後退 2 格", Kind = CardKind.Move, Steps = -2 },
            new Card { Text = "傳送魔法！直接回到起點領獎金", Kind = CardKind.ToStart },
            new Card { Text = "今天是你的生日！每位玩家送你 $50", Kind = CardKind.Collect, Amount = 50 },
            new Card { Text = "颱風過境，每棟房子修繕費 $40", Kind = CardKind.Repair, Per = 40 },
            new Card { Text = "闖紅燈被抓到，直接前往警局", Kind = CardKind.Jail },
            new Card { Text = "工程隊大放送：免費幫你加蓋一棟房子", Kind = CardKind.FreeBuild },
        };

        /// <summary>A cash card's value for this player (luck doubles windfalls, tax halves fines).</summary>
        public static int CardCash(int amount, Player p)
        {
            if (amount > 0 && p.Hero.Perk == PerkId.Luck) return amount * 2;
            if (amount < 0 && p.Hero.Perk == PerkId.Tax) return amount / 2;
            return amount;
        }

        // ── AI ──
        public static bool CompletesGroup(List<Tile> board, Player p, Tile t) => board.Where(x => x.Group == t.Group && x != t).All(x => x.Owner == p.Id);

        public static bool AiWantsBuy(Player p, Tile t, List<Tile> board)
        {
            int reserve = CompletesGroup(board, p, t) ? 40 : 180;
            return p.Cash - LandCost(t, p.Hero.Perk) >= reserve;
        }

        public static bool AiWantsUpgrade(Player p, Tile t) => p.Cash - UpgradeCost(t, p.Hero.Perk) >= 260;

        /// <summary>Final standings: survivors first, then by net worth.</summary>
        public static List<Player> Ranking(IEnumerable<Player> ps, List<Tile> board)
            => Js.Sorted(ps, (a, b) => a.Alive != b.Alive ? (b.Alive ? 1 : -1) : Worth(b, board) - Worth(a, board));

        /// <summary>Board grid position (8×8 ring, clockwise from the top-left corner).</summary>
        public static (int gx, int gy) GridOf(int i)
        {
            if (i <= 7) return (i, 0);
            if (i <= 14) return (7, i - 7);
            if (i <= 21) return (21 - i, 7);
            return (0, 28 - i);
        }
    }

    public enum Phase { Await, Rolling, Moving, Decide, Card, End, Over }

    public class Offer { public bool Buy; public Tile Tile; public int Cost; }

    public class Result { public string Id, Name, Color; public int Worth, Props; public bool Alive, You; }

    /// <summary>Turn flow (scenes/play.ts) without drawing. Human actions: Roll, Decide, CardOk, EndTurnNow.</summary>
    public class Game
    {
        public const double HOP = 0.17;
        public readonly List<Tile> Board = Rules.MakeBoard();
        public readonly List<Player> Ps;
        public int Cur, Round = 1;
        public Phase Phase = Phase.Await;
        public double Timer;
        public int[] Dice = { 3, 4 };
        public int Hops, Dir = 1, HopFrom;
        public double HopT;
        public Offer Offer;
        public Card Card;
        public int Pot = 100;
        public readonly List<string> Log = new List<string>();
        public string Reason = "";
        public List<Result> Results = new List<Result>();
        readonly Func<double> rng;

        public event Action<string> Sound;               // dice hop coin pay build card bust
        public event Action<Player, string, string> Float;
        public event Action Shake;

        public Game(string hero, Func<double> rng = null)
        {
            this.rng = rng ?? Rand.Default;
            Ps = Rules.CreatePlayers(hero, this.rng);
            Say($"遊戲開始！你是 {Ps[0].Name}，對手：{string.Join("、", Ps.Skip(1).Select(p => p.Name))}");
            StartTurn();
        }

        public Player P => Ps[Cur];
        void Say(string s) { Log.Add(s); if (Log.Count > 40) Log.RemoveAt(0); }
        public static string Pn(Player p) => p.Ai ? p.Name : $"你（{p.Name}）";

        // ── turn flow ──
        void StartTurn()
        {
            var p = P;
            Offer = null; Card = null;
            if (p.Skip > 0)
            {
                p.Skip--;
                Say($"{Pn(p)}在警局反省，暫停一回合。");
                EndTurn();
                return;
            }
            Phase = Phase.Await;
            Timer = p.Ai ? 0.7 : 0;
        }

        public void Roll()
        {
            if (Phase != Phase.Await) return;
            Phase = Phase.Rolling;
            Timer = 0.75;
            Dice = new[] { 1 + (int)Math.Floor(rng() * 6), 1 + (int)Math.Floor(rng() * 6) };
            Sound?.Invoke("dice");
        }

        void StartMove(int steps)
        {
            Hops = Math.Abs(steps); Dir = steps < 0 ? -1 : 1;
            HopT = 0; HopFrom = P.Pos;
            Phase = Phase.Moving;
        }

        void Land()
        {
            var p = P;
            var t = Board[p.Pos];
            switch (t.Kind)
            {
                case Kind.Prop:
                    if (t.Owner < 0)
                    {
                        int cost = Rules.LandCost(t, p.Hero.Perk);
                        if (p.Cash >= cost) { Offer = new Offer { Buy = true, Tile = t, Cost = cost }; Phase = Phase.Decide; Timer = 0.9; return; }
                        Say($"{Pn(p)}來到 {t.Name}，可惜錢不夠買地。");
                    }
                    else if (t.Owner == p.Id)
                    {
                        int cost = Rules.UpgradeCost(t, p.Hero.Perk);
                        if (t.Level < Rules.MAX_LEVEL && p.Cash >= cost) { Offer = new Offer { Buy = false, Tile = t, Cost = cost }; Phase = Phase.Decide; Timer = 0.9; return; }
                        Say($"{Pn(p)}回到自己的 {t.Name}。");
                    }
                    else
                    {
                        var owner = Ps[t.Owner];
                        var r = Rules.PayRent(p, owner, t, Board);
                        Say($"{Pn(p)}在 {t.Name} 付給 {owner.Name} 過路費 ${r.Paid}{(r.Sold.Count > 0 ? $"（變賣：{string.Join("、", r.Sold)}）" : "")}");
                        Float?.Invoke(p, $"-${r.Paid}", "#ff6b6b");
                        Sound?.Invoke("pay");
                        if (r.Bankrupt) Bust(p);
                    }
                    break;
                case Kind.Chance:
                    Card = Rules.CARDS[(int)Math.Floor(rng() * Rules.CARDS.Length)];
                    Phase = Phase.Card;
                    Timer = p.Ai ? 1.8 : 3.5;
                    Sound?.Invoke("card");
                    return;
                case Kind.Tax:
                    {
                        var r = Rules.DoCharge(p, Rules.TaxFor(p), Board);
                        Pot += r.Paid;
                        Say($"{Pn(p)}繳了 {t.Name} ${r.Paid}，錢進了幸運池。");
                        Float?.Invoke(p, $"-${r.Paid}", "#ff6b6b");
                        Sound?.Invoke("pay");
                        if (r.Bankrupt) Bust(p);
                        break;
                    }
                case Kind.Pot:
                    {
                        int got = Pot;
                        p.Cash += got; Pot = 0;
                        Say($"{Pn(p)}拿走幸運池裡的 ${got}！");
                        Float?.Invoke(p, $"+${got}", "#ffe066");
                        Sound?.Invoke("coin");
                        break;
                    }
                case Kind.Jail: ToJail(p); break;
                case Kind.Rest: Say($"{Pn(p)}在露營區烤棉花糖。"); break;
                case Kind.Start: Say($"{Pn(p)}剛好停在起點。"); break;
            }
            EndTurn();
        }

        void ToJail(Player p)
        {
            p.Pos = Rules.REST_TILE; p.Skip = 1;
            Say($"{Pn(p)}被帶到警局，下回合暫停！");
            Float?.Invoke(p, "暫停一回合", "#ffb0b0");
            Sound?.Invoke("bust");
        }

        public void Decide(bool yes)
        {
            if (Phase != Phase.Decide) return;
            var p = P;
            var o = Offer;
            Offer = null;
            if (o != null && yes)
            {
                if (o.Buy && Rules.Buy(p, o.Tile))
                {
                    Say($"{Pn(p)}以 ${o.Cost} 買下 {o.Tile.Name}！");
                    Float?.Invoke(p, $"-${o.Cost}", "#ffd0a0");
                    Sound?.Invoke("coin");
                }
                else if (!o.Buy && Rules.Upgrade(p, o.Tile))
                {
                    Say($"{Pn(p)}在 {o.Tile.Name} 蓋了{(o.Tile.Level == Rules.MAX_LEVEL ? "飯店" : $"第 {o.Tile.Level} 棟房子")}！");
                    Float?.Invoke(p, $"-${o.Cost}", "#ffd0a0");
                    Sound?.Invoke("build");
                }
            }
            else if (o != null) Say($"{Pn(p)}決定先不{(o.Buy ? "買" : "蓋")}。");
            EndTurn();
        }

        public void CardOk() { if (Phase == Phase.Card) ApplyCard(); }

        void ApplyCard()
        {
            var p = P;
            var c = Card;
            Card = null;
            if (c == null) { EndTurn(); return; }
            Say($"{Pn(p)}抽到卡片：{c.Text}");
            switch (c.Kind)
            {
                case CardKind.Cash:
                    {
                        int amt = Rules.CardCash(c.Amount, p);
                        if (amt >= 0) { p.Cash += amt; Float?.Invoke(p, $"+${amt}", "#ffe066"); Sound?.Invoke("coin"); }
                        else { var r = Rules.DoCharge(p, -amt, Board); Pot += r.Paid; Float?.Invoke(p, $"-${r.Paid}", "#ff6b6b"); if (r.Bankrupt) Bust(p); }
                        break;
                    }
                case CardKind.Move: StartMove(c.Steps); return;
                case CardKind.ToStart:
                    {
                        p.Pos = 0;
                        int b = Rules.PassBonus(p);
                        p.Cash += b;
                        Float?.Invoke(p, $"+${b}", "#ffe066"); Sound?.Invoke("coin");
                        break;
                    }
                case CardKind.Collect:
                    {
                        int got = 0;
                        foreach (var o in Ps)
                            if (o != p && o.Alive) { var r = Rules.DoCharge(o, c.Amount, Board); got += r.Paid; if (r.Bankrupt) Bust(o); }
                        p.Cash += got; Float?.Invoke(p, $"+${got}", "#ffe066"); Sound?.Invoke("coin");
                        break;
                    }
                case CardKind.Repair:
                    {
                        int n = Board.Where(t => t.Owner == p.Id).Sum(t => t.Level);
                        if (n > 0) { var r = Rules.DoCharge(p, n * c.Per, Board); Pot += r.Paid; Float?.Invoke(p, $"-${r.Paid}", "#ff6b6b"); if (r.Bankrupt) Bust(p); }
                        else Say("還好你還沒蓋房子，不用付錢！");
                        break;
                    }
                case CardKind.Jail: ToJail(p); break;
                case CardKind.FreeBuild:
                    {
                        var t = Js.Sorted(Board.Where(x => x.Owner == p.Id && x.Level < Rules.MAX_LEVEL), (a, b) => b.Price - a.Price).FirstOrDefault();
                        if (t != null) { t.Level++; Say($"{t.Name} 升到 {t.Level} 級！"); Sound?.Invoke("build"); }
                        else { p.Cash += 100; Float?.Invoke(p, "+$100", "#ffe066"); Say("沒有可以蓋的地，改領 $100。"); }
                        break;
                    }
            }
            EndTurn();
        }

        void Bust(Player p)
        {
            Say($"{Pn(p)}破產了！所有地產收回。");
            Float?.Invoke(p, "破產！", "#ff4a4a");
            Sound?.Invoke("bust");
            Shake?.Invoke();
        }

        void EndTurn() { Phase = Phase.End; Timer = P.Ai ? 0.7 : 1.3; }

        public void EndTurnNow() { if (Phase == Phase.End) NextTurn(); }

        void NextTurn()
        {
            var alive = Ps.Where(p => p.Alive).ToList();
            if (!Ps[0].Alive) { Finish("你破產了……下次再加油！"); return; }
            if (alive.Count <= 1) { Finish("對手全部破產，你獨佔整個小鎮！"); return; }
            do { Cur = (Cur + 1) % Ps.Count; if (Cur == 0) Round++; } while (!P.Alive);
            if (Round > Rules.MAX_ROUNDS) { Finish($"{Rules.MAX_ROUNDS} 回合結束，以總資產決定名次。"); return; }
            StartTurn();
        }

        void Finish(string reason)
        {
            Phase = Phase.Over; Timer = 2.4;
            Round = Math.Min(Round, Rules.MAX_ROUNDS);
            Reason = reason;
            Results = Rules.Ranking(Ps, Board).Select(p => new Result
            {
                Id = p.Hero.Id, Name = p.Name, Worth = Rules.Worth(p, Board), Props = Board.Count(t => t.Owner == p.Id), Alive = p.Alive, You = !p.Ai, Color = p.Color,
            }).ToList();
            Say(reason);
        }

        /// <summary>Advance timers/AI/hops. Returns true when the post-game delay has elapsed.</summary>
        public bool Update(double dt)
        {
            Timer -= dt;
            var p = P;
            switch (Phase)
            {
                case Phase.Await: if (p.Ai && Timer <= 0) Roll(); break;
                case Phase.Rolling:
                    if (Timer <= 0)
                    {
                        int s = Dice[0] + Dice[1];
                        Say($"{Pn(p)}擲出 {Dice[0]} + {Dice[1]} = {s}");
                        StartMove(s);
                    }
                    break;
                case Phase.Moving:
                    HopT += dt / HOP;
                    if (HopT >= 1)
                    {
                        if (Rules.Hop(p, Dir)) { Float?.Invoke(p, $"起點 +${Rules.PassBonus(p)}", "#ffe066"); Sound?.Invoke("coin"); }
                        else Sound?.Invoke("hop");
                        Hops--; HopT = 0; HopFrom = p.Pos;
                        if (Hops <= 0) Land();
                    }
                    break;
                case Phase.Decide:
                    if (p.Ai && Timer <= 0 && Offer != null) Decide(Offer.Buy ? Rules.AiWantsBuy(p, Offer.Tile, Board) : Rules.AiWantsUpgrade(p, Offer.Tile));
                    break;
                case Phase.Card: if (Timer <= 0) ApplyCard(); break;
                case Phase.End: if (Timer <= 0) NextTurn(); break;
                case Phase.Over: if (Timer <= 0) return true; break;
            }
            return false;
        }
    }
}
