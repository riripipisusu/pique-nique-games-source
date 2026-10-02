using UnityEngine;

// Clairiere du pique-nique, a part du monde (tres loin, invisible depuis Croque-Carotte) : prairie, grande nappe
// ronde en vichy, lumiere de gouter, herbes, buissons et arbres en couronne. Le Uno et les petits chevaux s'y jouent.
public class Clairiere : MonoBehaviour
{
    public static readonly Vector3 Center = new Vector3(1500, 0, 1500);
    static Clairiere instance;

    public static void Show(bool on)
    {
        if (!instance) { if (!on) return; instance = new GameObject("Clairiere").AddComponent<Clairiere>(); Fog(true); return; }
        if (instance.gameObject.activeSelf != on) Fog(on);
        instance.gameObject.SetActive(on);
        instance.SetCamp(false);
        Nature(true);
        AgrouMap.Hide();
    }

    // Loup-garou dans une map d'Agrou : la prairie (sol, herbes, arbres) et le decor du camp s'effacent, le feu reste.
    static Vector2 fogDay = new Vector2(14, 50);
    public static void Nature(bool on)
    {
        if (!instance) return;
        fogDay = on ? new Vector2(14, 50) : new Vector2(60, 260);   // map d'Agrou : on voit le village, pas un mur de brume
        foreach (Transform t in instance.transform) if (t.gameObject != instance.picnic && t.gameObject != instance.camp) t.gameObject.SetActive(on);
        instance.campDecor.SetActive(on);
    }

    // Loup-garou : pas de nappe (enroulee dans un coin), un feu de camp au milieu.
    public static void Camp(bool on) { if (instance) instance.SetCamp(on); }
    public static void HideFire(bool hidden) { if (instance && instance.camp) instance.camp.SetActive(!hidden); }
    GameObject picnic, camp, campDecor;
    Light fire;
    void SetCamp(bool on) { picnic.SetActive(!on); camp.SetActive(on); }
    AudioSource crackle;
    // Nuit du Loup-garou : 0 = jour, 1 = nuit noire ou seul le feu eclaire. Le jeu donne la cible, on y glisse en ~2 s.
    public static float Darkness;
    static float dark;
    void Update()
    {
        if (!camp.activeSelf) return;
        dark = Mathf.MoveTowards(dark, Darkness, Time.deltaTime * 0.33f);   // ~3 s de crepuscule
        fire.intensity = Mathf.Lerp(3f, 7f, dark) + Mathf.PerlinNoise(Time.time * 6, 0) * Mathf.Lerp(1.6f, 3f, dark);
        fire.range = Mathf.Lerp(7, 12, dark);
        crackle.volume = Mathf.Lerp(0.35f, 0.6f, dark) * Sound.I.SfxVolume;
        ApplyNight();
        AgrouMap.Night(dark);
    }

    static void ApplyNight()
    {
        var f = savedFog;
        if (RenderSettings.sun) { RenderSettings.sun.intensity = Mathf.Lerp(f.sunI, 0.06f, dark); RenderSettings.sun.color = Color.Lerp(f.sunC, Board.Hex("8fa6ff"), dark); }
        RenderSettings.ambientSkyColor = Color.Lerp(f.amb0, Board.Hex("1c2547"), dark);
        RenderSettings.ambientEquatorColor = Color.Lerp(f.amb1, Board.Hex("121830"), dark);
        RenderSettings.ambientGroundColor = Color.Lerp(f.amb2, Board.Hex("07080e"), dark);
        var c = Color.Lerp(Board.Hex("c9dde0"), Board.Hex("070b18"), dark);
        RenderSettings.fogColor = c; RenderSettings.fogStartDistance = Mathf.Lerp(fogDay.x, 6, dark); RenderSettings.fogEndDistance = Mathf.Lerp(fogDay.y, 28, dark);
        if (Camera.main) Camera.main.backgroundColor = c;
    }

    Material lit;

    Material Mat(string hex, float smooth = 0.15f) { var m = new Material(lit) { color = Board.Hex(hex) }; m.SetFloat("_Smoothness", smooth); return m; }

