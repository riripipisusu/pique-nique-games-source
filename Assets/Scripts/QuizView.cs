using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

// Plateau TV du quiz : ecran geant ou l'image se devoile (flou ou pixels), pupitres des joueurs avec leur avatar.
// Les images sont telechargees depuis leur site d'origine ; la suivante est prechargee pendant la question en cours.
public class QuizView : MonoBehaviour
{
    public static readonly Vector3 Center = new Vector3(0, -600, 0);
    const float ScreenW = 7.2f, ScreenH = 4.05f, ScreenY = 3.9f, ScreenZ = 6.2f;

    Material lit, glowBase, screenMat;
    Transform screen, podiums;
    RenderTexture display;
    readonly Dictionary<string, Texture2D> images = new Dictionary<string, Texture2D>();
    readonly HashSet<string> loading = new HashSet<string>();
    readonly List<Podium> seats = new List<Podium>();
    Quiz quiz;
    public float reveal = 1;          // 0 = image cachee au maximum, 1 = nette
    public bool pixelated;
    public string imageUrl;

    class Podium
    {
        public Transform root, head; public Renderer panel; public Animator an; public Material glow; public Color color;
        public Label name, score, strip; public VisualElement band, screenUi; public int shown;
    }
    Font font;

    // Recul suffisant pour voir les joueurs entiers derriere leurs pupitres, l'ecran au-dessus.
    public Pose CamPose => new Pose(transform.TransformPoint(new Vector3(0, 2.9f, -7.2f)),
        Quaternion.LookRotation(transform.TransformDirection(new Vector3(0, -0.02f, 1))));

    // Image telechargee (ou abandonnee apres echec : on n'attend pas indefiniment).
    public bool Ready(string url) { Preload(url); return images.ContainsKey(url) || failed.Contains(url); }
    readonly HashSet<string> failed = new HashSet<string>();

    void Awake()
    {
        transform.position = Center;
        lit = Resources.Load<Material>("Lit");
        font = Resources.Load<Font>("Fonts/Fredoka");
        glowBase = Resources.Load<Material>("LitGlow");
        screenMat = new Material(Resources.Load<Material>("QuizScreen"));
        display = new RenderTexture(1024, 1024, 0) { name = "EcranQuiz" };
        screenMat.SetTexture("_BaseMap", display);
        BuildSet();
    }

