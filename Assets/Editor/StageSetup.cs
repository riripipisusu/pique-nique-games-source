using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Plateau du quiz importe de "tenna stage.blend" (Blender -> Assets/Stage/TennaStage.fbx, hors depot).
// Le FBX perd les materiaux a noeuds de Blender : on les refait en URP Lit a partir de materials.json
// (couleur, texture, emission) et de couleurs relevees sur le rendu Blender, puis on enregistre un prefab.
public static class StageSetup
{
    const string Dir = "Assets/Stage/";

    // Materiaux dont la couleur vient de noeuds que l'export ne lit pas (releves sur le rendu Blender).
    static readonly Dictionary<string, string> Colors = new Dictionary<string, string>
    {
        ["Material.028"] = "d63f86",   // rideaux
        ["Material.008"] = "f49b4c",   // cadre de l'ecran geant
        ["Material.009"] = "f7a6ec",   // ecran geant (remplace par l'image du quiz)
        ["Material.029"] = "f2e7d0",   // rampe du haut
        ["Wood"] = "6b4428",           // mur du fond
        ["Material.016"] = "f5c49a",   // pupitres : dessus
        ["Material.019"] = "f06a9a",   // pupitres : corps
    };

    // Tenna (rig de ThatAverageJoe, version Sketchfab) : animation d'attente en boucle (Legacy),
    // texture cuite du corps, ecran-visage emissif, expressions en blend shapes (Pog, Smile, Hmmm).
    public static GameObject Tenna()
    {
        const string fbxPath = Dir + "Tenna/Tenna.fbx";
        if (!File.Exists(fbxPath)) return null;
        var imp = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        if (imp.animationType != ModelImporterAnimationType.Legacy)
        {
            imp.animationType = ModelImporterAnimationType.Legacy;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips) { c.wrapMode = WrapMode.Loop; c.loopTime = true; }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath));
        var body = MatAt(Dir + "Tenna/Tenna_Body.mat", m =>
        {
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Tenna/Tenna_Sketchfab_BakedTexture.png"));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.3f);
        });
        var face = MatAt(Dir + "Tenna/Tenna_Face.mat", m =>
        {
            // Planche de visages (tenna_facesheet) : l'ecran du visage affiche la case prevue par les UV du modele.
            var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Tenna/tenna_facesheet.jpeg");
            m.SetTexture("_BaseMap", sheet);
            m.SetColor("_BaseColor", Color.white);
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", sheet);
            m.SetColor("_EmissionColor", Color.white * 0.8f);
        });
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            r.sharedMaterials = r.sharedMaterials.Select(m => m && m.name.StartsWith("Face") ? face : body).ToArray();
        var anim = go.GetComponent<Animation>() ?? go.AddComponent<Animation>();
        var clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
        if (clip) { anim.clip = clip; anim.AddClip(clip, clip.name); anim.playAutomatically = true; anim.wrapMode = WrapMode.Loop; }
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, Dir + "Tenna/Tenna.prefab");
        Object.DestroyImmediate(go);
        Debug.Log("TENNA OK, animation : " + (clip ? clip.name + " " + clip.length + " s" : "aucune"));
        return prefab;
    }

    static Material MatAt(string path, System.Action<Material> init)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        init(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    [System.Serializable] class MatInfo { public float[] @base, emit; public float emitk; public string tex; }

    public static GameObject Run()
    {
        var json = File.ReadAllText(Dir + "materials.json");
        var infos = JsonUtilityDict(json);
        Directory.CreateDirectory(Dir + "Materials");
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "TennaStage.fbx");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        var made = new Dictionary<string, Material>();
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!mats[i]) continue;
                string name = mats[i].name;
                if (!made.TryGetValue(name, out var m)) made[name] = m = Make(name, infos.TryGetValue(name, out var inf) ? inf : new MatInfo());
                mats[i] = m;
            }
            r.sharedMaterials = mats;
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, Dir + "TennaStage.prefab");
        Object.DestroyImmediate(go);
        Debug.Log($"PLATEAU OK : {made.Count} materiaux");
        return prefab;
    }

    static Material Make(string name, MatInfo inf)
    {
        string path = Dir + "Materials/" + name.Replace(".", "_") + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        var col = inf.@base != null ? new Color(inf.@base[0], inf.@base[1], inf.@base[2]) : Color.white;
        if (Colors.TryGetValue(name, out var hex)) ColorUtility.TryParseHtmlString("#" + hex, out col);
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Smoothness", 0.25f);
        string tex = inf.tex;
        if (name == "Wood.001") tex = "Wood055_2K_Color.jpg";   // l'export a garde la carte de rugosite
        Texture2D t = null;
        if (!string.IsNullOrEmpty(tex) && tex != "Untitled")
        {
            t = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Textures/" + Path.GetFileNameWithoutExtension(tex) + ".png");
            if (t) { m.SetTexture("_BaseMap", t); if (inf.@base == null || Colors.ContainsKey(name)) m.SetColor("_BaseColor", Color.white); }
        }
        bool emits = inf.emitk > 0.01f && name != "Wood.001";
        if (emits)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            var e = inf.emit != null ? new Color(inf.emit[0], inf.emit[1], inf.emit[2]) : Color.white;
            m.SetColor("_EmissionColor", e * (t ? 0.9f : 1.6f));
            if (t) m.SetTexture("_EmissionMap", t);
        }
        else m.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(m);
        return m;
    }

    // materials.json est un objet {nom: infos} : JsonUtility ne lit pas les dictionnaires, on decoupe a la main.
    static Dictionary<string, MatInfo> JsonUtilityDict(string json)
    {
        var d = new Dictionary<string, MatInfo>();
        var re = new System.Text.RegularExpressions.Regex("\"([^\"]+)\"\\s*:\\s*(\\{[^{}]*\\})");
        foreach (System.Text.RegularExpressions.Match mt in re.Matches(json))
            d[mt.Groups[1].Value] = JsonUtility.FromJson<MatInfo>(mt.Groups[2].Value.Replace("\"base\"", "\"base\""));
        return d;
    }
}
