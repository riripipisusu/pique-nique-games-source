using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Les ecrans de menu, facon "Modern Menus" (pack Synty INTERFACE) : pages pleines bleu nuit en diagonale,
// titres "JAUNE blanc »", cartes illustrees a cadre biseaute, barre d'icones 3D. Styles : Resources/UI/Modern.uss.
public partial class Ui
{
    // --- Morceaux communs -------------------------------------------------------------------
    readonly List<(VisualElement face, Label name)> profileChips = new List<(VisualElement, Label)>();
    VisualElement homeBust;
    readonly List<VisualElement> arts = new List<VisualElement>();
    Texture2D menuArt;
    VisualElement bgArt;
    public void SetArt(Texture2D t) { menuArt = t; foreach (var a in arts) a.style.backgroundImage = t; }

    // Page pleine : voile + pan diagonal bleu nuit, barre du haut (titre, profil, son), corps, pied de page.
    VisualElement Page(out VisualElement body, out VisualElement foot, string yellow, string white, params string[] c)
    {
        var s = Screen(c.Prepend("m-page").ToArray());
        var art = Div(s, "m-art");   // l'illustration de fond (MenuArt)
        art.pickingMode = PickingMode.Ignore;
        art.style.backgroundImage = menuArt;
        arts.Add(art);
        foreach (var v in new[] { "m-veil", "sk-veil-l", "sk-veil-r", "sk-veil-t", "sk-veil-b" }) Div(s, v).pickingMode = PickingMode.Ignore;
        var top = Div(s, "m-top");
        var t = Div(top, "m-title-row");
        if (yellow != null) Text(t, yellow.ToUpperInvariant(), "m-title", "y");
        if (!string.IsNullOrEmpty(white)) Text(t, white.ToUpperInvariant(), "m-title");
        Div(t, "m-chevron");
        Div(top, "grow");
        var me = SkPill(top, "in-top", () => OpenCreator(a => { game.SetMyAvatar(a); RefreshProfile(); }, game.myAvatar), out var meIco, out var meTxt);
        meIco.AddToClassList("sk-pill-face");
        profileChips.Add((meIco, meTxt));
        SoundPill(top, "in-top");
        body = Div(s, "m-body");
        foot = Div(s, "m-foot");
        return s;
    }

    // Pastille du joueur (portrait + prenom), clic = le createur.
    void ProfileChip(VisualElement parent)
    {
        var b = new Button(() => { Sound.I.UI("click"); OpenCreator(a => { game.SetMyAvatar(a); RefreshProfile(); }, game.myAvatar); });
        b.AddToClassList("m-chip");
        b.RegisterCallback<MouseEnterEvent>(_ => Sound.I.UI("hover"));
        var face = Div(b, "m-chip-face");
        var name = Text(b, "", "m-chip-name");
        face.pickingMode = name.pickingMode = PickingMode.Ignore;
        parent.Add(b);
        profileChips.Add((face, name));
    }

    void RefreshProfile()
    {
        foreach (var (face, name) in profileChips)
        {
            face.style.backgroundImage = Chars.Portrait(game.myAvatar);
            name.text = PlayerPrefs.GetString("cc-name", "Toi");
        }
        if (homeBust != null) homeBust.style.backgroundImage = Chars.Bust(game.myAvatar);
    }

    // Separateur decoratif (barre du pack, en deux moities).
    static void Rule(VisualElement p, string kind = "05")
    {
        var r = Div(p, "m-rule");
        Div(r, "m-rule-l", "r" + kind).pickingMode = PickingMode.Ignore;
        Div(r, "m-rule-r", "r" + kind).pickingMode = PickingMode.Ignore;
        r.pickingMode = PickingMode.Ignore;
    }

    static Label Head(VisualElement p, string text) => Text(p, text, "m-h");

