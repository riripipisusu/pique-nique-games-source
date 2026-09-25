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
    Vector2 screenSize = new Vector2(ScreenW, ScreenH);
    float screenAspect = 16 / 9f;
    bool fillsFace;   // (garde pour essai : image peinte sur la dalle trapezoidale, abandonne car deformee)
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
    public Pose CamPose => new Pose(transform.TransformPoint(camFrom), Quaternion.LookRotation(transform.TransformDirection(camAt - camFrom)));

    // --- Grand quiz de Tenna : la question (et les propositions en QCM) s'affiche sur l'ecran geant ---
    RenderTexture questionRt;
    VisualElement qRoot;
    Label qText, qAnswer;
    readonly List<Label> qProps = new List<Label>();
    bool showingQuestion;

    public void ShowQuestion(QuizQuestion q, bool mcq = false)
    {
        showingQuestion = q != null;
        if (q == null) { if (qRoot != null) qRoot.style.display = DisplayStyle.None; return; }
        if (qRoot == null) BuildQuestionPanel();
        qRoot.style.display = DisplayStyle.Flex;
        qText.text = q.q;
        qAnswer.text = "";
        for (int k = 0; k < 4; k++)
        {
            qProps[k].style.display = mcq ? DisplayStyle.Flex : DisplayStyle.None;
            qProps[k].text = mcq ? $"{k + 1}.  {q.p[k]}" : "";
            qProps[k].RemoveFromClassList("good");
        }
    }

    public void RevealAnswer(QuizQuestion q)
    {
        if (qRoot == null) return;
        qAnswer.text = q.d;
        for (int k = 0; k < 4; k++) qProps[k].EnableInClassList("good", q.p[k] == q.d);
    }

    void BuildQuestionPanel()
    {
        questionRt = new RenderTexture(1600, 900, 0) { name = "question", useMipMap = true, autoGenerateMips = true, anisoLevel = 8 };
        var baseSettings = Resources.Load<PanelSettings>("UI/Panel");
        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        ps.themeStyleSheet = baseSettings.themeStyleSheet;
        ps.targetTexture = questionRt;
        ps.scaleMode = PanelScaleMode.ConstantPixelSize;
        ps.clearColor = true;
        ps.colorClearValue = Board.Hex("1b1030");
        ps.SetScreenToPanelSpaceFunction(_ => new Vector2(float.NaN, float.NaN));
        var go = new GameObject("ecran question");
        go.transform.SetParent(transform, false);
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = ps;
        qRoot = doc.rootVisualElement;
        qRoot.pickingMode = PickingMode.Ignore;
        qRoot.styleSheets.Add(Resources.Load<StyleSheet>("UI/Menu"));
        qRoot.AddToClassList("root");
        qRoot.AddToClassList("tq");
        qText = new Label(); qText.AddToClassList("tq-text"); qRoot.Add(qText);
        var grid = new VisualElement(); grid.AddToClassList("tq-grid"); qRoot.Add(grid);
        for (int k = 0; k < 4; k++) { var l = new Label(); l.AddToClassList("tq-prop"); l.AddToClassList("choice-" + k); grid.Add(l); qProps.Add(l); }
        qAnswer = new Label(); qAnswer.AddToClassList("tq-answer"); qRoot.Add(qAnswer);
    }

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
        if (Synty.I && Synty.I.stage) BuildStage(); else BuildSet();
    }

    // --- Plateau "tenna stage" (modele Blender) ------------------------------------------
    Transform stage, standTemplate;
    static readonly Vector3 camFromStage = new Vector3(0, 5.6f, -10.2f);
    Vector3 camFrom = new Vector3(0, 2.9f, -7.2f), camAt = new Vector3(0, 2.9f - 0.144f, 0);

    void BuildStage()
    {
        // Le modele regarde vers +z : on le tourne pour que le public (la camera) soit cote -z.
        stage = Instantiate(Synty.I.stage, transform).transform;
        stage.localRotation = Quaternion.Euler(0, 180, 0);
        // Fond vert, mur et rideaux ne recoivent pas d'ombres : sinon celles des projecteurs y sont crenelees.
        foreach (var n in new[] { "backdrop", "wall", "Grid.001", "Grid.002", "Grid.003", "Grid.004" })
            if (stage.Find(n))
            {
                var rr = stage.Find(n).GetComponent<Renderer>();
                rr.receiveShadows = false;
                // URP lit l'option sur le materiau : copie sans ombres recues.
                var ms = rr.sharedMaterials;
                for (int k = 0; k < ms.Length; k++)
                {
                    if (!ms[k]) continue;
                    ms[k] = new Material(ms[k]);
                    ms[k].SetFloat("_ReceiveShadows", 0);
                    ms[k].EnableKeyword("_RECEIVE_SHADOWS_OFF");
                }
                rr.sharedMaterials = ms;
            }
        foreach (Transform c in stage)
            if (c.name.StartsWith("gamestand")) { if (c.name == "gamestand.003") standTemplate = c; c.gameObject.SetActive(false); }

        // Ecran geant : la dalle rose est remplacee par l'image du quiz, en 16:9 dans le cadre.
        var tv = stage.Find("big tv").GetComponent<MeshRenderer>();
        var mesh = tv.GetComponent<MeshFilter>().sharedMesh;
        var mats = tv.sharedMaterials;
        int slot = System.Array.FindIndex(mats, m => m && m.name.StartsWith("Material_009"));
        if (slot < 0) slot = mats.Length - 1;
        // Ecran geant refait en rectangle parfait 16:9 (la dalle du modele a des UV en morceaux) :
        // meme cadre orange, a la place de l'ancien, tourne face a la camera. L'image remplit tout l'ecran.
        var frameMat = mats[0];
        var tb = tv.bounds;
        tv.gameObject.SetActive(false);
        var group = new GameObject("ecran geant").transform;
        group.SetParent(transform, false);
        // Avance d'un metre vers le public : incline, le haut de l'ecran ne rentre plus dans le decor du fond.
        var center = new Vector3(tb.center.x, tb.min.y + 2.45f, tb.center.z) - transform.forward * 1.1f;
        group.position = center;
        group.rotation = Quaternion.LookRotation(center - transform.TransformPoint(camFromStage));
        const float sh = 3.95f, sw = sh * 16 / 9f, border = 0.32f;
        Box(PrimitiveType.Cube, new Vector3(0, 0, 0.12f), new Vector3(sw + border * 2, sh + border * 2, 0.24f), frameMat, group);
        Box(PrimitiveType.Cube, new Vector3(0, 0, -0.02f), new Vector3(sw + 0.08f, sh + 0.08f, 0.05f), new Material(lit) { color = Board.Hex("120c1c") }, group);
        screen = Box(PrimitiveType.Quad, new Vector3(0, 0, -0.06f), new Vector3(sw, sh, 1), screenMat, group);
        screen.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        screenAspect = 16 / 9f;
        fillsFace = true;

        // Lumieres : chaque projecteur du modele (positions reprises du fichier Blender) eclaire la scene.
        // Repere du plateau tourne = (x, z, y) de Blender.
        var rail = new[] { (-5.76f, "fff3e0"), (-4.64f, "ffd6f0"), (-3.58f, "fff3e0"), (3.56f, "ff6fb0"), (4.63f, "e6ff7a"), (5.76f, "ffe07a") };
        foreach (var (x, hex) in rail)
            StageLight(new Vector3(x, 4.7f, -0.3f), new Vector3(x * 0.35f, 0.3f, -2.6f), Board.Hex(hex), 34, 58, x == -4.64f || x == 4.63f);
        foreach (var x in new[] { -5.86f, 5.86f })   // projecteurs au sol : vers les pupitres et le fond
            StageLight(new Vector3(x, 0.9f, -3.8f), new Vector3(-x * 0.25f, 2.6f, 1.5f), Board.Hex("ffe2c0"), 35, 75, false);
        // Rideaux et fond de scene : lavage doux depuis la rampe.
        foreach (var x in new[] { -6.5f, 6.5f }) StageLight(new Vector3(x * 0.6f, 5.2f, -1.2f), new Vector3(x, 2.5f, 1.5f), Board.Hex("ffd8ee"), 20, 80, false);
        StageLight(new Vector3(0, 5.2f, -1.5f), new Vector3(0, 3.2f, 2.2f), Board.Hex("fff0da"), 16, 90, false);
        // Face douce depuis la salle, pour que les visages ne soient pas dans le noir.
        StageLight(new Vector3(0, 6, -12), new Vector3(0, 1.8f, -2), Board.Hex("fff4e8"), 14, 55, false);
        // Eclairage general : toute la scene (joueurs des bords, rideaux, mur du fond) est lisible.
        foreach (var (x, y, z, r, k) in new[] { (-6f, 3.5f, -3f, 9f, 1.6f), (0f, 3.5f, -3f, 9f, 1.2f), (6f, 3.5f, -3f, 9f, 1.6f),
                                                  (-7.5f, 4.5f, 1f, 9f, 1.6f), (7.5f, 4.5f, 1f, 9f, 1.6f), (0f, 7f, 1.5f, 12f, 1.8f) })
        {
            var l = new GameObject("ambiance").AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.localPosition = new Vector3(x, y, z);
            l.type = LightType.Point; l.range = r; l.intensity = k; l.color = Board.Hex("ffe6d0");
        }
        // Camera haute : les pupitres passent sous l'ecran geant au lieu de le masquer.
        camFrom = camFromStage;
        SpawnTenna();
        camAt = new Vector3(0, 1.75f, 0);   // le haut de l'image s'arrete a la rampe de projecteurs
        podiums = new GameObject("pupitres").transform;
        podiums.SetParent(transform, false);
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

    // --- Tenna, le presentateur (rig de ThatAverageJoe) ----------------------------------
    SkinnedMeshRenderer tennaFace;
    Transform tenna;

    // Gros plan : camera face a Tenna, a hauteur de son ecran-tete, un peu en contre-plongee.
    public bool HasTenna => tenna;
    public Pose TennaPose
    {
        get
        {
            float h = tenna.lossyScale.y / 0.09f * 2.35f / 2.35f;   // echelle relative au reglage d'origine
            var head = tenna.position + Vector3.up * 2.05f * h;
            var front = tenna.rotation * Vector3.forward;
            var from = head + front * 2.6f * h - Vector3.up * 0.35f * h + tenna.rotation * Vector3.right * 0.5f * h;
            return new Pose(from, Quaternion.LookRotation(head - Vector3.up * 0.25f * h - from));
        }
    }
    readonly Dictionary<int, float> faceWeights = new Dictionary<int, float>();

    void SpawnTenna()
    {
        if (!Synty.I.tenna) { Debug.LogWarning("Tenna absent du registre"); return; }
        var t = Instantiate(Synty.I.tenna, transform).transform;
        t.name = "Tenna";
        // Taille reelle mesuree sur le maillage pose, en coordonnees du monde (les bornes du skin et
        // les echelles internes du rig ne sont pas fiables) ; on ramene Tenna a 2.35 m, pieds au sol.
        float WorldHeight(out float footY)
        {
            var smr = t.GetComponentInChildren<SkinnedMeshRenderer>();
            var baked = new Mesh(); smr.BakeMesh(baked, true);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in baked.vertices) { float y = smr.transform.TransformPoint(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
            Destroy(baked);
            footY = lo;
            return hi - lo;
        }
        // Sur le devant de la scene, cote cour, tourne vers le public.
        // Position reglee a la souris dans Assets/Resources/QuizLayout.prefab (objet "Tenna").
        var marker = Resources.Load<GameObject>("QuizLayout")?.transform.Find("Tenna");
        if (marker)
        {
            t.localPosition = marker.localPosition;
            t.localRotation = marker.localRotation;
            t.localScale = marker.localScale;
        }
        else
        {
            t.localPosition = new Vector3(-5.2f, 0.12f, -2.2f);
            t.localRotation = Quaternion.Euler(0, 170, 0);
            t.localScale *= 2.35f / Mathf.Max(0.001f, WorldHeight(out _));
            WorldHeight(out float foot);
            t.position += Vector3.up * (transform.TransformPoint(new Vector3(0, 0.12f, 0)).y - foot);
        }
        foreach (var smr in t.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
        StartCoroutine(TennaDebug(t));
        tennaFace = t.GetComponentInChildren<SkinnedMeshRenderer>();
        tenna = t;
    }

    IEnumerator TennaDebug(Transform t)
    {
        yield return null; yield return null;
        var smr = t.GetComponentInChildren<SkinnedMeshRenderer>();
        var baked = new Mesh(); smr.BakeMesh(baked, true);
        var wb = new Bounds(smr.transform.TransformPoint(baked.bounds.center), Vector3.zero);
        foreach (var v in baked.vertices) wb.Encapsulate(smr.transform.TransformPoint(v));
        Debug.Log($"TENNA pos {t.position} echelle {t.lossyScale} monde {wb.center} taille {wb.size} anim {(t.GetComponent<Animation>() ? t.GetComponent<Animation>().isPlaying : false)} actif {smr.enabled}/{smr.gameObject.activeInHierarchy}");
    }

    // Expression du visage-ecran (blend shape) qui retombe doucement : "Pog", "SmileSketchfab", "HmmmSketchfab".
    public void TennaFace(string shape, float hold = 1.5f)
    {
        if (!tennaFace) return;
        int i = tennaFace.sharedMesh.GetBlendShapeIndex(shape);
        if (i < 0) return;
        foreach (var k in faceWeights.Keys.ToList()) faceWeights[k] = 0;
        faceWeights[i] = 100 + hold * 60;   // au-dela de 100 : tenu un moment avant de retomber
    }

    void UpdateTennaFace()
    {
        if (!tennaFace) return;
        foreach (var k in faceWeights.Keys.ToList())
        {
            faceWeights[k] = Mathf.Max(0, faceWeights[k] - Time.deltaTime * 60);
            tennaFace.SetBlendShapeWeight(k, Mathf.Min(100, faceWeights[k]));
        }
    }

    void StageLight(Vector3 pos, Vector3 at, Color c, float intensity, float angle, bool shadows)
    {
        var l = new GameObject("projecteur").AddComponent<Light>();
        l.transform.SetParent(transform, false);
        l.transform.localPosition = pos;
        l.transform.LookAt(transform.TransformPoint(at));
        l.type = LightType.Spot;
        l.spotAngle = angle;
        l.innerSpotAngle = angle * 0.55f;
        l.range = 18;
        l.intensity = intensity;
        l.color = c;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
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
            if (standTemplate)
            {
                StagePodium(p, i, n, q.players[i].name, col, avatars.Count > i ? avatars[i] : "Casual_Male");
                seats.Add(p);
                continue;
            }
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

    // Pupitres du modele Blender, en arc sur le devant de la scene ; le joueur se tient derriere.
    void StagePodium(Podium p, int i, int n, string player, Color col, string avatar)
    {
        // Rangee decalee a droite : la gauche de la scene est a Tenna.
        float spacing = Mathf.Min(1.45f, 10f / n), scale = Mathf.Min(1f, spacing / 1.45f);
        float x = (i - (n - 1) / 2f) * spacing + 0.7f;
        var root = p.root;
        root.localPosition = new Vector3(x, 0.12f, -3.0f + (x - 0.7f) * (x - 0.7f) * 0.03f);
        root.localRotation = Quaternion.Euler(0, (x - 0.7f) * 2.2f, 0);
        var stand = Instantiate(standTemplate.gameObject, stage);
        stand.SetActive(true);
        stand.transform.localScale = standTemplate.localScale * scale;
        // Recentrage : le pied du pupitre sur le point voulu.
        var r = stand.GetComponent<Renderer>();
        var b = r.bounds;
        stand.transform.position += root.position - new Vector3(b.center.x, b.min.y, b.center.z);
        stand.transform.SetParent(root, true);
        stand.transform.RotateAround(root.position, Vector3.up, (x - 0.7f) * 2.2f);
        // Ecran du pupitre : texture dessinee par l'interface (prenom, score, bande de reponse).
        var rt = new RenderTexture(720, 440, 0) { name = "pupitre", useMipMap = true, autoGenerateMips = true, anisoLevel = 8 };
        var face = new Material(Resources.Load<Material>("QuizScreen"));
        face.SetTexture("_BaseMap", rt);
        var mats = r.sharedMaterials;
        for (int k = 0; k < mats.Length; k++) if (mats[k] && mats[k].name.StartsWith("sign")) mats[k] = face;
        r.sharedMaterials = mats;
        p.panel = r;
        BuildPodiumUi(p, rt, player, col);
        float size = 1.62f * Mathf.Lerp(0.9f, 1f, scale);
        // Petite estrade derriere le pupitre (cachee par lui) : on voit le joueur jusqu'aux epaules.
        Chars.Spawn(avatar, root, new Vector3(0, 0.38f, 1.0f * scale), 180, out p.an, size);
        p.head = new GameObject("tete").transform;
        p.head.SetParent(root, false);
        p.head.localPosition = new Vector3(0, 0.38f + size + 0.25f, 1.0f * scale);
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
        // Ecran decoratif : il ne doit jamais recevoir la souris (sinon il vole les clics des vrais menus).
        ps.SetScreenToPanelSpaceFunction(_ => new Vector2(float.NaN, float.NaN));
        var go = new GameObject("ecran pupitre");
        go.transform.SetParent(p.root, false);
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = ps;
        var r = doc.rootVisualElement;
        r.pickingMode = PickingMode.Ignore;
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
        p.screenUi.style.backgroundColor = found ? Board.Hex("1c8a45") : Board.Hex("144ebe");
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
        UpdateTennaFace();
        if (!screen) return;
        if (showingQuestion) { Graphics.Blit(questionRt, display); return; }
        if (imageUrl == null || !images.TryGetValue(imageUrl, out var tex))
        {
            // Pas d'image (intro, chargement) : ecran violet.
            var prev = RenderTexture.active;
            RenderTexture.active = display;
            GL.Clear(false, true, Board.Hex("1b1030"));
            RenderTexture.active = prev;
            if (!fillsFace) screen.localScale = new Vector3(screenSize.x, screenSize.y, 1);
            return;
        }
        if (fillsFace) { Obscure(tex, reveal, pixelated); return; }
        // L'image garde ses proportions dans l'ecran 16:9.
        float aspect = tex.width / (float)tex.height;
        float w = screenSize.x, h = screenSize.x / aspect;
        if (h > screenSize.y) { h = screenSize.y; w = screenSize.y * aspect; }
        screen.localScale = new Vector3(w, h, 1);
        Obscure(tex, reveal, pixelated);
    }

    void Crop(Texture src, RenderTexture dst)
    {
        if (!fillsFace) { Graphics.Blit(src, dst); return; }
        float a = src.width / (float)src.height;
        var scale = a > screenAspect ? new Vector2(screenAspect / a, 1) : new Vector2(1, a / screenAspect);
        Graphics.Blit(src, dst, scale, (Vector2.one - scale) / 2);
    }

    // Degradation progressive : gros pixels (jusqu'a 1/64) ou flou (reduction puis agrandissement lisse).
    void Obscure(Texture src, float k, bool pixels)
    {
        // 128 -> 1, en restant brouille plus longtemps au debut (courbe en k^1.5).
        float amount = Mathf.Pow(2, 7f * (1 - Mathf.Pow(Mathf.Clamp01(k), 1.5f)));
        if (amount <= 1.05f) { Crop(src, display); return; }
        if (pixels)
        {
            // Pixels carres a l'ecran : la grille suit les proportions de l'image (etiree ensuite sur l'ecran).
            float aspect = fillsFace ? screenAspect : src.width / (float)src.height;
            int cols = Mathf.Max(2, Mathf.RoundToInt(display.width / amount));
            int rows = Mathf.Max(2, Mathf.RoundToInt(cols / aspect));
            var grid = RenderTexture.GetTemporary(cols, rows, 0);
            grid.filterMode = FilterMode.Point;
            Crop(src, grid);
            Graphics.Blit(grid, display);
            RenderTexture.ReleaseTemporary(grid);
            return;
        }
        int w = Mathf.Max(1, Mathf.RoundToInt(display.width / amount)), h = Mathf.Max(1, Mathf.RoundToInt(display.height / amount));
        var small = RenderTexture.GetTemporary(w, h, 0);
        small.filterMode = FilterMode.Bilinear;
        Crop(src, small);
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
