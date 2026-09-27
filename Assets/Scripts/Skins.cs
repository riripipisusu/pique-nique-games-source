using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Personnages Synty (packs Fantasy Characters, City, Farm ; cf. Editor/CharSetup) animes par les clips Mixamo.
// Un personnage = "Pack/Maillage/Variante" : la variante choisit l'atlas de couleurs (01..05 = palette, A/B/C = teint).
public static class Chars
{
    public const string Default = "City/Character_Male_Hoodie/01_A";

    public static readonly (string pack, string mesh, string label)[] Models =
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
        if (Sidekick.IsLook(id) || Mix.IsLook(id)) return "Personnalisé";
        if (Friends.ContainsKey(id)) return id.Substring(4);   // "Ami_Roux" -> "Roux"
        var p = Resolve(id).Split('/');
        var m = Models.FirstOrDefault(x => p.Length > 1 && x.mesh == p[1]);
        return m.label ?? id;
    }

    public static string PortraitName(string id) => id.Replace('/', '_');

    public static bool Valid(string id) => System.Array.IndexOf(Game.Characters, id) >= 0 || Mix.IsLook(id) || (Sidekick.IsLook(id) && Sidekick.Available);

    // Portrait : image precalculee (Resources/Portraits) ou, pour un personnage cree, rendu a la volee (mis en cache).
    static readonly Dictionary<string, Texture2D> shots = new Dictionary<string, Texture2D>();
    public static Texture2D Portrait(string id)
    {
        if (!Sidekick.IsLook(id) && !Mix.IsLook(id)) return Resources.Load<Texture2D>("Portraits/" + PortraitName(id));
        if (shots.TryGetValue(id, out var tex) && tex) return tex;
        var stage = new GameObject("PortraitStudio").transform;
        stage.position = new Vector3(0, -400, 0);
        var who = Spawn(id, stage, Vector3.zero, 180, out var an);
        var head = an.GetBoneTransform(HumanBodyBones.Head).position + Vector3.up * 0.1f;
        var cam = new GameObject("PortraitCam").AddComponent<Camera>();
        cam.transform.SetParent(stage, false);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.fieldOfView = 22;
        cam.nearClipPlane = 0.05f;
        cam.transform.position = head + new Vector3(0, 0.03f, -1.6f);
        cam.transform.LookAt(head);
        var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        rt.Release();
        Object.Destroy(stage.gameObject);
        return shots[id] = tex;
    }

    public static Transform Spawn(string id, Transform parent, Vector3 localPos, float rotY, out Animator an, float height = 1.8f)
    {
        GameObject g;
        float size;
        if (Sidekick.IsLook(id) && Sidekick.Available)
        {
            var look = Sidekick.Look.Decode(id);
            g = Sidekick.Build(look, parent);
            var headMesh = g.GetComponentInChildren<SkinnedMeshRenderer>();   // la tete porte le squelette : son maillage donne le haut du crane (sans coiffure ni chapeau)
            size = (headMesh.bounds.max.y - g.transform.position.y) * 100f / Mathf.Clamp(look.height, 80, 120);
        }
        else if (Mix.IsLook(id))
        {
            var look = Mix.Look.Decode(id);
            g = Mix.Build(look, parent);
            var rs = g.GetComponentsInChildren<SkinnedMeshRenderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            size = b.size.y * 100f / Mathf.Clamp(look.height, 80, 120);
        }
        else
        {
            g = SpawnSynty(id, parent, out var body);
            size = body.bounds.size.y;
        }
        g.transform.localPosition = localPos;
        g.transform.localRotation = Quaternion.Euler(0, rotY, 0);
        g.transform.localScale *= height / Mathf.Max(0.01f, size);
        an = g.GetComponent<Animator>();
        an.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("CharAnim");
        an.applyRootMotion = false;
        an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        return g.transform;
    }

    static GameObject SpawnSynty(string id, Transform parent, out SkinnedMeshRenderer body)
    {
        var p = Resolve(id).Split('/');
        var prefab = p.Length == 3 ? Resources.Load<GameObject>("Chars/" + p[0]) : null;
        var mat = p.Length == 3 ? Resources.Load<Material>($"CharMats/{p[0]}_{p[2]}") : null;
        if (!prefab || !mat) { p = Default.Split('/'); prefab = Resources.Load<GameObject>("Chars/" + p[0]); mat = Resources.Load<Material>($"CharMats/{p[0]}_{p[2]}"); }
        var g = Object.Instantiate(prefab, parent);
        body = null;
        foreach (var s in g.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            bool on = s.name == p[1];
            s.gameObject.SetActive(on);
            if (on) { s.sharedMaterial = mat; body = s; }
        }
        foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;   // armes et accessoires du pack
        return g;
    }
}
