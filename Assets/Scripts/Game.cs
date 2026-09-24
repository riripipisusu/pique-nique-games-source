using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

// Chef d'orchestre : ambiance, camera, entrees, enchainement regles -> animations -> interface.
public class Game : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (!FindAnyObjectByType<Game>()) new GameObject("Game").AddComponent<Game>(); }

    public Settings settings;
    public GameId gameId = GameId.Croque;
    public int option;                            // Croque : 0 classique / 1 ameliore ; Blackjack : nombre de manches
    public readonly List<string> names = new List<string> { "Joueur 1", "Joueur 2" };
    public readonly List<string> avatars = new List<string> { "Casual_Male", "Casual_Female" };
    public string myAvatar;
    public Rules rules;
    public Blackjack bj;
    public IMatch Match => (IMatch)rules ?? bj;
    public bool busy;

    Board board;
    Table table;
    Hub hub;
    Ui ui;
    Camera cam;
    Light sun;
    DepthOfField dof;
    bool inGame, paused, snapCam = true;
    float yaw = 35, pitch = 24, dist = 36;
    Vector3 target = new Vector3(0, 2.5f, 0);

    public static readonly string[] Characters =
    {
        "Casual_Male", "Casual_Female", "Casual2_Male", "Casual2_Female", "Casual3_Male", "Casual3_Female", "Casual_Bald",
        "Suit_Male", "Suit_Female", "OldClassy_Male", "OldClassy_Female", "Worker_Male", "Worker_Female",
        "Chef_Male", "Chef_Female", "Chef_Hat", "Doctor_Male_Young", "Doctor_Female_Young", "Doctor_Male_Old", "Doctor_Female_Old",
        "Cowboy_Male", "Cowboy_Female", "Cowboy_Hair", "Pirate_Male", "Pirate_Female", "Kimono_Male", "Kimono_Female",
        "Ninja_Male", "Ninja_Female", "Ninja_Male_Hair", "Ninja_Sand", "Ninja_Sand_Female", "Knight_Male", "Knight_Golden_Male",
        "Knight_Golden_Female", "Viking_Male", "Viking_Female", "Soldier_Male", "Soldier_Female", "BlueSoldier_Male",
        "BlueSoldier_Female", "Wizard", "Witch", "Elf", "Goblin_Male", "Goblin_Female", "Zombie_Male", "Zombie_Female",
    };

    void Start()
    {
        settings = Settings.Load();
        myAvatar = PlayerPrefs.GetString("cc-avatar", "Casual_Male");

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
        table = new GameObject("Casino").AddComponent<Table>();
        hub = new GameObject("PiqueNique").AddComponent<Hub>();
        hub.SetMe(myAvatar);
        MenuBackdrop();

        var uiGo = new GameObject("UI");
        uiGo.SetActive(false);
        var doc = uiGo.AddComponent<UIDocument>();
        doc.panelSettings = Resources.Load<PanelSettings>("UI/Panel");
        uiGo.SetActive(true);
        ui = uiGo.AddComponent<Ui>();
        net = Net.Create(this);
        ui.Init(this, doc);
        net.Changed += ui.RefreshOnline;

        ApplySettings();
        ui.ShowTitle();
        Sound.I.Music("music_menu");
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-autotest");
        if (at >= 0) ui.StartCoroutine(AutoTest(args[at + 1]));
        int nt = Array.IndexOf(args, "-nettest");
        if (nt >= 0) ui.StartCoroutine(NetTest(args[nt + 1] == "host", (GameId)Enum.Parse(typeof(GameId), args[nt + 2]), args[nt + 3]));
    }

    public static int DefaultOption(GameId g) => g == GameId.Croque ? 0 : 10;

    public void SelectGame(GameId g)
    {
        if (gameId != g) option = DefaultOption(g);
        gameId = g;
    }

    public void SetMyAvatar(string a)
    {
        myAvatar = a;
        PlayerPrefs.SetString("cc-avatar", a);
        hub.SetMe(a);
    }

    public void Replay()
    {
        if (!Online) StartGame();
        else if (net.IsHost) net.StartMatch();
    }

    // --- Tests automatiques ---------------------------------------------------------------
    int applied;

    // Deux instances jouent l'une contre l'autre via Relay puis ecrivent leur journal, pour comparer.
    IEnumerator NetTest(bool host, GameId g, string dir)
    {
        string codeFile = System.IO.Path.Combine(dir, "code.txt");
        yield return new WaitForSeconds(3);
        if (host)
        {
            net.Host("Hôte", g, DefaultOption(g));
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
        while (applied < 30 && !Match.Finished)
        {
            if (MyTurn && CanAct) { var a = Match.Bot(); if (a != null) Act(string.Join("|", a)); }
            yield return new WaitForSeconds(0.2f);
        }
        while (busy || (pending.Count > 0 && applied < 30)) yield return null;
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, (host ? "host" : "join") + ".png"), tex.EncodeToPNG());
        var log = rules != null ? rules.log : bj.log;
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, (host ? "host" : "join") + ".txt"),
            $"status={net.Status}\nseat={mySeat}\napplied={applied}\n" + string.Join("\n", log));
        yield return new WaitForSeconds(3);
        Application.Quit();
    }

    // Parcours automatique avec captures d'ecran, pour verifier une build sans interaction.
    IEnumerator AutoTest(string dir)
    {
        IEnumerator Shot(string n) { yield return new WaitForEndOfFrame(); var tex = ScreenCapture.CaptureScreenshotAsTexture(); System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, n + ".png"), tex.EncodeToPNG()); Destroy(tex); }
        yield return new WaitForSeconds(6); yield return Shot("1-titre");
        ui.OpenForTest("games"); yield return new WaitForSeconds(1); yield return Shot("2-jeux");
        SelectGame(GameId.Blackjack);
        ui.OpenForTest("setup"); yield return new WaitForSeconds(1); yield return Shot("3-setup");
        ui.OpenForTest("avatar"); yield return new WaitForSeconds(1); yield return Shot("4-avatars");
        names.Clear(); names.AddRange(new[] { "Anastasia", "Léo", "Camille" });
        avatars.Clear(); avatars.AddRange(new[] { "Casual_Female", "Cowboy_Male", "Witch" });
        SelectGame(GameId.Blackjack);
        StartGame();
        yield return new WaitForSeconds(2);
        for (int i = 0; i < 40 && !Match.Finished; i++)
        {
            while (busy) yield return null;
            var a = Match.Bot();
            if (bj.phase == BJPhase.Play && i % 5 == 1 && bj.CanDouble(bj.ActiveHand, bj.Current)) a = new[] { "double" };
            Act(string.Join("|", a));
            yield return new WaitForSeconds(0.3f);
            if (i == 6) yield return Shot("5-bj-mise");
            if (i == 14) yield return Shot("6-bj-jeu");
        }
        while (busy) yield return null;
        yield return Shot("7-bj-fin-manche");
        SelectGame(GameId.Croque);
        StartGame();
        yield return new WaitForSeconds(3);
        yield return Shot("8-croque");
        Pause(); yield return new WaitForSecondsRealtime(1); yield return Shot("9-pause");
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
        bj = null;
        board.Build(r);
    }

    public void ApplySettings()
    {
        settings.Apply(cam, sun);
        Sound.I.Refresh();
        board.speed = settings.animSpeed;
        table.speed = settings.animSpeed;
        table.back = settings.cardBack;
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
    public bool MyTurn => Match != null && Match.Actor >= 0 && (!Online || Match.Actor == mySeat);
    public bool Idle => inGame && !busy && pending.Count == 0;
    public bool CanAct => inGame && !busy && !paused && Match != null && !Match.Finished && MyTurn && pending.Count == 0 && Time.time > waitUntil;

    public void StartGame()
    {
        var n = names.Select((s, i) => string.IsNullOrWhiteSpace(s) ? "Joueur " + (i + 1) : s.Trim()).ToList();
        StartGame(gameId, option, n, UnityEngine.Random.Range(0, int.MaxValue), avatars.ToList());
    }

    public void StartGame(GameId g, int opt, List<string> n, int seed, List<string> av)
    {
        StopAllCoroutines();
        pending.Clear();
        waitUntil = 0;
        gameId = g;
        option = opt;
        busy = false;
        paused = false;
        Time.timeScale = 1;
        inGame = true;
        if (g == GameId.Croque)
        {
            bj = null;
            rules = new Rules(opt == 0 ? Mode.Classique : Mode.Ameliore, n, seed);
            board.Build(rules);
            dist = 30;
            pitch = 34;
            ui.ShowHud();
            ui.Say($"Au tour de {rules.Current.name} !");
        }
        else
        {
            rules = null;
            bj = new Blackjack(n, opt, seed);
            table.Build(bj);
            table.CamHome(mySeat, out target, out yaw);
            dist = 2.9f;
            pitch = 52;
            ui.ShowHud();
            var first = bj.events.ToList();
            StartCoroutine(Run(table.Play(first, s => ui.Say(s)), null));
        }
        Casino(g == GameId.Blackjack);
        snapCam = true;
        Sound.I.Music(g == GameId.Blackjack ? "music_blackjack" : "music_game");
    }

    // Ambiance : prairie ensoleillee ou salle de casino fermee aux lumieres chaudes.
    void Casino(bool on)
    {
        sun.enabled = !on;
        RenderSettings.fog = !on;
        RenderSettings.ambientMode = on ? AmbientMode.Flat : AmbientMode.Trilight;
        RenderSettings.ambientLight = Board.Hex("4a3a33");
    }

    // Action choisie par le joueur local : jouee tout de suite, ou envoyee a l'hote en ligne.
    public void Act(string action)
    {
        if (!CanAct) return;
        if (Online) { waitUntil = Time.time + 2; net.Act(action); return; }
        Apply(action);
    }

    public void Draw() { if (rules != null && rules.drawn == null) Act("draw"); }
    public void Move(int rabbit) { if (rules != null && rules.CanMove(rabbit)) Act("move|" + rabbit); }

    public void Enqueue(string action) => pending.Enqueue(action);

    public void PlayerLeft(int seat)
    {
        string n = rules != null ? rules.players[seat].name : bj.players[seat].name;
        ui.Say($"{n} est parti : l'hôte joue pour lui.", 3.5f);
    }

    void Apply(string action)
    {
        var p = action.Split('|');
        if (rules != null)
        {
            if (p[0] == "draw") DoDraw(); else DoMove(int.Parse(p[1]));
            return;
        }
        if (!bj.TryApply(p)) return;
        var evs = bj.events.ToList();
        StartCoroutine(Run(table.Play(evs, s => ui.Say(s)), () => { if (bj.Finished) ui.ShowVictory(); }));
    }

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
        if (inGame && rules != null && !rules.Over && rules.drawn == null && !busy) ui.Say($"Au tour de {rules.Current.name} !");
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
        if (!inGame || paused || Match == null || Match.Finished) return;
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
        Casino(false);
        snapCam = true;
        pitch = 14;
        dist = 6.5f;
        ui.ShowTitle();
        Sound.I.Music("music_menu");
    }

    // --- Boucle ---------------------------------------------------------------------------
    void Update()
    {
        if (inGame && !busy && pending.Count > 0 && Match != null && !Match.Finished)
        {
            waitUntil = 0;
            applied++;
            Apply(pending.Dequeue().Substring(4));
            ui.Refresh();
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (ui.InSubMenu) ui.Back();
            else if (inGame && !paused) Pause();
        }
        if (!CanAct) return;
        if (rules != null)
        {
            if (Input.GetKeyDown(KeyCode.Space)) Draw();
            for (int r = 0; r < Rules.RabbitsPerPlayer; r++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + r) || Input.GetKeyDown(KeyCode.Keypad1 + r)) Move(r);
        }
        else if (bj.phase == BJPhase.Play)
        {
            var h = bj.ActiveHand;
            if (Input.GetKeyDown(KeyCode.H)) Act("hit");
            if (Input.GetKeyDown(KeyCode.S)) Act("stand");
            if (Input.GetKeyDown(KeyCode.D) && bj.CanDouble(h, bj.Current)) Act("double");
            if (Input.GetKeyDown(KeyCode.P) && bj.CanSplit(h, bj.Current)) Act("split");
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
        bool atTable = inGame && bj != null;
        if (inGame && !paused) dist = Mathf.Clamp(dist - Input.mouseScrollDelta.y * (atTable ? 0.6f : 2f), atTable ? 1.5f : 12, atTable ? 3.3f : 55);

        Vector3 want;
        if (atTable) want = table.Focus;
        else
        {
            var home = new Vector3(0, 2.5f, 0);
            want = inGame && settings.autoCam ? Vector3.Lerp(home, board.Focus, 0.55f) : home;
            if (!inGame) { want = hub.Focus; pitch = Mathf.Lerp(pitch, 14, dt); dist = Mathf.Lerp(dist, 6.5f, dt); }
        }
        target = snapCam ? want : Vector3.Lerp(target, want, 1 - Mathf.Exp(-2.5f * dt));
        snapCam = false;
        cam.transform.position = target + Quaternion.Euler(pitch, yaw, 0) * Vector3.back * dist;
        cam.transform.LookAt(target);
        if (dof) dof.active = !inGame || paused;
        if (atTable) ui.UpdateBubbles(table.Bubbles(), cam, table);
    }
}
