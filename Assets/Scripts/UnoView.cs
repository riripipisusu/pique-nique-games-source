using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Scene du Uno, facon jeu Uno officiel mais en pique-nique : une grande nappe ronde en vichy dans une clairiere
// a part (tres loin du monde, invisible depuis Croque-Carotte), camera basse et fixe depuis ma place.
// Pas de personnages : chaque adversaire est un grand eventail de cartes debout, en arc face a moi (sa vignette
// est dans l'interface) ; defausse en vrac et pioche epaisse au milieu, grandes fleches du sens du jeu.
public class UnoView : MonoBehaviour
{
    public static readonly Vector3 Center = new Vector3(1500, 0, 1500);
    const float CardW = 0.42f, CardH = 0.65f;
    static readonly Vector3 PileAt = new Vector3(0.12f, 0, 0.5f), DeckAt = new Vector3(-0.85f, 0, 1.05f), RingAt = new Vector3(0, 0.03f, 0.55f);

    class Seat { public Transform fan; }
    readonly List<Seat> seats = new List<Seat>();
    Transform cast, pile, deck, arrows, arrowsBack;
    Material lit, cutout;
    readonly Dictionary<string, Material> cardMats = new Dictionary<string, Material>();
    Uno uno;
    int me;

    public Pose CamPose
    {
        get
        {
            var from = transform.TransformPoint(new Vector3(0, 2.45f, -2.75f));
            return new Pose(from, Quaternion.LookRotation(transform.TransformPoint(new Vector3(0, 0.2f, 0.62f)) - from));
        }
    }

    void Awake()
    {
        transform.position = Center;
        lit = Resources.Load<Material>("Lit");
        cutout = Resources.Load<Material>("LitCutout");
        BuildSet();
        gameObject.SetActive(false);
    }

    Material Mat(string hex, float smooth = 0.15f) { var m = new Material(lit) { color = Board.Hex(hex) }; m.SetFloat("_Smoothness", smooth); return m; }

    GameObject Prim(PrimitiveType t, Vector3 pos, Vector3 scale, Material m, Transform parent, Vector3 euler = default)
    {
        var g = GameObject.CreatePrimitive(t);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.transform.localRotation = Quaternion.Euler(euler);
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
        return g;
    }

