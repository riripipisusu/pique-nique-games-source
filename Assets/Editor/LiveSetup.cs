using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Jeu de rythme : Tenna en smoking (Tools/tenna_tux_blender.py -> Assets/Stage/Tenna/TennaTux.fbx, hors depot) en humanoide,
// ses danses Mixamo (Swing pendant la chanson, Silly a la fin), et la mise en place du concert a regler a la souris
// dans Assets/Resources/LiveLayout.prefab (camera et Tenna).
//   Unity -batchmode -executeMethod LiveSetup.Run
public static class LiveSetup
{
    const string Dir = "Assets/Stage/Tenna/";
    const string Fbx = Dir + "TennaTux.fbx", PrefabPath = Dir + "TennaTux.prefab", Ctrl = "Assets/Resources/TennaTuxAnim.controller";
    public const string LayoutPath = "Assets/Resources/LiveLayout.prefab";

    // Etat -> clip (Assets/Mixamo), en boucle ?
    static readonly (string state, string file, bool loop)[] States =
        { ("Swing", "t_swing", true), ("Silly", "t_silly", true), ("Clap", "clap", false), ("Excited", "t_excited", false), ("Point", "t_point", false) };

    // Couleurs principales des rampes "toon" du .blend (converties en sRGB).
    static readonly (string mat, string hex)[] Colors =
        { ("Tux", "1d1c24"), ("Shirt", "f6f3ff"), ("Yellow", "ffd708"), ("TV", "dccaf8"), ("HatStripes", "b9a4f2"), ("Nose", "e6c8e8") };