    // Vignette d'un jeu : illustration (sinon une icone 3D sur un fond de couleur).
    static readonly Dictionary<GameId, (string icon, string color)> GameIcon = new Dictionary<GameId, (string, string)>
    {
        [GameId.Chevaux] = ("Flag_01", "#2E8B3E"), [GameId.BonnePaye] = ("Currency_Notes_01", "#12876A"),
        [GameId.Paintball] = ("Lightning_01", "#FF7A1A"), [GameId.Serpents] = ("Star_01", "#6A3FB5"), [GameId.Roue] = ("Currency_Coin_01", "#C8327A"),
        [GameId.QuiSuisJe] = ("ExclamationMark_01", "#E08A00"), [GameId.Pouilleux] = ("Death_01", "#3D4A63"),
        [GameId.Uno] = ("Lightning_01", "#D8322E"), [GameId.Limite] = ("Chat_01", "#B5121B"), [GameId.LoupGarou] = ("Death_01", "#2B1B4A"), [GameId.Bac] = ("Book_01", "#1C6FC4"), [GameId.Rhythm] = ("Headphones_01", "#8E24AA"),
    };
    static void GameArt(VisualElement e, GameId g)
    {
        e.Clear();
        e.RemoveFromClassList("no-art");
        e.style.backgroundColor = StyleKeyword.Null;
        var tex = Resources.Load<Texture2D>("UI/Games/" + GameInfo[g].art);
        e.style.backgroundImage = tex;
        if (tex || !GameIcon.TryGetValue(g, out var ic)) return;
        e.AddToClassList("no-art");
        if (ColorUtility.TryParseHtmlString(ic.color, out var col)) e.style.backgroundColor = col;
        var i = Div(e, "art-icon");
        i.style.backgroundImage = Resources.Load<Texture2D>("MM/icon3d/" + ic.icon);
        i.pickingMode = PickingMode.Ignore;
    }

    // Carte cliquable : image, degrade, rayures, textes (accroche, titre en deux tons, sous-titre), cadre biseaute.
    Button Card(VisualElement parent, Action click, string kicker, string titleY, string titleW, string sub, params string[] c)
    {
        var b = new Button(() => { Sound.I.UI("click"); click(); });
        foreach (var x in c.Prepend("m-card")) b.AddToClassList(x);
        b.RegisterCallback<MouseEnterEvent>(_ => Sound.I.UI("hover"));
        Div(b, "m-card-art");
        Div(b, "m-card-shade");
        Div(b, "m-card-stripes");
        var t = Div(b, "m-card-txt");
        if (kicker != null) Text(t, kicker.ToUpperInvariant(), "m-card-kicker");
        var tr = Div(t, "m-card-title-row");
        if (!string.IsNullOrEmpty(titleY)) Text(tr, titleY.ToUpperInvariant(), "m-card-title", "y");
        if (!string.IsNullOrEmpty(titleW)) Text(tr, titleW.ToUpperInvariant(), "m-card-title");
        if (sub != null) Text(t, sub.ToUpperInvariant(), "m-card-sub");
        Div(b, "m-card-frame").SendToBack();
        b.Q(className: "m-card-art").SendToBack();
        foreach (var e in b.Query().ToList()) if (e != b) e.pickingMode = PickingMode.Ignore;
        parent.Add(b);
        return b;
    }

    Button GameTile(VisualElement parent, GameId g, string kicker, Action click, params string[] c)
    {
        var b = Card(parent, click, kicker, null, Games.Name(g), GameInfo[g].meta, c);
        b.userData = g;
        GameArt(b.Q(className: "m-card-art"), g);
        return b;
    }

    // "Regles" de l'accueil : on choisit un jeu dans la liste, et on lit ses regles au lieu de lancer une partie.
    bool rulesPick;
    void OpenGame(GameId g)
    {
        game.SelectGame(g);
        if (rulesPick) { rulesPick = false; RefreshRules(); Go(rulesScreen); return; }
        RefreshSetup();
        Go(setup);
    }

    // --- Accueil (copie de la maquette "Speed Kills" du pack, mesures relevees sur l'image, ramenees en 1920x1080) ---
    readonly List<Button> homeItems = new List<Button>();
    readonly List<(VisualElement ico, Label txt)> soundPills = new List<(VisualElement, Label)>();

    void SoundPill(VisualElement p, string cls)
    {
        SkPill(p, cls, () => { game.settings.mute = !game.settings.mute; game.ApplySettings(); RefreshSoundBtns(); RefreshHomeSound(); }, out var ico, out var txt);
        soundPills.Add((ico, txt));
        RefreshHomeSound();
    }

