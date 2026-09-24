using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Construit Resources/Synty.asset a partir des packs importes : Unity -batchmode -executeMethod SyntySetup.Run
public static class SyntySetup
{
    const string Meadow = "Assets/PolygonNatureBiomes/PNB_Meadow_Forest/";

    [MenuItem("Pique-Nique/Registre Synty")]
    public static void Run()
    {
        var reg = ScriptableObject.CreateInstance<Synty>();
        reg.prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { Meadow + "Prefabs", "Assets/PolygonNatureBiomes/PNB_Core/Prefabs" })
            .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
        reg.sky = AssetDatabase.LoadAssetAtPath<Material>(Meadow + "Materials/Skybox_Meadows_Mat_01.mat");
        reg.ground = AssetDatabase.LoadAssetAtPath<Material>(Meadow + "Materials/Rock_Grass_Triplanar_Meadow_01.mat");
        reg.water = AssetDatabase.LoadAssetAtPath<Material>(Meadow + "Materials/Water_Lake_01.mat");
        reg.post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Meadow + "Meadows_Post_Processing_01.asset");
        reg.layers = new[] { "Grass_01", "Grass_02", "Grass_Flowers_01", "Mud_01", "Moss_01", "Dirt_Cracked_Leaves_01" }
            .Select(n => AssetDatabase.LoadAssetAtPath<TerrainLayer>(Meadow + "Terrain/Terrain_Meadow_" + n + ".terrainlayer")).ToArray();
        if (!AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/TerrainLit.mat"))
            AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")), "Assets/Resources/TerrainLit.mat");
        // Shaders du moteur de terrain : jamais references par une scene, donc a inclure explicitement dans la build.
        var gs = new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset"));
        var inc = gs.FindProperty("m_AlwaysIncludedShaders");
        foreach (var n in new[] { "Nature/Terrain/BillboardTree", "Hidden/TerrainEngine/BillboardTree", "Hidden/TerrainEngine/CameraFacingBillboardTree", "Hidden/Nature/Tree Soft Occlusion Bark Rendertex", "Hidden/TerrainEngine/Splatmap/Standard-BaseGen", "Hidden/TerrainEngine/HeightBlitCopy", "Hidden/TerrainEngine/GenerateNormalmap", "Hidden/TerrainEngine/TerrainLayerUtils", "Hidden/TerrainEngine/CrossBlendNeighbors", "Hidden/TerrainEngine/Details/UniversalPipeline/WavingDoublePass", "Hidden/TerrainEngine/Details/UniversalPipeline/Vertexlit",
                                  "Hidden/TerrainEngine/Details/UniversalPipeline/BillboardWavingDoublePass", "Universal Render Pipeline/Terrain/Lit",
                                  "Hidden/Universal Render Pipeline/Terrain/Lit (Add Pass)", "Hidden/Universal Render Pipeline/Terrain/Lit (Basemap Rendering)" })
        {
            var sh = Shader.Find(n);
            if (!sh) { Debug.LogWarning("shader absent " + n); continue; }
            bool has = false;
            for (int i = 0; i < inc.arraySize; i++) has |= inc.GetArrayElementAtIndex(i).objectReferenceValue == sh;
            if (has) continue;
            inc.InsertArrayElementAtIndex(inc.arraySize);
            inc.GetArrayElementAtIndex(inc.arraySize - 1).objectReferenceValue = sh;
        }
        gs.ApplyModifiedProperties();
        // Instanciation GPU sur tous les materiaux des packs : les arbres identiques partent en un seul lot.
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/PolygonNatureBiomes" }))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
            if (m && !m.enableInstancing) { m.enableInstancing = true; EditorUtility.SetDirty(m); }
        }
        AssetDatabase.CreateAsset(reg, "Assets/Resources/Synty.asset");
        AssetDatabase.SaveAssets();
        Debug.Log($"SYNTY OK : {reg.prefabs.Length} prefabs, ciel {reg.sky != null}, sol {reg.ground != null}, eau {reg.water != null}, couches {reg.layers.Count(l => l)}");
    }
}
