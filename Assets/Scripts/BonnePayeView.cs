using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// La Bonne Paye sur la nappe de la clairiere, vue de dessus (camera fixe) :
//  - au milieu le plateau scanne (Resources/BonnePaye/Images/plateau, hors depot ; a defaut un plateau uni) ;
//  - a gauche les trois pioches (courrier, acquisitions, evenements) : on y pioche soi-meme d'un clic ;
//  - a droite la place du de ; de chaque cote le coin de table des joueurs (sieges pairs a gauche, impairs a droite) ou
//    sont posees leurs cartes : courrier ferme en pile, affaires, assurances et "Besoin d'argent ?" face visible ;
//  - une carte piochee monte vers la camera en se retournant, on la lit en grand, puis elle va a sa place.
// Au centre du plateau, la cagnotte : une pile de pieces d'or qui grandit avec le montant.
public class BonnePayeView : MonoBehaviour
{
    const float Half = 0.5f, SlabH = 0.03f, Margin = 0.028f;
    public const float CardW = 0.13f, CardH = 0.0826f;
    static readonly Vector3 BoardAt = new Vector3(0, 0.021f, 0.5f);
    public static Color ColorOf(int seat) => Board.Colors[seat % Board.Colors.Length];

    // Grille 6 x 6 du plateau (colonne, ligne depuis le haut) de chaque jour ; le centre 2 x 2 est le logo et la cagnotte.
    static readonly Vector2Int[] Grid = BuildGrid();
    static Vector2Int[] BuildGrid()
    {
        var g = new Vector2Int[BonnePaye.Days + 1];
        g[0] = new Vector2Int(5, 5);
        for (int d = 1; d <= 5; d++) g[d] = new Vector2Int(5 - d, 5);
        for (int d = 6; d <= 10; d++) g[d] = new Vector2Int(0, 10 - d);
        for (int d = 11; d <= 15; d++) g[d] = new Vector2Int(d - 10, 0);
        for (int d = 16; d <= 19; d++) g[d] = new Vector2Int(5, d - 15);
        for (int d = 20; d <= 23; d++) g[d] = new Vector2Int(24 - d, 4);
        for (int d = 24; d <= 26; d++) g[d] = new Vector2Int(1, 27 - d);
        for (int d = 27; d <= 29; d++) g[d] = new Vector2Int(d - 25, 1);
        g[30] = new Vector2Int(4, 2); g[31] = new Vector2Int(4, 3);
        return g;
    }
    static Vector3 GridPos(float c, float r)
    {
        float cell = (1 - 2 * Margin) * Half * 2 / 6;
        return BoardAt + new Vector3(-Half + Margin * 2 * Half + (c + 0.5f) * cell, SlabH, Half - Margin * 2 * Half - (r + 0.5f) * cell);
    }
    public static Vector3 CellPos(int d) => GridPos(Grid[d].x, Grid[d].y);
    Vector3 PawnPos(int seat, int d) => CellPos(d) + new Vector3((seat % 3 - 1) * 0.045f, 0, (seat / 3 - 0.5f) * 0.05f);
    static Vector3 PotPos => GridPos(2.5f, 2.5f) + new Vector3(0.1f, 0, -0.06f);

    // --- Autour du plateau ---
    static readonly string[] DeckName = { "courrier", "acquisition", "evenement" };
    static Vector3 DeckPos(int k) => BoardAt + new Vector3(-Half - 0.13f, 0, 0.3f - k * 0.3f);
    static Vector3 DieHome => BoardAt + new Vector3(Half + 0.13f, 0, -0.33f);
    const float ZoneX = Half + 0.42f;
    // Coin de table d'un joueur : sieges pairs a gauche, impairs a droite, de haut en bas.
    Vector3 ZonePos(int seat)
    {
        int rows = (bp.players.Count + 1) / 2, row = seat / 2;
        float z = rows == 1 ? 0 : rows == 2 ? 0.2f - row * 0.4f : 0.34f - row * 0.34f;
        return BoardAt + new Vector3(seat % 2 == 0 ? -ZoneX : ZoneX, 0.008f, z);   // au-dessus de la nappe (epaisse de 6 mm)
    }
    public Vector3 ZoneTag(int seat) => transform.TransformPoint(ZonePos(seat) + new Vector3(0, 0, CardH * 0.5f + 0.045f));

