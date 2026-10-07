using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Paintball en vue FPS dans une map d'Agrou. Chaque machine pilote son joueur (et l'hote / le hors ligne, les bots),
// envoie sa position ~20 fois par seconde et ses billes ; les autres sont interpoles. Le tireur decide s'il touche
// (comme dans la plupart des FPS entre amis : ce qu'on voit, on le touche), l'hote valide (Paintball).
public class PaintballView : MonoBehaviour
{
    public static readonly Color[] TeamColor = { Board.Hex("ff7a1a"), Board.Hex("1ab4ff") };
    const float Radius = 43, Eye = 1.6f, Speed = 4.8f, BallSpeed = 42, FireDelay = 0.17f, HitR = 0.42f;
    // Deplacement facon CS:GO (moteur Source, 1 unite = 1,905 cm) : course 250 u/s, marche x0,52, accroupi x0,34,
    // acceleration 5,5, frottement 5,2, gravite 800 u/s2, saut de 57 u, en l'air vitesse "voulue" plafonnee a 30 u/s
    // (d'ou le strafe en l'air et le bunny hop).
    const float U = 0.01905f, WalkK = 0.52f, CrouchK = 0.34f, Accel = 5.5f, AirAccel = 12f, Friction = 5.2f, StopSpeed = 80 * U,
        Gravity = 800 * U, JumpV = 301 * U, AirCap = 30 * U, StandH = 1.75f, CrouchH = 1.25f, CrouchEye = 1.12f;

    Paintball pb;
    Action<string> act, send;   // action fiable (hit, spawn) ; message temps reel
    int me;
    bool[] botSeat;
    Vector3 fire;
    Material lit, splatMat;
    Texture2D splatTex;
    Transform root;
    Font font;
    Material textMat;

    // --- Joueurs (moi compris : mon perso est cache, je ne vois que mon lanceur) ---
    class Av
    {
        public int seat, team;
        public Transform t, gun, head, hand;
        public Animator an;
        public TextMesh tag;
        public Vector3 pos, from, to, vel;
        public float yaw, pitch, tYaw, tPitch, lerp = 1, lastRx;
        public bool down, jumping;
        public float crouch;   // 0 debout, 1 accroupi (recu du reseau)
        public string anim;
        public readonly List<GameObject> paint = new List<GameObject>();
    }
    readonly List<Av> avs = new List<Av>();

    // Mon joueur.
    CharacterController cc;
    float yaw, pitch, vy, fireCd, sendAt, downUntil, recoil;
    public bool Down => avs.Count > me && pb.players[me].down;
    public float RespawnIn => Mathf.Max(0, downUntil - Time.time);
    public int HitBy { get; private set; } = -1;
    Transform viewGun;
    public bool Ready { get; private set; }

    // Bots (hors ligne, ou remplissage par l'hote) : position et etat geres ici.
    class BotBrain { public List<Vector3> path = new List<Vector3>(); public float think, fireCd, strafe, aimErr = 1; public int target = -1; public Vector3 goal; public float downUntil; }
    readonly Dictionary<int, BotBrain> bots = new Dictionary<int, BotBrain>();

    // Billes en vol.
    class Ball { public Vector3 p, v; public int seat; public float life; public Transform go; public bool judge; }
    readonly List<Ball> balls = new List<Ball>();
    readonly Queue<GameObject> splats = new Queue<GameObject>();

    void Awake()
    {
        lit = Resources.Load<Material>("Lit");
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        gameObject.SetActive(false);
    }

