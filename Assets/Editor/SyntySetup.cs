using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Construit Resources/Synty.asset a partir des packs importes : Unity -batchmode -executeMethod SyntySetup.Run
public static class SyntySetup
{
    const string Meadow = "Assets/PolygonNatureBiomes/PNB_Meadow_Forest/";

    // Seulement ce que la salle de casino utilise : le reste du pack (ville, vehicules...) reste hors de la build.
    static readonly string[] CasinoProps =
    {
        "SM_Prop_Blackjack_Table_01", "SM_Prop_Roulette_Table_01", "SM_Prop_Poker_Table_01", "SM_Prop_Chair_03", "SM_Prop_Slot_Machine_0",
        "SM_Prop_Chandelier_01", "SM_Prop_Pillar_0", "SM_Prop_Fountain_01", "SM_Prop_Pot_Plants_0", "SM_Prop_Wall_Art_0", "SM_Prop_Casino_Neon_", "SM_Prop_Bar_Stool_01",
        "SM_Bld_Casino_Wall_0", "SM_Bld_Ceiling_0", "SM_Bld_Pillar_Lights_01", "SM_Prop_Bar_Round_01", "SM_Prop_Bar_Bottle_0", "SM_Prop_Bar_Glass_0",
        "SM_Prop_Couch_Suite_01", "SM_Bld_Stage_01", "SM_Bld_Curtain_Closed_01", "SM_Prop_Light_Stage_Spot_01", "SM_Prop_Statue_Pegasus_01",
        "SM_Prop_CasinoSculpture_0", "SM_Prop_Casino_Sign_Decor_0", "SM_Prop_Slot_Stand", "SM_Prop_Craps_Table_01", "SM_Prop_Stanchion_01",
        "SM_Prop_Fortune_Wheel_01", "SM_Prop_ScreenWall_01", "SM_Prop_Wall_Fountain_01", "SM_Prop_Win_Sign_01", "SM_Prop_Casino_Dice_Sign_01",
        "SM_Prop_Ceiling_Clock_01", "SM_Item_Casino_Chip_Pile_0",
    };

    [MenuItem("Pique-Nique/Registre Synty")]
    public static void Run()
    {
        var reg = ScriptableObject.CreateInstance<Synty>();
        reg.prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { Meadow + "Prefabs", "Assets/PolygonNatureBiomes/PNB_Core/Prefabs" })
            .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
            .Concat(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/PolygonCasino/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => CasinoProps.Any(n => Path.GetFileNameWithoutExtension(p).StartsWith(n)))
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>))
            .ToArray();
        reg.sky = AssetDatabase.LoadAssetAtPath<Material>(Meadow + "Materials/Skybox_Meadows_Mat_01.mat");
        reg.ground = AssetDatabase.LoadAssetAtPath<Material>(Meadow + "Materials/Rock_Grass_Triplanar_Meadow_01.mat");
        reg.water = AssetDatabase.LoadAssetAtPath<Material>(Meadow + "Materials/Water_Lake_01.mat");
        reg.post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Meadow + "Meadows_Post_Processing_01.asset");
        reg.layers = new[] { "Grass_01", "Grass_02", "Grass_Flowers_01", "Mud_01", "Moss_01", "Dirt_Cracked_Leaves_01" }
            .Select(n => AssetDatabase.LoadAssetAtPath<TerrainLayer>(Meadow + "Terrain/Terrain_Meadow_" + n + ".terrainlayer")).ToArray();
        // Ecran du quiz : non eclaire (l'image garde ses couleurs), reference ici pour garder le shader dans la build.
        if (!AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/QuizScreen.mat"))
            AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Unlit")), "Assets/Resources/QuizScreen.mat");
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
        if (File.Exists("Assets/Stage/TennaStage.fbx")) reg.stage = StageSetup.Run();
        reg.tenna = StageSetup.Tenna();
        AssetDatabase.CreateAsset(reg, "Assets/Resources/Synty.asset");
        AssetDatabase.SaveAssets();
        Debug.Log($"SYNTY OK : {reg.prefabs.Length} prefabs, ciel {reg.sky != null}, sol {reg.ground != null}, eau {reg.water != null}, couches {reg.layers.Count(l => l)}");
    }
}
