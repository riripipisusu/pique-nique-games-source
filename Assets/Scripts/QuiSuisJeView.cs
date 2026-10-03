using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Qui suis-je ? dans la clairiere : les joueurs assis sur des caisses autour de la nappe, un post-it jaune sur le front
// (le nom du personnage). Vue a la premiere personne depuis ma place : clic droit pour tourner la tete, molette pour
// zoomer, clic sur quelqu'un pour le regarder de pres. Mon propre avatar est cache (je suis dedans).
public class QuiSuisJeView : MonoBehaviour
{
    const float Radius = 2.7f, SeatBack = 0.35f;
    static readonly Vector3 Middle = new Vector3(0, 0, 0.5f);   // centre de la nappe

    class Seat { public Transform root, crate, postit, wolf; public Animator an, wan; public TextMesh text; public Renderer paper; public Texture skin; public GameObject prop; public string propId; public bool dead; public Transform tomb; public int point = -1, pointLast = -1; public float pointW; public bool propAlong; public Quaternion propQ; public string gesture; public float gestW; public HumanPoseHandler pose; public Dictionary<string, Transform> fingers; public int outline; public bool outlineWolf; }
    readonly List<Seat> seats = new List<Seat>();
    QuiSuisJe qs;
    int n, me, fitFrames, eyeFrames;
    bool live;   // vue construite (Qui suis-je, ou Limite Limite sans post-its)
    Transform myHead;
    Transform cast;
    Material lit, paperMat, foundMat;
    Font font;
    Vector3 eye, wolfEye;
    float wolfK;
    float yaw, pitch, fov = 60, tYaw, tPitch, tFov = 60;

    void Awake()
    {
        transform.position = Clairiere.Center;
        lit = Resources.Load<Material>("Lit");
        font = Resources.Load<Font>("Fonts/Fredoka");
        // Papier sans ombre (materiau de l'ecran du quiz) : lisible meme a contre-jour.
        var unlit = Resources.Load<Material>("QuizScreen");
        paperMat = new Material(unlit) { color = Board.Hex("ffe45c") }; paperMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
        foundMat = new Material(unlit) { color = Board.Hex("8ef08a") }; foundMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
        gameObject.SetActive(false);
    }

