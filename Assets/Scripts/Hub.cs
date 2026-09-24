using UnityEngine;

// Le coin pique-nique de l'ecran d'accueil : nappe, panier, gouter, jeux de societe et amis.
public class Hub : MonoBehaviour
{
    public static readonly Vector3 Center = new Vector3(25, 0, -3);
    // Vise un peu a gauche de la nappe : la scene apparait a droite, degagee du menu.
    public Vector3 Focus => transform.position + Vector3.up * 0.9f + transform.TransformDirection(new Vector3(Mathf.Sin(-205 * Mathf.Deg2Rad), 0, Mathf.Cos(-205 * Mathf.Deg2Rad))) * -1.6f;

    const float SeatBack = 0.35f; // la caisse est sous le bassin, un peu en arriere des pieds

    Material lit;
    Transform me;

    // La bande d'amis, assise en arc de cercle face a la camera (qui regarde depuis -z).
    static readonly string[] Friends = { "Ami_Caramel", "Ami_Brune", "Ami_Platine", "Ami_Brun", "Ami_Roux" };

    void Awake()
    {
        lit = Resources.Load<Material>("Lit");
        transform.position = Center + Vector3.up * Board.Ground(Center.x, Center.z);
        transform.rotation = Quaternion.Euler(0, -30, 0);
        Build();
    }

    Material Mat(string hex, float smooth = 0.15f) { var m = new Material(lit) { color = Board.Hex(hex) }; m.SetFloat("_Smoothness", smooth); return m; }

