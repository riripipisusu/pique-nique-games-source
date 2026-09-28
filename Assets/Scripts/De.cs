using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Le de des petits chevaux (pack "Board Game Items", Resources/Chevaux/De : dans les builds, pas dans le depot ; a defaut,
// un de peint). Il attend sur la nappe a cote de l'ecurie du joueur ; on l'attrape a la souris, il tourne dans la main,
// et on le lache d'un geste : il roule vraiment (moteur physique) sur le plateau, entre des murs invisibles.
// Chaque lancer est d'abord simule d'un coup (Physics.Simulate) puis rejoue image par image. Un lancer venu d'ailleurs
// (bot, autre PC) a deja sa valeur : si la simulation tombe sur une autre face, le modele est tourne dans le de
// (un cube : rotation invisible) pour que la bonne face finisse en haut.
public class De : MonoBehaviour
{
    public const float Size = 0.042f;
    const float Step = 0.01f;

    Transform die, vis;
    Rigidbody rb;
    Vector3[] faceDir;
    Func<int, Vector3> homeOf;
    float half, top;
    Vector3 center;
    public float speed = 1;

    // De du pack (OBJ : Unity inverse son axe x) : 1 dessous, 2 derriere, 3 a droite, 4 a gauche, 5 devant, 6 dessus.
    static readonly Vector3[] PackDir = { Vector3.down, Vector3.back, Vector3.right, Vector3.left, Vector3.forward, Vector3.up };
    static readonly Vector3[] PaintedDir = { Vector3.up, Vector3.forward, Vector3.right, Vector3.left, Vector3.back, Vector3.down };

    // boardCenter : milieu du dessus du plateau ; boardHalf : demi-cote de l'aire de jeu ; home : place du de de chaque joueur (sur la nappe).
    public void Init(Material lit, Vector3 boardCenter, float boardHalf, Func<int, Vector3> home)
    {
        center = boardCenter; half = boardHalf; top = boardCenter.y; homeOf = home;
        Physics.simulationMode = SimulationMode.Script;   // seul ce jeu utilise la physique : on la fait avancer nous-memes
        Physics.autoSyncTransforms = true;
        var felt = new PhysicsMaterial { bounciness = 0.3f, dynamicFriction = 0.45f, staticFriction = 0.6f, bounceCombine = PhysicsMaterialCombine.Maximum };
        // Plateau et murs invisibles tout autour (le de ne quitte jamais le plateau).
        var floor = new GameObject("sol du de").AddComponent<BoxCollider>();
        floor.transform.SetParent(transform, false);
        floor.center = center + Vector3.down * 0.05f; floor.size = new Vector3(half * 2.2f, 0.1f, half * 2.2f); floor.sharedMaterial = felt;
        for (int k = 0; k < 4; k++)
        {
            var w = new GameObject("mur").AddComponent<BoxCollider>();
            w.transform.SetParent(transform, false);
            var d = new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left }[k];
            w.center = center + d * (half + 0.05f) + Vector3.up * 0.2f;
            w.size = k % 2 == 0 ? new Vector3(half * 2.4f, 0.4f, 0.1f) : new Vector3(0.1f, 0.4f, half * 2.4f);
            w.sharedMaterial = felt;
        }
        // Le de : un cube physique, le modele dans un enfant (pour la rotation de correction).
        die = new GameObject("de").transform;
        die.SetParent(transform, false);
        var box = die.gameObject.AddComponent<BoxCollider>();
        box.size = Vector3.one * Size; box.sharedMaterial = felt;
        rb = die.gameObject.AddComponent<Rigidbody>();
        rb.mass = 0.03f; rb.isKinematic = true; rb.sleepThreshold = 0.0005f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        vis = new GameObject("modele").transform;
        vis.SetParent(die, false);
        var tex = Resources.Load<Texture2D>("Chevaux/De/de_gobelet");
        var packDie = Resources.Load<GameObject>("Chevaux/De/de_rouge");
        if (packDie && tex)
        {
            var m = new Material(lit) { color = Color.white };
            m.SetTexture("_BaseMap", tex); m.SetFloat("_Smoothness", 0.55f);
            var g = Instantiate(packDie, vis);
            var mf = g.GetComponentInChildren<MeshFilter>();
            foreach (var r in g.GetComponentsInChildren<Renderer>()) r.sharedMaterial = m;
            float k = mf ? Size / mf.sharedMesh.bounds.size.x : 1;
            g.transform.localScale = Vector3.one * k;
            g.transform.localPosition = mf ? -mf.sharedMesh.bounds.center * k : Vector3.zero;
            faceDir = PackDir;
        }
        else { faceDir = PaintedDir; Painted(lit); }
    }

    // Sans le pack : de peint (une face par valeur).
    void Painted(Material lit)
    {
        for (int v = 1; v <= 6; v++)
        {
            var m = new Material(lit) { color = Color.white };
            m.SetTexture("_BaseMap", Pips(v));
            var f = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(f.GetComponent<Collider>());
            f.GetComponent<Renderer>().sharedMaterial = m;
            f.transform.SetParent(vis, false);
            f.transform.localScale = Vector3.one * Size;
            f.transform.localPosition = PaintedDir[v - 1] * Size / 2;
            f.transform.localRotation = Quaternion.LookRotation(-PaintedDir[v - 1], Mathf.Abs(PaintedDir[v - 1].y) > 0.5f ? Vector3.forward : Vector3.up);
        }
    }

    static Texture2D Pips(int v)
    {
        const int N = 96;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, true);
        var spots = new List<Vector2>();
        float a = 0.27f, b = 0.5f, c = 0.73f;
        if (v % 2 == 1) spots.Add(new Vector2(b, b));
        if (v >= 2) { spots.Add(new Vector2(a, a)); spots.Add(new Vector2(c, c)); }
        if (v >= 4) { spots.Add(new Vector2(a, c)); spots.Add(new Vector2(c, a)); }
        if (v == 6) { spots.Add(new Vector2(a, b)); spots.Add(new Vector2(c, b)); }
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var p = new Vector2((x + 0.5f) / N, (y + 0.5f) / N);
                float d = 9; foreach (var s in spots) d = Mathf.Min(d, Vector2.Distance(p, s));
                t.SetPixel(x, y, Color.Lerp(Board.Hex("fbf7ee"), Board.Hex("2a2320"), Mathf.Clamp01((0.085f - d) * N * 0.7f + 0.5f)));
            }
        t.Apply();
        return t;
    }

    // --- Place et etat ----------------------------------------------------------------------------------------
    int seat;
    Vector3 Rest(int s) => homeOf(s) + Vector3.up * Size / 2;
    public bool Holding { get; private set; }

    // Le de attend a la place du joueur, pose a plat.
    public void Park(int s)
    {
        seat = s;
        Holding = false;
        die.localPosition = Rest(s);
        die.localRotation = Quaternion.Euler(0, 25, 0);
        vis.localRotation = Quaternion.identity;
    }

    // Valeur de la face du dessus.
    public int Top() { int best = 0; var q = die.localRotation * vis.localRotation; for (int v = 1; v < 6; v++) if ((q * faceDir[v]).y > (q * faceDir[best]).y) best = v; return best + 1; }

    // Le de sautille doucement quand c'est a moi de le lancer.
    public void Beckon(bool on)
    {
        if (Holding || busy) return;
        die.localPosition = Rest(seat) + Vector3.up * (on ? Mathf.Abs(Mathf.Sin(Time.time * 4)) * 0.02f : 0);
    }

    // --- A la souris : on attrape le de, il tourne dans la main au-dessus du plateau, on le lache d'un geste ----------
    float heldFor;
    Vector3 handVel, hand, spin;
    bool busy;

    public void Grab()
    {
        Holding = true; heldFor = 0; handVel = Vector3.zero; hand = die.localPosition;
        spin = UnityEngine.Random.onUnitSphere * 400;
        Sound.I.Play("bj_chip1", 0.4f);
    }

    // Le de suit la souris a quelques centimetres au-dessus du plateau en tournant sur lui-meme.
    public void Hold(Ray worldRay)
    {
        if (!Holding) return;
        var ray = new Ray(transform.InverseTransformPoint(worldRay.origin), transform.InverseTransformDirection(worldRay.direction));
        float h = top + 0.12f;
        var at = hand;
        if (Mathf.Abs(ray.direction.y) > 1e-4f) at = ray.origin + ray.direction * ((h - ray.origin.y) / ray.direction.y);
        float lim = half - 0.05f;
        at = new Vector3(Mathf.Clamp(at.x - center.x, -lim, lim) + center.x, h, Mathf.Clamp(at.z - center.z, -lim, lim) + center.z);
        heldFor += Time.deltaTime;
        var p = Vector3.Lerp(hand, at, 1 - Mathf.Exp(-20 * Time.deltaTime));
        handVel = Vector3.Lerp(handVel, (p - hand) / Mathf.Max(1e-4f, Time.deltaTime), 0.35f);
        hand = p;
        die.localPosition = p + Vector3.up * Mathf.Sin(Time.time * 9) * 0.006f;
        die.localRotation = Quaternion.Euler(spin * Time.deltaTime * (0.5f + handVel.magnitude)) * die.localRotation;
    }

    // Lacher : le de part dans le sens du geste (plus vite si le geste est vif). Renvoie son lancer (a simuler).
    public float[] Release()
    {
        Holding = false;
        var dir = new Vector3(handVel.x, 0, handVel.z);
        if (dir.magnitude < 0.3f) dir = (new Vector3(center.x, 0, center.z) - new Vector3(hand.x, 0, hand.z)).normalized * 0.4f + dir;   // lache sans geste : vers le milieu
        var vel = Vector3.ClampMagnitude(dir * 1.1f, 2.6f) + dir.normalized * 0.5f + Vector3.up * 0.4f;
        var ang = Vector3.Cross(Vector3.up, dir.normalized) * (15 + vel.magnitude * 8) + UnityEngine.Random.onUnitSphere * 10;   // roule dans le sens du lancer
        return Fling(hand, die.localRotation, vel, ang);
    }

    // Lancer automatique (Espace, bouton, bots) : pris a sa place, lance du bord du joueur vers le milieu.
    public float[] AutoFling(int s)
    {
        var edge = center + Vector3.ProjectOnPlane(homeOf(s) - center, Vector3.up).normalized * (half - 0.1f);
        var from = new Vector3(Mathf.Clamp(edge.x - center.x, -half + 0.08f, half - 0.08f) + center.x, top + 0.14f, Mathf.Clamp(edge.z - center.z, -half + 0.08f, half - 0.08f) + center.z);
        var to = center + new Vector3(UnityEngine.Random.Range(-0.12f, 0.12f), 0, UnityEngine.Random.Range(-0.12f, 0.12f));
        var dir = new Vector3(to.x - from.x, 0, to.z - from.z).normalized;
        var vel = dir * UnityEngine.Random.Range(1.2f, 1.8f) + Vector3.up * 0.35f;
        var ang = Vector3.Cross(Vector3.up, dir) * UnityEngine.Random.Range(18f, 30f) + UnityEngine.Random.onUnitSphere * 8;
        return Fling(from, UnityEngine.Random.rotationUniform, vel, ang);
    }

    static float[] Fling(Vector3 p, Quaternion q, Vector3 v, Vector3 w) => new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, v.x, v.y, v.z, w.x, w.y, w.z };

    // --- Simulation ----------------------------------------------------------------------------------------------
    readonly List<(Vector3 p, Quaternion q)> frames = new List<(Vector3, Quaternion)>();
    readonly List<(int frame, float force)> hits = new List<(int, float)>();

    // Simule le lancer d'un coup et renvoie la face du dessus a l'arret (le de finit toujours bien a plat).
    public int Predict(float[] f)
    {
        frames.Clear(); hits.Clear();
        rb.isKinematic = false;
        rb.position = transform.TransformPoint(new Vector3(f[0], f[1], f[2]));
        rb.rotation = transform.rotation * new Quaternion(f[3], f[4], f[5], f[6]);
        rb.linearVelocity = transform.TransformDirection(new Vector3(f[7], f[8], f[9]));
        rb.angularVelocity = transform.TransformDirection(new Vector3(f[10], f[11], f[12]));
        Physics.SyncTransforms();
        int still = 0;
        var prevV = rb.linearVelocity;
        for (int i = 0; i < 700 && still < 25; i++)
        {
            Physics.Simulate(Step);
            frames.Add((transform.InverseTransformPoint(rb.position), Quaternion.Inverse(transform.rotation) * rb.rotation));
            float dv = (rb.linearVelocity - prevV).magnitude;
            if (dv > 0.35f && (hits.Count == 0 || i - hits[hits.Count - 1].frame > 4)) hits.Add((i, dv));
            prevV = rb.linearVelocity;
            still = rb.linearVelocity.magnitude < 0.01f && rb.angularVelocity.magnitude < 0.2f ? still + 1 : 0;
        }
        rb.isKinematic = true;
        // Pose a plat de la face la plus haute (si le de s'est arrete penche contre un cheval ou un mur, il bascule).
        var (lp, lq) = frames[frames.Count - 1];
        int t = 0; for (int v = 1; v < 6; v++) if ((lq * faceDir[v]).y > (lq * faceDir[t]).y) t = v;
        var flat = Quaternion.FromToRotation(lq * faceDir[t], Vector3.up) * lq;
        var rest = new Vector3(lp.x, top + Size / 2, lp.z);
        for (int k = 1; k <= 12; k++) frames.Add((Vector3.Lerp(lp, rest, k / 12f), Quaternion.Slerp(lq, flat, k / 12f)));
        return t + 1;
    }

    // Rotation du cube (symetrie du de) qui amene la face "want" la ou la simulation met la face "got".
    Quaternion Fix(int want, int got)
    {
        Vector3 a = faceDir[want - 1], b = faceDir[got - 1];
        if (want == got) return Quaternion.identity;
        if (Vector3.Dot(a, b) < -0.5f) return Quaternion.AngleAxis(180, Mathf.Abs(a.y) > 0.5f ? Vector3.right : Vector3.up);
        return Quaternion.FromToRotation(a, b);
    }

    // --- Lancer a l'ecran ----------------------------------------------------------------------------------------
    // fling null : lancer automatique ; windup : le de est d'abord ramasse a sa place (lancer qui ne vient pas de ma main).
    public IEnumerator Play(float[] fling, int value, int s, bool windup)
    {
        busy = true;
        if (fling == null) fling = AutoFling(s);
        var from = new Vector3(fling[0], fling[1], fling[2]);
        var q0 = new Quaternion(fling[3], fling[4], fling[5], fling[6]);
        if (windup)
        {
            // Ramasse, petit elan en arriere, puis lance.
            var a = die.localPosition; var ra = die.localRotation;
            var back = from + (from - new Vector3(center.x, from.y, center.z)).normalized * 0.06f + Vector3.up * 0.04f;
            for (float t = 0; t < 1; t += Time.deltaTime * speed / 0.45f)
            {
                die.localPosition = t < 0.6f ? Vector3.Lerp(a, back, Mathf.SmoothStep(0, 1, t / 0.6f)) + Vector3.up * Mathf.Sin(t / 0.6f * Mathf.PI) * 0.05f
                                             : Vector3.Lerp(back, from, (t - 0.6f) / 0.4f);
                die.localRotation = Quaternion.Slerp(ra, q0, Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
        }
        int got = Predict(fling);
        vis.localRotation = Fix(value, got);
        Sound.I.Play("bj_slide" + UnityEngine.Random.Range(1, 3), 0.4f);
        int hit = 0;
        for (float t = 0; ; t += Time.deltaTime * speed)
        {
            int i = Mathf.Min(frames.Count - 1, (int)(t / Step));
            die.localPosition = frames[i].p;
            die.localRotation = frames[i].q;
            while (hit < hits.Count && hits[hit].frame <= i) { Sound.I.Play("rt_bounce" + UnityEngine.Random.Range(1, 5), Mathf.Clamp01(0.25f + hits[hit].force * 0.3f), 0.15f); hit++; }
            if (i == frames.Count - 1) break;
            yield return null;
        }
        yield return new WaitForSeconds(0.25f / speed);
        busy = false;
    }

    // Au joueur suivant : le de saute jusqu'a sa place (il garde sa face, pose a plat).
    public IEnumerator Collect(int s)
    {
        seat = s;
        busy = true;
        var a = die.localPosition; var b = Rest(s);
        for (float t = 0; t < 1; t += Time.deltaTime * speed / 0.45f)
        {
            die.localPosition = Vector3.Lerp(a, b, Mathf.SmoothStep(0, 1, t)) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.1f;
            yield return null;
        }
        die.localPosition = b;
        Sound.I.Play("bj_place1", 0.4f);
        busy = false;
    }
}