    Material Color(string hex, float smooth = 0.3f) { var m = new Material(lit) { color = Board.Hex(hex) }; m.SetFloat("_Smoothness", smooth); return m; }
    Material Glow(Color c, float k = 2.5f) { var m = new Material(glowBase) { color = c }; m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * k); return m; }

    Transform Box(PrimitiveType t, Vector3 pos, Vector3 scale, Material m, Transform parent, Vector3 euler = default)
    {
        var g = GameObject.CreatePrimitive(t);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.transform.localRotation = Quaternion.Euler(euler);
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g.transform;
    }

    GameObject Prop(string name, Vector3 pos, float rotY, float scale = 1)
    {
        var p = Synty.Get(name);
        if (!p) return null;
        var g = Instantiate(p, transform);
        g.transform.localPosition = pos;
        g.transform.localRotation = Quaternion.Euler(0, rotY, 0);
        g.transform.localScale = Vector3.one * scale;
        return g;
    }

    void Spot(Vector3 pos, Vector3 at, Color c, float intensity, float angle = 40)
    {
        var l = new GameObject("projecteur").AddComponent<Light>();
        l.transform.SetParent(transform, false);
        l.transform.localPosition = pos;
        l.transform.LookAt(transform.TransformPoint(at));
        l.type = LightType.Spot;
        l.spotAngle = angle;
        l.range = 16;
        l.intensity = intensity;
        l.color = c;
    }

    void BuildSet()
    {
        var t = transform;
        // Sol noir brillant, estrade, anneaux lumineux.
        Box(PrimitiveType.Cylinder, new Vector3(0, -0.05f, 2), new Vector3(26, 0.05f, 26), Color("0d0b14", 0.85f), t);
        Box(PrimitiveType.Cylinder, new Vector3(0, 0.05f, 2.2f), new Vector3(11, 0.1f, 6.5f), Color("1b1726", 0.7f), t);
        Box(PrimitiveType.Cylinder, new Vector3(0, 0.02f, 2.2f), new Vector3(11.5f, 0.06f, 7f), Glow(Board.Hex("7a3cff"), 1.6f), t);
        // Rideaux au fond, ecran geant encadre de lumiere.
        for (int i = 0; i < 5; i++) Prop("SM_Bld_Curtain_Closed_01", new Vector3(-12.5f + (i + 1) * 5, 0, 8.4f), 0);
        Box(PrimitiveType.Cube, new Vector3(0, ScreenY, ScreenZ + 0.2f), new Vector3(ScreenW + 0.6f, ScreenH + 0.6f, 0.3f), Color("15121c", 0.6f), t);
        var frame = Glow(Board.Hex("ffc83a"), 2);
        foreach (var (p, s) in new[] { (new Vector3(0, ScreenH / 2 + 0.25f, 0), new Vector3(ScreenW + 0.5f, 0.06f, 0.05f)), (new Vector3(0, -ScreenH / 2 - 0.25f, 0), new Vector3(ScreenW + 0.5f, 0.06f, 0.05f)),
                                       (new Vector3(ScreenW / 2 + 0.25f, 0, 0), new Vector3(0.06f, ScreenH + 0.5f, 0.05f)), (new Vector3(-ScreenW / 2 - 0.25f, 0, 0), new Vector3(0.06f, ScreenH + 0.5f, 0.05f)) })
            Box(PrimitiveType.Cube, new Vector3(0, ScreenY, ScreenZ) + p, s, frame, t);
        screen = Box(PrimitiveType.Quad, new Vector3(0, ScreenY, ScreenZ), new Vector3(ScreenW, ScreenH, 1), screenMat, t);
        screen.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // Rampe de projecteurs, neons, lumieres colorees.
        Box(PrimitiveType.Cube, new Vector3(0, 7, 3), new Vector3(16, 0.15f, 0.15f), Color("2a2a30", 0.6f), t);
        for (int i = 0; i < 6; i++) Prop("SM_Prop_Light_Stage_Spot_01", new Vector3(-7.5f + i * 3, 6.95f, 3), 180);
        // Eventails lumineux de part et d'autre de l'ecran.
        foreach (var (x, n) in new[] { (-9.2f, 1), (5.8f, 2) }) Prop("SM_Prop_Casino_Sign_Decor_0" + n, new Vector3(x, 2.2f, 8.1f), 0, 0.9f);
        Spot(new Vector3(-6, 6.8f, 0), new Vector3(0, 0, 2), Board.Hex("ff4fd8"), 30);
        Spot(new Vector3(6, 6.8f, 0), new Vector3(0, 0, 2), Board.Hex("3fd0ff"), 30);
        Spot(new Vector3(0, 6.8f, -2), new Vector3(0, 1, 2), Board.Hex("fff1d6"), 25, 55);
        Spot(new Vector3(0, 6.5f, 3), new Vector3(0, 3.5f, 8.4f), Board.Hex("7a3cff"), 20, 90);
        podiums = new GameObject("pupitres").transform;
        podiums.SetParent(t, false);
    }

    // Un pupitre par joueur, en arc face a la camera ; l'avatar se tient derriere.
    public void Build(Quiz q, IList<string> avatars)
    {
        quiz = q;
        foreach (Transform c in podiums) Destroy(c.gameObject);
        seats.Clear();
        int n = q.players.Count;
        float spacing = Mathf.Min(1.9f, 12f / Mathf.Max(1, n));   // jusqu'a 10 joueurs en arc
        float size = n > 6 ? 1.55f : 1.75f;
        for (int i = 0; i < n; i++)
        {
            float x = (i - (n - 1) / 2f) * spacing;
            var root = new GameObject("pupitre " + i).transform;
            root.SetParent(podiums, false);
            root.localPosition = new Vector3(x, 0.1f, 1.2f + x * x * 0.03f);
            root.localRotation = Quaternion.Euler(0, -x * 2.5f, 0);
            var col = Board.Colors[i % Board.Colors.Length];
            var p = new Podium { root = root, color = col, glow = Glow(col, 1.2f) };
            // Pupitre facon plateau TV : la face avant EST un ecran (texture dessinee par l'interface) avec
            // prenom, score et bande de reponse (derniere proposition ou "Trouvé !"), cadre a la couleur du joueur.
            float w = Mathf.Min(1.25f, spacing * 0.86f);
            Box(PrimitiveType.Cube, new Vector3(0, 0.45f, 0), new Vector3(w, 0.9f, 0.6f), Color("dfe8f7", 0.5f), root);
            Box(PrimitiveType.Cube, new Vector3(0, 0.93f, -0.02f), new Vector3(w + 0.06f, 0.06f, 0.68f), Color("f4f8ff", 0.6f), root);
            Box(PrimitiveType.Cube, new Vector3(0, 0.2f, 0.62f), new Vector3(w, 0.4f, 0.6f), Color("15121c", 0.6f), root);
            p.panel = Box(PrimitiveType.Quad, new Vector3(0, 0.5f, -0.302f), new Vector3(w * 0.9f, 0.72f, 1), p.glow, root).GetComponent<Renderer>();
            // Rendu en 2x avec mipmaps : net de pres, lisible de loin sans scintiller.
            var rt = new RenderTexture(720, 576, 0) { name = "pupitre", useMipMap = true, autoGenerateMips = true, anisoLevel = 8 };
            var face = new Material(Resources.Load<Material>("QuizScreen"));
            face.SetTexture("_BaseMap", rt);
            Box(PrimitiveType.Quad, new Vector3(0, 0.5f, -0.305f), new Vector3(w * 0.86f, 0.688f, 1), face, root);
            BuildPodiumUi(p, rt, q.players[i].name, col);
            Chars.Spawn(avatars.Count > i ? avatars[i] : "Casual_Male", root, new Vector3(0, 0.4f, 0.62f), 180, out p.an, size);
            p.head = new GameObject("tete").transform;
            p.head.SetParent(root, false);
            p.head.localPosition = new Vector3(0, 0.4f + size + 0.25f, 0.62f);
            seats.Add(p);
        }
    }

    // Interface d'un pupitre rendue dans sa texture (police et styles du jeu : Resources/UI/Menu.uss, classes .pod-*).
    void BuildPodiumUi(Podium p, RenderTexture rt, string player, Color col)
    {
        var baseSettings = Resources.Load<PanelSettings>("UI/Panel");
        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        ps.themeStyleSheet = baseSettings.themeStyleSheet;
        ps.targetTexture = rt;
        ps.scaleMode = PanelScaleMode.ConstantPixelSize;
        ps.scale = 2;
        ps.clearColor = true;
        ps.colorClearValue = Board.Hex("0e3a8f");
        var go = new GameObject("ecran pupitre");
        go.transform.SetParent(p.root, false);
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = ps;
        var r = doc.rootVisualElement;
        r.styleSheets.Add(Resources.Load<StyleSheet>("UI/Menu"));
        r.AddToClassList("root");
        r.AddToClassList("pod");
        r.style.borderTopColor = r.style.borderBottomColor = r.style.borderLeftColor = r.style.borderRightColor = col;
        p.screenUi = r;
        p.name = new Label(player.ToUpperInvariant()); p.name.AddToClassList("pod-name"); r.Add(p.name);
        p.score = new Label("0"); p.score.AddToClassList("pod-score"); r.Add(p.score);
        p.band = new VisualElement(); p.band.AddToClassList("pod-band"); r.Add(p.band);
        p.strip = new Label(""); p.strip.AddToClassList("pod-strip"); p.band.Add(p.strip);
    }

    TextMesh Label(Transform parent, Vector3 pos, float size, Color c, bool bold)
    {
        var t = new GameObject("texte").AddComponent<TextMesh>();
        t.transform.SetParent(parent, false);
        t.transform.localPosition = pos;
        t.transform.localRotation = Quaternion.identity;
        t.font = font;
        t.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        t.fontSize = 96;
        t.characterSize = size;
        t.anchor = TextAnchor.MiddleCenter;
        t.alignment = TextAlignment.Center;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.color = c;
        return t;
    }

    // Texte de la bande de reponse, raccourci pour tenir sous le pupitre.
    public void Strip(int seat, string text, Color c)
    {
        var p = seats[seat];
        p.strip.text = (text.Length > 16 ? text.Substring(0, 15) + "…" : text).ToUpperInvariant();
        p.strip.style.color = c;
    }

    void LateUpdate()
    {
        // Le score defile jusqu'a sa nouvelle valeur.
        if (quiz == null) return;
        for (int i = 0; i < seats.Count && i < quiz.players.Count; i++)
        {
            var p = seats[i];
            int target = quiz.players[i].score;
            if (p.shown == target) continue;
            p.shown = Mathf.Min(target, p.shown + Mathf.Max(1, (target - p.shown) / 6));
            if (p.shown > target) p.shown = target;
            p.score.text = p.shown.ToString();
        }
    }

    // --- Images -------------------------------------------------------------------------
    public void Preload(string url) { if (url != null && !images.ContainsKey(url) && !loading.Contains(url)) StartCoroutine(Load(url)); }

    IEnumerator Load(string url)
    {
        loading.Add(url);
        using (var r = UnityWebRequestTexture.GetTexture(url, false))
        {
            r.timeout = 20;
            yield return r.SendWebRequest();
            if (r.result == UnityWebRequest.Result.Success)
            {
                var tex = DownloadHandlerTexture.GetContent(r);
                tex.wrapMode = TextureWrapMode.Clamp;
                images[url] = tex;
            }
            else { failed.Add(url); Debug.LogWarning($"Quiz : image introuvable {url} ({r.error})"); }
        }
        loading.Remove(url);
        // Pas plus d'une vingtaine d'images en memoire.
        if (images.Count > 20) foreach (var k in images.Keys.Where(k => k != imageUrl).Take(images.Count - 20).ToList()) { Destroy(images[k]); images.Remove(k); }
    }

    // Etat du pupitre : allume a sa couleur, vert quand il a trouve.
    public void SetFound(int seat, bool found)
    {
        var p = seats[seat];
        var c = found ? Board.Hex("3fe07a") : p.color;
        p.glow.color = c;
        p.glow.SetColor("_EmissionColor", c * (found ? 3f : 1.2f));
        p.band.EnableInClassList("found", found);
        if (!found) p.strip.text = "";
        if (found && p.an) p.an.CrossFadeInFixedTime("Victory", 0.15f);
        else if (p.an) p.an.CrossFadeInFixedTime("Idle", 0.25f);
    }

    public void Wrong(int seat) { if (seats[seat].an) seats[seat].an.CrossFadeInFixedTime("RecieveHit", 0.1f); }
    public void Winner(int seat) { if (seats[seat].an) seats[seat].an.CrossFadeInFixedTime("Victory", 0.2f); }

    void Update()
    {
        if (!screen) return;
        if (imageUrl == null || !images.TryGetValue(imageUrl, out var tex))
        {
            // Pas d'image (intro, chargement) : ecran violet.
            var prev = RenderTexture.active;
            RenderTexture.active = display;
            GL.Clear(false, true, Board.Hex("1b1030"));
            RenderTexture.active = prev;
            screen.localScale = new Vector3(ScreenW, ScreenH, 1);
            return;
        }
        // L'image garde ses proportions dans l'ecran 16:9.
        float aspect = tex.width / (float)tex.height;
        float w = ScreenW, h = ScreenW / aspect;
        if (h > ScreenH) { h = ScreenH; w = ScreenH * aspect; }
        screen.localScale = new Vector3(w, h, 1);
        Obscure(tex, reveal, pixelated);
    }

    // Degradation progressive : gros pixels (jusqu'a 1/64) ou flou (reduction puis agrandissement lisse).
    void Obscure(Texture src, float k, bool pixels)
    {
        // 128 -> 1, en restant brouille plus longtemps au debut (courbe en k^1.5).
        float amount = Mathf.Pow(2, 7f * (1 - Mathf.Pow(Mathf.Clamp01(k), 1.5f)));
        if (amount <= 1.05f) { Graphics.Blit(src, display); return; }
        if (pixels)
        {
            // Pixels carres a l'ecran : la grille suit les proportions de l'image (etiree ensuite sur l'ecran).
            float aspect = src.width / (float)src.height;
            int cols = Mathf.Max(2, Mathf.RoundToInt(display.width / amount));
            int rows = Mathf.Max(2, Mathf.RoundToInt(cols / aspect));
            var grid = RenderTexture.GetTemporary(cols, rows, 0);
            grid.filterMode = FilterMode.Point;
            Graphics.Blit(src, grid);
            Graphics.Blit(grid, display);
            RenderTexture.ReleaseTemporary(grid);
            return;
        }
        int w = Mathf.Max(1, Mathf.RoundToInt(display.width / amount)), h = Mathf.Max(1, Mathf.RoundToInt(display.height / amount));
        var small = RenderTexture.GetTemporary(w, h, 0);
        small.filterMode = FilterMode.Bilinear;
        Graphics.Blit(src, small);
        // Remontee par paliers : un flou doux au lieu de gros blocs.
        var cur = small;
        while (cur.width * 2 < display.width)
        {
            var up = RenderTexture.GetTemporary(cur.width * 2, cur.height * 2, 0);
            up.filterMode = FilterMode.Bilinear;
            Graphics.Blit(cur, up);
            if (cur != small) RenderTexture.ReleaseTemporary(cur);
            cur = up;
        }
        Graphics.Blit(cur, display);
        if (cur != small) RenderTexture.ReleaseTemporary(cur);
        RenderTexture.ReleaseTemporary(small);
    }
}
