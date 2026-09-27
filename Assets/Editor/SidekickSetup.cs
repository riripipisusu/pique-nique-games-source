using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Personnages modulaires Synty Sidekick (pack gratuit) : diagnostic des pieces.
public static class SidekickSetup
{
    const string Parts = "Assets/Synty/SidekickCharacters/Resources/Meshes/";

    // Palettes des 4 personnages d'exemple -> Resources/Palettes (lisibles, sans filtrage) pour le createur.
    public static void Build()
    {
        string dst = "Assets/Synty/SidekickCharacters/Resources/Palettes/";
        Directory.CreateDirectory(dst);
        for (int i = 1; i <= 4; i++)
        {
            string p = dst + "Palette_" + i + ".png";
            File.Copy($"Assets/Synty/SidekickCharacters/Characters/Starter/Starter_0{i}/Textures/T_Starter_0{i}ColorMap.png", p, true);
            AssetDatabase.ImportAsset(p);
            var imp = (TextureImporter)AssetImporter.GetAtPath(p);
            imp.isReadable = true;
            imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.SaveAndReimport();
        }
        Rig();
    }

    // Avatar humanoide du squelette Sidekick complet (celui des personnages d'exemple), porte par Resources/Rig.
    static void Rig()
    {
        string path = "Assets/Synty/SidekickCharacters/Resources/Rig.prefab";
        var go = new GameObject("Rig");
        go.AddComponent<Animator>().avatar = AssetDatabase.LoadAssetAtPath<Avatar>("Assets/Synty/SidekickCharacters/Characters/HumanSpecies/HumanSpecies_01/Meshes/HumanSpecies_01-avatar.asset");
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    // Planche de test : quelques combinaisons montees. -executeMethod SidekickSetup.Sheet -out <png>
    public static void Sheet()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        var looks = new List<Sidekick.Look>();
        for (int i = 0; i < 8; i++)
            looks.Add(new Sidekick.Look
            {
                head = 1 + i % 2, hair = 1 + i, brows = 1 + i, beard = i % 3 == 0 ? 1 + i : 0, nose = 1 + i, ears = 1 + i,
                top = Sidekick.Outfits[i % 3].set, bottom = Sidekick.Outfits[(i + 1) % 3].set, feet = Sidekick.Outfits[i % 3].set,
                hands = Sidekick.Outfits[(i + 2) % 3].set, hat = i == 5 ? "SCFI_CIVL_10" : "", palette = 1 + i % 4, skin = i % 7, hairColor = i * 2 % 14,
                fem = i % 2 == 0 ? -100 : 100, muscle = i == 3 ? 100 : 0, weight = i == 6 ? 100 : i == 7 ? -100 : 0,
            });
        for (int i = 0; i < looks.Count; i++)
        {
            var g = Sidekick.Build(looks[i], null);
            g.transform.SetPositionAndRotation(new Vector3(i * 1.0f, 0, 0), Quaternion.Euler(0, 180, 0));
        }
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(new Vector3(3.5f, 1.0f, -6.5f), Quaternion.identity);
        cam.fieldOfView = 40;
        var rt = new RenderTexture(1800, 700, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1800, 700, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1800, 700), 0, 0);
        var args = System.Environment.GetCommandLineArgs();
        File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1], tex.EncodeToPNG());
    }

    // Perso "Simple" avec chaque palette + liste des maillages montes. -executeMethod SidekickSetup.Simple -out <png>
    public static void Simple()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        for (int i = 0; i < 4; i++)
        {
            var l = new Sidekick.Look { top = "HUMN_BASE_01", bottom = "HUMN_BASE_01", hands = "HUMN_BASE_01", feet = "HUMN_BASE_01", palette = 1 + i, hair = 5 };
            var g = Sidekick.Build(l, null);
            g.transform.SetPositionAndRotation(new Vector3(i * 1.0f, 0, 0), Quaternion.Euler(0, 180, 0));
            if (i == 0) Debug.Log("DIAG maillages : " + string.Join(",", g.GetComponentsInChildren<SkinnedMeshRenderer>().Select(s => s.name + "/" + s.sharedMesh.subMeshCount)));
        }
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(new Vector3(1.5f, 1.2f, -4f), Quaternion.Euler(5, 0, 0));
        cam.fieldOfView = 35;
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        var args = System.Environment.GetCommandLineArgs();
        File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1], tex.EncodeToPNG());
    }

    public static void Sub()
    {
        foreach (var n in new[] { "SK_HUMN_BASE_01_11AUPL_HU01", "SK_HUMN_BASE_01_10TORS_HU01", "SK_HUMN_BASE_01_01HEAD_HU01", "SK_SCFI_CIVL_09_10TORS_HU01" })
        {
            var go = Resources.Load<GameObject>((n.Contains("HUMN") ? "Meshes/Species/Humans/" : "Meshes/Outfits/Starter/") + n);
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            var m = smr.sharedMesh;
            var uv = m.uv; var uv2 = new List<Vector2>(); m.GetUVs(1, uv2);
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var tri = m.GetTriangles(s);
                var cells = new HashSet<string>(); var cells2 = new HashSet<string>();
                foreach (int i in tri) { cells.Add($"{(int)(uv[i].x * 32)},{(int)(uv[i].y * 32)}"); if (uv2.Count > 0) cells2.Add($"{uv2[i].x:0.00},{uv2[i].y:0.00}"); }
                Debug.Log($"DIAG {n} sub{s} tris={tri.Length / 3} mats={string.Join(",", smr.sharedMaterials.Select(x => x ? x.name : "null"))} uv0={string.Join(" ", cells.Take(20))} uv1={string.Join(" ", cells2.Take(8))}");
            }
        }
    }

    public static void Diag()
    {
        var files = Directory.GetFiles(Parts, "*.fbx", SearchOption.AllDirectories).Select(f => f.Replace('\\', '/')).OrderBy(f => f).ToArray();
        var first = AssetDatabase.LoadAssetAtPath<GameObject>(files[0]);
        var imp = (ModelImporter)AssetImporter.GetAtPath(files[0]);
        var smr0 = first.GetComponentInChildren<SkinnedMeshRenderer>();
        Debug.Log($"DIAG piece {first.name} : rig={imp.animationType} root={smr0.rootBone?.name} bones={smr0.bones.Length} mat={smr0.sharedMaterial?.name}/{smr0.sharedMaterial?.shader.name} hier={string.Join(",", first.GetComponentsInChildren<Transform>().Take(8).Select(t => t.name))}");
        // Cases de la palette (32x32) utilisees par type de piece.
        var bySlot = new SortedDictionary<string, HashSet<int>>();
        foreach (var f in files)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(f);
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            if (!smr) { Debug.Log("DIAG sans skin " + go.name); continue; }
            var m = smr.sharedMesh;
            string slot = go.name.Split('_')[4].Substring(2);   // SK_HUMN_BASE_01_02HAIR_HU01 -> HAIR
            if (!bySlot.TryGetValue(slot, out var set)) bySlot[slot] = set = new HashSet<int>();
            foreach (var uv in m.uv) set.Add(Mathf.Clamp((int)(uv.y * 32), 0, 31) * 32 + Mathf.Clamp((int)(uv.x * 32), 0, 31));
            if (m.blendShapeCount > 0 && (slot == "HEAD" || slot == "TORS"))
                Debug.Log($"DIAG blend {go.name} : " + string.Join(",", Enumerable.Range(0, m.blendShapeCount).Select(i => m.GetBlendShapeName(i))));
        }
        foreach (var kv in bySlot) Debug.Log($"DIAG cases {kv.Key} : " + string.Join(" ", kv.Value.OrderBy(x => x).Select(c => $"{c % 32},{c / 32}")));
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/SidekickCharacters/Characters/Starter/Starter_01/Starter_01.prefab");
        Debug.Log($"DIAG prefab : " + string.Join(",", pf.GetComponentsInChildren<Component>(true).Select(c => c.GetType().Name).Distinct()) + " smr=" + string.Join(",", pf.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.name + ":" + s.sharedMesh.blendShapeCount)));
        var an = pf.GetComponentInChildren<Animator>();
        Debug.Log($"DIAG animator : avatar={an?.avatar?.name} human={an?.avatar?.isHuman}");
    }
}
