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
