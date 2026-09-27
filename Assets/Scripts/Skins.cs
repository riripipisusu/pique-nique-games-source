using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Personnages Synty (packs Fantasy Characters, City, Farm ; cf. Editor/CharSetup) animes par les clips Mixamo.
// Un personnage = "Pack/Maillage/Variante" : la variante choisit l'atlas de couleurs (01..05 = palette, A/B/C = teint).
public static class Chars
{
    public const string Default = "City/Character_Male_Hoodie/01_A";

    static readonly (string pack, string mesh, string label)[] Models =
    {
        ("City", "Character_Male_Hoodie", "Sweat à capuche"), ("City", "Character_Male_Jacket", "Veste (H)"),
        ("City", "Character_Female_Jacket", "Veste (F)"), ("City", "Character_Female_Coat", "Manteau"),
        ("City", "Character_BusinessMan_Shirt", "Employé"), ("City", "Character_BusinessMan_Suit", "Homme d'affaires"),
        ("City", "Character_BusinessWoman", "Femme d'affaires"), ("City", "Character_Female_Police", "Policière"),
        ("City", "Character_Male_Police", "Policier"),
        ("Fantasy", "Character_Female_Peasant_01", "Paysanne"), ("Fantasy", "Character_Female_Peasant_02", "Villageoise"),
        ("Fantasy", "Character_Male_Peasant_01", "Paysan"), ("Fantasy", "Character_Male_Baird", "Barde"),
        ("Fantasy", "Character_Male_Rouge_01", "Voleur"), ("Fantasy", "Character_Female_Gypsy", "Bohémienne"),
        ("Fantasy", "Character_Female_Druid", "Druidesse"), ("Fantasy", "Character_Female_Witch", "Sorcière"),
        ("Fantasy", "Character_Male_Sorcerer", "Sorcier"), ("Fantasy", "Character_Male_Wizard", "Magicien"),
        ("Fantasy", "Character_Female_Queen", "Reine"), ("Fantasy", "Character_Male_King", "Roi"),
        ("Farm", "SM_Chr_FarmBoy_01", "Jeune fermier"), ("Farm", "SM_Chr_FarmGirl_01", "Jeune fermière"),
        ("Farm", "SM_Chr_Farmer_Female_01", "Fermière"), ("Farm", "SM_Chr_Farmer_Male_01", "Fermier"),
        ("Farm", "SM_Chr_Farmer_Male_Old_01", "Vieux fermier"), ("Farm", "SM_Chr_Scarecrow_01", "Épouvantail"),
    };

    // La bande d'amis (d'apres leurs photos).
    public static readonly Dictionary<string, string> Friends = new Dictionary<string, string>
    {
        ["Ami_Caramel"] = "City/Character_Female_Jacket/03_A",
        ["Ami_Brune"] = "City/Character_BusinessWoman/01_B",
        ["Ami_Platine"] = "City/Character_Female_Jacket/01_A",
        ["Ami_Brun"] = "City/Character_BusinessMan_Shirt/01_A",
        ["Ami_Roux"] = "City/Character_Male_Jacket/02_A",
    };

    // Au choix : deux variantes (palette et teint differents) par modele City/Fantasy, une pour la ferme.
    public static readonly string[] All = Models.SelectMany((m, i) =>
    {
        int pal = m.pack == "Fantasy" ? 5 : 4;
        var a = $"{m.pack}/{m.mesh}/0{1 + i % pal}_{(i % 2 == 0 ? "A" : "B")}";
        var b = $"{m.pack}/{m.mesh}/0{1 + (i + 2) % pal}_C";
        return m.pack == "Farm" ? new[] { a } : new[] { a, b };
    }).ToArray();

    static string Resolve(string id) => Friends.TryGetValue(id, out var f) ? f : id;

    public static string Label(string id)
    {
        if (Friends.ContainsKey(id)) return id.Substring(4);   // "Ami_Roux" -> "Roux"
        var p = Resolve(id).Split('/');
        var m = Models.FirstOrDefault(x => p.Length > 1 && x.mesh == p[1]);
        return m.label ?? id;
    }

    public static string PortraitName(string id) => id.Replace('/', '_');

    public static Transform Spawn(string id, Transform parent, Vector3 localPos, float rotY, out Animator an, float height = 1.8f)
    {
        var p = Resolve(id).Split('/');
        var prefab = p.Length == 3 ? Resources.Load<GameObject>("Chars/" + p[0]) : null;
        var mat = p.Length == 3 ? Resources.Load<Material>($"CharMats/{p[0]}_{p[2]}") : null;
        if (!prefab || !mat) { p = Default.Split('/'); prefab = Resources.Load<GameObject>("Chars/" + p[0]); mat = Resources.Load<Material>($"CharMats/{p[0]}_{p[2]}"); }
        var g = Object.Instantiate(prefab, parent);
        SkinnedMeshRenderer body = null;
        foreach (var s in g.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            bool on = s.name == p[1];
            s.gameObject.SetActive(on);
            if (on) { s.sharedMaterial = mat; body = s; }
        }
        foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;   // armes et accessoires du pack
        g.transform.localPosition = localPos;
        g.transform.localRotation = Quaternion.Euler(0, rotY, 0);
        g.transform.localScale *= height / Mathf.Max(0.01f, body.bounds.size.y);
        an = g.GetComponent<Animator>();
        an.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("CharAnim");
        an.applyRootMotion = false;
        an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        return g.transform;
    }
}
