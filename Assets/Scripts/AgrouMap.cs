using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Maps d'Agrou pour le Loup-garou (Resources/Agrou/Maps, importees par Editor/AgrouImport) : posees pour que leur feu
// de camp tombe au centre du cercle des joueurs ; leurs lampes s'allument la nuit. Map 0 = la clairiere du pique-nique.
public static class AgrouMap
{
    public static readonly (string id, string name)[] All =
    {
        (null, "Clairière"), ("PlaceDuVillage", "Place du village"), ("Foret", "Forêt"), ("Cimetiere", "Cimetière"), ("Valley", "Vallée"),
        ("Feerique", "Féerique"), ("Grotte", "Grotte"), ("MapIlePirate", "Île pirate"), ("MapNoel", "Noël"), ("AsianValley", "Vallée asiatique"),
    };
    public const int Random = 15;
    // Les decors Synty sont petits a cote de nos persos (tonneau d'1,17 m mais portes basses) : la map est agrandie.
    const float Scale = 1.35f;

    public static bool Available(int i) => i == 0 || i > 0 && i < All.Length && Resources.Load<TextAsset>("Agrou/Maps/" + All[i].id);

    // Choix de l'hote -> map jouee (au hasard : tiree de la graine, la meme chez tout le monde).
    public static int Resolve(int choice, int seed)
    {
        if (choice != Random) return Available(choice) ? choice : 0;
        var ok = new List<int>();
        for (int i = 1; i < All.Length; i++) if (Available(i)) ok.Add(i);
        return ok.Count == 0 ? 0 : ok[(seed & 0x7fffffff) % ok.Count];
    }

    [System.Serializable] class Lamp { public string type; public float[] color; }
    [System.Serializable] class Info { public Lamp[] lights; }

    static GameObject cur;
    static int curId;
    static readonly List<Light> lamps = new List<Light>();

    public static void Show(int i, Vector3 fire)
    {
        if (cur && curId == i) { cur.SetActive(true); return; }
        Hide();
        if (cur) Object.Destroy(cur);
        lamps.Clear();
        grass = null;
        if (i <= 0) return;
        var prefab = Resources.Load<GameObject>("Agrou/Maps/" + All[i].id);
        if (!prefab) return;
        cur = Object.Instantiate(prefab); curId = i;
        cur.name = "Map " + All[i].id;
        cur.transform.localScale = Vector3.one * Scale;
        var c = cur.transform.Find("FIRE_CENTER");
        if (c) cur.transform.position += fire - c.position;
        foreach (var r in cur.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            var ms = r.sharedMaterials;
            if (ms.Any(x => !x)) { r.enabled = false; continue; }   // vegetation sans materiau : cachee (sinon des plaques)
            if (r.name.StartsWith("Foliage") && ms.Any(x => !x.mainTexture && x.name != "None"))   // tapis d'herbe sans materiau : carte d'herbe d'Agrou
            {
                if (!grass) { grass = new Material(Resources.Load<Material>("Agrou/Mats/Herbe")); grass.color = GrassTint(i); }
                if (Resources.Load<Material>("Agrou/Mats/Herbe")) r.sharedMaterials = ms.Select(_ => grass).ToArray(); else r.enabled = false;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                continue;
            }
            if (ms.Any(x => x.name == "None")) r.sharedMaterials = ms.Select(x => x.name == "None" ? Ground(i) : x).ToArray();   // terrain
        }
        // Vallee asiatique : son herbe venait du terrain d'Agrou (non exporte) ; on regarnit la prairie autour du camp.
        if (All[i].id == "AsianValley")
        {
            var rng = new System.Random(7);
            string[] grassP = { "SM_Env_Grass_Short_Clump_01", "SM_Env_Grass_Short_Clump_02", "SM_Env_Grass_Med_Clump_01", "SM_Env_Wildflowers_01", "SM_Env_Grass_Short_Clump_03" };
            for (int k = 0; k < 700; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2, r = 9.5f + (float)rng.NextDouble() * 38;
                var prefab2 = Synty.Get(grassP[k % grassP.Length]);
                if (!prefab2) continue;
                var g = Object.Instantiate(prefab2, cur.transform);
                g.transform.position = fire + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                g.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0);
                g.transform.localScale = Vector3.one * (1.2f + (float)rng.NextDouble()) / cur.transform.lossyScale.x;
            }
        }
        // Lampes de la map (lampadaires, torches) pres du feu : allumees la nuit seulement (Forward+ : pas de limite par objet).
        var info = JsonUtility.FromJson<Info>(Resources.Load<TextAsset>("Agrou/Maps/" + All[i].id).text);
        for (int k = 0; info?.lights != null && k < info.lights.Length; k++)
        {
            var l = info.lights[k];
            var t = cur.transform.Find("LIGHT_" + k);
            if (!t || l.type == "SUN" || (t.position - fire).sqrMagnitude > 45 * 45) continue;
            var lt = t.gameObject.AddComponent<Light>();
            lt.type = LightType.Point; lt.range = 9; lt.intensity = 0; lt.shadows = LightShadows.None;
            lt.color = l.color != null && l.color.Length >= 3 ? Color.Lerp(new Color(l.color[0], l.color[1], l.color[2]), new Color(1f, 0.75f, 0.45f), 0.5f) : new Color(1f, 0.75f, 0.45f);
            lt.enabled = false;
            lamps.Add(lt);
        }
    }

    public static bool Active => cur && cur.activeSelf;
    static Material grass;
    static Color GrassTint(int i) => All[i].id switch { "MapNoel" => Board.Hex("dfe8ee"), "Grotte" => Board.Hex("5f6b45"), "Cimetiere" => Board.Hex("5e7f3a"), _ => Board.Hex("6f9c3c") };

    // Sol du terrain par map (dans Agrou, des couches peintes : sable de la place, neige de Noel...).
    static Material Ground(int i)
    {
        string hex = All[i].id switch
        {
            "PlaceDuVillage" => "d9c49a", "MapIlePirate" => "e3cf9e", "MapNoel" => "eef3f7", "Grotte" => "7d746a",
            "Cimetiere" => "6f8a4a", "Foret" => "6b8a3c", "Feerique" => "7fa65a", _ => "7a9a48",
        };
        var m = new Material(Resources.Load<Material>("Lit")) { color = Board.Hex(hex), name = "Sol " + All[i].id };
        m.SetFloat("_Smoothness", 0.05f);
        return m;
    }

    public static void Hide() { if (cur) cur.SetActive(false); }

    // 0 = jour, 1 = nuit.
    public static void Night(float k)
    {
        foreach (var l in lamps) { l.enabled = k > 0.02f; l.intensity = 2.5f * k; }
    }
}
