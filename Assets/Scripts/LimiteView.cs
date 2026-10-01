using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Limite Limite sur la nappe de la clairiere : un tapis de jeu rond, la pioche rouge (reponses), la pioche noire
// (questions) et la defausse au milieu. Chaque joueur a sa place autour du tapis : sa main (de dos chez les autres,
// face visible devant moi), ses cartes jouees face cachee devant lui. Pour juger, les cartes jouees sont rassemblees
// au centre, melangees (le Boss ne sait pas a qui elles sont), et retournees une a une ; apres son choix, le nom de
// chaque auteur apparait. Chaque PC voit la table depuis sa place : les cartes visibles sont tournees vers lui.
// Toutes les cartes ont une place cible calculee d'apres la partie ; elles y glissent (vol, retournement) toutes seules.
public class LimiteView : MonoBehaviour
{
    public const float W = 0.08f, H = 0.112f, Th = 0.002f;       // carte reponse (rouge)
    const float QW = 0.1f, QH = 0.14f;                            // carte question (noire)
    const float Rad = 0.44f;                                       // places des joueurs autour du centre
    static readonly Vector3 Mid = new Vector3(0, 0.03f, 0.5f);    // centre de la nappe

    public class Card : MonoBehaviour
    {
        public string key, text; public int seat = -1; public bool black, mine;
        public Vector3 pos; public Quaternion rot; public float scale = 1;
        public TextMesh front; public Renderer body;
        public bool gone;   // sortie : vole a la defausse puis disparait
        public bool winner; public GameObject halo;   // reponse choisie par le Boss : dore et rebondit
    }

    Limite ll;
    int me;
    Material lit, red, redSel, black, felt, rim, gold;
    Font font;
    Transform root, plates;
    readonly Dictionary<string, Card> cards = new Dictionary<string, Card>();
    readonly List<TextMesh> names = new List<TextMesh>(), authors = new List<TextMesh>();
    public Card hover;
    public List<string> selection = new List<string>();   // mes cartes choisies (ordre des trous)
    public int revealed;                                   // reponses retournees au centre (jugement)
    float yaw;

    void Awake()
    {
        transform.position = Clairiere.Center;
        lit = Resources.Load<Material>("Lit");
        font = Resources.Load<Font>("Fonts/SairaCondensed-ExtraBold") ?? Resources.Load<Font>("Fonts/Fredoka-Bold");
        red = Mat("d7202a", 0.25f); redSel = Mat("f0333d", 0.35f); black = Mat("141317", 0.3f);
        felt = Mat("1d4d33", 0.05f); rim = Mat("5a3a22", 0.3f);
        gold = new Material(Resources.Load<Material>("LitGlow") ?? lit) { color = Board.Hex("f7df2e") };
        if (gold.HasProperty("_EmissionColor")) gold.SetColor("_EmissionColor", Board.Hex("f7c400") * 1.6f);
        gameObject.SetActive(false);
    }

    Material Mat(string hex, float smooth) { var m = new Material(lit) { color = Board.Hex(hex) }; m.SetFloat("_Smoothness", smooth); return m; }

    GameObject Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(t);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos; g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    // --- Places ----------------------------------------------------------------------------------------------------
    int N => ll.players.Count;
    float AngleOf(int s) => ((s - me + N) % N) * 360f / N;                       // moi en bas (angle 0), les autres autour
    Quaternion Toward(int s) => Quaternion.Euler(0, AngleOf(s), 0);               // "haut" de la carte : vers le centre
    // Le cote oppose est resserre vers le centre (la camera est penchee : sinon le joueur d'en face sort de l'ecran).
    Vector3 Spot(int s, float r) => Mid + Toward(s) * new Vector3(0, 0, -r);   // meme cercle pour tous : table symetrique
    Quaternion FaceUp(Quaternion yawRot) => yawRot;                               // a plat, face visible
    Quaternion FaceDown(Quaternion yawRot) => yawRot * Quaternion.Euler(0, 0, 180);
    static readonly Vector3 RedDeck = Mid + new Vector3(0.13f, 0, 0.06f), BlackDeck = Mid + new Vector3(-0.13f, 0, 0.06f), Discard = Mid + new Vector3(0.24f, 0, 0.06f);