    void BuildTitle()
    {
        title = Screen("hm");
        var pic = Div(title, "hm-pic");
        pic.pickingMode = PickingMode.Ignore;
        var art = Div(pic, "hm-art");
        art.pickingMode = PickingMode.Ignore;
        art.style.backgroundImage = menuArt;
        arts.Add(art);
        foreach (var v in new[] { "sk-tint", "sk-veil-l", "sk-veil-r", "sk-veil-t", "sk-veil-b" }) Div(title, v).pickingMode = PickingMode.Ignore;

        // Pastilles du haut (a la place des monnaies) : toi a gauche (clic = personnaliser), le son a droite.
        var me = SkPill(title, "sk-pill-l", () => OpenCreator(a => { game.SetMyAvatar(a); RefreshProfile(); }, game.myAvatar), out var meIco, out var meTxt);
        meIco.AddToClassList("sk-pill-face");
        profileChips.Add((meIco, meTxt));
        SoundPill(title, "sk-pill-r");

        // Grande affiche en haut a gauche.
        // (coins en perspective mesures sur la maquette : la grande affiche vient vers nous a gauche, celles de droite a droite)
        SkCard(title, GameId.Roue, "La Roue", "Nouveau jeu disponible", () => OpenGame(GameId.Roue), new Vector2(24, 24), new[] { 0f, 0f, 1f, 0.09f, 1f, 0.92f, 0f, 1f }, "sk-main", "left");

        // Le menu.
        var menu = Div(title, "sk-menu");
        HomeItem(menu, "Jouer", () => { rulesPick = false; gameFilter = -1; RefreshGames(); Go(games); }, true);
        HomeItem(menu, "En ligne", OpenOnline);
        HomeItem(menu, "Règles", () => { rulesPick = true; gameFilter = -1; RefreshGames(); Go(games); });
        HomeItem(menu, "Paramètres", () => { SelectTab(tab); Go(settingsScreen); });
        HomeItem(menu, "Quitter", Application.Quit);

        // Les affiches de droite.
        SkCard(title, GameId.Trivia, "Quiz de Tenna", null, () => OpenGame(GameId.Trivia), new Vector2(690, 24), new[] { 0f, 0.08f, 1f, 0f, 1f, 1f, 0f, 0.975f }, "sk-r1");
        SkCard(title, GameId.Croque, "Croque\nCarotte", "Le classique", () => OpenGame(GameId.Croque), new Vector2(690, 326), new[] { 0f, 0.05f, 1f, 0f, 1f, 1f, 0f, 0.965f }, "sk-r2");
        var perso = SkCard(title, GameId.Rhythm, "Crée ton\nperso", "Nouveaux personnages", () => OpenCreator(a => { game.SetMyAvatar(a); RefreshProfile(); }, game.myAvatar), new Vector2(24, 380), new[] { 0f, 0.035f, 1f, 0f, 1f, 1f, 0f, 0.93f }, "sk-r3");
        var pa = ((VisualElement)perso.userData).Q(className: "sk-card-art");
        foreach (var e in pa.Query(className: "art-icon").ToList()) e.RemoveFromHierarchy();
        pa.style.backgroundImage = StyleKeyword.Null;
        pa.style.backgroundColor = StyleKeyword.Null;
        var busts = new VisualElement { pickingMode = PickingMode.Ignore };
        busts.AddToClassList("sk-busts");
        pa.Insert(0, busts);
        var castCodes = LoadCast(Resources.Load<TextAsset>("MenuCast")?.text).ConvertAll(c => c.code);
        if (castCodes.Count == 0) castCodes = new List<string>(Chars.Friends.Values);
        foreach (var code in castCodes) Div(busts, "sk-bust").style.backgroundImage = Chars.Bust(code);
        var bang = Div(title, "sk-bang");
        Div(bang, "sk-bang-in");
        Div(bang, "sk-bang-ico").style.backgroundImage = Resources.Load<Texture2D>("MM/icon3d/ExclamationMark_01");
        foreach (var e in bang.Query().ToList()) e.pickingMode = PickingMode.Ignore;

        // Aide en bas, avec les touches du pack.
        var hint = Div(title, "sk-hint");
        hint.pickingMode = PickingMode.Ignore;
        var mouse = Div(hint, "sk-key", "round");
        Div(mouse, "sk-key-ico").style.backgroundImage = Resources.Load<Texture2D>("MM/input/Mouse_Left");
        Text(hint, "Choisir", "sk-key-txt");
        Text(Div(hint, "sk-key"), "ÉCHAP", "sk-key-cap");
        Text(hint, "Retour", "sk-key-txt");
        Text(title, "v" + Application.version, "hm-version");

        updateCard = Div(title, "m-box", "update-card");
        updateCard.style.display = DisplayStyle.None;
        updateText = Text(updateCard, "", "p");
        updateBtn = Btn(updateCard, "Mettre à jour", () =>
        {
            updateBtn.SetEnabled(false);
            StartCoroutine(Updater.Install(p => updateText.text = $"Téléchargement... {p * 100:0} %", err => { updateText.text = err; updateBtn.SetEnabled(true); }));
        }, "m-gold", "small");
    }

