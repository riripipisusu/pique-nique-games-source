using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Monde 3D : terrain, decor, cases, lapins, animations. Lit Rules, ne le modifie jamais.
public class Board : MonoBehaviour
{
    // 4 premieres couleurs pour les jeux a 4 ; le quiz monte a 10 joueurs.
    public static readonly Color[] Colors = { Hex("e8483b"), Hex("3b7de0"), Hex("45b35f"), Hex("f0b92a"), Hex("9b59d6"), Hex("f07e2a"), Hex("e05aa8"), Hex("2ab7b0"), Hex("8d6e4a"), Hex("a3d93b") };
    public static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

    const float HillR = 15f, HillH = 5.5f;
    static readonly Vector3 PenCenter = new Vector3(-18, 0, -12);

    public float speed = 1;
    public bool showNumbers = true, busy;
    public Vector3 Focus { get; private set; } = new Vector3(0, 2.5f, 0);

    Rules rules;
    Material lit, fxMat;
    Mesh cube, sphere;
    Font font;
    RuntimeAnimatorController rabbitCtrl;
    GameObject rabbitPrefab;
    Transform root, carrot;
    readonly Dictionary<int, Transform> stumps = new Dictionary<int, Transform>();
    readonly Dictionary<int, float> tops = new Dictionary<int, float>();
    readonly Dictionary<int, Transform> labels = new Dictionary<int, Transform>();
    readonly Dictionary<int, GameObject> warn = new Dictionary<int, GameObject>();
    Bunny[,] bunnies;

    class Bunny
    {
        public Transform t, ring;
        public Animator an;
        public void Play(string s, float f = 0.15f) => an.CrossFadeInFixedTime(s, f);
    }

    void Awake()
    {
        lit = Resources.Load<Material>("Lit");
        fxMat = Resources.Load<Material>("Particle");
        font = Resources.Load<Font>("Fonts/Fredoka");
        rabbitCtrl = Resources.Load<RuntimeAnimatorController>("RabbitAnim");
        rabbitPrefab = Resources.Load<GameObject>("Models/Rabbit");
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = c.GetComponent<MeshFilter>().sharedMesh; Destroy(c);
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere = s.GetComponent<MeshFilter>().sharedMesh; Destroy(s);
        BuildWorld();
    }

    // --- Outils -----------------------------------------------------------------
    Material Mat(Color c, float smooth = 0.15f)
    {
        var m = new Material(lit) { color = c };
        m.SetFloat("_Smoothness", smooth);
        return m;
    }

