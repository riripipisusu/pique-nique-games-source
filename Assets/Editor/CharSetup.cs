using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Personnages Synty (Fantasy Characters, City, Farm) animes avec les clips Mixamo (humanoides) :
//   Resources/Chars/<Pack>.prefab   tous les personnages d'un pack (un seul maillage active a l'apparition, cf. Chars.Spawn)
//   Resources/CharMats/<Pack>_<variante>.mat   une variante de couleurs de l'atlas du pack (URP)
//   Resources/CharAnim.controller   etats utilises par le jeu (Idle, SitDown, Victory, PickUp...)
public static class CharSetup
{
    const string Res = "Assets/Resources/";
    public static readonly (string pack, string fbx, string tex)[] Packs =
    {
        ("Fantasy", "Assets/PolygonFantasyCharacters/Models/FixedScaleCharacters/Characters.fbx", "Assets/PolygonFantasyCharacters/Textures/Polygon_Fantasy_Characters_Texture_"),
        ("City", "Assets/PolygonCity/Models/FixedScaleCharacters/Characters.fbx", "Assets/PolygonCity/Textures/PolygonCity_Texture_"),
        ("Farm", "Assets/PolygonFarm/Models/Characters.fbx", "Assets/PolygonFarm/Textures/PolygonFarm_Texture_"),
    };

    // Etat -> fichier Mixamo (Assets/Mixamo), boucle ?
    static readonly (string state, string file, bool loop)[] States =
    {
        ("Idle", "idle", true), ("SitDown", "sit_idle3", true), ("SitTalk", "sit_talk3", true), ("SitLaugh", "sit_laugh", true),
        ("SitClap", "sit_clap", true), ("Wave", "wave", true), ("Clap", "clap", true), ("Victory", "victory", true),
        ("Dance", "dance", true), ("Defeat", "defeat", false), ("Think", "think", true), ("PickUp", "deal", false),
        ("RecieveHit", "disappointed", false),
    };

    public static void Build()
    {
        // 1. Clips Mixamo en humanoide, deplacement "cuit" dans la pose (sur place, hauteur d'origine : assis reste assis).
        foreach (var (_, file, loop) in States)
        {
            string path = "Assets/Mixamo/" + file + ".fbx";
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = file;
                c.loopTime = loop;
                c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
        }

        // 2. Controleur.
        string ctrlPath = Res + "CharAnim.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        var sm = ctrl.layers[0].stateMachine;
        foreach (var (state, file, _) in States)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Mixamo/" + file + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            var st = sm.AddState(state);
            st.motion = clip;
            if (state == "Idle") sm.defaultState = st;
        }