    void RefreshHomeSound()
    {
        foreach (var (ico, txt) in soundPills)
        {
            txt.text = game.settings.mute ? "MUET" : "SON";
            ico.style.backgroundImage = Resources.Load<Texture2D>("MM/flat/" + (game.settings.mute ? "Sound_01_Off" : "Sound_01_On"));
        }
    }

    // Pastille facon "monnaie" : une etoile violette (icone dedans) a cheval sur une barre sombre biseautee.
    Button SkPill(VisualElement p, string cls, Action click, out VisualElement ico, out Label txt)
    {
        var b = new Button(() => { Sound.I.UI("click"); click(); });
        b.AddToClassList("sk-pill");
        b.AddToClassList(cls);
        b.RegisterCallback<MouseEnterEvent>(_ => Sound.I.UI("hover"));
        Div(b, "sk-pill-bar");
        var star = Div(b, "sk-pill-star");
        ico = Div(star, "sk-pill-ico");
        txt = Text(b, "", "sk-pill-txt");
        foreach (var e in b.Query().ToList()) if (e != b) e.pickingMode = PickingMode.Ignore;
        p.Add(b);
        return b;
    }

    // Entree du menu : texte seul ; survolee (ou "Jouer" au depart), elle passe dans la barre a crochets.
    Button HomeItem(VisualElement menu, string text, Action click, bool main = false)
    {
        var b = new Button(() => { Sound.I.UI("click"); click(); });
        b.AddToClassList("sk-item");
        b.RegisterCallback<MouseEnterEvent>(_ =>
        {
            Sound.I.UI("hover");
            foreach (var o in homeItems) o.EnableInClassList("selected", o == b);
        });
        if (main) b.AddToClassList("selected");
        var br = Div(b, "sk-bracket");
        Div(br, "sk-bar");
        foreach (var k in new[] { "tl", "tr", "bl", "br" }) Div(br, "sk-corner", k);   // les crochets jaunes passent devant la barre
        foreach (var e in br.Query().ToList()) e.pickingMode = PickingMode.Ignore;
        Div(b, "sk-arrow").pickingMode = PickingMode.Ignore;
        Text(b, text.ToUpperInvariant(), "sk-item-label").pickingMode = PickingMode.Ignore;
        homeItems.Add(b);
        menu.Add(b);
        return b;
    }

    // Les affiches sont dessinees a plat dans une texture (un panneau UI hors ecran, 1,5 fois plus fin que l'ecran),
    // puis affichees en perspective (Warp). Le survol passe la classe "hover" a l'affiche a plat.
    const float PosterScale = 1.5f;
    UIDocument posterDoc;
    RenderTexture posterRT;

    VisualElement PosterRoot()
    {
        if (posterDoc) return posterDoc.rootVisualElement;
        posterRT = new RenderTexture(2048, 1024, 24, RenderTextureFormat.ARGB32) { name = "Affiches" };
        var ps = Instantiate(Resources.Load<PanelSettings>("UI/Panel"));
        ps.targetTexture = posterRT;
        ps.scaleMode = PanelScaleMode.ConstantPixelSize;
        ps.scale = PosterScale;
        ps.clearColor = true;
        ps.colorClearValue = new Color(0, 0, 0, 0);
        ps.sortingOrder = -100;
        var go = new GameObject("AffichesHorsEcran");
        go.SetActive(false);
        posterDoc = go.AddComponent<UIDocument>();
        posterDoc.panelSettings = ps;
        go.SetActive(true);
        var r = posterDoc.rootVisualElement;
        r.styleSheets.Add(Resources.Load<StyleSheet>("UI/Menu"));
        r.styleSheets.Add(Resources.Load<StyleSheet>("UI/Modern"));
        r.AddToClassList("root");
        r.pickingMode = PickingMode.Ignore;
        return r;
    }

