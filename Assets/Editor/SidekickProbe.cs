using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Inventaire du pack Sidekick : -executeMethod SidekickProbe.Run -> Logs/sidekick_probe.txt (pieces, blend shapes,
// cases de la grille de couleurs 32x32 utilisees par chaque piece) et Logs/sidekick_bones.txt (squelette du prefab).
public static class SidekickProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        var files = Directory.GetFiles("Assets/Synty/SidekickCharacters/Resources/Meshes", "*.fbx", SearchOption.AllDirectories).OrderBy(f => f).ToArray();
        foreach (var f in files)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(f.Replace('\\', '/'));
            if (!go) continue;
            foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = s.sharedMesh;
                var cells = m.uv.GroupBy(u => Mathf.Clamp(Mathf.FloorToInt(u.x * 32), 0, 31) + 32 * Mathf.Clamp(Mathf.FloorToInt(u.y * 32), 0, 31))
                    .OrderByDescending(g => g.Count()).Select(g => $"{g.Key}:{g.Count()}");
                var bs = Enumerable.Range(0, m.blendShapeCount).Select(i => m.GetBlendShapeName(i).Split('.').Last());
                var b = s.sharedMesh.bounds;
                sb.AppendLine($"{Path.GetFileNameWithoutExtension(f)}|{m.vertexCount}|{string.Join(",", cells)}|{string.Join(",", bs)}|{s.rootBone?.name}|{b.center.y:0.00},{b.size.y:0.00}");
            }
        }
        File.WriteAllText("Logs/sidekick_probe.txt", sb.ToString());
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/SidekickCharacters/Characters/HumanSpecies/HumanSpecies_01/HumanSpecies_01.prefab");
        var bones = new StringBuilder();
        void Walk(Transform t, int d) { bones.AppendLine(new string(' ', d) + t.name); foreach (Transform c in t) Walk(c, d + 1); }
        Walk(pf.transform, 0);
        File.WriteAllText("Logs/sidekick_bones.txt", bones.ToString());
        // Couleurs de la grille pour chaque personnage.
        var tx = new StringBuilder();
        foreach (var t in Directory.GetFiles("Assets/Synty/SidekickCharacters/Characters", "*ColorMap.png", SearchOption.AllDirectories))
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(t.Replace('\\', '/'));
            if (!imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(t.Replace('\\', '/'));
            var px = tex.GetPixels32();
            tx.AppendLine(Path.GetFileNameWithoutExtension(t) + "|" + string.Join(",", px.Select(c => $"{c.r:x2}{c.g:x2}{c.b:x2}")));
        }
        File.WriteAllText("Logs/sidekick_colors.txt", tx.ToString());
    }
}
