using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

public static class Setup
{
    const string Res = "Assets/Resources/";

    // Configure tout le projet : URP, post-traitement, materiaux, theme UI, scene. Idempotent.
    public static void CreateScene()
    {
        Directory.CreateDirectory("Assets/Settings");
        Directory.CreateDirectory(Res + "UI");

        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP.asset");
        if (!urp)
        {
            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(data, "Assets/Settings/Renderer.asset");
            urp = UniversalRenderPipelineAsset.Create(data);
            AssetDatabase.CreateAsset(urp, "Assets/Settings/URP.asset");
        }
        urp.supportsHDR = true;
        urp.shadowDistance = 70;
        urp.shadowCascadeCount = 2;
        EditorUtility.SetDirty(urp);
        GraphicsSettings.defaultRenderPipeline = urp;
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }

        Mat("Lit", "Universal Render Pipeline/Lit", m => m.SetFloat("_Smoothness", 0.15f));
        Mat("Particle", "Universal Render Pipeline/Particles/Simple Lit", null);
        Mat("Sky", "Skybox/Procedural", m =>
        {
            m.SetColor("_SkyTint", new Color(0.45f, 0.62f, 0.95f));
            m.SetColor("_GroundColor", new Color(0.55f, 0.62f, 0.7f));
            m.SetFloat("_AtmosphereThickness", 0.85f);
            m.SetFloat("_Exposure", 1.1f);
            m.SetFloat("_SunSize", 0.05f);
        });

