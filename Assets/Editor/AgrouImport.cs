using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Maps et accessoires d'Agrou (convertis par Tools/AgrouExtract dans AgrouCache/, hors git) -> Assets/Resources/Agrou/ (hors git).
// Les materiaux Unreal sont refaits en URP d'apres leurs fiches JSON (texture de base, decoupe alpha, emission).
// Unity -batchmode -executeMethod AgrouImport.Run
public static class AgrouImport
{
    const string Cache = "AgrouCache/";
    const string Out = "Assets/Resources/Agrou/";
    public static readonly string[] Maps = { "PlaceDuVillage", "Foret", "Cimetiere", "Valley", "Feerique", "Grotte", "MapIlePirate", "MapNoel", "AsianValley" };

    static Dictionary<string, string> jsons;
    static readonly Dictionary<string, Material> made = new Dictionary<string, Material>();

    public static void Run()
    {
        foreach (var d in new[] { "Maps", "Props", "Tex", "Mats" }) Directory.CreateDirectory(Out + d);
        jsons = new Dictionary<string, string>();
        foreach (var f in Directory.GetFiles(Cache, "*.json", SearchOption.AllDirectories))
        {
            var n = Path.GetFileNameWithoutExtension(f);
            if (!jsons.ContainsKey(n)) jsons[n] = f;
        }
        var models = new List<string>();
        foreach (var m in Maps)
        {
            if (!File.Exists(Cache + m + ".fbx")) { Debug.LogWarning("AgrouImport : map absente " + m); continue; }
            File.Copy(Cache + m + ".fbx", Out + "Maps/" + m + ".fbx", true);
            File.Copy(Cache + m + ".json", Out + "Maps/" + m + ".json", true);
            models.Add(Out + "Maps/" + m + ".fbx");
        }
        if (Directory.Exists(Cache + "propsfbx"))
            foreach (var f in Directory.GetFiles(Cache + "propsfbx", "*.fbx"))
            {
                File.Copy(f, Out + "Props/" + Path.GetFileName(f), true);
                models.Add(Out + "Props/" + Path.GetFileName(f));
            }
        AssetDatabase.Refresh();

        foreach (var path in models)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            imp.importAnimation = false; imp.importCameras = false; imp.importLights = false;
            imp.importBlendShapes = false; imp.animationType = ModelImporterAnimationType.None;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var id in imp.GetExternalObjectMap().Keys.ToList()) imp.RemoveRemap(id);   // relance : on repart des materiaux du FBX
            imp.SaveAndReimport();
            var names = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(x => x.name).Distinct().ToList();
            bool prop = path.Contains("/Props/");
            foreach (var n in names) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), Mat(prop && n == "None" ? "Mat_PolyKnights_01" : n));   // bouclier sans materiau : atlas Knights
            imp.SaveAndReimport();
            Debug.Log($"AgrouImport : {path} ({names.Count} materiaux : {string.Join(", ", names)})");
        }
        // Tapis d'herbe des maps (cartes d'herbe sans materiau a l'export) : la carte d'Agrou, decoupee, teintee en jeu.
        var grass = Tex("Agrou/Content/02_Polygon_Asset/PolygonNature/Textures/T_Grass_Card_02.0");
        if (grass)
        {
            var g = new Material(Resources.Load<Material>("LitCutout")) { name = "Herbe" };
            g.SetTexture("_BaseMap", grass); g.SetFloat("_Cull", 0); g.SetFloat("_Cutoff", 0.4f); g.SetFloat("_Smoothness", 0.05f);
            var path = Out + "Mats/Herbe.mat";
            var old = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (old) { EditorUtility.CopySerialized(g, old); EditorUtility.SetDirty(old); } else AssetDatabase.CreateAsset(g, path);
        }
        AssetDatabase.SaveAssets();
    }

    // Controle : materiaux des maps dont le shader n'est pas URP (magenta en jeu).
    public static void Check()
    {
        foreach (var m in Maps)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Out + "Maps/" + m + ".fbx");
            if (!go) continue;
            var bad = go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(x => !x || !x.shader || !(x.shader.name.StartsWith("Universal") || x.shader.name.Contains("Synty") || x.shader.name.Contains("Polygon")))
                .Select(x => x ? x.name + " [" + (x.shader ? x.shader.name : "?") + "] " + AssetDatabase.GetAssetPath(x) : "null").Distinct();
            Debug.Log("AgrouCheck " + m + " : " + string.Join(" | ", bad));
        }
    }

    public static void Foliage()
    {
        foreach (var m in Maps)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Out + "Maps/" + m + ".fbx");
            if (!go) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("Foliage")))
                Debug.Log("AgrouFol " + m + " " + r.name + " : " + string.Join(" | ", r.sharedMaterials.Select(x => x ? x.name + " tex=" + (x.mainTexture ? x.mainTexture.name : "-") + " clip=" + x.IsKeywordEnabled("_ALPHATEST_ON") + " sh=" + x.shader.name : "null")));
        }
    }

    // Materiau URP d'apres la fiche Unreal. Sans fiche (terrain, "None") : herbe Synty.
    static Material Mat(string name)
    {
        if (made.TryGetValue(name, out var m)) return m;
        string path = Out + "Mats/" + name + ".mat";
        string json = jsons.TryGetValue(name, out var jp) ? File.ReadAllText(jp) : null;
        var tex = new List<(string key, string obj)>();
        if (json != null)
            foreach (Match x in Regex.Matches(json, "\"([^\"]+)\":\\s*\\{\\s*\"ObjectName\":\\s*\"Texture2D'[^']*'\",\\s*\"ObjectPath\":\\s*\"([^\"]+)\""))
                tex.Add((x.Groups[1].Value, x.Groups[2].Value));
        bool masked = json != null && Regex.IsMatch(json, "\"BlendMode\":\\s*1");
        string Pick(params string[] keys) => keys.Select(k => tex.FirstOrDefault(t => t.key == k).obj).FirstOrDefault(o => o != null);
        var diffuse = Pick("PM_Diffuse", "Diffuse", "BaseColor", "Base Color", "Albedo", "Base_Texture", "Texture")
                      ?? tex.Select(t => t.obj).FirstOrDefault(o => !Regex.IsMatch(o, "normal|rough|metal|emiss|mask|noise|blend|_N\\.|_O\\.", RegexOptions.IgnoreCase));
        var emissive = Pick("PM_Emissive");
        // Instance sans texture propre : celle du parent (Tools/AgrouExtract fixmats ecrit <materiau>.diffuse).
        if (tex.Count == 0 && jp != null && File.Exists(Path.ChangeExtension(jp, ".diffuse")))
        {
            var d = File.ReadAllText(Path.ChangeExtension(jp, ".diffuse")).Trim();
            if (!d.StartsWith("Engine/")) diffuse = d;
        }

        var baseMat = Resources.Load<Material>(masked ? "LitCutout" : "Lit");
        m = new Material(baseMat) { name = name };
        m.SetFloat("_Smoothness", 0.1f);
        if (masked) m.SetFloat("_Cull", 0);   // feuillage : les deux faces
        var dt = Tex(diffuse);
        // Decoupe Unreal : opacite dans une texture de masque a part -> fusionnee dans l'alpha.
        string maskObj = tex.Select(t => t.obj).FirstOrDefault(o => Regex.IsMatch(o, "mask|opacity", RegexOptions.IgnoreCase) && !Regex.IsMatch(o, "Black|Trunk"));
        if (maskObj == null && jp != null && File.Exists(Path.ChangeExtension(jp, ".mask"))) maskObj = File.ReadAllText(Path.ChangeExtension(jp, ".mask")).Trim();
        if (masked && diffuse != null && Png(Regex.Replace(diffuse, @"\.\d+$", "") + "_Mask") != null) maskObj = Regex.Replace(diffuse, @"\.\d+$", "") + "_Mask";   // masque du meme nom (saule...)
        bool plant = name.Contains("Plant");   // cartes de plantes : la forme vient du masque de feuilles, pas de l'atlas
        if (plant && jp != null && File.Exists(Path.ChangeExtension(jp, ".mask"))) maskObj = File.ReadAllText(Path.ChangeExtension(jp, ".mask")).Trim();
        if (masked && dt && maskObj != null) dt = WithAlpha(diffuse, maskObj, name, plant) ?? dt;
        bool ground = json == null || Regex.IsMatch(name, "Terrain|Landscape", RegexOptions.IgnoreCase);
        if (dt && !ground) { m.SetTexture("_BaseMap", dt); m.color = Color.white; }
        else if (ground && Synty.I && Synty.I.ground) m = new Material(Synty.I.ground) { name = name };
        else if (Regex.IsMatch(name, "Ocean|River|Water", RegexOptions.IgnoreCase)) { m.color = new Color(0.16f, 0.42f, 0.55f); m.SetFloat("_Smoothness", 0.85f); }
        else if (Regex.IsMatch(name, "Fenetre|Lumiere", RegexOptions.IgnoreCase)) { m.color = new Color(1f, 0.8f, 0.45f); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(1f, 0.7f, 0.35f) * 2); }
        else if (Regex.IsMatch(name, "Antlers|Tusks", RegexOptions.IgnoreCase)) m.color = new Color(0.9f, 0.85f, 0.72f);
        else if (masked || Regex.IsMatch(name, "Invisible|Magic", RegexOptions.IgnoreCase)) { m = new Material(Resources.Load<Material>("LitCutout")) { name = name, color = new Color(1, 1, 1, 0) }; }   // feuillage ou effet sans texture : invisible plutot que des plaques grises
        else m.color = new Color(0.6f, 0.6f, 0.6f);
        var et = Tex(emissive);
        if (et) { m.EnableKeyword("_EMISSION"); m.SetTexture("_EmissionMap", et); m.SetColor("_EmissionColor", Color.white * 1.5f); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        var old = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (old) { EditorUtility.CopySerialized(m, old); old.name = name; EditorUtility.SetDirty(old); m = old; }   // meme GUID : les liens restent valides
        else AssetDatabase.CreateAsset(m, path);
        return made[name] = m;
    }

    static string Png(string obj)
    {
        string rel = Regex.Replace(obj, "\\.\\d+$", "") + ".png";
        return new[] { Cache + rel, Cache + "props/" + rel }.FirstOrDefault(File.Exists);
    }

    static Texture2D WithAlpha(string diffuse, string mask, string name, bool force = false)
    {
        string a = Png(diffuse), b = Png(mask);
        if (a == null || b == null) return null;
        var c = new Texture2D(2, 2); c.LoadImage(File.ReadAllBytes(a));
        var m = new Texture2D(2, 2); m.LoadImage(File.ReadAllBytes(b));
        var px = c.GetPixels();
        if (!force && px.Count(p => p.a < 0.5f) > px.Length * 0.15f) return null;   // la texture se decoupe deja vraiment toute seule   // l'alpha de la texture est deja la decoupe (atlas des plantes)
        for (int y = 0; y < c.height; y++)
            for (int x = 0; x < c.width; x++)
                px[y * c.width + x].a = m.GetPixelBilinear((x + 0.5f) / c.width, (y + 0.5f) / c.height).r;
        c.SetPixels(px); c.Apply();
        string dst = Out + "Tex/" + name + "_alpha.png";
        File.WriteAllBytes(dst, c.EncodeToPNG());
        AssetDatabase.ImportAsset(dst);
        var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
        imp.alphaIsTransparency = true; imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
    }

    static Texture2D Tex(string obj)
    {
        if (obj == null) return null;
        string rel = Regex.Replace(obj, "\\.\\d+$", "") + ".png";
        string src = new[] { Cache + rel, Cache + "props/" + rel }.FirstOrDefault(File.Exists);
        if (src == null) return null;
        string dst = Out + "Tex/" + Path.GetFileName(src);
        if (!File.Exists(dst)) { File.Copy(src, dst); AssetDatabase.ImportAsset(dst); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
    }
}
