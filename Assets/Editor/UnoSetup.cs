using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Uno : materiau transparent non eclaire (fleches du sens, halos), cree dans l'editeur pour que la variante
// transparente du shader soit bien gardee dans la build.   -executeMethod UnoSetup.Run
public static class UnoSetup
{
    public static void Run()
    {
        const string path = "Assets/Resources/UnoGlow.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.SetFloat("_Surface", 1);
        m.SetFloat("_Blend", 0);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/uno_ring.png"));
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        Debug.Log("UNOGLOW OK");
    }
}