    Transform parent;
    GameObject Prim(PrimitiveType t, Vector3 pos, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(t);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent ? parent : transform, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    void Model(string name, Vector3 pos, float scale, float rot)
    {
        var prefab = Synty.Get(name) ?? Resources.Load<GameObject>("Models/" + name);
        if (!prefab) return;
        var g = Instantiate(prefab, parent ? parent : transform);
        g.transform.localPosition = pos;
        g.transform.localRotation = Quaternion.Euler(0, rot, 0);
        g.transform.localScale = Vector3.one * scale;
    }

    // Vichy rouge : trois tons (rouge plein au croisement, rose sur les bandes, blanc casse ailleurs).
    static Texture2D Gingham()
    {
        const int N = 64;
        var t = new Texture2D(N, N, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 8 };
        Color red = Board.Hex("c9372f"), pink = Board.Hex("e98a80"), white = Board.Hex("f8efe2");
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                bool a = x < N / 2, b = y < N / 2;
                t.SetPixel(x, y, a && b ? red : a || b ? pink : white);
            }
        t.Apply();
        return t;
    }

    void Awake()
    {
        transform.position = Center;
        lit = Resources.Load<Material>("Lit");
        var ground = Prim(PrimitiveType.Cylinder, new Vector3(0, -0.05f, 0), new Vector3(120, 0.05f, 120), Synty.I && Synty.I.ground ? Synty.I.ground : Mat("5f8f3a"));
        ground.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var cloth = Mat("ffffff", 0.05f);
        cloth.SetTexture("_BaseMap", Gingham());
        cloth.SetTextureScale("_BaseMap", new Vector2(9, 9));
        picnic = new GameObject("nappe"); picnic.transform.SetParent(transform, false); parent = picnic.transform;
        Prim(PrimitiveType.Cylinder, new Vector3(0, 0.01f, 0.5f), new Vector3(6.6f, 0.01f, 6.6f), Mat("9e2a24"));     // ourlet
        Prim(PrimitiveType.Cylinder, new Vector3(0, 0.015f, 0.5f), new Vector3(6.4f, 0.012f, 6.4f), cloth);
        // Lumiere chaude au milieu de la nappe (le halo du jeu Uno, en version gouter au soleil).
        var glow = new GameObject("halo").AddComponent<Light>();
        glow.transform.SetParent(picnic.transform, false);
        glow.transform.localPosition = new Vector3(0, 1.6f, 0.6f);
        glow.type = LightType.Point; glow.range = 4.5f; glow.intensity = 2.2f; glow.color = Board.Hex("ffd9a0");
        // Feu de camp (Loup-garou) : pierres, buches, flammes, lumiere qui vacille ; la nappe roulee pres d'un rocher.
        camp = new GameObject("feu de camp"); camp.transform.SetParent(transform, false); parent = camp.transform;
        Model("SM_Prop_Camp_Fireplace_Stones_01", new Vector3(0, 0, 0.5f), 1.3f, 0);
        Model("SM_Prop_Camp_Fireplace_01", new Vector3(0, 0, 0.5f), 1.3f, 30);
        Model("FX_Fire_01", new Vector3(0, 0.1f, 0.5f), 1f, 0);
        fire = new GameObject("flammes").AddComponent<Light>();
        fire.transform.SetParent(camp.transform, false);
        fire.transform.localPosition = new Vector3(0, 0.7f, 0.5f);
        fire.type = LightType.Point; fire.range = 7f; fire.color = Board.Hex("ff9a3c"); fire.shadows = LightShadows.Soft;
        campDecor = new GameObject("decor"); campDecor.transform.SetParent(camp.transform, false); parent = campDecor.transform;
        var roll = Prim(PrimitiveType.Cylinder, new Vector3(3.3f, 0.13f, 3.4f), new Vector3(0.26f, 0.55f, 0.26f), cloth);
        roll.transform.localRotation = Quaternion.Euler(0, -40, 90);
        cloth.SetTextureScale("_BaseMap", new Vector2(9, 9));
        Model("SM_Env_Rock_02", new Vector3(3.9f, 0, 3.9f), 0.8f, 120);
        Model("log", new Vector3(-3.4f, 0, 3.2f), 1.4f, 70);
        crackle = camp.AddComponent<AudioSource>();
        crackle.clip = Resources.Load<AudioClip>("Audio/wg/wg_feudecamp"); crackle.loop = true; crackle.playOnAwake = true;
        camp.SetActive(false);
        parent = null;
        // Herbe et fleurs autour de la nappe, buissons et rochers, arbres en couronne.
        var rng = new System.Random(11);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        Vector3 Around(float r0, float r1) { float a = R(0, Mathf.PI * 2), r = R(r0, r1); return new Vector3(Mathf.Cos(a) * r, 0, 0.5f + Mathf.Sin(a) * r); }
        string[] grass = { "SM_Env_Grass_Short_Clump_01", "SM_Env_Grass_Short_Clump_02", "SM_Env_Grass_Med_Clump_01", "SM_Env_Wildflowers_01", "SM_Env_Wildflowers_02", "SM_Env_Flowers_Flat_01", "SM_Env_Flowers_Flat_02" };
        for (int i = 0; i < 160; i++) Model(grass[i % grass.Length], Around(3.6f, 16), R(0.9f, 1.5f), R(0, 360));
        string[] bushes = { "SM_Env_Bush_01", "SM_Env_Bush_02", "SM_Env_Bush_03", "SM_Env_Grass_Bush_01", "SM_Env_Rock_01", "SM_Env_Rock_02", "SM_Env_Rock_Small_Pile_01" };
        for (int i = 0; i < 26; i++) Model(bushes[i % bushes.Length], Around(8, 18), R(0.8f, 1.3f), R(0, 360));
        string[] trees = { "SM_Env_Tree_Meadow_01", "SM_Env_Tree_Meadow_02", "SM_Env_Tree_Birch_01", "SM_Env_Tree_Birch_02", "SM_Env_Tree_Fruit_01", "SM_Env_Tree_Fruit_02" };
        for (int i = 0; i < 34; i++) Model(trees[i % trees.Length], Around(13, 32), R(0.85f, 1.25f), R(0, 360));
        // Foret profonde : plusieurs rangees serrees d'arbres plus grands, et des buissons dans les trous, jusqu'au bout
        // du sol ; la brume (voir Show) les fond au loin. On ne voit plus le bord du monde.
        for (int i = 0; i < 150; i++) Model(trees[i % trees.Length], Around(30, 58), R(1.2f, 1.9f), R(0, 360));
        for (int i = 0; i < 70; i++) Model(bushes[i % 3], Around(24, 55), R(1.4f, 2.4f), R(0, 360));
    }