        if (!File.Exists(Res + "Post.asset"))
        {
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, Res + "Post.asset");
            var tone = p.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
            var col = p.Add<ColorAdjustments>(true); col.postExposure.Override(0f); col.contrast.Override(8); col.saturation.Override(4);
            var wb = p.Add<WhiteBalance>(true); wb.temperature.Override(2);
            var bloom = p.Add<Bloom>(true); bloom.threshold.Override(1f); bloom.intensity.Override(0.3f); bloom.scatter.Override(0.6f);
            var vig = p.Add<Vignette>(true); vig.intensity.Override(0.24f); vig.smoothness.Override(0.5f);
            var dof = p.Add<DepthOfField>(true); dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(18); dof.gaussianEnd.Override(55); dof.gaussianMaxRadius.Override(1.2f);
            foreach (var c in p.components) AssetDatabase.AddObjectToAsset(c, p);
            EditorUtility.SetDirty(p);
        }

        if (!File.Exists(Res + "UI/Theme.tss"))
        {
            File.WriteAllText(Res + "UI/Theme.tss", "@import url(\"unity-theme://default\");\n");
            AssetDatabase.ImportAsset(Res + "UI/Theme.tss");
        }
        if (!File.Exists(Res + "UI/Panel.asset"))
        {
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Res + "UI/Theme.tss");
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 1f;
            AssetDatabase.CreateAsset(ps, Res + "UI/Panel.asset");
        }

        // Les modeles importes avant le passage a URP doivent etre reimportes pour avoir des materiaux URP.
        AssetDatabase.ImportAsset(Res + "Models", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
        PlayerSettings.productName = "Pique-Nique's Games";
        PlayerSettings.companyName = "Anastasia";
        PlayerSettings.defaultIsNativeResolution = true;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        AssetDatabase.SaveAssets();

        RabbitController();
        AssetDatabase.ImportAsset(Res + "Characters", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        CharacterController();
        Portraits();
        AssetDatabase.DeleteAsset(Res + "Base.mat");
        AssetDatabase.SaveAssets();
        Diagnose();
        SelfCheck();
    }

    // Copie les clips du lapin (pour pouvoir regler la boucle) et monte un AnimatorController.
    static void RabbitController()
    {
        string dir = Res + "Anim/";
        Directory.CreateDirectory(dir);
        var clips = AssetDatabase.LoadAllAssetsAtPath(Res + "Models/Rabbit.glb").OfType<AnimationClip>().ToArray();
        string ctrlPath = Res + "RabbitAnim.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        var sm = ctrl.layers[0].stateMachine;
        string[] loops = { "Idle", "Sitting_Eating", "Wave", "Jump_Idle" };
        foreach (var n in new[] { "Idle", "Jump", "Jump_Idle", "Death", "HitReact", "Wave", "Yes", "No", "Sitting_Eating", "Duck" })
        {
            var copy = Object.Instantiate(clips.First(c => c.name.EndsWith("|" + n)));
            copy.name = n;
            var s = AnimationUtility.GetAnimationClipSettings(copy);
            s.loopTime = loops.Contains(n);
            AnimationUtility.SetAnimationClipSettings(copy, s);
            AssetDatabase.DeleteAsset(dir + n + ".anim");
            AssetDatabase.CreateAsset(copy, dir + n + ".anim");
            var state = sm.AddState(n);
            state.motion = copy;
            if (n == "Idle") sm.defaultState = state;
        }
    }

    // Tous les personnages Quaternius partagent le meme squelette : un seul controleur suffit.
    static void CharacterController()
    {
        string dir = Res + "CharAnim/";
        Directory.CreateDirectory(dir);
        var clips = AssetDatabase.LoadAllAssetsAtPath(Res + "Characters/Casual_Male.fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
        Debug.Log("DIAG char clips: " + string.Join(", ", clips.Select(c => c.name)));
        string ctrlPath = Res + "CharAnim.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        var sm = ctrl.layers[0].stateMachine;
        string[] loops = { "Idle", "Walk", "Run" };
        foreach (var n in new[] { "Idle", "Walk", "Run", "PickUp", "Victory", "Defeat", "RecieveHit", "Death", "SitDown" })
        {
            var src = clips.FirstOrDefault(c => c.name.EndsWith(n) && !clips.Any(o => o != c && o.name.EndsWith(n) && o.name.Length < c.name.Length));
            if (!src) { Debug.LogWarning("DIAG clip manquant: " + n); continue; }
            var copy = Object.Instantiate(src);
            copy.name = n;
            var s = AnimationUtility.GetAnimationClipSettings(copy);
            s.loopTime = loops.Contains(n);
            AnimationUtility.SetAnimationClipSettings(copy, s);
            AssetDatabase.DeleteAsset(dir + n + ".anim");
            AssetDatabase.CreateAsset(copy, dir + n + ".anim");
            var state = sm.AddState(n);
            state.motion = copy;
            if (n == "Idle") sm.defaultState = state;
        }
    }

    // Portrait de chaque personnage (tete et epaules), pour l'ecran de choix d'avatar.
    static void Portraits()
    {
        string dir = Res + "Portraits/";
        Directory.CreateDirectory(dir);
        var go = new GameObject("PortraitCam");
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.fieldOfView = 22;
        var lightGo = new GameObject("PortraitLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.4f;
        lightGo.transform.rotation = Quaternion.Euler(30, -25, 0);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.9f, 0.9f, 0.95f);
        var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        foreach (var path in Directory.GetFiles(Res + "Characters", "*.fbx"))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
            var c = (GameObject)Object.Instantiate(prefab);
            Chars.ApplySkin(c, prefab.name);
            c.transform.rotation = Quaternion.Euler(0, 180, 0);
            var rs = c.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var head = new Vector3(b.center.x, b.max.y - b.size.y * 0.24f, b.center.z);
            cam.transform.position = head + new Vector3(0, b.size.y * 0.05f, -b.size.y * 1.5f);
            cam.transform.LookAt(head);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            tex.Apply();
            File.WriteAllBytes(dir + Path.GetFileNameWithoutExtension(path) + ".png", tex.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(c);
        }
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(lightGo);
        rt.Release();
        AssetDatabase.ImportAsset(dir, ImportAssetOptions.ImportRecursive);
    }

    static void Mat(string name, string shader, System.Action<Material> init)
    {
        string path = Res + name + ".mat";
        if (File.Exists(path)) return;
        var m = new Material(Shader.Find(shader));
        init?.Invoke(m);
        AssetDatabase.CreateAsset(m, path);
    }

    public static void DiagChar()
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(Res + "Characters/Casual_Male.fbx");
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            foreach (var m in r.sharedMaterials)
                Debug.Log($"DIAG mat {r.name}/{m.name} shader={m.shader.name} color={m.color} tex={m.mainTexture} keywords={string.Join(",", m.shaderKeywords)} surf={(m.HasProperty("_Surface") ? m.GetFloat("_Surface") : -1)} metal={(m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : -1)}");
        var imp = (ModelImporter)AssetImporter.GetAtPath(Res + "Characters/Casual_Male.fbx");
        Debug.Log("DIAG importer materialImportMode=" + imp.materialImportMode + " location=" + imp.materialLocation);
    }

    static void Diagnose()
    {
        var rabbit = Resources.Load<GameObject>("Models/Rabbit");
        Debug.Log("DIAG rabbit: " + (rabbit ? string.Join(", ", rabbit.GetComponentsInChildren<Component>(true).Select(c => c.GetType().Name).Distinct()) : "null"));
        if (rabbit)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(Res + "Models/Rabbit.glb").OfType<AnimationClip>().Select(c => c.name + (c.legacy ? "(legacy)" : ""));
            Debug.Log("DIAG clips: " + string.Join(", ", clips));
            var b = rabbit.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
            Debug.Log("DIAG rabbit bounds: " + b.size);
        }
        var tree = Resources.Load<GameObject>("Models/tree_oak");
        Debug.Log("DIAG tree: " + (tree ? tree.GetComponentInChildren<Renderer>().bounds.size + " " + tree.GetComponentInChildren<Renderer>().sharedMaterial.shader.name : "null"));
    }

    public static void Build()
    {
        var r = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Main.unity" }, "Build/PiqueNiqueGames.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
        if (r.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new System.Exception("build: " + r.summary.result);
    }

    public static void SelfCheck()
    {
        var rng = new System.Random(1);
        for (int g = 0; g < 200; g++)
        {
            var mode = g % 2 == 0 ? Mode.Classique : Mode.Ameliore;
            var r = new Rules(mode, new[] { "A", "B", "C" }, g);
            if (mode == Mode.Ameliore && new System.Collections.Generic.HashSet<int>(r.cycle).Count != 25) throw new System.Exception("cycle incomplet");
            for (int n = 0; n < 5000 && !r.Over; n++)
            {
                r.Draw();
                if (r.drawn != null) for (int k = 0; k < 3; k++) if (r.Move((k + rng.Next(3)) % 3) != null) break;
                var seen = new System.Collections.Generic.HashSet<int>();
                foreach (var p in r.players) foreach (var pos in p.rabbits)
                    if (pos != 0 && pos != r.summit && !seen.Add(pos)) throw new System.Exception("deux lapins sur la case " + pos);
            }
            if (!r.Over) throw new System.Exception("partie sans fin");
        }
        // Blackjack : bots au hasard, puis rejeu des memes actions pour verifier le determinisme.
        for (int g = 0; g < 300; g++)
        {
            var names = new[] { "A", "B", "C", "D" }.Take(2 + g % 3).ToArray();
            var a = new Blackjack(names, 10, g);
            var b = new Blackjack(names, 10, g);
            string[] all = { "hit", "stand", "double", "split" };
            for (int n = 0; n < 5000 && !a.Finished; n++)
            {
                string[] act = a.phase == BJPhase.Bet ? new[] { "bet", (10 * (1 + rng.Next(10))).ToString() }
                             : a.phase == BJPhase.Insurance ? new[] { "ins", rng.Next(2).ToString() }
                             : new[] { all[rng.Next(4)] };
                if (!a.TryApply(act)) { act = a.Bot(); if (!a.TryApply(act)) throw new System.Exception("action bot refusee : " + string.Join("|", act)); }
                b.TryApply(act);
                foreach (var p in a.players)
                {
                    if (p.chips < 0) throw new System.Exception("jetons negatifs");
                    foreach (var h in p.hands) if (Blackjack.Value(h.cards) > 21 && !h.done) throw new System.Exception("main sautee non terminee");
                }
            }
            if (!a.Finished) throw new System.Exception("blackjack sans fin");
            if (string.Join("|", a.log) != string.Join("|", b.log)) throw new System.Exception("blackjack non deterministe");
        }
        Debug.Log("SELFCHECK OK");
    }
}
