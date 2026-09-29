using System.Collections.Generic;
using UnityEngine;

// Qui suis-je ? dans la clairiere : les joueurs assis sur des caisses autour de la nappe, un post-it jaune sur le front
// (le nom du personnage). Vue a la premiere personne depuis ma place : clic droit pour tourner la tete, molette pour
// zoomer, clic sur quelqu'un pour le regarder de pres. Mon propre avatar est cache (je suis dedans).
public class QuiSuisJeView : MonoBehaviour
{
    const float Radius = 2.7f, SeatBack = 0.35f;
    static readonly Vector3 Middle = new Vector3(0, 0, 0.5f);   // centre de la nappe

    class Seat { public Transform root, crate, postit; public Animator an; public TextMesh text; public Renderer paper; }
    readonly List<Seat> seats = new List<Seat>();
    QuiSuisJe qs;
    int me, fitFrames, eyeFrames;
    Transform myHead;
    Transform cast;
    Material lit, paperMat, foundMat;
    Font font;
    Vector3 eye;
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
        int n = qs.players.Count, k = ((i - me) % n + n) % n;
        float a = (k == 0 ? 0 : n == 2 ? 180 : 70 + (k - 1) * 220f / (n - 2)) * Mathf.Deg2Rad;
        return Middle + new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a)) * Radius;
    }
    Quaternion Facing(int i) { var d = Middle - SeatPos(i); d.y = 0; return Quaternion.LookRotation(d); }

    public void Build(QuiSuisJe q, int mySeat, IList<string> avatars)
    {
        Clairiere.Show(true);
        gameObject.SetActive(true);
        qs = q;
        me = Mathf.Clamp(mySeat, 0, q.players.Count - 1);
        if (cast) Destroy(cast.gameObject);
        cast = new GameObject("joueurs").transform;
        cast.SetParent(transform, false);
        seats.Clear();
        for (int i = 0; i < q.players.Count; i++)
        {
            var s = new Seat();
            var pos = SeatPos(i); var face = Facing(i);
            s.crate = Model("SM_Prop_Camp_Crate_01", pos - face * new Vector3(0, 0, SeatBack), 0.6f, face.eulerAngles.y + 90);
            s.root = Chars.Spawn(avatars.Count > i ? avatars[i] : Chars.Default, cast, pos, face.eulerAngles.y, out s.an);
            s.an.Play("SitDown", 0, i * 0.19f);
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
            s.text.font = font; s.text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            s.text.fontSize = 96; s.text.anchor = TextAnchor.MiddleCenter; s.text.alignment = TextAlignment.Center;
            s.text.fontStyle = FontStyle.Bold; s.text.color = Board.Hex("2e1b10");
            if (i == me)
            {
                myHead = s.an.GetBoneTransform(HumanBodyBones.Head);   // je vois mon corps, pas ma tete (ecrasee dans LateUpdate)
                s.postit.gameObject.SetActive(false);
            }
            seats.Add(s);
        }
        fitFrames = 3; eyeFrames = 5; eye = Vector3.zero;
        yaw = tYaw = 0; pitch = tPitch = 12; fov = tFov = 60;
        Sync();
    }

    public void Hide() { qs = null; gameObject.SetActive(false); Clairiere.Show(false); }

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

    public void Anim(int seat, string state) { if (seat >= 0 && seat < seats.Count && seats[seat].an) seats[seat].an.CrossFadeInFixedTime(state, 0.4f); }
    public void AllBut(int seat, string state) { for (int i = 0; i < seats.Count; i++) if (i != seat) Anim(i, state); }

    Transform Head(int i) => seats[i].an ? seats[i].an.GetBoneTransform(HumanBodyBones.Head) : null;
    public Vector3 HeadOf(int i) { var h = Head(i); return (h ? h.position : seats[i].root.position + Vector3.up * 1.3f) + Vector3.up * 0.32f; }

    void LateUpdate()
    {
        if (qs == null) return;
        // Une fois la pose assise calculee, chacun se pose sur sa caisse (comme les amis de l'accueil).
        if (fitFrames > 0 && --fitFrames == 0)
            for (int i = 0; i < seats.Count; i++)
            {
                var hips = seats[i].an.GetBoneTransform(HumanBodyBones.Hips);
                var box = seats[i].crate ? seats[i].crate.GetComponentInChildren<Renderer>() : null;
                if (!hips || !box) continue;
                var b = box.bounds;
                seats[i].root.position += new Vector3(b.center.x - hips.position.x, b.max.y + 0.1f - hips.position.y, b.center.z - hips.position.z);
            }
        for (int i = 0; i < seats.Count; i++)
        {
            var h = Head(i);
            if (!h) continue;
            var fwd = seats[i].root.forward;
            // Mon oeil : pris une fois, apres m'etre assis (suivre la tete animee fait tanguer la camera).
            if (i == me) { if (eyeFrames > 0 && --eyeFrames == 0) eye = h.position + fwd * 0.12f + Vector3.up * 0.06f; continue; }
            seats[i].postit.position = h.position + fwd * 0.13f + Vector3.up * 0.2f;   // sur le front
            seats[i].postit.rotation = Quaternion.LookRotation(-fwd);
        }
        if (myHead) myHead.localScale = Vector3.one * 0.001f;   // ma tete (et mes cheveux) ne bouchent pas la vue
        float k = 1 - Mathf.Exp(-Time.deltaTime * 8);
        yaw = Mathf.LerpAngle(yaw, tYaw, k); pitch = Mathf.Lerp(pitch, tPitch, k); fov = Mathf.Lerp(fov, tFov, k);
    }

    // --- Camera a la premiere personne --------------------------------------------------
    public float Fov => fov;
    public Pose CamPose
    {
        get
        {
            var basis = Facing(me);
            var pos = eye == Vector3.zero ? transform.TransformPoint(SeatPos(me) + Vector3.up * 1.25f) : eye;
            return new Pose(pos, basis * Quaternion.Euler(pitch, yaw, 0));
        }
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
