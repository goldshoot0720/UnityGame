// Writes every game's in-game guide (GameGuide subclasses) to Docs/Guides/GameN.md.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MoeGames.EditorTools
{
    static class GuideExporter
    {
        [MenuItem("MoeGames/Export Game Guides (Docs/Guides)")]
        static void Export()
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Docs", "Guides");
            Directory.CreateDirectory(dir);
            int n = 0;
            for (int i = 1; i <= 12; i++)
            {
                var g = GuideBook.Get(i);
                if (g == null) continue;
                File.WriteAllText(Path.Combine(dir, $"Game{i}.md"), g.ToMarkdown());
                n++;
            }
            Debug.Log($"[MoeGames] exported {n} guides to {dir}");
        }
    }
}
