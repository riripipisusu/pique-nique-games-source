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
        // Variantes activees a l execution : un materiau qui les utilise les garde dans la build.
        Mat("LitCutout", "Universal Render Pipeline/Lit", m => { m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450; });
        Mat("LitGlow", "Universal Render Pipeline/Lit", m => { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.white); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; });
        Mat("Sky", "Skybox/Procedural", m =>
        {
            m.SetColor("_SkyTint", new Color(0.45f, 0.62f, 0.95f));
            m.SetColor("_GroundColor", new Color(0.55f, 0.62f, 0.7f));
            m.SetFloat("_AtmosphereThickness", 0.85f);
            m.SetFloat("_Exposure", 1.1f);
            m.SetFloat("_SunSize", 0.05f);
        });

        // Etalonnage "cinema" : ACES, ombres froides / hautes lumieres chaudes, bloom, vignette, grain.
        AssetDatabase.DeleteAsset(Res + "Post.asset");
        {
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, Res + "Post.asset");
            var tone = p.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var col = p.Add<ColorAdjustments>(true); col.postExposure.Override(0.35f); col.contrast.Override(18); col.saturation.Override(12);
            var wb = p.Add<WhiteBalance>(true); wb.temperature.Override(6); wb.tint.Override(-3);
            var smh = p.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.92f, 0.98f, 1.1f, 0));
            smh.highlights.Override(new Vector4(1.08f, 1.02f, 0.92f, 0));
            var bloom = p.Add<Bloom>(true); bloom.threshold.Override(0.9f); bloom.intensity.Override(0.55f); bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(1f, 0.9f, 0.75f));
            var vig = p.Add<Vignette>(true); vig.intensity.Override(0.3f); vig.smoothness.Override(0.45f);
            var grain = p.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Thin1); grain.intensity.Override(0.12f);
            var dof = p.Add<DepthOfField>(true); dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(10); dof.gaussianEnd.Override(40); dof.gaussianMaxRadius.Override(1.2f);
            foreach (var c in p.components) AssetDatabase.AddObjectToAsset(c, p);
            EditorUtility.SetDirty(p);
        }

        // Ombres : 4 cascades, ombres douces de haute qualite, ombres des lumieres ponctuelles.
        var urpSo = new SerializedObject(urp);
        urpSo.FindProperty("m_ShadowCascadeCount").intValue = 4;
        var soft = urpSo.FindProperty("m_SoftShadowQuality");
        if (soft != null) soft.intValue = 3;
        urpSo.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
        urpSo.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 2048;
        urpSo.ApplyModifiedPropertiesWithoutUndo();

        // Occlusion ambiante (SSAO) : la classe est interne a URP, on l'ajoute par son nom.
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/Renderer.asset");
        if (!rendererData.rendererFeatures.Any(f => f && f.GetType().Name == "ScreenSpaceAmbientOcclusion"))
        {
            var type = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            feature.name = "SSAO";
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            rendererData.rendererFeatures.Add(feature);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long id);
            var so = new SerializedObject(rendererData);
            var map = so.FindProperty("m_RendererFeatureMap");
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach (var f in rendererData.rendererFeatures.Where(f => f && f.GetType().Name == "ScreenSpaceAmbientOcclusion"))
        {
            var fs = new SerializedObject(f);
            fs.FindProperty("m_Settings.Intensity").floatValue = 1.6f;
            fs.FindProperty("m_Settings.Radius").floatValue = 0.6f;
            fs.FindProperty("m_Settings.DirectLightingStrength").floatValue = 0.35f;
            fs.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("DIAG SSAO ok");
        }
        EditorUtility.SetDirty(rendererData);

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
        PlayerSettings.runInBackground = true;   // en ligne, un Alt+Tab ne doit pas figer la partie
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
    public static void Portraits()
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
        var names = Directory.GetFiles(Res + "Characters", "*.fbx").Select(Path.GetFileNameWithoutExtension).Concat(Chars.Looks.Keys);
        foreach (var name in names)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Res + "Characters/" + Chars.ModelOf(name) + ".fbx");
            var c = (GameObject)Object.Instantiate(prefab);
            Chars.ApplySkin(c, name);
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
            File.WriteAllBytes(dir + name + ".png", tex.EncodeToPNG());
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

    // Terrain Synty precalcule dans sa propre scene (hors depot) : chargement rapide et shaders de terrain gardes dans la build.
    static void BakeWorld()
    {
        Directory.CreateDirectory("Assets/Synty");
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        var t = Nature.Build(null, Board.FreeSpot);
        AssetDatabase.DeleteAsset("Assets/Synty/Monde_Terrain.asset");
        AssetDatabase.CreateAsset(t.terrainData, "Assets/Synty/Monde_Terrain.asset");
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, "Assets/Synty/Monde.unity");
    }

    public static void Build()
    {
        if (File.Exists("VERSION")) PlayerSettings.bundleVersion = File.ReadAllText("VERSION").Trim();
        BakeWorld();
        var r = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Main.unity", "Assets/Synty/Monde.unity" }, "Build/PiqueNiqueGames.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
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
        // Roulette : paiements, validite des mises, prison, fin de partie et jetons jamais negatifs.
        void Eq(int got, int want, string what) { if (got != want) throw new System.Exception($"roulette {what} : {got} au lieu de {want}"); }
        Eq(Roulette.Payout("P:17"), 35, "plein"); Eq(Roulette.Payout("C:14-17"), 17, "cheval"); Eq(Roulette.Payout("T:0a"), 11, "transversale 0");
        Eq(Roulette.Payout("Q:0"), 8, "carre 0"); Eq(Roulette.Payout("Q:13"), 8, "carre"); Eq(Roulette.Payout("S:10"), 5, "sixain");
        Eq(Roulette.Payout("D:2"), 2, "douzaine"); Eq(Roulette.Payout("L:2"), 2, "colonne"); Eq(Roulette.Payout("R"), 1, "rouge");
        Eq(Roulette.Numbers("R").Length, 18, "rouges"); Eq(Roulette.Numbers("L:2").Last(), 36, "colonne 3");
        foreach (var bad in new[] { "C:3-4", "C:0-5", "Q:3", "Q:33", "P:37", "S:11", "X" })
            if (Roulette.Numbers(bad) != null) throw new System.Exception("roulette : mise invalide acceptee " + bad);
        if (Roulette.Parse("R:-:15") != null || Roulette.Parse("P:17:10;R:-:20").Count != 2) throw new System.Exception("roulette : lecture des mises");
        for (int g = 0; g < 300; g++)
        {
            var names = new[] { "A", "B", "C", "D" }.Take(1 + g % 4).ToArray();
            var a = new Roulette(names, 20, g);
            var b = new Roulette(names, 20, g);
            string[] keys = { "P:0", "P:17", "C:0-2", "T:5", "Q:0", "S:3", "D:1", "L:0", "R", "N", "PA", "IM", "MA", "PS" };
            for (int n = 0; n < 500 && !a.Finished; n++)
            {
                var p = a.Current;
                var bets = Enumerable.Range(0, rng.Next(4)).Select(_ => keys[rng.Next(keys.Length)]).Select(k => $"{(Roulette.IsSimple(k) ? k + ":-" : k)}:{10 * (1 + rng.Next(5))}");
                string[] act = { "bets", string.Join(";", bets) };
                if (!a.TryApply(act)) { act = a.Bot(); b.Bot(); if (!a.TryApply(act)) throw new System.Exception("roulette : action bot refusee " + act[1]); }
                else if (n % 3 == 0) a.Bot();   // appeler Bot sur une seule copie ne doit rien changer (hote vs invites)
                b.TryApply(act);
                foreach (var q in a.players) if (q.chips < 0) throw new System.Exception("roulette : jetons negatifs");
            }
            if (!a.Finished) throw new System.Exception("roulette sans fin");
            if (a.players.Any(q => q.prison.Count > 0)) throw new System.Exception("roulette : prison non videe en fin de partie");
            if (string.Join("|", a.log) != string.Join("|", b.log)) throw new System.Exception("roulette non deterministe");
        }
        // Clic sur le tapis : le point d'ancrage de chaque mise doit redonner cette mise.
        var allKeys = Enumerable.Range(0, 37).Select(n => "P:" + n).Concat(new[] { "C:0-1", "C:0-2", "C:0-3", "T:0a", "T:0b", "Q:0", "D:0", "D:1", "D:2", "L:0", "L:1", "L:2" })
            .Concat(Roulette.Simple).Concat(Enumerable.Range(1, 33).Select(n => $"C:{n}-{n + 3}")).Concat(Enumerable.Range(1, 35).Where(n => n % 3 != 0).Select(n => $"C:{n}-{n + 1}"))
            .Concat(Enumerable.Range(1, 32).Where(n => n % 3 != 0).Select(n => "Q:" + n)).Concat(Enumerable.Range(0, 12).Select(r => "T:" + r)).Concat(Enumerable.Range(0, 11).Select(r => "S:" + r));
        foreach (var k in allKeys) if (Ui.KeyAt(Ui.Anchor(k)) != k) throw new System.Exception($"tapis : {k} lu comme {Ui.KeyAt(Ui.Anchor(k))}");
        // Quiz : tolerance des reponses, points, partie jusqu'a 100.
        void Ok(bool c, string what) { if (!c) throw new System.Exception("quiz : " + what); }
        Ok(Quiz.Matches("attaque des titans", new[] { "L'Attaque des Titans" }), "article");
        Ok(Quiz.Matches("spiderman", new[] { "Spider-Man" }), "tiret");
        Ok(Quiz.Matches("atack on titan", new[] { "Attack on Titan" }), "faute de frappe");
        Ok(Quiz.Matches("Cote d ivoire", new[] { "Côte d'Ivoire" }), "accents");
        Ok(Quiz.Matches("gta", new[] { "Grand Theft Auto V", "GTA" }), "acronyme");
        Ok(!Quiz.Matches("chat", new[] { "Chien" }), "mot court proche refuse");
        Ok(!Quiz.Matches("Iran", new[] { "Irak" }), "pays proche refuse");
        Ok(Quiz.PointsFor(0, true) == 12 && Quiz.PointsFor(20000, false) == 3, "points");
        Ok(Quiz.Pool.Count > 100 && Quiz.Pool.All(q => q.a.Length > 0 && q.u.StartsWith("https://")), "banque de questions");
        {
            var q = new Quiz(new[] { "A", "B" }, 2, 3, Quiz.Pool);
            for (int r = 0; r < 60 && !q.Finished; r++)
            {
                Ok(q.TryApply(new[] { "next" }), "next");
                Ok(q.TryApply(new[] { "guess", "1", "500", "reponse fausse" }), "mauvaise reponse");
                Ok(q.TryApply(new[] { "guess", "0", "1000", q.Current.a[0] }), "bonne reponse");
                Ok(!q.TryApply(new[] { "guess", "0", "1200", q.Current.a[0] }), "deja trouve");
                Ok(q.TryApply(new[] { "end" }), "end");
            }
            Ok(q.Finished && q.players[0].score >= Quiz.Target && q.players[1].score == 0, "fin de partie a 100");
        }
        if (!Updater.IsNewer("v2.1.0", "2.0.9") || Updater.IsNewer("v2.1.0", "2.1.0") || Updater.IsNewer("v1.9", "2.0")) throw new System.Exception("comparaison de versions");
        Debug.Log("SELFCHECK OK");
    }
}