    BonnePaye bp;
    Material lit;
    Transform pawnRoot, potRoot, zoneRoot;
    // Le plateau, ses pions et la cagnotte tournent ensemble autour du centre du plateau (clic droit) : chacun le met
    // dans son sens ; pioches, coins de table et camera ne bougent pas. Les positions locales restent celles du plateau.
    Transform pivot;
    public float BoardYaw
    {
        set
        {
            var r = Quaternion.Euler(0, value, 0);
            pivot.localRotation = r;
            pivot.localPosition = BoardAt - r * BoardAt;
        }
    }
    readonly Transform[] decks = new Transform[3];
    Transform[] pawns;
    public De de;
    public float speed = 1;
    public float Speed { set { speed = value; if (de) de.speed = value; } }
    float Dt => Time.deltaTime * speed;

    void Awake()
    {
        transform.position = Clairiere.Center;
        lit = Resources.Load<Material>("Lit");
        pivot = new GameObject("plateau tournant").transform; pivot.SetParent(transform, false);
        BuildBoard();
        potRoot = new GameObject("cagnotte").transform; potRoot.SetParent(pivot, false);
        zoneRoot = new GameObject("cartes des joueurs").transform; zoneRoot.SetParent(transform, false);
        for (int k = 0; k < 3; k++) decks[k] = BuildDeck(k);
        de = new GameObject("de").AddComponent<De>();
        de.transform.SetParent(transform, false);
        de.Init(lit, BoardAt + Vector3.up * SlabH, Half, _ => DieHome);
        gameObject.SetActive(false);
    }

    Material Mat(Color c, float smooth = 0.3f) { var m = new Material(lit) { color = c }; m.SetFloat("_Smoothness", smooth); return m; }

