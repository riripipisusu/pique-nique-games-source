using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Serpents et echelles sur la nappe de la clairiere : plateau en bois (dessus peint par Tools/serpents_plateau.py),
// echelles en bois et serpents en 3D poses sur les cases, pions du pack "Board Game Items" (Resources/Serpents/Pions,
// hors depot ; a defaut, pions simples), et le de lance a la main (De.cs). Une seule vue pour tous, face au plateau.
public class SerpentsView : MonoBehaviour
{
    const float Cs = 0.085f, Half = Cs * 5, SlabH = 0.035f, PawnScale = 2.4f;
    static readonly Vector3 BoardAt = new Vector3(0, 0.021f, 0.5f);
    static readonly string[] PawnColor = { "red", "blue", "green", "yellow" };
    static readonly Color[] PawnTint = { Board.Hex("e5322d"), Board.Hex("1f6fc5"), Board.Hex("3aa84a"), Board.Hex("f7c315") };
    public static Color ColorOf(int seat) => PawnTint[seat % PawnTint.Length];

    Serpents sp;
    Material lit;
    Transform pawnRoot;
    Transform[] pawns;
    public De de;
    public float speed = 1;
    public float Speed { set { speed = value; if (de) de.speed = value; } }
    float Dt => Time.deltaTime * speed;

    void Awake()
    {
        transform.position = Clairiere.Center;
        lit = Resources.Load<Material>("Lit");
        BuildBoard();
        int li = 0;
        foreach (var kv in Serpents.Ladders) BuildLadder(kv.Key, kv.Value, li++);
        int k = 0;
        foreach (var kv in Serpents.Snakes) BuildSnake(kv.Key, kv.Value, k++);
        de = new GameObject("de").AddComponent<De>();
        de.transform.SetParent(transform, false);
        de.Init(lit, BoardAt + Vector3.up * SlabH, Half, _ => BoardAt + Vector3.right * (Half + 0.13f) + Vector3.back * Half * 0.55f);
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
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
        return g;
    }

    // Case n (1..100) -> centre de la case sur le dessus du plateau ; 0 = hors plateau, a gauche de la case 1.
    public static Vector3 CellPos(int n)
    {
        if (n <= 0) return BoardAt + new Vector3(-Half - 0.07f, SlabH - 0.035f, -Half + Cs * 1.5f);
        int r = (n - 1) / 10, c = r % 2 == 0 ? (n - 1) % 10 : 9 - (n - 1) % 10;
        return BoardAt + new Vector3((c - 4.5f) * Cs, SlabH, (r - 4.5f) * Cs);
    }
    // Plusieurs pions sur une case : chacun dans un coin.
    Vector3 PawnPos(int seat, int n)
    {
        if (n <= 0) return CellPos(0) + Vector3.forward * (seat - 1.5f) * 0.055f;   // au depart : alignes a cote du plateau
        int others = 0;
        for (int s = 0; s < sp.players.Count; s++) if (s != seat && sp.players[s].pos == n) others++;
        var off = others == 0 && n > 0 ? Vector3.zero : new Vector3(seat % 2 == 0 ? -1 : 1, 0, seat < 2 ? -1 : 1) * Cs * 0.22f;
        return CellPos(n) + off;
    }