    // Affiche : image du jeu etalonnee violet, trainee violette, titre jaune, cadre du pack ; a l'ecran en perspective.
    // corners : x,y des coins haut-gauche, haut-droit, bas-droit, bas-gauche (fractions). Renvoie le bouton (userData = l'affiche a plat).
    Button SkCard(VisualElement p, GameId g, string titleText, string sub, Action click, Vector2 atlasPos, float[] corners, params string[] c)
    {
        var flat = new VisualElement { pickingMode = PickingMode.Ignore };
        flat.AddToClassList("sk-card");
        foreach (var x in c) flat.AddToClassList(x);
        flat.style.left = atlasPos.x;
        flat.style.top = atlasPos.y;
        Div(flat, "sk-glow");
        Div(flat, "sk-glow-line");
        var art = Div(flat, "sk-card-art");
        GameArt(art, g);
        Div(art, "sk-card-brush");
        Div(art, "sk-card-brush", "b2");
        var t = Div(art, "sk-card-txt");
        Text(t, titleText.ToUpperInvariant(), "sk-card-title");
        if (sub != null) Text(t, sub.ToUpperInvariant(), "sk-card-sub");
        Div(flat, "sk-card-frame");
        Div(flat, "sk-card-frame", "inner");   // la grande affiche a un second cadre, plus fin, a l'interieur
        PosterRoot().Add(flat);
        foreach (var e in flat.Query().ToList()) e.pickingMode = PickingMode.Ignore;

        var b = new Button(() => { Sound.I.UI("click"); click(); }) { userData = flat };
        b.AddToClassList("sk-warp");
        foreach (var x in c) b.AddToClassList(x);
        b.RegisterCallback<MouseEnterEvent>(_ => { Sound.I.UI("hover"); flat.AddToClassList("hover"); });
        b.RegisterCallback<MouseLeaveEvent>(_ => flat.RemoveFromClassList("hover"));
        var w = new Warp
        {
            tex = posterRT, atlasPos = atlasPos, atlasSize = new Vector2(posterRT.width, posterRT.height) / PosterScale, pad = 24,
            corners = new[] { new Vector2(corners[0], corners[1]), new Vector2(corners[2], corners[3]), new Vector2(corners[4], corners[5]), new Vector2(corners[6], corners[7]) },
        };
        w.AddToClassList("sk-warp-mesh");
        b.Add(w);
        p.Add(b);
        return b;
    }

    // --- Choix du jeu -------------------------------------------------------------------------
    void BuildGames()
    {
        games = Page(out var body, out var foot, "À quoi", "on joue ?");
        var bar = Div(body, "m-tabs-row");
        string[] cats = { "Tous", "Jeux de société", "Casino", "TV Time" };
        for (int i = 0; i < cats.Length; i++)
        {
            int k = i - 1;
            var t = new Button(() => { Sound.I.UI("tick"); gameFilter = k; RefreshGames(); }) { text = cats[i].ToUpperInvariant() };
            t.AddToClassList("m-tab");
            Corners(t);
            bar.Add(t);
            gameTabs.Add(t);
        }
        Div(bar, "grow");
        gameCount = Text(bar, "", "m-count");
        Rule(body);
        // Grille dans une zone qui defile : elle ne deborde plus sur les onglets ni sur le pied de page.
        var scroll = Add(body, new ScrollView(ScrollViewMode.Vertical), "games-scroll");
        gameRow = Div(scroll.contentContainer, "games-grid");
        foreach (var g in GameInfo.Keys) GameCard(gameRow, g);
        Rule(body);
        Ico(Btn(foot, "Retour", Back, "m-dark", "small"), "back");
        Text(foot, "Survole un jeu pour lire sa description", "m-hint");
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
        for (int i = 0; i < gameTabs.Count; i++) gameTabs[i].EnableInClassList("selected", i - 1 == gameFilter);
        gameCount.text = n + (n > 1 ? " jeux" : " jeu");
    }

    void GameCard(VisualElement parent, GameId g)
    {
        var b = GameTile(parent, g, null, () => OpenGame(g), "game-tile");
        Text(b.Q(className: "m-card-txt"), GameInfo[g].desc, "m-card-desc").pickingMode = PickingMode.Ignore;
        if (Beta.Contains(g)) Text(b, "BETA", "m-beta").pickingMode = PickingMode.Ignore;
    }
    static readonly HashSet<GameId> Beta = new HashSet<GameId> { GameId.Paintball };   // jeux encore en test

    // --- Preparation d'une partie ---------------------------------------------------------------
    VisualElement setupArt;
    Label setupMeta, setupDesc;

