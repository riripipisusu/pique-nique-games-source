using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Capture d'une scene depuis sa camera principale : Unity -batchmode -executeMethod DemoShot.Run -scene <chemin> -out <png>
public static class DemoShot
{
    public static void Run()
    {
        var args = System.Environment.GetCommandLineArgs();
        string Arg(string k) { int i = System.Array.IndexOf(args, k); return i >= 0 ? args[i + 1] : null; }
        ShaderUtil.allowAsyncCompilation = false; // sinon les shaders pas encore compiles sortent en couleur de remplacement
        EditorSceneManager.OpenScene(Arg("-scene"));
        var cam = Camera.main ?? Object.FindFirstObjectByType<Camera>();
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt;
        cam.Render();
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        File.WriteAllBytes(Arg("-out"), tex.EncodeToPNG());
        Debug.Log("CAPTURE OK " + cam.name);
    }

    // Rendu du terrain genere, hors jeu : Unity -batchmode -executeMethod DemoShot.Terrain -out <png>
    public static void Terrain()
    {
        ShaderUtil.allowAsyncCompilation = false;
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);
        var root = new GameObject("Monde").transform;
        Nature.Build(root, (p, m) => new Vector2(p.x, p.z).magnitude > 20 + m);
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(new Vector3(-40, 25, -60), Quaternion.Euler(20, 35, 0));
        cam.farClipPlane = 650;
        var args = System.Environment.GetCommandLineArgs();
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        File.WriteAllBytes(args[System.Array.IndexOf(args, "-out") + 1], tex.EncodeToPNG());
        Debug.Log("CAPTURE OK terrain " + Object.FindFirstObjectByType<UnityEngine.Terrain>().materialTemplate);
    }
}