    public void Build(Limite l, int mySeat)
    {
        Clairiere.Show(true);
        gameObject.SetActive(true);
        ll = l; me = Mathf.Clamp(mySeat, 0, l.players.Count - 1);
        yaw = 0;
        foreach (var c in cards.Values) if (c) Destroy(c.gameObject);
        cards.Clear(); selection.Clear(); revealed = 0;
        if (root) Destroy(root.gameObject);
        root = new GameObject("tapis").transform; root.SetParent(transform, false);
        // Pioches (paquets epais) et defausse.
        Prim(PrimitiveType.Cube, root, RedDeck - Vector3.up * 0.012f, new Vector3(W, 0.024f, H), red);
        Prim(PrimitiveType.Cube, root, BlackDeck - Vector3.up * 0.012f, new Vector3(QW, 0.024f, QH), black);
        Label(root, RedDeck + Vector3.up * 0.0005f, Quaternion.identity, "LIMITE\nLIMITE", 0.0011f, Color.white);
        Label(root, BlackDeck + Vector3.up * 0.0005f, Quaternion.identity, "LIMITE\nLIMITE", 0.0013f, Board.Hex("e8403a"));
        // Noms des joueurs devant leur place.
        plates = new GameObject("noms").transform; plates.SetParent(root, false);
        names.Clear();
        for (int s = 0; s < N; s++)
        {
            // Plaque sombre sous le nom : lisible sur les carreaux de la nappe.
            float pr = Rad + 0.11f;
            var plate = Prim(PrimitiveType.Cube, plates, Spot(s, pr) - Vector3.up * 0.001f, new Vector3(0.34f, 0.002f, 0.07f), black);
            plate.transform.localRotation = Toward(s);
            names.Add(Label(plates, Spot(s, pr) + Vector3.up * 0.001f, Toward(s), ll.players[s].name, 0.0042f, Color.Lerp(Board.Colors[s % Board.Colors.Length], Color.white, 0.35f)));
        }
        Sync(true);
    }

    public void Hide() { ll = null; gameObject.SetActive(false); Clairiere.Show(false); }

    TextMesh Label(Transform parent, Vector3 pos, Quaternion yawRot, string text, float size, Color color)
    {
        var t = new GameObject("texte").AddComponent<TextMesh>();
        t.transform.SetParent(parent, false);
        t.transform.localPosition = pos;
        t.transform.localRotation = yawRot * Quaternion.Euler(90, 0, 0);
        t.font = font; t.GetComponent<MeshRenderer>().sharedMaterial = RoueView.TextMat(font);
        t.fontSize = 64; t.characterSize = size; t.anchor = TextAnchor.MiddleCenter; t.alignment = TextAlignment.Center;
        t.color = color; t.text = text;
        return t;
    }

    // Retour a la ligne (TextMesh ne coupe pas tout seul).
    public static string Wrap(string s, int max)
    {
        var lines = new List<string>(); var cur = "";
        foreach (var w in s.Split(' '))
        {
            if (cur.Length > 0 && cur.Length + 1 + w.Length > max) { lines.Add(cur); cur = w; }
            else cur = cur.Length > 0 ? cur + " " + w : w;
        }
        if (cur.Length > 0) lines.Add(cur);
        return string.Join("\n", lines);
    }