    GameObject Prim(PrimitiveType t, Vector3 pos, Vector3 scale, Material m, Transform parent)
    {
        var g = GameObject.CreatePrimitive(t);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    // Matiere sans reflet (scans deja lumineux : sinon des zones disparaissent sous la lumiere).
    static void Matte(Material m, float dim)
    {
        m.color = new Color(dim, dim, dim, 1);
        m.SetFloat("_Smoothness", 0);
        m.SetFloat("_SpecularHighlights", 0); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.SetFloat("_EnvironmentReflections", 0); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
    }

    void BuildBoard()
    {
        var root = new GameObject("plateau").transform;
        root.SetParent(pivot, false);
        Prim(PrimitiveType.Cube, BoardAt + Vector3.up * SlabH / 2, new Vector3(Half * 2 + 0.03f, SlabH, Half * 2 + 0.03f), Mat(Board.Hex("1a3d6b")), root);
        var top = new Material(lit);
        var tex = Resources.Load<Texture2D>("BonnePaye/Images/plateau");
        if (tex) { top.SetTexture("_BaseMap", tex); Matte(top, 0.76f); } else top.color = Board.Hex("3fb6e0");
        var q = Prim(PrimitiveType.Quad, BoardAt + Vector3.up * (SlabH + 0.0006f), new Vector3(Half * 2, Half * 2, 1), top, root);
        q.transform.localRotation = Quaternion.Euler(90, 0, 0);
    }

    // --- Cartes 3D ---
    readonly Dictionary<string, Material> cardMats = new Dictionary<string, Material>();
    Material CardMat(string img)
    {
        if (cardMats.TryGetValue(img, out var m)) return m;
        // Sans eclairage : les cartes gardent les couleurs du scan (pas de reflet ni de lueur qui gene la lecture).
        m = new Material(Resources.Load<Material>("QuizScreen"));   // URP Unlit (deja dans le jeu compile)
        var tex = Resources.Load<Texture2D>("BonnePaye/Images/" + img);
        if (tex) m.SetTexture("_BaseMap", tex); else m.SetColor("_BaseColor", Board.Hex("d8423a"));
        m.SetColor("_BaseColor", tex ? new Color(0.93f, 0.93f, 0.93f, 1) : Board.Hex("d8423a"));
        m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cull", 0);
        return cardMats[img] = m;
    }
    static string Face(int deck, int img) => img < 0 ? "dos_" + DeckName[deck] : DeckName[deck] + "_" + img;
    Transform Card(string img, Transform parent, Vector3 pos, float yaw = 0)
    {
        var q = Prim(PrimitiveType.Quad, pos, new Vector3(CardW, CardH, 1), CardMat(img), parent);
        q.transform.localRotation = Quaternion.Euler(90, yaw, 0);
        q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return q.transform;
    }

    // Pioche : un paquet epais, la carte du dessus de dos ; une boite de clic genereuse.
    Transform BuildDeck(int k)
    {
        var d = new GameObject("pioche " + DeckName[k]).transform;
        d.SetParent(transform, false);
        d.localPosition = DeckPos(k);
        Prim(PrimitiveType.Cube, new Vector3(0, 0.011f, 0), new Vector3(CardW * 0.95f, 0.022f, CardH * 0.93f), Mat(Board.Hex("f4efe6")), d).name = "tranche";
        Card(Face(k, -1), d, new Vector3(0, 0.0225f, 0)).name = "dessus";
        var box = d.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0, 0.02f, 0); box.size = new Vector3(CardW * 1.3f, 0.06f, CardH * 1.5f);
        return d;
    }
    public int DeckUnder(Ray r) { for (int k = 0; k < 3; k++) if (decks[k].GetComponent<Collider>().Raycast(r, out _, 20)) return k; return -1; }
    public int DeckWanted = -1;   // pioche ou le joueur doit piocher (elle se souleve et respire)

    // Cartes gardees par un joueur, dans l'ordre de pose (images).
    List<string> Kept(BonnePaye.Player p)
    {
        var kept = new List<string>();
        foreach (int c in p.acqs) kept.Add(Face(1, BonnePaye.Acqs[c].img));
        if (p.medic) kept.Add(Face(0, Array.Find(BonnePaye.Mails, m => m.t == "assur" && m.cat == "med").img));
        if (p.auto) kept.Add(Face(0, Array.Find(BonnePaye.Mails, m => m.t == "assur" && m.cat == "garage").img));
        foreach (int c in p.besoin) kept.Add(Face(0, BonnePaye.Mails[c].img));
        return kept;
    }
    Vector3 MailPilePos(int seat, int k) => ZonePos(seat) + new Vector3(-0.075f + k * 0.004f, 0.004f + k * 0.0022f, k * 0.003f);
    Vector3 KeptPos(int seat, int k, int n)
    {
        float step = n > 1 ? Mathf.Min(0.04f, 0.1f / (n - 1)) : 0;
        return ZonePos(seat) + new Vector3(0.06f + k * step, 0.004f + k * 0.0022f, -k * 0.006f);
    }

    // Tapis de couleur et cartes devant chaque joueur.
    // Epaisseur de chaque pioche selon les cartes restantes (0,45 mm par carte) ; vide : la pioche disparait.
    void SyncDecks()
    {
        int[] left = { bp.MailLeft, bp.AcqLeft, bp.EvtLeft };
        for (int k = 0; k < 3; k++)
        {
            float h = Mathf.Max(0.0015f, left[k] * 0.00045f);
            var side = decks[k].Find("tranche"); var top = decks[k].Find("dessus");
            side.localScale = new Vector3(CardW * 0.95f, h, CardH * 0.93f); side.localPosition = new Vector3(0, h / 2, 0);
            top.localPosition = new Vector3(0, h + 0.0005f, 0);
            side.gameObject.SetActive(left[k] > 0); top.gameObject.SetActive(left[k] > 0);
        }
    }

