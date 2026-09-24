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
        PlayerSettings.productName = "Croque-Carotte";
        PlayerSettings.companyName = "Anastasia";
        PlayerSettings.defaultIsNativeResolution = true;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        AssetDatabase.SaveAssets();

        RabbitController();
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

    static void Mat(string name, string shader, System.Action<Material> init)
    {
        string path = Res + name + ".mat";
        if (File.Exists(path)) return;
        var m = new Material(Shader.Find(shader));
        init?.Invoke(m);
        AssetDatabase.CreateAsset(m, path);
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
        var r = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Main.unity" }, "Build/CroqueCarotte.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
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
        Debug.Log("SELFCHECK OK");
    }
}