    Transform Model(string name, Vector3 pos, float scale, float rot)
    {
        var prefab = Synty.Get(name) ?? Resources.Load<GameObject>("Models/" + name);
        if (!prefab) return null;
        var g = Instantiate(prefab, transform);
        g.transform.localPosition = pos;
        g.transform.localRotation = Quaternion.Euler(0, rot, 0);
        g.transform.localScale = Vector3.one * scale;
        return g.transform;
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

    // --- Decor : prairie, grande nappe ronde, panier, clairiere d'arbres ------------------------------------
    void BuildSet()
    {
        var ground = Prim(PrimitiveType.Cylinder, new Vector3(0, -0.05f, 0), new Vector3(120, 0.05f, 120), Synty.I && Synty.I.ground ? Synty.I.ground : Mat("5f8f3a"), transform);
        ground.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var cloth = Mat("ffffff", 0.05f);
        cloth.SetTexture("_BaseMap", Gingham());
        cloth.SetTextureScale("_BaseMap", new Vector2(9, 9));
        Prim(PrimitiveType.Cylinder, new Vector3(0, 0.01f, 0.5f), new Vector3(6.6f, 0.01f, 6.6f), Mat("9e2a24"), transform);     // ourlet
        Prim(PrimitiveType.Cylinder, new Vector3(0, 0.015f, 0.5f), new Vector3(6.4f, 0.012f, 6.4f), cloth, transform);
        // Lumiere chaude au milieu de la nappe (le halo du jeu Uno, en version gouter au soleil).
        var glow = new GameObject("halo").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = new Vector3(0, 1.6f, 0.6f);
        glow.type = LightType.Point; glow.range = 4.5f; glow.intensity = 2.2f; glow.color = Board.Hex("ffd9a0");
        // Clairiere : herbe et fleurs autour de la nappe, buissons et rochers, arbres en couronne.
        var rng = new System.Random(11);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        Vector3 Around(float r0, float r1) { float a = R(0, Mathf.PI * 2), r = R(r0, r1); return new Vector3(Mathf.Cos(a) * r, 0, 0.5f + Mathf.Sin(a) * r); }
        string[] grass = { "SM_Env_Grass_Short_Clump_01", "SM_Env_Grass_Short_Clump_02", "SM_Env_Grass_Med_Clump_01", "SM_Env_Wildflowers_01", "SM_Env_Wildflowers_02", "SM_Env_Flowers_Flat_01", "SM_Env_Flowers_Flat_02" };
        for (int i = 0; i < 160; i++) Model(grass[i % grass.Length], Around(3.6f, 16), R(0.9f, 1.5f), R(0, 360));
        string[] bushes = { "SM_Env_Bush_01", "SM_Env_Bush_02", "SM_Env_Bush_03", "SM_Env_Grass_Bush_01", "SM_Env_Rock_01", "SM_Env_Rock_02", "SM_Env_Rock_Small_Pile_01" };
        for (int i = 0; i < 26; i++) Model(bushes[i % bushes.Length], Around(8, 18), R(0.8f, 1.3f), R(0, 360));
        string[] trees = { "SM_Env_Tree_Meadow_01", "SM_Env_Tree_Meadow_02", "SM_Env_Tree_Birch_01", "SM_Env_Tree_Birch_02", "SM_Env_Tree_Fruit_01", "SM_Env_Tree_Fruit_02" };
        for (int i = 0; i < 34; i++) Model(trees[i % trees.Length], Around(13, 32), R(0.85f, 1.25f), R(0, 360));
        // Sens du jeu : deux grandes fleches en cercle autour des piles, qui tournent doucement.
        // uno_ring : fleches dans le sens horaire vu du dessus (sens +1 : le tour part vers la gauche) ; uno_ring_ccw : l'inverse.
        arrows = Ring("UI/uno_ring");
        arrowsBack = Ring("UI/uno_ring_ccw");
        pile = new GameObject("defausse").transform; pile.SetParent(transform, false); pile.localPosition = PileAt;
        deck = new GameObject("pioche").transform; deck.SetParent(transform, false); deck.localPosition = DeckAt; deck.localRotation = Quaternion.Euler(0, 24, 0);
        var hit = deck.gameObject.AddComponent<BoxCollider>();   // zone de clic de la pioche (genereuse)
        hit.center = new Vector3(0, 0.1f, 0); hit.size = new Vector3(CardW * 1.3f, 0.35f, CardH * 1.3f);
    }

    Transform Ring(string tex)
    {
        var m = new Material(Resources.Load<Material>("UnoGlow"));
        m.SetTexture("_BaseMap", Resources.Load<Texture2D>(tex));
        var t = Prim(PrimitiveType.Quad, RingAt, new Vector3(2.75f, 2.75f, 1), m, transform, new Vector3(90, 0, 0)).transform;
        t.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return t;
    }

    // --- Cartes --------------------------------------------------------------------------------------------
    public static string FaceCode(int card, int chosenColor) =>
        Uno.IsWild(card) && chosenColor >= 0 ? Uno.Code(card) + "_" + "rygb"[chosenColor] : Uno.Code(card);

    Material CardMat(string code)
    {
        if (cardMats.TryGetValue(code, out var m)) return m;
        m = new Material(cutout);
        m.SetTexture("_BaseMap", Resources.Load<Texture2D>("Uno/" + code));
        m.color = Color.white;
        m.SetFloat("_Smoothness", 0.35f);
        return cardMats[code] = m;
    }

    Transform Card(string code, Transform parent, Vector3 pos, Quaternion rot, float scale = 1)
    {
        var g = Prim(PrimitiveType.Quad, pos, new Vector3(CardW, CardH, 1) * scale, CardMat(code), parent);
        g.transform.localRotation = rot;
        g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g.transform;
    }
    static Quaternion Flat(float yaw) => Quaternion.Euler(90, yaw, 0);

    // --- Places ------------------------------------------------------------------------------------------------
    // Moi en bas (hors champ : ma main est dans l'interface), les autres en arc de gauche a droite dans l'ordre du jeu.
    Vector3 SeatPos(int seat)
    {
        int n = uno.players.Count, k = ((seat - me) % n + n) % n - 1, m = n - 1;
        float a = (180 - (k + 1) * 180f / (m + 1)) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a) * 2.6f, 0, 0.8f + Mathf.Sin(a) * 1.55f);   // assez loin des fleches du centre
    }
    float FanScale => uno.players.Count > 6 ? 0.62f : uno.players.Count > 4 ? 0.78f : 0.9f;

    public void Build(Uno u, IList<string> avatars, int mySeat)
    {
        gameObject.SetActive(true);
        uno = u;
        me = Mathf.Clamp(mySeat, 0, u.players.Count - 1);
        if (cast) Destroy(cast.gameObject);
        cast = new GameObject("eventails").transform;
        cast.SetParent(transform, false);
        seats.Clear();
        for (int i = 0; i < u.players.Count; i++)
        {
            var s = new Seat { fan = new GameObject("eventail " + i).transform };
            s.fan.SetParent(cast, false);
            if (i != me)
            {
                s.fan.localPosition = SeatPos(i) + Vector3.up * CardH * FanScale * 0.55f;
                s.fan.rotation = Quaternion.LookRotation(s.fan.position - CamPose.position) * Quaternion.Euler(-8, 0, 0);   // debout, le dos vers moi
            }
            seats.Add(s);
        }
        Sync();
    }

    public void Hide() { StopAllCoroutines(); Dealing = false; shown = null; HideWheel(); if (cursor) Destroy(cursor.gameObject); cursor = null; cursorColor = -1; gameObject.SetActive(false); }

    // Point d'accroche de la vignette d'un joueur (au-dessus de son eventail).
    public Vector3 TagOf(int seat) => seats[seat].fan.position + Vector3.up * CardH * FanScale * 0.6f;

    // La pioche se souleve et respire quand c'est a moi de piocher ; un clic dessus = piocher (Game).
    public bool DeckReady;
    float deckLift;

    void Update()
    {
        if (uno == null) return;
        bool cw = uno.dir > 0;
        arrows.gameObject.SetActive(cw);
        arrowsBack.gameObject.SetActive(!cw);
        (cw ? arrows : arrowsBack).Rotate(0, 0, (cw ? -1 : 1) * 10 * Time.deltaTime, Space.Self);
        deckLift = Mathf.MoveTowards(deckLift, DeckReady ? 1 : 0, Time.deltaTime * 4);
        deck.localPosition = DeckAt + Vector3.up * deckLift * (0.05f + 0.025f * Mathf.Sin(Time.time * 5));
        UpdateCursor();
    }

    public bool DeckUnder(Ray r) => deck && deck.GetComponent<Collider>() is Collider c && c.Raycast(r, out _, 50);

    // --- Etat affiche ------------------------------------------------------------------------------------------
    public void Sync()
    {
        if (uno == null) return;
        foreach (Transform c in pile) Destroy(c.gameObject);
        foreach (Transform c in deck) Destroy(c.gameObject);
        // Defausse en vrac : les dernieres cartes, chacune de travers ; le joker du dessus prend la couleur choisie.
        int from = Mathf.Max(0, uno.discard.Count - 7);
        for (int i = from; i < uno.discard.Count; i++)
        {
            int card = uno.discard[i];
            bool top = i == uno.discard.Count - 1;
            int h = card * 37 + i * 53;
            var at = new Vector3((h % 17 - 8) * 0.012f, 0.03f + (i - from) * 0.003f, (h % 13 - 6) * 0.012f);
            Card(FaceCode(card, top ? uno.color : -1), pile, at, Flat((h % 70) - 35));
        }
        // Pioche : un vrai paquet, dont l'epaisseur suit le nombre de cartes.
        float th = 0.02f + uno.drawPile.Count * 0.0011f;
        Prim(PrimitiveType.Cube, new Vector3(0, th / 2 + 0.02f, 0), new Vector3(CardW * 0.97f, th, CardH * 0.97f), Mat("f4f1ea", 0.2f), deck);
        Card("back", deck, new Vector3(0, th + 0.021f, 0), Flat(0));
        SyncFans();
    }

    // --- Animations -------------------------------------------------------------------------------------------
    Vector3 HandPos(int seat)
    {
        if (seat != me) return seats[seat].fan.position;
        var cam = CamPose;
        return cam.position + cam.rotation * new Vector3(0, -0.55f, 1.3f);
    }
    Vector3 PilePos => pile.position + Vector3.up * 0.06f;
    Vector3 DeckPos => deck.position + Vector3.up * 0.1f;
    Quaternion FacingCam(Vector3 at) => Quaternion.LookRotation(at - CamPose.position);

    // Vol d'une carte en arc ; "spin" : un demi-tour sur elle-meme en route (carte jouee).
    IEnumerator Fly(string code, Vector3 a, Vector3 b, Quaternion ra, Quaternion rb, float dur, float spin = 0, System.Action done = null)
    {
        var c = Card(code, transform, Vector3.zero, Quaternion.identity);
        c.position = a;
        for (float t = 0; t < 1; t += Time.deltaTime / dur)
        {
            float e = 1 - (1 - t) * (1 - t);
            c.position = Vector3.Lerp(a, b, e) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.35f;
            c.rotation = Quaternion.Slerp(ra, rb, e) * Quaternion.Euler(0, 0, spin * (1 - e));
            yield return null;
        }
        Destroy(c.gameObject);
        done?.Invoke();
    }

    // La carte du dessus de la defausse rebondit a l'arrivee.
    IEnumerator Punch()
    {
        if (pile.childCount == 0) yield break;
        var top = pile.GetChild(pile.childCount - 1);
        var s0 = top.localScale;
        for (float t = 0; t < 1; t += Time.deltaTime / 0.22f)
        {
            if (!top) yield break;
            top.localScale = s0 * (1 + 0.18f * Mathf.Sin(t * Mathf.PI) * (1 - t));
            yield return null;
        }
        if (top) top.localScale = s0;
    }

    // Distribution : 7 tours de table, carte par carte depuis la pioche ; ma main (interface) apparait a la fin.
    public bool Dealing;
    int[] shown;

    void SyncFans()
    {
        for (int i = 0; i < seats.Count; i++)
        {
            var s = seats[i];
            foreach (Transform c in s.fan) Destroy(c.gameObject);
            if (i == me) continue;
            int n = Mathf.Min(uno.players[i].hand.Count, 16);
            if (shown != null) n = Mathf.Min(n, shown[i]);
            float step = Mathf.Min(0.11f, 0.95f / Mathf.Max(1, n));
            for (int k = 0; k < n; k++)
            {
                float t = k - (n - 1) / 2f;
                Card("back", s.fan, new Vector3(t * step * CardW / 0.42f, -Mathf.Abs(t) * 0.004f, -k * 0.004f), Quaternion.Euler(0, 0, -t * 3f), FanScale);
            }
        }
    }

    IEnumerator Deal()
    {
        Dealing = true;
        shown = new int[seats.Count];
        foreach (Transform c in pile) Destroy(c.gameObject);
        SyncFans();
        int n = seats.Count;
        for (int k = 0; k < Uno.HandSize; k++)
            for (int j = 1; j <= n; j++)
            {
                int seat = (uno.dealer + j) % n;
                Sound.I.Play("card", 0.5f, 0.15f);
                StartCoroutine(Fly("back", DeckPos, HandPos(seat), transform.rotation * Flat(24), FacingCam(HandPos(seat)), 0.22f, 0, () => { shown[seat]++; SyncFans(); }));
                yield return new WaitForSeconds(n > 6 ? 0.025f : 0.045f);
            }
        yield return new WaitForSeconds(0.3f);
        shown = null;
        // Premiere carte retournee sur la defausse.
        Sound.I.Play("card", 1, 0.05f);
        yield return Fly(FaceCode(uno.Top, Uno.IsWild(uno.Top) ? uno.color : -1), DeckPos, PilePos, transform.rotation * Flat(24), transform.rotation * Flat(0), 0.3f, 180);
        Dealing = false;
        Sync();
        StartCoroutine(Punch());
    }

    public IEnumerator Play(List<UEvent> evs, System.Action<UEvent> onEvent)
    {
        foreach (var e in evs)
        {
            if (e.type == UEv.Deal) Dealing = true;   // ma main disparait avant meme que l'interface se rafraichisse
            onEvent?.Invoke(e);
            switch (e.type)
            {
                case UEv.Played:
                    Sound.I.Play("card", 1, 0.05f);
                    var from = HandPos(e.seat);
                    yield return Fly(FaceCode(e.card, Uno.IsWild(e.card) ? e.other : -1), from, PilePos,
                        e.seat == me ? FacingCam(from) : FacingCam(from) * Quaternion.Euler(0, 180, 0), transform.rotation * Flat(0), 0.32f, e.seat == me ? 0 : 180);
                    Sync();
                    StartCoroutine(Punch());
                    StartCoroutine(Fx(e.card, e.other));   // passe, inversion, +2, +4 : le symbole jaillit
                    if (Uno.IsWild(e.card) && e.seat != me) yield return RevealColor(e.other);
                    else if (Uno.Kind(e.card) >= Uno.Skip) yield return new WaitForSeconds(0.55f);   // la couleur choisie par l'adversaire
                    break;
                case UEv.Drew:
                    for (int k = 0; k < Mathf.Min(e.count, 4); k++)
                    {
                        Sound.I.Play("card", 0.7f, 0.1f);
                        StartCoroutine(Fly("back", DeckPos, HandPos(e.seat), transform.rotation * Flat(24), FacingCam(HandPos(e.seat)), 0.3f));
                        yield return new WaitForSeconds(0.1f);
                    }
                    yield return new WaitForSeconds(0.25f);
                    Sync();
                    break;
                case UEv.Deal:
                    yield return Deal();
                    break;
                case UEv.ChallengeResult:
                    ChallengeFx(e.other == 1);
                    Sync();
                    break;
                case UEv.Skipped:
                    Sync();
                    StartCoroutine(SkipOn(e.seat));
                    break;
                case UEv.Uno:
                    StartCoroutine(CallUno(e.seat));
                    break;
                case UEv.Swapped:
                case UEv.Rotated:
                    StartCoroutine(SevenZero(e.type == UEv.Swapped));
                    yield return new WaitForSeconds(0.5f);
                    Sync();
                    break;
                default:
                    Sync();
                    break;
            }
        }
    }

    // --- Effets du jeu Uno (modeles et animations d'origine fournis par l'utilisatrice, reassembles par UnoFxSetup :
    //     Resources/UnoFX/<Effet>.prefab, un etat d'Animator par clip). Ils sont concus vus d'en haut (face vers +y,
    //     haut de l'image vers +z) dans une scene ou le symbole fait ~6 unites : on les redresse face a la camera.
    static readonly Dictionary<string, GameObject> fxCache = new Dictionary<string, GameObject>();
    static GameObject FxPrefab(string name)
    {
        if (!fxCache.TryGetValue(name, out var g)) fxCache[name] = g = Resources.Load<GameObject>("UnoFX/" + name);
        return g;
    }
    // Mes effets (passe, UNO) : ma main est dans l'interface, toujours au premier plan ; on les place donc
    // au-dessus d'elle, un peu sous le centre de l'image, pour qu'ils ne passent pas derriere mes cartes.
    Vector3 MyFxPos { get { var c = CamPose; return c.position + c.rotation * new Vector3(0, -0.12f, 1.9f); } }
    static string ColorLetter(int color) => "RYGB"[Mathf.Clamp(color, 0, 3)].ToString();

    // Lance l'animation "state" de l'effet "name" en "at", a l'echelle "scale" (1 unite d'origine = scale metre).
    Transform PlayFx(string name, string state, Vector3 at, float scale, float life = -1)
    {
        var prefab = FxPrefab(name);
        if (!prefab) return null;
        var root = new GameObject("effet " + name).transform;
        root.SetParent(transform, false);
        root.position = at;
        var cam = CamPose;
        root.rotation = Quaternion.LookRotation(cam.rotation * Vector3.up, cam.position - at);
        root.localScale = Vector3.one * scale;
        var g = Instantiate(prefab, root);
        g.transform.localPosition = Vector3.zero;
        g.transform.localRotation = Quaternion.identity;
        var an = g.GetComponent<Animator>();
        an.Play(state, 0, 0);
        an.Update(0);
        float len = life > 0 ? life : an.GetCurrentAnimatorStateInfo(0).length;
        Destroy(root.gameObject, Mathf.Max(0.2f, len) + 0.05f);
        return root;
    }

    // Texture seule (rayons, halo...) sur un quad face a la camera.
    Transform FxQuad(string tex, Vector3 at, float size, Color c, bool additive)
    {
        var m = new Material(Resources.Load<Material>("UnoGlow"));
        m.SetTexture("_BaseMap", Resources.Load<Texture2D>("UnoFX/" + tex));
        m.SetColor("_BaseColor", c);
        if (additive) { m.SetFloat("_Blend", 2); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One); }
        var q = Prim(PrimitiveType.Quad, Vector3.zero, Vector3.one * size, m, transform);
        q.transform.position = at;
        q.transform.rotation = FacingCam(at);
        q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return q.transform;
    }

    static void Fade(Transform root, float a)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials)
            {
                var c = m.GetColor("_BaseColor"); c.a = a; m.SetColor("_BaseColor", c);
            }
    }

    // Cartes speciales : l'effet d'origine a la couleur de la carte, au-dessus de la defausse ; +4 : rayons de soleil.
    IEnumerator Fx(int card, int color)
    {
        int kind = Uno.Kind(card);
        var top = PilePos + Vector3.up * 0.55f;
        if (kind == Uno.Wild4)
        {
            Sound.I.Play("bj_chips", 0.8f);
            var rays = FxQuad("Classic_Drawfour_Sunshine_01", top, 2.2f, new Color(1, 0.85f, 0.4f, 1), true);
            var glow = FxQuad("Classic_Drawfour_Glow", top, 1.3f, new Color(0.8f, 0.5f, 1, 1), true);
            var r = rays.rotation;
            for (float t = 0; t < 1; t += Time.deltaTime / 1.3f)
            {
                rays.rotation = r * Quaternion.Euler(0, 0, t * 90);
                float a = t < 0.15f ? t / 0.15f : t > 0.7f ? 1 - (t - 0.7f) / 0.3f : 1;
                rays.localScale = Vector3.one * 2.2f * (0.7f + 0.3f * Mathf.Min(1, t * 4));
                Fade(rays, a); Fade(glow, a);
                yield return null;
            }
            Destroy(rays.gameObject); Destroy(glow.gameObject);
            yield break;
        }
        string name = kind == Uno.Skip ? "Skip" : kind == Uno.Reverse ? "Reverse" : kind == Uno.Draw2 ? "DrawTwo" : null;
        if (name == null) yield break;
        Sound.I.Play(kind == Uno.Reverse ? "tick" : "bj_chips", 0.8f);
        PlayFx(name, name + "_" + ColorLetter(color), top, 0.14f);
    }

    // Passe son tour : le symbole "passe" (petit) sur l'eventail - ou devant moi - du joueur qui passe.
    IEnumerator SkipOn(int seat)
    {
        PlayFx("Skip", "Skip_" + ColorLetter(uno.color), seat == me ? MyFxPos : HandPos(seat) + Vector3.up * 0.1f, seat == me ? 0.1f : 0.07f);
        yield break;
    }

    // "UNO !" : le mot en relief et ses etoiles, au-dessus du joueur.
    IEnumerator CallUno(int seat)
    {
        var at = seat == me ? MyFxPos : TagOf(seat) + Vector3.up * 0.15f;
        PlayFx("CallUno", "CallUNO", at, 0.12f);
        yield break;
    }

    // Variante 7-0 : le 7 (echange de mains) ou le 0 (toutes les mains tournent) au-dessus de la defausse.
    IEnumerator SevenZero(bool seven)
    {
        var at = PilePos + Vector3.up * 0.55f;
        string c = ColorLetter(uno.color);
        if (!seven) { PlayFx("Zero", "SevenZero_0_" + c, at, 0.13f); yield break; }
        var fx = PlayFx("Seven", "SevenZero_7_" + c, at, 0.13f, 1.4f);   // apparition puis boucle
        yield return new WaitForSeconds(0.22f);
        if (fx) fx.GetComponentInChildren<Animator>().Play("SevenZero_7_" + c + "_Loop", 0, 0);
    }

    // Contestation du +4 : coche (bluff demasque) ou croix (le +4 etait regulier) au-dessus de la defausse.
    public void ChallengeFx(bool caught) => PlayFx("Challenge", caught ? "Succeed" : "Failed", PilePos + Vector3.up * 0.6f, 0.09f);

    // Joueur actif : le curseur triangulaire du jeu Uno, a la couleur du jeu, flotte au-dessus de son eventail.
    Transform cursor;
    Material cursorMat;
    int cursorColor = -1;
    void UpdateCursor()
    {
        int a = uno == null || Dealing ? -1 : uno.Actor;
        bool show = a >= 0 && a != me && a < seats.Count;
        if (!show) { if (cursor) cursor.gameObject.SetActive(false); return; }
        if (!cursor)
        {
            var mesh = Resources.Load<Mesh>("UnoFX/Cursor");
            if (!mesh) return;
            cursor = new GameObject("curseur").transform;
            cursor.SetParent(transform, false);
            var pivot = new GameObject("modele").transform;
            pivot.SetParent(cursor, false);
            pivot.localPosition = -mesh.bounds.center;
            pivot.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            cursorMat = new Material(Shader.Find("PiqueNique/UnoFx"));
            cursorMat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            cursorMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            cursorMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            cursorMat.renderQueue = 3000;
            pivot.gameObject.AddComponent<MeshRenderer>().sharedMaterial = cursorMat;
            float k = 0.22f / Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z);
            cursor.localScale = Vector3.one * k;
        }
        if (cursorColor != uno.color)
        {
            cursorColor = uno.color;
            cursorMat.SetTexture("_MainTex", Resources.Load<Texture2D>("UnoFX/Classic_Cursor_" + new[] { "Red", "Yellow", "Green", "Blue" }[Mathf.Clamp(uno.color, 0, 3)]));
        }
        cursor.gameObject.SetActive(true);
        var at = TagOf(a) + Vector3.up * (0.14f + 0.04f * Mathf.Sin(Time.time * 4));
        cursor.position = at;
        // Le modele est a plat (face +y), pointe vers -x : face a la camera, pointe vers le bas, leger balancement.
        var cam = CamPose;
        cursor.rotation = Quaternion.LookRotation(cam.rotation * Vector3.up, cam.position - at) * Quaternion.Euler(0, -90 + Mathf.Sin(Time.time * 2) * 12, 0);
    }

    // --- Roue des couleurs en 3D : quatre parts au-dessus de la defausse -----------------------------------------
    Transform wheel;
    readonly Transform[] wedges = new Transform[4];
    static readonly string[] WedgeHex = { "e5322d", "f7c315", "3aa84a", "1f6fc5" };
    public bool WheelPicking;
    int hover = -1;

    // Une part de camembert (secteur de cylindre) entre deux angles, dans le plan XZ, epaisseur h.
    static Mesh Wedge(float a0, float a1, float r, float h)
    {
        var v = new List<Vector3>(); var tri = new List<int>();
        const int seg = 14;
        void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) { int i = v.Count; v.AddRange(new[] { p0, p1, p2, p3 }); tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
        Vector3 P(float a, float y) => new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        for (int k = 0; k < seg; k++)
        {
            float u0 = Mathf.Lerp(a0, a1, k / (float)seg), u1 = Mathf.Lerp(a0, a1, (k + 1f) / seg);
            int i = v.Count; v.AddRange(new[] { new Vector3(0, h, 0), P(u1, h), P(u0, h) }); tri.AddRange(new[] { i, i + 1, i + 2 });   // dessus
            i = v.Count; v.AddRange(new[] { Vector3.zero, P(u0, 0), P(u1, 0) }); tri.AddRange(new[] { i, i + 1, i + 2 });              // dessous
            Quad(P(u0, 0), P(u0, h), P(u1, h), P(u1, 0));                                                                            // bord
        }
        Quad(Vector3.zero, new Vector3(0, h, 0), P(a0, h), P(a0, 0));
        Quad(Vector3.zero, P(a1, 0), P(a1, h), new Vector3(0, h, 0));
        var m = new Mesh(); m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    void BuildWheel()
    {
        wheel = new GameObject("roue des couleurs").transform;
        wheel.SetParent(transform, false);
        // Rouge en haut a gauche, jaune en haut a droite, vert en bas a droite, bleu en bas a gauche (comme la carte joker).
        float[] start = { 90, 0, 270, 180 };
        for (int c = 0; c < 4; c++)
        {
            float a0 = start[c] * Mathf.Deg2Rad, a1 = (start[c] + 90) * Mathf.Deg2Rad, mid = (a0 + a1) / 2;
            var g = new GameObject(Uno.ColorNames[c]);
            g.transform.SetParent(wheel, false);
            g.transform.localPosition = new Vector3(Mathf.Cos(mid), 0, Mathf.Sin(mid)) * 0.04f;   // parts legerement ecartees
            var mesh = Wedge(a0, a1, 0.72f, 0.2f);   // camembert epais, presque a plat au-dessus de la defausse
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var col = Board.Hex(WedgeHex[c]);
            var mat = Mat(WedgeHex[c], 0.55f);
            mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", col * 0.35f);
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            g.AddComponent<MeshCollider>().sharedMesh = mesh;
            wedges[c] = g.transform;
        }
        wheel.gameObject.SetActive(false);
    }

    void PlaceWheel()
    {
        // Presque a plat au-dessus de la defausse (comme pose sur la table), a peine penchee vers moi : on voit
        // le dessus et l'epaisseur, et elle reste sous les vignettes des joueurs.
        wheel.position = PilePos + Vector3.up * 0.2f;
        var away = Vector3.ProjectOnPlane(wheel.position - CamPose.position, Vector3.up).normalized;
        wheel.rotation = Quaternion.LookRotation(away) * Quaternion.Euler(-14, 0, 0);
    }

    public void ShowWheelPick()
    {
        if (!wheel) BuildWheel();
        PlaceWheel();
        foreach (var w in wedges) { w.localScale = Vector3.one; w.gameObject.SetActive(true); }
        wheel.gameObject.SetActive(true);
        WheelPicking = true;
        StartCoroutine(Pop(wheel, 0.25f));
    }

    public int WheelClosedFrame = -1;
    public void HideWheel() { if (WheelPicking) WheelClosedFrame = Time.frameCount; WheelPicking = false; hover = -1; if (wheel) wheel.gameObject.SetActive(false); }

    public int WedgeUnder(Ray r)
    {
        if (!wheel || !wheel.gameObject.activeSelf) return -1;
        for (int c = 0; c < 4; c++)
            if (wedges[c].GetComponent<Collider>().Raycast(r, out _, 50)) return c;
        return -1;
    }

    IEnumerator Pop(Transform t, float dur)
    {
        for (float k = 0; k < 1; k += Time.deltaTime / dur)
        {
            float e = 1 + 2.7f * Mathf.Pow(k - 1, 3) + 1.7f * Mathf.Pow(k - 1, 2);   // ease-out-back
            t.localScale = Vector3.one * e;
            yield return null;
        }
        t.localScale = Vector3.one;
    }

    // Un adversaire a pose un joker : la roue surgit, la couleur choisie monte et grossit, les autres s'effacent.
    IEnumerator RevealColor(int c)
    {
        if (c < 0) yield break;
        if (!wheel) BuildWheel();
        PlaceWheel();
        wheel.gameObject.SetActive(true);
        foreach (var w in wedges) { w.localScale = Vector3.one; w.gameObject.SetActive(true); }
        yield return Pop(wheel, 0.2f);
        Sound.I.Play("tick");
        for (float t = 0; t < 1; t += Time.deltaTime / 0.35f)
        {
            for (int k = 0; k < 4; k++)
            {
                wedges[k].localScale = Vector3.one * (k == c ? 1 + 0.3f * t : 1 - 0.7f * t);
                var p = wedges[k].localPosition; p.y = k == c ? 0.2f * t : 0; wedges[k].localPosition = p;
            }
            yield return null;
        }
        yield return new WaitForSeconds(0.45f);
        foreach (var w in wedges) { var p = w.localPosition; p.y = 0; w.localPosition = p; }
        wheel.gameObject.SetActive(false);
    }

    // Survol pendant mon choix : la part sous la souris se souleve.
    void LateUpdate()
    {
        if (!WheelPicking || !wheel) return;
        var cam = Camera.main;
        hover = cam ? WedgeUnder(cam.ScreenPointToRay(Input.mousePosition)) : -1;
        for (int k = 0; k < 4; k++)
        {
            var p = wedges[k].localPosition;
            p.y = Mathf.MoveTowards(p.y, k == hover ? 0.16f : 0, Time.deltaTime * 1.2f);   // la part survolee monte
            wedges[k].localPosition = p;
        }
    }
}
