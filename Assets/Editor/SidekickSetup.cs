using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Prepare le pack Sidekick (Modern Civilians) pour le createur de personnages :
//   -executeMethod SidekickSetup.Build
// -> Resources/Sidekick/catalog.json (toutes les pieces, les personnages du pack avec leurs pieces, leur morphologie et
//    leur grille de couleurs 32x32) et Resources/Sidekick/Skeleton.prefab (squelette humanoide commun, sans maillage).
// Le dossier Resources/Sidekick vient du pack : jamais commite (.gitignore).
public static class SidekickSetup
{
    const string Root = "Assets/Synty/SidekickCharacters";
    const string Out = "Assets/Resources/Sidekick";

    [System.Serializable] class Part { public string slot, name, path; }
    [System.Serializable] class Preset { public string name; public string[] parts; public float type, size, muscle; public string colors; }
    [System.Serializable] class Catalog { public Part[] parts; public Preset[] presets; }

    public static void Build()
    {
        Directory.CreateDirectory(Out);
        // Pieces : chemin Resources (sans extension), emplacement = code a 4 lettres du nom (02HAIR -> HAIR).
        var parts = new List<Part>();
        foreach (var f in Directory.GetFiles(Root + "/Resources/Meshes", "*.fbx", SearchOption.AllDirectories).OrderBy(x => x))
        {
            // maillages lisibles en jeu : le createur regarde quelles cases de couleur chaque piece utilise
            if (AssetImporter.GetAtPath(f.Replace('\\', '/')) is ModelImporter mi && !mi.isReadable) { mi.isReadable = true; mi.SaveAndReimport(); }
            var name = Path.GetFileNameWithoutExtension(f);
            var m = Regex.Match(name, @"_\d\d([A-Z]{4})_");
            if (!m.Success) continue;
            var rel = f.Replace('\\', '/').Substring((Root + "/Resources/").Length);
            parts.Add(new Part { slot = m.Groups[1].Value, name = name, path = rel.Substring(0, rel.Length - 4) });
        }
        // Personnages du pack : pieces et morphologie (fichier .sk), couleurs (texture de la grille).
        var presets = new List<Preset>();
        foreach (var sk in Directory.GetFiles(Root + "/Characters", "*.sk", SearchOption.AllDirectories).OrderBy(x => x))
        {
            var lines = File.ReadAllLines(sk);
            float Val(string key) { var l = lines.FirstOrDefault(x => x.Trim().StartsWith(key + ":")); return l == null ? 0 : float.Parse(l.Split(':')[1].Trim(), System.Globalization.CultureInfo.InvariantCulture); }
            var p = new Preset
            {
                name = Path.GetFileNameWithoutExtension(sk),
                parts = lines.Where(x => x.TrimStart().StartsWith("- Name: SK_")).Select(x => x.Split(':')[1].Trim()).ToArray(),
                type = Val("BodyTypeValue"), size = Val("BodySizeValue"), muscle = Val("MuscleValue"),
            };
            var texPath = Directory.GetFiles(Path.GetDirectoryName(sk) + "/Textures", "*ColorMap.png").FirstOrDefault();
            if (texPath != null)
            {
                texPath = texPath.Replace('\\', '/');
                var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
                if (!imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                p.colors = string.Concat(tex.GetPixels32().Select(c => $"{c.r:x2}{c.g:x2}{c.b:x2}"));
            }
            presets.Add(p);
        }
        File.WriteAllText(Out + "/catalog.json", JsonUtility.ToJson(new Catalog { parts = parts.ToArray(), presets = presets.ToArray() }));

        // Squelette : le prefab d'une espece humaine, sans son maillage combine (les pieces viendront s'y greffer).
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Characters/HumanSpecies/HumanSpecies_01/HumanSpecies_01.prefab");
        var go = (GameObject)Object.Instantiate(src);
        go.name = "SidekickSkeleton";
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Object.DestroyImmediate(smr.gameObject);
        var an = go.GetComponent<Animator>();
        an.applyRootMotion = false;
        PrefabUtility.SaveAsPrefabAsset(go, Out + "/Skeleton.prefab");
        Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
        Debug.Log($"SIDEKICK : {parts.Count} pieces, {presets.Count} personnages");
    }
}