    [MenuItem("Pique-Nique/Concert : Tenna en smoking + mise en place")]
    public static void Run()
    {
        if (!File.Exists(Fbx)) { Debug.LogWarning("TennaTux.fbx absent (Tools/tenna_tux_blender.py)"); return; }
        // 1. Humanoide, avec la meme correspondance d'os que Tenna normal.
        var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx);
        imp.animationType = ModelImporterAnimationType.Generic;
        imp.humanDescription = new HumanDescription();
        imp.SaveAndReimport();
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.SaveAndReimport();
        var hd = imp.humanDescription;
        hd.human = StageSetup.TennaBones.Select(b => new HumanBone { humanName = b.human, boneName = b.bone, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
        imp.humanDescription = hd;
        imp.SaveAndReimport();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Avatar>().FirstOrDefault();
        Debug.Log($"DIAG avatar TennaTux : valide={avatar?.isValid} humain={avatar?.isHuman}");

        // 2. Danses Mixamo en humanoide.
        foreach (var (_, file, loop) in States)
        {
            string path = "Assets/Mixamo/" + file + ".fbx";
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            if (mi.animationType == ModelImporterAnimationType.Human && mi.clipAnimations.Length > 0) continue;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.SaveAndReimport();
            var cl = mi.defaultClipAnimations;
            foreach (var c in cl) { c.name = file; c.loopTime = loop; c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true; c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true; }
            mi.clipAnimations = cl;
            mi.SaveAndReimport();
        }

        // 3. Controleur : Idle (l'animation d'origine de Tenna normal), danses, reactions.
        AssetDatabase.DeleteAsset(Ctrl);
        var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(Ctrl);
        var sm = ctrl.layers[0].stateMachine;
        var idle = sm.AddState("Idle");
        idle.motion = AssetDatabase.LoadAllAssetsAtPath(Dir + "Tenna.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        sm.defaultState = idle;
        foreach (var (state, file, loop) in States)
        {
            var st = sm.AddState(state);
            st.motion = AssetDatabase.LoadAllAssetsAtPath("Assets/Mixamo/" + file + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            if (loop) continue;
            var back = st.AddTransition(idle);
            back.hasExitTime = true; back.exitTime = 0.92f; back.duration = 0.25f;
        }

        // 4. Materiaux (couleurs a plat, un peu auto-eclaires comme Tenna normal) et visage sur la planche d'expressions.
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx));
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            r.sharedMaterials = r.sharedMaterials.Select(m =>
            {
                string n = m ? m.name.Replace("TennaTux_", "") : "";
                if (n.StartsWith("Face"))
                    return Mat("Face", x =>
                    {
                        var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "tenna_facesheet_bold.png") ?? AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "tenna_facesheet.jpeg");
                        x.SetTexture("_BaseMap", sheet); x.SetColor("_BaseColor", Color.black);
                        x.EnableKeyword("_EMISSION"); x.SetTexture("_EmissionMap", sheet); x.SetColor("_EmissionColor", Color.white * 0.95f);
                        x.SetFloat("_Smoothness", 0);
                    });
                var hex = Colors.FirstOrDefault(c => n.StartsWith(c.mat)).hex ?? "808080";
                return Mat(n, x =>
                {
                    var c = Board.Hex(hex);
                    x.SetColor("_BaseColor", c); x.SetFloat("_Smoothness", 0.25f);
                    x.EnableKeyword("_EMISSION"); x.SetColor("_EmissionColor", c * (hex == "1d1c24" ? 0.05f : 0.2f));
                });
            }).ToArray();
        var an = go.GetComponent<Animator>(); if (!an) an = go.AddComponent<Animator>();
        an.avatar = avatar;
        an.runtimeAnimatorController = ctrl;
        an.applyRootMotion = false;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);

        // 5. Registre : le jeu charge ce Tenna-la pour le concert.
        var reg = AssetDatabase.LoadAssetAtPath<Synty>("Assets/Resources/Synty.asset");
        if (reg) { reg.tennaTux = prefab; EditorUtility.SetDirty(reg); }
        Layout(prefab);
        CaneScale();
        AssetDatabase.SaveAssets();
        Debug.Log("TENNATUX OK");
    }

    static Material Mat(string name, System.Action<Material> init)
    {
        string path = Dir + "TennaTux_" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        init(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    // Mise en place du concert, a regler a la souris : double-clic sur Assets/Resources/LiveLayout.prefab.
    // Le jeu relit la position/rotation (et le champ de vision) de "Camera", et la position/rotation/echelle de "Tenna".
    // La scene y sert d'apercu. Creee une seule fois : les reglages faits a la main ne sont jamais ecrases.
    public static void Layout(GameObject tenna)
    {
        if (File.Exists(LayoutPath)) return;
        var root = new GameObject("LiveLayout");
        var stage = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Rhythm/LiveStage.fbx");
        if (stage)
        {
            var st = (GameObject)PrefabUtility.InstantiatePrefab(stage, root.transform);
            st.name = "Scene (apercu, non utilisee)";
            st.transform.localRotation = Quaternion.Euler(0, 180, 0);
        }
        var cam = new GameObject("Camera").AddComponent<Camera>();
        cam.transform.SetParent(root.transform, false);
        cam.fieldOfView = 50;
        Vector3 from = new Vector3(0, 3.6f, -12.5f), at = new Vector3(0, 3.1f, 0);
        cam.transform.localPosition = from;
        cam.transform.localRotation = Quaternion.LookRotation(at - from);
        if (tenna)
        {
            var t = (GameObject)PrefabUtility.InstantiatePrefab(tenna, root.transform);
            t.name = "Tenna";
            t.transform.localPosition = new Vector3(5.5f, 0.02f, -3.6f);
            t.transform.localRotation = Quaternion.Euler(0, 205, 0);
            // 2.35 m de haut, pieds au sol (maillage pose mesure en coordonnees du monde).
            var smr = t.GetComponentInChildren<SkinnedMeshRenderer>();
            float Measure(out float foot)
            {
                var baked = new Mesh(); smr.BakeMesh(baked, true);
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var v in baked.vertices) { float y = smr.transform.TransformPoint(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
                foot = lo;
                return hi - lo;
            }
            t.transform.localScale *= 2.35f / Mathf.Max(0.001f, Measure(out _));
            Measure(out float footY);
            t.transform.position += Vector3.up * (0.02f - footY);
        }
        PrefabUtility.SaveAsPrefabAsset(root, LayoutPath);
        Object.DestroyImmediate(root);
        Debug.Log("LIVELAYOUT cree");
    }

    // Canne de Tenna a 70 % dans la mise en place (une seule fois : ensuite c'est le reglage fait a la main qui compte).
    public static void CaneScale()
    {
        var root = PrefabUtility.LoadPrefabContents(LayoutPath);
        var cane = root.transform.Find("Tenna")?.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Cane");
        if (cane && Mathf.Approximately(cane.localScale.x, 1)) { cane.localScale = Vector3.one * 0.7f; PrefabUtility.SaveAsPrefabAsset(root, LayoutPath); Debug.Log("CANNE 70 %"); }
        PrefabUtility.UnloadPrefabContents(root);
    }

    // Verif : la meme danse sur Tenna en smoking, Tenna normal et un personnage Synty, a 5 instants.
    //   -executeMethod LiveSetup.DanceShot -clip t_swing -out <png>
    public static void DanceShot()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        var args = System.Environment.GetCommandLineArgs();
        string file = args[System.Array.IndexOf(args, "-clip") + 1];
        var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Mixamo/" + file + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        string[] models = { PrefabPath, Dir + "Tenna.prefab", "Assets/Resources/Chars/City.prefab" };
        const int T = 5, W = 300, H = 420;
        var sheet = new Texture2D(W * T, H * models.Length, TextureFormat.RGB24, false);
        var cam = Camera.main; cam.fieldOfView = 30; cam.backgroundColor = new Color(0.25f, 0.25f, 0.3f); cam.clearFlags = CameraClearFlags.SolidColor;
        var rt = new RenderTexture(W, H, 24);
        cam.targetTexture = rt;
        for (int m = 0; m < models.Length; m++)
            for (int i = 0; i < T; i++)
            {
                var g = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(models[m]));
                if (models[m].Contains("City")) foreach (var r in g.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.gameObject.SetActive(r.name == "Character_Male_Hoodie");
                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(g, clip, clip.length * (0.1f + 0.8f * i / (T - 1)));
                AnimationMode.EndSampling();
                g.transform.rotation = Quaternion.Euler(0, 150, 0);
                var smr = g.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r => r.sharedMesh.vertexCount).First();
                var bk = new Mesh(); smr.BakeMesh(bk, true);
                var b = new Bounds(smr.transform.TransformPoint(bk.vertices[0]), Vector3.zero);
                foreach (var v in bk.vertices) b.Encapsulate(smr.transform.TransformPoint(v));
                cam.transform.position = b.center + new Vector3(0, 0, -b.size.y * 2.2f);
                cam.transform.LookAt(b.center);
                cam.Render();
                RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, W, H), i * W, (models.Length - 1 - m) * H);
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(g);
            }
        File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1], sheet.EncodeToPNG());
    }
}