    Vector3 SeatPos(int i)
    {
        // Moi en bas ; les autres en arc en face de moi (entre 70 et 290 degres) : tout le monde tient dans le champ.
        int k = ((i - me) % n + n) % n;
        float a = (k == 0 ? 0 : n == 2 ? 180 : 70 + (k - 1) * 220f / (n - 2)) * Mathf.Deg2Rad;
        return Middle + new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a)) * Radius;
    }
    Quaternion Facing(int i) { var d = Middle - SeatPos(i); d.y = 0; return Quaternion.LookRotation(d); }

    public void Build(QuiSuisJe q, int mySeat, IList<string> avatars) { qs = q; BuildSeats(q.players.Count, mySeat, avatars); }
    // Meme table, sans post-its (Limite Limite).
    public void BuildPlain(int count, int mySeat, IList<string> avatars) { qs = null; BuildSeats(count, mySeat, avatars); }

    void BuildSeats(int count, int mySeat, IList<string> avatars)
    {
        Clairiere.Show(true);
        Clairiere.Camp(qs == null);   // Loup-garou : feu de camp au lieu de la nappe
        gameObject.SetActive(true);
        n = count; live = true;
        me = Mathf.Clamp(mySeat, 0, n - 1);
        if (cast) Destroy(cast.gameObject);
        cast = new GameObject("joueurs").transform;
        cast.SetParent(transform, false);
        seats.Clear();
        for (int i = 0; i < n; i++)
        {
            var s = new Seat();
            var pos = SeatPos(i); var face = Facing(i);
            s.crate = Model("SM_Prop_Camp_Crate_01", pos - face * new Vector3(0, 0, SeatBack), 0.6f, face.eulerAngles.y + 90);
            s.root = Chars.Spawn(avatars.Count > i ? avatars[i] : Chars.Default, cast, pos, face.eulerAngles.y, out s.an);
            s.an.Play("SitDown", 0, i * 0.19f);
            s.an.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // cache (loup-garou) : il garde sa pose assise
            // Post-it : carre jaune et nom, place chaque image sur le front.
            s.postit = new GameObject("post-it").transform;
            s.postit.SetParent(cast, false);
            var paper = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(paper.GetComponent<Collider>());
            paper.transform.SetParent(s.postit, false);
            paper.transform.localScale = new Vector3(0.3f, 0.22f, 1);
            s.paper = paper.GetComponent<Renderer>();
            s.paper.sharedMaterial = paperMat;
            s.paper.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.text = new GameObject("nom").AddComponent<TextMesh>();
            s.text.transform.SetParent(s.postit, false);
            s.text.transform.localPosition = new Vector3(0, 0, -0.003f);
            s.text.font = font; s.text.GetComponent<MeshRenderer>().sharedMaterial = RoueView.TextMat(font);
            s.text.fontSize = 96; s.text.anchor = TextAnchor.MiddleCenter; s.text.alignment = TextAlignment.Center;
            s.text.fontStyle = FontStyle.Bold; s.text.color = Board.Hex("2e1b10");
            if (i == me) myHead = s.an.GetBoneTransform(HumanBodyBones.Head);   // je vois mon corps, pas ma tete (ecrasee dans LateUpdate)
            if (i == me || qs == null) s.postit.gameObject.SetActive(false);
            seats.Add(s);
        }
        fitFrames = 3; eyeFrames = 5; eye = Vector3.zero;
        yaw = tYaw = 0; pitch = tPitch = 12; fov = tFov = 60;
        Sync();
    }

    public void Hide() { qs = null; live = false; gameObject.SetActive(false); Clairiere.Show(false); }

    Transform Model(string name, Vector3 pos, float scale, float rot)
    {
        var prefab = Synty.Get(name) ?? Resources.Load<GameObject>("Models/" + name);
        if (!prefab) return null;
        var g = Instantiate(prefab, cast);
        g.transform.localPosition = pos;
        g.transform.localRotation = Quaternion.Euler(0, rot, 0);
        g.transform.localScale = Vector3.one * scale;
        return g.transform;
    }

    static string Wrap(string s)
    {
        if (s.Length <= 11) return s;
        var words = s.Split(' ');
        var lines = new List<string>(); var cur = "";
        foreach (var w in words)
        {
            if (cur.Length > 0 && cur.Length + w.Length > 12) { lines.Add(cur); cur = w; }
            else cur = cur.Length > 0 ? cur + " " + w : w;
        }
        lines.Add(cur);
        return string.Join("\n", lines);
    }

    // Post-its : le personnage choisi ("?" en attendant), vert quand il est trouve.
    public void Sync()
    {
        if (qs == null) return;
        for (int i = 0; i < seats.Count; i++)
        {
            var s = seats[i];
            s.paper.sharedMaterial = qs.players[i].Found ? foundMat : paperMat;
            s.text.text = Wrap(qs.players[i].perso ?? "?");
            int lines = s.text.text.Split('\n').Length;
            s.text.characterSize = 0.0048f * (lines > 2 ? 0.7f : lines == 2 ? 0.85f : 1f);
        }
    }

    // Loup-garou : un mort disparait de sa caisse (une petite tombe a sa place).
    // Paupieres (blendshapes eyeBlink des tetes Sidekick) : 0 ouverts, 1 fermes.
    public void SetEyes(int seat, float closed)
    {
        if (seat < 0 || seat >= seats.Count || !seats[seat].root) return;
        foreach (var smr in seats[seat].root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var m = smr.sharedMesh;
            if (!m) continue;
            for (int i = 0; i < m.blendShapeCount; i++)
                if (m.GetBlendShapeName(i).Contains(".eyeBlink")) smr.SetBlendShapeWeight(i, closed * 100);
        }
    }

    // Loup-garou d'Agrou par-dessus le perso (meme pose assise, hanches calees sur les siennes) ; skin = palette du role, null = humain.
    public void SetWolf(int seat, Texture skin)
    {
        if (seat < 0 || seat >= seats.Count) return;
        var st = seats[seat];
        if (st.skin == skin) return;
        st.skin = skin;
        if (skin && !st.wolf)
        {
            var prefab = Resources.Load<GameObject>("LoupGarou/Models/werewolf");
            if (!prefab) return;
            st.wolf = Instantiate(prefab, cast).transform;
            st.wolf.SetPositionAndRotation(st.root.position, st.root.rotation);
            st.wolf.localScale = Vector3.one * 1.1f;
            st.wan = st.wolf.GetComponent<Animator>() ?? st.wolf.gameObject.AddComponent<Animator>();
            st.wan.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("CharAnim");
            st.wan.applyRootMotion = false;
            st.wan.Play("SitDown", 0, seat * 0.19f);
        }
        if (st.wolf)
        {
            foreach (var r in st.wolf.GetComponentsInChildren<Renderer>()) { r.enabled = skin; if (skin) r.material = new Material(lit) { mainTexture = skin, color = Color.white }; }
        }
        foreach (var r in st.root.GetComponentsInChildren<Renderer>()) r.enabled = !skin;
    }

    // Accessoire de role (Resources/Agrou/Props) tenu par un os : redimensionne a `size` (plus grande dimension), axe le plus
    // fin face au joueur, axe le plus long vers `up` ou `right` du perso ; `off` en metres dans le repere du perso.
    public void SetProp(int seat, string model, HumanBodyBones bone = HumanBodyBones.RightHand, Vector3 off = default, float size = 0.3f, bool longUp = true, bool alongArm = false)
    {
        if (seat < 0 || seat >= seats.Count) return;
        var st = seats[seat];
        if (st.propId == model) return;
        st.propId = model;
        if (st.prop) Destroy(st.prop);
        if (model == null || st.skin) return;
        var prefab = Resources.Load<GameObject>("Agrou/Props/" + model);
        var b = st.an.GetBoneTransform(bone);
        if (!prefab || !b) return;
        var g = Instantiate(prefab);
        var rs = g.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) { Destroy(g); return; }
        // Boite dans le repere du modele (avant toute rotation).
        var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds);
        var sz = bb.size;
        int lo = sz.x <= sz.y && sz.x <= sz.z ? 0 : sz.y <= sz.z ? 1 : 2, hi = sz.x >= sz.y && sz.x >= sz.z ? 0 : sz.y >= sz.z ? 1 : 2;
        if (lo == hi) hi = (lo + 1) % 3;
        Vector3 Ax(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
        var root = st.root;
        var q = Quaternion.LookRotation(root.forward, longUp ? root.up : root.right) * Quaternion.Inverse(Quaternion.LookRotation(Ax(lo), Ax(hi)));
        var pivot = new GameObject(model).transform;
        g.transform.SetParent(pivot, false);
        g.transform.localPosition = -bb.center;
        pivot.SetPositionAndRotation(b.position + root.rotation * off, q);
        pivot.localScale = Vector3.one * (size / Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z)));
        pivot.SetParent(b, true);
        foreach (var r in rs) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        st.prop = pivot.gameObject;
        // Arme epaulee (fusil) : l'axe long suit l'avant-bras a chaque image (voir LateUpdate), la crosse vers le haut.
        st.propAlong = alongArm;
        int mid = 3 - lo - hi;
        st.propQ = Quaternion.Inverse(Quaternion.LookRotation(Ax(hi), Ax(mid)));
    }

    // Mort : le perso s'effondre (ragdoll), disparait, et sa caisse laisse place a la pierre tombale d'Agrou.
    public void SetAlive(int seat, bool alive)
    {
        if (seat < 0 || seat >= seats.Count) return;
        var st = seats[seat];
        if (alive || st.dead) return;
        st.dead = true;
        SetWolf(seat, null); SetProp(seat, null);
        StartCoroutine(Die(st, seat == me));
    }

    static readonly (HumanBodyBones bone, HumanBodyBones parent, HumanBodyBones end, float r, float mass)[] Rag =
    {
        (HumanBodyBones.Hips, HumanBodyBones.LastBone, HumanBodyBones.Spine, 0.14f, 3),
        (HumanBodyBones.Spine, HumanBodyBones.Hips, HumanBodyBones.Neck, 0.13f, 3),
        (HumanBodyBones.Head, HumanBodyBones.Spine, HumanBodyBones.LastBone, 0.11f, 1),
        (HumanBodyBones.LeftUpperLeg, HumanBodyBones.Hips, HumanBodyBones.LeftLowerLeg, 0.07f, 1.5f),
        (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftFoot, 0.055f, 1),
        (HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips, HumanBodyBones.RightLowerLeg, 0.07f, 1.5f),
        (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightFoot, 0.055f, 1),
        (HumanBodyBones.LeftUpperArm, HumanBodyBones.Spine, HumanBodyBones.LeftLowerArm, 0.05f, 1),
        (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftHand, 0.04f, 0.7f),
        (HumanBodyBones.RightUpperArm, HumanBodyBones.Spine, HumanBodyBones.RightLowerArm, 0.05f, 1),
        (HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightHand, 0.04f, 0.7f),
    };

    IEnumerator Die(Seat st, bool mine)
    {
        if (!floor)   // sol physique sous le cercle
        {
            floor = new GameObject("sol").AddComponent<BoxCollider>();
            floor.transform.SetParent(transform, false);
            floor.center = new Vector3(0, -0.05f, 0.5f); floor.size = new Vector3(30, 0.1f, 30);
        }
        if (st.crate) st.crate.gameObject.SetActive(false);
        Tomb(st);   // la tombe surgit a l'instant ou le corps s'effondre
        Physics.simulationMode = SimulationMode.FixedUpdate;   // le de des petits chevaux la met en manuel
        if (st.an && st.root)
        {
            Ragdoll(st, (st.root.right * 1.1f - st.root.forward * 0.2f + Vector3.up * 0.2f) * 60);   // s'effondre sur le cote de sa tombe
            yield return new WaitForSeconds(3.5f);
        }
        if (st.root) st.root.gameObject.SetActive(false);   // moi aussi : je reste un oeil au-dessus de ma tombe
    }
    BoxCollider floor;

    // ---- Pendaison d'Agrou (Blueprint Pendaison, Resources/Agrou/Props/Pendaison) -----------------------------------
    // Le feu disparait, la potence prend sa place tournee vers `faceTo` ; chaque condamne monte sur sa trappe, corde au cou ;
    // la camera du Blueprint (devant, grand angle) filme. Drop() ouvre les trappes ; EndGallows() remet tout en place.
    Transform gallows;
    readonly List<(Seat st, LineRenderer rope, Transform top)> hanged = new List<(Seat, LineRenderer, Transform)>();
    static Transform Deep(Transform t, string name) => t.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
    readonly Dictionary<Seat, (Vector3 pos, Quaternion rot)> gallowsFrom = new Dictionary<Seat, (Vector3, Quaternion)>();
    Vector3 gallowsCamPos, gallowsCamLook;
    public bool OnGallows(int seat) => seat >= 0 && seat < seats.Count && hanged.Any(h => h.st == seats[seat]);
    public void GallowsCamera() { if (gallows) CinematicFixed(gallowsCamPos, gallowsCamLook, 77); }

    // Egalite : celui que le village epargne redescend de la potence et retourne s'asseoir.
    public void Release(int seat)
    {
        if (!OnGallows(seat)) return;
        var h = hanged.First(x => x.st == seats[seat]);
        hanged.Remove(h);
        if (h.rope) Destroy(h.rope.gameObject);
        if (gallowsFrom.TryGetValue(h.st, out var from)) { h.st.root.SetPositionAndRotation(from.pos, from.rot); gallowsFrom.Remove(h.st); }
        if (h.st.an) h.st.an.Play("SitDown", 0, 0);
    }

    // Joueur parti : il disparait, sa caisse aussi (pas de tombe : il n'est pas mort).
    public void Leave(int seat)
    {
        if (seat < 0 || seat >= seats.Count) return;
        var st = seats[seat];
        Release(seat);
        st.dead = true;
        SetWolf(seat, null); SetProp(seat, null);
        if (st.root) st.root.gameObject.SetActive(false);
        if (st.crate) st.crate.gameObject.SetActive(false);
    }

    public bool Gallows(List<int> condemned, Vector3 faceTo, bool cine = true)
    {
        var prefab = Resources.Load<GameObject>("Agrou/Props/Pendaison");
        if (!prefab) return false;
        EndGallows();
        gallows = Instantiate(prefab, transform).transform;
        gallows.name = "potence";
        gallows.localScale *= 1.35f;   // meme agrandissement que les maps (la racine du FBX porte deja l echelle des cm)
        var center = transform.TransformPoint(Middle);
        var sol = Deep(gallows, "SOL"); var cam = Deep(gallows, "CAMERA");
        // L'avant de la potence (cote camera) vers faceTo.
        var front = cam.position - gallows.position; front.y = 0;
        // Sur le cote du cercle (a ma gauche), face au feu : elle ne cache ni le feu ni personne.
        var spotPos = center + transform.TransformDirection(Facing(me) * Vector3.left) * (Radius + 4.2f);
        var want = center - spotPos; want.y = 0;
        if (want.sqrMagnitude > 0.01f && front.sqrMagnitude > 0.01f) gallows.rotation = Quaternion.FromToRotation(front.normalized, want.normalized) * gallows.rotation;
        gallows.position += spotPos - sol.position;
        foreach (var r in gallows.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        for (int k = 0; k < condemned.Count && k < 2; k++)
        {
            var st = seats[condemned[k]];
            var trap = Deep(gallows, k == 0 ? "TRAPPE_1" : "TRAPPE_2");
            var spot = Deep(gallows, k == 0 ? "SPHERE_1" : "SPHERE_2");
            var top = Deep(gallows, k == 0 ? "CABLE_1" : "CABLE_2");
            float floorY = trap.GetComponentsInChildren<Renderer>().Select(r => r.bounds.max.y).DefaultIfEmpty(center.y).Max();
            gallowsFrom[st] = (st.root.position, st.root.rotation);
            SetWolf(condemned[k], null); SetProp(condemned[k], null); Gesture(condemned[k], null); Point(condemned[k], -1);
            st.root.SetPositionAndRotation(new Vector3(spot.position.x, floorY, spot.position.z), Quaternion.LookRotation(want.sqrMagnitude > 0.01f ? want.normalized : Vector3.forward));
            if (st.an) st.an.Play("Idle", 0, 0);

            var rope = new GameObject("corde").AddComponent<LineRenderer>();
            rope.transform.SetParent(gallows, false);
            rope.widthMultiplier = 0.035f; rope.positionCount = 2; rope.useWorldSpace = true;
            rope.sharedMaterial = new Material(lit) { color = Board.Hex("8a6a3e") };
            hanged.Add((st, rope, top));
        }
        gallowsCamPos = cam.position; gallowsCamLook = gallows.position + Vector3.up * 2.3f;
        if (cine) GallowsCamera();
        return true;
    }

    // Les trappes s'ouvrent : chaque pendu tombe, retenu par la corde a la tete ; sa tombe surgit a sa place au meme instant.
    public void Drop()
    {
        if (!gallows) return;
        foreach (var n in new[] { "TRAPPE_1", "TRAPPE_2" }) { var t = Deep(gallows, n); if (t) t.gameObject.SetActive(false); }
        foreach (var (st, _, _) in hanged)
        {
            st.dead = true;
            if (st.crate) st.crate.gameObject.SetActive(false);
            Tomb(st);
            var bodies = Ragdoll(st, Vector3.zero);
            if (bodies.TryGetValue(HumanBodyBones.Head, out var head))
            {
                var anchor = new GameObject("noeud").AddComponent<Rigidbody>();
                anchor.isKinematic = true;
                anchor.transform.SetParent(gallows, true);
                anchor.transform.position = head.position;
                var j = head.gameObject.AddComponent<ConfigurableJoint>();
                j.connectedBody = anchor; j.autoConfigureConnectedAnchor = false; j.anchor = Vector3.zero; j.connectedAnchor = Vector3.zero;
                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
                j.linearLimit = new SoftJointLimit { limit = 0.45f };   // il tombe d'une demi-corde puis reste pendu
            }
        }
    }

    public void EndGallows()
    {
        gallowsFrom.Clear();
        foreach (var (st, rope, _) in hanged) { if (st.root) st.root.gameObject.SetActive(false); if (rope) Destroy(rope.gameObject); }
        hanged.Clear();
        if (gallows) Destroy(gallows.gameObject);
        gallows = null;
        Clairiere.HideFire(false);
    }

    // Corps en ragdoll (os du perso humanoide) ; renvoie les corps par os.
    Dictionary<HumanBodyBones, Rigidbody> Ragdoll(Seat st, Vector3 push)
    {
        Physics.simulationMode = SimulationMode.FixedUpdate;   // le de des petits chevaux la met en manuel
        if (!floor)
        {
            floor = new GameObject("sol").AddComponent<BoxCollider>();
            floor.transform.SetParent(transform, false);
            floor.center = new Vector3(0, -0.05f, 0.5f); floor.size = new Vector3(30, 0.1f, 30);
        }
        var bodies = new Dictionary<HumanBodyBones, Rigidbody>();
        if (!st.an) return bodies;
        st.an.enabled = false;
        var cols = new List<Collider>();
        foreach (var (bone, parent, end, r, mass) in Rag)
        {
            var t = st.an.GetBoneTransform(bone);
            if (!t) continue;
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass; rb.interpolation = RigidbodyInterpolation.Interpolate;
            float k = 1 / Mathf.Max(0.001f, t.lossyScale.x);
            var e = end == HumanBodyBones.LastBone ? null : st.an.GetBoneTransform(end);
            if (e)
            {
                var c = t.gameObject.AddComponent<CapsuleCollider>();
                var d = t.InverseTransformPoint(e.position);
                c.direction = Mathf.Abs(d.x) > Mathf.Abs(d.y) && Mathf.Abs(d.x) > Mathf.Abs(d.z) ? 0 : Mathf.Abs(d.y) > Mathf.Abs(d.z) ? 1 : 2;
                c.center = d / 2; c.height = d.magnitude; c.radius = r * k;
                cols.Add(c);
            }
            else { var c = t.gameObject.AddComponent<SphereCollider>(); c.radius = r * k; c.center = Vector3.up * r * k * 0.8f; cols.Add(c); }
            if (parent != HumanBodyBones.LastBone && bodies.TryGetValue(parent, out var pb))
            {
                var j = t.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = pb; j.enableProjection = true;
            }
            bodies[bone] = rb;
        }
        foreach (var a in cols) foreach (var b in cols) if (a != b) Physics.IgnoreCollision(a, b);
        if (push != Vector3.zero && bodies.TryGetValue(HumanBodyBones.Spine, out var sp)) sp.AddForce(push, ForceMode.Impulse);
        return bodies;
    }

    void Tomb(Seat st)
    {
        var pos = st.crate ? st.crate.localPosition : Vector3.zero;
        pos -= (Middle - pos).normalized * 0.35f;   // un peu en retrait : le corps tombe a cote, pas dedans
        var prefab = Resources.Load<GameObject>("Agrou/Props/SM_Prop_Gravestone_02");   // pierre tombale d'Agrou
        var g = prefab ? Instantiate(prefab, cast) : GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = "tombe";
        g.transform.SetParent(cast, false);
        g.transform.localPosition = pos;
        var toFire = Middle - pos; toFire.y = 0;
        g.transform.localRotation = Quaternion.LookRotation(toFire) * (prefab ? prefab.transform.localRotation : Quaternion.identity);
        var rs = g.GetComponentsInChildren<Renderer>();
        foreach (var rr in rs) foreach (var m in rr.materials) m.color = new Color(0.62f, 0.4f, 0.27f);   // pierre brune, comme dans Agrou
        if (rs.Length > 0)
        {
            var bb = rs[0].bounds; foreach (var rr in rs) bb.Encapsulate(rr.bounds);
            g.transform.localScale *= 1.15f / Mathf.Max(0.01f, bb.size.y);
            bb = rs[0].bounds; foreach (var rr in rs) bb.Encapsulate(rr.bounds);
            g.transform.position += Vector3.up * (transform.position.y - bb.min.y);
        }
        st.tomb = g.transform;
    }
    public bool IsMe(int seat) => seat == me;
    public string SitDiag() => string.Join(" | ", seats.Select((st, i) => { var h = st.an ? st.an.GetBoneTransform(HumanBodyBones.Hips) : null; var b = st.crate ? st.crate.GetComponentInChildren<Renderer>() : null; return h && b && st.crate.gameObject.activeSelf ? $"{i}:{h.position.y - b.bounds.max.y:F2}/{b.bounds.size.y:F2}{(st.skin ? "L" : "")}{(st.point != -1 ? "P" : "")}" : $"{i}:-"; }));
    public Vector3 SeatWorld(int seat) => transform.TransformPoint(SeatPos(Mathf.Clamp(seat, 0, Mathf.Max(0, n - 1))));

    // Geste d'Agrou (couche "haut du corps" de CharAnim, etat wg_<clip>) ; null = juste assis.
    public void Gesture(int seat, string clip)
    {
        if (seat < 0 || seat >= seats.Count) return;
        var st = seats[seat];
        if (st.gesture == clip || st.dead) return;
        st.gesture = clip;
        if (clip == null) return;
        foreach (var an in new[] { st.an, st.wan })
            if (an && an.layerCount > 1 && an.HasState(1, Animator.StringToHash("wg_" + clip))) an.CrossFadeInFixedTime("wg_" + clip, 0.35f, 1);
    }

    // Contour d'un perso (et de son loup) : 0 = aucun, 1 = survole, 2 = choisi.
    static Material[] outlineMats;
    public void Outline(int seat, int level)
    {
        if (seat < 0 || seat >= seats.Count) return;
        var st = seats[seat];
        if (st.dead) level = 0;
        bool w = st.wolf;
        if (st.outline == level && st.outlineWolf == w) return;
        st.outline = level; st.outlineWolf = w;
        if (outlineMats == null)
        {
            var sh = Resources.Load<Shader>("WgOutline");
            outlineMats = new[] { null, new Material(sh) { color = new Color(1, 1, 1, 1) }, new Material(sh) { color = new Color(1f, 0.8f, 0.2f, 1) } };
            outlineMats[2].SetFloat("_Width", 0.008f);
        }
        foreach (var t in new[] { st.root, st.wolf })
            if (t) foreach (var r in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var ms = r.sharedMaterials.Where(m => m != outlineMats[1] && m != outlineMats[2]).ToList();
                if (level > 0) ms.Add(outlineMats[level]);
                r.sharedMaterials = ms.ToArray();
            }
    }

    // Doigt pointe (vote) : -1 = rien, -2 = droit devant (le chasseur epaule), sinon un siege.
    public void Point(int seat, int target) { if (seat >= 0 && seat < seats.Count) seats[seat].point = target; }

    void ApplyPoint(Seat st, int idx)
    {
        float goal = st.point == -1 ? 0 : 1;
        if (st.point != -1) st.pointLast = st.point;
        st.pointW = Mathf.MoveTowards(st.pointW, goal, Time.deltaTime * 3);
        if (st.pointW <= 0.001f || st.pointLast == -1) return;
        var an = st.skin && st.wan ? st.wan : st.an;
        if (!an || !an.enabled) return;
        var ua = an.GetBoneTransform(HumanBodyBones.RightUpperArm); var la = an.GetBoneTransform(HumanBodyBones.RightLowerArm); var h = an.GetBoneTransform(HumanBodyBones.RightHand);
        if (!ua || !la || !h) return;
        Vector3 aim = st.pointLast == -2 ? transform.TransformPoint(Middle) + Vector3.up * 1.0f
            : st.pointLast < seats.Count ? HeadOf(st.pointLast) - Vector3.up * 0.45f : ua.position + st.root.forward;
        float w = Mathf.SmoothStep(0, 1, st.pointW);
        if (an == st.an) Fist(st, h, w);
        // Le buste pivote vers la cible (reparti sur la colonne), puis le bras vise.
        var flat = aim - st.root.position; flat.y = 0;
        float yaw = Mathf.Clamp(Vector3.SignedAngle(st.root.forward, flat, Vector3.up), -80, 80) * w;
        foreach (var (bb, f) in new[] { (HumanBodyBones.Spine, 0.35f), (HumanBodyBones.Chest, 0.35f), (HumanBodyBones.UpperChest, 0.3f) })
        {
            var t = an.GetBoneTransform(bb);
            if (t) t.rotation = Quaternion.AngleAxis(yaw * f, Vector3.up) * t.rotation;
        }
        var dir = (aim - ua.position).normalized;
        ua.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(la.position - ua.position, dir), w) * ua.rotation;
        la.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(h.position - la.position, dir), w) * la.rotation;
    }

    // Poing ferme, index tendu (os des doigts Sidekick : index/middle/ring/pinky/thumb_0x_r).
    // Poing ferme, index tendu : os des doigts Sidekick (index/middle/ring/pinky/thumb_0x_r). Axe de flexion de chaque os
    // calcule une fois sur la pose de reference du squelette (T-pose, paume vers le bas) : le pli va forcement cote paume.
    static Dictionary<string, (Vector3 axis, Quaternion bind)> curl;
    static void BuildCurl()
    {
        curl = new Dictionary<string, (Vector3, Quaternion)>();
        var sk = Resources.Load<GameObject>("Sidekick/Skeleton");
        if (!sk) return;
        foreach (var b in sk.GetComponentsInChildren<Transform>(true))
        {
            if (!b.name.EndsWith("_r") || b.childCount == 0 || !System.Text.RegularExpressions.Regex.IsMatch(b.name, "^(index|middle|ring|pinky|thumb)_0[1-3]_r$")) continue;
            var dir = (b.GetChild(0).position - b.position).normalized;
            var axis = Vector3.Cross(dir, Vector3.down);
            if (axis.sqrMagnitude < 1e-6f) continue;
            axis.Normalize();
            if ((Quaternion.AngleAxis(30, axis) * dir).y > dir.y) axis = -axis;   // doit plier vers le bas (la paume)
            curl[b.name] = (b.InverseTransformDirection(axis), b.localRotation);
        }
    }

    void Fist(Seat st, Transform hand, float w)
    {
        if (curl == null) BuildCurl();
        st.fingers ??= hand.GetComponentsInChildren<Transform>().GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        foreach (var kv in curl)
        {
            if (!st.fingers.TryGetValue(kv.Key, out var t)) continue;
            if (kv.Key.StartsWith("index")) { t.localRotation = Quaternion.Slerp(t.localRotation, kv.Value.bind, w); continue; }   // index tendu
            float a = kv.Key.StartsWith("thumb") ? 35 : kv.Key.EndsWith("01_r") ? 85 : 95;
            t.localRotation = t.localRotation * Quaternion.AngleAxis(a * w, kv.Value.axis);
        }
    }

    public void Anim(int seat, string state) { if (seat >= 0 && seat < seats.Count && seats[seat].an) seats[seat].an.CrossFadeInFixedTime(state, 0.4f); }
    public void AllBut(int seat, string state) { for (int i = 0; i < seats.Count; i++) if (i != seat) Anim(i, state); }

    Transform Head(int i) => seats[i].an ? seats[i].an.GetBoneTransform(HumanBodyBones.Head) : null;
    public Vector3 HeadOf(int i) { if (i >= 0 && i < seats.Count && seats[i].dead && seats[i].tomb) return seats[i].tomb.position + Vector3.up * 1.55f;   // nom au-dessus de la tombe
 var h = Head(i); return (h ? h.position : seats[i].root.position + Vector3.up * 1.3f) + Vector3.up * 0.32f; }

    // Chacun se pose sur sa caisse, mesure faite sur la pose assise seule (sans geste d'Agrou en cours). Toutes les caisses
    // ont la meme hauteur, fixee ici une fois pour toutes : assez haute pour que les pieds touchent le sol (moyenne des persos).
    void FitSeats()
    {
        var drops = new List<float>();
        foreach (var st in seats)
        {
            var hips = st.an.GetBoneTransform(HumanBodyBones.Hips);
            var box = st.crate ? st.crate.GetComponentInChildren<Renderer>() : null;
            if (!hips || !box) continue;
            if (st.an.layerCount > 1) { st.an.SetLayerWeight(1, 0); st.an.Update(0); }
            var b = box.bounds;
            st.root.position += new Vector3(b.center.x - hips.position.x, b.max.y + 0.1f - hips.position.y, b.center.z - hips.position.z);
            float foot = float.MaxValue;
            foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
            { var t = st.an.GetBoneTransform(bone); if (t) foot = Mathf.Min(foot, t.position.y); }
            if (foot < float.MaxValue) drops.Add(transform.position.y + 0.07f - foot);   // l'os est dans la chaussure, ~7 cm au-dessus de la semelle
        }
        float drop = drops.Count == 0 ? 0 : Mathf.Max(0, drops.Average());
        if (drop <= 0) return;
        foreach (var st in seats)
        {
            var box = st.crate ? st.crate.GetComponentInChildren<Renderer>() : null;
            if (!box) continue;
            float h = box.bounds.size.y;
            st.root.position += Vector3.up * drop;
            var c = st.crate; c.localScale = new Vector3(c.localScale.x, c.localScale.y * (h + drop) / h, c.localScale.z);
        }
    }

    void LateUpdate()
    {
        if (!live) return;
        // Une fois la pose assise calculee, chacun se pose sur sa caisse (comme les amis de l'accueil).
        if (fitFrames > 0 && --fitFrames == 0) FitSeats();
        for (int i = 0; i < seats.Count; i++)
        {
            var h = Head(i);
            if (!h) continue;
            var fwd = seats[i].root.forward;
            // Mon oeil : pris une fois, apres m'etre assis (suivre la tete animee fait tanguer la camera).
            if (i == me) { if (eyeFrames > 0 && --eyeFrames == 0) eye = h.position + fwd * 0.12f + Vector3.up * 0.06f; continue; }
            seats[i].postit.position = h.position + fwd * 0.24f + Vector3.up * 0.22f;   // devant le front, en avant des meches (sinon certaines coupes le cachent)
            seats[i].postit.rotation = Quaternion.LookRotation(-fwd);
        }
        // Ma tete (et mes cheveux) ne bouchent pas la vue... sauf quand la camera vient me filmer (ma mort, ma pendaison).
        if (myHead) myHead.localScale = Vector3.one * (Cine > 0.05f && (cineSeat == me || cineFixed) || me < seats.Count && seats[me].dead ? 1 : 0.001f);
        bool wolfMe = me < seats.Count && seats[me].wolf && seats[me].skin;
        wolfK = Mathf.MoveTowards(wolfK, wolfMe ? 1 : 0, Time.deltaTime * 2);
        if (wolfMe)
        {
            var wh = seats[me].wan.GetBoneTransform(HumanBodyBones.Head);
            var want = wh ? wh.position + seats[me].root.forward * 0.15f + Vector3.up * 0.05f : eye;
            wolfEye = wolfEye == Vector3.zero ? want : Vector3.Lerp(wolfEye, want, 1 - Mathf.Exp(-Time.deltaTime * 2));   // amortit le balancement de l'animation
        }
        if (me < seats.Count && seats[me].wan)   // pareil pour mon loup-garou (ses animations remettent l'echelle de la tete)
            foreach (var b in new[] { HumanBodyBones.Head, HumanBodyBones.Neck })
            { var t = seats[me].wan.GetBoneTransform(b); if (t) t.localScale = myHead ? myHead.localScale : Vector3.one * 0.001f; }
        foreach (var (hs, rope, top) in hanged)
        {
            var h = hs.an ? hs.an.GetBoneTransform(HumanBodyBones.Head) : null;
            if (rope && h && top) { rope.SetPosition(0, top.position); rope.SetPosition(1, h.position); }
        }
        for (int i = 0; i < seats.Count; i++)
        {
            var st = seats[i];
            if (st.dead) continue;
            st.gestW = Mathf.MoveTowards(st.gestW, st.gesture != null ? 1 : 0, Time.deltaTime * 3);
            foreach (var an in new[] { st.an, st.wan }) if (an && an.layerCount > 1) an.SetLayerWeight(1, st.gestW);
            ApplyPoint(st, i);
            if (st.prop && st.propAlong)
            {
                var la = st.an.GetBoneTransform(HumanBodyBones.RightLowerArm); var h = st.an.GetBoneTransform(HumanBodyBones.RightHand);
                var d = (h.position - la.position).normalized;
                st.prop.transform.rotation = Quaternion.LookRotation(d, st.root.up) * st.propQ;
                st.prop.transform.position = h.position + d * 0.25f;
            }
        }
        foreach (var st in seats)   // le loup reste assis la ou le perso est assis
            if (st.wolf && st.skin)
            {
                // Hanches au-dessus de celles du perso (horizontalement), pattes posees au sol : le loup ne flotte pas.
                var a = st.an.GetBoneTransform(HumanBodyBones.Hips); var b = st.wan.GetBoneTransform(HumanBodyBones.Hips);
                var lf = st.wan.GetBoneTransform(HumanBodyBones.LeftToes) ?? st.wan.GetBoneTransform(HumanBodyBones.LeftFoot);
                var rf = st.wan.GetBoneTransform(HumanBodyBones.RightToes) ?? st.wan.GetBoneTransform(HumanBodyBones.RightFoot);
                if (a && b) st.wolf.position += new Vector3(a.position.x - b.position.x, 0, a.position.z - b.position.z);
                if (lf && rf) st.wolf.position += Vector3.up * (transform.position.y + 0.04f - Mathf.Min(lf.position.y, rf.position.y));
            }   // ma tete (et mes cheveux) ne bouchent pas la vue
        float k = 1 - Mathf.Exp(-Time.deltaTime * 8);
        yaw = Mathf.LerpAngle(yaw, tYaw, k); pitch = Mathf.Lerp(pitch, tPitch, k); fov = Mathf.Lerp(fov, tFov, k);
        cineW = Mathf.MoveTowards(cineW, cineGoal, Time.deltaTime * 3f);   // Agrou : SetViewTargetWithBlend 0,2 s
        if (cineSeat >= 0 && cineSeat < seats.Count && seats[cineSeat].an)   // le regard suit le corps qui tombe
        {
            var hips = seats[cineSeat].an.GetBoneTransform(HumanBodyBones.Hips);
            var want = seats[cineSeat].dead && hips ? Vector3.Lerp(cineHead, hips.position, 0.55f) : cineHead - Vector3.up * 0.12f;
            cineLook = Vector3.Lerp(cineLook, want, 1 - Mathf.Exp(-Time.deltaTime * 6));
        }
    }

    // --- Camera a la premiere personne --------------------------------------------------
    public float Fov => Mathf.Lerp(fov, cineFixed ? cineFov : 38, Cine);
    public Pose CamPose
    {
        get
        {
            var basis = Facing(me);
            var pos = eye == Vector3.zero ? transform.TransformPoint(SeatPos(me) + Vector3.up * 1.25f) : eye;
            if (wolfK > 0) pos = Vector3.Lerp(pos, wolfEye, Mathf.SmoothStep(0, 1, wolfK));   // transforme : je vois par les yeux du loup (plus grand, penche en avant)
            var p = new Pose(pos, basis * Quaternion.Euler(pitch, yaw, 0));
            if (Cine > 0 && cineFixed)
            {
                var f = new Pose(cineFixedPos, Quaternion.LookRotation(cineLook - cineFixedPos));
                return new Pose(Vector3.Lerp(p.position, f.position, Cine), Quaternion.Slerp(p.rotation, f.rotation, Cine));
            }
            if (Cine <= 0 || cineSeat < 0 || cineSeat >= seats.Count) return p;
            // Plan de cinema : la camera quitte ma place et vient se poser face au joueur, a hauteur de visage.
            var head = cineHead;
            var front = transform.TransformDirection(Facing(cineSeat) * Vector3.forward);
            var cp = head + front * 2.6f + Vector3.up * 0.05f;   // camera "capturephoto" d'Agrou : 2,8 m devant le perso, a hauteur du visage
            var c = new Pose(cp, Quaternion.LookRotation(cineLook - cp));
            float t = Cine;
            return new Pose(Vector3.Lerp(p.position, c.position, t), Quaternion.Slerp(p.rotation, c.rotation, t));
        }
    }
    int cineSeat = -1; float cineW, cineGoal; Vector3 cineHead, cineLook;
    float Cine => Mathf.SmoothStep(0, 1, cineW);
    // Mise en scene d'une mort : la camera va face au joueur (seat), puis revient (EndCinematic).
    public void Cinematic(int seat)
    {
        if (seat < 0 || seat >= seats.Count) { EndCinematic(); return; }
        cineFixed = false; cineSeat = seat; cineGoal = 1; cineHead = HeadOf(seat) - Vector3.up * 0.32f; cineLook = cineHead - Vector3.up * 0.12f;
    }
    public void EndCinematic() { cineGoal = 0; cineFixed = false; }
    bool cineFixed; Vector3 cineFixedPos; float cineFov = 38;
    public void CinematicFixed(Vector3 pos, Vector3 look, float vfov)
    {
        cineFixed = true; cineFixedPos = pos; cineLook = look; cineFov = vfov; cineGoal = 1; cineSeat = -2;
    }
    public void Look(float dx, float dy)
    {
        // La souris tourne la tete directement (pas de lissage : sinon ca tire en arriere).
        yaw = tYaw = Mathf.Clamp(tYaw + dx, -130, 130); pitch = tPitch = Mathf.Clamp(tPitch - dy, -35, 78);
    }
    public void Zoom(float d) { tFov = Mathf.Clamp(tFov - d * 12, 12, 60); }

    // Clic : la tete la plus proche du rayon (a l'ecran) ; on se tourne vers elle et on zoome pour lire le post-it.
    public int HeadUnder(Ray r)
    {
        int best = -1; float bestA = 6;
        for (int i = 0; i < seats.Count; i++)
        {
            if (i == me) continue;
            float a = Vector3.Angle(r.direction, HeadOf(i) - Vector3.up * 0.3f - r.origin);
            if (a < bestA) { bestA = a; best = i; }
        }
        return best;
    }
    public void LookAt(int seat, bool zoom = true)
    {
        if (seat < 0 || seat == me || seat >= seats.Count) { tFov = 60; return; }
        var dir = Quaternion.Inverse(Facing(me)) * (HeadOf(seat) - Vector3.up * 0.25f - CamPose.position);
        var e = Quaternion.LookRotation(dir).eulerAngles;
        tYaw = Mathf.DeltaAngle(0, e.y); tPitch = Mathf.DeltaAngle(0, e.x);
        if (zoom) tFov = Mathf.Clamp(Vector3.Distance(CamPose.position, HeadOf(seat)) * 4.2f, 14, 30);
    }
    public void ResetView() { tYaw = 0; tPitch = 12; tFov = 60; }
}
