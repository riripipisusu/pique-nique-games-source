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

    Button Btn(VisualElement p, string text, Action onClick, params string[] c)
    {
        var b = new Button(() => { Sound.I.UI("click"); onClick(); }) { text = text };
        b.AddToClassList("btn");
        b.RegisterCallback<MouseEnterEvent>(_ => { if (b.enabledSelf) Sound.I.UI("hover"); });
        return Add(p, b, c);
    }

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
    }

    public void ShowTitle()
    {
        history.Clear();
        foreach (var s in All) Hide(s);
        current = null;
        Go(title, false);
    }

    // --- Ecran titre ----------------------------------------------------------------
    void BuildTitle()
    {
        title = Screen("title-screen");
        var logo = Text(title, "Pique-Nique's Games", "logo");
        Text(title, "Des jeux de société à partager entre amis", "tagline");
        var col = Div(title, "menu-col");
        Btn(col, "Jouer", () => Go(games));
        Btn(col, "Paramètres", () => { SelectTab(tab); Go(settingsScreen); }, "green");
        Btn(col, "Quitter", Application.Quit, "ghost");
        Text(title, $"v{Application.version}  ·  Décors Synty Studios · Modèles Kenney & Quaternius (CC0)  ·  Sons de roulette : Pixabay · Musiques : MMAudio, Geoff Harvey (Pixabay), « A Conversation with Saul » de Matthew Pablo (CC-BY 3.0)", "credits");
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

    public void ShowUpdate(string version)
    {
        updateText.text = $"Nouvelle version {version} disponible !";
        updateCard.style.display = DisplayStyle.Flex;
    }

    // --- Choix du jeu -----------------------------------------------------------------
    void BuildGames()
    {
        games = Screen();
        var panel = Div(games, "panel");
        panel.style.width = 1640;
        Text(panel, "À quoi on joue ?", "panel-title");
        var row = Div(panel, "row");
        GameCard(row, GameId.Croque, "Croque-Carotte", "2 à 4 joueurs  ·  Course de lapins",
            "Grimpe la montagne jusqu'au potager... mais gare aux trous quand la carotte tourne !", "game-croque");
        GameCard(row, GameId.Blackjack, "Blackjack", "2 à 4 joueurs  ·  Cartes",
            "Approche-toi de 21 sans dépasser et bats le croupier. Le plus riche après les manches gagne.", "game-bj");
        GameCard(row, GameId.Roulette, "Roulette", "1 à 4 joueurs  ·  Casino",
            "Roulette française : pleins, chevaux, carrés, rouge ou noir... Le plus riche après les coups gagne.", "game-rt");
        var back = Btn(panel, "Retour", Back, "ghost", "small");
        back.style.width = 260;
        back.style.marginTop = 20;
    }

    void GameCard(VisualElement parent, GameId g, string name, string meta, string desc, string cls)
    {
        var b = new Button(() => { Sound.I.UI("click"); game.SelectGame(g); RefreshSetup(); Go(setup); });
        b.AddToClassList("game-card");
        b.AddToClassList(cls);
        Div(b, "game-art");
        Text(b, name, "mode-name");
        Text(b, meta, "game-meta");
        Text(b, desc, "mode-desc");
        parent.Add(b);
    }

    // --- Preparation d'une partie (options + joueurs) -------------------------------------
    VisualElement setupOptions, playerList;
    Label setupTitle;
    Button addPlayer;

    void BuildSetup()
    {
        setup = Screen();
        var panel = Div(setup, "panel");
        panel.style.width = 1180;
        setupTitle = Text(panel, "", "panel-title");
        Text(panel, "Options", "h2");
        setupOptions = Div(panel, "row");
        Text(panel, "Joueurs sur ce PC (chacun son tour)", "h2");
        playerList = Div(panel);
        addPlayer = Btn(panel, "+ Ajouter un joueur", () =>
        {
            game.names.Add("Joueur " + (game.names.Count + 1));
            game.avatars.Add(Game.Characters[(game.names.Count * 7) % Game.Characters.Length]);
            RefreshSetup();
        }, "ghost", "small");
        var bottom = Div(panel, "row", "spread");
        bottom.style.marginTop = 20;
        Btn(bottom, "Retour", Back, "ghost", "small").style.width = 200;
        Btn(bottom, "Règles", () => { RefreshRules(); Go(rulesScreen); }, "ghost", "small").style.width = 200;
        Btn(bottom, "Jouer en ligne", () => { RefreshOnline(); Go(onlineScreen); }, "small").style.width = 300;
        Btn(bottom, "Jouer sur ce PC !", () => game.StartGame(), "green").style.width = 380;
    }

    void OptionCards(VisualElement parent, int current, Action<int> pick)
    {
        parent.Clear();
        var opts = game.gameId == GameId.Croque
            ? new[] { (0, "Classique", "19 cases en spirale. La carotte ouvre 1 à 3 trous au hasard."), (1, "Amélioré", "25 cases, trous selon un cycle secret à deviner.") }
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
            Portrait(row, game.avatars[i], () => OpenPicker(a => { game.avatars[idx] = a; RefreshSetup(); }), 60).style.borderTopColor = Board.Colors[i];
            Div(row, "chip").style.backgroundColor = Board.Colors[i];
            var field = Add(row, new TextField { value = game.names[i], maxLength = 16 }, "name-field");
            field.RegisterValueChangedCallback(e => game.names[idx] = e.newValue);
            var rm = Btn(row, "Retirer", () => { game.names.RemoveAt(idx); game.avatars.RemoveAt(idx); RefreshSetup(); }, "ghost", "small");
            rm.style.marginLeft = 12;
            rm.SetEnabled(game.names.Count > 2);
        }
        addPlayer.style.display = game.names.Count < Rules.MaxPlayers ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // --- Choix du personnage ---------------------------------------------------------------
    Action<string> onPick;

    void BuildPicker()
    {
        picker = Screen("dim");
        var panel = Div(picker, "panel", "settings-panel");
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
        Btn(bottom, "Annuler", Back, "ghost", "small").style.width = 280;
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
        var panel = Div(settingsScreen, "panel", "settings-panel");
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
        Btn(bottom, "Par défaut", () => { game.ResetSettings(); SelectTab(tab); }, "ghost", "small").style.width = 280;
        Btn(bottom, "Retour", Back, "small").style.width = 280;
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
        var panel = Div(rulesScreen, "panel", "settings-panel");
        rulesTitle = Text(panel, "", "panel-title");
        rulesBody = Add(panel, new ScrollView(), "settings-scroll");
        var bottom = Div(panel, "row");
        bottom.style.justifyContent = Justify.Center;
        Btn(bottom, "Retour", Back, "small").style.width = 280;
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
        Btn(hud, "II", () => game.Pause(), "round", "ghost", "pause-btn");
        feed = Div(hud, "panel", "feed");

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
        drawBtn = Btn(col, "Piocher une carte", () => game.Draw(), "small");
        drawBtn.style.width = 360;
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

        banner = Text(hud, "", "banner");
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
        bool bj = game.bj != null, rt = game.rt != null;
        ccHud.style.display = game.rules != null ? DisplayStyle.Flex : DisplayStyle.None;
        bjHud.style.display = bj ? DisplayStyle.Flex : DisplayStyle.None;
        rtHud.style.display = rt ? DisplayStyle.Flex : DisplayStyle.None;
        bubbles.Clear();
        seatTags.Clear();
        playersBar.style.display = feed.style.display = bj || rt ? DisplayStyle.None : DisplayStyle.Flex;
        hint.text = bj || rt ? "" : "Clic droit : tourner  ·  Molette : zoom  ·  Échap : pause";
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
    }

    void PlayerCard(int i, string name, string avatar, bool active)
    {
        var pc = Div(playersBar, "pcard");
        pc.EnableInClassList("active", active);
        var por = Portrait(pc, avatar, null, 46);
        por.style.borderTopColor = por.style.borderBottomColor = por.style.borderLeftColor = por.style.borderRightColor = Board.Colors[i];
        Text(pc, name, "pname");
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
                b.style.backgroundColor = Color.Lerp(Board.Colors[r.Current.color], Color.white, 0.1f);
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
        var panel = Div(pause, "panel");
        panel.style.width = 560;
        Text(panel, "Pause", "panel-title");
        Btn(panel, "Reprendre", Back, "green");
        Btn(panel, "Paramètres", () => { SelectTab(tab); Go(settingsScreen); });
        Btn(panel, "Règles", () => { RefreshRules(); Go(rulesScreen); });
        Btn(panel, "Menu principal", () => game.ToMenu(), "ghost");
    }

    public void ShowPause() { history.Clear(); history.Push(hud); current = null; Go(pause, false); }

    void BuildVictory()
    {
        victory = Screen("dim");
        var panel = Div(victory, "panel");
        panel.style.width = 900;
        panel.style.alignItems = Align.Center;
        winTitle = Text(panel, "", "panel-title", "win-title");
        winSub = Text(panel, "", "p");
        winSub.style.unityTextAlign = TextAnchor.MiddleCenter;
        var row = Div(panel, "row");
        row.style.marginTop = 20;
        Btn(row, "Menu principal", () => game.ToMenu(), "ghost").style.width = 360;
        replayBtn = Btn(row, "Rejouer !", () => game.Replay(), "green");
        replayBtn.style.width = 360;
        row.Children().First().style.marginRight = 20;
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
            var ranking = (game.bj != null ? game.bj.players.Select(p => (p.name, p.seat, p.chips)) : game.rt.players.Select(p => (p.name, p.seat, p.chips)))
                .OrderByDescending(p => p.chips).ToList();
            winTitle.text = $"{ranking[0].name} gagne !";
            winTitle.style.color = Board.Colors[ranking[0].seat];
            winSub.text = string.Join("\n", ranking.Select((p, i) => $"{i + 1}.  {p.name}  —  {p.chips} jetons"));
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
        var panel = Div(onlineScreen, "panel");
        panel.style.width = 900;
        onlineTitle = Text(panel, "Jouer en ligne", "panel-title");
        Text(panel, "Toi", "h2");
        var me = Div(panel, "row");
        myPortraitBox = Div(me);
        onlineName = Add(me, new TextField { value = PlayerPrefs.GetString("cc-name", "Joueur"), maxLength = 16 }, "name-field");
        onlineName.style.marginLeft = 14;
        Text(panel, "Créer une partie", "h2");
        Text(panel, "Tu recevras un code à donner à tes amis (jusqu'à 4 joueurs).", "p");
        Btn(panel, "Héberger une partie", () => { SaveName(); game.net.Host(onlineName.value, game.gameId, game.option); }, "green");
        Text(panel, "Rejoindre une partie", "h2");
        var row = Div(panel, "row");
        codeField = Add(row, new TextField { maxLength = 8 }, "name-field");
        var join = Btn(row, "Rejoindre", () => { SaveName(); game.net.Join(codeField.value, onlineName.value); }, "small");
        join.style.marginLeft = 12;
        join.style.width = 240;
        onlineStatus = Text(panel, "", "p", "status");
        var back = Btn(panel, "Retour", Back, "ghost", "small");
        back.style.width = 240;
        back.style.alignSelf = Align.FlexStart;
    }

    void SaveName() => PlayerPrefs.SetString("cc-name", onlineName.value);

    void BuildLobby()
    {
        lobbyScreen = Screen();
        var panel = Div(lobbyScreen, "panel");
        panel.style.width = 1100;
        Text(panel, "Salon", "panel-title");
        lobbyGame = Text(panel, "", "h2");
        lobbyGame.style.unityTextAlign = TextAnchor.MiddleCenter;
        var cr = Div(panel, "row");
        cr.style.justifyContent = Justify.Center;
        Text(cr, "Code :", "h2");
        lobbyCode = Text(cr, "", "code");
        var cp = Btn(cr, "Copier", () => GUIUtility.systemCopyBuffer = game.net.Code, "ghost", "small");
        cp.style.width = 180;
        cp.style.marginLeft = 16;
        Text(panel, "Joueurs", "h2");
        lobbyList = Div(panel);
        Text(panel, "Options", "h2");
        lobbyOptions = Div(panel, "row");
        lobbyStatus = Text(panel, "", "p", "status");
        var bottom = Div(panel, "row", "spread");
        bottom.style.marginTop = 16;
        Btn(bottom, "Quitter", Back, "ghost", "small").style.width = 240;
        startBtn = Btn(bottom, "Lancer la partie !", () => game.net.StartMatch(), "green");
        startBtn.style.width = 420;
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
        lobbyCode.text = n.Code;
        lobbyStatus.text = n.IsHost ? (n.Lobby.Count < 2 ? "En attente d'au moins un autre joueur..." : "Tout le monde est là ? Lance la partie !") : "En attente de l'hôte...";
        lobbyList.Clear();
        for (int i = 0; i < n.Lobby.Count; i++)
        {
            var row = Div(lobbyList, "player-row");
            Portrait(row, i < n.LobbyAvatars.Count ? n.LobbyAvatars[i] : "Casual_Male", null, 52);
            Div(row, "chip").style.backgroundColor = Board.Colors[i];
            Text(row, n.Lobby[i] + (i == game.mySeat ? "  (toi)" : "") + (i == 0 ? "  · hôte" : ""), "p").style.marginBottom = 0;
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
