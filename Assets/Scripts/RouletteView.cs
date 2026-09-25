using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// La roulette en 3D : cylindre Synty (ordre europeen, 0 vers +z, cases dans le sens horaire vu de dessus),
// bille lancee a contre-sens qui finit dans la case tiree. Rejoue les evenements produits par Roulette.
public class RouletteView : MonoBehaviour
{
    // Profil du modele Synty mesure par lancer de rayons (metres, sous le pivot du cylindre, qui est en haut de la croix) :
    // piste de la bille r 0.34-0.37 a -0.26, anneau des numeros r 0.26-0.33 a -0.28, cases r 0.21-0.24 a -0.307.
    // Hauteurs ci-dessous = centre de la bille (rayon 0.014).
    const float TrackR = 0.353f, TrackY = -0.244f, NumR = 0.285f, NumY = -0.265f, PocketR = 0.228f, PocketY = -0.292f, Idle = 12f;

    public float speed = 1;
    Transform spot, wheel, ball;
    Animator dealer;
    float wheelYaw;
    int pocket = -1;
    bool spinning;
    AudioSource roll;

    public bool TopView { get; private set; }   // plan fixe au-dessus du cylindre pendant le lancer

    // Place du joueur : assis cote +z de la table, face au tapis imprime (cadrage fixe, sans zoom).
    public Pose SeatPose => LookFrom(new Vector3(-0.42f, 1.42f, 0.98f), new Vector3(-0.42f, 0.78f, -0.12f));
    // Au-dessus du cylindre, joueurs en bas de l'image.
    public Pose TopPose => new Pose(wheel.position + Vector3.up * 0.72f, Quaternion.LookRotation(Vector3.down, -spot.forward));

    Pose LookFrom(Vector3 from, Vector3 at)
    {
        Vector3 a = spot.TransformPoint(from), b = spot.TransformPoint(at);
        return new Pose(a, Quaternion.LookRotation(b - a));
    }
    const float Felt = 0.781f;   // hauteur du tapis imprime (mesuree)
    readonly Dictionary<int, Transform> stacks = new Dictionary<int, Transform>();
    Mesh chipMesh;

    // Point du tapis HUD (Ui.Anchor, en pixels) -> point du tapis imprime sur la table Synty (repere local de la table).
    // Calage par vue de dessus : case 1 en (505, 405) px, pas de 40.5 px par colonne et 55 px par rangee.
    // Camera a 3.2 m, tapis a 0.78 m : 2.42 m de recul, champ de 50 deg sur 900 px.
    static readonly float PxPerM = 450 / ((3.2f - Felt) * Mathf.Tan(25 * Mathf.Deg2Rad));
    static Vector3 TableSpot(Vector2 a)
    {
        const float C = Ui.C;
        float px = 505 - (a.x - 1.5f * C) / C * 40.5f, py = 405 + (2.5f - a.y / C) * 55f;
        return new Vector3((px - 450) / PxPerM, Felt, (450 - py) / PxPerM);
    }

    // Mises d'un joueur : piles de vrais jetons (500, 100, 50, 10) posees sur un palet a sa couleur,
    // legerement decalees pour que plusieurs joueurs restent visibles sur la meme case.
    static readonly int[] Values = { 500, 100, 50, 10 };
    readonly Dictionary<string, Material> chipMats = new Dictionary<string, Material>();
    Material ChipMat(string key, System.Func<Material> make) { if (!chipMats.TryGetValue(key, out var m)) chipMats[key] = m = make(); return m; }
    Material Side(int v) => ChipMat("s" + v, () => new Material(Resources.Load<Material>("Lit")) { color = Board.Hex(v switch { 10 => "d8423a", 50 => "2f6fd6", 100 => "26262a", _ => "8a4fd0" }) });
    Material Face(int v) => ChipMat("f" + v, () =>
    {
        var m = new Material(Resources.Load<Material>("LitCutout")) { color = Color.white };
        m.SetTexture("_BaseMap", Resources.Load<Texture2D>("Casino/chip_" + v));
        return m;
    });

