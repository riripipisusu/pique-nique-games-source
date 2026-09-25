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
    public Roulette rt;
    public Quiz qz;
    public IMatch Match => (IMatch)rules ?? (IMatch)bj ?? (IMatch)rt ?? qz;
    public bool busy;

    Board board;
    Table table;
    public RouletteView rview;
    public QuizView qview;
    Light quizLight;
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
        "Ami_Caramel", "Ami_Brune", "Ami_Platine", "Ami_Brun", "Ami_Roux",
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
        Application.runInBackground = true;
        settings = Settings.Load();
        myAvatar = PlayerPrefs.GetString("cc-avatar", "Casual_Male");

        cam = new GameObject("Camera").AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 50;
        cam.farClipPlane = 650;
        cam.gameObject.AddComponent<AudioListener>();

        sun = new GameObject("Soleil").AddComponent<Light>();
        sun.type = LightType.Directional;
        // Plein soleil de fin de matinee facon Meadow Forest : lumiere chaude, ciel Synty, brume legere.
        sun.color = Board.Hex("fff0d4");
        sun.intensity = 2.2f;
        sun.shadowStrength = 0.75f;
        sun.transform.rotation = Quaternion.Euler(42, -58, 0);
        RenderSettings.sun = sun;
        var sky = new Material(Synty.I.sky);
        sky.SetColor("_ColorTop", Board.Hex("3f86d6"));   // bleu franc au zenith, pas le bleu nuit d'origine
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Board.Hex("a9c8ef");
        RenderSettings.ambientEquatorColor = Board.Hex("b3c49a");
        RenderSettings.ambientGroundColor = Board.Hex("5a5a3c");
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;   // net au premier plan, collines de l'horizon fondues dans le ciel
        RenderSettings.fogColor = Board.Hex("d6e4ee");
        RenderSettings.fogStartDistance = 60;
        RenderSettings.fogEndDistance = 430;

        var vol = new GameObject("PostFX").AddComponent<Volume>();
        vol.isGlobal = true;
        vol.profile = Instantiate(Resources.Load<VolumeProfile>("Post"));
        vol.profile.TryGet(out dof);

        Sound.Create(settings);
        board = new GameObject("Board").AddComponent<Board>();
        table = new GameObject("Casino").AddComponent<Table>();
        hub = new GameObject("PiqueNique").AddComponent<Hub>();
        qview = new GameObject("PlateauQuiz").AddComponent<QuizView>();
        rview = table.gameObject.AddComponent<RouletteView>();
        rview.Init(table);
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
        if (Array.IndexOf(args, "-autotest") < 0 && Array.IndexOf(args, "-nettest") < 0)
            ui.StartCoroutine(Updater.Check(v =>
            {
                ui.ShowUpdate(v);
                if (Array.IndexOf(args, "-autoupdate") >= 0) ui.StartCoroutine(Updater.Install(_ => { }, Debug.LogError));
            }));
        int at = Array.IndexOf(args, "-autotest");
        if (at >= 0) ui.StartCoroutine(AutoTest(args[at + 1]));
        int nt = Array.IndexOf(args, "-nettest");
        if (nt >= 0) ui.StartCoroutine(NetTest(args[nt + 1] == "host", (GameId)Enum.Parse(typeof(GameId), args[nt + 2]), args[nt + 3]));
    }

    public static int DefaultOption(GameId g) => g == GameId.Croque ? 0 : g == GameId.Blackjack ? 10 : g == GameId.Roulette ? 20 : 0;

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
            if (qz != null && qz.phase == QPhase.Guess && MyTurn && CanAct)
                Act("guess|" + (UnityEngine.Random.value < 0.5f ? "mauvaise idee" : qz.Current.a[0]));   // quiz : propositions au hasard
            else if (MyTurn && CanAct) { var a = Match.Bot(); if (a != null) Act(string.Join("|", a)); }
            yield return new WaitForSeconds(0.2f);
        }
        while (busy || (pending.Count > 0 && applied < 30)) yield return null;
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, (host ? "host" : "join") + ".png"), tex.EncodeToPNG());
        var log = rules?.log ?? bj?.log ?? rt?.log ?? qz.log;
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, (host ? "host" : "join") + ".txt"),
            $"status={net.Status}\nseat={mySeat}\napplied={applied}\n" + string.Join("\n", log));
        yield return new WaitForSeconds(3);
        Application.Quit();
    }

    // Parcours automatique avec captures d'ecran, pour verifier une build sans interaction.
    IEnumerator AutoTest(string dir)
    {
        IEnumerator Shot(string n) { yield return new WaitForEndOfFrame(); var tex = ScreenCapture.CaptureScreenshotAsTexture(); System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, n + ".png"), tex.EncodeToPNG()); Destroy(tex); }
        IEnumerator Fps(string n) { int f = Time.frameCount; float t = Time.realtimeSinceStartup; yield return new WaitForSecondsRealtime(3); System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "fps.txt"), $"{n}: {(Time.frameCount - f) / (Time.realtimeSinceStartup - t):0} fps" + System.Environment.NewLine); }
        yield return new WaitForSeconds(6); yield return Shot("1-titre"); yield return Fps("titre");
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-quiz") >= 0)
        {
            // Partie hors ligne a 4 : on joue les propositions des autres a la main.
            // 10 joueurs : le maximum du quiz.
            names.Clear(); names.AddRange(new[] { "Anastasia", "Léo", "Camille", "Ana", "Hugo", "Inès", "Tom", "Lina", "Noé", "Zoé" });
            avatars.Clear(); avatars.AddRange(new[] { "Ami_Caramel", "Ami_Brun", "Ami_Platine", "Ami_Brune", "Ami_Roux", "Casual2_Female", "Cowboy_Male", "Witch", "Ninja_Male_Hair", "Chef_Female" });
            SelectGame(GameId.Quiz);
            option = 2;
            StartGame();
            yield return new WaitForSeconds(1.5f); yield return Shot("q1-intro");
            while (qz.phase != QPhase.Guess) yield return null;
            yield return new WaitForSeconds(1.2f);
            ApplyQuiz(new[] { "guess", "1", QuizElapsedMs.ToString(), "Portugal" });
            ApplyQuiz(new[] { "guess", "2", QuizElapsedMs.ToString(), "je sais pas" });
            yield return new WaitForSeconds(2.5f);
            ApplyQuiz(new[] { "guess", "3", QuizElapsedMs.ToString(), qz.Current.a[0] });
            yield return new WaitForSeconds(0.6f); yield return Shot("q2-flou-debut");
            yield return new WaitForSeconds(6); yield return Shot("q3-milieu");
            ApplyQuiz(new[] { "guess", "0", QuizElapsedMs.ToString(), qz.Current.a[0].ToLower() + "e" });   // faute de frappe acceptee
            while (qz.phase != QPhase.Reveal) yield return null;
            yield return new WaitForSeconds(0.8f); yield return Shot("q4-revelation");
            while (qz.phase != QPhase.Guess) yield return null;
            yield return new WaitForSeconds(3); yield return Shot("q5-image2");
            qz.players[1].score = 96;
            ApplyQuiz(new[] { "guess", "1", QuizElapsedMs.ToString(), qz.Current.a[0] });
            while (!qz.Finished) yield return null;
            yield return new WaitForSeconds(5); yield return Shot("q6-victoire");
            yield return Fps("quiz");
            Application.Quit();
            yield break;
        }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-casinotour") >= 0)
        {
            SelectGame(GameId.Roulette);
            StartGame();
            yield return new WaitForSeconds(2);
            var views = new (string n, Vector3 from, Vector3 at)[]
            {
                ("c1-blackjack", new Vector3(0, 2.1f, -3.2f), new Vector3(0, 1.4f, 6)),
                ("c2-large-gauche", new Vector3(-13.5f, 4.8f, -11), new Vector3(3, 1, 7)),
                ("c3-large-droite", new Vector3(13.5f, 4.8f, -11), new Vector3(-3, 1, 7)),
                ("c4-depuis-fond", new Vector3(0, 4.5f, 12.5f), new Vector3(0, 1, -6)),
            };
            foreach (var v in views)
            {
                Vector3 a = table.transform.TransformPoint(v.from), b = table.transform.TransformPoint(v.at);
                tour = new Pose(a, Quaternion.LookRotation(b - a));
                yield return null; yield return null;
                yield return Shot(v.n);
            }
            tour = null;
            yield return null;
            yield return Shot("c5-roulette");
            yield return Fps("casino");
            Application.Quit();
            yield break;
        }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-roulette") >= 0)
        {
            names.Clear(); names.AddRange(new[] { "Anastasia", "Léo", "Camille" });
            avatars.Clear(); avatars.AddRange(new[] { "Ami_Caramel", "Ami_Brun", "Ami_Platine" });
            SelectGame(GameId.Roulette);
            StartGame();
            yield return new WaitForSeconds(2);
            while (busy) yield return null;
            rview.Highlight("Q:13"); yield return null; yield return Shot("r1-tapis"); rview.Highlight(null);
            Act("bets|P:17:50;C:14-17:20;R:-:100;Q:0:10;D:1:30");
            yield return new WaitForSeconds(0.5f);
            Act("bets|N:-:50;S:3:20");
            yield return new WaitForSeconds(0.8f);
            yield return Shot("r1b-mises");
            Act("bets|");
            yield return new WaitForSeconds(1.8f); yield return Shot("r2a-dessus");
            yield return new WaitForSeconds(1.6f); yield return Shot("r2b-numeros");
            yield return new WaitForSeconds(0.9f); yield return Shot("r2c-rebond");
            while (busy) yield return null;
            yield return Shot("r2d-case");

            yield return Shot("r3-resultat");
            yield return Fps("roulette");
            for (int i = 0; i < 6; i++) { while (busy) yield return null; Act(string.Join("|", Match.Bot())); yield return new WaitForSeconds(0.3f); }
            while (busy) yield return null;
            yield return Shot("r4-historique");
            Application.Quit();
            yield break;
        }
        bool croqueOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "-croque") >= 0;
        if (!croqueOnly) {
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
        }
        SelectGame(GameId.Croque);
        StartGame();
        yield return new WaitForSeconds(3);
        yield return Shot("8-croque"); yield return Fps("croque");
        // Diagnostic de performance : on coupe un element a la fois.
        var ter = FindFirstObjectByType<Terrain>();
        float td = ter.treeDistance, dd = ter.detailObjectDistance;
        ter.treeDistance = 0; yield return Fps("sans arbres"); ter.treeDistance = td;
        ter.detailObjectDistance = 0; yield return Fps("sans herbe"); ter.detailObjectDistance = dd;
        sun.shadows = LightShadows.None; yield return Fps("sans ombres"); sun.shadows = LightShadows.Soft;
        var urp = (UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        int msaa = urp.msaaSampleCount; urp.msaaSampleCount = 1; yield return Fps("sans msaa"); urp.msaaSampleCount = msaa;
        urp.renderScale = 0.5f; yield return Fps("demi resolution"); urp.renderScale = 1;
        float lb = QualitySettings.lodBias; QualitySettings.lodBias = 0.4f; yield return Fps("detail bas"); QualitySettings.lodBias = lb;
        cam.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = false; yield return Fps("sans postfx"); cam.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "fps.txt"), $"ombres {urp.shadowDistance} m, cascades {urp.shadowCascadeCount}, lodbias {QualitySettings.lodBias}, msaa {urp.msaaSampleCount}, arbres {ter.terrainData.treeInstanceCount}, ecran {Screen.width}x{Screen.height}" + System.Environment.NewLine);
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
        rt = null;
        board.Build(r);
    }

    public void ApplySettings()
    {
        settings.Apply(cam, sun);
        Sound.I.Refresh();
        board.speed = settings.animSpeed;
        table.speed = settings.animSpeed;
        if (rview) rview.speed = settings.animSpeed;
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
    public bool MyTurn => Match != null && (Match.Actor == Quiz.Everyone ? qz != null && !qz.players[Math.Max(0, mySeat)].found
                                                    : Match.Actor >= 0 && (!Online || Match.Actor == mySeat));
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
        bj = null;
        rt = null;
        rules = null;
        qz = null;
        if (g == GameId.Quiz)
        {
            qz = new Quiz(n, opt, seed, Quiz.Pool);
            qview.Build(qz, av);
            qview.imageUrl = null;
            qview.Preload(qz.Next.u);
            quizPhaseStart = Time.time;
            ui.ShowHud();
            ui.Say("Tenez-vous prêts !");
        }
        else if (g == GameId.Croque)
        {
            rules = new Rules(opt == 0 ? Mode.Classique : Mode.Ameliore, n, seed);
            board.Build(rules);
            dist = 30;
            pitch = 34;
            ui.ShowHud();
            ui.Say($"Au tour de {rules.Current.name} !");
        }
        else if (g == GameId.Blackjack)
        {
            bj = new Blackjack(n, opt, seed);
            table.Build(bj);
            table.CamHome(mySeat, out target, out yaw);
            dist = 2.9f;
            pitch = 52;
            ui.ShowHud();
            var first = bj.events.ToList();
            StartCoroutine(Run(table.Play(first, s => ui.Say(s)), null));
        }
        else
        {
            rt = new Roulette(n, opt, seed);
            dist = 3.2f;
            pitch = 50;
            ui.ShowHud();
            var first = rt.events.ToList();
            StartCoroutine(Run(rview.Play(first, s => ui.Say(s)), null));
        }
        Casino(g != GameId.Croque);
        // Plateau TV : tout est eclaire de face, uniformement (lumiere directionnelle propre au quiz).
        if (g == GameId.Quiz) RenderSettings.ambientLight = Board.Hex("9a8f8a");
        if (!quizLight)
        {
            quizLight = new GameObject("LumiereQuiz").AddComponent<Light>();
            quizLight.type = LightType.Directional;
            quizLight.color = Board.Hex("fff1e0");
            quizLight.intensity = 1.5f;
            quizLight.shadows = LightShadows.Soft;
            quizLight.shadowStrength = 0.45f;
            quizLight.transform.rotation = Quaternion.Euler(32, 12, 0);   // de face, legerement de cote
        }
        quizLight.enabled = g == GameId.Quiz;
        snapCam = true;
        Sound.I.Music(g == GameId.Croque ? "music_game" : g == GameId.Quiz ? "music_menu" : "music_blackjack");
    }

    // Ambiance : prairie ensoleillee ou salle de casino fermee aux lumieres chaudes.
    void Casino(bool on)
    {
        sun.enabled = !on;
        RenderSettings.fog = !on;
        RenderSettings.ambientMode = on ? AmbientMode.Flat : AmbientMode.Trilight;
        RenderSettings.ambientLight = Board.Hex("52423a");
    }

    // Action choisie par le joueur local : jouee tout de suite, ou envoyee a l'hote en ligne.
    public void Act(string action)
    {
        if (!CanAct) return;
        if (Online) { if (qz == null) waitUntil = Time.time + 2; net.Act(action); return; }   // quiz : on peut enchainer les propositions
        Apply(action);
    }

    public void Draw() { if (rules != null && rules.drawn == null) Act("draw"); }
    public void Move(int rabbit) { if (rules != null && rules.CanMove(rabbit)) Act("move|" + rabbit); }

    public void Enqueue(string action) => pending.Enqueue(action);

    public void PlayerLeft(int seat)
    {
        string n = rules?.players[seat].name ?? bj?.players[seat].name ?? rt?.players[seat].name ?? qz.players[seat].name;
        ui.Say($"{n} est parti : l'hôte joue pour lui.", 3.5f);
    }

    // --- Quiz ------------------------------------------------------------------------------
    float quizPhaseStart;
    public int QuizElapsedMs => (int)((Time.time - quizPhaseStart) * 1000);

    // Pilotage des phases par l'hote (ou seul hors ligne) : intro 4 s, 20 s par image (fin anticipee
    // 1.5 s apres que tout le monde a trouve), 5 s de revelation. On attend que l'image soit arrivee.
    public string QuizTick(Quiz q)
    {
        float t = Time.time - quizPhaseStart;
        if (q.phase == QPhase.Reveal) return t > (q.round == 0 ? 4 : 5.5f) && qview.Ready(q.Next.u) ? "next" : null;
        if (q.phase != QPhase.Guess) return null;
        if (t * 1000 > Quiz.RoundMs) return "end";
        if (q.AllFound && t > q.players.Max(pl => pl.foundMs) / 1000f + 1.5f) return "end";
        return null;
    }

    void ApplyQuiz(string[] p)
    {
        if (p[0] == "guess" && p.Length == 2) p = new[] { "guess", Math.Max(0, mySeat).ToString(), QuizElapsedMs.ToString(), p[1] };   // hors ligne
        if (!qz.TryApply(p)) return;
        foreach (var e in qz.events)
            switch (e.type)
            {
                case QEv.Question:
                    quizPhaseStart = Time.time;
                    qview.imageUrl = qz.Current.u;
                    qview.pixelated = qz.Pixelated(qz.round);
                    qview.reveal = 0;
                    qview.Preload(qz.Next.u);
                    for (int i = 0; i < qz.players.Count; i++) qview.SetFound(i, false);
                    Sound.I.Play("open");
                    ui.QuizQuestion();
                    break;
                case QEv.Found:
                    qview.SetFound(e.seat, true);
                    Sound.I.Play(e.seat == mySeat || !Online ? "win" : "bj_chip1");
                    ui.QuizFound(e.seat, e.points);
                    break;
                case QEv.Wrong:
                    qview.Wrong(e.seat);
                    ui.QuizWrong(e.seat, e.text);
                    break;
                case QEv.Reveal:
                    quizPhaseStart = Time.time;
                    qview.reveal = 1;
                    Sound.I.Play("tick");
                    ui.QuizReveal();
                    break;
                case QEv.GameOver:
                    qview.reveal = 1;
                    var best = qz.players.Max(pl => pl.score);
                    foreach (var pl in qz.players.Where(pl => pl.score == best)) qview.Winner(pl.seat);
                    StartCoroutine(QuizEnd());
                    break;
            }
        ui.Refresh();
    }

    IEnumerator QuizEnd() { yield return new WaitForSeconds(4); ui.ShowVictory(); }

    void Apply(string action)
    {
        var p = action.Split('|');
        if (qz != null) { ApplyQuiz(p); return; }
        if (rules != null)
        {
            if (p[0] == "draw") DoDraw(); else DoMove(int.Parse(p[1]));
            return;
        }
        if (rt != null)
        {
            if (!rt.TryApply(p)) return;
            var revs = rt.events.ToList();
            StartCoroutine(Run(rview.Play(revs, s => ui.Say(s)), () => { if (rt.Finished) ui.ShowVictory(); }));
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
        if (quizLight) quizLight.enabled = false;
        snapCam = true;
        pitch = 14;
        dist = 6.5f;
        ui.ShowTitle();
        Sound.I.Music("music_menu");
    }

    // --- Boucle ---------------------------------------------------------------------------
    void Update()
    {
        if (inGame && qz != null)
        {
            // L'image se devoile en 18 s ; seul (hors ligne), c'est ce PC qui pilote les phases.
            if (qz.phase == QPhase.Guess) qview.reveal = Mathf.Clamp01((Time.time - quizPhaseStart) / 18f);
            if (!Online && Idle && !paused && !qz.Finished) { var tick = QuizTick(qz); if (tick != null) Apply(tick); }
        }
        if (inGame && !busy && pending.Count > 0 && Match != null && !Match.Finished)
        {
            waitUntil = 0;
            applied++;
            Apply(pending.Dequeue().Substring(4));
            ui.Refresh();
        }
        if (!Focused) return;
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
        else if (bj != null && bj.phase == BJPhase.Play)
        {
            var h = bj.ActiveHand;
            if (Input.GetKeyDown(KeyCode.H)) Act("hit");
            if (Input.GetKeyDown(KeyCode.S)) Act("stand");
            if (Input.GetKeyDown(KeyCode.D) && bj.CanDouble(h, bj.Current)) Act("double");
            if (Input.GetKeyDown(KeyCode.P) && bj.CanSplit(h, bj.Current)) Act("split");
        }
    }

    // Test automatique : la fenetre prend le focus au lancement, mais la souris appartient a l'utilisatrice.
    Pose? tour;   // test automatique : camera placee a la main
    static readonly bool Testing = Array.IndexOf(Environment.GetCommandLineArgs(), "-autotest") >= 0;
    static bool Focused => Application.isFocused && !Testing;

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime, sens = settings.camSens;
        if (!inGame) yaw += 3.5f * dt;
        // Fenetre sans le focus (autre jeu, autre ecran) : la souris et le clavier ne sont pas pour nous.
        bool focus = Focused;
        if (tour.HasValue) { cam.transform.SetPositionAndRotation(tour.Value.position, tour.Value.rotation); return; }
        if (inGame && qz != null)
        {
            var qp = qview.CamPose;
            cam.transform.SetPositionAndRotation(qp.position, qp.rotation);
            if (dof) dof.active = paused;
            ui.UpdateQuiz(cam);
            return;
        }
        // Roulette : camera fixe a la place du joueur, plan de dessus pendant le lancer ; les mises se posent au clic sur le tapis.
        if (inGame && rt != null)
        {
            var pose = rview.TopView ? rview.TopPose : rview.SeatPose;
            cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
            if (dof) dof.active = paused;
            ui.RouletteMouse(cam, focus && !paused);
            return;
        }
        if (focus && Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * 4 * sens;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3 * sens, 10, 75);
        }
        if (focus && Input.GetKey(KeyCode.Q)) yaw += 70 * dt * sens;
        if (focus && Input.GetKey(KeyCode.E)) yaw -= 70 * dt * sens;
        bool atTable = inGame && (bj != null || rt != null);
        if (focus && inGame && !paused) dist = Mathf.Clamp(dist - Input.mouseScrollDelta.y * (atTable ? 0.6f : 2f), atTable ? 1.5f : 12, atTable ? 3.3f : 55);

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
        if (inGame && bj != null) ui.UpdateBubbles(table.Bubbles(), cam, table);
    }
}