    // --- Plateau en bois ------------------------------------------------------------------------------------------
    void BuildBoard()
    {
        var root = new GameObject("plateau").transform;
        root.SetParent(transform, false);
        Prim(PrimitiveType.Cube, BoardAt + Vector3.up * SlabH / 2, new Vector3(Half * 2 + 0.05f, SlabH, Half * 2 + 0.05f), Mat(Board.Hex("8a5530")), root);
        var dark = Mat(Board.Hex("5e3818"));
        const float fw = 0.03f, fh = 0.014f;
        for (int k = 0; k < 4; k++)
        {
            bool x = k % 2 == 0; float s = k < 2 ? 1 : -1;
            Prim(PrimitiveType.Cube, BoardAt + new Vector3(x ? 0 : s * (Half + fw / 2), SlabH + fh / 2, x ? s * (Half + fw / 2) : 0),
                 x ? new Vector3(Half * 2 + fw * 2, fh, fw) : new Vector3(fw, fh, Half * 2), dark, root);
        }
        var top = new Material(lit) { color = Color.white };
        top.SetTexture("_BaseMap", Resources.Load<Texture2D>("Serpents/plateau"));
        top.SetFloat("_Smoothness", 0.35f);
        var q = Prim(PrimitiveType.Quad, BoardAt + Vector3.up * (SlabH + 0.0006f), new Vector3(Half * 2, Half * 2, 1), top, root);
        q.transform.localRotation = Quaternion.Euler(90, 0, 0);
    }

