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
        arrows = Ring("UnoFX/Board_Arrow");        // fleches officielles (sens horaire vu du dessus)
        arrowsBack = Ring("UnoFX/Board_Arrow_02"); // ... et l'inverse
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

    public void Hide() { StopAllCoroutines(); Dealing = false; shown = null; HideWheel(); foreach (var c in cursors) if (c) Destroy(c.gameObject); cursors.Clear(); cursorColor = -1; gameObject.SetActive(false); }

    // Point d'accroche de la vignette d'un joueur (au-dessus de son eventail).
    public Vector3 TagOf(int seat) => seats[seat].fan.position + Vector3.up * CardH * FanScale * 0.6f;
    // Au-dessus de la vignette (l'interface passe devant la scene : un effet derriere elle serait cache).
    Vector3 AboveTag(int seat) => TagOf(seat) + Vector3.up * 0.42f;

    // La pioche se souleve et respire quand c'est a moi de piocher ; un clic dessus = piocher (Game).
    public bool DeckReady;
    float deckLift, arrowBoost;
    public void ArrowsChange() => arrowBoost = 1;   // changement de sens : les fleches s'emballent un instant

    void Update()
    {
        if (uno == null) return;
        bool cw = uno.dir > 0;
        arrows.gameObject.SetActive(cw);
        arrowsBack.gameObject.SetActive(!cw);
        arrowBoost = Mathf.MoveTowards(arrowBoost, 0, Time.deltaTime * 1.2f);
        (cw ? arrows : arrowsBack).Rotate(0, 0, (cw ? -1 : 1) * (10 + 260 * arrowBoost * arrowBoost) * Time.deltaTime, Space.Self);
        deckLift = Mathf.MoveTowards(deckLift, DeckReady ? 1 : 0, Time.deltaTime * 4);
        deck.localPosition = DeckAt + Vector3.up * deckLift * (0.05f + 0.025f * Mathf.Sin(Time.time * 5));
        UpdateDeckGlow();
        UpdateStack();
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
            int chosen = top ? (jokerPending ? -1 : uno.color) : wildColor.TryGetValue(card, out int wc) ? wc : -1;
            Card(FaceCode(card, chosen), pile, at, Flat((h % 70) - 35));
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
    bool jokerPending;   // joker qui vient d'arriver : encore noir
    readonly Dictionary<int, int> wildColor = new Dictionary<int, int>();   // couleur choisie de chaque joker pose

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
        Sound.I.Play("bj_shuffle", 0.9f, 0.02f);   // on melange avant de distribuer
        wildColor.Clear();
        if (Uno.IsWild(uno.Top)) wildColor[uno.Top] = uno.color;
        shown = new int[seats.Count];
        foreach (Transform c in pile) Destroy(c.gameObject);
        SyncFans();
        int n = seats.Count;
        for (int k = 0; k < Uno.HandSize; k++)
            for (int j = 1; j <= n; j++)
            {
                int seat = (uno.dealer + j) % n;
                Sound.I.Play(Random.value < 0.5f ? "bj_slide1" : "bj_slide2", 0.55f, 0.12f);   // carte distribuee
                StartCoroutine(Fly("back", DeckPos, HandPos(seat), transform.rotation * Flat(24), FacingCam(HandPos(seat)), 0.22f, 0, () => { shown[seat]++; SyncFans(); }));
                yield return new WaitForSeconds(n > 6 ? 0.025f : 0.045f);
            }
        yield return new WaitForSeconds(0.3f);
        shown = null;
        // Premiere carte retournee sur la defausse.
        Sound.I.Play("bj_flip", 0.9f);
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
                    Sound.I.Play(Random.value < 0.5f ? "bj_place1" : "bj_place2", 1, 0.06f);   // carte posee
                    var from = HandPos(e.seat);
                    // Un joker vole en noir ; il prend sa couleur une fois pose (effet "carte joker").
                    yield return Fly(FaceCode(e.card, -1), from, PilePos,
                        e.seat == me ? FacingCam(from) : FacingCam(from) * Quaternion.Euler(0, 180, 0), transform.rotation * Flat(0), 0.32f, e.seat == me ? 0 : 180);
                    if (Uno.IsWild(e.card)) { wildColor[e.card] = e.other; jokerPending = true; Sync(); jokerPending = false; }   // noir d'abord
                    else Sync();
                    StartCoroutine(Punch());
                    StartCoroutine(Fx(e.card, e.other));   // passe, inversion, +2, +4 : le symbole jaillit
                    if (Uno.IsWild(e.card) && e.seat != me) yield return RevealColor(e.other);
                    if (Uno.IsWild(e.card)) yield return JokerColor(e.card, e.other);
                    else if (Uno.Kind(e.card) >= Uno.Skip) yield return new WaitForSeconds(0.55f);   // la couleur choisie par l'adversaire
                    break;
                case UEv.Drew:
                    for (int k = 0; k < Mathf.Min(e.count, 4); k++)
                    {
                        Sound.I.Play(Random.value < 0.5f ? "bj_slide1" : "bj_slide2", 0.8f, 0.1f);   // carte piochee
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
                    if (e.seat == me) StartCoroutine(SkipOn(e.seat));   // les autres : logo sur leur vignette (interface)
                    break;
                case UEv.Uno:
                    UnoAudio.Play(e.seat == me ? "Sfx_Card_UNO_Call" : "Sfx_Card_UNO_Call_NonPlayer");
                    StartCoroutine(CallUno(e.seat));
                    break;
                case UEv.Caught:
                    StartCoroutine(Exclamation(e.seat));
                    Sync();
                    break;
                case UEv.JumpIn:
                    StartCoroutine(JumpInFx(e.seat));
                    Sync();
                    break;
                case UEv.Reversed:
                    ArrowsChange();
                    Sync();
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
    // upright : effet modele debout (face vers -z), comme le mot "UNO", au lieu de a plat.
    Transform PlayFx(string name, string state, Vector3 at, float scale, float life = -1, bool upright = false)
    {
        var prefab = FxPrefab(name);
        if (!prefab) return null;
        var root = new GameObject("effet " + name).transform;
        root.SetParent(transform, false);
        root.position = at;
        var cam = CamPose;
        root.rotation = upright ? Quaternion.LookRotation(at - cam.position, cam.rotation * Vector3.up) : Quaternion.LookRotation(cam.rotation * Vector3.up, cam.position - at);
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
            UnoAudio.Play("Sfx_Card_Function_Draw4");
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
        if (kind == Uno.Skip) UnoAudio.Play("Sfx_Card_Function_Skip");
        else if (kind == Uno.Reverse) { UnoAudio.Play(uno.dir > 0 ? "Sfx_Card_Function_Reverse_Clockwise" : "Sfx_Card_Function_Reverse_Anticlockwise"); UnoAudio.Play("Sfx_Card_Function_Reverse_Bell", 0.7f); }
        else UnoAudio.Play("Sfx_Card_Function_Draw2");
        PlayFx(name, name + "_" + ColorLetter(color), top, 0.14f);
    }

    // Passe son tour : le symbole "passe" (petit) sur l'eventail - ou devant moi - du joueur qui passe.
    IEnumerator SkipOn(int seat)
    {
        UnoAudio.Play("Sfx_Card_Function_Skip_Sub", 0.8f);
        PlayFx("Skip", "Skip_" + ColorLetter(uno.color), seat == me ? MyFxPos : HandPos(seat) + Vector3.up * 0.1f, seat == me ? 0.1f : 0.07f);
        yield break;
    }

    // autotest : UNO et "!" chez moi puis chez un adversaire, une capture pour chacun
    public IEnumerator TestUnoFx(System.Func<string, IEnumerator> shot)
    {
        foreach (int s in new[] { me, (me + 1) % seats.Count })
        {
            StartCoroutine(CallUno(s)); yield return new WaitForSeconds(0.5f); yield return shot("n13-uno-" + s);
            yield return new WaitForSeconds(1.5f);
            StartCoroutine(Exclamation(s)); yield return new WaitForSeconds(0.35f); yield return shot("n14-pas-dit-" + s);
            yield return new WaitForSeconds(1.2f);
        }
        ShowWheelPick(); yield return new WaitForSeconds(0.6f); yield return shot("n15-roue");
        HideWheel(0); yield return new WaitForSeconds(0.35f); yield return shot("n15-roue-rouge");
    }

    // "UNO !" : le mot en relief et ses etoiles, au-dessus du joueur.
    IEnumerator CallUno(int seat)
    {
        var at = seat == me ? MyFxPos : AboveTag(seat);
        PlayFx("CallUno", "CallUNO", at, 0.12f, upright: true);
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

    // Curseurs triangulaires du jeu Uno, a la couleur du jeu : au-dessus du joueur actif ; quand je dois choisir avec
    // qui echanger ma main (le 7), un au-dessus de chaque adversaire (curseurs de choix).
    readonly List<Transform> cursors = new List<Transform>();
    Material cursorMat;
    int cursorColor = -1;
    Transform NewCursor()
    {
        var mesh = Resources.Load<Mesh>("UnoFX/Cursor");
        if (!mesh) return null;
        if (!cursorMat)
        {
            cursorMat = new Material(Shader.Find("PiqueNique/UnoFx"));
            cursorMat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            cursorMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            cursorMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            cursorMat.renderQueue = 3000;
        }
        var c = new GameObject("curseur").transform;
        c.SetParent(transform, false);
        var pivot = new GameObject("modele").transform;
        pivot.SetParent(c, false);
        pivot.localPosition = -mesh.bounds.center;
        pivot.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        pivot.gameObject.AddComponent<MeshRenderer>().sharedMaterial = cursorMat;
        c.localScale = Vector3.one * (0.22f / Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z));
        return c;
    }

    void UpdateCursor()
    {
        var targets = new List<int>();
        if (uno != null && !Dealing)
        {
            if (uno.phase == UPhase.SwapPick && uno.turn == me) targets.AddRange(Enumerable.Range(0, seats.Count).Where(i => i != me));
            else if (uno.Actor >= 0 && uno.Actor != me && uno.Actor < seats.Count) targets.Add(uno.Actor);
        }
        while (cursors.Count < targets.Count) { var c = NewCursor(); if (!c) return; cursors.Add(c); }
        for (int i = 0; i < cursors.Count; i++) cursors[i].gameObject.SetActive(i < targets.Count);
        if (targets.Count == 0) return;
        if (cursorColor != uno.color)
        {
            cursorColor = uno.color;
            cursorMat.SetTexture("_MainTex", Resources.Load<Texture2D>("UnoFX/Classic_Cursor_" + UnoAudio.ColorWord(uno.color)));
        }
        var cam = CamPose;
        for (int i = 0; i < targets.Count; i++)
        {
            var at = TagOf(targets[i]) + Vector3.up * (0.14f + 0.04f * Mathf.Sin(Time.time * 4 + i));
            cursors[i].position = at;
            // Le modele est a plat (face +y), pointe vers -x : face a la camera, pointe vers le bas, leger balancement.
            cursors[i].rotation = Quaternion.LookRotation(cam.rotation * Vector3.up, cam.position - at) * Quaternion.Euler(0, -90 + Mathf.Sin(Time.time * 2 + i) * 12, 0);
        }
    }

    // --- Petits effets : textures d'origine sur des quads face a la camera ------------------------------------
    static readonly Dictionary<string, Texture2D> fxTex = new Dictionary<string, Texture2D>();
    static Texture2D FxTex(string name) { if (!fxTex.TryGetValue(name, out var t)) fxTex[name] = t = Resources.Load<Texture2D>("UnoFX/" + name); return t; }

    Renderer FxBillboard(string tex, Vector3 at, Vector2 size, Color c, bool additive, Transform parent = null)
    {
        var m = new Material(Shader.Find("PiqueNique/UnoFx"));
        if (tex != null) m.SetTexture("_MainTex", FxTex(tex));
        m.SetColor("_Tint", c * 0.5f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        m.renderQueue = 3000 + (additive ? 20 : 10);
        var q = Prim(PrimitiveType.Quad, Vector3.zero, new Vector3(size.x, size.y, 1), m, parent ? parent : transform);
        q.transform.position = at;
        q.transform.rotation = FacingCam(at);
        var r = q.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return r;
    }
    static void Tint(Renderer r, Color c, float a) { if (r) r.material.SetColor("_Tint", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, a * 0.5f)); }

    // Compteur de cumul (+4, +6, +8, +12, +16) au-dessus de la defausse, sur un halo violet qui pulse.
    Renderer stackNum, stackGlow;
    int stackShown;
    void UpdateStack()
    {
        int n = uno.pending;
        bool show = n >= 4 && FxTex(n.ToString()) != null;
        if (!show) { if (stackNum) { Destroy(stackNum.gameObject); Destroy(stackGlow.gameObject); } stackShown = 0; return; }
        var at = PilePos + Vector3.up * 0.55f;
        if (n != stackShown)
        {
            if (stackNum) { Destroy(stackNum.gameObject); Destroy(stackGlow.gameObject); }
            stackGlow = FxBillboard("Steaking_Circle", at, Vector2.one * 0.75f, Color.white, true);
            stackNum = FxBillboard(n.ToString(), at, Vector2.one * 0.7f, Color.white, false);
            stackNum.transform.position += (CamPose.position - at).normalized * 0.02f;
            stackShown = n;
            stackPop = 0;
        }
        stackPop = Mathf.Min(1, stackPop + Time.deltaTime * 4);
        float pop = stackPop < 1 ? 1 + 0.35f * Mathf.Sin(stackPop * Mathf.PI) : 1 + 0.04f * Mathf.Sin(Time.time * 6);
        stackNum.transform.localScale = new Vector3(0.7f, 0.7f, 1) * pop;
        stackGlow.transform.localScale = new Vector3(0.75f, 0.75f, 1) * (1.1f + 0.15f * Mathf.Sin(Time.time * 4));
    }
    float stackPop;

    // Pioche qui brille quand c'est a moi de piocher : anneau lumineux sous le paquet et etincelle au-dessus.
    Renderer deckRing, deckStar;
    float deckIdle;
    void UpdateDeckGlow()
    {
        bool on = DeckReady && !Dealing;
        if (on && !deckRing)
        {
            deckRing = FxBillboard("DrawCard_RingGlow", deck.position, Vector2.one * 0.95f, new Color(1f, 0.92f, 0.55f), true);
            deckRing.transform.rotation = transform.rotation * Quaternion.Euler(90, 0, 0);   // a plat sur la nappe
            deckStar = FxBillboard("Classic_CardGlow_Star", deck.position, Vector2.one * 0.35f, Color.white, true);
            deckIdle = 0;
        }
        if (!on) { if (deckRing) { Destroy(deckRing.gameObject); Destroy(deckStar.gameObject); } return; }
        float k = 0.55f + 0.45f * Mathf.Sin(Time.time * 4);
        deckRing.transform.position = deck.position + Vector3.up * 0.02f;
        Tint(deckRing, new Color(1f, 0.92f, 0.55f), k);
        var top = deck.position + Vector3.up * (0.12f + 0.03f * Mathf.Sin(Time.time * 3));
        deckStar.transform.position = top;
        deckStar.transform.rotation = FacingCam(top) * Quaternion.Euler(0, 0, Time.time * 60);
        Tint(deckStar, Color.white, k);
        deckIdle += Time.deltaTime;
    }

    // Intervention : un anneau blanc s'elargit et la fleche verte "jump in" jaillit au-dessus du joueur.
    IEnumerator JumpInFx(int seat)
    {
        var at = seat == me ? MyFxPos : AboveTag(seat);
        var ring = FxBillboard("right_false", at, Vector2.one * 0.3f, new Color(0.6f, 1f, 0.6f), true);
        var arrow = FxBillboard("jump in", at, Vector2.one * 0.35f, Color.white, false);
        for (float t = 0; t < 1; t += Time.deltaTime / 0.9f)
        {
            ring.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.1f, Mathf.Sqrt(t));
            Tint(ring, new Color(0.6f, 1f, 0.6f), 1 - t);
            float pop = t < 0.25f ? Mathf.Sin(t / 0.25f * Mathf.PI * 0.5f) * 1.2f : Mathf.Lerp(1.2f, 1, (t - 0.25f) * 3);
            arrow.transform.localScale = Vector3.one * 0.35f * Mathf.Max(0.01f, pop);
            arrow.transform.position = at + Vector3.up * 0.12f * t;
            Tint(arrow, Color.white, t > 0.7f ? 1 - (t - 0.7f) / 0.3f : 1);
            yield return null;
        }
        Destroy(ring.gameObject); Destroy(arrow.gameObject);
    }

    // "Tu n'as pas dit UNO !" : le gros "!" rouge du jeu Uno surgit et tremble au-dessus du joueur pris.
    IEnumerator Exclamation(int seat)
    {
        var mesh = Resources.Load<Mesh>("UnoFX/Exclamation_Mesh");
        if (!mesh) yield break;
        var at = seat == me ? MyFxPos : AboveTag(seat) + Vector3.up * 0.2f;   // le "!" est grand : sinon sa base passe sous la vignette
        var root = new GameObject("pas dit UNO").transform;
        root.SetParent(transform, false);
        root.position = at;
        var cam = CamPose;
        root.rotation = Quaternion.LookRotation(cam.rotation * Vector3.up, cam.position - at);   // modele a plat (+y)
        var g = new GameObject("!").transform;
        g.SetParent(root, false);
        float k = 0.42f / Mathf.Max(0.0001f, Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z));
        g.localScale = Vector3.one * k;
        g.localPosition = -mesh.bounds.center * k;
        g.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var m = new Material(Shader.Find("PiqueNique/UnoFx"));
        m.SetTexture("_MainTex", FxTex("Exclamation"));
        m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.renderQueue = 3015;
        var r = g.gameObject.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        var r0 = root.rotation;
        for (float t = 0; t < 1; t += Time.deltaTime / 1.3f)
        {
            float pop = t < 0.15f ? Mathf.Sin(t / 0.15f * Mathf.PI * 0.5f) * 1.3f : Mathf.Lerp(1.3f, 1, Mathf.Clamp01((t - 0.15f) * 5));
            root.localScale = Vector3.one * Mathf.Max(0.01f, pop);
            root.rotation = r0 * Quaternion.AngleAxis(Mathf.Sin(t * 40) * 14 * (1 - t), Vector3.up);
            m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f * (t > 0.75f ? 1 - (t - 0.75f) / 0.25f : 1)));
            yield return null;
        }
        Destroy(root.gameObject);
    }

    // Carte joker : une fois posee, elle prend sa couleur dans un eclair (image d'origine du changement de couleur).
    IEnumerator JokerColor(int card, int color)
    {
        if (color < 0 || pile.childCount == 0) { Sync(); yield break; }
        var top = pile.GetChild(pile.childCount - 1);
        var flash = FxBillboard(null, top.position + Vector3.up * 0.01f, new Vector2(CardW, CardH), Color.white, false);
        flash.material.SetTexture("_MainTex", Resources.Load<Texture2D>("Uno/" + FaceCode(card, color)));   // la carte a sa couleur, fond transparent
        flash.transform.rotation = top.rotation;
        for (float t = 0; t < 1; t += Time.deltaTime / 0.45f)
        {
            float s = 1 + 0.25f * Mathf.Sin(t * Mathf.PI);
            flash.transform.localScale = new Vector3(CardW, CardH, 1) * s;
            Tint(flash, Color.white * (1 + (1 - t)), 1);
            yield return null;
        }
        Sync();   // la defausse montre maintenant le joker a sa couleur
        Destroy(flash.gameObject);
    }

    // --- Roue officielle du joker (modele a squelette et animation du jeu Uno : Resources/UnoFX/Wheel) ------------
    // Animation de 5 s : ouverture (0 - 0,33 s), puis selection du rouge, du jaune, du vert et du bleu (~1 s chacune).
    // Vue de face (verifie a l'ecran) : vert en haut, jaune a droite, rouge en bas, bleu a gauche.
    const float WheelOpen = 0.33f, WheelStep = 0.99f, WheelSelFrom = 0.66f, WheelSelTo = 1.32f;
    static readonly int[] WheelBoneColor = { 0, 1, 2, 3 };
    static readonly string[] WedgeName = { "Wild_Red", "Wild_Yellow", "Wild_Green", "Wild_Blue" };
    static readonly Color[] WedgeColor = { Board.Hex("e5322d"), Board.Hex("f7c315"), Board.Hex("3aa84a"), Board.Hex("1f6fc5") };   // comme l'interface
    static readonly string[] WheelBone = { "Bone_Red_02", "Bone_Yellow_02", "Bone_Green_02", "Bone_Blue_02" };
    Transform wheel;
    public Vector2? TestMouse;   // autotest : pointeur virtuel (on ne simule pas la vraie souris)
    public Vector2 Mouse => TestMouse ?? (Vector2)Input.mousePosition;
    // autotest : point ecran au milieu de la part c (meme repere que WedgeUnder)
    public Vector2 WedgeScreen(int c) => Camera.main.WorldToScreenPoint(wheel.TransformPoint(c == 2 ? new Vector3(0, 0, 3) : c == 0 ? new Vector3(0, 0, -3) : c == 1 ? new Vector3(3, 0, 0) : new Vector3(-3, 0, 0)));
    readonly Transform[] wedgeBones = new Transform[4];
    Animator wheelAn;
    float wheelLen = 5.03f;
    public bool WheelPicking;
    int hover = -1, lastHover = -1;

    void BuildWheel()
    {
        var prefab = Resources.Load<GameObject>("UnoFX/Wheel");
        if (!prefab) return;
        wheel = new GameObject("roue des couleurs").transform;
        wheel.SetParent(transform, false);
        var g = Instantiate(prefab, wheel).transform;
        g.localPosition = Vector3.zero; g.localRotation = Quaternion.identity;
        wheel.localScale = Vector3.one * 0.14f;
        // Deux materiaux comme a l'origine : les 4 parts opaques (sinon le rouge vire au marron sur la nappe),
        // le halo "Wild_glow" en additif, sans profondeur, dessine apres.
        foreach (var r in g.GetComponentsInChildren<Renderer>(true))
        {
            var m = new Material(r.sharedMaterial);
            bool glow = r.name == "Wild_glow";
            m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, glow ? 0.5f : 1f));
            int wc = System.Array.FindIndex(WedgeName, n => r.name == n);
            if (wc >= 0)   // la texture melange deux couleurs par part (le bleu y est turquoise) : couleur Uno franche
            {
                m.SetTexture("_MainTex", Texture2D.whiteTexture);
                m.SetColor("_Tint", WedgeColor[wc] * 0.5f);
            }
            m.SetFloat("_SrcBlend", (float)(glow ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
            m.SetFloat("_DstBlend", (float)(glow ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.Zero));
            m.SetFloat("_ZWrite", glow ? 0 : 1);
            m.renderQueue = glow ? 3010 : 3000;
            r.sharedMaterials = Enumerable.Repeat(m, r.sharedMaterials.Length).ToArray();
        }
        for (int c = 0; c < 4; c++) wedgeBones[c] = g.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == WheelBone[c]);
        wheelAn = g.GetComponent<Animator>();
        if (wheelAn) { wheelAn.speed = 0; var clip = wheelAn.runtimeAnimatorController.animationClips.FirstOrDefault(); if (clip) wheelLen = clip.length; }
        wheel.gameObject.SetActive(false);
    }

    void PlaceWheel()
    {
        // Au-dessus de la defausse, face a la camera (le modele est a plat : +y vers la camera, haut de l'image = +z).
        var at = PilePos + Vector3.up * 0.45f;
        wheel.position = at;
        var cam = CamPose;
        wheel.rotation = Quaternion.LookRotation(cam.rotation * Vector3.up, cam.position - at);
    }

    // Pose de la roue a l'instant t (s) de la prise d'origine : l'animateur est fige (vitesse 0) et positionne a la main.
    void SampleWheel(float t)
    {
        if (!wheelAn) return;
        wheelAn.Play("Roue", 0, Mathf.Clamp01(t / wheelLen));
        wheelAn.Update(0);
    }

    IEnumerator OpenWheel()
    {
        Sound.I.Play("tick");
        for (float t = 0; t < WheelOpen; t += Time.deltaTime) { SampleWheel(t); yield return null; }
        SampleWheel(WheelOpen);
    }

    // La couleur choisie s'avance, les autres s'effacent (segment d'origine de cette couleur).
    IEnumerator ChooseOnWheel(int c)
    {
        float a = WheelSelFrom + c * WheelStep, b = WheelSelTo + c * WheelStep;
        for (float t = a; t < b; t += Time.deltaTime) { SampleWheel(t); yield return null; }
        SampleWheel(b);
        yield return new WaitForSeconds(0.25f);
    }

    public void ShowWheelPick()
    {
        if (!wheel) BuildWheel();
        if (!wheel) return;
        PlaceWheel();
        wheel.gameObject.SetActive(true);
        WheelPicking = true;
        hover = lastHover = -1;
        StartCoroutine(OpenWheel());
    }

    public int WheelClosedFrame = -1;
    // chosen >= 0 : couleur choisie (la part s'avance avant de disparaitre) ; sinon annulation.
    public void HideWheel(int chosen = -1)
    {
        if (WheelPicking) WheelClosedFrame = Time.frameCount;
        bool picked = WheelPicking;
        WheelPicking = false;
        int c = chosen;
        hover = -1;
        if (!wheel) return;
        if (picked && c >= 0) StartCoroutine(CloseAfter(c)); else wheel.gameObject.SetActive(false);
    }
    IEnumerator CloseAfter(int c) { yield return ChooseOnWheel(c); if (!WheelPicking) wheel.gameObject.SetActive(false); }

    // Part visee : position du point vise dans le plan de la roue.
    public int WedgeUnder(Ray r)
    {
        if (!wheel || !wheel.gameObject.activeSelf) return -1;
        var plane = new Plane(wheel.up, wheel.position);
        if (!plane.Raycast(r, out float d)) return -1;
        var local = wheel.InverseTransformPoint(r.GetPoint(d));
        var v = new Vector2(local.x, local.z);
        if (v.magnitude < 0.3f || v.magnitude > 6.5f) return -1;   // (unites du modele d'origine, ~5 de rayon)
        if (Mathf.Abs(v.y) >= Mathf.Abs(v.x)) return v.y > 0 ? 2 : 0;   // (verifie a l'ecran) vert en haut, rouge en bas
        return v.x > 0 ? 1 : 3;                                          // jaune a droite, bleu a gauche
    }

    // Un adversaire a pose un joker : la roue s'ouvre, puis sa couleur s'avance.
    IEnumerator RevealColor(int c)
    {
        if (c < 0) yield break;
        if (!wheel) BuildWheel();
        if (!wheel) yield break;
        PlaceWheel();
        wheel.gameObject.SetActive(true);
        yield return OpenWheel();
        yield return new WaitForSeconds(0.2f);
        yield return ChooseOnWheel(c);
        wheel.gameObject.SetActive(false);
    }

    // Survol pendant mon choix : la part visee grossit (os de la part), petit son a chaque changement.
    void LateUpdate()
    {
        if (!WheelPicking || !wheel) return;
        var cam = Camera.main;
        hover = cam ? WedgeUnder(cam.ScreenPointToRay(Mouse)) : -1;
        if (hover != lastHover && hover >= 0) Sound.I.UI("hover");
        lastHover = hover;
        SampleWheel(WheelOpen);
        for (int k = 0; k < 4; k++) if (wedgeBones[k]) wedgeBones[k].localScale *= k == hover ? 1.18f : 1f;
    }
}