    public void ShowBets(int seat, IDictionary<string, int> bets)
    {
        if (stacks.TryGetValue(seat, out var old) && old) Destroy(old.gameObject);
        var root = new GameObject("mises " + seat).transform;
        root.SetParent(spot, false);
        stacks[seat] = root;
        if (!chipMesh) { var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder); chipMesh = c.GetComponent<MeshFilter>().sharedMesh; Destroy(c); }
        if (!quadMesh) { var q = GameObject.CreatePrimitive(PrimitiveType.Quad); quadMesh = q.GetComponent<MeshFilter>().sharedMesh; Destroy(q); }
        var puck = ChipMat("p" + seat, () => new Material(Resources.Load<Material>("Lit")) { color = Board.Colors[seat] });
        var offset = new Vector3((seat % 2) * 0.014f - 0.007f, 0, (seat / 2) * 0.014f - 0.007f);
        const float D = 0.05f, H = 0.0075f;
        foreach (var kv in bets)
        {
            var p = TableSpot(Ui.Anchor(kv.Key)) + offset;
            Part(chipMesh, p + Vector3.up * 0.0015f, new Vector3(D * 1.18f, 0.0015f, D * 1.18f), puck, root, false);
            float y = 0.003f;
            int left = kv.Value, top = 10, count = 0;
            foreach (int v in Values)
                for (; left >= v && count < 10; left -= v, count++)
                {
                    Part(chipMesh, p + Vector3.up * (y + H / 2), new Vector3(D, H / 2, D), Side(v), root, false);
                    y += H; top = v;
                }
            Part(quadMesh, p + Vector3.up * (y + 0.0004f), new Vector3(D, D, 1), Face(top), root, true);
        }
    }

    Mesh quadMesh;
    void Part(Mesh mesh, Vector3 pos, Vector3 scale, Material m, Transform parent, bool face)
    {
        var g = new GameObject("jeton");
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        if (face) g.transform.localRotation = Quaternion.Euler(90, 180, 0);   // lisible depuis la place des joueurs
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = m;
    }

    // Inverse de TableSpot : point du tapis (repere de la table) -> point du tapis HUD (Ui.Anchor), ou null hors du tapis.
    public Vector2? LayoutAt(Ray ray)
    {
        var plane = new Plane(spot.up, spot.TransformPoint(new Vector3(0, Felt, 0)));
        if (!plane.Raycast(ray, out float d)) return null;
        var p = spot.InverseTransformPoint(ray.GetPoint(d));
        float px = p.x * PxPerM + 450, py = 450 - p.z * PxPerM;
        const float C = Ui.C;
        return new Vector2(1.5f * C + (505 - px) / 40.5f * C, (2.5f - (py - 405) / 55f) * C);
    }

    // Survol : les cases couvertes par la mise s'allument sur le tapis.
    readonly List<GameObject> glow = new List<GameObject>();
    Material glowMat;
    string glowKey;
    public void Highlight(string key)
    {
        if (key == glowKey) return;
        glowKey = key;
        foreach (var g in glow) Destroy(g);
        glow.Clear();
        if (key == null) return;
        if (!glowMat)
        {
            glowMat = new Material(Resources.Load<Material>("LitGlow")) { color = Board.Hex("ffd35a") };
            glowMat.SetColor("_EmissionColor", Board.Hex("ffc83a") * 2.5f);
        }
        const float C = Ui.C;
        foreach (var n in Roulette.Numbers(key))
        {
            var c = n == 0 ? new Vector2(C / 2, 1.5f * C) : new Vector2(C + (n - 1) / 3 * C + C / 2, (2 - (n - 1) % 3) * C + C / 2);
            // Cadre dore autour de la case : les numeros restent lisibles.
            var center = TableSpot(c) + Vector3.up * 0.002f;
            float w = 40.5f / PxPerM, h = (n == 0 ? 165f : 55f) / PxPerM, t = 0.007f;
            foreach (var (off, size) in new[] { (new Vector3(0, 0, h / 2), new Vector3(w, t, 1)), (new Vector3(0, 0, -h / 2), new Vector3(w, t, 1)),
                                                (new Vector3(w / 2, 0, 0), new Vector3(t, h, 1)), (new Vector3(-w / 2, 0, 0), new Vector3(t, h, 1)) })
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(spot, false);
                q.transform.localPosition = center + off;
                q.transform.localRotation = Quaternion.Euler(90, 0, 0);
                q.transform.localScale = size;
                q.GetComponent<Renderer>().sharedMaterial = glowMat;
                q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glow.Add(q);
            }
        }
    }

    void ClearBets() { foreach (var t in stacks.Values) if (t) Destroy(t.gameObject); stacks.Clear(); }


    public void Init(Table table)
    {
        spot = table.RouletteSpot;
        dealer = table.RouletteDealer;
        wheel = spot.GetComponentsInChildren<Transform>().First(t => t.name.Contains("Wheel"));
        var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(b.GetComponent<Collider>());
        var m = new Material(Resources.Load<Material>("Lit")) { color = new Color(0.97f, 0.97f, 0.95f) };
        m.SetFloat("_Smoothness", 0.85f);
        b.GetComponent<Renderer>().sharedMaterial = m;
        ball = b.transform;
        ball.name = "Bille";
        ball.SetParent(wheel.parent, false);
        ball.localScale = Vector3.one * 0.028f;
        SetPocket(0);
    }

    void Update()
    {
        if (!wheel || spinning) return;
        wheelYaw += Idle * Time.deltaTime;
        wheel.localRotation = Quaternion.Euler(0, wheelYaw, 0);
        if (pocket >= 0) PlaceBall(wheelYaw + PocketAngle(pocket), PocketR, PocketY);
    }

    static float PocketAngle(int number) => Array.IndexOf(Roulette.Wheel, number) * 360f / 37f;

    void SetPocket(int number) => pocket = number;

    // Bille a un angle (degres, sens horaire vu de dessus depuis +z), un rayon et une hauteur, autour de l'axe du cylindre.
    void PlaceBall(float angle, float r, float y)
    {
        float a = angle * Mathf.Deg2Rad;
        ball.localPosition = wheel.localPosition + new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
    }

    public IEnumerator Play(List<REvent> evs, Action<string> say)
    {
        float Wait(float s) => s / Mathf.Max(0.25f, speed);
        foreach (var e in evs)
            switch (e.type)
            {
                case REv.RoundStart:
                    if (e.amount > 1) { yield return new WaitForSeconds(Wait(1.2f)); ClearBets(); }   // on laisse voir les mises gagnantes
                    say($"Faites vos jeux !  ·  {e.text}");
                    yield return new WaitForSeconds(Wait(0.6f));
                    break;
                case REv.Bets:
                    ShowBets(e.seat, (Roulette.Parse(e.text) ?? new List<RBet>()).ToDictionary(b => b.key, b => b.amount));
                    if (e.amount > 0) Sound.I.Play("bj_chips");
                    yield return new WaitForSeconds(Wait(0.3f));
                    break;
                case REv.Spin:
                    say("Rien ne va plus !");
                    Highlight(null);
                    TopView = true;
                    if (dealer) dealer.CrossFadeInFixedTime("PickUp", 0.2f);
                    yield return Spin(e.number, Wait(5.5f));
                    if (dealer) dealer.CrossFadeInFixedTime("Idle", 0.3f);
                    say(Roulette.Describe(e.number).Replace(",", " ·") + " !");
                    yield return new WaitForSeconds(Wait(1.6f));
                    TopView = false;
                    break;
                case REv.Result:
                    if (e.amount > 0) Sound.I.Play("bj_chips");
                    yield return new WaitForSeconds(Wait(0.25f));
                    break;
                case REv.GameOver:
                    yield return new WaitForSeconds(Wait(0.8f));
                    break;
            }
    }

    // Lancer realiste : la bille file a contre-sens sur la piste, ralentit, descend sur les numeros ou elle roule
    // et rebondit, puis tombe dans sa case et tourne avec le cylindre, qui ralentit jusqu'a son rythme de repos.
    IEnumerator Spin(int number, float duration)
    {
        pocket = -1;
        spinning = true;
        const float turns = 5, settle = 0.86f;       // tours de bille par rapport au cylindre ; moment ou elle est dans la case
        float t = 0, start = wheelYaw, startSpeed = 140;
        float Wheel(float k) => start + startSpeed * duration * k - (startSpeed - Idle) * duration * k * k * 0.5f;
        float bounceSeed = number * 1.7f;
        // Son enregistre (lancer, roulement, chute a 4.4 s) : cale sur l'animation, accelere avec la vitesse du jeu.
        if (!roll) { roll = Sound.I.Loop("rt_spin"); roll.loop = false; }
        roll.pitch = 5.5f / duration;
        roll.volume = Sound.I.SfxVolume;
        roll.time = 0;
        roll.Play();
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            wheelYaw = Wheel(k);
            wheel.localRotation = Quaternion.Euler(0, wheelYaw, 0);
            // Angle de la bille relatif au cylindre : part de loin et converge vers la case (sens inverse du cylindre).
            float q = Mathf.Clamp01(k / settle);
            float rel = PocketAngle(number) + turns * 360f * (1 - q) * (1 - q);
            float r, y;
            if (k < 0.5f) { r = TrackR; y = TrackY; }                                            // piste exterieure
            else if (k < 0.62f)
            {
                float d = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.5f, 0.62f, k));              // descend sur les numeros
                r = Mathf.Lerp(TrackR, NumR, d); y = Mathf.Lerp(TrackY, NumY, d);
            }
            else if (k < 0.8f)
            {
                float u = Mathf.InverseLerp(0.62f, 0.8f, k);                                     // roule et rebondit sur les numeros
                r = Mathf.Lerp(NumR, 0.262f, u) + Mathf.Sin(u * 17 + bounceSeed) * 0.012f * (1 - u);
                y = NumY + Mathf.Abs(Mathf.Sin(u * Mathf.PI * 5)) * 0.022f * (1 - u);
            }
            else
            {
                float d = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.8f, settle, k));            // tombe dans la case
                r = Mathf.Lerp(0.262f, PocketR, d); y = Mathf.Lerp(NumY, PocketY, d) + Mathf.Sin(d * Mathf.PI) * 0.01f;
            }
            PlaceBall(wheelYaw + rel, r, y);
            yield return null;
        }
        wheelYaw = Wheel(1);
        SetPocket(number);
        spinning = false;
    }
}
