using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Le pouilleux sur la nappe de la clairiere (meme decor et meme camera que le Uno) : chaque adversaire tient ses cartes
// de dos en eventail face a moi ; ma main est dans l'interface. A mon tour, l'eventail de mon voisin s'allume : la carte
// survolee se souleve, un clic la pioche. Les paires jetees volent, face visible, sur le tas au milieu de la nappe.
public class PouilleuxView : MonoBehaviour
{
    const float CardW = 0.3f, CardH = 0.426f;
    static readonly Vector3 PileAt = new Vector3(0, 0.03f, 0.55f);

    Pouilleux pq;
    int me;
    Material cutout;
    Transform cast, pile;
    readonly List<Transform> fans = new List<Transform>();
    readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    public float speed = 1;
    public bool busy;
    public int hover = -1;
    float Dt => Time.deltaTime * speed;

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
        transform.position = Clairiere.Center;
        cutout = Resources.Load<Material>("LitCutout");
        pile = new GameObject("tas").transform; pile.SetParent(transform, false); pile.localPosition = PileAt;
        gameObject.SetActive(false);
    }

    static readonly string[] Suits = { "spades", "diamonds", "hearts", "clubs" };
    static readonly string[] Ranks = { "A", "02", "03", "04", "05", "06", "07", "08", "09", "10", "J", "Q", "K" };
    public static string Face(int card) => card < 0 ? "back_red" : Suits[card / 13] + "_" + Ranks[card % 13];

    Material Mat(string img)
    {
        if (mats.TryGetValue(img, out var m)) return m;
        m = new Material(cutout);
        m.SetTexture("_BaseMap", Resources.Load<Texture2D>("Cards/" + img));
        m.color = Color.white;
        m.SetFloat("_Smoothness", 0.3f);
        return mats[img] = m;
    }
    Transform Card(string img, Transform parent, float scale = 1, bool shadow = false)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        g.transform.SetParent(parent, false);
        g.transform.localScale = new Vector3(CardW, CardH, 1) * scale;
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = Mat(img);
        r.shadowCastingMode = shadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        Destroy(g.GetComponent<Collider>());
        return g.transform;
    }

    // --- Places : moi en bas (ma main est dans l'interface), les autres en arc face a moi ---
    Vector3 SeatPos(int seat)
    {
        int n = pq.players.Count, k = ((seat - me) % n + n) % n - 1, m = n - 1;
        float a = (180 - (k + 1) * 180f / (m + 1)) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a) * 2.3f, 0, 0.8f + Mathf.Sin(a) * 1.45f);
    }
    float FanScale => pq.players.Count > 4 ? 1.2f : 1.5f;
    public Vector3 TagOf(int seat) => fans[seat].position + fans[seat].up * (CardH * FanScale * 1.25f);
    Vector3 HandPos() { var c = CamPose; return c.position + c.rotation * new Vector3(0, -0.4f, 1.3f); }
    Quaternion FacingCam(Vector3 at) => Quaternion.LookRotation(at - CamPose.position);

    public void Build(Pouilleux p, int mySeat)
    {
        Clairiere.Show(true);
        gameObject.SetActive(true);
        pq = p;
        me = Mathf.Clamp(mySeat, 0, p.players.Count - 1);
        if (cast) Destroy(cast.gameObject);
        cast = new GameObject("eventails").transform;
        cast.SetParent(transform, false);
        fans.Clear();
        for (int i = 0; i < p.players.Count; i++)
        {
            var f = new GameObject("eventail " + i).transform;
            f.SetParent(cast, false);
            if (i != me)
            {
                // Pivot de l'eventail en bas, au-dessus de la nappe : les cartes tournent autour (jamais dans la nappe).
                f.localPosition = SeatPos(i) + Vector3.up * 0.14f;
                var look = f.position - CamPose.position; look.y = 0;
                f.rotation = Quaternion.LookRotation(look) * Quaternion.Euler(-12, 0, 0);
                var cu = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(cu.GetComponent<Collider>());
                cu.name = "coussin " + i;
                cu.transform.SetParent(cast, false);
                cu.transform.localPosition = SeatPos(i) + new Vector3(0, 0.035f, 0.15f);
                cu.transform.localScale = new Vector3(0.95f, 0.035f, 0.8f) * FanScale / 1.5f + new Vector3(0, 0.035f * (1 - FanScale / 1.5f), 0);
                var cm = new Material(cutout); cm.SetTexture("_BaseMap", null);
                cm.color = Color.Lerp(Board.Colors[i % Board.Colors.Length], Color.white, 0.15f); cm.SetFloat("_Smoothness", 0.1f);
                cu.GetComponent<Renderer>().sharedMaterial = cm;
            }
            fans.Add(f);
        }
        Sync();
    }

    public void Hide() { StopAllCoroutines(); pq = null; busy = false; gameObject.SetActive(false); Clairiere.Show(false); }

    // Eventail de dos de chaque adversaire : une carte par carte de sa main (boite de clic sur chacune) ; tas du milieu.
    public void Sync()
    {
        if (pq == null) return;
        for (int s = 0; s < fans.Count; s++)
        {
            Clear(fans[s]);
            if (s == me) continue;
            int n = pq.players[s].hand.Count;
            for (int k = 0; k < n; k++)
            {
                var c = Card(Face(-1), fans[s], FanScale, true);
                Place(c, k, n, 0);
                c.name = "carte " + k;
            }
        }
        Clear(pile);
        int from = Mathf.Max(0, pq.discard.Count - 10);
        for (int i = from; i < pq.discard.Count; i++)
        {
            int h = pq.discard[i] * 37 + i * 53;
            var c = Card(Face(pq.discard[i]), pile, 0.8f);
            c.localPosition = new Vector3((h % 17 - 8) * 0.014f, (i - from) * 0.003f, (h % 13 - 6) * 0.014f);
            c.localRotation = Quaternion.Euler(90, (h % 70) - 35, 0);
        }
    }

    static void Clear(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--) { var c = t.GetChild(i); c.SetParent(null, false); Destroy(c.gameObject); }
    }

    // Vraie main en eventail : chaque carte tourne autour du pivot (bas de l'eventail) ; lift la fait sortir de la main.
    // Pivot bien en dessous des cartes : arc large et doux, comme une main tenue.
    float Radius => CardH * FanScale * 1.7f;
    float Spread(int n) => Mathf.Min(4.5f, (pq.players.Count > 4 ? 30f : 38f) / Mathf.Max(1, n));
    void Place(Transform c, int k, int n, float lift)
    {
        float a = (k - (n - 1) / 2f) * Spread(n);
        var r = Quaternion.Euler(0, 0, -a);
        c.localRotation = r;
        c.localPosition = r * new Vector3(0, Radius + lift, 0) - new Vector3(0, Radius - CardH * FanScale * 0.5f - 0.06f, k * 0.003f);
    }
    float LiftOf(Transform c) => (c.localPosition + new Vector3(0, Radius - CardH * FanScale * 0.5f - 0.06f, 0)).magnitude - Radius;

    // Carte de l'eventail du voisin sous la souris (index dans sa main), -1 sinon.
    public int CardUnder(Ray r)
    {
        if (pq == null || busy || pq.Target < 0 || pq.Target == me) return -1;
        var f = fans[pq.Target];
        int best = -1; float bestD = float.MaxValue;
        for (int k = 0; k < f.childCount; k++)
        {
            var c = f.GetChild(k);
            var plane = new Plane(c.forward, c.position);
            if (!plane.Raycast(r, out float d)) continue;
            var local = c.InverseTransformPoint(r.GetPoint(d));
            if (Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f && d < bestD) { bestD = d; best = k; }
        }
        return best;
    }

    // A mon tour, l'eventail du voisin chez qui je pioche se tend vers moi (legerement agrandi) et la carte survolee
    // sort de sa main. Mouvements lisses, rien ne bouge hors de mon tour.
    void LateUpdate()
    {
        if (pq == null || busy) return;
        int t = pq.Target;
        bool mine = pq.turn == me && t >= 0 && !pq.Finished;
        float k8 = 1 - Mathf.Exp(-Time.deltaTime * 14);
        for (int s = 0; s < fans.Count; s++)
        {
            if (s == me) continue;
            var f = fans[s];
            bool target = mine && s == t;
            f.localScale = Vector3.Lerp(f.localScale, Vector3.one * (target ? 1.12f : 1f), k8);
            int n = f.childCount;
            for (int k = 0; k < n; k++)
            {
                var c = f.GetChild(k);
                float want = target ? (k == hover ? 0.2f : 0.03f) : 0;
                float cur = LiftOf(c);
                Place(c, k, n, Mathf.Lerp(cur, want, k8));
            }
        }
    }

    // --- Animations ---
    IEnumerator Fly(string img, Vector3 a, Vector3 b, Quaternion ra, Quaternion rb, float dur, string flipTo = null, float scale = 1)
    {
        var c = Card(img, transform, scale);
        c.position = a;
        var r = c.GetComponent<Renderer>();
        Sound.I.Play(UnityEngine.Random.value < 0.5f ? "bj_slide1" : "bj_slide2", 0.6f, 0.1f);
        for (float t = 0; t < 1; t += Dt / dur)
        {
            float e = 1 - (1 - t) * (1 - t);
            c.position = Vector3.Lerp(a, b, e) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.3f;
            float spin = flipTo != null ? t * 180 : 0;
            if (flipTo != null && spin > 90) r.sharedMaterial = Mat(flipTo);
            c.rotation = Quaternion.Slerp(ra, rb, e) * Quaternion.Euler(0, spin > 90 ? spin - 180 : spin, 0);
            yield return null;
        }
        Destroy(c.gameObject);
    }

    Vector3 FanCardPos(int seat, int index)
    {
        var f = fans[seat];
        return index >= 0 && index < f.childCount ? f.GetChild(index).position : f.position;
    }

    public IEnumerator Play(List<PEvent> evs, Action<PEvent> say)
    {
        busy = true;
        foreach (var e in evs)
        {
            say?.Invoke(e);
            switch (e.type)
            {
                case PEv.Deal: Sound.I.Play("bj_shuffle", 0.9f); Sync(); yield return new WaitForSeconds(0.3f / speed); break;
                case PEv.Take:
                {
                    // De l'eventail (ou de ma main) vers la main de celui qui pioche ; retournee si c'est moi qui pioche.
                    Vector3 from = e.other == me ? HandPos() : FanCardPos(e.other, e.index);
                    Vector3 to = e.seat == me ? HandPos() : fans[e.seat].position;
                    Quaternion rf = e.other == me ? FacingCam(from) : fans[e.other].rotation;
                    Quaternion rt = e.seat == me ? FacingCam(to) : fans[e.seat].rotation;
                    // La carte quitte l'eventail tout de suite (elle est dans les regles deja deplacee).
                    if (e.other != me && e.index < fans[e.other].childCount) fans[e.other].GetChild(e.index).gameObject.SetActive(false);
                    // Ma carte qu'on me prend part face visible puis se retourne ; celle que je pioche arrive de dos et se retourne.
                    yield return Fly(e.other == me ? Face(e.card) : Face(-1), from, to, rf, rt, 0.55f, e.seat == me ? Face(e.card) : e.other == me ? Face(-1) : null, FanScale);
                    Sync();
                    break;
                }
                case PEv.Pair:
                {
                    Vector3 from = e.seat == me ? HandPos() : fans[e.seat].position;
                    var flat = transform.rotation * Quaternion.Euler(90, 0, 0);
                    StartCoroutine(Fly(Face(e.card2), from, pile.position + Vector3.up * 0.05f, FacingCam(from), flat, 0.4f, null, 0.8f));
                    yield return Fly(Face(e.card), from + Vector3.right * 0.08f, pile.position + Vector3.up * 0.06f, FacingCam(from), flat, 0.45f, null, 0.8f);
                    Sound.I.Play("bj_place1", 0.7f);
                    Sync();
                    break;
                }
                case PEv.Out: Sound.I.Play("win", 0.5f); break;
                case PEv.Lose: Sound.I.Play("lose"); yield return new WaitForSeconds(0.8f / speed); break;
            }
        }
        Sync();
        busy = false;
    }
}
