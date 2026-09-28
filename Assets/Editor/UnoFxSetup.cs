using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Effets du Uno a partir des assets du jeu Uno fournis par l'utilisatrice (Assets/UnoFX, hors depot).
// Les prefabs y ont perdu leurs modeles et materiaux, et leurs animateurs ne pointent plus vers les clips ; il reste
// la hierarchie et les animations d'origine. On reassemble donc chaque effet : hierarchie d'origine + nos modeles
// (gfx/*) + materiaux "PiqueNique/UnoFx" (equivalent URP des shaders de particules) + clips d'origine dont les
// proprietes de teinte (noms brouilles "path_0x41..71") sont renommees en _Tint.r/g/b/a.
// Resultat : Resources/UnoFX/<Effet>.prefab, avec un Animator dont chaque etat porte le nom du clip (Skip_R...).
//   -executeMethod UnoFxSetup.Run        -executeMethod UnoFxSetup.Shot -out <png>
public static class UnoFxSetup
{
    const string Src = "Assets/UnoFX/", Out = "Assets/Resources/UnoFX/";
    static readonly Dictionary<string, string> Channel = new Dictionary<string, string>
        { ["0x41"] = "r", ["0x51"] = "g", ["0x61"] = "b", ["0x71"] = "a" };
    static readonly Dictionary<char, string> ColorName = new Dictionary<char, string> { ['R'] = "Red", ['Y'] = "Yellow", ['G'] = "Green", ['B'] = "Blue" };

    // Habillage d'un noeud : modele (null = quad face a +y), texture, additif ?
    struct Skin { public string mesh, tex; public bool add; }
    static Skin S(string mesh, string tex, bool add = false) => new Skin { mesh = mesh, tex = tex, add = add };
    static Skin Glow(string tex) => new Skin { mesh = null, tex = tex, add = true };

    class Fx
    {
        public string name, prefab, clipDir;
        public string[] clips;
        public Func<string, Skin?> skin;
    }

    static Skin? ByColor(string node, string prefix, string mesh, string texFmt, bool lower = false)
    {
        if (!node.StartsWith(prefix) || node.Length != prefix.Length + 1) return null;
        string c = ColorName[node[node.Length - 1]];
        return S(mesh, string.Format(texFmt, lower ? c.ToLower() : c));
    }

    static readonly Fx[] Effects =
    {
        new Fx { name = "Skip", prefab = "cardanimation/skip/Skip", clipDir = "animation/skip", clips = new[] { "Skip_R", "Skip_Y", "Skip_G", "Skip_B" },
                 skin = n => n.StartsWith("Glow") ? Glow("gfx/skip/texture/Skip_Glow.tga") : ByColor(n, "Skip_", "gfx/skip/mesh/Skip_T.asset", "gfx/skip/texture/Classic_Skip_{0}.png") },
        new Fx { name = "Reverse", prefab = "cardanimation/reverse/Reverse", clipDir = "animation/reverse", clips = new[] { "Reverse_R", "Reverse_Y", "Reverse_G", "Reverse_B" },
                 skin = n => n == "Glow" ? Glow("gfx/reverse/texture/Reverse_Glow.tga") : ByColor(n, "Reverse_", "gfx/reverse/mesh/Reverse_T.asset", "gfx/reverse/texture/Classic_Reverse_{0}.png", true) },
        new Fx { name = "DrawTwo", prefab = "cardanimation/draw2/DrawTwo", clipDir = "animation/drawtwo", clips = new[] { "DrawTwo_R", "DrawTwo_Y", "DrawTwo_G", "DrawTwo_B" },
                 skin = n => n.StartsWith("Drawtwo_Glow") ? Glow("gfx/drawtwo/texture/DrawTwo_Glow.tga") : ByColor(n, "Drawtwo_", "gfx/drawtwo/mesh/Drawtwo_T.asset", "gfx/drawtwo/texture/Classic_Drawtwo_{0}.png") },
        new Fx { name = "Seven", prefab = "cardanimation/sevenzero/SevenZero_7", clipDir = "animation/sevenzero", clips = new[] { "SevenZero_7_R", "SevenZero_7_Y", "SevenZero_7_G", "SevenZero_7_B", "SevenZero_7_R_Loop", "SevenZero_7_Y_Loop", "SevenZero_7_G_Loop", "SevenZero_7_B_Loop" },
                 skin = n => n == "Glow" ? Glow("gfx/sevenzero/texture/Seven_Glow.tga") : ByColor(n, "SevenZero_7_", "gfx/sevenzero/mesh/Classic_SevenZero_7.asset", "gfx/sevenzero/texture/Classic_SevenZero_7_{0}.png") },
        new Fx { name = "Zero", prefab = "cardanimation/sevenzero/SevenZero_0", clipDir = "animation/sevenzero", clips = new[] { "SevenZero_0_R", "SevenZero_0_Y", "SevenZero_0_G", "SevenZero_0_B" },
                 skin = n => n == "Glow" ? Glow("gfx/sevenzero/texture/Zero_Glow.tga") : ByColor(n, "SevenZero_0_", "gfx/sevenzero/mesh/Classic_SevenZero_0.asset", "gfx/sevenzero/texture/Classic_SevenZero_0_{0}.png") },
        new Fx { name = "CallUno", prefab = "calluno/CallUNO", clipDir = "animation/call_uno", clips = new[] { "CallUNO" },
                 skin = n => n == "UNO" ? S("gfx/call_uno/mesh/Shape1.asset", "gfx/call_uno/texture/UNO_UV.tga")
                           : n.StartsWith("Star_0") ? S("gfx/call_uno/mesh/Star001.asset", "gfx/call_uno/texture/UNO_Star_UV_" + new[] { "Red", "Yellow", "Green", "Blue", "Pink" }[(n[n.Length - 1] - '1') % 5] + ".png")
                           : n.StartsWith("CallUNO_Light") ? Glow("gfx/call_uno/texture/CallUNO_Light.tga") : (Skin?)null },
        new Fx { name = "Challenge", prefab = "challenge_wilddraw4/Challenge_01", clipDir = "animation/challenge_wilddraw4", clips = new[] { "Succeed", "Failed" },
                 skin = n => n == "Succeed" ? S("animation/challenge_wilddraw4/mesh/Plane001.asset", "animation/challenge_wilddraw4/texture/Right.png")
                           : n == "Failed" ? S("animation/challenge_wilddraw4/mesh/Plane001.asset", "animation/challenge_wilddraw4/texture/Failed.png") : (Skin?)null },
    };

