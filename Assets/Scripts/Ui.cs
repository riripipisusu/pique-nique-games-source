using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Tous les ecrans (UI Toolkit, construits en code, styles dans Resources/UI/Menu.uss).
public partial class Ui : MonoBehaviour
{
    Game game;
    VisualElement root, title, games, setup, picker, settingsScreen, rulesScreen, hud, pause, victory, onlineScreen, lobbyScreen;
    VisualElement current;
    readonly Stack<VisualElement> history = new Stack<VisualElement>();

    public void Init(Game g, UIDocument doc)
    {
        game = g;
        root = doc.rootVisualElement;
        root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Menu"));
        root.AddToClassList("root");
        root.pickingMode = PickingMode.Ignore;
        BuildTitle();
        BuildGames();
        BuildSetup();
        BuildSettings();
        BuildRules();
        BuildHud();
        BuildPause();
        BuildVictory();
        BuildOnline();
        BuildLobby();
        BuildPicker();
    }

    // --- Outils ---------------------------------------------------------------------
    static T Add<T>(VisualElement parent, T e, params string[] cls) where T : VisualElement
    {
        foreach (var c in cls) e.AddToClassList(c);
        parent.Add(e);
        return e;
    }
    static VisualElement Div(VisualElement p, params string[] c) => Add(p, new VisualElement(), c);
    static Label Text(VisualElement p, string t, params string[] c) => Add(p, new Label(t), c);

    // Boutons "gel" du design : corps sombre (levre), face coloree avec reflet, texte a contour.
    // Couleurs : (orange par defaut) green, ghost (creme), blue, red. Tailles : small, lg, round.
    static readonly string[] PlainButtons = { "chip-btn", "round-act", "qz-choice" };

    Button Btn(VisualElement p, string text, Action onClick, params string[] c)
    {
        var b = new Button(() => { Sound.I.UI("click"); onClick(); });
        b.AddToClassList("btn");
        b.RegisterCallback<MouseEnterEvent>(_ => { if (b.enabledSelf) Sound.I.UI("hover"); });
        if (c.Any(PlainButtons.Contains)) b.text = text;
        else
        {
            b.AddToClassList("gl");
            var face = Div(b, "gl-face");
            Div(face, "gl-shine");
            Text(face, text, "gl-label");
            face.pickingMode = PickingMode.Ignore;
            foreach (var e in face.Children()) e.pickingMode = PickingMode.Ignore;
        }
        return Add(p, b, c);
    }

    static VisualElement Face(Button b) => b.Q(className: "gl-face");
    static string Label(Button b) => b.Q<Label>(className: "gl-label")?.text ?? b.text;

    // Icone (Resources/UI/Icons, traits blancs) devant le texte d'un bouton.
    static Button Ico(Button b, string icon)
    {
        var face = Face(b);
        var i = face.Q(className: "gl-ico");
        if (i == null) { i = new VisualElement { pickingMode = PickingMode.Ignore }; i.AddToClassList("gl-ico"); face.Insert(1, i); }
        i.style.backgroundImage = Resources.Load<Texture2D>("UI/Icons/" + icon);
        return b;
    }

    // Panneau du design : cadre vert a contour sombre autour d'une plaque creme (retournee).
    static VisualElement Panel(VisualElement p, params string[] c)
    {
        var frame = Div(p, "frame");
        frame.pickingMode = PickingMode.Ignore;
        return Div(frame, c.Prepend("panel").ToArray());
    }

    static void Ring(VisualElement e, Color c) => e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = c;

    // Bouton rond pour couper / remettre le son.
    readonly List<Button> soundBtns = new List<Button>();
    void SoundBtn(VisualElement p)
    {
        Button b = null;
        b = Btn(p, "", () => { game.settings.mute = !game.settings.mute; game.ApplySettings(); RefreshSoundBtns(); }, "ghost", "round");
        soundBtns.Add(b);
        RefreshSoundBtns();
    }
    void RefreshSoundBtns() { foreach (var b in soundBtns) Ico(b, game.settings.mute ? "mute" : "sound"); }

    // Portrait cliquable d'un personnage.
    VisualElement Portrait(VisualElement p, string avatar, Action onClick, float size = 64)
    {
        VisualElement e = onClick != null ? new Button(() => { Sound.I.UI("click"); onClick(); }) : new VisualElement();
        e.AddToClassList("portrait");
        e.style.width = size;
        e.style.height = size;
        e.style.backgroundImage = Resources.Load<Texture2D>("Portraits/" + avatar);
        p.Add(e);
        return e;
    }

    VisualElement Screen(params string[] c)
    {
        var s = Div(root, "screen", "hidden");
        foreach (var x in c) s.AddToClassList(x);
        s.pickingMode = PickingMode.Ignore;
        return s;
    }

    void Show(VisualElement s)
    {
        s.BringToFront();
        s.RemoveFromClassList("hidden");
        s.RemoveFromClassList("in");
        s.schedule.Execute(() => s.AddToClassList("in")).StartingIn(20);
    }