    GameObject Prim(PrimitiveType t, Vector3 pos, Vector3 scale, Material m, Vector3 euler = default)
    {
        var g = GameObject.CreatePrimitive(t);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(transform, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.transform.localRotation = Quaternion.Euler(euler);
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    void Model(string name, Vector3 pos, float scale, float rot)
    {
        var prefab = Synty.Get(name) ?? Resources.Load<GameObject>("Models/" + name);
        if (!prefab) { Debug.LogWarning("Modele introuvable : " + name); return; }
        var g = Instantiate(prefab, transform);
        g.transform.localPosition = pos;
        g.transform.localRotation = Quaternion.Euler(0, rot, 0);
        g.transform.localScale = Vector3.one * scale;
    }

    void Build()
    {
        // Nappe a carreaux rouges et blancs
        var red = Mat("d8423a");
        var white = Mat("f6efe2");
        for (int x = 0; x < 8; x++)
            for (int z = 0; z < 8; z++)
                Prim(PrimitiveType.Cube, new Vector3(-1.75f + x * 0.5f, 0.015f, -1.75f + z * 0.5f), new Vector3(0.5f, 0.02f, 0.5f), (x + z) % 2 == 0 ? red : white);

        // Panier en osier
        var wicker = Mat("b07a3c");
        Prim(PrimitiveType.Cylinder, new Vector3(0.9f, 0.18f, -0.3f), new Vector3(0.7f, 0.16f, 0.5f), wicker);
        Prim(PrimitiveType.Cylinder, new Vector3(0.9f, 0.35f, -0.3f), new Vector3(0.74f, 0.02f, 0.54f), Mat("8a5a2b"));
        var handle = Prim(PrimitiveType.Cube, new Vector3(0.9f, 0.55f, -0.3f), new Vector3(0.06f, 0.4f, 0.06f), Mat("8a5a2b"), new Vector3(0, 0, 90));
        handle.transform.localScale = new Vector3(0.06f, 0.6f, 0.06f);
        Model("crop_carrot", new Vector3(0.75f, 0.32f, -0.3f), 1.2f, 30);
        Model("crop_melon", new Vector3(-0.4f, 0.02f, 0.6f), 1.6f, 10);
        Model("crop_pumpkin", new Vector3(1.4f, 0.02f, 0.5f), 1.3f, 70);
        Model("crop_turnip", new Vector3(-0.8f, 0.02f, -0.5f), 1.4f, 200);

        // Boites de jeux de societe et un paquet de cartes
        Prim(PrimitiveType.Cube, new Vector3(-0.1f, 0.07f, -0.3f), new Vector3(0.7f, 0.1f, 0.5f), Mat("f08a24", 0.3f), new Vector3(0, 15, 0));
        Prim(PrimitiveType.Cube, new Vector3(-0.1f, 0.13f, -0.3f), new Vector3(0.72f, 0.03f, 0.52f), Mat("5f9e3c", 0.3f), new Vector3(0, 15, 0));
        Prim(PrimitiveType.Cube, new Vector3(0.05f, 0.22f, -0.25f), new Vector3(0.6f, 0.12f, 0.42f), Mat("1f6b3e", 0.3f), new Vector3(0, -8, 0));
        var deck = Prim(PrimitiveType.Cube, new Vector3(0.3f, 0.04f, 0.3f), new Vector3(0.2f, 0.05f, 0.28f), Mat("f6efe2"), new Vector3(0, 35, 0));
        var back = new Material(lit);
        back.SetTexture("_BaseMap", Resources.Load<Texture2D>("Cards/back_red"));
        var top = Prim(PrimitiveType.Quad, new Vector3(0.3f, 0.066f, 0.3f), new Vector3(0.2f, 0.28f, 1), back, new Vector3(90, 35, 0));

        // Coin de verdure : feu de camp eteint, rochers, champignons, fleurs sauvages et herbe basse.
        Model("SM_Prop_Camp_Fireplace_Stones_01", new Vector3(-3.2f, 0, 2.2f), 1.2f, 0);
        Model("SM_Prop_Camp_Fireplace_01", new Vector3(-3.2f, 0, 2.2f), 1.2f, 30);
        Model("SM_Env_Rock_02", new Vector3(0.3f, 0, 4.2f), 0.9f, 80);
        Model("SM_Env_Rock_01", new Vector3(3.8f, 0, 1.2f), 0.7f, 200);
        Model("SM_Prop_Mushroom_Group_02", new Vector3(2.8f, 0, -1.8f), 2f, 40);
        Model("SM_Prop_Mushroom_Group_03", new Vector3(-2.9f, 0, -1.2f), 2f, 150);
        var rng = new System.Random(3);
        for (int i = 0; i < 40; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2, r = 2.8f + (float)rng.NextDouble() * 4.5f;
            var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            if (p.z < -2 && Mathf.Abs(p.x) < 3) continue; // champ de la camera degage
            Model(i % 3 == 0 ? "SM_Env_Wildflowers_0" + (i / 3 % 3 + 1) : "SM_Env_Grass_Short_Clump_0" + (i % 3 + 1), p, 1f + (float)rng.NextDouble() * 0.5f, i * 47);
        }

        for (int i = 0; i < Friends.Length; i++)
        {
            float ang = (-5 + i * 42) * Mathf.Deg2Rad; // cote oppose a la camera du menu (qui regarde depuis -115 deg)
            var pos = new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang)) * 2.45f;
            float rot = ang * Mathf.Rad2Deg + 180;
            var face = Quaternion.Euler(0, rot, 0);
            Model("SM_Prop_Camp_Crate_01", pos - face * new Vector3(0, 0, SeatBack), 0.6f, rot + 90);
            Chars.Spawn(Friends[i], transform, pos, rot, out var an);
            an.Play("SitDown", 0, 0.95f);
        }
    }

    // Le personnage du joueur, debout au bord de la nappe, qui salue.
    public void SetMe(string avatar)
    {
        if (me) Destroy(me.gameObject);
        float a = -150 * Mathf.Deg2Rad; // debout au bord droit de la nappe
        me = Chars.Spawn(avatar, transform, new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 3.1f, -150 + 180, out var an);
        an.Play("Victory");
    }
}
