using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Registre des prefabs et materiaux Synty (rempli par SyntySetup dans l'editeur, les packs restant hors de Resources).
public class Synty : ScriptableObject
{
    public GameObject[] prefabs;
    public Material sky, ground, water;
    public VolumeProfile post;
    public GameObject stage, tenna;      // plateau du quiz (Assets/Stage, importe de Blender)
    public TerrainLayer[] layers; // herbe, herbe 2, herbe fleurie, boue, mousse, feuilles mortes

    static Synty inst;
    static Dictionary<string, GameObject> map;
    public static Synty I => inst ? inst : inst = Resources.Load<Synty>("Synty");

    public static GameObject Get(string name)
    {
        if (map == null)
        {
            map = new Dictionary<string, GameObject>();
            if (I) foreach (var p in I.prefabs) if (p) map[p.name] = p;
        }
        return map.TryGetValue(name, out var g) ? g : null;
    }
}