    void BuildSetup()
    {
        setup = Page(out var body, out var foot, "Prêt à", "jouer ?");
        var cols = Div(body, "setup-cols");
        var left = Div(cols, "setup-left");
        var card = Div(left, "m-card", "setup-card");
        setupArt = Div(card, "m-card-art");
        Div(card, "m-card-shade");
        var t = Div(card, "m-card-txt");
        setupTitle = Text(t, "", "m-card-title", "y");
        setupMeta = Text(t, "", "m-card-sub");
        Div(card, "m-card-frame").SendToBack();
        setupArt.SendToBack();
        setupDesc = Text(left, "", "setup-desc");

        var right = Add(cols, new ScrollView(ScrollViewMode.Vertical), "setup-right");
        Head(right, "Options");
        setupOptions = Div(right, "row", "m-opts");
        setupThemes = Div(right, "themes");
        localTitle = Head(right, "Joueurs sur ce PC (chacun son tour)");
        playerList = Div(right);
        addPlayer = Ico(Btn(right, "Ajouter un joueur", () =>
        {
            game.names.Add("Joueur " + (game.names.Count + 1));
            game.avatars.Add(Game.Characters[(game.names.Count * 7) % Game.Characters.Length]);
            RefreshSetup();
        }, "m-dark", "small"), "plus");
        addPlayer.style.alignSelf = Align.FlexStart;
        botsRow = Div(right, "row", "m-strip");
        Text(botsRow, "Hors ligne contre des bots", "m-strip-label").style.flexGrow = 1;
        Btn(botsRow, "−", () => { game.quizBots = Mathf.Max(game.gameId == GameId.Limite ? 2 : 1, game.quizBots - 1); RefreshSetup(); }, "m-dark", "small", "square");
        botsLabel = Text(botsRow, "", "m-value");
        Btn(botsRow, "+", () => { game.quizBots = Mathf.Min(Quiz.MaxPlayers - 1, game.quizBots + 1); RefreshSetup(); }, "m-dark", "small", "square");
        Ico(Btn(botsRow, "Jouer contre les bots", () => game.StartQuizWithBots(), "m-gold", "small"), "play").style.marginLeft = 20;

        Ico(Btn(foot, "Retour", Back, "m-dark", "small"), "back");
        Ico(Btn(foot, "Règles", () => { RefreshRules(); Go(rulesScreen); }, "m-dark", "small"), "book");
        Div(foot, "grow");
        Ico(Btn(foot, "Jouer en ligne", OpenOnline, "m-blue"), "globe");
        localBtn = Ico(Btn(foot, "Jouer sur ce PC", () => game.StartGame(), "m-gold"), "monitor");
    }

    void RefreshSetupCard()
    {
        setupTitle.text = Games.Name(game.gameId).ToUpperInvariant();
        setupMeta.text = GameInfo[game.gameId].meta.ToUpperInvariant();
        setupDesc.text = GameInfo[game.gameId].desc;
        GameArt(setupArt, game.gameId);
    }

    // --- Parametres -------------------------------------------------------------------------------
    void BuildSettings()
    {
        settingsScreen = Page(out var body, out var foot, "Paramètres", null);
        var bar = Div(body, "m-tabs-row", "center");
        string[] names = { "Graphismes", "Audio", "Jeu", "Commandes" };
        for (int i = 0; i < names.Length; i++)
        {
            int k = i;
            var t = new Button(() => { Sound.I.UI("tick"); SelectTab(k); }) { text = names[i].ToUpperInvariant() };
            t.AddToClassList("m-tab");
            Corners(t);
            bar.Add(t);
            tabs.Add(t);
        }
        Rule(body, "06");
        settingsBody = Add(body, new ScrollView(ScrollViewMode.Vertical), "m-settings");
        Ico(Btn(foot, "Retour", Back, "m-dark", "small"), "back");
        Div(foot, "grow");
        Btn(foot, "Par défaut", () => { game.ResetSettings(); SelectTab(tab); }, "m-dark", "small");
    }

    VisualElement Setting(string label)
    {
        var row = Div(settingsBody, "m-set");
        Text(row, label, "m-set-label");
        return row;
    }

    // Choix a fleches : ◀ valeur ▶ (remplace les listes deroulantes).
    VisualElement Drop(string label, string[] choices, int index, Action<int> set)
    {
        var sel = Div(Setting(label), "m-sel");
        int i = Mathf.Clamp(index, 0, choices.Length - 1);
        Label val = null;
        void Step(int d) { i = (i + d + choices.Length) % choices.Length; val.text = choices[i]; Sound.I.UI("tick"); set(i); game.ApplySettings(); }
        Arrow(sel, true, () => Step(-1));
        var box = Div(sel, "m-sel-box");
        val = Text(box, choices[i], "m-sel-val");
        Arrow(sel, false, () => Step(1));
        return sel;
    }