    Card Make(string key, string text, bool isBlack, Vector3 from, Quaternion fromRot)
    {
        var g = new GameObject("carte");
        g.transform.SetParent(transform, false);
        var c = g.AddComponent<Card>();
        c.key = key; c.text = text; c.black = isBlack;
        float w = isBlack ? QW : W, h = isBlack ? QH : H;
        var body = Prim(PrimitiveType.Cube, g.transform, Vector3.zero, new Vector3(w, Th, h), isBlack ? black : red);
        c.body = body.GetComponent<Renderer>();
        var box = g.AddComponent<BoxCollider>(); box.size = new Vector3(w, Th * 4, h);
        // Recto : le texte ; verso : le logo (vu quand la carte est retournee).
        c.front = new GameObject("recto").AddComponent<TextMesh>();
        c.front.transform.SetParent(g.transform, false);
        c.front.transform.localPosition = new Vector3(0, Th / 2 + 0.0004f, 0);
        c.front.transform.localRotation = Quaternion.Euler(90, 0, 0);
        c.front.font = font; c.front.GetComponent<MeshRenderer>().sharedMaterial = RoueView.TextMat(font);
        c.front.fontSize = 64; c.front.anchor = TextAnchor.MiddleCenter; c.front.alignment = TextAlignment.Center;
        c.front.color = Color.white;
        c.front.text = text == null ? "" : Wrap(text.Replace("_", "_____"), isBlack ? 15 : 11);
        int lines = c.front.text.Split('\n').Length;
        c.front.characterSize = (isBlack ? 0.0016f : 0.0017f) * (lines > 6 ? 0.78f : lines > 4 ? 0.9f : 1f);
        var back = new GameObject("verso").AddComponent<TextMesh>();
        back.transform.SetParent(g.transform, false);
        back.transform.localPosition = new Vector3(0, -Th / 2 - 0.0004f, 0);
        back.transform.localRotation = Quaternion.Euler(-90, 0, 180);
        back.font = font; back.GetComponent<MeshRenderer>().sharedMaterial = RoueView.TextMat(font);
        back.fontSize = 64; back.characterSize = 0.0016f; back.anchor = TextAnchor.MiddleCenter; back.alignment = TextAlignment.Center;
        back.color = isBlack ? Board.Hex("e8403a") : Color.white; back.text = "LIMITE\nLIMITE";
        g.transform.localPosition = from; g.transform.localRotation = fromRot;
        cards[key] = c;
        return c;
    }

