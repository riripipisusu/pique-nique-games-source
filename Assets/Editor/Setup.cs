using UnityEditor;
using UnityEditor.SceneManagement;

public static class Setup
{
    // La scene reste vide : Game se cree tout seul au lancement (RuntimeInitializeOnLoadMethod).
    public static void CreateScene()
    {
        // Un materiau dans Resources force l'inclusion du shader Standard dans la build.
        System.IO.Directory.CreateDirectory("Assets/Resources");
        if (!System.IO.File.Exists("Assets/Resources/Base.mat"))
            AssetDatabase.CreateAsset(new UnityEngine.Material(UnityEngine.Shader.Find("Standard")), "Assets/Resources/Base.mat");
        var scene =EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
        PlayerSettings.productName = "Croque-Carotte";
        PlayerSettings.companyName = "Anastasia";
        AssetDatabase.SaveAssets();
        SelfCheck();
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
        UnityEngine.Debug.Log("SELFCHECK OK");
    }
}
