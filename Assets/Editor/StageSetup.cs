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
            var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Tenna/tenna_facesheet_bold.png") ?? AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Tenna/tenna_facesheet.jpeg");   // traits epaissis (lisibles de loin)
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

    // Mise en place du quiz a regler a la souris : Assets/Resources/QuizLayout.prefab (double-clic pour l'ouvrir).
    // Le plateau y sert d'apercu ; le jeu relit la position, la rotation et l'echelle de l'objet "Tenna".
    // Cree une seule fois : les reglages faits a la main ne sont jamais ecrases.
    const string LayoutPath = "Assets/Resources/QuizLayout.prefab";
    public static void Layout(GameObject stage, GameObject tenna)
    {
        if (File.Exists(LayoutPath) || !stage || !tenna) return;
        var root = new GameObject("QuizLayout");
        var st = (GameObject)PrefabUtility.InstantiatePrefab(stage, root.transform);
        st.name = "Plateau (apercu, non utilise)";
        st.transform.localRotation = Quaternion.Euler(0, 180, 0);
        var t = (GameObject)PrefabUtility.InstantiatePrefab(tenna, root.transform);
        t.name = "Tenna";
        t.transform.localPosition = new Vector3(-5.2f, 0.12f, -2.2f);
        t.transform.localRotation = Quaternion.Euler(0, 170, 0);
        // Echelle : Tenna mesure 2.35 m (maillage pose mesure en coordonnees du monde).
        var smr = t.GetComponentInChildren<SkinnedMeshRenderer>();
        var baked = new Mesh(); smr.BakeMesh(baked, true);
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var v in baked.vertices) { float y = smr.transform.TransformPoint(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
        t.transform.localScale *= 2.35f / Mathf.Max(0.001f, hi - lo);
        PrefabUtility.SaveAsPrefabAsset(root, LayoutPath);
        Object.DestroyImmediate(root);
        Debug.Log("QUIZLAYOUT cree");
    }

    // Visage de Tenna : planche epaissie sur le materiau, et case de la planche que montrent les UV de l'ecran.
    public static void TennaFace()
    {
        var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Tenna/tenna_facesheet_bold.png");
        var m = AssetDatabase.LoadAssetAtPath<Material>(Dir + "Tenna/Tenna_Face.mat");
        m.SetTexture("_BaseMap", sheet);
        m.SetTexture("_EmissionMap", sheet);
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "Tenna/Tenna.prefab");
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = r.sharedMesh;
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
            {
                if (!r.sharedMaterials[sm] || !r.sharedMaterials[sm].name.StartsWith("Tenna_Face")) continue;
                var uv = mesh.uv; var tris = mesh.GetTriangles(sm);
                Vector2 lo = Vector2.one * 9, hi = -lo;
                foreach (int i in tris) { lo = Vector2.Min(lo, uv[i]); hi = Vector2.Max(hi, uv[i]); }
                Debug.Log($"DIAG visage {r.name} sub{sm} uv {lo} -> {hi} blend {string.Join(",", Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName))}");
            }
        }
    }

    // Tenna en humanoide (squelette Rigify "DEF-") : il joue alors les clips Mixamo (Assets/Mixamo/t_*.fbx, clap...).
    // Controleur Resources/TennaAnim : Idle (son animation d'origine), Clap, Laugh, Taunt, Point, Excited.
    static readonly (string human, string bone)[] TennaBones =
    {
        ("Hips", "DEF-spine"), ("Spine", "DEF-spine.001"), ("Chest", "DEF-spine.002"), ("UpperChest", "DEF-spine.003"),
        ("Neck", "DEF-spine.004"), ("Head", "DEF-spine.006"),
        ("LeftUpperLeg", "DEF-thigh.L"), ("LeftLowerLeg", "DEF-shin.L"), ("LeftFoot", "DEF-foot.L"), ("LeftToes", "DEF-toe.L"),
        ("RightUpperLeg", "DEF-thigh.R"), ("RightLowerLeg", "DEF-shin.R"), ("RightFoot", "DEF-foot.R"), ("RightToes", "DEF-toe.R"),
        ("LeftShoulder", "DEF-shoulder.L"), ("LeftUpperArm", "DEF-upper_arm.L"), ("LeftLowerArm", "DEF-forearm.L"), ("LeftHand", "DEF-hand.L"),
        ("RightShoulder", "DEF-shoulder.R"), ("RightUpperArm", "DEF-upper_arm.R"), ("RightLowerArm", "DEF-forearm.R"), ("RightHand", "DEF-hand.R"),
        ("Left Thumb Proximal", "DEF-thumb.01.L"), ("Left Thumb Intermediate", "DEF-thumb.02.L"), ("Left Thumb Distal", "DEF-thumb.03.L"),
        ("Left Index Proximal", "DEF-f_index.01.L"), ("Left Index Intermediate", "DEF-f_index.02.L"), ("Left Index Distal", "DEF-f_index.03.L"),
        ("Left Middle Proximal", "DEF-f_middle.01.L"), ("Left Middle Intermediate", "DEF-f_middle.02.L"), ("Left Middle Distal", "DEF-f_middle.03.L"),
        ("Left Ring Proximal", "DEF-f_ring.01.L"), ("Left Ring Intermediate", "DEF-f_ring.02.L"), ("Left Ring Distal", "DEF-f_ring.03.L"),
        ("Left Little Proximal", "DEF-f_pinky.01.L"), ("Left Little Intermediate", "DEF-f_pinky.02.L"), ("Left Little Distal", "DEF-f_pinky.03.L"),
        ("Right Thumb Proximal", "DEF-thumb.01.R"), ("Right Thumb Intermediate", "DEF-thumb.02.R"), ("Right Thumb Distal", "DEF-thumb.03.R"),
        ("Right Index Proximal", "DEF-f_index.01.R"), ("Right Index Intermediate", "DEF-f_index.02.R"), ("Right Index Distal", "DEF-f_index.03.R"),
        ("Right Middle Proximal", "DEF-f_middle.01.R"), ("Right Middle Intermediate", "DEF-f_middle.02.R"), ("Right Middle Distal", "DEF-f_middle.03.R"),
        ("Right Ring Proximal", "DEF-f_ring.01.R"), ("Right Ring Intermediate", "DEF-f_ring.02.R"), ("Right Ring Distal", "DEF-f_ring.03.R"),
        ("Right Little Proximal", "DEF-f_pinky.01.R"), ("Right Little Intermediate", "DEF-f_pinky.02.R"), ("Right Little Distal", "DEF-f_pinky.03.R"),
    };
    static readonly (string state, string file, bool loop)[] TennaStates =
        { ("Clap", "clap", false), ("Laugh", "t_laugh", false), ("Taunt", "t_taunt", false), ("Point", "t_point", false), ("Excited", "t_excited", false) };

    public static void TennaHuman()
    {
        const string fbx = Dir + "Tenna/Tenna.fbx";
        var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
        imp.animationType = ModelImporterAnimationType.Generic;   // repart d'un squelette frais (la hierarchie a change)
        imp.humanDescription = new HumanDescription();
        imp.SaveAndReimport();
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.SaveAndReimport();
        var hd = imp.humanDescription;
        hd.human = TennaBones.Select(b => new HumanBone { humanName = b.human, boneName = b.bone, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
        imp.humanDescription = hd;
        var clips = imp.defaultClipAnimations;
        foreach (var c in clips) { c.loopTime = true; c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true; c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true; }
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
        Debug.Log($"DIAG avatar Tenna : valide={avatar?.isValid} humain={avatar?.isHuman}");

        foreach (var (_, file, loop) in TennaStates)
        {
            string path = "Assets/Mixamo/" + file + ".fbx";
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            if (mi.animationType != ModelImporterAnimationType.Human)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.SaveAndReimport();
                var cl = mi.defaultClipAnimations;
                foreach (var c in cl) { c.name = file; c.loopTime = loop; c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true; c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true; }
                mi.clipAnimations = cl;
                mi.SaveAndReimport();
            }
        }
        const string ctrlPath = "Assets/Resources/TennaAnim.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        var sm = ctrl.layers[0].stateMachine;
        var idle = sm.AddState("Idle");
        idle.motion = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        sm.defaultState = idle;
        foreach (var (state, file, _) in TennaStates)
        {
            var st = sm.AddState(state);
            st.motion = AssetDatabase.LoadAllAssetsAtPath("Assets/Mixamo/" + file + ".fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            var back = st.AddTransition(idle);
            back.hasExitTime = true; back.exitTime = 0.92f; back.duration = 0.25f;
        }
        // Prefab : Animator humanoide a la place de l'ancienne Animation.
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "Tenna/Tenna.prefab"));
        var legacy = go.GetComponent<Animation>();
        if (legacy) Object.DestroyImmediate(legacy);
        var an = go.GetComponent<Animator>(); if (!an) an = go.AddComponent<Animator>();
        an.avatar = avatar;
        an.runtimeAnimatorController = ctrl;
        an.applyRootMotion = false;
        PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
        Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
    }

    // Tenna dans chaque animation (verif) : -executeMethod StageSetup.TennaAnimShot -out <png>
    public static void TennaAnimShot()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        var names = new[] { "Idle" }.Concat(TennaStates.Select(s => s.state)).ToArray();
        var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/Resources/TennaAnim.controller");
        for (int i = 0; i < names.Length; i++)
        {
            var g = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "Tenna/Tenna.prefab"));
            var clip = ctrl.layers[0].stateMachine.states.First(s => s.state.name == names[i]).state.motion as AnimationClip;
            var an = g.GetComponent<Animator>();
            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(g, clip, clip.length * 0.45f);
            AnimationMode.EndSampling();
            g.transform.rotation = Quaternion.Euler(0, 180, 0);
            var smr = g.GetComponentInChildren<SkinnedMeshRenderer>();
            var bk = new Mesh(); smr.BakeMesh(bk, true);
            float lo = 1e9f, hi = -1e9f; foreach (var v in bk.vertices) { float y = smr.transform.TransformPoint(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
            g.transform.localScale *= 2f / (hi - lo);
            g.transform.position = new Vector3(i * 2.2f, -lo * 2f / (hi - lo), 0);
        }
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(new Vector3((names.Length - 1) * 1.1f, 1.3f, -9f), Quaternion.Euler(2, 0, 0));
        cam.fieldOfView = 45;
        var rt = new RenderTexture(1800, 700, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1800, 700, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1800, 700), 0, 0);
        var args = System.Environment.GetCommandLineArgs();
        File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1], tex.EncodeToPNG());
        AnimationMode.StopAnimationMode();
    }

    // Gros plan du visage de Tenna (verif) : -executeMethod StageSetup.FaceShot -out <png>
    public static void FaceShot()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        var g = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "Tenna/Tenna.prefab"));
        foreach (var r in g.GetComponentsInChildren<Renderer>())
            Debug.Log("DIAG rendu " + r.name + " : " + string.Join(",", r.sharedMaterials.Select(m => m ? m.name + "/" + (m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "-") : "null")));
        var smr = g.GetComponentInChildren<SkinnedMeshRenderer>();
        Debug.Log("DIAG os Tenna : " + string.Join(",", smr.bones.Select(x => x.name + "<" + (x.parent ? x.parent.name : "-"))));
        var baked = new Mesh(); smr.BakeMesh(baked, true);
        var b = new Bounds(smr.transform.TransformPoint(baked.vertices[0]), Vector3.zero);
        foreach (var v in baked.vertices) b.Encapsulate(smr.transform.TransformPoint(v));
        var head = new Vector3(b.center.x, b.max.y - b.size.y * 0.13f, b.center.z);
        var cam = Camera.main;
        foreach (var dir in new[] { Vector3.forward, Vector3.back })
        {
            cam.transform.position = head + dir * b.size.y * 0.7f;
            cam.transform.LookAt(head);
            cam.fieldOfView = 30;
            var rt = new RenderTexture(800, 800, 24);
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(800, 800, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 800, 800), 0, 0);
            var args = System.Environment.GetCommandLineArgs();
            File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1].Replace(".png", dir == Vector3.forward ? "_a.png" : "_b.png"), tex.EncodeToPNG());
        }
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