    void SyncZones()
    {
        SyncDecks();
        foreach (Transform c in zoneRoot) Destroy(c.gameObject);
        for (int s = 0; s < bp.players.Count; s++)
        {
            var p = bp.players[s];
            var pad = Prim(PrimitiveType.Cube, ZonePos(s) + new Vector3(0, 0.001f, 0), new Vector3(0.34f, 0.002f, CardH + 0.05f), Mat(Color.Lerp(ColorOf(s), Color.white, 0.25f), 0.05f), zoneRoot);
            pad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int k = 0; k < p.unread.Count; k++) Card(Face(0, -1), zoneRoot, MailPilePos(s, k), (k % 3 - 1) * 5);
            var kept = Kept(p);
            for (int k = 0; k < kept.Count; k++) Card(kept[k], zoneRoot, KeptPos(s, k, kept.Count), -6 + k * 3);
        }
    }

    // --- Pions ---
    public void Build(BonnePaye b)
    {
        Clairiere.Show(true);
        gameObject.SetActive(true);
        bp = b;
        if (pawnRoot) Destroy(pawnRoot.gameObject);
        pawnRoot = new GameObject("pions").transform;
        pawnRoot.SetParent(pivot, false);
        pawns = new Transform[b.players.Count];
        var white = Mat(Board.Hex("f4efe6"));
        for (int i = 0; i < b.players.Count; i++)
        {
            var p = new GameObject("pion " + i).transform;
            p.SetParent(pawnRoot, false);
            var m = Mat(ColorOf(i), 0.55f);
            Prim(PrimitiveType.Cylinder, new Vector3(0, 0.004f, 0), new Vector3(0.038f, 0.004f, 0.038f), white, p);
            Prim(PrimitiveType.Cylinder, new Vector3(0, 0.03f, 0), new Vector3(0.026f, 0.024f, 0.026f), m, p);
            Prim(PrimitiveType.Sphere, new Vector3(0, 0.066f, 0), Vector3.one * 0.03f, m, p);
            var box = p.gameObject.AddComponent<BoxCollider>();   // le de rebondit dessus
            box.center = new Vector3(0, 0.038f, 0); box.size = new Vector3(0.038f, 0.076f, 0.038f);
            pawns[i] = p;
        }
        de.Park(b.turn);
        ClearHeld();
        Sync();
    }

    public void Hide() { StopAllCoroutines(); ClearHeld(); bp = null; busy = false; gameObject.SetActive(false); Clairiere.Show(false); }

    public void Sync()
    {
        if (bp == null) return;
        for (int s = 0; s < bp.players.Count; s++) pawns[s].localPosition = PawnPos(s, bp.players[s].pos);
        SyncPot();
        SyncZones();
    }

    // Cagnotte : une piece par tranche de 100 €, en petites piles (40 pieces au plus).
    int potShown = -1;
    Material gold;
    void SyncPot()
    {
        int n = Mathf.Min(40, (bp.pot + 99) / 100);
        if (n == potShown) return;
        potShown = n;
        foreach (Transform c in potRoot) Destroy(c.gameObject);
        gold ??= Mat(Board.Hex("f2c230"), 0.8f);
        for (int i = 0; i < n; i++)
        {
            int stack = i % 5, h = i / 5;
            var at = PotPos + new Vector3((stack % 3 - 1) * 0.034f, 0.003f + h * 0.0045f, (stack / 3 - 0.5f) * 0.034f);
            Prim(PrimitiveType.Cylinder, at, new Vector3(0.028f, 0.0022f, 0.028f), gold, potRoot).transform.localRotation = Quaternion.Euler(0, i * 23, 0);
        }
    }

    void LateUpdate()
    {
        for (int k = 0; k < 3; k++) if (decks[k])
            decks[k].localPosition = DeckPos(k) + Vector3.up * (k == DeckWanted && !busy ? 0.014f + 0.01f * Mathf.Sin(Time.time * 5) : 0);
        // Carte tenue devant la camera : elle flotte doucement.
        if (held && heldReady)
        {
            var pose = InspectPose();
            held.position = pose.position + pose.rotation * Vector3.up * 0.004f * Mathf.Sin(Time.time * 2.2f);
            held.rotation = pose.rotation * Quaternion.Euler(Mathf.Sin(Time.time * 1.7f) * 2.5f, Mathf.Sin(Time.time * 1.3f) * 3f, 0);
        }
        if (bp == null || busy || bp.Finished) return;
        SyncPot();
        var p = pawns[bp.turn];
        float lift = bp.Pending == null && !bp.rolled ? Mathf.Abs(Mathf.Sin(Time.time * 4)) * 0.014f : 0;
        p.localPosition = PawnPos(bp.turn, bp.players[bp.turn].pos) + Vector3.up * lift;
    }

    // --- Camera fixe, de dessus, cadree sur le plateau, les pioches et les coins des joueurs ---
    const float Pitch = 76, TopUi = 0.13f, BottomUi = 0.1f;
    Camera cam;
    public Pose CamPose(float fovDeg, float aspect)
    {
        float free = 1 - TopUi - BottomUi;
        float tanV = Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad);
        float sizeZ = Half + 0.02f, sizeX = ZoneX + 0.2f;
        float dist = Mathf.Max(sizeZ / (tanV * free) / Mathf.Sin(Pitch * Mathf.Deg2Rad), sizeX / (tanV * aspect));
        var rot = transform.rotation * Quaternion.Euler(Pitch, 0, 0);
        var target = transform.TransformPoint(BoardAt + Vector3.up * SlabH);
        float shift = (BottomUi - TopUi) * tanV * dist;
        var from = target - rot * Vector3.forward * dist - rot * Vector3.up * shift;
        return new Pose(from, rot);
    }
    // Carte tenue : devant la camera, un peu au-dessus du centre, assez grande pour etre lue.
    Pose InspectPose()
    {
        if (!cam) cam = Camera.main;
        var t = cam.transform;
        return new Pose(t.position + t.forward * 0.62f + t.up * 0.035f, Quaternion.LookRotation(t.forward, t.up));
    }
    const float InspectScale = 2.7f;

    // --- Animations -----------------------------------------------------------------------------------------------------
    public bool busy, thrownHere;
    Transform held;          // carte montree en grand (en attente d'une decision ou le temps de la lire)
    bool heldReady;
    int heldSeat = -1;
    string heldImg;
    void ClearHeld() { if (held) Destroy(held.gameObject); held = null; heldReady = false; }

    public IEnumerator Play(List<BPEvent> evs, Action<BPEvent> say)
    {
        busy = true;
        foreach (var e in evs)
        {
            say?.Invoke(e);
            switch (e.type)
            {
                case BPEv.Rolled:
                    float[] fling = null;
                    if (!string.IsNullOrEmpty(e.text))
                    {
                        var parts = e.text.Split('|');
                        fling = new float[13];
                        for (int k = 0; k < 13; k++) float.TryParse(parts[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fling[k]);
                    }
                    yield return de.Play(fling, e.amount, e.seat, !thrownHere);
                    thrownHere = false;
                    break;
                case BPEv.Moved: yield return Walk(e); break;
                case BPEv.Jackpot: yield return Jackpot(e.seat); break;
                case BPEv.Lotto: yield return LottoWin(e.seat); break;
                case BPEv.Card: yield return CardMove(e); break;
                case BPEv.Pay: SyncPot(); break;
            }
        }
        // Carte tenue sans decision en attente : elle part a sa place (ou a la defausse).
        var j = bp?.Pending;
        bool waiting = j != null && (j.t == BonnePaye.Task.Buy || j.t == BonnePaye.Task.Insure);
        if (held && !waiting) yield return PutAway();
        Sync();
        if (bp != null && !bp.Finished) yield return de.Collect(bp.Actor >= 0 ? bp.Actor : bp.turn);
        busy = false;
    }

    static float Back(float t) { float c = 1.4f; t -= 1; return 1 + (c + 1) * t * t * t + c * t * t; }   // arrivee avec petit depassement

    // Vol d'une carte entre deux poses du monde ; retournee a mi-course si "flipTo" est donne.
    IEnumerator Fly(Transform c, Vector3 a, Quaternion ra, float sa, Vector3 b, Quaternion rb, float sb, float dur, string flipTo = null, float arc = 0.1f)
    {
        var r = c.GetComponent<Renderer>();
        var up = transform.TransformDirection(Vector3.up);
        Sound.I.Play(UnityEngine.Random.value < 0.5f ? "bj_slide1" : "bj_slide2", 0.6f, 0.1f);
        for (float t = 0; t < 1; t += Dt / dur)
        {
            float e = Back(Mathf.Clamp01(t));
            c.position = Vector3.LerpUnclamped(a, b, e) + up * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * arc;
            c.localScale = new Vector3(CardW, CardH, 1) * Mathf.LerpUnclamped(sa, sb, e);
            // Retournement autour de la longueur de la carte ; a mi-tour on change d'image (le quad n'a qu'une face).
            float spin = flipTo != null ? Mathf.Clamp01(t * 1.5f) * 180 : 0;
            if (flipTo != null && spin > 90) r.sharedMaterial = CardMat(flipTo);
            c.rotation = Quaternion.Slerp(ra, rb, Mathf.Clamp01(e)) * Quaternion.Euler(0, spin > 90 ? spin - 180 : spin, 0);
            yield return null;
        }
        c.position = b; c.rotation = rb; c.localScale = new Vector3(CardW, CardH, 1) * sb;
    }
    Quaternion Flat(float yaw = 0) => transform.rotation * Quaternion.Euler(90, yaw, 0);
    Vector3 W(Vector3 local) => transform.TransformPoint(local);

    // Carte piochee ou ouverte : courrier ferme -> directement sur la pile du joueur (surprise) ;
    // acquisition / evenement / courrier ouvert -> monte vers la camera en se retournant, et s'y tient.
    IEnumerator CardMove(BPEvent e)
    {
        if (held) yield return PutAway();
        if (e.deck == 0 && e.img < 0)
        {
            int k = Mathf.Max(0, bp.players[e.seat].unread.Count - 1);
            var c = Card(Face(0, -1), transform, DeckPos(0));
            yield return Fly(c, W(DeckPos(0) + Vector3.up * 0.03f), Flat(), 1, W(MailPilePos(e.seat, k)), Flat((k % 3 - 1) * 5), 1, 0.5f, null, 0.16f);
            Destroy(c.gameObject);
            SyncZones();
            yield break;
        }
        // Depart : la pioche, ou (courrier ouvert au Jour de paye) le haut de la pile du joueur.
        Vector3 from = e.deck == 0 ? MailPilePos(e.seat, bp.players[e.seat].unread.Count) : DeckPos(e.deck) + Vector3.up * 0.03f;
        if (e.deck == 0) SyncZones();   // la lettre quitte la pile
        held = Card(Face(e.deck, -1), transform, from);
        held.parent = null;
        heldSeat = e.seat; heldImg = Face(e.deck, e.img); heldReady = false;
        var pose = InspectPose();
        yield return Fly(held, W(from), Flat(), 1, pose.position, pose.rotation, InspectScale, 0.7f, heldImg, 0.25f);
        heldReady = true;
        Sound.I.Play("tick", 0.6f);
        yield return new WaitForSeconds(e.deck == 1 ? 0.3f : 1.5f / speed);   // le temps de lire (les affaires attendent la decision)
    }

    // La carte tenue va a sa place : gardee (affaire achetee, assurance, Besoin d'argent) -> devant le joueur ; sinon
    // elle file vers sa pioche (defausse) en rapetissant.
    IEnumerator PutAway()
    {
        var c = held; held = null; heldReady = false;
        if (!c) yield break;
        var p = heldSeat >= 0 && heldSeat < bp.players.Count ? bp.players[heldSeat] : null;
        var kept = p != null ? Kept(p) : new List<string>();
        int k = kept.LastIndexOf(heldImg);
        Vector3 to; float sb = 1;
        if (k >= 0) to = W(KeptPos(heldSeat, k, kept.Count));
        else { to = W(DeckPos(heldImg.StartsWith("acquisition") ? 1 : heldImg.StartsWith("evenement") ? 2 : 0) + new Vector3(0, 0.12f, 0)); sb = 0.2f; }
        yield return Fly(c, c.position, c.rotation, InspectScale, to, Flat(k >= 0 ? -6 + k * 3 : 0), sb, 0.5f, null, 0.08f);
        Destroy(c.gameObject);
        SyncZones();
    }

    IEnumerator Hop(Transform p, Vector3 a, Vector3 b, float height, float dur, int n)
    {
        Sound.I.Play("hop" + (n % 3 + 1), 0.5f);
        for (float t = 0; t < 1; t += Dt / dur)
        {
            p.localPosition = Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * height;
            yield return null;
        }
        p.localPosition = b;
    }

    // En avant : case par case ; en arriere ou retour au depart : un seul saut.
    IEnumerator Walk(BPEvent e)
    {
        var p = pawns[e.seat];
        if (e.to > e.from && e.to - e.from <= 6)
            for (int d = e.from + 1; d <= e.to; d++) yield return Hop(p, p.localPosition, PawnPos(e.seat, d), 0.045f, 0.2f, d);
        else yield return Hop(p, p.localPosition, PawnPos(e.seat, e.to), 0.14f, 0.5f, 0);
    }

    // Loterie gagnee : une gerbe de pieces jaillit du centre du plateau et file vers le coin de table du gagnant.
    IEnumerator LottoWin(int seat)
    {
        Sound.I.Play("win");
        gold ??= Mat(Board.Hex("f2c230"), 0.8f);
        var coins = new List<Transform>();
        var from = BoardAt + Vector3.up * (SlabH + 0.01f);
        var to = ZonePos(seat) + Vector3.up * 0.02f;
        for (int i = 0; i < 18; i++)
        {
            var c = Prim(PrimitiveType.Cylinder, from, new Vector3(0.03f, 0.0025f, 0.03f), gold, transform).transform;
            c.localRotation = Quaternion.Euler(UnityEngine.Random.Range(0, 360f), UnityEngine.Random.Range(0, 360f), 0);
            coins.Add(c);
        }
        for (float t = 0; t < 1.3f; t += Dt / 1.1f)
        {
            for (int i = 0; i < coins.Count; i++)
            {
                float k = Mathf.Clamp01(t - i * 0.02f);
                var side = new Vector3(Mathf.Cos(i * 2.4f), 0, Mathf.Sin(i * 2.4f)) * 0.08f * Mathf.Sin(k * Mathf.PI);
                coins[i].localPosition = Vector3.Lerp(from, to, k) + side + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.25f;
                coins[i].Rotate(0, 0, 720 * Time.deltaTime, Space.Self);
            }
            yield return null;
        }
        foreach (var c in coins) Destroy(c.gameObject);
    }

    // Cagnotte gagnee : les pieces s'envolent vers le pion du gagnant.
    IEnumerator Jackpot(int seat)
    {
        Sound.I.Play("win");
        var coins = new List<Transform>();
        foreach (Transform c in potRoot) coins.Add(c);
        var to = pawns[seat].localPosition + Vector3.up * 0.08f;
        var from = new List<Vector3>(); foreach (var c in coins) from.Add(c.localPosition);
        for (float t = 0; t < 1; t += Dt / 0.8f)
        {
            for (int i = 0; i < coins.Count; i++)
            {
                float k = Mathf.Clamp01(t * 1.4f - i * 0.01f);
                coins[i].localPosition = Vector3.Lerp(from[i], to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.15f;
            }
            yield return null;
        }
        potShown = -1;
        SyncPot();
    }
}
