using UnityEngine;

// Studio photo du createur de personnages : loin du monde, fond degrade, eclairage trois points, un podium qui tourne.
// Le personnage est rendu dans une texture affichee par l'interface ; on le tourne a la souris, on zoome sur le visage.
public class CreatorStudio : MonoBehaviour
{
    public static readonly Vector3 Center = new Vector3(0, -500, 0);
    public RenderTexture Target { get; private set; }
    Camera cam;
    Transform turntable, current;
    Animator an;
    float yaw = 180, yawVel, zoom, zoomTarget;   // zoom : 0 = en pied, 1 = visage
    string shown;

    public static CreatorStudio Make()
    {
        var s = new GameObject("StudioPersonnage").AddComponent<CreatorStudio>();
        s.Init();
        return s;
    }

    void Init()
    {
        transform.position = Center;
        Target = new RenderTexture(900, 1200, 24, RenderTextureFormat.ARGB32) { name = "Createur", antiAliasing = 4 };
        cam = new GameObject("camera").AddComponent<Camera>();
        cam.transform.SetParent(transform, false);
        cam.targetTexture = Target;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.fieldOfView = 26;
        cam.nearClipPlane = 0.05f; cam.farClipPlane = 30;
        cam.cullingMask = 1 << 30;
        // Podium rond.
        var lit = Resources.Load<Material>("Lit");
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(transform, false);
        disc.transform.localScale = new Vector3(1.3f, 0.04f, 1.3f);
        disc.transform.localPosition = new Vector3(0, -0.04f, 0);
        disc.GetComponent<Renderer>().sharedMaterial = new Material(lit) { color = Board.Hex("f3d9a4") };
        disc.layer = 30;
        turntable = new GameObject("plateau").transform;
        turntable.SetParent(transform, false);
        // Eclairage trois points (seulement ce calque).
        Light L(Vector3 at, Color c, float i)
        {
            var l = new GameObject("lampe").AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.localPosition = at;
            l.transform.LookAt(transform.TransformPoint(new Vector3(0, 1.2f, 0)));
            l.type = LightType.Spot; l.spotAngle = 60; l.range = 12; l.intensity = i; l.color = c;
            l.cullingMask = 1 << 30;
            l.shadows = LightShadows.Soft;
            return l;
        }
        L(new Vector3(-1.8f, 2.6f, -2.4f), Board.Hex("fff1dd"), 14);
        L(new Vector3(2.2f, 1.8f, -1.6f), Board.Hex("cfe0ff"), 6).shadows = LightShadows.None;
        L(new Vector3(0.4f, 2.8f, 2.4f), Board.Hex("ffd0f0"), 9).shadows = LightShadows.None;
        gameObject.SetActive(false);
    }

    public void Show(bool on) { gameObject.SetActive(on); }

    // Remplace le personnage (reconstruit a chaque changement).
    public void Set(string code)
    {
        if (code == shown && current) return;
        shown = code;
        if (current) Destroy(current.gameObject);
        current = Chars.Spawn(code, turntable, Vector3.zero, 0, out an);
        foreach (var t in current.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
        an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        an.Play("Idle");
    }

    public void Drag(float dx) { yawVel = 0; yaw -= dx * 0.4f; }
    public void Fling(float v) { yawVel = -v * 0.4f; }
    public void Focus(bool face) { zoomTarget = face ? 1 : 0; }
    public void Wave() { if (an) an.CrossFadeInFixedTime("Wave", 0.2f); }

    void LateUpdate()
    {
        yaw += yawVel * Time.unscaledDeltaTime * 60; yawVel = Mathf.Lerp(yawVel, 0, Time.unscaledDeltaTime * 3);
        turntable.localRotation = Quaternion.Euler(0, yaw, 0);
        zoom = Mathf.Lerp(zoom, zoomTarget, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 6));
        float h = current ? current.localScale.y * Sidekick.NaturalHeight / 1.8f * 1.8f : 1.8f;
        var head = an ? an.GetBoneTransform(HumanBodyBones.Head) : null;
        var focusFull = transform.TransformPoint(new Vector3(0, h * 0.52f, 0));
        var focusFace = head ? head.position + Vector3.up * 0.06f : focusFull + Vector3.up * 0.8f;
        var at = Vector3.Lerp(focusFull, focusFace, zoom);
        float dist = Mathf.Lerp(h * 2.55f + 1.2f, 1.25f, zoom);
        cam.transform.position = at + new Vector3(0, Mathf.Lerp(0.15f, 0.02f, zoom), -dist);
        cam.transform.LookAt(at);
    }
}
