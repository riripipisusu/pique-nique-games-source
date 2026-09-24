using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Salle de casino et table de blackjack. Rejoue les evenements produits par Blackjack.
// Repere local : origine au milieu du bord plat (cote croupier), joueurs vers -z, tapis a 0.9 m.
public class Table : MonoBehaviour
{
    public static readonly Vector3 Center = new Vector3(0, -300, 400); // sous la prairie : invisible depuis l'exterieur
    const float Top = 0.9f, R = 1.6f;

    public float speed = 1;
    public string back = "back_red";
    public Vector3 Focus => transform.TransformPoint(new Vector3(0, Top, -0.78f));
    public Transform RouletteSpot { get; private set; }   // la table de roulette, ou l'on joue a la Roulette
    public Animator RouletteDealer { get; private set; }

    Material lit;
    readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    Mesh quad, cyl;
    Transform root, shoe;
    Animator dealerAn;
    Blackjack bj;
    int seats;

    class Card { public Transform t; public int card; public bool down; }
    readonly List<Card> dealerCards = new List<Card>();
    readonly Dictionary<int, List<List<Card>>> hands = new Dictionary<int, List<List<Card>>>();
    readonly Dictionary<int, List<int>> stakes = new Dictionary<int, List<int>>();
    readonly Dictionary<int, List<Transform>> stakeGo = new Dictionary<int, List<Transform>>();
    readonly Dictionary<(int, int), string> results = new Dictionary<(int, int), string>();
    readonly List<Transform> clutter = new List<Transform>();

