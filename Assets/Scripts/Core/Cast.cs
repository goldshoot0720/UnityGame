// The eight shared characters (ids are the ones every PhaserGame source uses).
using UnityEngine;

namespace MoeGames
{
    public static class Cast
    {
        public static readonly string[] Ids = { "whale", "penguin", "glasses", "tshirt", "calico", "whitecat", "redcat", "sailor" };
        /// <summary>FBX file names in Assets/Characters, same order as <see cref="Ids"/>.</summary>
        public static readonly string[] Fbx = { "Dpskmusume", "Gugugaga-pose", "fengbro-pose", "Tu-pose", "Miabubu-pose", "Miabyby-pose", "Yamei", "Yumei-pose" };
        public static readonly string[] Names = { "汐音", "小冰", "光哉", "阿翔", "小花", "書白", "緋音", "澪" };
        public static readonly string[] Colors = { "#5ab0ff", "#f2c14e", "#d2b48c", "#9aa0a6", "#f08a3c", "#b57bff", "#e0443e", "#3a5ba0" };

        public static int IndexOf(string id)
        {
            for (int i = 0; i < Ids.Length; i++) if (Ids[i] == id) return i;
            // Game6-style c1..c8 aliases.
            if (id != null && id.Length == 2 && id[0] == 'c' && id[1] >= '1' && id[1] <= '8') return id[1] - '1';
            return -1;
        }

        public static string Name(string id) { int i = IndexOf(id); return i < 0 ? id : Names[i]; }
        public static Color Color(string id) { int i = IndexOf(id); return i < 0 ? UnityEngine.Color.white : Js.Hex(Colors[i]); }
        public static string Canonical(string id) { int i = IndexOf(id); return i < 0 ? id : Ids[i]; }
    }
}