    // Test : chaque bouton visible du titre est-il bien celui qui recoit un clic en son centre ?
    public string PickReport()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var b in title.Query<Button>().ToList())
        {
            if (b.resolvedStyle.display == DisplayStyle.None || b.worldBound.width < 1) continue;
            var hit = root.panel.Pick(b.worldBound.center);
            bool ok = hit == b || (hit != null && b.Contains(hit));
            sb.Append($"[{Label(b)}:{(ok ? "ok" : "BLOQUE par " + (hit?.name ?? hit?.GetType().Name ?? "rien"))}] ");
        }
        return sb.ToString();
    }

    static void Hide(VisualElement s) { s.AddToClassList("hidden"); s.RemoveFromClassList("in"); }

    VisualElement[] All => new[] { title, games, setup, picker, settingsScreen, rulesScreen, hud, pause, victory, onlineScreen, lobbyScreen };

    // Navigation entre ecrans de menu, avec retour arriere.
    void Go(VisualElement s, bool remember = true)
    {
        if (current != null)
        {
            if (remember) history.Push(current);
            Hide(current);
        }
        current = s;
        Show(s);
    }

    public void Back()
    {
        if (history.Count == 0) return;
        Sound.I.UI("back");
        if (current == lobbyScreen) game.net.Leave();
        Go(history.Pop(), false);
        if (current == hud) game.Resume();
    }

    public bool InSubMenu => history.Count > 0;

    public void OpenForTest(string screen)
    {
        if (screen == "games") Go(games);
        else if (screen == "setup") { RefreshSetup(); Go(setup); }
        else if (screen == "avatar") OpenPicker(a => { });
        else if (screen == "settings") { SelectTab(1); Go(settingsScreen); }
        else if (screen == "online") { RefreshOnline(); Go(onlineScreen); }
    }

    public void ShowTitle()
    {
        history.Clear();
        foreach (var s in All) Hide(s);
        current = null;
        RefreshProfile();
        RefreshSoundBtns();
        Go(title, false);
    }

    // --- Ecran titre ----------------------------------------------------------------
    VisualElement profilePortrait;
    Label profileName;

    void BuildTitle()
    {
        title = Screen("title-screen");
        var logo = Div(title, "logo-box");
        Text(logo, "Pique-Nique's", "logo");
        Text(logo, "Games", "logo", "logo2");
        Text(title, "Des jeux de société à partager entre amis", "tagline");
        var col = Div(title, "menu-col");
        Ico(Btn(col, "Jouer", () => { RefreshGames(); Go(games); }, "lg"), "play");
        var row = Div(col, "menu-row");
        Ico(Btn(row, "Paramètres", () => { SelectTab(tab); Go(settingsScreen); }, "blue"), "gear");
        Ico(Btn(row, "Quitter", Application.Quit, "red"), "exit");

        // Carte "profil" : son personnage (clic = changer), et son prenom.
        var profile = Btn(title, "", () => OpenPicker(a => { game.SetMyAvatar(a); RefreshProfile(); }), "ghost", "profile");
        var face = Face(profile);
        face.Q<Label>().RemoveFromHierarchy();
        profilePortrait = Div(face, "portrait");
        profilePortrait.style.width = profilePortrait.style.height = 70;
        var txt = Div(face);
        Text(txt, "Ton personnage", "profile-sub");
        profileName = Text(txt, "", "profile-name");
        foreach (var e in face.Query().ToList()) e.pickingMode = PickingMode.Ignore;

        var corner = Div(title, "corner");
        SoundBtn(corner);
        Text(title, "Version " + Application.version, "version");
        Text(title, "Décors Synty Studios · Modèles Kenney & Quaternius (CC0)  ·  Questions : OpenQuizzDB (CC BY-SA) · Tenna : rig de ThatAverageJoe · Sons de roulette : Pixabay · Musiques : MMAudio, Geoff Harvey (Pixabay), « A Conversation with Saul » de Matthew Pablo (CC-BY 3.0)", "credits");
        updateCard = Div(title, "panel", "update-card");
        updateCard.style.display = DisplayStyle.None;
        updateText = Text(updateCard, "", "p");
        updateBtn = Btn(updateCard, "Mettre à jour", () =>
        {
            updateBtn.SetEnabled(false);
            StartCoroutine(Updater.Install(p => updateText.text = $"Téléchargement... {p * 100:0} %", err => { updateText.text = err; updateBtn.SetEnabled(true); }));
        }, "green", "small");
        logo.schedule.Execute(() =>
        {
            float t = Time.unscaledTime;
            logo.style.rotate = new Rotate(Angle.Degrees(Mathf.Sin(t * 1.3f) * 1.5f));
            logo.style.translate = new Translate(0, Mathf.Sin(t * 2.1f) * 8f);
        }).Every(16);
    }

    VisualElement updateCard;
    Label updateText;
    Button updateBtn;

    void RefreshProfile()
    {
        profilePortrait.style.backgroundImage = Resources.Load<Texture2D>("Portraits/" + game.myAvatar);
        profileName.text = PlayerPrefs.GetString("cc-name", "Toi");
    }

    public void ShowUpdate(string version)
    {
        updateText.text = $"Nouvelle version {version} disponible !";
        updateCard.style.display = DisplayStyle.Flex;
    }

    // --- Choix du jeu -----------------------------------------------------------------
    // Fiche de chaque jeu : illustration (Resources/UI/Games), famille (0 societe, 1 casino, 2 TV Time), textes.
    static readonly Dictionary<GameId, (string art, int cat, string meta, string desc)> GameInfo = new Dictionary<GameId, (string, int, string, string)>
    {
        [GameId.Croque] = ("croque", 0, "2 à 4 joueurs · Plateau", "Grimpe la montagne jusqu'au potager... mais gare aux trous quand la carotte tourne !"),
        [GameId.Blackjack] = ("blackjack", 1, "2 à 4 joueurs · Cartes", "Approche-toi de 21 sans dépasser et bats le croupier."),
        [GameId.Roulette] = ("roulette", 1, "1 à 4 joueurs · Casino", "Pleins, chevaux, carrés, rouge ou noir... Le plus riche gagne."),
        [GameId.Quiz] = ("quiz", 2, "1 à 10 joueurs · Images", "Une image floutée se dévoile : films, jeux, drapeaux, pochettes..."),
        [GameId.Trivia] = ("trivia", 2, "1 à 10 joueurs · Culture G", "Tenna pose les questions, en QCM ou en réponse libre."),
    };

    int gameFilter = -1;
    VisualElement gameRow;
    Label gameCount;
    readonly List<Button> gameTabs = new List<Button>();

    void BuildGames()
    {
        games = Screen();
        var panel = Panel(games);
        panel.style.width = 1760;
        Text(panel, "À quoi on joue ?", "panel-title");
        var top = Div(panel, "row", "spread");
        var tabsRow = Div(top, "row");
        string[] cats = { "Tous", "Jeux de société", "Casino", "TV Time" };
        for (int i = 0; i < cats.Length; i++)
        {
            int k = i - 1;
            gameTabs.Add(Btn(tabsRow, cats[i], () => { gameFilter = k; RefreshGames(); }, "ghost", "small", "filter"));
        }
        gameCount = Text(top, "", "muted");
        gameRow = Div(panel, "row", "game-row");
        foreach (var g in GameInfo.Keys) GameCard(gameRow, g);
        Ico(Btn(panel, "Retour", Back, "ghost", "small"), "back").style.alignSelf = Align.FlexStart;
        RefreshGames();
    }

    void RefreshGames()
    {
        int n = 0;
        foreach (var card in gameRow.Children())
        {
            bool on = gameFilter < 0 || GameInfo[(GameId)card.userData].cat == gameFilter;
            card.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (on) n++;
        }
        for (int i = 0; i < gameTabs.Count; i++)
        {
            gameTabs[i].EnableInClassList("blue", i - 1 == gameFilter);
            gameTabs[i].EnableInClassList("ghost", i - 1 != gameFilter);
        }
        gameCount.text = n + (n > 1 ? " jeux" : " jeu");
    }

    void GameCard(VisualElement parent, GameId g)
    {
        var info = GameInfo[g];
        var b = new Button(() => { Sound.I.UI("click"); game.SelectGame(g); RefreshSetup(); Go(setup); }) { userData = g };
        b.AddToClassList("game-card");
        b.RegisterCallback<MouseEnterEvent>(_ => Sound.I.UI("hover"));
        Div(b, "game-art").style.backgroundImage = Resources.Load<Texture2D>("UI/Games/" + info.art);
        var txt = Div(b, "game-txt");
        Text(txt, Games.Name(g), "mode-name");
        Text(txt, info.meta, "game-meta");
        Text(txt, info.desc, "mode-desc");
        parent.Add(b);
    }

    // --- Preparation d'une partie (options + joueurs) -------------------------------------
    VisualElement setupOptions, playerList, localTitle, localBtn, botsRow;
    Label botsLabel;
    Label setupTitle;
    Button addPlayer;

    void BuildSetup()
    {
        setup = Screen();
        var panel = Panel(setup);
        panel.style.width = 1240;
        setupTitle = Text(panel, "", "panel-title");
        Text(panel, "Options", "h2");
        setupOptions = Div(panel, "row");
        localTitle = Text(panel, "Joueurs sur ce PC (chacun son tour)", "h2");
        playerList = Div(panel);
        addPlayer = Ico(Btn(panel, "Ajouter un joueur", () =>
        {
            game.names.Add("Joueur " + (game.names.Count + 1));
            game.avatars.Add(Game.Characters[(game.names.Count * 7) % Game.Characters.Length]);
            RefreshSetup();
        }, "ghost", "small"), "plus");
        addPlayer.style.alignSelf = Align.FlexStart;
        // Quiz : partie hors ligne contre des bots (pour s'entrainer ou tester sans second PC).
        botsRow = Div(panel, "row", "setting");
        Text(botsRow, "Hors ligne contre des bots", "setting-label").style.flexGrow = 1;
        Btn(botsRow, "−", () => { game.quizBots = Mathf.Max(1, game.quizBots - 1); RefreshSetup(); }, "ghost", "small", "square");
        botsLabel = Text(botsRow, "", "bet-label");
        Btn(botsRow, "+", () => { game.quizBots = Mathf.Min(Quiz.MaxPlayers - 1, game.quizBots + 1); RefreshSetup(); }, "ghost", "small", "square");
        Ico(Btn(botsRow, "Jouer contre les bots", () => game.StartQuizWithBots(), "green", "small"), "play").style.marginLeft = 20;
        var bottom = Div(panel, "row", "spread", "bottom-row");
        Ico(Btn(bottom, "Retour", Back, "ghost", "small"), "back");
        Ico(Btn(bottom, "Règles", () => { RefreshRules(); Go(rulesScreen); }, "ghost", "small"), "book");
        Div(bottom, "grow");
        Ico(Btn(bottom, "Jouer en ligne", () => { RefreshOnline(); Go(onlineScreen); }), "globe");
        localBtn = Ico(Btn(bottom, "Jouer sur ce PC", () => game.StartGame(), "green"), "monitor");
    }

    void OptionCards(VisualElement parent, int current, Action<int> pick)
    {
        parent.Clear();
        var opts = game.gameId == GameId.Croque
            ? new[] { (0, "Classique", "19 cases en spirale. La carotte ouvre 1 à 3 trous au hasard."), (1, "Amélioré", "25 cases, trous selon un cycle secret à deviner.") }
            : game.gameId == GameId.Trivia
            ? new[] { (0, "QCM", "4 propositions, une seule réponse : la bonne et vite !"), (1, "Réponse libre", "Tape la réponse toi-même, autant d'essais que tu veux.") }
            : game.gameId == GameId.Quiz
            ? new[] { (0, "Flou", "L'image est floue puis se précise."), (1, "Pixelisé", "De gros pixels qui s'affinent."), (2, "Mélangé", "Flou ou pixels, au hasard à chaque image.") }
            : game.gameId == GameId.Blackjack
            ? new[] { (5, "Partie rapide", "5 manches"), (10, "Partie normale", "10 manches"), (20, "Longue soirée", "20 manches") }
            : new[] { (10, "Partie rapide", "10 coups"), (20, "Partie normale", "20 coups"), (40, "Longue soirée", "40 coups") };
        foreach (var (value, name, desc) in opts)
        {
            var b = new Button(() => { Sound.I.UI("tick"); pick(value); });
            b.AddToClassList("mode-card");
            b.EnableInClassList("selected", value == current);
            Text(b, name, "mode-name");
            Text(b, desc, "mode-desc");
            parent.Add(b);
        }
    }

    void RefreshSetup()
    {
        setupTitle.text = Games.Name(game.gameId);
        OptionCards(setupOptions, game.option, v => { game.option = v; RefreshSetup(); });
        while (game.avatars.Count < game.names.Count) game.avatars.Add("Casual_Male");
        playerList.Clear();
        for (int i = 0; i < game.names.Count; i++)
        {
            int idx = i;
            var row = Div(playerList, "player-row");
            Ring(Portrait(row, game.avatars[i], () => OpenPicker(a => { game.avatars[idx] = a; RefreshSetup(); }), 60), Board.Colors[i]);
            var field = Add(row, new TextField { value = game.names[i], maxLength = 16 }, "name-field");
            field.RegisterValueChangedCallback(e => game.names[idx] = e.newValue);
            var rm = Btn(row, "Retirer", () => { game.names.RemoveAt(idx); game.avatars.RemoveAt(idx); RefreshSetup(); }, "red", "small");
            rm.style.marginLeft = 12;
            rm.SetEnabled(game.names.Count > 2);
        }
        addPlayer.style.display = game.names.Count < Rules.MaxPlayers ? DisplayStyle.Flex : DisplayStyle.None;
        // Le quiz se joue en ligne (chacun tape sur son PC) : pas de joueurs locaux.
        bool local = !Games.TvTime(game.gameId);
        foreach (var e in new[] { localTitle, playerList, localBtn }) e.style.display = local ? DisplayStyle.Flex : DisplayStyle.None;
        if (!local) addPlayer.style.display = DisplayStyle.None;
        botsRow.style.display = local ? DisplayStyle.None : DisplayStyle.Flex;
        botsLabel.text = game.quizBots + (game.quizBots > 1 ? " bots" : " bot");
    }

    // --- Choix du personnage ---------------------------------------------------------------
    Action<string> onPick;

    void BuildPicker()
    {
        picker = Screen("dim");
        var panel = Panel(picker, "settings-panel");
        Text(panel, "Choisis ton personnage", "panel-title");
        var sv = Add(panel, new ScrollView(), "settings-scroll");
        var grid = Div(sv, "avatar-grid");
        foreach (var c in Game.Characters)
        {
            var cell = Div(grid, "avatar-cell");
            Portrait(cell, c, () => { onPick?.Invoke(c); Back(); }, 120);
            Text(cell, Pretty(c), "avatar-name");
        }
        var bottom = Div(panel, "row");
        bottom.style.justifyContent = Justify.Center;
        Ico(Btn(bottom, "Annuler", Back, "ghost", "small"), "back");
    }

    static string Pretty(string c) => c.Replace("_Male", " (H)").Replace("_Female", " (F)").Replace("_", " ").Replace("Casual", "Décontracté").Replace("OldClassy", "Chic").Replace("Worker", "Ouvrier").Replace("Suit", "Costume").Replace("Chef", "Chef").Replace("Doctor", "Docteur").Replace("Young", "jeune").Replace("Old", "âgé").Replace("Knight", "Chevalier").Replace("Golden", "doré").Replace("Soldier", "Soldat").Replace("BlueSoldier", "Soldat bleu").Replace("Wizard", "Sorcier").Replace("Witch", "Sorcière").Replace("Zombie", "Zombie").Replace("Pirate", "Pirate").Replace("Hair", "coiffé").Replace("Hat", "à toque").Replace("Bald", "chauve").Replace("Sand", "du désert").Replace("Goblin", "Gobelin").Replace("Elf", "Elfe");

    void OpenPicker(Action<string> pick) { onPick = pick; Go(picker); }

    // --- Parametres -------------------------------------------------------------------
    VisualElement settingsBody;
    readonly List<Button> tabs = new List<Button>();
    int tab;

    void BuildSettings()
    {
        settingsScreen = Screen("dim");
        var panel = Panel(settingsScreen, "settings-panel");
        Text(panel, "Paramètres", "panel-title");
        var bar = Div(panel, "tabs");
        string[] names = { "Graphismes", "Audio", "Jeu", "Commandes" };
        for (int i = 0; i < names.Length; i++)
        {
            int k = i;
            var t = new Button(() => { Sound.I.UI("tick"); SelectTab(k); }) { text = names[i] };
            t.AddToClassList("tab");
            bar.Add(t);
            tabs.Add(t);
        }
        settingsBody = Add(panel, new ScrollView(ScrollViewMode.Vertical), "settings-scroll");
        var bottom = Div(panel, "row", "spread");
        Ico(Btn(bottom, "Retour", Back, "ghost", "small"), "back");
        Btn(bottom, "Par défaut", () => { game.ResetSettings(); SelectTab(tab); }, "ghost", "small");
    }

    static readonly string[] Backs = { "back_red", "back_blue", "back_teal", "back_purple", "back_black", "back_pn" };

    void SelectTab(int k)
    {
        tab = k;
        for (int i = 0; i < tabs.Count; i++) tabs[i].EnableInClassList("selected", i == k);
        settingsBody.Clear();
        var s = game.settings;
        switch (k)
        {
            case 0:
                Drop("Qualité", new[] { "Basse", "Moyenne", "Haute", "Ultra", "Personnalisée" }, s.quality, v => { s.Preset(v); SelectTab(0); });
                var res = UnityEngine.Screen.resolutions.Select(r => (r.width, r.height)).Distinct().ToList();
                if (!res.Contains((s.resW, s.resH))) res.Add((s.resW, s.resH));
                Drop("Résolution", res.Select(r => $"{r.width} × {r.height}").ToArray(), res.IndexOf((s.resW, s.resH)), v => { s.resW = res[v].width; s.resH = res[v].height; });
                Drop("Affichage", new[] { "Plein écran (fenêtré)", "Fenêtré", "Plein écran exclusif" }, s.display, v => s.display = v);
                Check("Synchronisation verticale", s.vsync, v => { s.vsync = v; SelectTab(0); });
                var fps = Drop("Images par seconde max.", new[] { "30", "60", "120", "144", "Illimité" }, s.fpsCap, v => s.fpsCap = v);
                fps.SetEnabled(!s.vsync);
                Drop("Ombres", new[] { "Désactivées", "Basses", "Hautes", "Ultra" }, s.shadows, v => { s.shadows = v; s.quality = 4; SelectTab(0); });
                Drop("Détail du décor", new[] { "Bas", "Moyen", "Haut", "Ultra" }, s.detail, v => { s.detail = v; s.quality = 4; SelectTab(0); });
                Drop("Anticrénelage", new[] { "Aucun", "MSAA 2x", "MSAA 4x", "MSAA 8x" }, s.aa, v => { s.aa = v; s.quality = 4; SelectTab(0); });
                Check("Effets visuels (bloom, couleurs, flou)", s.post, v => { s.post = v; s.quality = 4; SelectTab(0); });
                Range("Échelle de rendu", 0.5f, 1f, s.renderScale, v => { s.renderScale = v; s.quality = 4; }, v => Mathf.RoundToInt(v * 100) + " %");
                break;
            case 1:
                Range("Volume général", 0, 1, s.master, v => s.master = v, Pct);
                Range("Musique", 0, 1, s.music, v => s.music = v, Pct);
                Range("Effets sonores", 0, 1, s.sfx, v => s.sfx = v, Pct);
                Range("Interface", 0, 1, s.ui, v => s.ui = v, Pct);
                Check("Couper le son", s.mute, v => s.mute = v);
                break;
            case 2:
                Range("Vitesse des animations", 0.5f, 2.5f, s.animSpeed, v => s.animSpeed = v, v => v.ToString("0.0") + "x");
                Range("Sensibilité de la caméra", 0.3f, 2.5f, s.camSens, v => s.camSens = v, v => v.ToString("0.0") + "x");
                Check("Caméra qui suit l'action (Croque-Carotte)", s.autoCam, v => s.autoCam = v);
                Check("Numéros sur les cases (Croque-Carotte)", s.tileNumbers, v => s.tileNumbers = v);
                Drop("Dos des cartes", new[] { "Rouge", "Bleu", "Vert", "Violet", "Noir", "Pique-Nique" }, Array.IndexOf(Backs, s.cardBack), v => s.cardBack = Backs[v]);
                break;
            default:
                Text(settingsBody, "Croque-Carotte", "h2");
                Info("Piocher une carte", "Espace");
                Info("Choisir un lapin", "1  ·  2  ·  3");
                Text(settingsBody, "Blackjack", "h2");
                Info("Tirer / Rester", "H  ·  S");
                Info("Doubler / Séparer", "D  ·  P");
                Text(settingsBody, "Partout", "h2");
                Info("Tourner la caméra", "Clic droit + glisser  ·  Q / E");
                Info("Zoomer", "Molette");
                Info("Pause", "Échap");
                break;
        }
    }

    static string Pct(float v) => Mathf.RoundToInt(v * 100) + " %";

    VisualElement Setting(string label)
    {
        var row = Div(settingsBody, "setting");
        Text(row, label, "setting-label");
        return row;
    }

    DropdownField Drop(string label, string[] choices, int index, Action<int> set)
    {
        var d = Add(Setting(label), new DropdownField(choices.ToList(), Mathf.Clamp(index, 0, choices.Length - 1)), "setting-field");
        d.RegisterValueChangedCallback(_ => { Sound.I.UI("tick"); set(d.index); game.ApplySettings(); });
        return d;
    }

    void Check(string label, bool value, Action<bool> set)
    {
        var t = Add(Setting(label), new Toggle { value = value });
        t.RegisterValueChangedCallback(e => { Sound.I.UI("tick"); set(e.newValue); game.ApplySettings(); });
    }

    void Range(string label, float min, float max, float value, Action<float> set, Func<float, string> fmt)
    {
        var row = Setting(label);
        var box = Div(row, "row", "setting-field");
        var s = Add(box, new Slider(min, max) { value = value });
        var v = Text(box, fmt(value), "value");
        s.RegisterValueChangedCallback(e => { v.text = fmt(e.newValue); set(e.newValue); game.ApplySettings(); });
    }

    void Info(string label, string keys)
    {
        var row = Setting(label);
        Text(row, keys, "setting-label").style.color = new Color(0.94f, 0.54f, 0.14f);
    }

    // --- Regles -----------------------------------------------------------------------
    VisualElement rulesBody;
    Label rulesTitle;

    void BuildRules()
    {
        rulesScreen = Screen("dim");
        var panel = Panel(rulesScreen, "settings-panel");
        rulesTitle = Text(panel, "", "panel-title");
        rulesBody = Add(panel, new ScrollView(), "settings-scroll");
        var bottom = Div(panel, "row");
        bottom.style.justifyContent = Justify.Center;
        Ico(Btn(bottom, "Retour", Back, "ghost", "small"), "back");
    }

    void RefreshRules()
    {
        rulesTitle.text = "Règles : " + Games.Name(game.gameId);
        rulesBody.Clear();
        void S(string h, string p) { Text(rulesBody, h, "h2"); Text(rulesBody, p, "p"); }
        if (game.gameId == GameId.Croque)
        {
            S("Le but", "Chaque joueur a 3 lapins. Le premier à les amener tous les trois au potager, en haut de la montagne, gagne la partie.");
            S("À ton tour", "Pioche une carte. Une carte chiffrée (1, 2 ou 3) fait avancer le lapin de ton choix d'autant de cases. Une case ne porte qu'un lapin : si elle est prise, on saute jusqu'à la suivante libre.");
            S("La carte Carotte", "Elle fait tourner la grosse carotte du sommet... et des trous s'ouvrent sous certaines cases ! Les lapins qui s'y trouvent dégringolent jusqu'à l'enclos de départ. Les trous restent ouverts jusqu'au prochain tour de carotte : un lapin qui s'arrête dessus tombe aussi !");
            S("Mode Classique", "19 cases en spirale. Chaque tour de carotte ouvre 1 à 3 trous tirés au hasard, n'importe où à partir de la case 3.");
            S("Mode Amélioré", "25 cases sur deux anneaux. Les trous s'ouvrent un par un, selon un cycle de 25 crans tiré au début de la partie mais qui ne change plus. La carte Double carotte avance de deux crans. Quand un cran est connu, la case menacée s'allume en rouge. Observe, déduis, et place tes lapins là où ça ne tombera pas ! (D'après la vidéo d'Hydrios « Il manque 2 cases à Croque-Carotte ».)");
        }
        else if (game.gameId == GameId.Trivia)
        {
            S("Le but", "Tenna pose des questions de culture générale : cinéma, histoire, sciences, sport, musique, géographie... Le premier à 100 points gagne.");
            S("QCM", "Quatre propositions : clique sur la tienne ou appuie sur 1, 2, 3 ou 4. Une seule réponse par question : si tu te trompes, tu attends la suivante.");
            S("Réponse libre", "Tape ta réponse puis Entrée, autant de fois que tu veux pendant les 20 secondes. Les petites fautes de frappe sont acceptées, et tout le monde voit tes mauvaises réponses !");
            S("Les points", "Plus tu réponds vite, plus tu gagnes : 10 points tout de suite, 3 à la dernière seconde, et 2 de bonus pour le premier. Après chaque question, Tenna donne la réponse et une petite anecdote.");
            S("Les questions", "Questions issues d'OpenQuizzDB (openquizzdb.org), sous licence libre CC BY-SA.");
        }
        else if (game.gameId == GameId.Quiz)
        {
            S("Le but", "Une image apparaît sur l'écran géant, floutée ou pixelisée, et se précise peu à peu. Devine ce que c'est avant les autres ! Le premier à 100 points gagne.");
            S("Les catégories", "Films et séries (des scènes, pas les affiches), anime, jeux vidéo, pochettes d'album, drapeaux, photos et personnalités.");
            S("Répondre", "Tape ta réponse puis Entrée, autant de fois que tu veux pendant les 20 secondes. Le titre français ou original, les abréviations connues (GTA, AoT...) et les petites fautes de frappe sont acceptés. Les mauvaises réponses de chacun s'affichent pour tout le monde.");
            S("Les points", "Plus tu trouves vite, plus tu gagnes : 10 points tout de suite, 3 à la dernière seconde, et 2 de bonus pour le premier qui trouve.");
        }
        else if (game.gameId == GameId.Roulette)
        {
            S("Le but", "Chaque joueur commence avec 1 000 jetons. Après le nombre de coups choisi, le joueur le plus riche gagne. Un joueur qui n'a plus de quoi miser (10 jetons) est éliminé.");
            S("Faites vos jeux", "À ton tour, choisis un jeton puis clique sur le tapis pour miser (clic droit pour retirer). Quand tout le monde a misé, le croupier annonce « Rien ne va plus » et lance la bille. 37 cases : les numéros 1 à 36, rouges ou noirs, et le zéro vert.");
            S("Les mises et leurs gains", "Plein (un numéro) : 35 fois la mise. Cheval (2 numéros voisins, clique sur leur bordure) : 17 fois. Transversale (une rangée de 3, clique sous la rangée) : 11 fois. Carré (4 numéros, clique sur leur coin) : 8 fois. Sixain (2 rangées, clique sous leur séparation) : 5 fois. Douzaine ou colonne : 2 fois. Chances simples (rouge, noir, pair, impair, manque 1-18, passe 19-36) : 1 fois.");
            S("Le zéro et la prison", "Quand le 0 sort, les mises sur les chances simples ne sont pas perdues : elles vont « en prison ». Au coup suivant, si ta chance sort, ta mise t'est rendue ; sinon elle est perdue. Si le 0 ressort, il faudra que ta chance sorte deux fois de suite. Le 0 se joue en plein, à cheval avec 1, 2 ou 3, en transversale 0-1-2 ou 0-2-3, ou en carré 0-1-2-3.");
        }
        else
        {
            S("Le but", "Chaque joueur commence avec 1 000 jetons et joue contre le croupier. Après le nombre de manches choisi, le joueur le plus riche gagne. Un joueur qui n'a plus de quoi miser (10 jetons) est éliminé.");
            S("Une manche", "Chacun mise (10 jetons minimum), puis le croupier distribue deux cartes à chaque joueur et deux pour lui, dont une face cachée. Les figures valent 10, l'As vaut 1 ou 11, les autres cartes leur valeur.");
            S("À ton tour", "Tirer : prendre une carte. Rester : garder sa main. Dépasser 21, c'est perdre sa mise tout de suite.");
            S("Doubler", "Sur tes deux premières cartes : tu doubles ta mise et tu reçois exactement une carte de plus.");
            S("Séparer", "Si tes deux premières cartes ont le même rang, tu peux les séparer en deux mains (avec une deuxième mise égale). Jusqu'à 4 mains. Des As séparés ne reçoivent qu'une carte chacun.");
            S("Assurance", "Quand le croupier montre un As, tu peux payer la moitié de ta mise pour t'assurer contre son blackjack. S'il l'a, l'assurance te rapporte 2 contre 1.");
            S("Le croupier", "Il retourne sa carte cachée puis tire jusqu'à avoir au moins 17. Tu gagnes si tu as plus que lui sans dépasser 21, ou s'il saute. Égalité : ta mise t'est rendue. Un blackjack (As + 10 en deux cartes) paie 3 contre 2.");
        }
    }

    // --- HUD --------------------------------------------------------------------------
    VisualElement playersBar, ccHud, bjHud, action, card, rabbitRow, cycle, cycleGrid, feed, bjAction, bjButtons, bubbles, seatTags, bjLeft, bjRight;
    Label turn, cardValue, cardSub, deck, banner, hint, roundLabel, bjTurn, betLabel, balance, betInfo, roundInfo;
    Button drawBtn;
    IVisualElementScheduledItem bannerHide;
    int betAmount = Blackjack.MinBet;

    void BuildHud()
    {
        hud = Screen("hud");
        bubbles = Div(hud, "bubbles");
        bubbles.pickingMode = PickingMode.Ignore;
        playersBar = Div(hud, "players-bar");
        var corner = Div(hud, "corner");
        SoundBtn(corner);
        Ico(Btn(corner, "", () => game.Pause(), "ghost", "round"), "pause");
        feed = Div(hud, "feed");
        feed.pickingMode = PickingMode.Ignore;

        ccHud = Div(hud, "layer");
        cycle = Div(ccHud, "panel", "cycle");
        Text(cycle, "Crans des aiguilles", "h2").style.marginTop = 0;
        cycleGrid = Div(cycle, "cycle-grid");
        Text(cycle, "Orange : cran actuel. Rouge : les 2 prochains crans (carotte simple ou double).", "small-note");
        action = Div(ccHud, "panel", "action");
        card = Div(action, "card", "flip");
        cardValue = Text(card, "", "card-value");
        cardSub = Text(card, "", "card-sub");
        var col = Div(action, "action-col");
        turn = Text(col, "", "turn");
        drawBtn = Ico(Btn(col, "Piocher une carte", () => game.Draw()), "cards");
        rabbitRow = Div(col, "rabbit-row");
        deck = Text(col, "", "deck");

        bjHud = Div(hud, "layer");
        seatTags = Div(bjHud, "layer");
        seatTags.pickingMode = PickingMode.Ignore;
        roundLabel = Text(bjHud, "", "round-label");
        bjTurn = Text(bjHud, "", "bj-turn");
        bjTurn.pickingMode = roundLabel.pickingMode = PickingMode.Ignore;
        bjLeft = Div(bjHud, "bj-side", "left");
        bjRight = Div(bjHud, "bj-side", "right");
        bjAction = Div(bjHud, "bj-bet");
        betLabel = Text(bjAction, "", "bet-label");
        bjButtons = Div(bjAction, "rabbit-row");
        var bar = Div(bjHud, "bj-bar");
        balance = Text(bar, "", "bar-item");
        betInfo = Text(bar, "", "bar-item");
        roundInfo = Text(bar, "", "bar-item");

        BuildRouletteHud();
        BuildQuizHud();

        var bannerRow = Div(hud, "banner-row");
        bannerRow.pickingMode = PickingMode.Ignore;
        banner = Text(bannerRow, "", "banner");
        banner.pickingMode = PickingMode.Ignore;
        hint = Text(hud, "", "hint");
        foreach (var e in new[] { ccHud, bjHud }) e.pickingMode = PickingMode.Ignore;
    }

    public void ShowHud()
    {
        history.Clear();
        foreach (var s in All) if (s != hud) Hide(s);
        current = hud;
        Show(hud);
        card.AddToClassList("flip");
        card.style.display = DisplayStyle.None;
        bool bj = game.bj != null, rt = game.rt != null, qz = game.qz != null;
        ccHud.style.display = game.rules != null ? DisplayStyle.Flex : DisplayStyle.None;
        bjHud.style.display = bj ? DisplayStyle.Flex : DisplayStyle.None;
        rtHud.style.display = rt ? DisplayStyle.Flex : DisplayStyle.None;
        qzHud.style.display = qz ? DisplayStyle.Flex : DisplayStyle.None;
        if (qz)
        {
            qzFeed.Clear(); qzReveal.style.display = DisplayStyle.None; qzInput.SetEnabled(false);
            // Avant la 1re question : champ de reponse en reponse libre, rien en QCM (les cases arrivent avec la question).
            qzChoices.style.display = DisplayStyle.None;
            qzInput.style.display = game.qz.Mcq ? DisplayStyle.None : DisplayStyle.Flex;
        }
        bubbles.Clear();
        seatTags.Clear();
        playersBar.style.display = feed.style.display = bj || rt || qz ? DisplayStyle.None : DisplayStyle.Flex;
        hint.text = bj || rt || qz ? "" : "Clic droit : tourner  ·  Molette : zoom  ·  Échap : pause";
        if (rt) ResetRouletteBets();
        if (bj) betAmount = Blackjack.MinBet * 5;
        Refresh();
    }

    public void Say(string text, float seconds = 2.4f)
    {
        banner.text = text;
        banner.RemoveFromClassList("show");
        banner.schedule.Execute(() => banner.AddToClassList("show")).StartingIn(16);
        bannerHide?.Pause();
        bannerHide = banner.schedule.Execute(() => banner.RemoveFromClassList("show")).StartingIn((long)(seconds * 1000));
    }

    public void ShowCard(Card c)
    {
        card.style.display = DisplayStyle.Flex;
        card.EnableInClassList("carrot-card", c.carrot);
        cardValue.text = c.carrot ? (c.turns == 2 ? "Double\ncarotte" : "Carotte !") : c.steps.ToString();
        cardSub.text = c.carrot ? "la carotte tourne..." : (c.steps > 1 ? "cases" : "case");
        card.AddToClassList("flip");
        card.schedule.Execute(() => card.RemoveFromClassList("flip")).StartingIn(30);
    }

    public void HideCard()
    {
        card.AddToClassList("flip");
        card.schedule.Execute(() => card.style.display = DisplayStyle.None).StartingIn(300);
    }

    static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    public void Refresh()
    {
        if (game.rules != null) RefreshCroque();
        else if (game.bj != null) RefreshBlackjack();
        else if (game.rt != null) RefreshRoulette();
        else if (game.qz != null) RefreshQuiz();
    }

    void PlayerCard(int i, string name, string avatar, bool active)
    {
        var pc = Div(playersBar, "pcard");
        pc.EnableInClassList("active", active);
        Ring(Portrait(pc, avatar, null, 52), Board.Colors[i]);
        Text(pc, name, "pname").style.color = Board.Colors[i];
    }

    void Feed(List<string> log)
    {
        feed.Clear();
        var lines = log.Skip(Math.Max(0, log.Count - 5)).ToList();
        for (int i = 0; i < lines.Count; i++) Text(feed, lines[i], "feed-line").EnableInClassList("last", i == lines.Count - 1);
    }

    string Avatar(int seat) => game.Online ? (seat < game.net.LobbyAvatars.Count ? game.net.LobbyAvatars[seat] : "Casual_Male")
                                           : (seat < game.avatars.Count ? game.avatars[seat] : "Casual_Male");

    void RefreshCroque()
    {
        var r = game.rules;
        playersBar.Clear();
        for (int i = 0; i < r.players.Count; i++)
        {
            var p = r.players[i];
            PlayerCard(i, p.name, Avatar(i), i == r.turn && !r.Over);
            var pc = playersBar.Children().Last();
            foreach (int pos in p.rabbits.OrderByDescending(x => x))
            {
                var pip = Text(pc, pos > 0 && pos < r.summit ? pos.ToString() : "", "pip");
                if (pos >= r.summit) pip.style.backgroundColor = Board.Colors[p.color];
                else if (pos > 0) pip.AddToClassList("field");
            }
        }

        bool mine = !game.busy && !r.Over && game.MyTurn;
        drawBtn.style.display = r.drawn == null && mine ? DisplayStyle.Flex : DisplayStyle.None;
        var name = $"<color={Hex(Board.Colors[r.Current.color])}>{r.Current.name}</color>";
        turn.text = r.Over ? "Partie terminée !" : game.busy ? "..." : r.drawn == null ? $"Au tour de <b>{name}</b>" : $"{name}, quel lapin avance ?";
        rabbitRow.Clear();
        if (r.drawn != null && mine)
            for (int k = 0; k < Rules.RabbitsPerPlayer; k++)
            {
                int idx = k;
                int pos = r.Current.rabbits[k];
                string where = pos == 0 ? "départ" : pos >= r.summit ? "potager" : "case " + pos;
                var b = Btn(rabbitRow, $"Lapin {k + 1}\n<size=17>{where}</size>", () => game.Move(idx));
                b.SetEnabled(r.CanMove(k));
                Face(b).style.backgroundColor = Board.Colors[r.Current.color];
            }
        deck.text = $"Pioche : {r.DeckLeft} cartes";

        cycle.style.display = r.mode == Mode.Ameliore ? DisplayStyle.Flex : DisplayStyle.None;
        if (r.mode == Mode.Ameliore)
        {
            cycleGrid.Clear();
            int now = r.rotations % Rules.CycleLength;
            for (int t = 0; t < Rules.CycleLength; t++)
            {
                int c = r.Over ? r.cycle[t] : r.known[t];
                var cell = Text(cycleGrid, c > 0 ? c.ToString() : "?", "cell");
                if (c > 0) cell.AddToClassList("known");
                if (t == (now + 1) % 25 || t == (now + 2) % 25) cell.AddToClassList("next");
                if (t == now && r.rotations > 0) cell.AddToClassList("now");
            }
        }
        Feed(r.log);
    }

    // Bouton rond façon casino : pastille coloree + libelle dessous.
    void RoundAction(VisualElement parent, string icon, string label, string cls, bool enabled, Action a)
    {
        var box = Div(parent, "round-action");
        var b = Btn(box, icon, a, "round-act", cls);
        b.SetEnabled(enabled);
        Text(box, label, "round-label-txt");
    }

    void RefreshBlackjack()
    {
        var b = game.bj;
        playersBar.Clear();
        roundLabel.text = "";
        Feed(b.log);

        bjButtons.Clear();
        bjLeft.Clear();
        bjRight.Clear();
        betLabel.text = "";
        bool mine = !game.busy && game.MyTurn && !b.Finished;
        var cur = b.Current;
        var me = game.Online && game.mySeat >= 0 ? b.players[game.mySeat] : cur ?? b.players[0];
        balance.text = $"Solde : <b>{me.chips}</b>";
        betInfo.text = $"Mise : <b>{me.hands.Sum(h => h.bet)}</b>";
        roundInfo.text = b.Finished ? "Partie terminée" : $"Manche <b>{Math.Min(b.round, b.rounds)}/{b.rounds}</b>";
        string who = cur != null ? $"<color={Hex(Board.Colors[cur.seat])}>{cur.name}</color>" : "";
        bjAction.style.display = DisplayStyle.None;
        if (game.busy || cur == null) { bjTurn.text = ""; return; }
        if (!mine) { bjTurn.text = $"Au tour de {who}..."; return; }

        switch (b.phase)
        {
            case BJPhase.Bet:
                bjAction.style.display = DisplayStyle.Flex;
                betAmount = Mathf.Clamp(betAmount, Blackjack.MinBet, cur.chips / Blackjack.MinBet * Blackjack.MinBet);
                bjTurn.text = $"{who}, faites vos jeux !";
                betLabel.text = $"Mise : {betAmount}";
                foreach (int v in new[] { 10, 50, 100, 500 })
                {
                    int add = v;
                    var chip = Btn(bjButtons, "", () => { betAmount = Math.Min(betAmount + add, cur.chips / 10 * 10); Sound.I.Play("bj_chip1"); Refresh(); }, "chip-btn");
                    chip.style.backgroundImage = Resources.Load<Texture2D>("Casino/chip_" + v);
                    chip.SetEnabled(betAmount + v <= cur.chips);
                }
                Btn(bjButtons, "Effacer", () => { betAmount = Blackjack.MinBet; Refresh(); }, "ghost", "small");
                Btn(bjButtons, "Miser", () => game.Act("bet|" + betAmount), "green", "small");
                break;
            case BJPhase.Insurance:
                bjTurn.text = $"{who}, le croupier montre un As. Assurance ({b.InsuranceCost(cur)} jetons) ?";
                RoundAction(bjLeft, "Non", "PAS D'ASSURANCE", "act-red", true, () => game.Act("ins|0"));
                RoundAction(bjRight, "Oui", "ASSURANCE", "act-green", true, () => game.Act("ins|1"));
                break;
            case BJPhase.Play:
                var h = b.ActiveHand;
                string handTxt = cur.hands.Count > 1 ? $" (main {cur.hands.IndexOf(h) + 1}/{cur.hands.Count})" : "";
                bjTurn.text = $"À toi, {who}{handTxt}";
                RoundAction(bjLeft, "x2", "DOUBLER", "act-blue", b.CanDouble(h, cur), () => game.Act("double"));
                RoundAction(bjLeft, "<>", "SÉPARER", "act-blue", b.CanSplit(h, cur), () => game.Act("split"));
                RoundAction(bjRight, "=", "RESTER", "act-red", true, () => game.Act("stand"));
                RoundAction(bjRight, "+", "TIRER", "act-green", true, () => game.Act("hit"));
                break;
        }
    }

    // Etiquettes flottantes : valeur des mains, et medaillons des joueurs a leur place.
    public void UpdateBubbles(IEnumerable<(Vector3 pos, string text, Color col)> items, Camera cam, Table table)
    {
        var b = game.bj;
        if (seatTags.childCount != b.players.Count)
        {
            seatTags.Clear();
            foreach (var p in b.players)
            {
                var tag = Div(seatTags, "seat-tag");
                tag.pickingMode = PickingMode.Ignore;
                Portrait(tag, Avatar(p.seat), null, 62).style.borderTopColor = Board.Colors[p.seat];
                var col = Div(tag);
                Text(col, p.name, "seat-name");
                Text(col, "", "seat-chips");
            }
        }
        for (int i = 0; i < b.players.Count && seatTags.panel != null; i++)
        {
            var tag = seatTags[i];
            var p = RuntimePanelUtils.CameraTransformWorldToPanel(seatTags.panel, table.SeatTagPos(i), cam);
            tag.style.left = p.x;
            tag.style.top = p.y;
            tag.EnableInClassList("active", b.Actor == i);
            ((Label)tag[1][1]).text = b.players[i].broke ? "ruiné" : b.players[i].chips.ToString();
            var por = tag[0];
            por.style.borderTopColor = por.style.borderBottomColor = por.style.borderLeftColor = por.style.borderRightColor = Board.Colors[i];
        }
        UpdateValueBadges(items, cam);
    }

    void UpdateValueBadges(IEnumerable<(Vector3 pos, string text, Color col)> items, Camera cam)
    {
        var list = items.ToList();
        while (bubbles.childCount < list.Count) Text(bubbles, "", "bubble", "badge");
        for (int i = 0; i < bubbles.childCount; i++)
        {
            var l = (Label)bubbles[i];
            if (i >= list.Count || bubbles.panel == null) { l.style.display = DisplayStyle.None; continue; }
            var p = RuntimePanelUtils.CameraTransformWorldToPanel(bubbles.panel, list[i].pos, cam);
            l.style.display = DisplayStyle.Flex;
            l.text = list[i].text;
            l.style.left = p.x;
            l.style.top = p.y;
            l.style.borderBottomColor = list[i].col;
        }
    }

    // --- Pause & victoire -------------------------------------------------------------
    Label winTitle, winSub;
    Button replayBtn;

    void BuildPause()
    {
        pause = Screen("dim");
        var panel = Panel(pause);
        panel.style.width = 700;
        Text(panel, "Pause", "panel-title");
        Ico(Btn(panel, "Reprendre", Back, "lg"), "play");
        var row = Div(panel, "menu-row");
        Ico(Btn(row, "Règles", () => { RefreshRules(); Go(rulesScreen); }, "ghost"), "book");
        Ico(Btn(row, "Paramètres", () => { SelectTab(tab); Go(settingsScreen); }, "blue"), "gear");
        Ico(Btn(panel, "Quitter la partie", () => game.ToMenu(), "red"), "exit");
    }

    public void ShowPause() { history.Clear(); history.Push(hud); current = null; Go(pause, false); }

    void BuildVictory()
    {
        victory = Screen("dim");
        var panel = Panel(victory);
        panel.style.width = 900;
        panel.style.alignItems = Align.Center;
        winTitle = Text(panel, "", "panel-title", "win-title");
        winSub = Text(panel, "", "p");
        winSub.style.unityTextAlign = TextAnchor.MiddleCenter;
        var row = Div(panel, "row");
        row.style.marginTop = 20;
        Ico(Btn(row, "Menu principal", () => game.ToMenu(), "ghost"), "exit").style.marginRight = 20;
        replayBtn = Ico(Btn(row, "Rejouer !", () => game.Replay(), "lg"), "play");
    }

    public void ShowVictory()
    {
        if (game.rules != null)
        {
            var w = game.rules.players[game.rules.winner];
            winTitle.text = $"{w.name} gagne !";
            winTitle.style.color = Board.Colors[w.color];
            winSub.text = "Ses trois lapins festoient au potager.";
        }
        else
        {
            var ranking = (game.bj != null ? game.bj.players.Select(p => (p.name, p.seat, p.chips))
                         : game.rt != null ? game.rt.players.Select(p => (p.name, p.seat, p.chips))
                         : game.qz.players.Select(p => (p.name, p.seat, chips: p.score)))
                .OrderByDescending(p => p.chips).ToList();
            winTitle.text = $"{ranking[0].name} gagne !";
            winTitle.style.color = Board.Colors[ranking[0].seat];
            winSub.text = string.Join("\n", ranking.Select((p, i) => $"{i + 1}.  {p.name}  —  {p.chips} {(game.qz != null ? "points" : "jetons")}"));
        }
        replayBtn.style.display = !game.Online || game.net.IsHost ? DisplayStyle.Flex : DisplayStyle.None;
        current = null;
        Go(victory, false);
    }

    // --- En ligne -----------------------------------------------------------------------
    VisualElement lobbyList, lobbyOptions, myPortraitBox;
    TextField onlineName, codeField;
    Label onlineStatus, lobbyCode, lobbyStatus, onlineTitle, lobbyGame;
    Button startBtn;

    void BuildOnline()
    {
        onlineScreen = Screen();
        var panel = Panel(onlineScreen);
        panel.style.width = 1000;
        onlineTitle = Text(panel, "Jouer en ligne", "panel-title");
        Text(panel, "Toi", "h2");
        var me = Div(panel, "row");
        myPortraitBox = Div(me);
        onlineName = Add(me, new TextField { value = PlayerPrefs.GetString("cc-name", "Joueur"), maxLength = 16 }, "name-field");
        onlineName.style.marginLeft = 14;
        Text(panel, "Créer une partie", "h2");
        Text(panel, "Tu recevras un code à donner à tes amis.", "p");
        Ico(Btn(panel, "Héberger une partie", () => { SaveName(); game.net.Host(onlineName.value, game.gameId, game.option); }), "globe");
        Text(panel, "Rejoindre une partie", "h2");
        var row = Div(panel, "row");
        codeField = Add(row, new TextField { maxLength = 8 }, "name-field");
        var join = Ico(Btn(row, "Rejoindre", () => { SaveName(); game.net.Join(codeField.value, onlineName.value); }, "blue", "small"), "play");
        join.style.marginLeft = 12;
        onlineStatus = Text(panel, "", "p", "status");
        Ico(Btn(panel, "Retour", Back, "ghost", "small"), "back").style.alignSelf = Align.FlexStart;
    }

    void SaveName() { PlayerPrefs.SetString("cc-name", onlineName.value); RefreshProfile(); }

    VisualElement lobbyArt;
    Label lobbyCount, lobbyMeta;

    void BuildLobby()
    {
        lobbyScreen = Screen();
        var panel = Panel(lobbyScreen);
        panel.style.width = 1700;
        Text(panel, "Salon", "panel-title");
        var cols = Div(panel, "row", "lobby-cols");
        var left = Div(cols, "lobby-left");
        var head = Div(left, "row", "spread");
        Text(head, "Joueurs", "h2");
        lobbyCount = Text(head, "", "muted");
        lobbyList = Add(left, new ScrollView(), "lobby-list");
        var lb = Div(left, "row", "spread");
        lobbyStatus = Text(lb, "", "p", "status");
        Ico(Btn(lb, "Quitter le salon", Back, "red", "small"), "exit");

        var right = Div(cols, "lobby-right");
        Text(right, "Code du salon", "h2");
        var cr = Div(right, "row");
        lobbyCode = Text(cr, "", "code");
        Ico(Btn(cr, "Copier", () => GUIUtility.systemCopyBuffer = game.net.Code, "blue"), "copy");
        Text(right, "Donne ce code à tes amis pour qu'ils te rejoignent.", "muted");
        var gc = Div(right, "row", "lobby-game");
        lobbyArt = Div(gc, "lobby-art");
        var gt = Div(gc);
        lobbyGame = Text(gt, "", "mode-name");
        lobbyMeta = Text(gt, "", "game-meta");
        lobbyOptions = Div(right, "row", "lobby-options");
        Div(right, "grow");
        startBtn = Ico(Btn(right, "Lancer la partie", () => game.net.StartMatch(), "lg"), "play");
    }

    public void RefreshOnline()
    {
        var n = game.net;
        onlineTitle.text = "En ligne : " + Games.Name(game.gameId);
        myPortraitBox.Clear();
        Portrait(myPortraitBox, game.myAvatar, () => OpenPicker(a => { game.SetMyAvatar(a); RefreshOnline(); }), 64);
        onlineStatus.text = n.Status;
        if (n.InGame) return;
        if (n.Active && n.Lobby.Count > 0 && current == onlineScreen) Go(lobbyScreen);
        if (!n.Active && current == lobbyScreen) Go(onlineScreen, false);
        lobbyGame.text = Games.Name(n.LobbyGame);
        lobbyMeta.text = GameInfo[n.LobbyGame].meta;
        lobbyArt.style.backgroundImage = Resources.Load<Texture2D>("UI/Games/" + GameInfo[n.LobbyGame].art);
        lobbyCount.text = $"{n.Lobby.Count} / {Games.MaxPlayers(n.LobbyGame)} joueurs";
        lobbyCode.text = n.Code;
        lobbyStatus.text = n.IsHost ? (n.Lobby.Count < 2 ? "En attente d'au moins un autre joueur..." : "Tout le monde est là ? Lance la partie !") : "En attente de l'hôte...";
        lobbyList.Clear();
        for (int i = 0; i < n.Lobby.Count; i++)
        {
            var row = Div(lobbyList, "player-row");
            Ring(Portrait(row, i < n.LobbyAvatars.Count ? n.LobbyAvatars[i] : "Casual_Male", null, 56), Board.Colors[i]);
            Text(row, n.Lobby[i], "lobby-name").style.color = Board.Colors[i];
            if (i == 0) Text(row, "Hôte", "pill", "pill-gold");
            if (i == game.mySeat) Text(row, "Toi", "pill", "pill-blue");
        }
        var g0 = game.gameId;
        game.gameId = n.LobbyGame;
        OptionCards(lobbyOptions, n.LobbyOption, v => n.SetOption(v));
        game.gameId = g0;
        lobbyOptions.SetEnabled(n.IsHost);
        startBtn.style.display = n.IsHost ? DisplayStyle.Flex : DisplayStyle.None;
        startBtn.SetEnabled(n.Lobby.Count >= 2);
    }
}
