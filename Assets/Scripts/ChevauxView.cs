using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Petits chevaux sur la nappe de la clairiere : plateau en bois (dessus peint par Tools/chevaux_plateau.py),
// pions cavaliers (Tools/cheval_blender.py) aux couleurs des joueurs, de lance a la main (De.cs).
// Grille 15 x 15 : colonne c, ligne r (r = 0 au fond, cote +z) ; meme geometrie que le script du plateau.
public class ChevauxView : MonoBehaviour
{
    const float Cs = 0.06f, Half = Cs * 7.5f, SlabH = 0.035f;
    static readonly Vector3 BoardAt = new Vector3(0, 0.021f, 0.5f);   // sur la nappe (Clairiere)
    float Top => BoardAt.y + SlabH;

    Chevaux ch;
    Material lit;
    Transform pawnRoot;
    Transform[,] pawns;
    public float speed = 1;

    void Awake()
    {
        transform.position = Clairiere.Center;
        lit = Resources.Load<Material>("Lit");
        BuildBoard();
        de = new GameObject("de").AddComponent<De>();
        de.transform.SetParent(transform, false);
        de.Init(lit, BoardAt + Vector3.up * SlabH, Half, s => DieHome(s));
        gameObject.SetActive(false);
    }

    Material Mat(Color c, float smooth = 0.2f) { var m = new Material(lit) { color = c }; m.SetFloat("_Smoothness", smooth); return m; }

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

