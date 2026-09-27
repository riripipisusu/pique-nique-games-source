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
        ("Idle", "idle", true), ("SitDown", "sit_idle", true), ("SitTalk", "sit_talk", true), ("SitLaugh", "sit_laugh", true),
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
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            if (!go.GetComponent<Animator>()) go.AddComponent<Animator>().avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().First();
            PrefabUtility.SaveAsPrefabAsset(go, Res + "Chars/" + pack + ".prefab");
            Object.DestroyImmediate(go);
            Debug.Log($"DIAG pack {pack} : " + string.Join(",", AssetDatabase.LoadAssetAtPath<GameObject>(Res + "Chars/" + pack + ".prefab").GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.name)));
        }
        AssetDatabase.SaveAssets();
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
