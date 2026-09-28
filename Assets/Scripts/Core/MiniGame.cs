// Base class + catalogue for the 12 games. Each game lives in its own assembly
// (Assets/Scripts/Games/GameN) and is discovered by reflection through [MoeGame], so Core
// never references a game directly.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoeGames
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class MoeGameAttribute : Attribute
    {
        public int Number { get; }
        public MoeGameAttribute(int number) { Number = number; }
    }

    public sealed class GameInfo
    {
        public int Number;
        public string Title, Genre, Color, Blurb;
        public Type Type;
    }

    public static class Catalog
    {
        /// <summary>Titles/genres from PhaserGame/README.md (shown even before a game's code exists).</summary>
        static readonly GameInfo[] infos =
        {
            new GameInfo { Number = 1, Title = "萌友棒球對決", Genre = "棒球", Color = "#2a6fdb", Blurb = "三局制捕手視角棒球：看準球路揮棒、選球種投球。" },
            new GameInfo { Number = 2, Title = "萌友街頭 3x3", Genre = "3 對 3 籃球", Color = "#e0503a", Blurb = "半場 3 對 3 街頭籃球，先得 21 分獲勝。" },
            new GameInfo { Number = 3, Title = "萌友卡丁車 GP", Genre = "賽車", Color = "#f2c14e", Blurb = "8 車 3 圈道具賽車：漂移集氣、衝刺魚、香蕉皮、無敵星。" },
            new GameInfo { Number = 4, Title = "萌友洛克英雄", Genre = "橫向動作", Color = "#3a5ba0", Blurb = "擊敗七位頭目奪取武器，攻破要塞與三段變形守護者。" },
            new GameInfo { Number = 5, Title = "萌友格鬥王", Genre = "格鬥", Color = "#b0303a", Blurb = "逐格判定格鬥：連段、指令必殺、超必殺，對電腦或雙人。" },
            new GameInfo { Number = 6, Title = "萌友戰棋・八方對決", Genre = "戰棋", Color = "#b57bff", Blurb = "4 對 4 回合制戰棋：地形、射程與反擊。" },
            new GameInfo { Number = 7, Title = "萌友大亂鬥", Genre = "俯視射擊大亂鬥", Color = "#7ee05a", Blurb = "八人俯視射擊混戰：八種武器，先拿 10 殺者獲勝。" },
            new GameInfo { Number = 8, Title = "萌友卡牌對決", Genre = "卡牌對戰", Color = "#d2b48c", Blurb = "法力曲線、嘲諷、衝鋒與登場效果的 1 對 1 卡牌對決。" },
            new GameInfo { Number = 9, Title = "萌友大富翁", Genre = "大富翁", Color = "#39c6ff", Blurb = "四人大富翁：買地蓋房收過路費，20 回合比總資產。" },
            new GameInfo { Number = 10, Title = "萌友瘋狂坦克", Genre = "回合制砲擊", Color = "#9aa0a6", Blurb = "可破壞地形、風向與特殊彈的四坦克回合制砲擊。" },
            new GameInfo { Number = 11, Title = "萌友戰機 2026～2027", Genre = "縱向街機射擊", Color = "#5ab0ff", Blurb = "2026 海洋 → 2027 宇宙的縱向彈幕射擊，八種主砲。" },
            new GameInfo { Number = 12, Title = "萌友水球大作戰", Genre = "水球對戰", Color = "#ff6fa8", Blurb = "四人水球對戰：困住對手、戳破水泡，先贏兩回合。" },
        };

        static bool scanned;

        public static IReadOnlyList<GameInfo> All
        {
            get
            {
                if (!scanned) Scan();
                return infos;
            }
        }

        public static GameInfo Get(int n) => n >= 1 && n <= infos.Length ? All[n - 1] : null;

        static void Scan()
        {
            scanned = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string an = asm.GetName().Name;
                if (!an.StartsWith("MoeGames.Game") && an != "Assembly-CSharp") continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || !typeof(MiniGame).IsAssignableFrom(t)) continue;
                    var attr = (MoeGameAttribute)Attribute.GetCustomAttribute(t, typeof(MoeGameAttribute));
                    if (attr != null && attr.Number >= 1 && attr.Number <= infos.Length) infos[attr.Number - 1].Type = t;
                }
            }
        }
    }

    public abstract class MiniGame : MonoBehaviour
    {
        public GameInfo Info { get; internal set; }
        public int Number => Info?.Number ?? 0;
        /// <summary>"Game1".."Game12" — the Assets/Generated sub-folder this game's assets live in.</summary>
        public string Scope => "Game" + Number;
        protected CamRig Rig => App.I.Rig;
        protected Camera Cam => App.I.Rig.Cam;
        /// <summary>Parent for this game's 3D objects (destroyed with the game).</summary>
        protected Transform World { get; private set; }

        internal void Boot(GameInfo info)
        {
            Info = info;
            Sfx.Scope = Scope;
            World = new GameObject("World").transform;
            World.SetParent(transform, false);
            Begin();
        }

        /// <summary>Touch controls for phones / touch browsers (null = tap only).</summary>
        public virtual TouchLayout Touch => TouchLayout.TapOnly;

        /// <summary>Called once after the component is created.</summary>
        protected abstract void Begin();

        /// <summary>Leave the game and return to the hub.</summary>
        public void ExitToHub() => App.I.ReturnToHub();

        /// <summary>Clear the 3D world (used when switching between a game's own screens).</summary>
        protected void ClearWorld()
        {
            Gui.Swallow();
            Prim.Clear(World);
        }

        protected Chibi SpawnChar(string id, Vector3 pos, float height = 1.6f, Transform parent = null)
            => Chibi.Spawn(id, parent ? parent : World, pos, height, Scope);

        /// <summary>Clear the world and show characters in a row on a round stage facing the camera
        /// (used by title / select / result screens). Returns them in order.</summary>
        /// <summary>Name of an original background image (Assets/PhaserAssets/GameN) shown behind lineups.</summary>
        protected virtual string Backdrop => null;

        protected Chibi[] ShowLineup(IList<string> ids, string floorHex = "#2b3f73", string accentHex = "#ffe066", float spacing = 1.25f, float height = 1.5f)
        {
            ClearWorld();
            Rig.Viewport(new Rect(0, 0, 1, 1));
            if (Backdrop != null)
            {
                float span0 = (ids.Count - 1) * spacing + 2f;
                float dist0 = Mathf.Max(5.5f, span0 * 0.75f);
                Art.Backdrop(World, Backdrop, new Vector3(0, 2.2f, 9f), (dist0 + 9f) * 0.95f);
            }
            float w = Mathf.Max(4f, (ids.Count - 1) * spacing + 3f);
            Prim.Cyl(World, new Vector3(0, -0.1f, 0.6f), w, 0.2f, Js.Hex(floorHex));
            Prim.Cyl(World, new Vector3(0, 0.0f, 0.6f), w - 0.5f, 0.05f, Js.Hex(accentHex));
            Prim.Cyl(World, new Vector3(0, 0.03f, 0.6f), w - 0.8f, 0.05f, Js.Hex(floorHex));
            var list = new Chibi[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                float x = (i - (ids.Count - 1) / 2f) * spacing;
                var c = SpawnChar(ids[i], new Vector3(x, 0.05f, Mathf.Abs(x) * 0.12f), height);
                c.Face(new Vector3(0, 0, -1));
                list[i] = c;
            }
            float span = (ids.Count - 1) * spacing + 2f;
            float dist = Mathf.Max(5.5f, span * 0.75f);
            Rig.Set(new Vector3(0, 1.9f, -dist), new Vector3(0, 1.0f, 0), 42f);
            return list;
        }

        /// <summary>GUI position just under a character's feet (for name cards / buttons).</summary>
        protected Vector2 GuiAt(Vector3 world)
        {
            var p = Gui.WorldToGui(Cam, world);
            return new Vector2(p.x, p.y);
        }
    }
}