    void Awake()
    {
        lit = Resources.Load<Material>("Lit");
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad); quad = q.GetComponent<MeshFilter>().sharedMesh; Destroy(q);
        var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cyl = c.GetComponent<MeshFilter>().sharedMesh; Destroy(c);
        transform.position = Center;
        BuildRoom();
        BuildTable(transform, true);
        // Tables voisines : roulette et poker, chacune avec son croupier.
        foreach (var (x, table, dealer) in new[] { (-5.2f, "SM_Prop_Roulette_Table_01", "Suit_Female"), (5.2f, "SM_Prop_Poker_Table_01", "OldClassy_Male") })
        {
            var other = new GameObject("AutreTable").transform;
            other.SetParent(transform, false);
            other.localPosition = new Vector3(x, 0, 3.6f);
            bool roulette = table.Contains("Roulette");
            // Roulette : cylindre et croupier cote -z (face a la table de blackjack), joueurs cote +z, devant le tapis imprime.
            other.localRotation = Quaternion.Euler(0, roulette ? 205 : -25, 0);
            Prop(table, Vector3.zero, 0, other);
            Chars.Spawn(dealer, other, roulette ? new Vector3(0.1f, 0, -1.25f) : new Vector3(0, 0, 1.3f), roulette ? 0 : 180, out var an);
            if (roulette) { RouletteSpot = other; RouletteDealer = an; }
            Prop("SM_Prop_Chandelier_01", new Vector3(0, 5.15f, 0), 0, other);
        }
    }

    // --- Materiaux et formes ------------------------------------------------------------
    Material Mat(string key, System.Func<Material> make)
    {
        if (!mats.TryGetValue(key, out var m)) mats[key] = m = make();
        return m;
    }

    Material Color(string hex, float smooth = 0.2f, float metal = 0) => Mat(hex + smooth + metal, () =>
    {
        var m = new Material(lit) { color = Board.Hex(hex) };
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        return m;
    });

    Material Tex(string res, bool cutout = false, float tiling = 1) => Mat(res + tiling, () =>
    {
        var m = new Material(cutout ? Resources.Load<Material>("LitCutout") : lit) { color = UnityEngine.Color.white };
        m.SetTexture("_BaseMap", Resources.Load<Texture2D>(res));
        m.SetTextureScale("_BaseMap", Vector2.one * tiling);
        m.SetFloat("_Smoothness", 0.25f);
        if (cutout)
        {
            m.SetFloat("_AlphaClip", 1);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
        }
        return m;
    });

    Material Glow(string hex) => Mat("glow" + hex, () =>
    {
        var m = new Material(Resources.Load<Material>("LitGlow")) { color = Board.Hex(hex) };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", Board.Hex(hex) * 2.2f);
        return m;
    });

    GameObject Part(Mesh mesh, Vector3 pos, Vector3 scale, Material m, Transform parent, Vector3 euler = default)
    {
        var g = new GameObject("p");
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.transform.localRotation = Quaternion.Euler(euler);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = m;
        return g;
    }

    // Objet du pack Casino Synty (null si le pack n'est pas installe).
    GameObject Prop(string name, Vector3 pos, float rotY, Transform parent, float scale = 1, bool shadows = true)
    {
        var prefab = Synty.Get(name);
        if (!prefab) return null;
        var g = Instantiate(prefab, parent);
        g.transform.localPosition = pos;
        g.transform.localRotation = Quaternion.Euler(0, rotY, 0);
        g.transform.localScale = Vector3.one * scale;
        if (!shadows) foreach (var r in g.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // Pampilles du lustre : leur materiau de verre sort en bloc blanc sous URP, on garde la monture seule.
        foreach (var r in g.GetComponentsInChildren<Renderer>()) if (r.name.EndsWith("_Lights_01")) r.enabled = false;
        return g;
    }

    static Mesh cubeMesh;
    static Mesh Cube()
    {
        if (!cubeMesh) { var c = GameObject.CreatePrimitive(PrimitiveType.Cube); cubeMesh = c.GetComponent<MeshFilter>().sharedMesh; Destroy(c); }
        return cubeMesh;
    }

    // Demi-disque (tapis), UV cales sur la texture : bord plat en haut.
    static Mesh HalfDisc(float r, int seg)
    {
        var v = new List<Vector3> { Vector3.zero };
        var uv = new List<Vector2> { new Vector2(0.5f, 1) };
        var t = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = Mathf.PI + Mathf.PI * i / seg;
            var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            v.Add(p);
            uv.Add(new Vector2((p.x + r) / (2 * r), 1 + p.z / r));
            if (i > 0) { t.Add(0); t.Add(i); t.Add(i + 1); }
        }
        var m = new Mesh();
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals();
        if (m.normals[0].y < 0) { t.Reverse(); m.SetTriangles(t, 0); m.RecalculateNormals(); }
        return m;
    }

    // Tube le long d'un demi-cercle (rail, accoudoir), ou paroi verticale (jupe) si wall.
    static Mesh Arc(float r, float thick, int seg, bool wall, float height = 0)
    {
        var v = new List<Vector3>();
        var nrm = new List<Vector3>();
        var t = new List<int>();
        const int ring = 16;
        int n = wall ? 2 : ring;
        for (int i = 0; i <= seg; i++)
        {
            float a = Mathf.PI + Mathf.PI * i / seg;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            if (wall) { v.Add(dir * r); v.Add(dir * r - Vector3.up * height); nrm.Add(dir); nrm.Add(dir); }
            else
                for (int k = 0; k < ring; k++)
                {
                    float b = k * Mathf.PI * 2 / ring;
                    var nk = dir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    v.Add(dir * r + nk * thick);
                    nrm.Add(nk);
                }
        }
        for (int i = 0; i < seg; i++)
            for (int k = 0; k < (wall ? 1 : ring); k++)
            {
                int a0 = i * n + k, a1 = i * n + (k + 1) % n, b0 = a0 + n, b1 = a1 + n;
                t.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
                t.AddRange(new[] { a0, a1, b0, a1, b1, b0 });   // double face : pas de trou vu de l'autre cote
            }
        // Normales exactes : avec la double face, le calcul automatique s'annulait (facettes noires).
        var m = new Mesh();
        m.SetVertices(v); m.SetNormals(nrm); m.SetTriangles(t, 0);
        return m;
    }

    void BuildTable(Transform t, bool main)
    {
        Part(HalfDisc(R, 48), new Vector3(0, Top, 0), Vector3.one, Tex("Casino/felt"), t);
        // Meuble Synty (bord plat cote croupier, arrondi vers les joueurs), elargi pour notre tapis de 1.6 m de rayon.
        var body = Prop("SM_Prop_Blackjack_Table_01", Vector3.zero, 180, t);
        if (body) body.transform.localScale = new Vector3(1.42f, 0.86f, 1.42f); // son tapis passe juste sous le notre (textes en francais)
        // Rack a jetons et sabot
        Part(Cube(), new Vector3(0, Top + 0.025f, -0.1f), new Vector3(0.9f, 0.05f, 0.16f), Color("241815", 0.5f), t);
        int[] rack = { 5, 10, 50, 100, 500 };
        for (int k = 0; k < 5; k++)
        {
            for (int j = 0; j < 6; j++)
                Part(cyl, new Vector3(-0.34f + k * 0.17f, Top + 0.1f, -0.16f + j * 0.022f), new Vector3(0.085f, 0.01f, 0.085f), ChipSide(rack[k]), t, new Vector3(90, 0, 0));
            // Face du premier jeton de chaque pile, tournee vers les joueurs : meme motif que leurs jetons.
            Part(quad, new Vector3(-0.34f + k * 0.17f, Top + 0.1f, -0.1705f), Vector3.one * 0.085f, Tex("Casino/chip_" + rack[k], true), t);
        }
        var s = Part(Cube(), new Vector3(1.05f, Top + 0.07f, -0.28f), new Vector3(0.2f, 0.14f, 0.3f), Color("15100e", 0.7f), t, new Vector3(0, -25, 0)).transform;
        Part(Cube(), new Vector3(1.05f, Top + 0.1f, -0.28f), new Vector3(0.14f, 0.1f, 0.26f), Tex("Cards/back_red"), t, new Vector3(0, -25, 0));
        if (main) shoe = s;
    }

    Material ChipSide(int value) => Color(value switch { 5 => "ebebeb", 10 => "d8423a", 50 => "2f6fd6", 100 => "26262a", _ => "8a4fd0" }, 0.45f);

    void BuildRoom()
    {
        var t = transform;
        Part(Cube(), new Vector3(0, -0.05f, 1), new Vector3(24, 0.1f, 20), Tex("Casino/carpet", false, 10), t);
        Part(Cube(), new Vector3(0, 5.2f, 1), new Vector3(24, 0.1f, 20), Color("1a1210", 0.2f), t);
        var wood = Color("3b2419", 0.35f);
        var gold = Color("d9b25a", 0.8f, 1);
        var dark = Color("1a0f0b", 0.3f);
        foreach (var (pos, size) in new[] { (new Vector3(0, 2.6f, 8), new Vector3(24, 5.2f, 0.2f)), (new Vector3(0, 2.6f, -7), new Vector3(24, 5.2f, 0.2f)),
                                            (new Vector3(-9, 2.6f, 1), new Vector3(0.2f, 5.2f, 20)), (new Vector3(9, 2.6f, 1), new Vector3(0.2f, 5.2f, 20)) })
        {
            Part(Cube(), pos, size, wood, t);
            var trim = new Vector3(size.x > 1 ? size.x : 0.26f, 0.06f, size.z > 1 ? size.z : 0.26f);
            Part(Cube(), new Vector3(pos.x, 3.0f, pos.z), trim, gold, t);
            Part(Cube(), new Vector3(pos.x, 0.15f, pos.z), new Vector3(trim.x, 0.3f, trim.z), dark, t);
        }
        foreach (var p in new[] { new Vector3(-4, 2.6f, 7.85f), new Vector3(0, 2.6f, 7.85f), new Vector3(4, 2.6f, 7.85f), new Vector3(-8.85f, 2.6f, 0), new Vector3(8.85f, 2.6f, 0), new Vector3(-8.85f, 2.6f, 4), new Vector3(8.85f, 2.6f, 4) })
        {
            Part(cyl, p, new Vector3(0.18f, 0.12f, 0.18f), Glow("ffcf7a"), t);
            Light(p + Vector3.up * 0.1f, 4f, 1.6f, false);
        }
        Part(cyl, new Vector3(0, 3.1f, -0.4f), new Vector3(1.4f, 0.05f, 0.8f), gold, t);
        Part(cyl, new Vector3(0, 3.04f, -0.4f), new Vector3(1.2f, 0.03f, 0.65f), Glow("ffe2a8"), t);
        Light(new Vector3(0, 2.8f, -0.5f), 6, 4.5f, true);
        Light(new Vector3(0, 2.4f, -1.8f), 5, 0.8f, false);
        foreach (var x in new[] { -5.2f, 5.2f }) Light(new Vector3(x, 2.6f, 3), 5, 1.6f, false);

        // Decor Synty : machines a sous au fond, colonnes, tableaux, plantes, lustre au-dessus de la table.
        for (int i = 0; i < 12; i++)
        {
            float x = i < 6 ? -8.2f + i * 0.95f : 3.45f + (i - 6) * 0.95f;
            Prop("SM_Prop_Slot_Machine_0" + (i % 4 + 1), new Vector3(x, 0, 7.3f), 180, t);
            Prop("SM_Prop_Bar_Stool_01", new Vector3(x, 0, 6.4f), 180, t);
        }
        foreach (var z in new[] { -4.5f, -0.5f, 3.5f })
            foreach (var x in new[] { -8.45f, 8.45f }) Prop("SM_Prop_Pillar_01", new Vector3(x, 0, z), 0, t);
        Prop("SM_Prop_Wall_Art_01", new Vector3(-8.85f, 2.2f, 1.5f), 90, t);
        Prop("SM_Prop_Wall_Art_02", new Vector3(8.85f, 2.2f, 1.5f), -90, t);
        Prop("SM_Prop_Wall_Art_03", new Vector3(0, 2.4f, 7.85f), 180, t);
        foreach (var (x, z) in new[] { (-8.2f, -6.3f), (8.2f, -6.3f), (-8.2f, 7.3f), (8.2f, 7.3f), (-2.6f, 7.3f), (2.6f, 7.3f) })
            Prop("SM_Prop_Pot_Plants_0" + (1 + (int)Mathf.Abs(x + z) % 4), new Vector3(x, 0, z), 0, t);
        Prop("SM_Prop_Chandelier_01", new Vector3(0, 5.15f, -0.6f), 0, t, 1, false);
    }

    void Light(Vector3 pos, float range, float intensity, bool shadows)
    {
        var l = new GameObject("lumiere").AddComponent<Light>();
        l.transform.SetParent(transform, false);
        l.transform.localPosition = pos;
        l.type = LightType.Point;
        l.range = range;
        l.intensity = intensity;
        l.color = Board.Hex("ffd9a0");
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
    }

    // --- Places -----------------------------------------------------------------------
    // Siege 0 a gauche de l'ecran, puis vers la droite.
    float SeatAngle(int seat) => Mathf.PI + Mathf.PI * (seat + 1) / (seats + 1);

    Vector3 OnArc(int seat, float r) { float a = SeatAngle(seat); return new Vector3(Mathf.Cos(a) * r, Top, Mathf.Sin(a) * r); }

    Vector3 HandPos(int seat, int hand, int nHands)
    {
        var side = new Vector3(-Mathf.Sin(SeatAngle(seat)), 0, Mathf.Cos(SeatAngle(seat)));
        return OnArc(seat, 0.98f) + side * (hand - (nHands - 1) / 2f) * 0.3f + Vector3.up * 0.002f;
    }

    Vector3 StakePos(int seat, int hand, int nHands) => HandPos(seat, hand, nHands) + (OnArc(seat, 1.26f) - OnArc(seat, 0.98f));

    static Vector3 CardPos(Vector3 hand, int k) => hand + new Vector3(k * 0.062f, k * 0.0015f, k * 0.018f);

    public Vector3 SeatTagPos(int seat) => transform.TransformPoint(OnArc(seat, R + 0.1f) + Vector3.up * 0.1f);

    public void Build(Blackjack b)
    {
        bj = b;
        seats = b.players.Count;
        if (root) Destroy(root.gameObject);
        root = new GameObject("Manche").transform;
        root.SetParent(transform, false);
        ClearRound();
        if (!dealerAn) Chars.Spawn("Suit_Male", transform, new Vector3(0, 0, 0.55f), 180, out dealerAn);
        for (int s = 0; s < seats; s++)
        {
            var seatPos = OnArc(s, R + 0.8f); seatPos.y = 0;
            Prop("SM_Prop_Chair_03", seatPos, -SeatAngle(s) * Mathf.Rad2Deg - 90, root, 0.85f, false); // sinon leurs dossiers zebrent le tapis
            Part(cyl, OnArc(s, 1.26f) + Vector3.up * 0.0008f, new Vector3(0.31f, 0.0005f, 0.31f), Color(ColorUtility.ToHtmlStringRGB(Board.Colors[s]), 0.3f), root);
            Part(cyl, OnArc(s, 1.26f) + Vector3.up * 0.0012f, new Vector3(0.27f, 0.0005f, 0.27f), Color("15603a", 0.1f), root);
        }
    }

    void ClearRound()
    {
        foreach (var c in dealerCards) if (c.t) Destroy(c.t.gameObject);
        dealerCards.Clear();
        foreach (var h in hands.Values) foreach (var l in h) foreach (var c in l) if (c.t) Destroy(c.t.gameObject);
        hands.Clear();
        foreach (var l in stakeGo.Values) foreach (var s in l) if (s) Destroy(s.gameObject);
        stakeGo.Clear();
        stakes.Clear();
        results.Clear();
        foreach (var c in clutter) if (c) Destroy(c.gameObject);
        clutter.Clear();
    }

    // --- Cartes et jetons ---------------------------------------------------------------
    static readonly string[] Suits = { "spades", "diamonds", "hearts", "clubs" };
    static readonly string[] Ranks = { "A", "02", "03", "04", "05", "06", "07", "08", "09", "10", "J", "Q", "K" };

    Transform MakeCard(int card)
    {
        var t = new GameObject("carte").transform;
        t.SetParent(root, false);
        Part(quad, Vector3.up * 0.0006f, new Vector3(0.16f, 0.224f, 1), Tex("Cards/" + Suits[Blackjack.Suit(card)] + "_" + Ranks[Blackjack.Rank(card)]), t, new Vector3(90, 0, 0));
        Part(quad, Vector3.zero, new Vector3(0.16f, 0.224f, 1), Tex("Cards/" + back), t, new Vector3(-90, 0, 0));
        t.position = shoe.position + Vector3.up * 0.08f;
        t.rotation = Face(true);
        return t;
    }

    static readonly int[] ChipValues = { 500, 100, 50, 10, 5 };

    Transform Chips(int amount, Vector3 localPos)
    {
        var g = new GameObject("jetons").transform;
        g.SetParent(root, false);
        g.localPosition = localPos;
        int col = 0;
        foreach (int value in ChipValues)
        {
            int n = Mathf.Min(amount / value, 10);
            amount -= amount / value * value;
            if (n == 0) continue;
            var p = new Vector3((col % 2) * 0.09f - (col > 0 ? 0.045f : 0), 0, (col / 2) * 0.09f);
            for (int k = 0; k < n; k++)
                Part(cyl, p + Vector3.up * (0.006f + k * 0.012f), new Vector3(0.085f, 0.006f, 0.085f), ChipSide(value), g);
            Part(quad, p + Vector3.up * (0.0125f + (n - 1) * 0.012f), Vector3.one * 0.085f, Tex("Casino/chip_" + value, true), g, new Vector3(90, 0, 0));
            col++;
        }
        return g;
    }

    float D(float s) => s / speed;

    IEnumerator Fly(Transform t, Vector3 worldTo, Quaternion rotTo, float dur, float arc = 0.12f)
    {
        var from = t.position;
        var r0 = t.rotation;
        for (float k = 0; k < 1; k += Time.deltaTime / dur)
        {
            float e = Mathf.SmoothStep(0, 1, k);
            t.position = Vector3.Lerp(from, worldTo, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * arc;
            t.rotation = Quaternion.Slerp(r0, rotTo, e);
            yield return null;
        }
        t.position = worldTo;
        t.rotation = rotTo;
    }

    Quaternion Face(bool down) => transform.rotation * Quaternion.Euler(0, 0, down ? 180 : 0);
    Vector3 W(Vector3 local) => transform.TransformPoint(local);

    IEnumerator LayoutSeat(int seat)
    {
        var hs = hands[seat];
        for (int h = 0; h < hs.Count; h++)
            for (int k = 0; k < hs[h].Count; k++)
                StartCoroutine(Fly(hs[h][k].t, W(CardPos(HandPos(seat, h, hs.Count), k)), Face(false), D(0.3f), 0.03f));
        for (int h = 0; h < stakeGo[seat].Count; h++)
            if (stakeGo[seat][h]) StartCoroutine(Fly(stakeGo[seat][h], W(StakePos(seat, h, hs.Count)), transform.rotation, D(0.3f), 0.03f));
        yield return new WaitForSeconds(D(0.32f));
    }

    void SetStake(int seat, int hand, int amount)
    {
        if (!stakes.ContainsKey(seat)) { stakes[seat] = new List<int>(); stakeGo[seat] = new List<Transform>(); }
        while (stakes[seat].Count <= hand) { stakes[seat].Add(0); stakeGo[seat].Add(null); }
        stakes[seat][hand] = amount;
        if (stakeGo[seat][hand]) Destroy(stakeGo[seat][hand].gameObject);
        int n = hands.TryGetValue(seat, out var hs) ? Mathf.Max(1, hs.Count) : 1;
        stakeGo[seat][hand] = amount > 0 ? Chips(amount, StakePos(seat, hand, n)) : null;
    }

    // --- Lecture des evenements --------------------------------------------------------
    public IEnumerator Play(List<BJEvent> evs, System.Action<string> say)
    {
        foreach (var e in evs)
        {
            switch (e.type)
            {
                case BJEv.RoundStart:
                    if (dealerCards.Count > 0 || hands.Count > 0)
                    {
                        yield return new WaitForSeconds(D(1.6f));
                        Sound.I.Play("bj_collect");
                        var all = dealerCards.Concat(hands.Values.SelectMany(h => h.SelectMany(l => l))).Select(c => c.t).ToList();
                        foreach (var t in all) StartCoroutine(Fly(t, W(new Vector3(-1.1f, Top + 0.05f, -0.3f)), Face(true), D(0.4f)));
                        yield return new WaitForSeconds(D(0.45f));
                        ClearRound();
                    }
                    say(e.text);
                    dealerAn.CrossFadeInFixedTime("Idle", 0.2f);
                    break;
                case BJEv.Shuffle:
                    Sound.I.Play("bj_shuffle", 1, 0);
                    dealerAn.CrossFadeInFixedTime("PickUp", 0.1f);
                    yield return new WaitForSeconds(D(0.9f));
                    break;
                case BJEv.Bet:
                    if (!hands.ContainsKey(e.seat)) hands[e.seat] = new List<List<Card>> { new List<Card>() };
                    int cur = stakes.TryGetValue(e.seat, out var st) && st.Count > e.hand ? st[e.hand] : 0;
                    SetStake(e.seat, e.hand, cur + e.amount);
                    Sound.I.Play("bj_chip" + Random.Range(1, 3));
                    yield return new WaitForSeconds(D(0.25f));
                    break;
                case BJEv.Deal:
                {
                    var c = new Card { t = MakeCard(e.card), card = e.card, down = e.faceDown };
                    Sound.I.Play("bj_slide" + Random.Range(1, 3));
                    Vector3 to;
                    if (e.seat < 0) { dealerCards.Add(c); to = new Vector3((dealerCards.Count - 1) * 0.1f - 0.06f, Top + 0.002f + dealerCards.Count * 0.0015f, -0.36f); }
                    else
                    {
                        if (!hands.ContainsKey(e.seat)) hands[e.seat] = new List<List<Card>> { new List<Card>() };
                        var hs = hands[e.seat];
                        while (hs.Count <= e.hand) hs.Add(new List<Card>());
                        hs[e.hand].Add(c);
                        to = CardPos(HandPos(e.seat, e.hand, hs.Count), hs[e.hand].Count - 1);
                    }
                    yield return Fly(c.t, W(to), Face(e.faceDown), D(0.32f));
                    Sound.I.Play("bj_place" + Random.Range(1, 3));
                    break;
                }
                case BJEv.Reveal:
                    if (dealerCards.Count > 1 && dealerCards[1].down)
                    {
                        dealerCards[1].down = false;
                        Sound.I.Play("bj_flip");
                        yield return Fly(dealerCards[1].t, dealerCards[1].t.position, Face(false), D(0.35f), 0.08f);
                    }
                    break;
                case BJEv.Split:
                {
                    var hs = hands[e.seat];
                    var moved = hs[e.hand][1];
                    hs[e.hand].RemoveAt(1);
                    hs.Insert(e.hand + 1, new List<Card> { moved });
                    stakes[e.seat].Insert(e.hand + 1, 0);
                    stakeGo[e.seat].Insert(e.hand + 1, null);
                    SetStake(e.seat, e.hand + 1, e.amount);
                    Sound.I.Play("bj_chips");
                    yield return LayoutSeat(e.seat);
                    break;
                }
                case BJEv.Insurance:
                    clutter.Add(Chips(e.amount, OnArc(e.seat, 0.72f)));
                    Sound.I.Play("bj_chip1");
                    break;
                case BJEv.Result:
                    if (e.hand >= 0) results[(e.seat, e.hand)] = e.text;
                    if (e.hand >= 0 && stakeGo.TryGetValue(e.seat, out var sg) && e.hand < sg.Count && sg[e.hand])
                    {
                        if (e.amount < 0) StartCoroutine(Fly(sg[e.hand], W(new Vector3(0, Top + 0.06f, -0.1f)), transform.rotation, D(0.45f)));
                        else if (e.amount > 0) { SetStake(e.seat, e.hand, stakes[e.seat][e.hand] + e.amount); Sound.I.Play("bj_chips"); }
                    }
                    yield return new WaitForSeconds(D(0.35f));
                    break;
                case BJEv.Broke:
                    say(bj.players[e.seat].name + " est ruiné !");
                    yield return new WaitForSeconds(D(1f));
                    break;
                case BJEv.GameOver:
                    yield return new WaitForSeconds(D(1.2f));
                    dealerAn.CrossFadeInFixedTime("Victory", 0.2f);
                    break;
            }
        }
    }

    // --- Etiquettes flottantes ------------------------------------------------------------
    public IEnumerable<(Vector3 pos, string text, Color col)> Bubbles()
    {
        if (dealerCards.Count > 0)
            yield return (W(new Vector3(-0.22f, Top, -0.3f)), Blackjack.Value(dealerCards.Where(c => !c.down).Select(c => c.card).ToList()).ToString(), new Color(0.1f, 0.1f, 0.1f));
        foreach (var kv in hands)
            for (int h = 0; h < kv.Value.Count; h++)
            {
                if (kv.Value[h].Count == 0) continue;
                var (v, soft) = Blackjack.Eval(kv.Value[h].Select(c => c.card).ToList());
                string txt = results.TryGetValue((kv.Key, h), out var r) ? r : v > 21 ? "Sauté !" : (soft && v < 21 ? $"{v - 10}/{v}" : v.ToString());
                yield return (W(HandPos(kv.Key, h, kv.Value.Count) + new Vector3(-0.13f, 0, -0.08f)), txt, Board.Colors[kv.Key]);
            }
    }

    public void CamHome(int mySeat, out Vector3 target, out float yaw)
    {
        target = Focus;
        yaw = transform.eulerAngles.y;
    }
}
