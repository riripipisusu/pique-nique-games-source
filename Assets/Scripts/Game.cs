using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

// Chef d'orchestre : ambiance, camera, entrees, enchainement Rules -> animations -> interface.
public class Game : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (!FindAnyObjectByType<Game>()) new GameObject("Game").AddComponent<Game>(); }

    public Settings settings;
    public Rules rules;
    public Mode mode = Mode.Classique;
    public readonly List<string> names = new List<string> { "Joueur 1", "Joueur 2" };
    public bool busy;

    Board board;
    Ui ui;
    Camera cam;
    Light sun;
    DepthOfField dof;
    bool inGame, paused;
    float yaw = 35, pitch = 24, dist = 36;
    Vector3 target = new Vector3(0, 2.5f, 0);

    void Start()
    {
        settings = Settings.Load();

        cam = new GameObject("Camera").AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 50;
        cam.farClipPlane = 400;
        cam.gameObject.AddComponent<AudioListener>();

        sun = new GameObject("Soleil").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = Board.Hex("fff0d4");
        sun.intensity = 1.15f;
        sun.shadowStrength = 0.75f;
        sun.transform.rotation = Quaternion.Euler(42, -40, 0);
        RenderSettings.sun = sun;
        RenderSettings.skybox = Resources.Load<Material>("Sky");
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Board.Hex("b9d4f5");
        RenderSettings.ambientEquatorColor = Board.Hex("b8c7a8");
        RenderSettings.ambientGroundColor = Board.Hex("6e7f55");
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Board.Hex("cfe2f2");
        RenderSettings.fogStartDistance = 70;
        RenderSettings.fogEndDistance = 190;

        var vol = new GameObject("PostFX").AddComponent<Volume>();
        vol.isGlobal = true;
        vol.profile = Instantiate(Resources.Load<VolumeProfile>("Post"));
        vol.profile.TryGet(out dof);

        Sound.Create(settings);
        board = new GameObject("Board").AddComponent<Board>();
        MenuBackdrop();

        var uiGo = new GameObject("UI");
        uiGo.SetActive(false);
        var doc = uiGo.AddComponent<UIDocument>();
        doc.panelSettings = Resources.Load<PanelSettings>("UI/Panel");
        uiGo.SetActive(true);
        ui = uiGo.AddComponent<Ui>();
        ui.Init(this, doc);

        ApplySettings();
        ui.ShowTitle();
        Sound.I.Music("music_menu");
        net = Net.Create(this);
        net.Changed += ui.RefreshOnline;
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-autotest");
        if (at >= 0) ui.StartCoroutine(AutoTest(args[at + 1]));
        int nt = Array.IndexOf(args, "-nettest");
        if (nt >= 0) ui.StartCoroutine(NetTest(args[nt + 1] == "host", args[nt + 2]));
    }

    public void Replay()
    {
        if (!Online) StartGame();
        else if (net.IsHost) net.StartMatch();
    }

    int applied;

    // Deux instances jouent l'une contre l'autre via Relay puis ecrivent leur journal, pour comparer.
    IEnumerator NetTest(bool host, string dir)
    {
        string codeFile = System.IO.Path.Combine(dir, "code.txt");
        yield return new WaitForSeconds(3);
        if (host)
        {
            net.Host("Hôte");
            while (net.Code == "") { if (net.Status.StartsWith("Impossible")) break; yield return null; }
            System.IO.File.WriteAllText(codeFile, net.Code);
            while (net.Lobby.Count < 2) yield return null;
            yield return new WaitForSeconds(1);
            net.StartMatch();
        }
        else
        {
            while (!System.IO.File.Exists(codeFile)) yield return new WaitForSeconds(0.5f);
            net.Join(System.IO.File.ReadAllText(codeFile), "Invité");
        }
        while (!inGame) yield return null;
        while (applied < 30 && !rules.Over)
        {
            if (MyTurn && CanAct)
            {
                if (rules.drawn == null) Draw();
                else for (int k = 0; k < 3; k++) if (rules.CanMove(k)) { Move(k); break; }
            }
            yield return new WaitForSeconds(0.2f);
        }
        while (busy || (pending.Count > 0 && applied < 30)) yield return null;
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, (host ? "host" : "join") + ".png"), tex.EncodeToPNG());
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, (host ? "host" : "join") + ".txt"),
            $"status={net.Status}\nseat={mySeat}\napplied={applied}\n" + string.Join("\n", rules.log));
        yield return new WaitForSeconds(3);
        Application.Quit();
    }

    // Parcours automatique avec captures d'ecran, pour verifier une build sans interaction.
    IEnumerator AutoTest(string dir)
    {
        IEnumerator Shot(string n) { yield return new WaitForEndOfFrame(); var tex = ScreenCapture.CaptureScreenshotAsTexture(); System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, n + ".png"), tex.EncodeToPNG()); Destroy(tex); }
        yield return new WaitForSeconds(6); yield return Shot("1-titre");
        ui.OpenForTest("settings"); yield return new WaitForSeconds(1); yield return Shot("2-parametres");
        ui.OpenForTest("newgame"); yield return new WaitForSeconds(1); yield return Shot("3-nouvelle");
        foreach (var m in new[] { Mode.Classique, Mode.Ameliore })
        {
            mode = m;
            names.Clear(); names.AddRange(new[] { "Anastasia", "Léo", "Camille" });
            StartGame();
            yield return new WaitForSeconds(2);
            for (int i = 0; i < 16; i++)
            {
                while (busy) yield return null;
                Debug.Log("AUTOTEST draw " + i); Draw();
                yield return new WaitForSeconds(0.8f);
                while (busy) yield return null;
                if (i == 6) yield return Shot("4-" + m + "-carte");
                for (int k = 0; k < 3 && rules.drawn != null; k++) Move((i + k) % 3);
                yield return new WaitForSeconds(0.3f);
                if (i == 10) yield return Shot("5-" + m + "-saut");
            }
            while (busy) yield return null;
            yield return new WaitForSeconds(1); yield return Shot("6-" + m + "-plateau");
        }
        Pause(); yield return new WaitForSecondsRealtime(1); yield return Shot("7-pause");
        yield return new WaitForSecondsRealtime(1);
        Application.Quit();
    }

    // Plateau de fond du menu : une partie simulee pour que les lapins soient eparpilles.
    void MenuBackdrop()
    {
        var r = new Rules(Mode.Classique, new[] { "A", "B", "C", "D" }, 4);
        var rng = new System.Random(4);
        for (int i = 0; i < 26 && !r.Over; i++)
        {
            r.Draw();
            if (r.drawn != null) for (int k = 0; k < 3 && r.Move(rng.Next(3)) == null; k++) { }
        }
        rules = null;
        board.Build(r);
    }

    public void ApplySettings()
    {
        settings.Apply(cam, sun);
        Sound.I.Refresh();
        board.speed = settings.animSpeed;
        board.showNumbers = settings.tileNumbers;
        settings.Save();
    }

    public void ResetSettings()
    {
        var fresh = new Settings { resW = settings.resW, resH = settings.resH };
        foreach (var f in typeof(Settings).GetFields()) f.SetValue(settings, f.GetValue(fresh));
        ApplySettings();
    }

    // --- Flux de partie ---------------------------------------------------------------
    public int mySeat = -1;                       // -1 = partie locale
    public Net net;
    readonly Queue<string> pending = new Queue<string>();
    float waitUntil;
    public bool Online => net.Active && net.InGame;
    public bool MyTurn => rules != null && (!Online || rules.turn == mySeat);
    public bool Idle => inGame && !busy && pending.Count == 0;
    bool CanAct => inGame && !busy && !paused && rules != null && !rules.Over && MyTurn && pending.Count == 0 && Time.time > waitUntil;

    public void StartGame()
    {
        var n = names.Select((s, i) => string.IsNullOrWhiteSpace(s) ? "Joueur " + (i + 1) : s.Trim()).ToList();
        StartGame(mode, n, UnityEngine.Random.Range(0, int.MaxValue));
    }

    public void StartGame(Mode m, List<string> n, int seed)
    {
        StopAllCoroutines();
        pending.Clear();
        waitUntil = 0;
        rules = new Rules(m, n, seed);
        board.Build(rules);
        busy = false;
        paused = false;
        Time.timeScale = 1;
        inGame = true;
        dist = 30;
        pitch = 34;
        ui.ShowHud();
        ui.Say($"Au tour de {rules.Current.name} !");
        Sound.I.Music("music_game");
    }

    public void Draw()
    {
        if (!CanAct || rules.drawn != null) return;
        if (Online) { waitUntil = Time.time + 2; net.Act("draw"); return; }
        DoDraw();
    }

    public void Move(int rabbit)
    {
        if (!CanAct || !rules.CanMove(rabbit)) return;
        if (Online) { waitUntil = Time.time + 2; net.Act("move|" + rabbit); return; }
        DoMove(rabbit);
    }

    public void Enqueue(string action) => pending.Enqueue(action);

    public void PlayerLeft(int seat) => ui.Say($"{rules.players[seat].name} est parti : l'hôte joue pour lui.", 3.5f);

    void DoDraw()
    {
        string who = rules.Current.name;
        var res = rules.Draw();
        Sound.I.Play("card", 1, 0.05f);
        if (res == null)
        {
            ui.ShowCard(rules.drawn);
            ui.Say($"Avance de {rules.drawn.steps} !");
            ui.Refresh();
            return;
        }
        ui.ShowCard(new Card { carrot = true, turns = res.turns });
        ui.Say($"{who} tourne la carotte !");
        StartCoroutine(Run(board.AnimCarrot(res), () =>
        {
            ui.HideCard();
            string cases = string.Join(", ", res.opened);
            if (res.fallen.Count == 0) ui.Say($"Case {cases} : ouf, personne !");
            else ui.Say(string.Join(" et ", res.fallen.Select(f => rules.players[f.player].name).Distinct()) + " dégringole !");
            StartCoroutine(NextTurnBanner(2.4f));
        }));
    }

    void DoMove(int rabbit)
    {
        var res = rules.Move(rabbit);
        if (res == null) return;
        ui.HideCard();
        StartCoroutine(Run(board.AnimMove(res), () =>
        {
            if (rules.Over) StartCoroutine(Run(board.Celebrate(rules.winner), () => ui.ShowVictory()));
            else ui.Say($"Au tour de {rules.Current.name} !");
        }));
    }

    IEnumerator NextTurnBanner(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (inGame && !rules.Over && rules.drawn == null && !busy) ui.Say($"Au tour de {rules.Current.name} !");
    }

    IEnumerator Run(IEnumerator anim, Action after)
    {
        busy = true;
        board.busy = true;
        ui.Refresh();
        yield return anim;
        busy = false;
        board.busy = false;
        after?.Invoke();
        ui.Refresh();
    }

    public void Pause()
    {
        if (!inGame || paused || rules.Over) return;
        paused = true;
        if (!Online)
        {
            Time.timeScale = 0;
            AudioListener.pause = true;
        }
        ui.ShowPause();
    }

    public void Resume()
    {
        paused = false;
        Time.timeScale = 1;
        AudioListener.pause = false;
    }

    public void ToMenu()
    {
        StopAllCoroutines();
        Resume();
        if (net.Active) net.Leave();
        pending.Clear();
        inGame = false;
        busy = false;
        MenuBackdrop();
        ui.ShowTitle();
        Sound.I.Music("music_menu");
    }

    // --- Boucle ---------------------------------------------------------------------------
    void Update()
    {
        if (inGame && !busy && pending.Count > 0 && rules != null && !rules.Over)
        {
            var p = pending.Dequeue().Split('|');
            waitUntil = 0;
            applied++;
            if (p[1] == "draw") DoDraw(); else DoMove(int.Parse(p[2]));
            ui.Refresh();
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (ui.InSubMenu) ui.Back();
            else if (inGame && !paused) Pause();
        }
        if (CanAct)
        {
            if (Input.GetKeyDown(KeyCode.Space)) Draw();
            for (int r = 0; r < Rules.RabbitsPerPlayer; r++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + r) || Input.GetKeyDown(KeyCode.Keypad1 + r)) Move(r);
        }
    }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime, sens = settings.camSens;
        if (!inGame) yaw += 3.5f * dt;
        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * 4 * sens;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3 * sens, 10, 75);
        }
        if (Input.GetKey(KeyCode.Q)) yaw += 70 * dt * sens;
        if (Input.GetKey(KeyCode.E)) yaw -= 70 * dt * sens;
        if (inGame && !paused) dist = Mathf.Clamp(dist - Input.mouseScrollDelta.y * 2f, 12, 55);

        var home = new Vector3(0, 2.5f, 0);
        var want = inGame && settings.autoCam ? Vector3.Lerp(home, board.Focus, 0.55f) : home;
        if (!inGame) { want = new Vector3(0, 4f, 0); pitch = Mathf.Lerp(pitch, 20, dt); dist = Mathf.Lerp(dist, 34, dt); }
        target = Vector3.Lerp(target, want, 1 - Mathf.Exp(-2.5f * dt));
        cam.transform.position = target + Quaternion.Euler(pitch, yaw, 0) * Vector3.back * dist;
        cam.transform.LookAt(target);
        if (dof) dof.active = !inGame || paused;
    }
}