    static void Arrow(VisualElement p, bool left, Action click)
    {
        var b = new Button(() => click());
        b.AddToClassList("m-arrow");
        b.AddToClassList(left ? "left" : "right");
        Div(b, "m-arrow-ico").pickingMode = PickingMode.Ignore;
        p.Add(b);
    }

    // Oui / Non en deux moities.
    void Check(string label, bool value, Action<bool> set)
    {
        var row = Div(Setting(label), "m-toggle");
        Button on = null, off = null;
        void Pick(bool v) { on.EnableInClassList("selected", v); off.EnableInClassList("selected", !v); }
        on = new Button(() => { Sound.I.UI("tick"); Pick(true); set(true); game.ApplySettings(); }) { text = "OUI" };
        off = new Button(() => { Sound.I.UI("tick"); Pick(false); set(false); game.ApplySettings(); }) { text = "NON" };
        on.AddToClassList("m-toggle-l"); off.AddToClassList("m-toggle-r");
        row.Add(on); row.Add(off);
        Pick(value);
    }

    // Curseur a fleches, la valeur est ecrite sur la poignee.
    void Range(string label, float min, float max, float value, Action<float> set, Func<float, string> fmt)
    {
        var box = Div(Setting(label), "m-sel");
        var s = new Slider(min, max) { value = value };
        s.AddToClassList("m-slider");
        var v = new Label(fmt(value)) { pickingMode = PickingMode.Ignore };
        v.AddToClassList("m-slider-val");
        Arrow(box, true, () => s.value = Mathf.Clamp(s.value - (max - min) / 20, min, max));
        box.Add(s);
        Arrow(box, false, () => s.value = Mathf.Clamp(s.value + (max - min) / 20, min, max));
        s.Q(className: "unity-base-slider__dragger")?.Add(v);
        s.RegisterValueChangedCallback(e => { v.text = fmt(e.newValue); set(e.newValue); game.ApplySettings(); });
    }

    void Info(string label, string keys) => Text(Setting(label), keys, "m-keys");

    // --- Regles -------------------------------------------------------------------------------------
    void BuildRules()
    {
        rulesScreen = Screen("m-center");
        Div(rulesScreen, "m-veil", "strong").pickingMode = PickingMode.Ignore;
        rulesTitle = Text(rulesScreen, "", "m-big-title");
        var box = Div(rulesScreen, "m-box", "rules-box");
        rulesBody = Add(box, new ScrollView(), "rules-scroll");
        Ico(Btn(rulesScreen, "Retour", Back, "m-gold", "small"), "back").style.marginTop = 26;
    }

    // --- Pause et victoire : un grand bandeau en travers de l'ecran ------------------------------------
    void BuildPause()
    {
        // Comme l'accueil : la partie reste visible sous un voile violet, menu en texte a crochets a gauche.
        pause = Screen("hm", "pause-scr");
        foreach (var v in new[] { "pause-veil", "sk-veil-l", "sk-veil-b" }) Div(pause, v).pickingMode = PickingMode.Ignore;
        var head = Div(pause, "pause-head");
        Text(head, "PAUSE", "m-title", "y");
        Text(head, "La partie t'attend", "pause-sub");
        var menu = Div(pause, "sk-menu", "pause-menu");
        HomeItem(menu, "Reprendre", Back, true);
        HomeItem(menu, "Règles", () => { RefreshRules(); Go(rulesScreen); });
        HomeItem(menu, "Paramètres", () => { SelectTab(tab); Go(settingsScreen); });
        pauseLobby = HomeItem(menu, "Retour au salon", () => game.net.QuitToLobby());
        pauseQuit = HomeItem(menu, "Quitter la partie", () => game.ToMenu());
    }

    void BuildVictory()
    {
        victory = Screen("m-center");
        Div(victory, "m-veil").pickingMode = PickingMode.Ignore;
        var band = Div(victory, "m-band", "win");
        var trophy = Div(band, "m-trophy");
        trophy.style.backgroundImage = Resources.Load<Texture2D>("MM/icon3d/Trophy_03");
        winTitle = Text(band, "", "m-band-title");
        Rule(band, "11");
        winSub = Text(band, "", "m-ranking");
        var row = Div(band, "row", "m-band-row");
        winMenu = Ico(Btn(row, "Menu principal", () => { if (game.Online) game.net.QuitToLobby(); else game.ToMenu(); }, "m-dark"), "exit");
        winMenu.style.marginRight = 20;
        replayBtn = Ico(Btn(row, "Rejouer !", () => game.Replay(), "m-gold", "lg"), "play");
    }