        // 3. Materiaux URP (une par variante d'atlas) et prefab par pack.
        Directory.CreateDirectory(Res + "Chars");
        Directory.CreateDirectory(Res + "CharMats");
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var (pack, fbx, tex) in Packs)
        {
            foreach (var t in Directory.GetFiles(Path.GetDirectoryName(tex), Path.GetFileName(tex) + "*.png"))
            {
                string v = Path.GetFileNameWithoutExtension(t).Substring(Path.GetFileName(tex).Length);   // "01_A"
                var m = new Material(lit);
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(t.Replace('\\', '/')));
                m.SetFloat("_Smoothness", 0.1f);
                string mp = Res + "CharMats/" + pack + "_" + v + ".mat";
                AssetDatabase.DeleteAsset(mp);
                AssetDatabase.CreateAsset(m, mp);
            }
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (!mi.isReadable) { mi.isReadable = true; mi.SaveAndReimport(); }   // decoupe des persos a la carte (Mix)
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            if (!go.GetComponent<Animator>()) go.AddComponent<Animator>().avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().First();
            PrefabUtility.SaveAsPrefabAsset(go, Res + "Chars/" + pack + ".prefab");
            Object.DestroyImmediate(go);
            Debug.Log($"DIAG pack {pack} : " + string.Join(",", AssetDatabase.LoadAssetAtPath<GameObject>(Res + "Chars/" + pack + ".prefab").GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.name)));
        }
        AssetDatabase.SaveAssets();
    }

    // Melanges de test : -executeMethod CharSetup.MixSheet -out <png>
    public static void MixSheet()
    {
        ShaderUtil.allowAsyncCompilation = false;
        foreach (var (_, fbx, _) in Packs) { var mi = (ModelImporter)AssetImporter.GetAtPath(fbx); if (!mi.isReadable) { mi.isReadable = true; mi.SaveAndReimport(); } }
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        var looks = new[]
        {
            new Mix.Look { head = "City/Character_Female_Jacket/1", top = "City/Character_Male_Hoodie/2", bottom = "Farm/SM_Chr_FarmGirl_01/1", feet = "City/Character_Male_Jacket/1", skin = "A" },
            new Mix.Look { head = "Fantasy/Character_Male_Wizard/2", top = "City/Character_BusinessMan_Suit/1", bottom = "City/Character_BusinessMan_Suit/1", feet = "Fantasy/Character_Male_King/1", skin = "B" },
            new Mix.Look { head = "Farm/SM_Chr_Farmer_Male_Old_01/1", top = "City/Character_Female_Police/1", bottom = "Farm/SM_Chr_Farmer_Male_01/3", feet = "Farm/SM_Chr_Farmer_Male_01/3", skin = "C" },
            new Mix.Look { head = "City/Character_Male_Hoodie/3", top = "Fantasy/Character_Male_King/1", bottom = "City/Character_Male_Jacket/2", feet = "City/Character_Male_Hoodie/1", skin = "B" },
            new Mix.Look { head = "Fantasy/Character_Female_Witch/1", top = "City/Character_Female_Jacket/4", bottom = "City/Character_Female_Jacket/2", feet = "Fantasy/Character_Female_Druid/1", skin = "A" },
            new Mix.Look { head = "City/Character_BusinessWoman/2", top = "Farm/SM_Chr_Farmer_Female_01/4", bottom = "City/Character_Male_Hoodie/4", feet = "Farm/SM_Chr_Farmer_Female_01/1", skin = "C" },
        };
        for (int i = 0; i < looks.Length; i++)
        {
            var g = Mix.Build(looks[i], null);
            AssetDatabase.LoadAllAssetsAtPath("Assets/Mixamo/idle.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__")).SampleAnimation(g, 1f);
            g.transform.SetPositionAndRotation(new Vector3(i * 1.1f, 0, 0), Quaternion.Euler(0, i % 2 == 0 ? 180 : 150, 0));
        }
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(new Vector3(2.75f, 1.0f, -5.8f), Quaternion.Euler(3, 0, 0));
        cam.fieldOfView = 36;
        var rt = new RenderTexture(1800, 800, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1800, 800, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1800, 800), 0, 0);
        var args = System.Environment.GetCommandLineArgs();
        File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1], tex.EncodeToPNG());
    }

    public static void Bones()
    {
        foreach (var (pack, _, _) in Packs)
        {
            var smr = AssetDatabase.LoadAssetAtPath<GameObject>(Res + "Chars/" + pack + ".prefab").GetComponentsInChildren<SkinnedMeshRenderer>(true)[0];
            Debug.Log($"DIAG os {pack} ({smr.bones.Length}) : " + string.Join(",", smr.bones.Select(b => b.name)));
        }
    }

    // Compare des poses assises (clips Mixamo) de profil sur un ami. -executeMethod CharSetup.SitShot -out <png>
    public static void SitShot()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        var args0 = System.Environment.GetCommandLineArgs();
        string[] files = System.Array.IndexOf(args0, "-files") >= 0 ? args0[System.Array.IndexOf(args0, "-files") + 1].Split(',') : new[] { "sit_idle", "sit_idle2", "sit_idle3" };
        for (int i = 0; i < files.Length; i++)
        {
            string path = "Assets/Mixamo/" + files[i] + ".fbx";
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (imp.animationType != ModelImporterAnimationType.Human)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.SaveAndReimport();
                var cl = imp.defaultClipAnimations;
                foreach (var c in cl) { c.name = files[i]; c.loopTime = true; c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true; c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true; }
                imp.clipAnimations = cl;
                imp.SaveAndReimport();
            }
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            var t = Chars.Spawn("Ami_Caramel", null, new Vector3(i * 1.4f, 0, 0), 90, out var an);
            clip.SampleAnimation(t.gameObject, clip.length * 0.4f);
        }
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(new Vector3((files.Length - 1) * 0.7f, 0.8f, -5.5f), Quaternion.Euler(4, 0, 0));
        cam.fieldOfView = 32;
        var rt = new RenderTexture(1500, 700, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1500, 700, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1500, 700), 0, 0);
        var args = System.Environment.GetCommandLineArgs();
        File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1], tex.EncodeToPNG());
    }

    // Planche : chaque personnage d'un pack (lignes) dans chaque variante de couleurs (colonnes). -executeMethod CharSetup.Sheet -out <dossier>
    public static void Sheet()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        var args = System.Environment.GetCommandLineArgs();
        string outDir = args[System.Array.IndexOf(args, "-out") + 1];
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.8f, 0.8f, 0.85f);
        foreach (var (pack, _, _) in Packs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Res + "Chars/" + pack + ".prefab");
            var meshes = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.name).ToArray();
            var vars = Directory.GetFiles(Res + "CharMats", pack + "_*.mat").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToArray();
            var all = new System.Collections.Generic.List<GameObject>();
            for (int r = 0; r < meshes.Length; r++)
                for (int c = 0; c < vars.Length; c++)
                {
                    var g = (GameObject)Object.Instantiate(prefab);
                    foreach (var s in g.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        s.gameObject.SetActive(s.name == meshes[r]);
                        s.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Res + "CharMats/" + vars[c] + ".mat");
                    }
                    g.transform.SetPositionAndRotation(new Vector3(c * 1.0f, -r * 2.1f, 0), Quaternion.Euler(0, 180, 0));
                    all.Add(g);
                }
            var cam = Camera.main;
            cam.orthographic = true;
            cam.orthographicSize = meshes.Length * 1.05f;
            cam.transform.SetPositionAndRotation(new Vector3((vars.Length - 1) * 0.5f, -meshes.Length * 1.05f + 2.0f, -10), Quaternion.identity);
            int w = vars.Length * 110, h = meshes.Length * 230;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            File.WriteAllBytes(Path.Combine(outDir, "chars_" + pack + ".png"), tex.EncodeToPNG());
            Debug.Log($"DIAG planche {pack} : lignes {string.Join(",", meshes)} | colonnes {string.Join(",", vars)}");
            foreach (var g in all) Object.DestroyImmediate(g);
        }
    }
}