    GameObject Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Color c, Transform parent, float smooth = 0.15f)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = Mat(c, smooth);
        return g;
    }

    GameObject Spawn(string model, Vector3 pos, float scale, float rotY, Transform parent)
    {
        var prefab = Synty.Get(model) ?? Resources.Load<GameObject>("Models/" + model);
        if (!prefab) { Debug.LogWarning("Modele introuvable : " + model); return null; }
        var g = Instantiate(prefab, pos, Quaternion.Euler(0, rotY, 0), parent);
        g.transform.localScale = Vector3.one * scale;
        return g;
    }

    static Bounds BoundsOf(GameObject g)
    {
        var rs = g.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    public static float HillY(float r)
    {
        if (r >= HillR) return 0;
        float k = r / HillR;
        return HillH * Mathf.Pow(1 - k * k, 1.4f);
    }

    public static float Ground(float x, float z) => GroundBase(x, z) - Nature.RiverCarve(x, z);

    // Relief sans le lit de la riviere : colline du plateau, bosses, etang, collines boisees a l'horizon.
    public static float GroundBase(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float n = Mathf.PerlinNoise(x * 0.35f + 100, z * 0.35f + 100) - 0.5f;
        float far = Mathf.Clamp01((r - 28) / 30) * Mathf.PerlinNoise(x * 0.04f, z * 0.04f) * 7f;
        float rim = Mathf.Pow(Mathf.InverseLerp(140, 340, r), 1.5f) * (18 + 30 * Mathf.PerlinNoise(x * 0.01f + 9, z * 0.01f + 4));
        return HillY(r) + n * (r < HillR ? 0.12f : 0.3f) + far + rim;
    }

    // Hors du plateau, de l'enclos, de l'etang et du pique-nique.
    public static bool FreeSpot(Vector3 p, float margin)
    {
        float r = new Vector2(p.x, p.z).magnitude;
        return r > HillR + margin
            && Vector3.Distance(new Vector3(p.x, 0, p.z), PenCenter) > 7.5f
            && Vector3.Distance(new Vector3(p.x, 0, p.z), Hub.Center) > 9f;
    }

    // --- Monde fixe ---------------------------------------------------------------
    void BuildWorld()
    {
        var world = new GameObject("Monde").transform;

        var rng = new System.Random(11);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        // Nenuphars sur la riviere, pres du plateau.
        for (int i = 0; i < 7; i++)
        {
            var p = Vector3.Lerp(new Vector3(52, 0, -29), new Vector3(20, 0, -18), i / 6f) + new Vector3(R(-1.5f, 1.5f), 0, R(-1.5f, 1.5f));
            p.y = GroundBase(p.x, p.z) - 0.5f;
            Spawn("SM_Env_Lillies_0" + (i % 3 + 1), p, 1f, R(0, 360), world);
        }

        // Enclos de depart : terre battue + barriere
        Prim(PrimitiveType.Cylinder, PenCenter + Vector3.up * (Ground(PenCenter.x, PenCenter.z) + 0.02f), new Vector3(9.5f, 0.04f, 8), Hex("c9a26b"), world);
        for (int i = 0; i < 18; i++)
        {
            if (i == 4) continue; // ouverture vers la montagne
            float a = i * 20f * Mathf.Deg2Rad;
            var p = PenCenter + new Vector3(Mathf.Cos(a) * 5.2f, 0, Mathf.Sin(a) * 4.4f);
            p.y = Ground(p.x, p.z);
            var dir = new Vector3(-Mathf.Sin(a) * 5.2f, 0, Mathf.Cos(a) * 4.4f);
            var f = Spawn("SM_Prop_Meadow_Fence_01", p, 0.7f, 0, world);
            f.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0, 90, 0);
        }
        var sign = PenCenter + new Vector3(5.5f, 0, 2.6f);
        Spawn("sign", sign + Vector3.up * Ground(sign.x, sign.z), 2.5f, 200, world);

        bool Free(Vector3 p, float margin) => FreeSpot(p, margin);
        Vector3 RandomSpot(float rMin, float rMax, float margin)
        {
            for (int k = 0; k < 50; k++)
            {
                float a = R(0, Mathf.PI * 2), r = R(rMin, rMax);
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                if (Free(p, margin)) { p.y = Ground(p.x, p.z); return p; }
            }
            return new Vector3(0, -100, 0);
        }

        // Terrain precalcule dans la scene "Monde" (build) ; genere a la volee sinon.
        if (Application.CanStreamedLevelBeLoaded("Monde")) UnityEngine.SceneManagement.SceneManager.LoadScene("Monde", UnityEngine.SceneManagement.LoadSceneMode.Additive);
        else Nature.Build(world, FreeSpot);
        Nature.Water(world, Synty.I.water);
        Spawn("SM_Env_Cloud_Ring_01", new Vector3(0, 55, 0), 2.2f, 0, world);
        Spawn("SM_Env_Cloud_Ring_02", new Vector3(0, 70, 0), 2.6f, 120, world);
        for (int i = 0; i < 30; i++) Spawn("SM_Prop_Mushroom_Group_0" + rng.Next(2, 6), RandomSpot(18, 40, 1), R(1.5f, 2.5f), R(0, 360), world);
        for (int i = 0; i < 6; i++) Spawn(i % 2 == 0 ? "crop_pumpkin" : "crop_melon", PenCenter + new Vector3(-7 + i * 0.9f, Ground(-25f + i, -12f), 3 + (i % 3)), 2.8f, R(0, 360), world);
    }

    // --- Plateau d'une partie ---------------------------------------------------------
    Vector3 TileXZ(int i)
    {
        float r, a;
        if (rules.mode == Mode.Ameliore)
        {
            bool outer = i <= Rules.OuterRing;
            a = 225 + (outer ? (i - 1) * 24f : (i - 16) * 36f + 12f);  // case 1 face a l'enclos
            r = outer ? 11f : 6.6f;
        }
        else
        {
            float t = (i - 1) / (float)(rules.summit - 2);
            r = 12.2f - 8.8f * t;
            a = 225 - t * 560;
        }
        a *= Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
    }

    Vector3 Pos(int i)
    {
        if (i >= rules.summit) return new Vector3(0, HillH, 0);
        var p = TileXZ(i);
        p.y = tops[i];
        return p;
    }

    Vector3 PenSlot(int p, int r)
    {
        var v = PenCenter + new Vector3((p - 1.5f) * 1.7f, 0, (r - 1) * 1.6f);
        v.y = Ground(v.x, v.z) + 0.05f;
        return v;
    }

    Vector3 SummitSlot(int p, int r)
    {
        float a = (p * 3 + r) * 30 * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a) * 2.1f, HillH + 0.05f, Mathf.Sin(a) * 2.1f);
    }

    Vector3 Slot(int p, int r)
    {
        int pos = rules.players[p].rabbits[r];
        if (pos == Rules.Start) return PenSlot(p, r);
        if (pos >= rules.summit) return SummitSlot(p, r);
        return Pos(pos);
    }

    public void Build(Rules r)
    {
        rules = r;
        if (root) Destroy(root.gameObject);
        root = new GameObject("Plateau").transform;
        stumps.Clear(); stumpBase.Clear(); tops.Clear(); labels.Clear(); warn.Clear();
        StopAllCoroutines();

        for (int i = 1; i < rules.summit; i++)
        {
            var p = TileXZ(i);
            float g = HillY(p.magnitude);
            // Trou (noir) + case pavee encastree dans la colline, comme sur le plateau d'origine.
            Prim(PrimitiveType.Cylinder, p + Vector3.up * (g + 0.01f), new Vector3(2.05f, 0.03f, 2.05f), Hex("4a7a2c"), root, 0);
            Prim(PrimitiveType.Cylinder, p + Vector3.up * (g + 0.02f), new Vector3(1.75f, 0.03f, 1.75f), Hex("1d140c"), root, 0);
            var s = new GameObject("case" + i).transform;
            s.SetParent(root);
            s.position = p + Vector3.up * (g + 0.08f);
            Prim(PrimitiveType.Cylinder, Vector3.down * 0.9f, new Vector3(1.7f, 0.95f, 1.7f), Hex("d9a45a"), s);
            Prim(PrimitiveType.Cylinder, Vector3.up * 0.01f, new Vector3(1.45f, 0.02f, 1.45f), Hex("eab86b"), s);
            for (int c = 0; c < 7; c++)
            {
                float ca = c * 51.4f * Mathf.Deg2Rad + i;
                var cp = c == 0 ? Vector3.zero : new Vector3(Mathf.Cos(ca), 0, Mathf.Sin(ca)) * 0.45f;
                Prim(PrimitiveType.Cylinder, cp + Vector3.up * 0.025f, new Vector3(0.38f, 0.015f, 0.38f), Hex("f2c885"), s);
            }
            tops[i] = s.position.y + 0.04f;
            stumps[i] = s;
            stumpBase[i] = s.position;

            var label = new GameObject("n" + i).AddComponent<TextMesh>();
            label.font = font;
            label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            label.text = i.ToString();
            label.fontSize = 64;
            label.characterSize = 0.11f;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = Hex("5b3a22");
            label.transform.SetParent(s, true);
            label.transform.position = p + Vector3.up * (tops[i] + 0.06f);
            labels[i] = label.transform;

            if (rules.mode == Mode.Ameliore)
            {
                var w = Prim(PrimitiveType.Cylinder, p + Vector3.up * (g + 0.06f), new Vector3(2.7f, 0.03f, 2.7f), Hex("ff4a3a"), root, 0.6f);
                w.SetActive(false);
                warn[i] = w;
            }
        }

        BuildLadders();

        // La grosse carotte plantee au sommet : corps orange strie, couronne de fanes, manivelle.
        carrot = new GameObject("Carotte").transform;
        carrot.SetParent(root);
        carrot.position = new Vector3(0, HillH, 0);
        Prim(PrimitiveType.Cylinder, Vector3.up * 0.2f, new Vector3(3.2f, 1.4f, 3.2f), Hex("f07f1a"), carrot, 0.35f);
        for (int k = 0; k < 4; k++)
            Prim(PrimitiveType.Cylinder, Vector3.up * (-0.6f + k * 0.45f), new Vector3(3.26f, 0.04f, 3.26f), Hex("c9600f"), carrot);
        Prim(PrimitiveType.Cylinder, Vector3.up * 1.62f, new Vector3(2.7f, 0.03f, 2.7f), Hex("f7a04a"), carrot, 0.35f);
        for (int k = 0; k < 16; k++)
        {
            float a = k * 22.5f * Mathf.Deg2Rad;
            Prim(PrimitiveType.Capsule, new Vector3(Mathf.Cos(a) * 1.35f, 1.85f, Mathf.Sin(a) * 1.35f), new Vector3(0.42f, 0.32f, 0.42f), Hex("3aa55a"), carrot, 0.3f);
        }
        for (int k = 0; k < 5; k++)
        {
            float a = k * 72f;
            var leaf = Prim(PrimitiveType.Capsule, Vector3.zero, new Vector3(0.22f, 0.9f, 0.08f), Hex("47b865"), carrot, 0.3f);
            leaf.transform.localRotation = Quaternion.Euler(0, a, 22);
            leaf.transform.localPosition = leaf.transform.localRotation * Vector3.up * 0.9f + Vector3.up * 1.6f;
        }
        var crank = Prim(PrimitiveType.Cube, new Vector3(1.75f, 0.9f, 0), new Vector3(0.25f, 1.1f, 0.35f), Hex("6b4426"), carrot);
        crank.transform.localRotation = Quaternion.Euler(0, 0, -15);

        bunnies = new Bunny[rules.players.Count, Rules.RabbitsPerPlayer];
        for (int p = 0; p < rules.players.Count; p++)
            for (int k = 0; k < Rules.RabbitsPerPlayer; k++)
                bunnies[p, k] = MakeBunny(Colors[rules.players[p].color]);
        PlaceStumps();
        Sync();
    }

    // Petites echelles moulees entre les cases qui se suivent (et depuis l'enclos, et vers la carotte).
    void BuildLadders()
    {
        var links = new List<(Vector3 a, float ra, Vector3 b, float rb)>();
        var summit = Vector3.zero;
        int last = rules.summit - 1;
        links.Add((new Vector3(PenCenter.x, 0, PenCenter.z), 5f, TileXZ(1), 1f));
        for (int i = 1; i < last; i++) links.Add((TileXZ(i), 1f, TileXZ(i + 1), 1f));
        links.Add((TileXZ(last), 1f, summit, 1.9f));

        var rung = Hex("2e7a30");
        foreach (var (a, ra, b, rb) in links)
        {
            var d = b - a;
            float len = d.magnitude - ra - rb;
            if (len < 0.3f) continue;
            d.Normalize();
            var side = Vector3.Cross(Vector3.up, d) * 0.45f;
            var rot = Quaternion.LookRotation(d);
            int n = Mathf.Max(2, Mathf.RoundToInt(len / 0.38f));
            Vector3 prevL = default, prevR = default;
            for (int k = 0; k <= n; k++)
            {
                var p = a + d * (ra + len * k / n);
                p.y = Ground(p.x, p.z) + 0.05f;
                var r = Prim(PrimitiveType.Cube, p, new Vector3(0.95f, 0.08f, 0.13f), rung, root);
                r.transform.rotation = rot;
                var L = p + side; L.y = Ground(L.x, L.z) + 0.09f;
                var R = p - side; R.y = Ground(R.x, R.z) + 0.09f;
                if (k > 0)
                    foreach (var (u, v) in new[] { (prevL, L), (prevR, R) })
                    {
                        var rail = Prim(PrimitiveType.Cube, (u + v) / 2, new Vector3(0.1f, 0.12f, (v - u).magnitude + 0.05f), rung, root);
                        rail.transform.rotation = Quaternion.LookRotation(v - u);
                    }
                prevL = L; prevR = R;
            }
        }
    }

    Bunny MakeBunny(Color c)
    {
        var t = new GameObject("Lapin").transform;
        t.SetParent(root);
        var model = Instantiate(rabbitPrefab, t);
        model.transform.localScale = Vector3.one * 0.42f;
        var an = model.GetComponentInChildren<Animator>();
        an.runtimeAnimatorController = rabbitCtrl;
        an.applyRootMotion = false;
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            if (r.name.Contains("Eye")) continue;
            var ms = r.materials;
            foreach (var m in ms) m.color = Color.Lerp(Color.white, c, 0.4f);
            r.materials = ms;
        }
        var ring = Prim(PrimitiveType.Cylinder, Vector3.up * 0.02f, new Vector3(1.4f, 0.015f, 1.4f), c, t, 0.4f);
        return new Bunny { t = t, an = an, ring = ring.transform };
    }

    void Face(Transform t, Vector3 dir)
    {
        dir.y = 0;
        if (dir.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(dir);
    }

    public void Sync()
    {
        for (int p = 0; p < rules.players.Count; p++)
            for (int r = 0; r < Rules.RabbitsPerPlayer; r++)
            {
                var b = bunnies[p, r];
                b.t.position = Slot(p, r);
                b.t.localScale = Vector3.one;
                bool home = rules.players[p].rabbits[r] >= rules.summit;
                Face(b.t, home ? b.t.position - new Vector3(0, b.t.position.y, 0) : -b.t.position);
                b.an.speed = speed;
                b.Play(home ? "Sitting_Eating" : "Idle", 0.25f);
            }
    }

    // --- Effets -----------------------------------------------------------------------
    void Burst(Vector3 pos, int n, Gradient colors, Mesh mesh, float spd, float size, float life, float gravity)
    {
        var g = new GameObject("fx");
        g.transform.position = pos;
        var ps = g.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.2f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(spd * 0.4f, spd);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = new ParticleSystem.MinMaxGradient(colors) { mode = ParticleSystemGradientMode.RandomColor };
        main.gravityModifier = gravity;
        main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        main.stopAction = ParticleSystemStopAction.Destroy;
        var em = ps.emission;
        em.rateOverTime = 0;
        em.SetBursts(new[] { new ParticleSystem.Burst(0, (short)n) });
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Hemisphere;
        sh.radius = 0.3f;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 1, 1, 0));
        var rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.z = new ParticleSystem.MinMaxCurve(-5, 5);
        var pr = g.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Mesh;
        pr.mesh = mesh;
        pr.sharedMaterial = fxMat;
        ps.Play();
    }

    static Gradient Grad(params Color[] cs)
    {
        var g = new Gradient();
        g.SetKeys(cs.Select((c, i) => new GradientColorKey(c, cs.Length == 1 ? 0 : i / (float)(cs.Length - 1))).ToArray(),
                  new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
        return g;
    }

    void Dust(Vector3 p) => Burst(p + Vector3.up * 0.1f, 10, Grad(Hex("d9c29a"), Hex("b9975f")), sphere, 2.2f, 0.25f, 0.6f, 0.3f);
    void Poof(Vector3 p) => Burst(p + Vector3.up * 0.4f, 22, Grad(Color.white, Hex("e6f0ff")), sphere, 3f, 0.4f, 0.8f, -0.1f);
    void Confetti(Vector3 p, int n) => Burst(p + Vector3.up * 0.5f, n, Grad(Hex("ff4d4d"), Hex("ffd23f"), Hex("3fb3ff"), Hex("5ee07a"), Hex("ff7ad9")), cube, 9f, 0.28f, 2.2f, 0.9f);

    // --- Animations -------------------------------------------------------------------
    float D(float seconds) => seconds / speed;

    IEnumerator Hop(Transform t, Vector3 to, float dur)
    {
        var from = t.position;
        Face(t, to - from);
        float h = 1.1f + Mathf.Abs(to.y - from.y) * 0.4f;
        for (float k = 0; k < 1; k += Time.deltaTime / dur)
        {
            var p = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, k));
            p.y += Mathf.Sin(k * Mathf.PI) * h;
            t.position = p;
            Focus = p;
            yield return null;
        }
        t.position = to;
    }

    public IEnumerator AnimMove(MoveResult m)
    {
        var b = bunnies[m.player, m.rabbit];
        b.an.speed = speed;
        b.Play("Duck", 0.1f);
        yield return new WaitForSeconds(D(0.15f));
        foreach (int i in m.path)
        {
            var to = i >= rules.summit ? SummitSlot(m.player, m.rabbit) : Pos(i);
            b.Play("Jump", 0.05f);
            yield return Hop(b.t, to, D(0.5f));
            Sound.I.Play("hop" + Random.Range(1, 4));
            Dust(to);
        }
        if (m.fell)
        {
            Sound.I.Play("hole");
            yield return FallToPen(new List<(Bunny, int, int)> { (b, m.player, m.rabbit) });
        }
        else if (m.path.Count > 0 && m.path[m.path.Count - 1] >= rules.summit)
        {
            b.Play("Yes");
            Sound.I.Play("arrive");
            Confetti(b.t.position, 60);
            yield return new WaitForSeconds(D(1.4f));
            b.Play("Sitting_Eating", 0.3f);
        }
        else
        {
            b.Play("Idle", 0.2f);
            yield return new WaitForSeconds(D(0.25f));
        }
    }

    public IEnumerator AnimCarrot(CarrotResult res)
    {
        Focus = carrot.position;
        Sound.I.Play("crank", 1, 0);
        float y0 = carrot.eulerAngles.y;
        for (float k = 0; k < 1; k += Time.deltaTime / D(1.2f))
        {
            carrot.rotation = Quaternion.Euler(0, y0 + Mathf.SmoothStep(0, 1, k) * 360, Mathf.Sin(k * Mathf.PI * 6) * 4 * (1 - k));
            yield return null;
        }
        carrot.rotation = Quaternion.Euler(0, y0, 0);

        // Les anciens trous se referment, les nouveaux s'ouvrent et restent ouverts.
        var shut = res.closed.Where(i => !res.opened.Contains(i)).ToList();
        if (shut.Count > 0) yield return MoveStumps(shut, false);
        var fresh = res.opened.Where(i => !res.closed.Contains(i)).ToList();
        if (res.opened.Count > 0) Focus = stumpBase[res.opened[0]];
        yield return new WaitForSeconds(D(0.3f));
        Sound.I.Play("hole");
        var fallers = res.fallen.Select(f => (bunnies[f.player, f.rabbit], f.player, f.rabbit)).ToList();
        foreach (var f in fallers) f.Item1.Play("HitReact", 0.05f);
        yield return MoveStumps(fresh, true);
        if (fallers.Count > 0) yield return FallToPen(fallers);
        else yield return new WaitForSeconds(D(0.4f));
        Sync();
    }

    const float HoleDepth = 2.2f;
    readonly Dictionary<int, Vector3> stumpBase = new Dictionary<int, Vector3>();

    IEnumerator MoveStumps(List<int> cases, bool down)
    {
        float dur = D(down ? 0.35f : 0.45f);
        for (float k = 0; k < 1; k += Time.deltaTime / dur)
        {
            float e = down ? k * k : 1 - (1 - Mathf.Pow(1 - k, 3) + Mathf.Sin(k * Mathf.PI) * 0.08f);
            foreach (int i in cases) stumps[i].position = stumpBase[i] - Vector3.up * HoleDepth * e;
            yield return null;
        }
        foreach (int i in cases) stumps[i].position = stumpBase[i] - Vector3.up * (down ? HoleDepth : 0);
    }

    // Les stumps suivent rules.open (utile apres Build).
    void PlaceStumps()
    {
        foreach (var kv in stumps) kv.Value.position = stumpBase[kv.Key] - Vector3.up * (rules.open.Contains(kv.Key) ? HoleDepth : 0);
    }

    IEnumerator FallToPen(List<(Bunny b, int player, int rabbit)> fallers)
    {
        Sound.I.Play("fall");
        var start = fallers.Select(f => f.b.t.position).ToList();
        foreach (var f in fallers) f.b.Play("Death", 0.1f);
        for (float k = 0; k < 1; k += Time.deltaTime / D(0.7f))
        {
            for (int i = 0; i < fallers.Count; i++)
            {
                fallers[i].b.t.position = start[i] - Vector3.up * 4f * k * k;
                fallers[i].b.t.localScale = Vector3.one * (1 - k * 0.6f);
            }
            yield return null;
        }
        Sound.I.Play("lose");
        foreach (var f in fallers)
        {
            f.b.t.position = PenSlot(f.player, f.rabbit);
            Face(f.b.t, -f.b.t.position);
            f.b.Play("Idle", 0);
            Poof(f.b.t.position);
        }
        for (float k = 0; k < 1; k += Time.deltaTime / D(0.3f))
        {
            foreach (var f in fallers) f.b.t.localScale = Vector3.one * Mathf.SmoothStep(0, 1, k);
            yield return null;
        }
    }

    public IEnumerator Celebrate(int winner)
    {
        Focus = new Vector3(0, HillH, 0);
        Sound.I.Play("win", 1, 0);
        for (int p = 0; p < rules.players.Count; p++)
            for (int r = 0; r < Rules.RabbitsPerPlayer; r++)
                bunnies[p, r].Play(p == winner ? "Wave" : "No", 0.2f);
        for (int i = 0; i < 4; i++)
        {
            Confetti(new Vector3(0, HillH + 1, 0), 120);
            yield return new WaitForSeconds(0.5f);
        }
    }

    // --- Boucle -------------------------------------------------------------------------
    void Update()
    {
        if (rules == null) return;
        var cam = Camera.main;
        if (cam)
            foreach (var kv in labels)
            {
                kv.Value.gameObject.SetActive(showNumbers && !rules.open.Contains(kv.Key));
                kv.Value.rotation = Quaternion.Euler(90, cam.transform.eulerAngles.y, 0);
            }

        float pulse = 1 + Mathf.Sin(Time.time * 6) * 0.12f;
        for (int p = 0; p < rules.players.Count; p++)
            for (int r = 0; r < Rules.RabbitsPerPlayer; r++)
            {
                bool hot = !busy && !rules.Over && p == rules.turn && rules.CanMove(r);
                bunnies[p, r].ring.localScale = new Vector3(1.4f, 0.015f, 1.4f) * (hot ? pulse * 1.25f : 1);
                bunnies[p, r].ring.localScale = new Vector3(bunnies[p, r].ring.localScale.x, 0.015f, bunnies[p, r].ring.localScale.z);
            }

        if (rules.mode == Mode.Ameliore)
        {
            var threat = new HashSet<int>();
            for (int d = 1; d <= 2; d++)
            {
                int c = rules.known[(rules.rotations + d) % Rules.CycleLength];
                if (c > 0) threat.Add(c);
            }
            foreach (var kv in warn)
            {
                kv.Value.SetActive(threat.Contains(kv.Key));
                kv.Value.transform.localScale = new Vector3(2.7f * pulse, 0.03f, 2.7f * pulse);
            }
        }
    }
}