    // --- En ligne -------------------------------------------------------------------------------------
    void BuildOnline()
    {
        onlineScreen = Page(out var body, out var foot, "Jouer", "en ligne");
        onlineTitle = new Label();
        var me = Div(body, "row", "m-strip", "online-me");
        myPortraitBox = Div(me);
        Text(me, "Ton pseudo", "m-strip-label");
        onlineName = Add(me, new TextField { value = PlayerPrefs.GetString("cc-name", "Joueur"), maxLength = 16 }, "m-field");
        onlineName.RegisterCallback<FocusOutEvent>(_ => SaveName());
        var cols = Div(body, "online-cols");

        var create = Div(cols, "m-box", "online-card");
        Div(create, "online-ico").style.backgroundImage = Resources.Load<Texture2D>("MM/icon3d/Connection_01");
        Text(create, "Créer un salon", "m-box-title");
        Text(create, "Tu reçois un code (ou un message d'invitation) à envoyer à tes amis, sur Discord par exemple. Dans le salon, tu choisis le jeu.", "m-box-text");
        Div(create, "grow");
        Ico(Btn(create, "Créer un salon", () => { SaveName(); game.net.Host(onlineName.value, game.gameId, game.option); }, "m-gold"), "globe");

        var join = Div(cols, "m-box", "online-card");
        Div(join, "online-ico").style.backgroundImage = Resources.Load<Texture2D>("MM/icon3d/Message_01");
        Text(join, "Rejoindre un salon", "m-box-title");
        Text(join, "Tape le code, ou colle le message d'invitation reçu.", "m-box-text");
        codeField = Add(join, new TextField { maxLength = 300 }, "m-field", "code-field");   // code, ou message d'invitation colle en entier
        Div(join, "grow");
        Ico(Btn(join, "Rejoindre", () => { SaveName(); game.net.Join(codeField.value, onlineName.value); }, "m-blue"), "play");

        onlineStatus = Text(body, "", "m-status");
        Ico(Btn(foot, "Retour", Back, "m-dark", "small"), "back");
    }

    void BuildLobby()
    {
        lobbyScreen = Page(out var body, out var foot, "Salon", null);
        var cols = Div(body, "lobby2");
        var left = Div(cols, "m-box", "lobby2-left");
        var head = Div(left, "row", "spread");
        Text(head, "Joueurs", "m-box-title");
        lobbyCount = Text(head, "", "m-count");
        lobbyList = Add(left, new ScrollView(), "lobby2-list");
        var codeRow = Div(left, "row", "lobby2-code");
        Text(codeRow, "Code", "m-strip-label");
        lobbyCode = Text(codeRow, "", "m-code");
        Ico(Btn(codeRow, "", () => GUIUtility.systemCopyBuffer = game.net.Code, "m-blue", "small", "square"), "copy").tooltip = "Copier le code";
        inviteBtn = Ico(Btn(left, "Copier l'invitation (Discord)", () => { GUIUtility.systemCopyBuffer = game.net.Invite; lobbyStatus.text = "Invitation copiée : colle-la dans Discord !"; }, "m-dark", "small"), "copy");

        var right = Add(cols, new ScrollView(ScrollViewMode.Vertical), "lobby2-right");
        lobbyPick = Div(right, "lobby2-pick");
        var gc = lobbyGameCard = Div(right, "row", "m-strip", "lobby2-game");
        lobbyArt = Div(gc, "lobby2-art");
        var gt = Div(gc);
        lobbyGame = Text(gt, "", "m-box-title");
        lobbyMeta = Text(gt, "", "m-box-text");
        lobbyOptions = Div(right, "row", "m-opts");
        lobbyThemes = Div(right, "themes", "lobby-themes");

        Ico(Btn(foot, "Quitter le salon", Back, "m-dark", "small", "danger"), "exit");
        lobbyStatus = Text(foot, "", "m-status");
        lobbyStatus.style.flexGrow = 1;
        startBtn = Ico(Btn(foot, "Lancer la partie", () => game.net.StartMatch(), "m-gold", "lg"), "play");
    }
}
