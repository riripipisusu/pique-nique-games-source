using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Vue 3D + menu + HUD. Toute la logique vient de Rules ; ici on ne fait qu'afficher et animer.
public class Game : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (!FindAnyObjectByType<Game>()) new GameObject("Game").AddComponent<Game>(); }

    static readonly Color[] Colors = { Hex("e0483b"), Hex("3b7de0"), Hex("3fae5a"), Hex("e8b429") };
    static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

    enum Phase { Menu, Playing }
    Phase phase = Phase.Menu;
    Mode mode = Mode.Classique;
    readonly List<string> names = new List<string> { "Joueur 1", "Joueur 2" };

    Rules rules;
    bool busy;
    string banner;
    float bannerUntil;

    Camera cam;
    float yaw = 30, pitch = 38, dist = 27;
    Material baseMat;
    Transform board, carrot;
    readonly Dictionary<int, Transform> tiles = new Dictionary<int, Transform>();
    readonly Dictionary<int, GameObject> warn = new Dictionary<int, GameObject>();
    Transform[,] rabbits;
    static readonly Vector3 Pen = new Vector3(-13, 0.1f, -10);

    // --- Mise en place ------------------------------------------------------
    void Start()
    {
        baseMat = Resources.Load<Material>("Base");

        cam = new GameObject("Camera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Hex("9fd3f0");
        cam.farClipPlane = 300;
        var light = new GameObject("Soleil").AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = Hex("fff1d6");
        light.intensity = 1.2f;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(50, -35, 0);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Hex("8a9fb0");
        RenderSettings.fog = true;
        RenderSettings.fogColor = Hex("9fd3f0");
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 45;
        RenderSettings.fogEndDistance = 110;
        BuildDecor();
    }

    Material Mat(Color c)
    {
        var m = new Material(baseMat) { color = c };
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
        return m;
    }

    GameObject Prim(PrimitiveType t, Vector3 pos, Vector3 scale, Color c, Transform parent, Vector3 euler = default)
    {
        var g = GameObject.CreatePrimitive(t);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.transform.localRotation = Quaternion.Euler(euler);
        g.GetComponent<Renderer>().sharedMaterial = Mat(c);
        return g;
    }

    void BuildDecor()
    {
        var d = new GameObject("Decor").transform;
        var rng = new System.Random(7);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        Prim(PrimitiveType.Cylinder, new Vector3(0, -0.1f, 0), new Vector3(160, 0.1f, 160), Hex("6fb04a"), d);
        for (int i = 0; i < 60; i++)
        {
            float a = R(0, Mathf.PI * 2), r = R(18, 55);
            var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            float h = R(1.5f, 3f);
            Prim(PrimitiveType.Cylinder, p + Vector3.up * h / 2, new Vector3(0.5f, h / 2, 0.5f), Hex("7a5230"), d);
            var green = Color.Lerp(Hex("2f7d32"), Hex("5aa33c"), R(0, 1));
            float s = R(2.2f, 3.6f);
            Prim(PrimitiveType.Sphere, p + Vector3.up * (h + s * 0.35f), new Vector3(s, s * 1.1f, s), green, d);
            Prim(PrimitiveType.Sphere, p + Vector3.up * (h + s * 0.9f), Vector3.one * s * 0.65f, green, d);
        }
        for (int i = 0; i < 30; i++)
        {
            float a = R(0, Mathf.PI * 2), r = R(14, 45), s = R(0.6f, 1.8f);
            Prim(PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * r, s * 0.2f, Mathf.Sin(a) * r), new Vector3(s, s * 0.6f, s * 0.8f), Hex("8e8e86"), d, new Vector3(0, R(0, 360), 0));
        }
        Color[] petals = { Hex("ffffff"), Hex("f7d23e"), Hex("ec6fa3"), Hex("a37be0") };
        for (int i = 0; i < 160; i++)
        {
            float a = R(0, Mathf.PI * 2), r = R(12, 35);
            Prim(PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * r, 0.12f, Mathf.Sin(a) * r), Vector3.one * 0.3f, petals[rng.Next(4)], d);
        }
        for (int i = 0; i < 8; i++)
        {
            float a = R(0, Mathf.PI * 2), r = R(35, 60);
            var c = new Vector3(Mathf.Cos(a) * r, R(18, 26), Mathf.Sin(a) * r);
            for (int k = 0; k < 4; k++)
                Prim(PrimitiveType.Sphere, c + new Vector3(k * 2.2f, R(-0.5f, 0.8f), R(-1, 1)), Vector3.one * R(3, 5), Color.white, d);
        }
        // Enclos de depart
        Prim(PrimitiveType.Cylinder, Pen - Vector3.up * 0.05f, new Vector3(7, 0.08f, 5), Hex("b98a57"), d);
        for (int i = 0; i < 12; i++)
        {
            float a = i * 30 * Mathf.Deg2Rad;
            Prim(PrimitiveType.Cube, Pen + new Vector3(Mathf.Cos(a) * 3.6f, 0.4f, Mathf.Sin(a) * 2.6f), new Vector3(0.2f, 0.8f, 0.2f), Hex("f2e6cf"), d);
        }
    }

    // --- Geometrie du plateau -------------------------------------------------
    Vector3 SummitPos => new Vector3(0, rules.mode == Mode.Classique ? 4.6f : 3.4f, 0);

    Vector3 Pos(int i)
    {
        if (i >= rules.summit) return SummitPos;
        if (rules.mode == Mode.Ameliore)
        {
            bool outer = i <= Rules.OuterRing;
            float ang = (outer ? (i - 1) * 24f : (i - 16) * 36f + 12f) * Mathf.Deg2Rad;
            float r = outer ? 9.5f : 5.8f;
            return new Vector3(Mathf.Cos(ang) * r, outer ? 0.9f : 2.1f, Mathf.Sin(ang) * r);
        }
        float t = (i - 1) / (float)(rules.summit - 2);
        float rr = 10.5f - 7.2f * t, a = (225 - t * 540) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a) * rr, 0.4f + t * 3.6f, Mathf.Sin(a) * rr);
    }

    Vector3 PenSlot(int p, int r) => Pen + new Vector3((p - 1.5f) * 1.5f, 0.05f, (r - 1) * 1.3f);

    Vector3 SummitSlot(int p, int r)
    {
        float a = (p * 3 + r) * 30 * Mathf.Deg2Rad;
        return SummitPos + new Vector3(Mathf.Cos(a) * 1.6f, 0.1f, Mathf.Sin(a) * 1.6f);
    }

    Vector3 RabbitPos(int p, int r)
    {
        int pos = rules.players[p].rabbits[r];
        if (pos == Rules.Start) return PenSlot(p, r);
        if (pos >= rules.summit) return SummitSlot(p, r);
        return Pos(pos) + Vector3.up * 0.12f;
    }

    void BuildBoard()
    {
        if (board) Destroy(board.gameObject);
        board = new GameObject("Plateau").transform;
        tiles.Clear();
        warn.Clear();
        bool am = rules.mode == Mode.Ameliore;
        Prim(PrimitiveType.Sphere, Vector3.zero, am ? new Vector3(14, 5.4f, 14) : new Vector3(15, 8.4f, 15), Hex("7d9b4c"), board);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int i = 1; i < rules.summit; i++)
        {
            var p = Pos(i);
            Prim(PrimitiveType.Cylinder, new Vector3(p.x, p.y / 2, p.z), new Vector3(1.1f, p.y / 2, 1.1f), Hex("8b6443"), board);
            var tile = Prim(PrimitiveType.Cylinder, p, new Vector3(1.8f, 0.12f, 1.8f), i % 2 == 0 ? Hex("e9dcc0") : Hex("d6c4a0"), board).transform;
            tiles[i] = tile;
            var label = new GameObject("n" + i).AddComponent<TextMesh>();
            label.font = font;
            label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            label.text = i.ToString();
            label.fontSize = 48;
            label.characterSize = 0.08f;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = Hex("6b4a2b");
            label.transform.SetParent(board, false);
            label.transform.position = p + Vector3.up * 0.13f;
            label.transform.rotation = Quaternion.Euler(90, 0, 0);
            if (am)
            {
                var w = Prim(PrimitiveType.Cylinder, p - Vector3.up * 0.04f, new Vector3(2.3f, 0.1f, 2.3f), Hex("ff3b2f"), board);
                w.SetActive(false);
                warn[i] = w;
            }
        }
        Prim(PrimitiveType.Cylinder, SummitPos - Vector3.up * 0.1f, new Vector3(4.4f, 0.15f, 4.4f), Hex("5b3a22"), board);
        carrot = new GameObject("Carotte").transform;
        carrot.SetParent(board);
        carrot.position = SummitPos;
        Prim(PrimitiveType.Capsule, new Vector3(0, 1.1f, 0), new Vector3(0.9f, 1.2f, 0.9f), Hex("f08a24"), carrot);
        Prim(PrimitiveType.Cube, new Vector3(0, 0.9f, 0.46f), new Vector3(0.6f, 0.06f, 0.05f), Hex("c96a12"), carrot);
        for (int k = 0; k < 3; k++)
            Prim(PrimitiveType.Cube, new Vector3(0, 2.6f, 0), new Vector3(0.18f, 1f, 0.18f), Hex("3fae5a"), carrot, new Vector3(0, k * 60, 22));

        rabbits = new Transform[rules.players.Count, Rules.RabbitsPerPlayer];
        for (int p = 0; p < rules.players.Count; p++)
            for (int r = 0; r < Rules.RabbitsPerPlayer; r++)
                rabbits[p, r] = MakeRabbit(Colors[rules.players[p].color]);
        Sync();
    }

    Transform MakeRabbit(Color c)
    {
        var root = new GameObject("Lapin").transform;
        root.SetParent(board);
        var m = new GameObject("m").transform;
        m.SetParent(root, false);
        Prim(PrimitiveType.Sphere, new Vector3(0, 0.36f, 0), new Vector3(0.62f, 0.56f, 0.75f), c, m);
        Prim(PrimitiveType.Sphere, new Vector3(0, 0.76f, 0.28f), Vector3.one * 0.46f, c, m);
        Prim(PrimitiveType.Capsule, new Vector3(0.11f, 1.12f, 0.22f), new Vector3(0.13f, 0.28f, 0.07f), c, m, new Vector3(-8, 0, -12));
        Prim(PrimitiveType.Capsule, new Vector3(-0.11f, 1.12f, 0.22f), new Vector3(0.13f, 0.28f, 0.07f), c, m, new Vector3(-8, 0, 12));
        Prim(PrimitiveType.Sphere, new Vector3(0, 0.38f, -0.38f), Vector3.one * 0.22f, Color.white, m);
        Prim(PrimitiveType.Sphere, new Vector3(0.12f, 0.82f, 0.48f), Vector3.one * 0.07f, Color.black, m);
        Prim(PrimitiveType.Sphere, new Vector3(-0.12f, 0.82f, 0.48f), Vector3.one * 0.07f, Color.black, m);
        Prim(PrimitiveType.Sphere, new Vector3(0, 0.72f, 0.52f), Vector3.one * 0.07f, Hex("f29bb0"), m);
        return root;
    }

    void Sync()
    {
        for (int p = 0; p < rules.players.Count; p++)
            for (int r = 0; r < Rules.RabbitsPerPlayer; r++)
            {
                rabbits[p, r].position = RabbitPos(p, r);
                rabbits[p, r].localScale = Vector3.one;
            }
    }

    // --- Actions ------------------------------------------------------------
    void StartGame()
    {
        var n = names.Select((s, i) => string.IsNullOrWhiteSpace(s) ? "Joueur " + (i + 1) : s.Trim());
        rules = new Rules(mode, n, Random.Range(0, int.MaxValue));
        busy = false;
        banner = null;
        BuildBoard();
        phase = Phase.Playing;
    }

    void Say(string s) { banner = s; bannerUntil = Time.time + 2.8f; }

    void DoDraw()
    {
        if (busy || rules.Over || rules.drawn != null) return;
        string who = rules.Current.name;
        var res = rules.Draw();
        if (res != null) StartCoroutine(AnimCarrot(res, who));
        else Say($"{who} pioche : avance de {rules.drawn.steps} !");
    }

    void DoMove(int r)
    {
        if (busy || !rules.CanMove(r)) return;
        var res = rules.Move(r);
        StartCoroutine(AnimMove(res));
    }

    // --- Animations ---------------------------------------------------------
    IEnumerator Hop(Transform t, Vector3 to, float dur)
    {
        var from = t.position;
        var dir = to - from;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.001f) t.rotation = Quaternion.LookRotation(dir);
        float h = 0.8f + Mathf.Abs(to.y - from.y) * 0.5f;
        for (float k = 0; k < 1; k += Time.deltaTime / dur)
        {
            var p = Vector3.Lerp(from, to, k);
            p.y += Mathf.Sin(k * Mathf.PI) * h;
            t.position = p;
            t.localScale = new Vector3(1, 1 + Mathf.Sin(k * Mathf.PI) * 0.15f, 1);
            yield return null;
        }
        t.position = to;
        t.localScale = Vector3.one;
    }

    IEnumerator AnimMove(MoveResult m)
    {
        busy = true;
        var t = rabbits[m.player, m.rabbit];
        foreach (int i in m.path)
            yield return Hop(t, i >= rules.summit ? SummitSlot(m.player, m.rabbit) : Pos(i) + Vector3.up * 0.12f, 0.32f);
        if (m.path.Count > 0 && m.path[m.path.Count - 1] >= rules.summit) Say($"{rules.players[m.player].name} arrive au potager !");
        if (rules.Over) Say($"{rules.players[rules.winner].name} gagne la partie !");
        Sync();
        busy = false;
    }

    IEnumerator AnimCarrot(CarrotResult res, string who)
    {
        busy = true;
        Say($"{who} tourne la carotte...");
        float y0 = carrot.eulerAngles.y;
        for (float k = 0; k < 1; k += Time.deltaTime / 1.1f)
        {
            carrot.rotation = Quaternion.Euler(0, y0 + Mathf.SmoothStep(0, 360, k), 0);
            yield return null;
        }
        var holes = res.opened.Select(i => tiles[i]).ToList();
        var fallers = res.fallen.Select(f => rabbits[f.player, f.rabbit]).ToList();
        var tileBase = holes.Select(t => t.position).ToList();
        var fallBase = fallers.Select(t => t.position).ToList();
        Say(res.fallen.Count == 0 ? $"Case {string.Join(", ", res.opened)} : personne ne tombe !" : $"Case {string.Join(", ", res.opened)} : un lapin dégringole !");
        for (float k = 0; k < 1; k += Time.deltaTime / 0.35f)
        {
            for (int i = 0; i < holes.Count; i++) holes[i].position = tileBase[i] - Vector3.up * 0.9f * k;
            for (int i = 0; i < fallers.Count; i++) fallers[i].position = fallBase[i] - Vector3.up * 0.9f * k;
            yield return null;
        }
        for (float k = 0; k < 1; k += Time.deltaTime / 0.6f)
        {
            for (int i = 0; i < fallers.Count; i++)
            {
                fallers[i].position = fallBase[i] - Vector3.up * (0.9f + 6 * k * k);
                fallers[i].rotation = Quaternion.Euler(k * 720, 0, k * 360);
            }
            yield return null;
        }
        yield return new WaitForSeconds(0.4f);
        for (int i = 0; i < res.fallen.Count; i++)
        {
            fallers[i].rotation = Quaternion.identity;
            fallers[i].position = PenSlot(res.fallen[i].player, res.fallen[i].rabbit);
        }
        for (float k = 0; k < 1; k += Time.deltaTime / 0.35f)
        {
            for (int i = 0; i < holes.Count; i++) holes[i].position = tileBase[i] - Vector3.up * 0.9f * (1 - k);
            foreach (var f in fallers) f.localScale = Vector3.one * k;
            yield return null;
        }
        for (int i = 0; i < holes.Count; i++) holes[i].position = tileBase[i];
        Sync();
        busy = false;
    }

    // --- Boucle -------------------------------------------------------------
    void Update()
    {
        if (phase == Phase.Menu) yaw += 5 * Time.deltaTime;
        if (Input.GetMouseButton(1)) { yaw += Input.GetAxis("Mouse X") * 4; pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3, 12, 80); }
        dist = Mathf.Clamp(dist - Input.mouseScrollDelta.y * 1.5f, 12, 50);
        var target = new Vector3(0, 2.5f, 0);
        cam.transform.position = target + Quaternion.Euler(pitch, yaw, 0) * Vector3.back * dist;
        cam.transform.LookAt(target);

        if (phase != Phase.Playing) return;
        if (Input.GetKeyDown(KeyCode.Space)) DoDraw();
        for (int r = 0; r < Rules.RabbitsPerPlayer; r++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + r) || Input.GetKeyDown(KeyCode.Keypad1 + r)) DoMove(r);

        // Les lapins jouables sautillent sur place
        for (int p = 0; p < rules.players.Count; p++)
            for (int r = 0; r < Rules.RabbitsPerPlayer; r++)
            {
                bool bob = !busy && p == rules.turn && rules.CanMove(r);
                rabbits[p, r].GetChild(0).localPosition = Vector3.up * (bob ? Mathf.Abs(Mathf.Sin(Time.time * 6 + r)) * 0.25f : 0);
            }

        if (rules.mode == Mode.Ameliore)
        {
            var threat = new HashSet<int>();
            for (int d = 1; d <= 2; d++)
            {
                int c = rules.known[(rules.rotations + d) % Rules.CycleLength];
                if (c > 0) threat.Add(c);
            }
            foreach (var kv in warn) kv.Value.SetActive(threat.Contains(kv.Key));
        }
    }

    // --- Interface (IMGUI) ----------------------------------------------------
    GUIStyle title, text, big, btn, panel, small;
    Texture2D white;

    void Styles()
    {
        if (title != null) return;
        white = Texture2D.whiteTexture;
        var bg = new Texture2D(1, 1);
        bg.SetPixel(0, 0, new Color(0.09f, 0.21f, 0.12f, 0.88f));
        bg.Apply();
        panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(18, 18, 14, 14) };
        panel.normal.background = bg;
        panel.border = new RectOffset(0, 0, 0, 0);
        title = new GUIStyle(GUI.skin.label) { fontSize = 60, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        title.normal.textColor = Hex("f08a24");
        text = new GUIStyle(GUI.skin.label) { fontSize = 19, wordWrap = true, richText = true };
        text.normal.textColor = Hex("f2f7f0");
        small = new GUIStyle(text) { fontSize = 15 };
        small.normal.textColor = Hex("a9c4ae");
        big = new GUIStyle(text) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fixedHeight = 44 };
    }

    void OnGUI()
    {
        Styles();
        float s = Screen.height / 900f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
        float w = Screen.width / s;
        if (phase == Phase.Menu) DrawMenu(w); else DrawHud(w);
    }

    void Swatch(Color c)
    {
        var r = GUILayoutUtility.GetRect(22, 22, GUILayout.Width(22));
        r.y += 10;
        GUI.color = c;
        GUI.DrawTexture(new Rect(r.x, r.y, 20, 20), white);
        GUI.color = Color.white;
    }

    void DrawMenu(float w)
    {
        GUILayout.BeginArea(new Rect(w / 2 - 280, 60, 560, 790), panel);
        GUILayout.Label("Croque-Carotte", title, GUILayout.Height(90));
        GUILayout.Label("Amène tes 3 lapins au potager avant les autres... sans tomber dans les trous !", small);
        GUILayout.Space(14);
        GUILayout.Label("<b>Mode de jeu</b>", text);
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(mode == Mode.Classique, "Classique", btn)) mode = Mode.Classique;
        if (GUILayout.Toggle(mode == Mode.Ameliore, "Amélioré", btn)) mode = Mode.Ameliore;
        GUILayout.EndHorizontal();
        GUILayout.Label(mode == Mode.Classique
            ? "19 cases en spirale. Tourner la carotte ouvre 1 à 3 trous au hasard."
            : "25 cases sur deux anneaux. Les trous s'ouvrent un par un selon un cycle secret : observe-le pour le prévoir. Les cases menacées s'allument en rouge.", small);
        GUILayout.Space(14);
        GUILayout.Label("<b>Joueurs</b> (sur ce PC, chacun son tour)", text);
        for (int i = 0; i < names.Count; i++)
        {
            GUILayout.BeginHorizontal();
            Swatch(Colors[i]);
            names[i] = GUILayout.TextField(names[i], 16, new GUIStyle(GUI.skin.textField) { fontSize = 20, fixedHeight = 40 });
            if (names.Count > 2 && GUILayout.Button("x", btn, GUILayout.Width(44))) { names.RemoveAt(i); GUILayout.EndHorizontal(); break; }
            GUILayout.EndHorizontal();
        }
        if (names.Count < Rules.MaxPlayers && GUILayout.Button("+ Ajouter un joueur", btn)) names.Add("Joueur " + (names.Count + 1));
        GUILayout.FlexibleSpace();
        GUI.backgroundColor = Hex("f08a24");
        if (GUILayout.Button("JOUER", new GUIStyle(btn) { fontSize = 28, fixedHeight = 64, fontStyle = FontStyle.Bold })) StartGame();
        GUI.backgroundColor = Color.white;
        if (GUILayout.Button("Quitter", btn)) Application.Quit();
        GUILayout.EndArea();
    }

    void DrawHud(float w)
    {
        GUILayout.BeginArea(new Rect(16, 16, 330, 560), panel);
        foreach (var (p, i) in rules.players.Select((p, i) => (p, i)))
        {
            GUILayout.BeginHorizontal();
            Swatch(Colors[p.color]);
            int home = p.rabbits.Count(x => x >= rules.summit);
            string name = i == rules.turn && !rules.Over ? $"<b><color=#f08a24>> {p.name}</color></b>" : p.name;
            GUILayout.Label($"{name}   <size=15>{home}/3 au potager</size>", text);
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(12);
        if (rules.Over)
        {
            GUILayout.Label($"{rules.players[rules.winner].name} gagne !", big);
            if (GUILayout.Button("Rejouer", btn)) StartGame();
        }
        else if (busy) GUILayout.Label("...", big);
        else if (rules.drawn == null)
        {
            GUILayout.Label($"Au tour de <b>{rules.Current.name}</b>", text);
            if (GUILayout.Button("Piocher une carte  [Espace]", btn)) DoDraw();
        }
        else
        {
            GUILayout.Label($"Avance de {rules.drawn.steps}", big);
            GUILayout.Label("Quel lapin ?", small);
            for (int r = 0; r < Rules.RabbitsPerPlayer; r++)
            {
                int pos = rules.Current.rabbits[r];
                string where = pos == 0 ? "au départ" : pos >= rules.summit ? "au potager" : "case " + pos;
                GUI.enabled = rules.CanMove(r);
                if (GUILayout.Button($"[{r + 1}] Lapin {r + 1} ({where})", btn)) DoMove(r);
                GUI.enabled = true;
            }
        }
        GUILayout.Label($"Cartes restantes : {rules.DeckLeft}", small);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Menu", btn)) { phase = Phase.Menu; StopAllCoroutines(); busy = false; }
        GUILayout.EndArea();

        GUILayout.BeginArea(new Rect(16, 900 - 196, 520, 180), panel);
        foreach (var l in rules.log.Skip(Mathf.Max(0, rules.log.Count - 6))) GUILayout.Label(l, small);
        GUILayout.EndArea();

        if (rules.mode == Mode.Ameliore) DrawCycle(w);

        if (banner != null && Time.time < bannerUntil)
        {
            var r = new Rect(w / 2 - 400, 30, 800, 60);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.Label(new Rect(r.x + 3, r.y + 3, r.width, r.height), banner, big);
            GUI.color = Color.white;
            GUI.Label(r, banner, big);
        }
        GUI.Label(new Rect(w - 420, 900 - 34, 410, 30), "Clic droit : tourner la caméra · Molette : zoom", small);
    }

    void DrawCycle(float w)
    {
        GUILayout.BeginArea(new Rect(w - 336, 16, 320, 400), panel);
        GUILayout.Label($"<b>Crans des aiguilles</b>  <size=15>({rules.rotations} crans)</size>", text);
        int now = rules.rotations % Rules.CycleLength;
        for (int row = 0; row < 5; row++)
        {
            GUILayout.BeginHorizontal();
            for (int col = 0; col < 5; col++)
            {
                int t = row * 5 + col;
                int c = rules.Over ? rules.cycle[t] : rules.known[t];
                bool next = t == (now + 1) % 25 || t == (now + 2) % 25;
                GUI.backgroundColor = t == now && rules.rotations > 0 ? Hex("f08a24") : next ? Hex("ff3b2f") : c > 0 ? Hex("4f8f5c") : Hex("24402b");
                GUILayout.Box(c > 0 ? c.ToString() : "?", new GUIStyle(btn) { fontSize = 18 }, GUILayout.Width(52));
            }
            GUILayout.EndHorizontal();
        }
        GUI.backgroundColor = Color.white;
        GUILayout.Label("Orange : cran actuel. Rouge : les 2 prochains crans possibles (1 ou 2 carottes).", small);
        GUILayout.EndArea();
    }
}