    // --- Echelle : deux montants et des barreaux, couchee sur les cases -------------------------------------------------
    // Couleurs des echelles, dans l'ordre de Serpents.Ladders (comme sur le modele) : 1 rouge, 4 bois, 9 rose, 21 rose, 28 bleu, 51 jaune, 71 dore, 80 brun.
    static readonly string[] LadderColors = { "e5322d", "d9a066", "f2a0c8", "f2a0c8", "4a90d9", "f7c315", "c9a44a", "8a5a2b" };
    void BuildLadder(int from, int to, int index)
    {
        var a = CellPos(from) + Vector3.up * 0.006f; var b = CellPos(to) + Vector3.up * 0.006f;
        var root = new GameObject($"echelle {from}-{to}").transform;
        root.SetParent(transform, false);
        root.localPosition = a;
        root.localRotation = Quaternion.LookRotation(b - a);
        float len = Vector3.Distance(a, b), w = Cs * 0.42f;
        var wood = Mat(Board.Hex(LadderColors[index % LadderColors.Length]), 0.25f);
        foreach (float sx in new[] { -1f, 1f })
        {
            var rail = Prim(PrimitiveType.Cylinder, new Vector3(sx * w / 2, 0.006f, len / 2), new Vector3(0.011f, len / 2, 0.011f), wood, root);
            rail.transform.localRotation = Quaternion.Euler(90, 0, 0);
            rail.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        var rungW = Mat(Color.Lerp(Board.Hex(LadderColors[index % LadderColors.Length]), Color.black, 0.15f), 0.25f);
        int rungs = Mathf.Max(3, Mathf.RoundToInt(len / (Cs * 0.45f)));
        for (int i = 1; i < rungs; i++)
        {
            var r = Prim(PrimitiveType.Cylinder, new Vector3(0, 0.007f, len * i / rungs), new Vector3(0.007f, w / 2, 0.007f), rungW, root);
            r.transform.localRotation = Quaternion.Euler(0, 0, 90);
        }
    }

    // --- Serpent : tube ondule de la tete (grosse, avec yeux et langue) a la queue (fine), rayures ---------------------------
    readonly Dictionary<int, Vector3[]> snakeCurve = new Dictionary<int, Vector3[]>();   // tete -> points (tete vers queue)
    // Dans l'ordre de Serpents.Snakes (comme sur le modele) : 17 bleu, 54 rouge, 62 rouge, 64 brun, 87 blanc, 93 rouge, 95 rouge, 98 vert.
    static readonly Color[] SnakeColors = { Board.Hex("4a90d9"), Board.Hex("e5322d"), Board.Hex("e5322d"), Board.Hex("9a5a3a"), Board.Hex("eef2f7"), Board.Hex("e5322d"), Board.Hex("d8402f"), Board.Hex("3aa84a") };

    static Texture2D stripes;
    static Texture2D Stripes()
    {
        if (stripes) return stripes;
        stripes = new Texture2D(4, 64, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
        for (int y = 0; y < 64; y++) for (int x = 0; x < 4; x++) stripes.SetPixel(x, y, y % 16 < 11 ? Color.white : new Color(0.55f, 0.55f, 0.55f));
        stripes.Apply();
        return stripes;
    }

    void BuildSnake(int head, int tail, int index)
    {
        var a = CellPos(head); var b = CellPos(tail);
        var dir = b - a; float len = dir.magnitude;
        var side = Vector3.Cross(Vector3.up, dir.normalized);
        const int N = 60, R = 10;
        float waves = Mathf.Max(1.5f, len / (Cs * 1.6f));
        var pts = new Vector3[N + 1];
        for (int i = 0; i <= N; i++)
        {
            float t = i / (float)N;
            pts[i] = a + dir * t + side * Mathf.Sin(t * waves * Mathf.PI * 2) * Cs * 0.35f * Mathf.Sin(t * Mathf.PI * 0.9f + 0.15f) + Vector3.up * 0.012f;
        }
        snakeCurve[head] = pts;
        // Tube : anneaux de R sommets le long de la courbe, rayon qui diminue vers la queue.
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        for (int i = 0; i <= N; i++)
        {
            float t = i / (float)N;
            var fwd = (pts[Mathf.Min(N, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized;
            var rgt = Vector3.Cross(Vector3.up, fwd).normalized; var up = Vector3.Cross(fwd, rgt);
            float rad = Mathf.Lerp(0.014f, 0.003f, t * t) * (1 + 0.1f * Mathf.Sin(t * 30));
            for (int k = 0; k <= R; k++)
            {
                float ang = k / (float)R * Mathf.PI * 2;
                v.Add(pts[i] + (rgt * Mathf.Cos(ang) + up * Mathf.Sin(ang) * 0.75f) * rad);
                uv.Add(new Vector2(k / (float)R, t * len / 0.03f));
            }
        }
        for (int i = 0; i < N; i++)
            for (int k = 0; k < R; k++)
            {
                int p0 = i * (R + 1) + k, p1 = p0 + R + 1;
                tri.AddRange(new[] { p0, p1, p0 + 1, p0 + 1, p1, p1 + 1 });
            }
        var mesh = new Mesh { vertices = v.ToArray(), uv = uv.ToArray(), triangles = tri.ToArray() };
        mesh.RecalculateNormals();
        var col = SnakeColors[index % SnakeColors.Length];
        var skin = new Material(lit) { color = col };
        skin.SetTexture("_BaseMap", Stripes());
        skin.SetFloat("_Smoothness", 0.6f);
        var g = new GameObject($"serpent {head}-{tail}");
        g.transform.SetParent(transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = skin;
        // Tete : un peu relevee, deux yeux et une langue fourchue.
        var fwd0 = (pts[0] - pts[2]).normalized;
        var headT = new GameObject("tete").transform;
        headT.SetParent(g.transform, false);
        headT.localPosition = pts[0] + fwd0 * 0.006f + Vector3.up * 0.006f;
        headT.localRotation = Quaternion.LookRotation(fwd0);
        Prim(PrimitiveType.Sphere, Vector3.zero, new Vector3(0.032f, 0.02f, 0.038f), Mat(col, 0.6f), headT);
        var white = Mat(Color.white, 0.8f); var black = Mat(Board.Hex("111111"), 0.9f);
        foreach (float sx in new[] { -1f, 1f })
        {
            Prim(PrimitiveType.Sphere, new Vector3(sx * 0.009f, 0.008f, 0.006f), Vector3.one * 0.009f, white, headT);
            Prim(PrimitiveType.Sphere, new Vector3(sx * 0.0095f, 0.011f, 0.009f), Vector3.one * 0.0045f, black, headT);
        }
        Prim(PrimitiveType.Cube, new Vector3(0, -0.002f, 0.026f), new Vector3(0.003f, 0.001f, 0.018f), Mat(Board.Hex("d81b3a")), headT);
    }

    // --- Pions -------------------------------------------------------------------------------------------------------------
    public void Build(Serpents s)
    {
        Clairiere.Show(true);
        gameObject.SetActive(true);
        sp = s;
        if (pawnRoot) Destroy(pawnRoot.gameObject);
        pawnRoot = new GameObject("pions").transform;
        pawnRoot.SetParent(transform, false);
        pawns = new Transform[s.players.Count];
        var tex = Resources.Load<Texture2D>("Serpents/Pions/pions");
        for (int i = 0; i < s.players.Count; i++)
        {
            var p = new GameObject("pion " + i).transform;
            p.SetParent(pawnRoot, false);
            var model = Resources.Load<GameObject>("Serpents/Pions/pion_" + PawnColor[i % PawnColor.Length]);
            if (model && tex)
            {
                var m = new Material(lit) { color = Color.white };
                m.SetTexture("_BaseMap", tex); m.SetFloat("_Smoothness", 0.55f);
                var g = Instantiate(model, p);
                foreach (var r in g.GetComponentsInChildren<Renderer>()) r.sharedMaterial = m;
                g.transform.localScale = Vector3.one * PawnScale;
            }
            else   // sans le pack : pion simple (socle, corps, tete)
            {
                var m = Mat(ColorOf(i), 0.5f);
                Prim(PrimitiveType.Cylinder, new Vector3(0, 0.005f, 0), new Vector3(0.036f, 0.005f, 0.036f), Mat(Board.Hex("f4efe6")), p);
                Prim(PrimitiveType.Cylinder, new Vector3(0, 0.03f, 0), new Vector3(0.024f, 0.025f, 0.024f), m, p);
                Prim(PrimitiveType.Sphere, new Vector3(0, 0.065f, 0), Vector3.one * 0.026f, m, p);
            }
            var box = p.gameObject.AddComponent<BoxCollider>();   // le de rebondit dessus
            box.center = new Vector3(0, 0.037f, 0); box.size = new Vector3(0.035f, 0.075f, 0.035f);
            pawns[i] = p;
        }
        de.Park(s.turn);
        Sync();
    }

    public void Hide() { StopAllCoroutines(); sp = null; busy = false; gameObject.SetActive(false); Clairiere.Show(false); }

    public void Sync()
    {
        if (sp == null) return;
        for (int s = 0; s < sp.players.Count; s++) { pawns[s].localPosition = PawnPos(s, sp.players[s].pos); pawns[s].localRotation = Quaternion.identity; }
    }

    // Pion du joueur actif : il sautille doucement quand c'est a lui de lancer.
    void LateUpdate()
    {
        if (sp == null || busy || sp.Finished) return;
        var p = pawns[sp.turn];
        p.localPosition = PawnPos(sp.turn, sp.players[sp.turn].pos) + Vector3.up * Mathf.Abs(Mathf.Sin(Time.time * 4)) * 0.012f;
    }

    // --- Camera : face au plateau, un peu en hauteur (la meme pour tous) ---------------------------------------------------
    public Pose CamPose(float dist)
    {
        var target = transform.TransformPoint(BoardAt + Vector3.up * 0.02f + Vector3.forward * 0.02f);
        var from = target + Quaternion.Euler(60, 0, 0) * Vector3.back * dist;
        return new Pose(from, Quaternion.LookRotation(target - from));
    }

    // --- Animations -----------------------------------------------------------------------------------------------------------
    public bool busy, thrownHere;

    public IEnumerator Play(List<SerpEvent> evs, Action<SerpEvent> say)
    {
        busy = true;
        foreach (var e in evs)
        {
            if (e.type != SEv.Rolled) say?.Invoke(e);
            switch (e.type)
            {
                case SEv.Rolled: yield return de.Play(e.fling, e.value, e.seat, !thrownHere); thrownHere = false; say?.Invoke(e); break;
                case SEv.Moved: yield return Walk(e); break;
                case SEv.Ladder: yield return Climb(e); break;
                case SEv.Snake: yield return Slide(e); break;
                case SEv.Bumped: yield return Bump(e); break;
                case SEv.Won: yield return Celebrate(e.seat); break;
                case SEv.Turn: if (evs.Count > 1) yield return de.Collect(e.seat); break;
            }
        }
        Sync();
        busy = false;
    }

    IEnumerator Hop(Transform p, Vector3 a, Vector3 b, float height, float dur, int n)
    {
        Sound.I.Play("hop" + (n % 3 + 1), 0.55f);
        for (float t = 0; t < 1; t += Dt / dur)
        {
            p.localPosition = Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * height;
            yield return null;
        }
        p.localPosition = b;
    }

    // Case par case (rebond au bout compris).
    IEnumerator Walk(SerpEvent e)
    {
        var p = pawns[e.seat];
        var at = p.localPosition;
        for (int i = 0; i < e.path.Length; i++)
        {
            var next = CellPos(e.path[i]);
            yield return Hop(p, at, next, e.from == 0 && i == 0 ? 0.08f : 0.035f, 0.17f, i);
            at = next;
        }
        yield return new WaitForSeconds(0.1f / speed);
    }

    // Echelle : il grimpe barreau par barreau.
    IEnumerator Climb(SerpEvent e)
    {
        var p = pawns[e.seat];
        Sound.I.Play("arrive", 0.7f);
        Vector3 a = CellPos(e.from), b = CellPos(e.to);
        int steps = Mathf.Max(3, Mathf.RoundToInt(Vector3.Distance(a, b) / (Cs * 0.45f)));
        for (int i = 0; i < steps; i++)
            yield return Hop(p, Vector3.Lerp(a, b, i / (float)steps), Vector3.Lerp(a, b, (i + 1f) / steps), 0.02f, 0.09f, i);
        yield return new WaitForSeconds(0.15f / speed);
    }

    // Serpent : il glisse le long du corps, de la tete a la queue, en tournoyant un peu.
    IEnumerator Slide(SerpEvent e)
    {
        var p = pawns[e.seat];
        Sound.I.Play("fall", 0.8f);
        if (!snakeCurve.TryGetValue(e.from, out var pts)) { p.localPosition = CellPos(e.to); yield break; }
        float dur = 0.5f + pts.Length * 0.012f;
        for (float t = 0; t < 1; t += Dt / dur)
        {
            float e2 = t * t * (3 - 2 * t);
            float f = e2 * (pts.Length - 1); int i = Mathf.Min(pts.Length - 2, (int)f);
            p.localPosition = Vector3.Lerp(pts[i], pts[i + 1], f - i) + Vector3.up * 0.008f;
            p.localRotation = Quaternion.Euler(0, t * 540, 0);
            yield return null;
        }
        p.localRotation = Quaternion.identity;
        yield return Hop(p, p.localPosition, CellPos(e.to), 0.03f, 0.2f, 0);
    }

    // Case deja occupee : le pion saute tres haut en tournoyant et retombe au depart.
    IEnumerator Bump(SerpEvent e)
    {
        var p = pawns[e.seat];
        Sound.I.Play("fall");
        var a = p.localPosition; var b = PawnPos(e.seat, 0);
        for (float t = 0; t < 1; t += Dt / 0.8f)
        {
            p.localPosition = Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.3f;
            p.localRotation = Quaternion.Euler(t * 720, 0, t * 360);
            yield return null;
        }
        p.localPosition = b; p.localRotation = Quaternion.identity;
    }

    IEnumerator Celebrate(int seat)
    {
        Sound.I.Play("win");
        var p = pawns[seat];
        var at = p.localPosition;
        for (int k = 0; k < 4; k++) yield return Hop(p, at, at, 0.1f, 0.35f, k);
        yield return new WaitForSeconds(0.5f / speed);
    }
}