    static Mesh quadY;
    // Quad 1 x 1 dont la face regarde +y (les symboles et halos d'origine sont a plat, vus d'en haut).
    static Mesh QuadY()
    {
        if (quadY) return quadY;
        quadY = new Mesh { name = "QuadY" };
        quadY.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
        quadY.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        quadY.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        quadY.RecalculateNormals(); quadY.RecalculateBounds();
        AssetDatabase.CreateAsset(quadY, Out + "QuadY.asset");
        return quadY;
    }

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static Material Mat(Skin s)
    {
        string key = s.tex + (s.add ? "+" : "");
        if (mats.TryGetValue(key, out var m)) return m;
        m = new Material(Shader.Find("PiqueNique/UnoFx"));
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Src + s.tex));
        m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(s.add ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        m.renderQueue = (int)RenderQueue.Transparent + (s.add ? 10 : 0);
        AssetDatabase.CreateAsset(m, Out + "Materials/" + Path.GetFileNameWithoutExtension(s.tex) + (s.add ? "_add" : "") + ".mat");
        return mats[key] = m;
    }

    // Copie du clip avec les teintes renommees (0x41 r, 0x51 g, 0x61 b, 0x71 a) vers le shader PiqueNique/UnoFx.
    static AnimationClip ConvertClip(string path, string outName)
    {
        var src = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (!src) { Debug.LogWarning("UNOFX clip absent " + path); return null; }
        var dst = new AnimationClip { name = outName, frameRate = src.frameRate };
        foreach (var b in AnimationUtility.GetCurveBindings(src))
        {
            var curve = AnimationUtility.GetEditorCurve(src, b);
            var nb = b;
            if (b.propertyName.StartsWith("material.path_"))
            {
                string code = b.propertyName.Substring("material.path_".Length, 4);
                if (!Channel.TryGetValue(code, out var ch)) continue;
                nb.propertyName = "material._Tint." + ch;
            }
            AnimationUtility.SetEditorCurve(dst, nb, curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(src);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(dst, settings);
        AssetDatabase.CreateAsset(dst, Out + "Clips/" + outName + ".anim");
        return dst;
    }

    public static void Run()
    {
        if (AssetDatabase.IsValidFolder(Out.TrimEnd('/'))) AssetDatabase.DeleteAsset(Out.TrimEnd('/'));
        Directory.CreateDirectory(Out + "Materials");
        Directory.CreateDirectory(Out + "Clips");
        AssetDatabase.Refresh();
        mats.Clear(); quadY = null;
        foreach (var fx in Effects)
        {
            var srcGo = AssetDatabase.LoadAssetAtPath<GameObject>(Src + fx.prefab + ".prefab");
            if (!srcGo) { Debug.LogWarning("UNOFX prefab absent " + fx.prefab); continue; }
            var go = Object.Instantiate(srcGo);
            go.name = fx.name;
            // Menage : votes Twitch, colliders, textes, scripts manquants, particules, ancien animateur.
            foreach (var t in go.GetComponentsInChildren<Transform>(true).ToList())
                if (t && (t.name.StartsWith("TwitchVote") || t.name == "Colliders" || t.name.StartsWith("Label"))) Object.DestroyImmediate(t.gameObject);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                Object.DestroyImmediate(ps);
                if (r) Object.DestroyImmediate(r);
            }
            var oldAn = go.GetComponent<Animator>();
            if (oldAn) Object.DestroyImmediate(oldAn);
            int dressed = 0;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var skin = fx.skin(mf.name);
                var mr = mf.GetComponent<MeshRenderer>();
                if (skin == null) { if (mr) mr.enabled = false; continue; }
                var s = skin.Value;
                mf.sharedMesh = s.mesh == null ? QuadY() : AssetDatabase.LoadAssetAtPath<Mesh>(Src + s.mesh);
                if (!mr) mr = mf.gameObject.AddComponent<MeshRenderer>();
                mr.enabled = true;
                mr.sharedMaterials = Enumerable.Repeat(Mat(s), mf.sharedMesh ? mf.sharedMesh.subMeshCount : 1).ToArray();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                dressed++;
            }
            // Animateur : un etat par clip d'origine ; l'etat par defaut est vide (on lance l'etat voulu a l'apparition).
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(Out + fx.name + ".controller");
            var sm = ctrl.layers[0].stateMachine;
            sm.defaultState = sm.AddState("Vide");
            float longest = 0;
            foreach (var c in fx.clips)
            {
                var clip = ConvertClip(Src + fx.clipDir + "/" + c + ".anim", fx.name + "_" + c);
                if (!clip) continue;
                sm.AddState(c).motion = clip;
                longest = Mathf.Max(longest, clip.length);
            }
            var an = go.AddComponent<Animator>();
            an.runtimeAnimatorController = ctrl;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PrefabUtility.SaveAsPrefabAsset(go, Out + fx.name + ".prefab");
            Object.DestroyImmediate(go);
            Debug.Log($"UNOFX {fx.name} : {dressed} noeuds habilles, {fx.clips.Length} clips, {longest:0.00} s");
        }
        // Textures seules utilisees a part (rayons du +4, cadre du joueur actif, logo, halo de carte jouable, curseur).
        foreach (var t in new[] { "gfx/drawfour/texture/Classic_Drawfour_Sunshine_01.png", "gfx/drawfour/texture/Classic_Drawfour_Glow.tga", "gfx/avater/texture/Classic_Avater_01.tga",
                                  "gfx/particle_cardglow/texture/Classic_Highlight.tga", "gfx/single_card/texture/UNO.tga", "gfx/skip/texture/Classic_Glow_Yellow.tga",
                                  "gfx/cursor/texture/Classic_Cursor_Red.png", "gfx/cursor/texture/Classic_Cursor_Yellow.png", "gfx/cursor/texture/Classic_Cursor_Green.png",
                                  "gfx/cursor/texture/Classic_Cursor_Blue.png", "gfx/cursor/mesh/Cursor.asset",
                                  "animation/arrows/texture/Board_Arrow.tga", "animation/arrows/texture/Board_Arrow_02.tga",
                                  "animation/stacking/texture/4.tga", "animation/stacking/texture/6.tga", "animation/stacking/texture/8.tga", "animation/stacking/texture/12.tga", "animation/stacking/texture/16.tga",
                                  "animation/stacking/texture/Steaking_Circle.tga", "animation/stacking/texture/Steaking_Glow.tga",
                                  "animation/jump_in/texture/Jump_in_Circle.tga", "animation/jump_in/texture/right_false.tga", "animation/jump_in/jump in.png",
                                  "animation/didnt_calluno/texture/Exclamation.tga", "gfx/call_uno/texture/PrepareCallUNO.tga",
                                  "animation/drawcard/texture/DrawCard_RingGlow.tga", "animation/button_glow/DrawCard_Glow.tga", "animation/button_glow/Button_Glow.tga",
                                  "gfx/particle_cardglow/texture/Classic_CardGlow_Star.tga", "animation/wild/Classic_Wild.png",
                                  "cardcoveringeffect/colorchangeeffect/CCE_Card_R_Wild.tga", "cardcoveringeffect/colorchangeeffect/CCE_Card_Y_Wild.tga",
                                  "cardcoveringeffect/colorchangeeffect/CCE_Card_G_Wild.tga", "cardcoveringeffect/colorchangeeffect/CCE_Card_B_Wild.tga",
                                  "cardcoveringeffect/colorchangeeffect/CCE_Card_R_Joker.tga", "cardcoveringeffect/colorchangeeffect/CCE_Card_Y_Joker.tga",
                                  "cardcoveringeffect/colorchangeeffect/CCE_Card_G_Joker.tga", "cardcoveringeffect/colorchangeeffect/CCE_Card_B_Joker.tga",
                                  "animation/wild/Wild_Red.asset", "animation/wild/Wild_Yellow.asset", "animation/wild/Wild_Green.asset", "animation/wild/Wild_Blue.asset", "animation/wild/Wild_glow.asset" })
            AssetDatabase.CopyAsset(Src + t, Out + Path.GetFileName(t));
        AssetDatabase.CopyAsset(Src + "animation/didnt_calluno/mesh/Shape1.asset", Out + "Exclamation_Mesh.asset");
        BuildWheel();
        AssetDatabase.SaveAssets();
        Debug.Log("UNOFX OK");
    }

    // Verification : chaque effet, vu d'en haut comme dans le jeu d'origine, a 40 % de son animation.
    public static void Shot()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        var jobs = new List<(string prefab, string clip)>();
        foreach (var fx in Effects) foreach (var c in fx.clips) jobs.Add((fx.name, c));
        const int W = 240, H = 240, cols = 8;
        int rows = (jobs.Count + cols - 1) / cols;
        var sheet = new Texture2D(W * cols, H * rows, TextureFormat.RGB24, false);
        var cam = Camera.main; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.15f, 0.15f, 0.2f);
        var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt;
        for (int i = 0; i < jobs.Count; i++)
        {
            var g = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Out + jobs[i].prefab + ".prefab"));
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Out + "Clips/" + jobs[i].prefab + "_" + jobs[i].clip + ".anim");
            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(g, clip, clip.length * 0.4f);
            AnimationMode.EndSampling();
            var rs = g.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList();
            var b = rs.Count > 0 ? rs[0].bounds : new Bounds(g.transform.position, Vector3.one);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float s = Mathf.Max(b.size.x, b.size.z, 0.1f);
            cam.transform.position = b.center + Vector3.up * s * 3;
            cam.transform.LookAt(b.center, Vector3.forward);
            cam.fieldOfView = 40;
            cam.farClipPlane = s * 20;
            cam.Render();
            RenderTexture.active = rt;
            sheet.ReadPixels(new Rect(0, 0, W, H), (i % cols) * W, (rows - 1 - i / cols) * H);
            Debug.Log($"SHOT {i} {jobs[i].prefab}/{jobs[i].clip} bornes {b.size}");
            AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(g);
        }
        var args = Environment.GetCommandLineArgs();
        File.WriteAllBytes(args[Array.IndexOf(args, "-out") + 1], sheet.EncodeToPNG());
    }

    // Verification de la roue officielle (modele a squelette) et du "!" : rendus de dessus a plusieurs instants.
    public static void WheelShot()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        var sb = new System.Text.StringBuilder();
        var cam = Camera.main; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.15f, 0.15f, 0.2f);
        const int W = 200, cols = 16;
        var sheet = new Texture2D(W * cols, W * 2, TextureFormat.RGB24, false);
        var rt = new RenderTexture(W, W, 24); cam.targetTexture = rt;
        var mat = new Material(Shader.Find("PiqueNique/UnoFx"));
        mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Src + "animation/wild/Classic_Wild.png"));
        mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Src + "animation/wild/Take 001.anim");
        sb.AppendLine("clip " + (clip ? clip.length + "s" : "absent"));
        if (clip) foreach (var b in AnimationUtility.GetCurveBindings(clip).Take(30)) sb.AppendLine("  " + b.path + " " + b.type.Name + "." + b.propertyName);
        for (int i = 0; i < cols; i++)
        {
            var g = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Src + "animation/wild/Classic_Wild.prefab"));
            foreach (var t in g.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (var r in g.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMesh ? r.sharedMesh.subMeshCount : 1)).ToArray();
                if (i == 0) sb.AppendLine($"SMR {r.name} mesh={(r.sharedMesh ? r.sharedMesh.name : "-")} os={r.bones.Length} racine={(r.rootBone ? r.rootBone.name : "-")}");
                r.updateWhenOffscreen = true;
            }
            if (clip)
            {
                AnimationMode.StartAnimationMode(); AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(g, clip, i * 0.33f);
                AnimationMode.EndSampling();
            }
            var rs = g.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList();
            var bb = rs.Count > 0 ? rs[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            foreach (var r in rs) bb.Encapsulate(r.bounds);
            float sz = 12f; var cc = g.transform.position;
            cam.transform.position = cc + Vector3.up * sz * 2.5f; cam.transform.LookAt(cc, Vector3.forward);
            cam.farClipPlane = sz * 20; cam.Render(); RenderTexture.active = rt;
            sheet.ReadPixels(new Rect(0, 0, W, W), i * W, W);
            cam.nearClipPlane = 0.3f;
            sb.AppendLine($"t={i} bornes={bb.size}");
            AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(g);
        }
        // Le "!" (maillage Shape1 + Exclamation.tga)
        var em = AssetDatabase.LoadAssetAtPath<Mesh>(Src + "animation/didnt_calluno/mesh/Shape1.asset");
        var eg = new GameObject("ex"); eg.AddComponent<MeshFilter>().sharedMesh = em;
        var emat = new Material(mat); emat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Src + "animation/didnt_calluno/texture/Exclamation.tga"));
        eg.AddComponent<MeshRenderer>().sharedMaterial = emat;
        var eb = eg.GetComponent<Renderer>().bounds; float es = Mathf.Max(eb.size.x, eb.size.y, eb.size.z);
        sb.AppendLine("exclamation bornes " + eb.size);
        foreach (var (axis, k) in new[] { (Vector3.up, 0), (Vector3.back, 1), (Vector3.right, 2) })
        {
            cam.nearClipPlane = 0.001f; cam.transform.position = eb.center + axis * es * 2.5f; cam.transform.LookAt(eb.center, axis == Vector3.up ? Vector3.forward : Vector3.up);
            cam.farClipPlane = es * 20; cam.Render(); RenderTexture.active = rt;
            sheet.ReadPixels(new Rect(0, 0, W, W), k * W, 0);
        }
        var args = Environment.GetCommandLineArgs();
        File.WriteAllBytes(args[Array.IndexOf(args, "-out") + 1], sheet.EncodeToPNG());
        File.WriteAllText("Logs/unofx_wheel.txt", sb.ToString());
    }

    // Roue officielle du joker : modele a squelette (4 parts + halo), texture Classic_Wild, animation "Take 001" copiee
    // telle quelle (5 s : ouverture, puis selection du rouge, du jaune, du vert, du bleu). UnoView l'echantillonne.
    static void BuildWheel()
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Src + "animation/wild/Classic_Wild.prefab");
        if (!src) { Debug.LogWarning("UNOFX roue absente"); return; }
        var go = Object.Instantiate(src);
        go.name = "Wheel";
        foreach (var t in go.GetComponentsInChildren<Transform>(true).ToList())
            if (t && (t.name.StartsWith("TwitchVote") || t.name == "Colliders")) Object.DestroyImmediate(t.gameObject);
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        var an = go.GetComponent<Animator>(); if (an) Object.DestroyImmediate(an);
        var m = Mat(S(null, "animation/wild/Classic_Wild.png"));
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            r.sharedMaterials = Enumerable.Repeat(m, Mathf.Max(1, r.sharedMesh ? r.sharedMesh.subMeshCount : 1)).ToArray();
            r.updateWhenOffscreen = true;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
        var clip = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AnimationClip>(Src + "animation/wild/Take 001.anim"));
        var cs = AnimationUtility.GetAnimationClipSettings(clip); cs.loopTime = false; AnimationUtility.SetAnimationClipSettings(clip, cs);
        AssetDatabase.CreateAsset(clip, Out + "Clips/Wheel.anim");
        // Animateur a un seul etat "Roue" : UnoView le positionne a l'instant voulu de la prise (Play(etat, 0, t / duree)).
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(Out + "Wheel.controller");
        var st = ctrl.layers[0].stateMachine.AddState("Roue");
        st.motion = clip;
        ctrl.layers[0].stateMachine.defaultState = st;
        var wa = go.AddComponent<Animator>();
        wa.runtimeAnimatorController = ctrl;
        wa.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        PrefabUtility.SaveAsPrefabAsset(go, Out + "Wheel.prefab");
        Object.DestroyImmediate(go);
        Debug.Log("UNOFX roue " + clip.length + " s");
    }
}