    // Brume de la clairiere, seulement pendant qu'elle est affichee (les autres decors gardent la leur).
    // Le bas du ciel (cache ailleurs par les collines) passe a la couleur de la brume : la foret s'y fond.
    static (bool on, FogMode mode, Color color, float start, float end, Color skyLow, Color skyTop, float sunI, Color sunC, Color amb0, Color amb1, Color amb2) savedFog;
    static void Fog(bool on)
    {
        var sky = RenderSettings.skybox;
        bool hasSky = sky && sky.HasProperty("_ColorBottom");
        if (on)
        {
            savedFog = (RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogColor, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance,
                        hasSky ? sky.GetColor("_ColorBottom") : Color.white, hasSky ? sky.GetColor("_ColorTop") : Color.white,
                        RenderSettings.sun ? RenderSettings.sun.intensity : 1, RenderSettings.sun ? RenderSettings.sun.color : Color.white,
                        RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor);
            var haze = Board.Hex("c9dde0");
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = haze; RenderSettings.fogStartDistance = 14; RenderSettings.fogEndDistance = 50;   // fondu total avant le bord du sol (60 m)
            // Tout le ciel prend la couleur de la brume (un peu plus bleu en haut) : les trous entre les feuillages s'y fondent.
            if (hasSky) { sky.SetColor("_ColorBottom", haze); sky.SetColor("_ColorTop", Board.Hex("a9c9e2")); }
            // Pas de ciel du tout : un fond couleur de brume, les trous entre les arbres et le bout du sol s y fondent.
            if (Camera.main) { Camera.main.clearFlags = CameraClearFlags.SolidColor; Camera.main.backgroundColor = haze; }
        }
        else
        {
            Darkness = dark = 0; ApplyNight();   // remet soleil et ambiance du jour
            (RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogColor, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance, _, _, _, _, _, _, _) = savedFog;
            if (hasSky) { sky.SetColor("_ColorBottom", savedFog.skyLow); sky.SetColor("_ColorTop", savedFog.skyTop); }
            if (Camera.main) Camera.main.clearFlags = CameraClearFlags.Skybox;
        }
    }
}