    Material Mat(Color c, float emit = 0, float smooth = 0.5f)
    {
        var m = new Material(lit) { color = c };
        m.SetFloat("_Smoothness", smooth);
        if (emit > 0) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * emit); }
        return m;
    }
    GameObject Prim(PrimitiveType t, Vector3 lpos, Vector3 scale, Material m, Transform parent)
    {
        var g = GameObject.CreatePrimitive(t);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = lpos; g.transform.localScale = scale;
        var r = g.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }

    // --- Mise en place ------------------------------------------------------------------------------------------
    bool MeBot => bots.ContainsKey(me);
    Transform vm; Animator vmAn;
    static readonly string[] ArmSlots = { "AUPL", "AUPR", "ALWL", "ALWR", "HNDL", "HNDR", "ASHL", "ASHR" };
    static bool ArmPart(Transform t) { for (; t; t = t.parent) if (System.Array.IndexOf(ArmSlots, t.name) >= 0) return true; return false; }
    // Bras de vue : la copie est posee pour que ses yeux soient sur la camera, tournee avec elle (visee comprise).
    void PlaceArms(Pose cam)
    {
        if (!vm) return;
        vm.gameObject.SetActive(!pb.players[me].down);
        vm.rotation = cam.rotation;
        vm.position = cam.position - cam.rotation * new Vector3(0, 1.6f, -0.06f);
    }
    public void Build(Paintball p, int mySeat, IList<string> avatars, bool[] isBot, Action<string> onAct, Action<string> onSend)
    {
        Clear();
        pb = p; me = mySeat; act = onAct; send = onSend; botSeat = isBot;
        gameObject.SetActive(true);
        fire = Clairiere.Center + new Vector3(0, 0, 0.5f);
        Clairiere.Show(true); Clairiere.Nature(false); Clairiere.Camp(true); Clairiere.HideFire(true); Clairiere.Day();
        int map = Array.FindIndex(AgrouMap.All, m => m.id == "PlaceDuVillage");
        AgrouMap.Show(map, fire);
        // Plein jour franc (couleurs vives) ; Clairiere.Show(false) remet l'ambiance d'origine en sortant.
        if (RenderSettings.sun) { RenderSettings.sun.intensity = 1.7f; RenderSettings.sun.color = Board.Hex("fff1d8"); }
        RenderSettings.ambientSkyColor = Board.Hex("cfe2f7"); RenderSettings.ambientEquatorColor = Board.Hex("e6d6b4"); RenderSettings.ambientGroundColor = Board.Hex("948062");
        RenderSettings.fogStartDistance = 60; RenderSettings.fogEndDistance = 220;
        // Collisions de la map autour de l'arene (murs, maisons, sol).
        foreach (var mf in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            var r = mf.GetComponent<Renderer>();
            if (!mf.sharedMesh || !r || !r.enabled || mf.GetComponent<Collider>() || !mf.sharedMesh.isReadable || r.bounds.SqrDistance(fire) > (Radius + 40) * (Radius + 40)) continue;
            if (r.bounds.size.y > 12) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // arbres geants : leur ombre noyait tout le village
            if (r.bounds.min.y > fire.y + 9) continue;   // cimes des arbres geants : pas d'obstacle
            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }
        root = new GameObject("paintball").transform;
        root.SetParent(transform, false);
        splatTex = SplatImage;
        splatMat = new Material(Shader.Find("PiqueNique/UnoFx"));
        splatMat.SetTexture("_MainTex", splatTex);
        splatMat.SetFloat("_SrcBlend", 5); splatMat.SetFloat("_DstBlend", 10); splatMat.SetFloat("_ZWrite", 0); splatMat.SetFloat("_Cull", 0);
        textMat = new Material(Resources.Load<Shader>("TextDepth")) { mainTexture = font.material.mainTexture };
        BuildNav();
        for (int s = 0; s < p.players.Count; s++)
        {
            var a = new Av { seat = s, team = Paintball.TeamOf(s) };
            a.t = Chars.Spawn(s < avatars.Count ? avatars[s] : Chars.Default, root, Vector3.zero, 0, out a.an, 1.75f);
            a.an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (a.an.layerCount > 1) { a.an.SetLayerWeight(1, 1); a.an.Play("wg_Idle_Aiming_Anim_mixamo_com", 1, 0); }
            a.head = a.an.GetBoneTransform(HumanBodyBones.Head);
            a.hand = a.an.GetBoneTransform(HumanBodyBones.RightHand);
            a.gun = Gun(root, a.team, GunLen).transform;   // devant le buste, dans l'axe de visee ; les mains s'y posent
            Hold(a);
            a.pos = a.from = a.to = SpawnPoint(s);
            a.t.position = a.pos;
            a.yaw = a.tYaw = Quaternion.LookRotation(fire - a.pos).eulerAngles.y;
            a.tag = Label(a.t, p.players[s].name, TeamColor[a.team]);
            if (s == me) a.tag.gameObject.SetActive(false);   // mon corps reste visible (sans la tete) ; mon lanceur est celui de la vue
            avs.Add(a);
            if (isBot[s]) bots[s] = new BotBrain { aimErr = 0.9f + (s % 3) * 0.35f };
        }
        // Mon joueur : une capsule qui se cogne aux murs, la camera a hauteur des yeux.
        var mine = new GameObject("moi");
        mine.transform.SetParent(root, false);
        cc = mine.AddComponent<CharacterController>();
        cc.height = 1.75f; cc.radius = 0.35f; cc.center = Vector3.up * 0.875f; cc.stepOffset = 0.45f; cc.slopeLimit = 50;
        cc.enabled = false; mine.transform.position = avs[me].pos; cc.enabled = true;
        yaw = avs[me].yaw; pitch = 0;
        viewGun = avs[me].gun;
        // Bras de vue : une copie de mon perso dont on ne garde que les bras, dans une pose fixe, mains sur le lanceur ;
        // elle suit la camera sans le balancement de la course. Mon corps dans le monde, lui, n'a plus de bras.
        vm = Chars.Spawn(me < avatars.Count ? avatars[me] : Chars.Default, root, Vector3.zero, 0, out vmAn, 1.75f);
        vmAn.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        vmAn.Play("Idle", 0, 0); vmAn.speed = 0;   // pose figee : seuls les mains (IK) suivent le lanceur
        if (vmAn.layerCount > 1) vmAn.SetLayerWeight(1, 0);
        foreach (var r in vm.GetComponentsInChildren<Renderer>(true)) { r.enabled = ArmPart(r.transform); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        // Comme dans CS:GO : mon corps n'est pas dessine (seuls les bras de vue et le lanceur) ; les autres le voient.
        foreach (var r in avs[me].t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        Destroy(avs[me].an.GetComponent<FpsHands>());
        var vh = vmAn.gameObject.AddComponent<FpsHands>();
        vh.right = viewGun.Find("main droite"); vh.left = viewGun.Find("main gauche"); vh.lookWeight = 0;
        ViewModelCamera();
        Ready = true;
    }

    // Comme dans CS:GO, les bras et le lanceur sont dessines par-dessus le decor (jamais dans un mur) : calque a part,
    // rendu par une camera superposee a la camera principale (pile URP).
    const int ViewLayer = 31;
    Camera vmCam;
    void ViewModelCamera()
    {
        var main = Camera.main;
        if (!main) return;
        foreach (var t in vm.GetComponentsInChildren<Transform>(true).Concat(viewGun.GetComponentsInChildren<Transform>(true))) t.gameObject.layer = ViewLayer;
        main.cullingMask &= ~(1 << ViewLayer);
        vmCam = new GameObject("camera des bras").AddComponent<Camera>();
        vmCam.transform.SetParent(main.transform, false);
        vmCam.cullingMask = 1 << ViewLayer;
        vmCam.nearClipPlane = 0.01f; vmCam.farClipPlane = 5;
        var d = vmCam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        d.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
        var md = main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() ?? main.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        md.cameraStack.Add(vmCam);
    }
    void LateUpdateViewCam() { if (vmCam && Camera.main) vmCam.fieldOfView = 68; }

    public void Clear()
    {
        if (vmCam)
        {
            var main = Camera.main;
            if (main) { main.cullingMask |= 1 << ViewLayer; main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()?.cameraStack.Remove(vmCam); }
            Destroy(vmCam.gameObject);
        }
        Ready = false;
        if (root) Destroy(root.gameObject);
        avs.Clear(); bots.Clear(); balls.Clear(); splats.Clear();
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        pb = null;
    }

    public void Hide() { Clear(); gameObject.SetActive(false); Clairiere.Show(false); }

    TextMesh Label(Transform parent, string s, Color c)
    {
        var t = new GameObject("nom").AddComponent<TextMesh>();
        t.transform.SetParent(parent, false);
        t.font = font; t.GetComponent<MeshRenderer>().sharedMaterial = textMat;
        t.text = s; t.color = c; t.fontSize = 48; t.characterSize = 0.045f / parent.localScale.x; t.anchor = TextAnchor.MiddleCenter; t.fontStyle = FontStyle.Bold;
        t.transform.localPosition = Vector3.up * 2.15f / parent.localScale.x;
        return t;
    }

    // Lanceur de paintball en formes simples : corps a la couleur de l'equipe, reservoir de billes, canon.
    // Lanceur : le pistolet a billets du pack Casino, recolore a la couleur de l'equipe, dans un repere ou le canon
    // pointe vers +z et le dessus vers +y ; longueur len (m). Sinon, un lanceur en formes simples.
    public static Vector3 GunFlip = new Vector3(1, 1, 1);   // signes des axes (verifie : le prefab a deja le canon vers +z)
    GameObject Gun(Transform parent, int team, float len)
    {
        var prefab = Synty.Get("SM_Wep_Paintball_Gun_01") ?? Synty.Get("SM_Prop_Cash_Gun_01");
        if (!prefab) return BoxGun(parent, team, len / 0.45f);
        var holder = new GameObject("lanceur");
        var g = Instantiate(prefab, Vector3.zero, Quaternion.identity);
        foreach (var c in g.GetComponentsInChildren<Collider>()) Destroy(c);
        var rs = g.GetComponentsInChildren<Renderer>();
        Bounds Box() { var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds); return bb; }
        var sz = Box().size;
        int[] ax = { 0, 1, 2 };
        System.Array.Sort(ax, (x, y) => sz[x].CompareTo(sz[y]));   // ax[0] le plus fin (largeur), ax[1] hauteur, ax[2] longueur (canon)
        Vector3 A(int k) => k == 0 ? Vector3.right : k == 1 ? Vector3.up : Vector3.forward;
        // On tourne le modele pour que son axe long aille vers +z et sa hauteur vers +y (signes : GunFlip).
        g.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(A(ax[2]) * GunFlip.z, A(ax[1]) * GunFlip.y)) * g.transform.rotation;   // compose avec la rotation propre du prefab
        g.transform.position -= Box().center;
        g.transform.SetParent(holder.transform, true);
        holder.transform.localScale = Vector3.one * (len / sz[ax[2]]);
        // Pack Kids en shader Standard (rose en URP) : materiau Lit avec sa texture, legerement teinte aux couleurs de l'equipe.
        var tint = Color.Lerp(TeamColor[team], Color.white, 0.55f);
        foreach (var r in rs)
        {
            r.materials = r.sharedMaterials.Select(m => { var c = new Material(lit) { color = tint }; c.SetTexture("_BaseMap", m ? m.mainTexture : null); c.SetFloat("_Smoothness", 0.45f); return c; }).ToArray();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        // Reperes des mains (mesures sur le modele de profil) : poignee pistolet, et devant du corps sous le canon.
        foreach (var (n, p) in new[] { ("main droite", new Vector3(0, -0.07f, 0.08f)), ("main gauche", new Vector3(0, -0.02f, 0.25f)) })
        {
            var t = new GameObject(n).transform; t.SetParent(holder.transform, false); t.localPosition = p;
        }
        holder.transform.SetParent(parent, false);
        return holder;
    }

    // Le perso tient son lanceur : mains sur les poignees, buste vers la cible.
    const float GunLen = 0.62f;
    static readonly Vector3 GunAt = new Vector3(0.13f, -0.22f, 0.4f);   // place du lanceur par rapport aux yeux
    void Aim(Av a, Pose eye)
    {
        var h = a.an ? a.an.GetComponent<FpsHands>() : null;
        if (!h) return;
        h.look = eye.position + eye.rotation * Vector3.forward * 10;
        h.crouch = a.seat == me ? 0 : a.crouch;
        h.weight = pb.players[a.seat].down ? 0 : 1;
    }
    void Hold(Av a)
    {
        var h = a.an.gameObject.GetComponent<FpsHands>() ?? a.an.gameObject.AddComponent<FpsHands>();
        h.right = a.gun.Find("main droite"); h.left = a.gun.Find("main gauche");
    }

    GameObject BoxGun(Transform parent, int team, float k)
    {
        var g = new GameObject("lanceur");
        g.transform.SetParent(parent, false);
        var body = Mat(TeamColor[team], 0.15f); var dark = Mat(Board.Hex("2a2a33")); var hop = Mat(Color.Lerp(TeamColor[team], Color.white, 0.5f), 0.3f, 0.9f);
        Prim(PrimitiveType.Cube, new Vector3(0, 0, 0.05f) * k, new Vector3(0.07f, 0.11f, 0.32f) * k, body, g.transform);
        Prim(PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0.36f) * k, new Vector3(0.035f, 0.17f, 0.035f) * k, dark, g.transform).transform.localRotation = Quaternion.Euler(90, 0, 0);
        Prim(PrimitiveType.Sphere, new Vector3(0, 0.11f, 0.0f) * k, new Vector3(0.09f, 0.08f, 0.11f) * k, hop, g.transform);
        Prim(PrimitiveType.Cube, new Vector3(0, -0.1f, -0.03f) * k, new Vector3(0.05f, 0.13f, 0.06f) * k, dark, g.transform).transform.localRotation = Quaternion.Euler(-15, 0, 0);
        return g;
    }

    static Texture2D splatShared;
    public static Texture2D SplatImage => splatShared ? splatShared : splatShared = SplatTexture();
    static Texture2D SplatTexture()
    {
        const int N = 64;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var rng = new System.Random(5);
        var blobs = Enumerable.Range(0, 9).Select(_ => (x: (float)rng.NextDouble() * 0.6f + 0.2f, y: (float)rng.NextDouble() * 0.6f + 0.2f, r: (float)rng.NextDouble() * 0.12f + 0.05f)).ToList();
        blobs.Add((0.5f, 0.5f, 0.28f));
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N, v = (y + 0.5f) / N, a = 0;
                foreach (var b in blobs) a = Mathf.Max(a, Mathf.Clamp01((b.r - Mathf.Sqrt((u - b.x) * (u - b.x) + (v - b.y) * (v - b.y))) * 40));
                px[y * N + x] = new Color(1, 1, 1, a);
            }
        t.SetPixels(px); t.Apply();
        return t;
    }

    // --- Bases et grille de deplacement des bots -------------------------------------------------------------------
    const float Cell = 1.5f;
    int gridN;
    bool[,] free;
    float[,] height;
    Vector3[] bases = new Vector3[2];

    float GroundAt(Vector3 p, out bool ok)
    {
        ok = false; float best = float.MinValue;
        foreach (var h in Physics.RaycastAll(new Vector3(p.x, fire.y + 12, p.z), Vector3.down, 30))
            if (h.point.y < fire.y + 8 && h.point.y > best) best = h.point.y;
        if (best == float.MinValue) return fire.y;
        ok = best < fire.y + 0.7f && best > fire.y - 3;
        return best;
    }

    void BuildNav()
    {
        gridN = Mathf.CeilToInt(Radius * 2 / Cell) + 1;
        free = new bool[gridN, gridN]; height = new float[gridN, gridN];
        for (int i = 0; i < gridN; i++)
            for (int j = 0; j < gridN; j++)
            {
                var w = CellPos(i, j);
                if ((w - fire).sqrMagnitude > Radius * Radius) continue;
                float h = GroundAt(w, out bool ok);
                height[i, j] = h;
                free[i, j] = ok && !Physics.CheckCapsule(new Vector3(w.x, h + 0.6f, w.z), new Vector3(w.x, h + 1.6f, w.z), 0.45f);
            }
        // Bases : les zones libres les plus a l'ouest et a l'est de la place (rues opposees).
        bases[0] = NearestFree(fire + new Vector3(-30, 0, 0));
        bases[1] = NearestFree(fire + new Vector3(30, 0, 0));
    }
    Vector3 CellPos(int i, int j) => fire + new Vector3((i - gridN / 2) * Cell, 0, (j - gridN / 2) * Cell);
    (int, int) CellOf(Vector3 w) => (Mathf.Clamp(Mathf.RoundToInt((w.x - fire.x) / Cell) + gridN / 2, 0, gridN - 1), Mathf.Clamp(Mathf.RoundToInt((w.z - fire.z) / Cell) + gridN / 2, 0, gridN - 1));
    Vector3 NearestFree(Vector3 w)
    {
        var (ci, cj) = CellOf(w);
        for (int r = 0; r < gridN; r++)
            for (int i = ci - r; i <= ci + r; i++)
                for (int j = cj - r; j <= cj + r; j++)
                    if (i >= 0 && j >= 0 && i < gridN && j < gridN && free[i, j] && Region(i, j)) { var p = CellPos(i, j); p.y = height[i, j]; return p; }
        return fire;
    }
    // Case reliee a la place centrale (pas un jardin ferme) : verifie une fois par une inondation depuis le centre.
    bool[,] reach;
    bool Region(int i, int j)
    {
        if (reach == null)
        {
            reach = new bool[gridN, gridN];
            var (si, sj) = (gridN / 2, gridN / 2);
            var q = new Queue<(int, int)>();
            for (int r = 0; r < 6 && q.Count == 0; r++) for (int a = -r; a <= r && q.Count == 0; a++) for (int b = -r; b <= r && q.Count == 0; b++) if (free[si + a, sj + b]) { reach[si + a, sj + b] = true; q.Enqueue((si + a, sj + b)); }
            while (q.Count > 0)
            {
                var (x, y) = q.Dequeue();
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= gridN || ny >= gridN || reach[nx, ny] || !free[nx, ny]) continue;
                    if (dx != 0 && dy != 0 && (!free[x + dx, y] || !free[x, y + dy])) continue;
                    reach[nx, ny] = true; q.Enqueue((nx, ny));
                }
            }
        }
        return reach[i, j];
    }

    Vector3 SpawnPoint(int seat)
    {
        // Chacun sa place a la base (en eventail), jamais sur un equipier.
        int team = Paintball.TeamOf(seat), k = seat / 2;
        var b = bases[team];
        var want = b + new Vector3((team == 0 ? -1 : 1) * (k % 2) * 2.4f, 0, (k - 2) * 2.4f);
        var p = NearestFree(want);
        for (int tries = 0; tries < 8 && avs.Any(o => o.seat != seat && o.team == team && (o.pos - p).sqrMagnitude < 1.5f * 1.5f); tries++)
            p = NearestFree(want + new Vector3(UnityEngine.Random.Range(-4f, 4f), 0, UnityEngine.Random.Range(-4f, 4f)));
        return p;
    }

    // Chemin le plus court sur la grille (A*, 8 voisins).
    List<Vector3> Path(Vector3 from, Vector3 to)
    {
        var (si, sj) = CellOf(from); var (ti, tj) = CellOf(NearestFree(to));
        var open = new SortedSet<(float f, int i, int j)>();
        var g = new Dictionary<(int, int), float> { [(si, sj)] = 0 };
        var prev = new Dictionary<(int, int), (int, int)>();
        open.Add((0, si, sj));
        int guard = 0;
        while (open.Count > 0 && guard++ < 6000)
        {
            var cur = open.Min; open.Remove(cur);
            if (cur.i == ti && cur.j == tj) break;
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = cur.i + dx, ny = cur.j + dy;
                if (nx < 0 || ny < 0 || nx >= gridN || ny >= gridN || !free[nx, ny]) continue;
                if (dx != 0 && dy != 0 && (!free[cur.i + dx, cur.j] || !free[cur.i, cur.j + dy])) continue;
                float ng = g[(cur.i, cur.j)] + (dx != 0 && dy != 0 ? 1.414f : 1);
                if (g.TryGetValue((nx, ny), out float old) && old <= ng) continue;
                g[(nx, ny)] = ng; prev[(nx, ny)] = (cur.i, cur.j);
                open.Add((ng + Mathf.Sqrt((nx - ti) * (nx - ti) + (ny - tj) * (ny - tj)), nx, ny));
            }
        }
        var path = new List<Vector3>();
        var c = (ti, tj);
        if (!prev.ContainsKey(c)) return path;
        while (c != (si, sj)) { var p = CellPos(c.Item1, c.Item2); p.y = height[c.Item1, c.Item2]; path.Add(p); c = prev[c]; }
        path.Reverse();
        return path;
    }

    // --- Camera ---------------------------------------------------------------------------------------------------
    public Pose CamPose
    {
        get
        {
            if (!Ready) return new Pose(fire + Vector3.up * 3, Quaternion.identity);
            var basePos = eyeAt;
            var rot = Quaternion.Euler(pitch - recoil, yaw, 0);
            if (Down)
            {
                // Touche : la camera glisse vers le bas et regarde le tireur.
                var by = HitBy >= 0 && HitBy < avs.Count ? avs[HitBy].pos + Vector3.up * 1.4f : basePos + Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                return new Pose(basePos - Vector3.up * 0.9f, Quaternion.Slerp(rot, Quaternion.LookRotation(by - basePos), 0.8f));
            }
            return new Pose(basePos, rot);
        }
    }

    // --- Boucle ---------------------------------------------------------------------------------------------------
    public bool InputOn;   // pas en pause, fenetre active
    public float Sens = 1;

    void Update()
    {
        if (!Ready || pb == null) return;
        float dt = Time.deltaTime;
        MoveMe(dt);
        foreach (var kv in bots) BotThink(kv.Key, kv.Value, dt);
        foreach (var a in avs) Animate(a, dt);
        Balls(dt);
        if (Time.time > sendAt)
        {
            sendAt = Time.time + 0.05f;
            if (!MeBot) Send(me, cc.transform.position, yaw, pitch, crouchK);
            foreach (var kv in bots) Send(kv.Key, avs[kv.Key].pos, avs[kv.Key].tYaw, avs[kv.Key].tPitch);
        }
    }

    // Ma tete (et mes cheveux) cachee : je vois mon corps en baissant les yeux, pas l'interieur de mon crane.
    // L'oeil suit la tete a l'horizontale (le corps se penche en courant), a hauteur fixe (pas de balancement).
    Vector3 eyeAt;
    void LateUpdate()
    {
        if (!Ready || avs.Count <= me) return;
        LateUpdateViewCam();
        var a = avs[me];
        // Tete et bras caches (les bras du perso ne tiennent pas le lanceur affiche a l'ecran) ; torse et jambes visibles.
        foreach (var bone in new[] { HumanBodyBones.Head })
        { var t = a.an.GetBoneTransform(bone); if (t) t.localScale = Vector3.one * 0.001f; }
    }

    // Oeil fixe par rapport au joueur (pas de balancement) : calcule avant de placer le lanceur et les bras de vue,
    // pour qu'ils restent immobiles a l'ecran.
    void UpdateEye()
    {
        var body = MeBot ? avs[me].pos : cc.transform.position;
        eyeAt = body + Vector3.up * Mathf.Lerp(Eye, CrouchEye, crouchK) + Quaternion.Euler(0, yaw, 0) * Vector3.forward * 0.15f;
    }

    // Autotest : force le regard vers le bas (voir son corps).
    public float? LookDown;

    void Send(int seat, Vector3 p, float y, float pi, float cr = 0) =>
        send?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture, "fp|{0}|{1:0.00}|{2:0.00}|{3:0.00}|{4:0}|{5:0}|{6:0.0}", seat, p.x - fire.x, p.y - fire.y, p.z - fire.z, y, pi, cr));

    Vector3 vel;
    bool crouched, walking;
    float crouchK, stepT;
    public float Spread { get; private set; }   // imprecision actuelle (viseur), comme dans CS:GO
    public bool AutoCrouch, AutoJump;          // autotest

    // Acceleration Source : on ajoute de la vitesse dans la direction voulue, sans depasser wishSpeed dans cette direction.
    static Vector3 Accelerate(Vector3 v, Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        if (wishDir.sqrMagnitude < 1e-4f) return v;
        wishDir.Normalize();
        float add = wishSpeed - Vector3.Dot(v, wishDir);
        if (add <= 0) return v;
        return v + wishDir * Mathf.Min(accel * dt, add);
    }

    void MoveMe(float dt)
    {
        var a = avs[me];
        if (MeBot)
        {
            // Autotest : je suis un bot ; la camera suit mon perso.
            cc.enabled = false; cc.transform.position = a.pos; cc.enabled = true;
            yaw = a.tYaw; pitch = LookDown ?? a.tPitch;
            UpdateEye();
            var cp0 = CamPose;
            viewGun.gameObject.SetActive(!pb.players[me].down);
            viewGun.position = cp0.position + cp0.rotation * GunAt;
            viewGun.rotation = cp0.rotation * Quaternion.Euler(0, -3, 0);
            PlaceArms(cp0);
            return;
        }
        bool down = pb.players[me].down;
        if (InputOn && !down)
        {
            if (Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            yaw += Input.GetAxisRaw("Mouse X") * 2.2f * Sens;
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 2.2f * Sens, -80, 80);
        }
        else if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        if (LookDown.HasValue) pitch = LookDown.Value;
        var dir = Vector3.zero;
        bool live = InputOn && !down;
        if (live)
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.UpArrow)) dir.z += 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) dir.z -= 1;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftArrow)) dir.x -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dir.x += 1;
        }
        if (Auto != null && !down) dir = Auto(this);   // autotest : pilote automatique
        var wish = Quaternion.Euler(0, yaw, 0) * Vector3.ClampMagnitude(dir, 1);
        // Accroupi (Ctrl, maintenu) : la capsule et l'oeil descendent ; on ne se releve que s'il y a la place.
        bool wantCrouch = live && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C)) || AutoCrouch;
        if (!wantCrouch && crouched && Physics.SphereCast(cc.transform.position + Vector3.up * (CrouchH - 0.35f), 0.33f, Vector3.up, out _, StandH - CrouchH + 0.05f)) wantCrouch = true;
        crouched = wantCrouch;
        crouchK = Mathf.MoveTowards(crouchK, crouched ? 1 : 0, dt * 8);
        float h = Mathf.Lerp(StandH, CrouchH, crouchK);
        cc.height = h; cc.center = Vector3.up * h / 2;
        // Marche (Maj : silencieuse et precise) / course / accroupi.
        walking = live && Input.GetKey(KeyCode.LeftShift);
        float maxSp = Speed * (crouched ? CrouchK : walking ? WalkK : 1);
        bool ground = cc.isGrounded;
        var hv = new Vector3(vel.x, 0, vel.z);
        if (ground)
        {
            // Frottement, puis acceleration vers la vitesse voulue (sv_friction 5.2, sv_accelerate 5.5).
            float spd = hv.magnitude;
            if (spd > 0.01f) { float drop = Mathf.Max(spd, StopSpeed) * Friction * dt; hv *= Mathf.Max(0, spd - drop) / spd; }
            hv = Accelerate(hv, wish, wish.sqrMagnitude > 0 ? maxSp : 0, Accel * maxSp, dt);
            vel.y = -2;
            // Saut (Espace ou molette, comme beaucoup de joueurs de CS).
            if (live && (Input.GetKeyDown(KeyCode.Space) || Mathf.Abs(Input.mouseScrollDelta.y) > 0.1f) || AutoJump) { AutoJump = false; vel.y = JumpV; ground = false; Sound.I.Play("hop2", 0.25f, 0.1f); }
        }
        else
        {
            hv = Accelerate(hv, wish, Mathf.Min(maxSp, AirCap), AirAccel * maxSp, dt);   // strafe en l'air
            vel.y -= Gravity * dt;
        }
        vel.x = hv.x; vel.z = hv.z;
        var before = cc.transform.position;
        var flags = cc.Move(vel * dt);
        if ((flags & CollisionFlags.Above) != 0 && vel.y > 0) vel.y = 0;
        if (!ground) { var real = (cc.transform.position - before) / Mathf.Max(dt, 1e-4f); vel.x = real.x; vel.z = real.z; }   // on glisse le long des murs
        // Pas de course (la marche et l'accroupi sont silencieux).
        if (cc.isGrounded && !walking && !crouched && hv.magnitude > 3) { stepT -= dt * hv.magnitude; if (stepT <= 0) { stepT = 2.2f; Sound.I.Play("tick", 0.12f, 0.3f); } }
        // Limite de l'arene.
        var off = cc.transform.position - fire; off.y = 0;
        if (off.magnitude > Radius) { cc.enabled = false; cc.transform.position -= off.normalized * (off.magnitude - Radius); cc.enabled = true; }
        a.pos = cc.transform.position; a.tYaw = yaw; a.tPitch = pitch;
        UpdateEye();
        // Imprecision : a l'arret la meilleure (accroupi encore mieux), en courant plus large, en l'air enorme.
        float hs = new Vector3(vel.x, 0, vel.z).magnitude;
        float target = (crouched ? 0.006f : 0.01f) + Mathf.InverseLerp(Speed * WalkK, Speed, hs) * 0.07f + (cc.isGrounded ? 0 : 0.14f);
        Spread = Mathf.Lerp(Spread, target, 1 - Mathf.Exp(-dt * 10));
        // Tir.
        fireCd -= dt; recoil = Mathf.MoveTowards(recoil, 0, dt * 25);
        bool trigger = InputOn && Input.GetMouseButton(0) && Cursor.lockState == CursorLockMode.Locked || AutoFire;
        if (trigger && !down && fireCd <= 0)
        {
            fireCd = FireDelay; recoil = 2.2f;
            var cp = CamPose;
            var spread = UnityEngine.Random.insideUnitCircle * Spread;
            var d = (cp.rotation * new Vector3(spread.x, spread.y, 1)).normalized;
            Shoot(me, cp.position + d * 0.6f - cp.rotation * Vector3.up * 0.12f, d, true);
        }
        // Lanceur a l'ecran (en bas a droite), recul.
        var cpose = CamPose;
        viewGun.gameObject.SetActive(!down);
        viewGun.position = cpose.position + cpose.rotation * (GunAt + Vector3.back * recoil * 0.02f);
        a.vel = cc.velocity;
        viewGun.rotation = cpose.rotation * Quaternion.Euler(-recoil * 2, -3, 0);
        PlaceArms(cpose);
        // Retour en jeu apres une touche.
        if (down && downUntil > 0 && Time.time > downUntil) { downUntil = 0; act?.Invoke("spawn|" + me); }
    }

    // Autotest : direction voulue (repere du joueur) et gachette.
    public Func<PaintballView, Vector3> Auto;
    public bool AutoFire;
    public float MyYaw { get => yaw; set => yaw = value; }
    public float MyHeight => cc ? cc.transform.position.y : 0;
    public float MyPitch { get => pitch; set => pitch = value; }
    public Vector3 MyPos => cc ? cc.transform.position : fire;
    public Vector3 AvatarPos(int s) => avs[s].pos;
    public float AvatarYaw(int s) => avs[s].yaw;
    public IEnumerable<(int seat, Vector3 pos, bool down)> Others => avs.Where(a => a.seat != me).Select(a => (a.seat, a.pos, pb.players[a.seat].down));

    // --- Billes ---------------------------------------------------------------------------------------------------
    void Shoot(int seat, Vector3 from, Vector3 dir, bool judge, bool net = true)
    {
        var b = new Ball { p = from, v = dir * BallSpeed, seat = seat, judge = judge };
        b.go = Prim(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.09f, Mat(TeamColor[Paintball.TeamOf(seat)], 1.2f), root).transform;
        b.go.position = from;
        balls.Add(b);
        Sound.I.Play("hop1", seat == me ? 0.35f : 0.18f, 0.25f);
        if (seat < avs.Count && seat != me && avs[seat].an && avs[seat].an.layerCount > 1) avs[seat].an.Play("wg_Firing_Rifle_Anim_mixamo_com", 1, 0);
        if (net) send?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture, "fs|{0}|{1:0.00}|{2:0.00}|{3:0.00}|{4:0.000}|{5:0.000}|{6:0.000}", seat, from.x - fire.x, from.y - fire.y, from.z - fire.z, dir.x, dir.y, dir.z));
    }

    void Balls(float dt)
    {
        for (int k = balls.Count - 1; k >= 0; k--)
        {
            var b = balls[k];
            var next = b.p + b.v * dt;
            b.v += Vector3.down * 4.5f * dt;
            b.life += dt;
            bool gone = b.life > 3;
            // Joueurs : seul le tireur (sa machine, ou l'hote pour un bot) decide de la touche.
            int shooterTeam = Paintball.TeamOf(b.seat);
            foreach (var a in avs)
            {
                if (a.seat == b.seat || a.team == shooterTeam || pb.players[a.seat].down) continue;
                var body = a.seat == me && !MeBot ? cc.transform.position : a.pos;
                float top = Mathf.Lerp(1.55f, 1.05f, a.seat == me && !MeBot ? crouchK : a.crouch);
                if (SegCapsule(b.p, next, body + Vector3.up * 0.3f, body + Vector3.up * top) < HitR)
                {
                    Splat(next, (b.p - next).normalized, a.team == 0 ? 1 : 0, a.seat == me ? null : a);
                    if (b.judge) act?.Invoke($"hit|{b.seat}|{a.seat}");
                    gone = true; break;
                }
            }
            if (!gone && Physics.Linecast(b.p, next, out var hit)) { Splat(hit.point, hit.normal, shooterTeam, null); gone = true; }
            if (gone) { Destroy(b.go.gameObject); balls.RemoveAt(k); continue; }
            b.p = next; b.go.position = next;
        }
    }

    static float SegCapsule(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        // Distance entre deux segments (approchee par echantillonnage, largement suffisant a cette echelle).
        float best = float.MaxValue;
        for (int i = 0; i <= 6; i++)
        {
            var p = Vector3.Lerp(a, b, i / 6f);
            var cd = d - c; float t = Mathf.Clamp01(Vector3.Dot(p - c, cd) / cd.sqrMagnitude);
            best = Mathf.Min(best, Vector3.Distance(p, c + cd * t));
        }
        return best;
    }

    void Splat(Vector3 at, Vector3 normal, int team, Av on)
    {
        var g = new GameObject("tache");
        g.transform.SetParent(on != null ? on.t : root, true);
        var q = g.AddComponent<MeshFilter>(); q.sharedMesh = QuadMesh();
        var r = g.AddComponent<MeshRenderer>();
        var m = new Material(splatMat); m.SetColor("_Tint", TeamColor[team] * 0.5f + new Color(0, 0, 0, 0.5f));
        r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        g.transform.position = at + normal * 0.02f;
        g.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0, 0, UnityEngine.Random.Range(0, 360f));
        float s = on != null ? 0.35f : UnityEngine.Random.Range(0.45f, 0.8f);
        g.transform.localScale = Vector3.one * s / (on != null ? on.t.lossyScale.x : 1);
        if (on != null) on.paint.Add(g);
        splats.Enqueue(g);
        while (splats.Count > 400) { var o = splats.Dequeue(); if (o) Destroy(o); }
        if (on == null) Sound.I.Play("bj_chip1", 0.12f, 0.3f);
    }
    static Mesh quad;
    static Mesh QuadMesh()
    {
        if (quad) return quad;
        quad = new Mesh { vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) }, uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }, triangles = new[] { 0, 2, 1, 0, 3, 2 } };
        quad.RecalculateNormals();
        return quad;
    }

    // --- Reseau ---------------------------------------------------------------------------------------------------
    public void OnRealtime(string[] p)
    {
        if (!Ready || p.Length < 2 || !int.TryParse(p[1], out int s) || s < 0 || s >= avs.Count || s == me || bots.ContainsKey(s)) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        float F(int i) => float.Parse(p[i], ci);
        if (p[0] == "fp" && p.Length >= 7)
        {
            var a = avs[s];
            a.from = a.pos; a.to = fire + new Vector3(F(2), F(3), F(4)); a.lerp = 0;
            a.tYaw = F(5); a.tPitch = F(6);
            a.crouch = p.Length >= 8 ? F(7) : 0;
            a.lastRx = Time.time;
        }
        else if (p[0] == "fs" && p.Length >= 8) Shoot(s, fire + new Vector3(F(2), F(3), F(4)), new Vector3(F(5), F(6), F(7)).normalized, false, false);
    }

    // --- Evenements du moteur -------------------------------------------------------------------------------------
    public void OnEvent(PbEvent e)
    {
        if (!Ready) return;
        var a = avs[e.seat < 0 ? 0 : e.seat];
        switch (e.type)
        {
            case PbEv.Hit:
                if (e.seat == me) { HitBy = e.by; downUntil = Time.time + 3.5f; Sound.I.Play("lose", 0.6f); }
                else { a.an.CrossFadeInFixedTime("RecieveHit", 0.1f); if (e.by == me) Sound.I.Play("bj_chips", 0.5f); }
                if (bots.TryGetValue(e.seat, out var bb)) bb.downUntil = Time.time + 3.5f;
                break;
            case PbEv.Spawn:
                foreach (var g in a.paint) if (g) Destroy(g);
                a.paint.Clear();
                var sp = SpawnPoint(e.seat);
                if (e.seat == me) { cc.enabled = false; cc.transform.position = sp; cc.enabled = true; yaw = Quaternion.LookRotation(fire - sp).eulerAngles.y; pitch = 0; HitBy = -1; }
                a.pos = a.from = a.to = sp; a.lerp = 1;
                if (bots.TryGetValue(e.seat, out var b2)) b2.path.Clear();
                break;
        }
    }

    // --- Autres joueurs : interpolation et animations ---------------------------------------------------------------
    void Animate(Av a, float dt)
    {
        bool down = pb.players[a.seat].down, mine = a.seat == me && !MeBot;
        if (!bots.ContainsKey(a.seat) && !mine)
        {
            a.lerp = Mathf.Min(1, a.lerp + dt / 0.05f);
            var target = Vector3.Lerp(a.from, a.to, a.lerp);
            a.vel = (target - a.pos) / Mathf.Max(dt, 1e-4f);
            a.pos = target;
        }
        a.t.position = a.pos;
        a.yaw = a.seat == me ? a.tYaw : Mathf.LerpAngle(a.yaw, a.tYaw, 1 - Mathf.Exp(-dt * 15));
        a.t.rotation = Quaternion.Euler(0, a.yaw, 0);
        var flat = new Vector3(a.vel.x, 0, a.vel.z);
        bool air = Mathf.Abs(a.vel.y) > 2.2f;
        string st = down ? "Defeat" : air ? "PistolJump" : flat.magnitude > 0.6f ? "PistolRun" : "Idle";
        if (st != a.anim)
        {
            a.anim = st; a.an.CrossFadeInFixedTime(st, 0.15f);
            if (a.an.layerCount > 1) a.an.SetLayerWeight(1, 0);   // les bras sont poses sur le lanceur par IK
        }
        // Lanceur devant le buste, dans l'axe de visee (comme le mien a l'ecran) ; les mains s'y posent.
        if (a.gun && a.seat != me)
        {
            a.gun.gameObject.SetActive(!down);
            var eye = new Pose(a.pos + Vector3.up * Mathf.Lerp(Eye, CrouchEye, a.crouch) + Quaternion.Euler(0, a.yaw, 0) * Vector3.forward * 0.12f, Quaternion.Euler(Mathf.Clamp(a.tPitch, -60, 60), a.yaw, 0));
            a.gun.SetPositionAndRotation(eye.position + eye.rotation * GunAt, eye.rotation);
            Aim(a, eye);
        }
        if (a.tag && Camera.main && a.seat != me) { a.tag.transform.rotation = Quaternion.LookRotation(a.tag.transform.position - Camera.main.transform.position); a.tag.gameObject.SetActive(!down); }
    }

    // --- Bots ---------------------------------------------------------------------------------------------------
    bool Sees(Vector3 from, Vector3 to) => !Physics.Linecast(from, to);

    void BotThink(int s, BotBrain b, float dt)
    {
        var a = avs[s];
        var pl = pb.players[s];
        if (pl.down)
        {
            a.vel = Vector3.zero;
            if (b.downUntil > 0 && Time.time > b.downUntil) { b.downUntil = 0; act?.Invoke("spawn|" + s); }
            return;
        }
        var eye = a.pos + Vector3.up * Eye;
        // Cible : l'ennemi visible le plus proche.
        b.think -= dt;
        if (b.think <= 0)
        {
            b.think = 0.25f;
            b.target = -1; float bd = 38;
            foreach (var o in avs)
            {
                if (o.team == a.team || pb.players[o.seat].down) continue;
                var op = o.seat == me && !MeBot ? cc.transform.position : o.pos;
                float d = Vector3.Distance(op, a.pos);
                if (d < bd && Sees(eye, op + Vector3.up * 1.2f)) { bd = d; b.target = o.seat; }
            }
            if (b.target < 0 && (b.path.Count == 0 || UnityEngine.Random.value < 0.02f))
            {
                // Personne en vue : vers un ennemi (ou un coin de la place) par les rues.
                var foes = avs.Where(o => o.team != a.team && !pb.players[o.seat].down).ToList();
                var goal = foes.Count > 0 && UnityEngine.Random.value < 0.7f ? (foes[UnityEngine.Random.Range(0, foes.Count)].pos)
                    : fire + new Vector3(UnityEngine.Random.Range(-25f, 25f), 0, UnityEngine.Random.Range(-25f, 25f));
                b.path = Path(a.pos, goal);
            }
            if (UnityEngine.Random.value < 0.15f) b.strafe = UnityEngine.Random.Range(-1f, 1f);
        }
        Vector3 move = Vector3.zero;
        if (b.target >= 0)
        {
            var o = avs[b.target];
            var op = (b.target == me && !MeBot ? cc.transform.position : o.pos) + Vector3.up * 1.15f;
            var to = op - eye;
            // Vise avec une erreur qui depend du bot, puis tire par rafales.
            var aim = Quaternion.LookRotation(to);
            a.tYaw = Mathf.MoveTowardsAngle(a.tYaw, aim.eulerAngles.y, 220 * dt);
            a.tPitch = -Mathf.Asin(to.normalized.y) * Mathf.Rad2Deg;
            b.fireCd -= dt;
            if (b.fireCd <= 0 && Mathf.Abs(Mathf.DeltaAngle(a.tYaw, aim.eulerAngles.y)) < 10)
            {
                b.fireCd = UnityEngine.Random.Range(0.22f, 0.45f);
                var err = UnityEngine.Random.insideUnitSphere * 0.035f * b.aimErr * (1 + to.magnitude / 25);
                var lead = b.target == me && !MeBot ? Vector3.zero : avs[b.target].vel * (to.magnitude / BallSpeed) * 0.6f;
                var d = (op + lead - eye).normalized + err;
                Shoot(s, eye + d.normalized * 0.6f, d.normalized, true);
            }
            move = Quaternion.Euler(0, a.tYaw, 0) * new Vector3(b.strafe, 0, to.magnitude > 14 ? 0.7f : to.magnitude < 6 ? -0.4f : 0);
        }
        else if (b.path.Count > 0)
        {
            var wp = b.path[0]; var d = wp - a.pos; d.y = 0;
            if (d.magnitude < 0.6f) b.path.RemoveAt(0);
            else { move = d.normalized; a.tYaw = Mathf.MoveTowardsAngle(a.tYaw, Quaternion.LookRotation(d).eulerAngles.y, 400 * dt); a.tPitch = 0; }
        }
        // Deplacement sur la grille (glisse le long des cases libres).
        var next = a.pos + move * Speed * 0.92f * dt;
        var (ni, nj) = CellOf(next);
        if (free[ni, nj] && (next - fire).magnitude < Radius) { next.y = Mathf.Lerp(a.pos.y, height[ni, nj], 0.3f); a.vel = (next - a.pos) / dt; a.pos = next; }
        else a.vel = Vector3.zero;
    }

    public static string Hex(int team) => "#" + ColorUtility.ToHtmlStringRGB(TeamColor[team]);
}