    // --- Ou va chaque carte, d'apres la partie --------------------------------------------------------------------------
    public void Sync(bool snap = false)
    {
        if (ll == null) return;
        var keep = new HashSet<string>();
        void Place(string key, string text, bool isBlack, Vector3 pos, Quaternion rot, Vector3 from, Quaternion fromRot, int seat = -1, bool mine = false, float scale = 1)
        {
            bool fresh = !cards.TryGetValue(key, out var c) || !c;
            if (fresh) { c = Make(key, text, isBlack, from, fromRot); if (!snap) Sfx(isBlack ? "bj_slide2" : Random.value < 0.5f ? "bj_slide1" : "bj_slide2", 0.5f); }
            else if (!snap && Vector3.Dot(c.rot * Vector3.up, rot * Vector3.up) < 0) Sfx("bj_flip", 0.7f);   // la carte se retourne
            else if (!snap && Vector3.Distance(c.pos, pos) > 0.15f) Sfx(Random.value < 0.5f ? "bj_place1" : "bj_place2", 0.45f);   // elle change de place
            c.pos = pos; c.rot = rot; c.seat = seat; c.mine = mine; c.scale = scale; c.gone = false;
            keep.Add(key);
        }
        // Question du tour : de la pioche noire au centre, face visible tournee vers moi.
        Place("q" + ll.round, ll.question, true, Mid + new Vector3(0, 0.002f, 0.06f), FaceUp(Quaternion.identity), BlackDeck + Vector3.up * 0.01f, FaceDown(Quaternion.identity));
        // Mains : la mienne en eventail devant moi (face visible), celles des autres de dos devant eux.
        for (int s = 0; s < N; s++)
        {
            var hand = ll.players[s].hand;
            bool mineSeat = s == me;
            for (int i = 0; i < hand.Count; i++)
            {
                float t = i - (hand.Count - 1) / 2f;
                var yawRot = Toward(s) * Quaternion.Euler(0, t * 4, 0);
                var p = Spot(s, Rad) + Toward(s) * new Vector3(t * (mineSeat ? W * 1.04f : W * 0.55f), 0.001f + i * 0.0006f, -Mathf.Abs(t) * 0.004f);
                string text = hand[i];
                string key = mineSeat ? $"h{s}:{text}:{hand.Take(i).Count(x => x == text)}" : $"h{s}:{i}";
                bool sel = mineSeat && selection.Contains(text);
                if (sel) p += Toward(s) * new Vector3(0, 0.01f, 0.03f);
                Place(key, mineSeat ? text : null, false, p, mineSeat ? FaceUp(yawRot) : FaceDown(yawRot), RedDeck + Vector3.up * 0.01f, FaceDown(Quaternion.identity), s, mineSeat, mineSeat ? 1.2f : 0.9f);
            }
        }
        // Cartes jouees : face cachee devant chaque joueur, puis au centre (melangees) pour le jugement.
        bool judging = ll.phase == LPhase.Judge || ll.phase == LPhase.Result;
        foreach (var kv in ll.played)
        {
            int s = kv.Key;
            for (int j = 0; j < kv.Value.Length; j++)
            {
                Vector3 pos; Quaternion rot;
                int slot = judging ? ll.order.IndexOf(s) : -1;
                if (slot >= 0)
                {
                    // Rangee au centre, sous la question, chaque groupe cote a cote ; tournee vers moi.
                    int n = ll.order.Count, per = kv.Value.Length;
                    float gw = per * W + 0.012f;
                    float x = (slot - (n - 1) / 2f) * Mathf.Min(gw + 0.02f, 0.95f / Mathf.Max(1, n - 1)) + (j - (per - 1) / 2f) * W * 1.02f;
                    bool open = ll.phase == LPhase.Result || slot < revealed;
                    bool win = j < ll.picks.Count && ll.picks[j] == s;   // reponse choisie pour ce trou
                    pos = Mid + new Vector3(x, win ? 0.03f : 0.004f, -0.1f);
                    rot = open ? FaceUp(Quaternion.identity) : FaceDown(Quaternion.identity);
                    Place($"p{ll.round}:{s}:{j}", kv.Value[j].StartsWith("*") ? kv.Value[j].Substring(1) : kv.Value[j], false, pos, rot, Spot(s, Rad), FaceDown(Toward(s)), s, false, win ? 1.35f : 1.15f);
                    cards[$"p{ll.round}:{s}:{j}"].winner = win;
                }
                else
                {
                    pos = Spot(s, Rad - 0.15f) + Toward(s) * new Vector3((j - (kv.Value.Length - 1) / 2f) * W * 0.7f, 0.002f + j * 0.001f, 0);
                    Place($"p{ll.round}:{s}:{j}", kv.Value[j].StartsWith("*") ? kv.Value[j].Substring(1) : kv.Value[j], false, pos, FaceDown(Toward(s)), Spot(s, Rad), FaceDown(Toward(s)), s);
                }
            }
        }
        // Le reste (cartes d'une manche finie, cartes jouees parties de la main) : vers la defausse.
        foreach (var c in cards.Values.ToList())
        {
            if (!c || keep.Contains(c.key)) continue;
            if (c.key.StartsWith("h") && !c.gone) { Destroy(c.gameObject); cards.Remove(c.key); continue; }   // jouee : remplacee par sa copie posee
            c.gone = true; c.pos = Discard + Vector3.up * 0.01f; c.rot = FaceDown(Quaternion.Euler(0, Random.Range(-30f, 30f), 0)); c.scale = 1;
        }
        // Noms des auteurs une fois le choix fait (le Boss ne les voit pas avant).
        foreach (var a in authors) if (a) Destroy(a.gameObject);
        authors.Clear();
        if (ll.phase == LPhase.Result)
            for (int k = 0; k < ll.order.Count; k++)
            {
                int s = ll.order[k];
                var first = cards.Values.FirstOrDefault(c => c && c.key == $"p{ll.round}:{s}:0");
                if (!first) continue;
                var pos = first.pos + new Vector3((ll.played[s].Length - 1) * W * 0.51f, 0.002f, -H * 0.75f);
                authors.Add(Label(root, pos, Quaternion.identity, ll.players[s].name, 0.0035f, Board.Colors[s % Board.Colors.Length]));
            }
        for (int s = 0; s < names.Count; s++)
            if (names[s]) names[s].text = ll.players[s].name + (s == ll.boss ? "  ★ BOSS" : "") + $"  ({ll.players[s].score})";
        foreach (var c in cards.Values) if (c && !keep.Contains(c.key)) c.winner = false;
        if (snap) foreach (var c in cards.Values) if (c) { c.transform.localPosition = c.pos; c.transform.localRotation = c.rot; }
    }