    // --- Geometrie du plateau ------------------------------------------------------------------------------
    static readonly List<Vector2Int> TrackCells = BuildTrack();
    static List<Vector2Int> BuildTrack()
    {
        var t = new List<Vector2Int>();
        void Add(int c, int r) => t.Add(new Vector2Int(c, r));
        for (int r = 0; r < 6; r++) Add(8, r);
        Add(8, 6); for (int c = 9; c < 15; c++) Add(c, 6); Add(14, 7);
        for (int c = 14; c > 8; c--) Add(c, 8); Add(8, 8); for (int r = 9; r < 15; r++) Add(8, r); Add(7, 14);
        for (int r = 14; r > 8; r--) Add(6, r); Add(6, 8); for (int c = 5; c >= 0; c--) Add(c, 8); Add(0, 7);
        for (int c = 0; c < 6; c++) Add(c, 6); Add(6, 6); for (int r = 5; r >= 0; r--) Add(6, r); Add(7, 0);
        return t;
    }
    static Vector2Int Ladder(int arm, int step) => arm == 0 ? new Vector2Int(7, step) : arm == 1 ? new Vector2Int(14 - step, 7) : arm == 2 ? new Vector2Int(7, 14 - step) : new Vector2Int(step, 7);
    static readonly Vector2Int[] StableCorner = { new Vector2Int(9, 0), new Vector2Int(9, 9), new Vector2Int(0, 9), new Vector2Int(0, 0) };
    static readonly Vector3[] ArmDir = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };   // du centre vers le bout du bras

    // Coordonnees de texture (en cases, depuis le coin haut-gauche) -> position locale sur le dessus du plateau.
    Vector3 At(float u, float v) => BoardAt + new Vector3((u - 7.5f) * Cs, SlabH, (7.5f - v) * Cs);
    Vector3 CellPos(Vector2Int c) => At(c.x + 0.5f, c.y + 0.5f);

    public Vector3 HorsePos(int seat, int horse, int progress)
    {
        int arm = ch.players[seat].arm;
        if (progress < 0)
        {
            var s = StableCorner[arm];
            return At(s.x + 1.75f + horse % 2 * 2.5f, s.y + 1.75f + horse / 2 * 2.5f);
        }
        if (progress <= Chevaux.Foot) return CellPos(TrackCells[ch.Cell(seat, progress)]);
        if (progress < Chevaux.Home) return CellPos(Ladder(arm, progress - Chevaux.Foot));
        // Arrives : ranges sur la nappe, au bord du plateau du cote de leur joueur (au bout de leur bras).
        var side = Vector3.Cross(Vector3.up, ArmDir[arm]);
        return BoardAt + ArmDir[arm] * (Half + 0.075f) + side * (horse - (ch.count - 1) / 2f) * 0.075f;
    }

    // Le cheval regarde vers la case suivante ; a l'ecurie, vers le centre du plateau.
    Quaternion HorseRot(int seat, int progress)
    {
        int arm = ch.players[seat].arm;
        if (progress < 0 || progress >= Chevaux.Home) return Quaternion.LookRotation(-ArmDir[arm]);
        if (progress >= Chevaux.Foot) return Quaternion.LookRotation(-ArmDir[arm]);
        var a = CellPos(TrackCells[ch.Cell(seat, progress)]);
        var b = CellPos(TrackCells[ch.Cell(seat, progress + 1)]);
        return Quaternion.LookRotation(b - a);
    }

    // --- Plateau en bois --------------------------------------------------------------------------------------
    void BuildBoard()
    {
        var wood = Mat(Board.Hex("9a6232"), 0.3f);
        var dark = Mat(Board.Hex("6e4220"), 0.3f);
        var root = new GameObject("plateau").transform;
        root.SetParent(transform, false);
        Prim(PrimitiveType.Cube, BoardAt + Vector3.up * SlabH / 2, new Vector3(Half * 2 + 0.05f, SlabH, Half * 2 + 0.05f), wood, root);
        const float fw = 0.028f, fh = 0.012f;   // cadre en relief
        for (int k = 0; k < 4; k++)
        {
            bool x = k % 2 == 0;
            float s = k < 2 ? 1 : -1;
            var pos = BoardAt + new Vector3(x ? 0 : s * (Half + fw / 2), SlabH + fh / 2, x ? s * (Half + fw / 2) : 0);
            Prim(PrimitiveType.Cube, pos, x ? new Vector3(Half * 2 + fw * 2, fh, fw) : new Vector3(fw, fh, Half * 2), dark, root);
        }
        var top = new Material(lit) { color = Color.white };
        top.SetTexture("_BaseMap", Resources.Load<Texture2D>("Chevaux/plateau"));
        top.SetFloat("_Smoothness", 0.35f);
        var q = Prim(PrimitiveType.Quad, BoardAt + Vector3.up * (SlabH + 0.0006f), new Vector3(Half * 2, Half * 2, 1), top, root);
        q.transform.localRotation = Quaternion.Euler(90, 0, 0);
    }

    // --- De (De.cs) : il attend sur la nappe, au bord du plateau a cote de l'ecurie du joueur qui doit le lancer
    //     (a gauche ou a droite : visible quelle que soit la place de la camera) --------------------------------------
    public De de;
    Vector3 DieHome(int seat)
    {
        var s = StableCorner[ch.players[seat].arm];
        var mid = At(s.x + 3, s.y + 3);
        return new Vector3(Mathf.Sign(mid.x - BoardAt.x) * (Half + 0.12f) + BoardAt.x, BoardAt.y, mid.z);
    }

    // --- Pions -----------------------------------------------------------------------------------------------
    public void Build(Chevaux c)
    {
        Clairiere.Show(true);
        gameObject.SetActive(true);
        ch = c;
        if (pawnRoot) Destroy(pawnRoot.gameObject);
        pawnRoot = new GameObject("pions").transform;
        pawnRoot.SetParent(transform, false);
        pawns = new Transform[c.players.Count, c.count];
        var prefab = Resources.Load<GameObject>("Models/Cheval");
        for (int s = 0; s < c.players.Count; s++)
        {
            var body = Mat(Board.Colors[c.ColorOf(s)], 0.45f);
            var eye = Mat(Board.Hex("1a1412"), 0.6f);
            for (int h = 0; h < ch.count; h++)
            {
                var p = Instantiate(prefab, pawnRoot).transform;
                p.name = $"cheval {s}-{h}";
                p.localScale = Vector3.one * 0.07f;
                foreach (var r in p.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++) mats[k] = mats[k] && mats[k].name.StartsWith("Oeil") ? eye : body;
                    r.sharedMaterials = mats;
                }
                var box = p.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0, 0.5f, 0); box.size = new Vector3(0.9f, 1.1f, 0.9f);
                pawns[s, h] = p;
            }
        }
        de.Park(c.turn);
        Sync();
    }

    public void Hide() { StopAllCoroutines(); ch = null; gameObject.SetActive(false); Clairiere.Show(false); }

    // Tous les chevaux a leur place (etat des regles).
    public void Sync()
    {
        if (ch == null) return;
        for (int s = 0; s < ch.players.Count; s++)
            for (int h = 0; h < ch.count; h++)
            {
                int pr = ch.players[s].horses[h];
                pawns[s, h].localPosition = HorsePos(s, h, pr);
                pawns[s, h].localRotation = HorseRot(s, pr);
            }
    }

    // Cheval du joueur actif sous la souris (-1 : aucun).
    public int HorseUnder(Ray r)
    {
        if (ch == null) return -1;
        float best = float.MaxValue; int hit = -1;
        for (int h = 0; h < ch.count; h++)
            if (pawns[ch.turn, h].GetComponent<Collider>().Raycast(r, out var info, 20) && info.distance < best) { best = info.distance; hit = h; }
        return hit;
    }

    // Chevaux jouables : ils sautillent (et plus haut sous la souris).
    public int hover = -1;
    void LateUpdate()
    {
        if (ch == null || busy) return;
        for (int h = 0; h < ch.count; h++)
        {
            var p = pawns[ch.turn, h];
            int pr = ch.players[ch.turn].horses[h];
            float lift = ch.CanPick(h) ? (h == hover ? 0.03f : 0.012f) * (0.6f + 0.4f * Mathf.Sin(Time.time * 7 + h)) : 0;
            p.localPosition = HorsePos(ch.turn, h, pr) + Vector3.up * Mathf.Max(0, lift);
        }
    }

    // --- Camera : en orbite au-dessus du plateau, depuis le cote d'un joueur -------------------------------------
    public Pose CamPose(float yaw, float pitch, float dist)
    {
        var target = transform.TransformPoint(BoardAt + Vector3.up * 0.02f);
        var from = target + Quaternion.Euler(pitch, yaw, 0) * Vector3.back * dist;
        return new Pose(from, Quaternion.LookRotation(target - from));
    }
    // Angle de camera pour regarder le plateau depuis le bras d'un joueur (bras du bas = yaw 0).
    public float YawFor(int seat) => ch == null ? 0 : new[] { 180f, -90f, 0f, 90f }[ch.players[Mathf.Clamp(seat, 0, ch.players.Count - 1)].arm];

    // --- Animations ------------------------------------------------------------------------------------------
    public bool busy;
    public bool thrownHere;   // le lancer vient de ma souris : pas de secouage rejoue
    float Dt => Time.deltaTime * speed;
    public float Speed { set { speed = value; if (de) de.speed = value; } }

    public IEnumerator Play(List<ChevEvent> evs, Action<ChevEvent> say)
    {
        busy = true;
        foreach (var e in evs)
        {
            if (e.type != CEv.Rolled) say?.Invoke(e);   // le resultat du de s'annonce une fois le de pose
            switch (e.type)
            {
                case CEv.Rolled: yield return de.Play(e.fling, e.value, e.seat, !thrownHere); thrownHere = false; say?.Invoke(e); break;
                case CEv.Moved: yield return Gallop(e); break;
                case CEv.Captured: yield return BackToStable(e); break;
                case CEv.NoMove: case CEv.Sacrifice: yield return new WaitForSeconds(0.9f / speed); break;
                case CEv.Won: yield return Celebrate(e.seat); break;
                case CEv.Turn:
                    if (evs.Count > 1) yield return new WaitForSeconds(0.25f / speed);
                    yield return de.Collect(e.seat);
                    break;
            }
        }
        Sync();
        busy = false;
    }

    IEnumerator Hop(Transform p, Vector3 a, Vector3 b, float height, float dur, int n)
    {
        Sound.I.Play("hop" + (n % 3 + 1), 0.55f);
        var ra = p.localRotation;
        var rb = (b - a).sqrMagnitude > 1e-6f ? Quaternion.LookRotation(new Vector3(b.x - a.x, 0, b.z - a.z).normalized) : ra;
        for (float t = 0; t < 1; t += Dt / dur)
        {
            p.localPosition = Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * height;
            p.localRotation = Quaternion.Slerp(ra, rb, Mathf.Clamp01(t * 3));
            yield return null;
        }
        p.localPosition = b;
    }

    // Case par case, petits sauts ; sortie de l'ecurie et montee des marches en un grand saut.
    IEnumerator Gallop(ChevEvent e)
    {
        var p = pawns[e.seat, e.horse];
        if (e.from < 0 || e.from >= Chevaux.Foot)
        {
            bool home = e.to == Chevaux.Home;   // de la derniere marche jusqu'a sa place au bord : un grand saut
            yield return Hop(p, HorsePos(e.seat, e.horse, e.from), HorsePos(e.seat, e.horse, e.to), home ? 0.18f : 0.07f, home ? 0.75f : 0.45f, 0);
            if (e.to == Chevaux.Home) Sound.I.Play("arrive");
        }
        else
        {
            int prev = e.from;   // chemin donne par les regles : rebond au pied de l'escalier compris
            foreach (int k in e.path)
            {
                yield return Hop(p, HorsePos(e.seat, e.horse, prev), HorsePos(e.seat, e.horse, k), 0.025f, 0.16f, k);
                prev = k;
            }
        }
        p.localRotation = HorseRot(e.seat, e.to);
        yield return new WaitForSeconds(0.1f / speed);
    }

    // Mange : le cheval bondit tres haut en tournoyant et retombe dans son ecurie.
    IEnumerator BackToStable(ChevEvent e)
    {
        var p = pawns[e.seat, e.horse];
        Sound.I.Play("fall");
        var a = p.localPosition; var b = HorsePos(e.seat, e.horse, -1);
        for (float t = 0; t < 1; t += Dt / 0.8f)
        {
            p.localPosition = Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.35f;
            p.localRotation = Quaternion.Euler(t * 720, t * 360, 0);
            yield return null;
        }
        p.localPosition = b;
        p.localRotation = HorseRot(e.seat, -1);
        Sound.I.Play("hop1", 0.6f);
    }

    // Victoire : les chevaux du gagnant sautent de joie les uns apres les autres.
    IEnumerator Celebrate(int seat)
    {
        Sound.I.Play("win");
        for (int round = 0; round < 3; round++)
            for (int h = 0; h < ch.count; h++)
            {
                var p = pawns[seat, h];
                var at = p.localPosition;
                StartCoroutine(Hop(p, at, at, 0.08f, 0.35f, h));
                yield return new WaitForSeconds(0.12f / speed);
            }
        yield return new WaitForSeconds(0.6f / speed);
    }
}
