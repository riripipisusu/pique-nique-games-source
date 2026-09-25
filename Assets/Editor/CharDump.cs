using System.Linq;
using UnityEditor;
using UnityEngine;

// Liste des materiaux et animations des personnages Quaternius (diagnostic).
public static class CharDump
{
    public static void Run()
    {
        foreach (var p in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Resources/Characters" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var g = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            var mats = g.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name + "#" + ColorUtility.ToHtmlStringRGB(m.color)).Distinct();
            Debug.Log("PERSO " + g.name + " : " + string.Join(", ", mats));
        }
        var ctrl = Resources.Load<RuntimeAnimatorController>("CharAnim");
        Debug.Log("ANIMS " + string.Join(", ", ctrl.animationClips.Select(c => c.name).Distinct()));
    }
}
public static class CharLineup
{
    // Rangee de personnages face camera : Unity -batchmode -executeMethod CharLineup.Run -out <png>
    public static void Run()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        string[] names = { "Casual_Female", "Casual2_Female", "Casual3_Female", "Casual_Male", "Casual2_Male", "Casual3_Male", "Suit_Female", "Suit_Male", "Worker_Female", "Doctor_Female_Young", "Ninja_Male_Hair", "Cowboy_Hair" };
        for (int i = 0; i < names.Length; i++)
        {
            var t = Chars.Spawn(names[i], null, new Vector3((i - names.Length / 2f) * 1.1f, 0, 0), 180, out _);
        }
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(new Vector3(-0.5f, 1.2f, -9), Quaternion.Euler(3, 0, 0));
        cam.fieldOfView = 40;
        var rt = new RenderTexture(1800, 600, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1800, 600, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1800, 600), 0, 0);
        var args = System.Environment.GetCommandLineArgs();
        System.IO.File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1], tex.EncodeToPNG());
    }
}
public static class PrefabShot
{
    // Rangee de prefabs Casino, avec leurs dimensions dans le journal : -executeMethod PrefabShot.Run -out <png>
    public static void Run()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        string[] names = { "SM_Prop_Blackjack_Table_01", "SM_Prop_Chair_01", "SM_Prop_Chair_03", "SM_Prop_Bar_Stool_01", "SM_Prop_Slot_Machine_01", "SM_Prop_Roulette_Table_01", "SM_Prop_Chandelier_01", "SM_Prop_Chip_Holder_01", "SM_Chr_Dealer_Female_01" };
        float x = -8;
        foreach (var n in names)
        {
            var path = AssetDatabase.FindAssets(n + " t:Prefab", new[] { "Assets/PolygonCasino/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath).First(p => System.IO.Path.GetFileNameWithoutExtension(p) == n);
            var g = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var rs = g.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            Debug.Log($"TAILLE {n} {b.size.x:0.00}x{b.size.y:0.00}x{b.size.z:0.00} centre {b.center} min {b.min} renderers {rs.Length} " + string.Join(",", rs.Select(r => r.name)));
            g.transform.position = new Vector3(x + b.size.x / 2 - b.center.x, 0, 0);
            x += b.size.x + 0.4f;
        }
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(new Vector3(x / 2 - 4, 4, -9), Quaternion.Euler(18, 0, 0));
        cam.fieldOfView = 70;
        var rt = new RenderTexture(1800, 700, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1800, 700, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1800, 700), 0, 0);
        var args = System.Environment.GetCommandLineArgs();
        System.IO.File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1], tex.EncodeToPNG());
    }
}
public static class WheelShot
{
    // Vue de dessus du cylindre de la roulette Synty (calage des cases) : -executeMethod WheelShot.Run -out <png>
    public static void Run()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        var path = AssetDatabase.FindAssets("SM_Prop_Roulette_Table_01 t:Prefab").Select(AssetDatabase.GUIDToAssetPath).First(p => p.EndsWith("/SM_Prop_Roulette_Table_01.prefab"));
        var g = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        foreach (var t in g.GetComponentsInChildren<Transform>())
        {
            var r = t.GetComponent<Renderer>();
            Debug.Log($"ROUE {t.name} localPos {t.localPosition} rot {t.localEulerAngles} " + (r ? $"bounds {r.bounds.center} ext {r.bounds.extents}" : ""));
        }
        var wheel = g.GetComponentsInChildren<Renderer>().First(r => r.name.Contains("Wheel"));
        var cam = Camera.main;
        var c = wheel.bounds.center;
        cam.transform.SetPositionAndRotation(c + Vector3.up * 1.2f, Quaternion.Euler(90, 0, 0));
        cam.fieldOfView = 35;
        var args = System.Environment.GetCommandLineArgs();
        var rt = new RenderTexture(900, 900, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(900, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 900, 900), 0, 0);
        System.IO.File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1], tex.EncodeToPNG());
        cam.transform.SetPositionAndRotation(g.transform.position + new Vector3(0, 3.2f, 0), Quaternion.Euler(90, 0, 0));
        cam.fieldOfView = 50;
        cam.Render();
        tex.ReadPixels(new Rect(0, 0, 900, 900), 0, 0);
        System.IO.File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1].Replace(".png", "_table.png"), tex.EncodeToPNG());
    }
}
public static class BallShot
{
    // Billes de calage autour du cylindre (rayon, hauteur sous le pivot) : -executeMethod BallShot.Run -out <png>
    public static void Run()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        var path = AssetDatabase.FindAssets("SM_Prop_Roulette_Table_01 t:Prefab").Select(AssetDatabase.GUIDToAssetPath).First(p => p.EndsWith("/SM_Prop_Roulette_Table_01.prefab"));
        var g = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        var wheel = g.GetComponentsInChildren<Transform>().First(t => t.name.Contains("Wheel"));
        var args0 = System.Environment.GetCommandLineArgs();
        bool side = System.Array.IndexOf(args0, "-side") >= 0;
        var tests = side
            ? new[] { (0.155f, -0.14f, Color.red), (0.155f, -0.17f, Color.green), (0.155f, -0.20f, Color.blue), (0.155f, -0.23f, Color.yellow), (0.215f, -0.14f, Color.magenta), (0.215f, -0.17f, Color.cyan), (0.215f, -0.20f, Color.white), (0.215f, -0.23f, Color.black) }
            : new[] { (0.16f, -0.17f, Color.red), (0.16f, -0.20f, Color.green), (0.16f, -0.23f, Color.blue), (0.13f, -0.20f, Color.yellow), (0.19f, -0.20f, Color.magenta), (0.235f, -0.16f, Color.cyan), (0.235f, -0.19f, Color.white) };
        for (int i = 0; i < tests.Length; i++)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            b.transform.SetParent(g.transform, false);
            float a = (side ? 150 + (i % 4) * 20 : 200 + i * 22) * Mathf.Deg2Rad;
            b.transform.localPosition = wheel.localPosition + new Vector3(Mathf.Sin(a) * tests[i].Item1, tests[i].Item2, Mathf.Cos(a) * tests[i].Item1);
            b.transform.localScale = Vector3.one * 0.028f;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.color = tests[i].Item3;
            b.GetComponent<Renderer>().sharedMaterial = m;
        }
        var cam = Camera.main;
        var c = wheel.position;
        cam.transform.position = c + (side ? new Vector3(0, -0.12f, -0.62f) : new Vector3(0, 0.35f, -0.75f));
        cam.transform.LookAt(c + Vector3.down * (side ? 0.18f : 0.15f));
        cam.fieldOfView = 40;
        var rt = new RenderTexture(1200, 800, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1200, 800, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0);
        var args = System.Environment.GetCommandLineArgs();
        System.IO.File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1], tex.EncodeToPNG());
    }
}
public static class WheelProfile
{
    // Profil du cylindre : hauteur de la surface (sous le pivot) selon le rayon, par lancer de rayons.
    public static void Run()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        var path = AssetDatabase.FindAssets("SM_Prop_Roulette_Table_01 t:Prefab").Select(AssetDatabase.GUIDToAssetPath).First(p => p.EndsWith("/SM_Prop_Roulette_Table_01.prefab"));
        var g = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        foreach (var mf in g.GetComponentsInChildren<MeshFilter>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        Physics.SyncTransforms();
        var wheel = g.GetComponentsInChildren<Transform>().First(t => t.name.Contains("Wheel"));
        var sb = new System.Text.StringBuilder("PROFIL ");
        foreach (float ang in new[] { 45f })
            for (float r = 0.18f; r <= 0.46f; r += 0.01f)
            {
                float a = ang * Mathf.Deg2Rad;
                var o = wheel.position + new Vector3(Mathf.Sin(a) * r, 0.5f, Mathf.Cos(a) * r);
                var hits = Physics.RaycastAll(o, Vector3.down, 2).OrderBy(x => x.distance).ToArray();
                string h = string.Join("/", hits.Select(x => (x.point.y - wheel.position.y).ToString("0.000") + (x.collider.transform == wheel ? "w" : "t")));
                sb.Append($"a{ang} r{r:0.00}:{h}  ");
            }
        foreach (var (px, py) in new[] { (505f, 405f), (60f, 515f), (545f, 460f), (325f, 311f) })
        {
            var o = new Vector3((px - 450) / 301.6f, 3, (450 - py) / 301.6f);
            var h = Physics.RaycastAll(o, Vector3.down, 5).OrderBy(x => x.distance).Select(x => x.point.y.ToString("0.000") + x.collider.name.Substring(Mathf.Max(0, x.collider.name.Length - 8)));
            sb.Append($" TAPIS {px},{py}: " + string.Join("/", h));
        }
        Debug.Log(sb.ToString());
    }
}
public static class Measure
{
    // Dimensions et point de pivot de prefabs : -executeMethod Measure.Run -names A,B,C
    public static void Run()
    {
        var args = System.Environment.GetCommandLineArgs();
        foreach (var n in args[System.Array.IndexOf(args, "-names") + 1].Split(','))
        {
            var path = AssetDatabase.FindAssets(n + " t:Prefab").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == n);
            if (path == null) { Debug.Log("MESURE " + n + " absent"); continue; }
            var g = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var rs = g.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            Debug.Log($"MESURE {n} taille {b.size.x:0.00}x{b.size.y:0.00}x{b.size.z:0.00} min {b.min.x:0.00},{b.min.y:0.00},{b.min.z:0.00} max {b.max.x:0.00},{b.max.y:0.00},{b.max.z:0.00}");
            Object.DestroyImmediate(g);
        }
    }
}
public static class StageShot
{
    // Vue de face du plateau importe depuis Blender : -executeMethod StageShot.Run -out <png>
    public static void Run()
    {
        ShaderUtil.allowAsyncCompilation = false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
        var g = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Stage/TennaStage.fbx"));
        foreach (var t in g.GetComponentsInChildren<Transform>())
        {
            var r = t.GetComponent<Renderer>();
            Debug.Log($"SCENE {t.name} pos {t.position} rot {t.eulerAngles} scale {t.lossyScale} " + (r ? $"bounds {r.bounds.center} size {r.bounds.size} mats {string.Join(",", r.sharedMaterials.Select(m => m ? m.name + ":" + m.shader.name : "null"))}" : ""));
        }
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(new Vector3(0, 3.6f, 16), Quaternion.Euler(5, 180, 0));
        cam.fieldOfView = 45;
        var args = System.Environment.GetCommandLineArgs();
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        System.IO.File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1], tex.EncodeToPNG());
    }
}
public static class TennaDump
{
    public static void Run()
    {
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath("Assets/Stage/Tenna/Tenna.fbx")) Debug.Log("SOUS " + a.GetType().Name + " " + a.name);
        var imp = (ModelImporter)AssetImporter.GetAtPath("Assets/Stage/Tenna/Tenna.fbx");
        Debug.Log("SOUS takes " + string.Join(",", imp.importedTakeInfos.Select(t => t.name + " " + t.startTime + "-" + t.stopTime)) + " anim=" + imp.importAnimation + " type=" + imp.animationType);
    }
}