    // Sons des cartes : un a la fois au plus toutes les 60 ms (une distribution ne fait pas un vacarme).
    float sfxAt;
    void Sfx(string n, float vol) { if (Time.time < sfxAt) return; sfxAt = Time.time + 0.06f; Sound.I.Play(n, vol, 0.12f); }

    void Update()
    {
        if (ll == null) return;
        float k = 1 - Mathf.Exp(-Time.deltaTime * 7);
        foreach (var c in cards.Values.ToList())
        {
            if (!c) continue;
            var t = c.transform;
            // En vol : la carte monte un peu (arc) quand elle est loin de sa place.
            float d = Vector3.Distance(t.localPosition, c.pos);
            var target = c.pos + Vector3.up * Mathf.Min(0.08f, d * 0.35f);
            t.localPosition = Vector3.Lerp(t.localPosition, d < 0.01f ? c.pos : target, k);
            t.localRotation = Quaternion.Slerp(t.localRotation, c.rot, k);
            float sc = (c == hover && !c.gone ? 1.08f : 1) * c.scale;
            if (c.winner) sc *= 1 + 0.05f * Mathf.Abs(Mathf.Sin(Time.time * 4));   // la gagnante rebondit
            t.localScale = Vector3.Lerp(t.localScale, Vector3.one * sc, k);
            // Halo dore autour de la reponse choisie.
            if (c.winner && !c.halo)
            {
                c.halo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(c.halo.GetComponent<Collider>());
                c.halo.transform.SetParent(c.transform, false);
                c.halo.transform.localPosition = new Vector3(0, -Th, 0);
                c.halo.transform.localScale = new Vector3(W * 1.18f, Th * 0.5f, H * 1.14f);
                c.halo.GetComponent<Renderer>().sharedMaterial = gold;
            }
            if (c.halo) c.halo.SetActive(c.winner);
            if (c.gone && d < 0.01f) { cards.Remove(c.key); Destroy(c.gameObject); }
        }
    }

    // Ma carte (dans la main) qui porte ce texte : pour placer la bulle "Poser" au-dessus.
    public Card MineWith(string text) => cards.Values.FirstOrDefault(c => c && c.mine && c.text == text);
    public Card AnyMine() => cards.Values.FirstOrDefault(c => c && c.mine);   // autotest : apercu d'une carte de ma main

    // --- Souris ---------------------------------------------------------------------------------------------------------
    public Card Pick(Ray worldRay)
    {
        Card best = null; float bestD = float.MaxValue;
        foreach (var h in Physics.RaycastAll(worldRay, 20f))
        {
            var c = h.collider.GetComponent<Card>();
            if (c && !c.gone && h.distance < bestD) { best = c; bestD = h.distance; }
        }
        return best;
    }
    // Une carte est lisible si elle est face visible (ou c'est la mienne).
    public static bool Readable(Card c) => c && (c.mine || Vector3.Dot(c.transform.up, Vector3.up) > 0.5f) && !string.IsNullOrEmpty(c.text);

    // --- Camera : depuis ma place, en hauteur, tournee vers le centre --------------------------------------------------------
    public Pose CamPose(float dist)
    {
        var center = transform.TransformPoint(Mid + Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0, -0.04f));
        var from = center + Quaternion.Euler(72, yaw, 0) * Vector3.back * dist;
        return new Pose(from, Quaternion.LookRotation(center - from));
    }
    public void Turn(float dx) => yaw = Mathf.Clamp(yaw + dx, -60, 60);
}