// Mains posees sur le lanceur (IK humanoide) et buste tourne vers le point vise : le perso tient vraiment son arme.
public class FpsHands : MonoBehaviour
{
    public Transform right, left;
    public Vector3 look;
    public float weight = 1, lookWeight = 1, crouch;
    Animator an;
    void Awake() { an = GetComponent<Animator>(); }
    void OnAnimatorIK(int layer)
    {
        if (layer != 0 || !an) return;
        if (crouch > 0.01f)
        {
            // Accroupi : le bassin descend, les pieds restent ou ils sont (genoux plies par l'IK).
            var lf = an.GetIKPosition(AvatarIKGoal.LeftFoot); var rf = an.GetIKPosition(AvatarIKGoal.RightFoot);
            an.bodyPosition -= Vector3.up * 0.42f * crouch + transform.forward * 0.08f * crouch;
            foreach (var (g, p) in new[] { (AvatarIKGoal.LeftFoot, lf), (AvatarIKGoal.RightFoot, rf) }) { an.SetIKPositionWeight(g, crouch); an.SetIKPosition(g, p); }
        }
        if (!right) return;
        an.SetLookAtWeight(weight * lookWeight, 0.55f, 0.7f, 0f, 0.6f);
        an.SetLookAtPosition(look);
        foreach (var (g, t) in new[] { (AvatarIKGoal.RightHand, right), (AvatarIKGoal.LeftHand, left) })
        {
            an.SetIKPositionWeight(g, weight); an.SetIKRotationWeight(g, weight * 0.8f);
            an.SetIKPosition(g, t.position); an.SetIKRotation(g, t.rotation);
        }
    }
}
