using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Tous les ecrans (UI Toolkit, construits en code, styles dans Resources/UI/Menu.uss).
public partial class Ui : MonoBehaviour
{
    Game game;
    VisualElement root, title, games, setup, creator, settingsScreen, rulesScreen, hud, pause, victory, onlineScreen, lobbyScreen;
    VisualElement current;
    readonly Stack<VisualElement> history = new Stack<VisualElement>();

    public bool Has(string cls) => root.Q(className: cls) != null;   // autotest
    public void Init(Game g, UIDocument doc)
    {
        game = g;
        root = doc.rootVisualElement;
        root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Menu"));
        root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Modern"));
        root.AddToClassList("root");
        root.pickingMode = PickingMode.Ignore;
        // Calque fixe de l'illustration, derriere tous les ecrans de menu : il ne disparait jamais entre deux ecrans
        // (sinon, pendant le fondu, on voit la scene 3D derriere).
        bgArt = Div(root, "m-art", "bg-art");
        bgArt.pickingMode = PickingMode.Ignore;
        arts.Add(bgArt);
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
        BuildCreator();
        // Menus : pas de barres de defilement (la molette suffit).
        foreach (var sv in root.Query<ScrollView>().ToList())
        {
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        }
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
    static readonly string[] PlainButtons = { "chip-btn", "round-act", "qz-choice", "uno-card" };

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
            if (c.Any(x => x.StartsWith("m-"))) Corners(b);   // menus violets : crochets au survol
        }
        return Add(p, b, c);
    }

    // Les 4 crochets jaune-vert du pack (visibles au survol / a la selection, cf. Modern.uss).
    static void Corners(VisualElement b)
    {
        // un bouton-texte qui a des enfants ne mesure plus son texte : on lui donne sa largeur
        if (b is Button bt && !string.IsNullOrEmpty(bt.text)) bt.style.minWidth = 64 + 17 * bt.text.Length;
        foreach (var k in new[] { "tl", "tr", "bl", "br" })
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("br-c"); e.AddToClassList(k);
            b.Add(e);
        }
    }

    static VisualElement Face(Button b) => b.Q(className: "gl-face");
    static string Label(Button b) => b.Q<Label>(className: "gl-label")?.text ?? b.text;

    // Icone blanche devant le texte d'un bouton : celle du pack Modern Menus (Synty) si on l'a, sinon la notre (Resources/UI/Icons).
    static readonly Dictionary<string, string> PackIcons = new Dictionary<string, string>
    {
        ["play"] = "Play_01", ["globe"] = "Connection_01", ["gear"] = "Settings_04", ["exit"] = "Cancel_01", ["back"] = "Arrow_Back_01",
        ["book"] = "Book_01", ["check"] = "Confirm_01", ["plus"] = "Plus_01", ["sound"] = "Sound_01_On", ["mute"] = "Sound_01_Off",
        ["monitor"] = "Display_01", ["user"] = "Star_01",
    };
    static Button Ico(Button b, string icon)
    {
        var face = Face(b);
        var i = face.Q(className: "gl-ico");
        if (i == null) { i = new VisualElement { pickingMode = PickingMode.Ignore }; i.AddToClassList("gl-ico"); face.Insert(1, i); }
        i.style.backgroundImage = PackIcons.TryGetValue(icon, out var pk) && Resources.Load<Texture2D>("MM/flat/" + pk) is Texture2D t ? t : Resources.Load<Texture2D>("UI/Icons/" + icon);
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
    void RefreshSoundBtns() { foreach (var b in soundBtns) Ico(b, game.settings.mute ? "mute" : "sound"); RefreshHomeSound(); }

    // Portrait cliquable d'un personnage.
    VisualElement Portrait(VisualElement p, string avatar, Action onClick, float size = 64)
    {
        VisualElement e = onClick != null ? new Button(() => { Sound.I.UI("click"); onClick(); }) : new VisualElement();
        e.AddToClassList("portrait");
        e.style.width = size;
        e.style.height = size;
        e.style.backgroundImage = Chars.Portrait(avatar);
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

    VisualElement[] All => new[] { title, games, setup, creator, settingsScreen, rulesScreen, hud, pause, victory, onlineScreen, lobbyScreen };

    // Navigation entre ecrans de menu, avec retour arriere.
    void Go(VisualElement s, bool remember = true)
    {
        if (current != null)
        {
            if (remember) history.Push(current);
            if (current == creator && s != creator) studio?.Show(false);   // le studio ne tourne que pendant le createur
            Hide(current);
        }
        current = s;
        bgArt.style.display = (s.ClassListContains("m-page") || s.ClassListContains("hm")) && !s.ClassListContains("pause-scr") ? DisplayStyle.Flex : DisplayStyle.None;
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
        else if (screen == "avatar") OpenCreator(a => { }, null);
        else if (screen == "settings") { SelectTab(1); Go(settingsScreen); }
        else if (screen == "online") OpenOnline();
        else if (screen == "creator") OpenCreator(a => { }, null);
        else if (screen.StartsWith("crtab:")) CreatorTab(screen.Substring(6));
        else if (screen == "crrandom") { crLook = Sidekick.Random(new System.Random(7)); CreatorRefresh(); }
        else if (screen == "crcolor") { crColorOpen = 7; CreatorRefresh(); }
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

    VisualElement updateCard;
    Label updateText;
    Button updateBtn;

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
        [GameId.Chevaux] = ("chevaux", 0, "2 à 4 joueurs · Plateau", "Un 6 pour sortir, fais le tour du plateau et grimpe l'escalier jusqu'au centre !"),
        [GameId.BonnePaye] = ("bonnepaye", 0, "2 à 6 joueurs · Plateau", "Factures, affaires, loterie et Jour de paye : le plus riche à la fin du mois gagne !"),
        [GameId.Serpents] = ("serpents", 0, "2 à 4 joueurs · Plateau", "Grimpe aux échelles, évite les serpents : vise la case 100 !"),
        [GameId.Roue] = ("roue", 2, "1 à 6 joueurs · Lettres", "Tourne la roue, trouve les lettres... et gare à la banqueroute !"),
        [GameId.QuiSuisJe] = ("quisuisje", 0, "1 à 8 joueurs · Devinettes", "Un post-it sur le front : devine qui tu es ! Contre des bots ou en ligne."),
        [GameId.LoupGarou] = ("loupgarou", 0, "4 à 12 joueurs · Rôles cachés", "La nuit, les loups dévorent. Le jour, le village vote. 23 rôles : qui ment ?"),
        [GameId.Limite] = ("limite", 0, "3 à 10 joueurs · Cartes", "Une question à trou, vos pires réponses : le Boss choisit sa préférée. Humour noir !"),
        [GameId.Pouilleux] = ("pouilleux", 0, "2 à 6 joueurs · Cartes", "Pioche chez ton voisin, jette tes paires... et ne finis pas avec le valet de pique !"),
        [GameId.Uno] = ("uno", 0, "2 à 10 joueurs · Cartes", "Même couleur ou même symbole, et n'oublie pas de crier UNO !"),
        [GameId.Blackjack] = ("blackjack", 1, "2 à 4 joueurs · Cartes", "Approche-toi de 21 sans dépasser et bats le croupier."),
        [GameId.Roulette] = ("roulette", 1, "1 à 4 joueurs · Casino", "Pleins, chevaux, carrés, rouge ou noir... Le plus riche gagne."),
        [GameId.Quiz] = ("quiz", 2, "1 à 10 joueurs · Images", "Une image floutée se dévoile : films, jeux, drapeaux, pochettes..."),
        [GameId.Trivia] = ("trivia", 2, "1 à 10 joueurs · 14 catégories", "Maths, logos, rébus, géo... avec Tenna !"),
        [GameId.Bac] = ("bac", 2, "1 à 10 joueurs · Mots", "Une lettre, des catégories : le premier qui a tout crie STOP !"),
        [GameId.Rhythm] = ("rhythm", 2, "1 à 10 joueurs · Musique", "121 chansons de Deltarune et Undertale : tout le monde joue en rythme, en même temps !"),
    };

    int gameFilter = -1;
    VisualElement gameRow;
    Label gameCount;
    readonly List<Button> gameTabs = new List<Button>();

    // --- Preparation d'une partie (options + joueurs) -------------------------------------
    VisualElement setupOptions, playerList, localTitle, localBtn, botsRow;
    Label botsLabel;
    Label setupTitle;
    Button addPlayer;

    VisualElement setupThemes, lobbyThemes;

    // Grand quiz : familles de themes a cocher (au moins une reste cochee).
    void ThemeChips(VisualElement parent, GameId g, int option, Action<int> pick, string text = null, Action<string> setText = null)
    {
        parent.Clear();
        parent.style.display = g == GameId.Trivia || g == GameId.Uno || g == GameId.Bac || g == GameId.QuiSuisJe || g == GameId.Limite || g == GameId.LoupGarou ? DisplayStyle.Flex : DisplayStyle.None;
        if (g == GameId.LoupGarou)
        {
            Text(parent, "Rôles spéciaux (les loups s'ajoutent tout seuls : 1 pour 4 joueurs)", "h2");
            var wrow = Div(parent, "row", "theme-row");
            foreach (var r in LoupGarou.Specials)
            {
                int bit = LoupGarou.Bit(r);
                var chip = new Button(() => { Sound.I.UI("tick"); pick(option ^ bit); }) { text = LoupGarou.Name(r) };
                chip.AddToClassList("theme-chip");
                chip.AddToClassList(LoupGarou.TeamOf(r) == Team.Loups ? "wg-chip-loup" : LoupGarou.TeamOf(r) == Team.Seul ? "wg-chip-seul" : "wg-chip-village");
                chip.EnableInClassList("selected", (option & bit) != 0);
                wrow.Add(chip);
            }
            Text(parent, "Lieu", "h2");
            var mrow = Div(parent, "row", "theme-row");
            int cur = LoupGarou.MapOf(option);
            for (int k = 0; k <= AgrouMap.All.Length; k++)
            {
                int m = k == AgrouMap.All.Length ? AgrouMap.Random : k;
                if (m != AgrouMap.Random && !AgrouMap.Available(m)) continue;
                var chip = new Button(() => { Sound.I.UI("tick"); pick(LoupGarou.WithMap(option, m)); }) { text = m == AgrouMap.Random ? "Au hasard" : AgrouMap.All[m].name };
                chip.AddToClassList("theme-chip");
                chip.EnableInClassList("selected", cur == m);
                mrow.Add(chip);
            }
            return;
        }
        if (g == GameId.Limite)
        {
            Text(parent, "Points pour gagner", "h2");
            var lrow = Div(parent, "row", "theme-row");
            for (int k = 0; k < Limite.Targets.Length; k++)
            {
                int kk = k;
                var chip = new Button(() => { Sound.I.UI("tick"); pick((option & ~(3 << 1)) | (kk << 1)); }) { text = Limite.Targets[k] + " points" };
                chip.AddToClassList("theme-chip");
                chip.EnableInClassList("selected", System.Math.Min(2, (option >> 1) & 3) == k);
                lrow.Add(chip);
            }
            return;
        }
        if (g == GameId.QuiSuisJe)
        {
            Text(parent, "Nombre de manches (une manche = jusqu'à ce que tout le monde ait trouvé)", "h2");
            var trow = Div(parent, "row", "theme-row");
            foreach (int k in new[] { 0, 1, 2, 3 })
            {
                int kk = k;
                var chip = new Button(() => { Sound.I.UI("tick"); pick((option & ~(3 << 5)) | (kk << 5)); }) { text = QuiSuisJe.RoundChoices[k] + (QuiSuisJe.RoundChoices[k] > 1 ? " manches" : " manche") };
                chip.AddToClassList("theme-chip");
                chip.EnableInClassList("selected", ((option >> 5) & 3) == k);
                trow.Add(chip);
            }
            return;
        }
        if (g == GameId.Bac)
        {
            // Categories : a cocher dans la liste, et en saisie libre (separees par des virgules).
            Text(parent, "Catégories", "h2");
            var brow = Div(parent, "row", "theme-row");
            for (int i = 0; i < PetitBac.AllCategories.Length; i++)
            {
                int bit = 1 << (i + 2);
                var chip = new Button(() => { Sound.I.UI("tick"); pick(option ^ bit); }) { text = PetitBac.AllCategories[i] };
                chip.AddToClassList("theme-chip");
                chip.EnableInClassList("selected", (option & bit) != 0);
                brow.Add(chip);
            }
            var field = Add(parent, new TextField { value = string.Join(", ", PetitBac.Customs(text)), maxLength = 160 }, "bac-custom");
            field.textEdition.placeholder = "Tes catégories à toi, séparées par des virgules (ex. : Super-héros, Dessert)";
            void Save() { var t = string.Join(";", PetitBac.Customs(field.value)); if (t != (text ?? "")) setText?.Invoke(t); }
            field.RegisterCallback<FocusOutEvent>(_ => Save());
            field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Save(); }, TrickleDown.TrickleDown);
            int n = Enumerable.Range(0, PetitBac.AllCategories.Length).Count(i => PetitBac.HasCat(option, i)) + PetitBac.Customs(text).Count;
            Text(parent, n < 3 ? "Moins de 3 catégories : on complète au hasard jusqu'à 6." : $"{Math.Min(n, PetitBac.MaxCats)} catégories" + (n > PetitBac.MaxCats ? $" (10 au plus)" : "") + ".", "muted");
            return;
        }
        if (g == GameId.Uno)
        {
            // Regles maison du UNO (comme dans le jeu d'Ubisoft) ; la contestation du +4 est toujours la.
            Text(parent, "Règles maison", "h2");
            var urow = Div(parent, "row", "theme-row");
            (int bit, string name, string desc)[] rules =
            {
                (Uno.OptStacking, "Cumul", "Un +2 se contre avec un +2 (ou un +4), un +4 avec un +4 : le suivant pioche tout !"),
                (Uno.OptSevenZero, "7-0", "Un 7 : tu échanges ta main avec qui tu veux. Un 0 : toutes les mains tournent."),
                (Uno.OptJumpIn, "Intervention", "Tu as la carte identique à celle du dessus ? Pose-la même si ce n'est pas ton tour !"),
                (Uno.OptDrawMatch, "Piocher jusqu'à jouer", "On pioche jusqu'à trouver une carte qui va."),
                (Uno.OptForcePlay, "Jeu forcé", "Une carte piochée qui va doit être posée."),
            };
            var desc = new List<string>();
            foreach (var (bit, name, d) in rules)
            {
                bool on = (option & bit) != 0;
                var chip = new Button(() => { Sound.I.UI("tick"); pick(option ^ bit); }) { text = name };
                chip.AddToClassList("theme-chip");
                chip.EnableInClassList("selected", on);
                urow.Add(chip);
                if (on) desc.Add(d);
            }
            desc.Add("Contestation du +4 : toujours active, comme dans le vrai jeu.");
            Text(parent, string.Join("\n", desc), "muted").style.whiteSpace = WhiteSpace.Normal;
            return;
        }
        if (g != GameId.Trivia) return;
        Text(parent, "Thèmes", "h2");
        var row = Div(parent, "row", "theme-row");
        int all = (1 << Quiz.Families.Length) - 1;
        int mask = Quiz.ThemeMask(option) == 0 ? all : Quiz.ThemeMask(option);
        for (int i = 0; i < Quiz.Families.Length; i++)
        {
            int bit = 1 << i;
            bool on = (mask & bit) != 0;
            var b = new Button(() =>
            {
                Sound.I.UI("tick");
                int m = mask ^ bit;
                if (m == 0) return;                                 // jamais zero theme
                pick((option & 1) | ((m == all ? 0 : m) << 1));
            }) { text = Quiz.Families[i].label };
            b.AddToClassList("theme-chip");
            b.EnableInClassList("selected", on);
            row.Add(b);
        }
    }

    void OptionCards(VisualElement parent, int current, Action<int> pick)
    {
        // Grand quiz : les cartes ne changent que le mode (bit 0), les themes sont gardes.
        if (game.gameId == GameId.Trivia) { int keep = current & ~1; var raw = pick; pick = v => raw(v | keep); current &= 1; }
        if (game.gameId == GameId.LoupGarou) { parent.Clear(); return; }   // tout est dans les roles (ThemeChips)
        if (game.gameId == GameId.Limite) { int keep = current & ~1; var raw = pick; pick = v => raw(v | keep); current &= 1; }
        if (game.gameId == GameId.QuiSuisJe) { int keep = current & ~1; var raw = pick; pick = v => raw(v | keep); current &= 1; }
        if (game.gameId == GameId.Uno || game.gameId == GameId.Bac) { int keep = current & ~3; var raw = pick; pick = v => raw(v | keep); current &= 3; }   // les regles maison sont gardees
        parent.Clear();
        if (game.gameId == GameId.Rhythm) { SongPicker(parent, current, pick); return; }
        if (game.gameId == GameId.BonnePaye)   // nombre de mois : 1 a 24
        {
            int months = BonnePaye.Months(current);
            var box = Div(parent, "mode-card", "selected", "bp-months");
            Text(box, "Durée de la partie", "mode-name");
            var row = Div(box, "row", "bp-months-row");
            Btn(row, "−", () => pick(Mathf.Max(0, current - 1)), "ghost", "small", "bp-step").SetEnabled(months > 1);
            Text(row, months > 1 ? $"{months} mois" : "1 mois", "bp-months-value");
            Btn(row, "+", () => pick(Mathf.Min(BonnePaye.MaxMonths - 1, current + 1)), "ghost", "small", "bp-step").SetEnabled(months < BonnePaye.MaxMonths);
            Text(box, "Un mois = un tour de plateau (environ 20 min à 4). On pourra prolonger à la fin.", "mode-desc");
            return;
        }
        var opts = game.gameId == GameId.Roue
            ? new[] { (0, "4 manches", "Comme à la télé : 4 manches puis la finale."), (1, "3 manches", "Un peu plus court, finale comprise."), (2, "2 manches", "Partie express, finale comprise.") }
            : game.gameId == GameId.QuiSuisJe
            ? new[] { (0, "Classique", "En ligne : chacun écrit le personnage de son voisin, questions à l'écrit ou à l'oral, les autres votent. Contre des bots : personnages et questions à choisir dans une liste.") }
            : game.gameId == GameId.Limite
            ? new[] { (0, "Paquet complet", "Le vrai Limite Limite : humour très noir, pour adultes."), (1, "Paquet Streamer", "Plus soft, pour jouer en stream ou en famille (enfin presque).") }
            : game.gameId == GameId.Pouilleux
            ? new[] { (0, "Classique", "Le valet de pique est le pouilleux : ne le garde pas !") }
            : game.gameId == GameId.Serpents
            ? new[] { (0, "Classique", "10 échelles, 10 serpents, il faut tomber pile sur 100.") }
            : game.gameId == GameId.Bac
            ? new[] { (1, "Partie courte", "3 manches."), (0, "Partie normale", "5 manches."), (2, "Longue partie", "8 manches.") }
            : game.gameId == GameId.Chevaux
            ? new[] { (0, "4 chevaux", "La partie complète."), (1, "3 chevaux", "Un peu plus courte."), (2, "2 chevaux", "Partie moyenne."), (3, "1 cheval", "Partie express !") }
            : game.gameId == GameId.Uno
            ? new[] { (0, "Une manche", "Le premier qui n'a plus de cartes gagne."), (1, "Partie courte", "Premier à 200 points."), (2, "Partie officielle", "Premier à 500 points, comme la règle.") }
            : game.gameId == GameId.Croque
            ? new[] { (0, "Classique", "19 cases en spirale. La carotte ouvre 1 à 3 trous au hasard."), (1, "Amélioré", "25 cases, trous selon un cycle secret à deviner.") }
            : game.gameId == GameId.Trivia
            ? new[] { (0, "QCM", "4 propositions, une seule réponse : la bonne et vite !"), (1, "Réponse libre", "Tape la réponse toi-même, autant d'essais que tu veux.") }
            : game.gameId == GameId.Quiz
            ? new[] { (0, "Flou", "L'image est floue puis se précise."), (1, "Pixelisé", "De gros pixels qui s'affinent."), (2, "Mélangé", "Flou ou pixels, au hasard à chaque image."), (3, "Image nette", "Ni flou ni pixels : l'image brute, le plus rapide gagne !") }
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
        RefreshSetupCard();
        OptionCards(setupOptions, game.option, v => { game.option = v; RefreshSetup(); });
        ThemeChips(setupThemes, game.gameId, game.option, v => { game.option = v; RefreshSetup(); }, game.bacText, t => { game.bacText = t; RefreshSetup(); });
        while (game.avatars.Count < game.names.Count) game.avatars.Add(Chars.Default);
        playerList.Clear();
        for (int i = 0; i < game.names.Count; i++)
        {
            int idx = i;
            var row = Div(playerList, "m-strip", "m-player");
            Ring(Portrait(row, game.avatars[i], () => OpenCreator(a => { game.avatars[idx] = a; RefreshSetup(); }, game.avatars[idx]), 60), Board.Colors[i]);
            var field = Add(row, new TextField { value = game.names[i], maxLength = 16 }, "m-field");
            field.RegisterValueChangedCallback(e => game.names[idx] = e.newValue);
            var rm = Btn(row, "Retirer", () => { game.names.RemoveAt(idx); game.avatars.RemoveAt(idx); RefreshSetup(); }, "m-dark", "small", "danger");
            rm.style.marginLeft = 12;
            rm.SetEnabled(game.names.Count > 2);
        }
        addPlayer.style.display = game.names.Count < Rules.MaxPlayers ? DisplayStyle.Flex : DisplayStyle.None;
        // Le quiz se joue en ligne (chacun tape sur son PC) : pas de joueurs locaux.
        bool local = !Games.WithBots(game.gameId);
        foreach (var e in new[] { localTitle, playerList, localBtn }) e.style.display = local ? DisplayStyle.Flex : DisplayStyle.None;
        if (!local) addPlayer.style.display = DisplayStyle.None;
        botsRow.style.display = local ? DisplayStyle.None : DisplayStyle.Flex;
        botsLabel.text = game.quizBots + (game.quizBots > 1 ? " bots" : " bot");
    }

    // --- Choix du personnage : le createur (UiCreator) ---
    VisualElement PickCell(VisualElement parent, string id, string label, float size, Action click)
    {
        var cell = Div(parent, "avatar-cell");
        var b = new Button(() => { Sound.I.UI("tick"); click(); });
        b.AddToClassList("portrait");
        b.style.width = b.style.height = size;
        b.style.backgroundImage = Chars.Portrait(id);
        cell.Add(b);
        if (label != null) Text(cell, label, "avatar-name");
        return cell;
    }

    // --- Parametres -------------------------------------------------------------------
    VisualElement settingsBody;
    readonly List<Button> tabs = new List<Button>();
    int tab;

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
                Text(settingsBody, "Croque-Carotte", "m-h");
                Info("Piocher une carte", "Espace");
                Info("Choisir un lapin", "1  ·  2  ·  3");
                Text(settingsBody, "Blackjack", "m-h");
                Info("Tirer / Rester", "H  ·  S");
                Info("Doubler / Séparer", "D  ·  P");
                Text(settingsBody, "Partout", "m-h");
                Info("Tourner la caméra", "Clic droit + glisser  ·  Q / E");
                Info("Zoomer", "Molette");
                Info("Pause", "Échap");
                break;
        }
    }

    static string Pct(float v) => Mathf.RoundToInt(v * 100) + " %";

    // --- Regles -----------------------------------------------------------------------
    VisualElement rulesBody;
    Label rulesTitle;

    void RefreshRules()
    {
        rulesTitle.text = "Règles : " + Games.Name(game.gameId);
        rulesBody.Clear();
        void S(string h, string p) { Text(rulesBody, h, "m-h"); Text(rulesBody, p, "m-p"); }
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
            S("Le but", "Tenna pose des questions dans 14 catégories : maths, culture G, français, qui est-ce, logos, code de la route, 4 images 1 mot, rébus, psychotechnique, classement, géo + date, cris d'animaux, têtes floutées et jeux vidéo. Choisis celles que tu veux ! Le premier à 100 points gagne.");
            S("Classement", "Clique les 4 réponses dans l'ordre demandé, de la première à la dernière (ou touches 1 à 4). Reclique une réponse pour reprendre à partir d'elle. Un seul essai !");
            S("Géo + Date", "Une photo d'un lieu célèbre : clique sur la carte là où il se trouve, règle l'année de sa construction, puis Valider. Jusqu'à 6 points pour le lieu et 6 pour l'année : plus tu es proche, plus tu marques.");
            S("QCM", "Quatre propositions : clique sur la tienne ou appuie sur 1, 2, 3 ou 4. Une seule réponse par question : si tu te trompes, tu attends la suivante.");
            S("Réponse libre", "Tape ta réponse puis Entrée, autant de fois que tu veux pendant les 20 secondes. Les petites fautes de frappe sont acceptées, et tout le monde voit tes mauvaises réponses !");
            S("Les points", "Plus tu réponds vite, plus tu gagnes : 10 points tout de suite, 3 à la dernière seconde, et 2 de bonus pour le premier. Après chaque question, Tenna donne la réponse et une petite anecdote.");
            S("Les questions", "Culture G et français : OpenQuizzDB (openquizzdb.org, CC BY-SA). Personnalités, lieux, logos, animaux, classements et jeux vidéo : Wikidata ; images et sons : Wikimedia Commons (licences libres) ; rébus : emoji Noto (Apache 2.0).");
        }
        else if (game.gameId == GameId.Quiz)
        {
            S("Le but", "Une image apparaît sur l'écran géant, floutée ou pixelisée, et se précise peu à peu. Devine ce que c'est avant les autres ! Le premier à 100 points gagne.");
            S("Les catégories", "Films et séries (des scènes, pas les affiches), anime, jeux vidéo, pochettes d'album, drapeaux, photos et personnalités.");
            S("Répondre", "Tape ta réponse puis Entrée, autant de fois que tu veux pendant les 20 secondes. Le titre français ou original, les abréviations connues (GTA, AoT...) et les petites fautes de frappe sont acceptés. Les mauvaises réponses de chacun s'affichent pour tout le monde.");
            S("Les points", "Plus tu trouves vite, plus tu gagnes : 10 points tout de suite, 3 à la dernière seconde, et 2 de bonus pour le premier qui trouve.");
        }
        else if (game.gameId == GameId.Chevaux)
        {
            S("Le but", "Chaque joueur a de 1 à 4 chevaux dans son écurie (au choix de l'hôte). Fais-leur faire le tour du plateau (56 cases, dans le sens des aiguilles d'une montre) puis monte ton escalier jusqu'au centre. Le premier qui y rentre tous ses chevaux gagne.");
            S("À ton tour", "Lance le dé : maintiens le clic pour le prendre en main, puis lâche-le d'un geste vers le plateau (ou appuie sur Espace, ou sur le bouton). Choisis ensuite le cheval qui avance : clique dessus, ou appuie sur 1, 2, 3 ou 4. Les chevaux qui peuvent bouger sautillent. Si aucun ne peut bouger, tu passes ton tour.");
            S("Le 6", "Il faut faire un 6 pour sortir un cheval de l'écurie : il se pose sur la case de départ de sa couleur. Et un 6 fait rejouer ! Mais attention : trois 6 de suite, et tu dois renvoyer un de tes chevaux en jeu à l'écurie (celui de ton choix).");
            S("Manger", "Si ton cheval s'arrête pile sur la case d'un cheval adverse, celui-ci retourne à son écurie. On ne peut pas passer par-dessus un cheval adverse : il faut faire le compte exact pour tomber dessus. Si tu arrives sur (ou dépasses) un de tes propres chevaux, tu t'arrêtes juste derrière lui.");
            S("L'escalier", "Il faut le compte exact pour s'arrêter au pied de ton escalier (la case juste avant ton départ) : si le dé est trop fort, le cheval va jusqu'au pied puis recule de ce qui dépasse. Ensuite, il faut faire 1 pour monter sur la 1re marche, 2 pour la 2e, et ainsi de suite jusqu'à 6 pour la 6e marche. Un dernier 6 et le cheval arrive au centre !");
        }
        else if (game.gameId == GameId.Uno)
        {
            S("Le but", "Être le premier à se débarrasser de toutes ses cartes. Chacun reçoit 7 cartes ; on pose à tour de rôle une carte de la même couleur ou du même chiffre (ou symbole) que celle du dessus de la défausse.");
            S("Si tu ne peux pas jouer", "Pioche une carte. Si elle peut être posée, tu peux la jouer tout de suite ; sinon tu la gardes et c'est au suivant.");
            S("Les cartes spéciales", "+2 : le suivant pioche 2 cartes et passe son tour. Inversion : le sens du jeu change (à deux, tu rejoues). Passe : le suivant ne joue pas. Joker : tu choisis la couleur. +4 : tu choisis la couleur, le suivant pioche 4 cartes et passe son tour.");
            S("UNO !", "Quand il ne te reste qu'une carte, appuie sur « UNO ! » (tu peux le faire juste avant de poser ton avant-dernière carte). Si tu oublies et qu'un autre joueur appuie sur « Contre-UNO ! » avant que le suivant joue, tu pioches 2 cartes.");
            S("Les points", "Celui qui finit marque les cartes restées dans la main des autres : leur chiffre pour les cartes numérotées, 20 points pour +2, inversion et passe, 50 points pour les jokers et les +4. Selon l'option : une seule manche, premier à 200 points, ou premier à 500 points (règle officielle).");
            S("Contester un +4", "On n'a le droit de poser un +4 que si on n'a aucune carte de la couleur demandée. Le joueur visé peut « Dénoncer » : si c'était du bluff, le poseur pioche 4 cartes à sa place ; si le +4 était réglo, celui qui a dénoncé en pioche 6 !");
            S("Règles maison (options)", "Cumul : un +2 se contre avec un +2 (ou un +4), un +4 avec un +4, et le suivant pioche le total. 7-0 : un 7 échange ta main avec le joueur de ton choix, un 0 fait tourner toutes les mains. Intervention : si tu as la carte exactement identique à celle du dessus, tu peux la poser même hors de ton tour. Piocher jusqu'à jouer : on pioche jusqu'à trouver une carte qui va. Jeu forcé : une carte piochée qui va doit être posée.");
        }
        else if (game.gameId == GameId.Roue)
        {
            S("Le but", "Gagner le plus d'argent en découvrant des énigmes : expressions, films, personnages, lieux... Règle du jeu TF1 Games, comme dans l'émission.");
            S("L'énigme rapide", "Chaque manche commence par une énigme rapide : les cases s'allument une à une. Le premier qui tape la bonne réponse gagne 500 € et prend la main. Une erreur, et tu es éliminé de l'énigme rapide.");
            S("À ton tour", "Tourne la roue : sur un montant, propose une consonne ; tu gagnes le montant pour chaque fois qu'elle apparaît, et tu rejoues. Absente : la main passe. Tu peux aussi acheter une voyelle (200 €, perdue si elle est absente) ou proposer la solution avant de tourner. Mauvaise solution : tu ne joues plus de la manche.");
            S("Les cases", "10 000 € : la somme n'est pas multipliée. 0 € : une consonne sans gain. CAVERNE : tu empoches la somme et tu rejoues. BANQUEROUTE : tu perds les gains de la manche. PASSE : la main passe.");
            S("Fin de manche", "Seul celui qui trouve l'énigme garde l'argent de la manche. En manche 2, l'énigme est à double sens (« D'or ou de chemise ? ») : trouve la réponse en 20 secondes pour 500 € de plus (un bouton !).");
            S("La finale", "Chacun sa finale, du plus riche au moins riche : tourne la roue des enveloppes (montant caché), R S T L N E sont données, choisis 3 consonnes et 1 voyelle, puis tu as 20 secondes pour trouver. Réussi : tu gagnes l'enveloppe. La plus grosse fortune gagne la partie !");
        }
        else if (game.gameId == GameId.QuiSuisJe)
        {
            S("Le but", "Chacun a un post-it sur le front avec un personnage choisi par son voisin : une star, un héros, un animal, un objet, un copain... Tout le monde le voit, sauf toi ! Devine qui tu es.");
            S("Le choix", "Au début, chacun choisit le personnage du joueur suivant. Il ne le verra pas, mais tous les autres oui.");
            S("Contre des bots", "Les bots ne lisent pas les questions écrites : hors ligne, tu choisis leur personnage parmi des propositions et tes questions dans une liste ; le jeu répond tout seul.");
            S("À ton tour", "Pose une question fermée : écris-la, ou pose-la à l'oral sur Discord puis clique « Posée à l'oral ». Les autres votent Oui, Non ou Je ne sais pas, la majorité répond. Une seule question par tour : ensuite, propose un nom ou passe la main. Si le nom est faux, la main passe aussi. Une manche dure jusqu'à ce que tout le monde ait trouvé, puis on change de post-its.");
            S("Les points", "Trouver en peu de questions rapporte plus : 12 points moins le nombre de questions (2 au minimum), et 3 de bonus pour le premier. Les points s'additionnent d'une manche à l'autre.");
            S("La vue", "Tu es assis autour de la nappe : clic droit pour tourner la tête, molette pour zoomer, clic sur un joueur pour lire son post-it de près.");
        }
        else if (game.gameId == GameId.LoupGarou)
        {
            S("Le but", "Chaque joueur reçoit une carte en secret. Les loups-garous doivent dévorer les villageois, le village doit démasquer les loups, et certains rôles jouent seuls. Il faut au moins 4 joueurs.");
            S("La nuit", "L'écran s'assombrit. Chaque rôle qui a un pouvoir se réveille et agit en même temps : clique sur un joueur (sa tête) pour le cibler. Les loups votent ensemble leur victime et discutent dans leur chat. Puis la Sorcière se réveille.");
            S("Le jour", "Les morts de la nuit sont annoncés (et leur carte révélée). Le premier jour, on élit un maire, qui tranche les égalités. Puis le village débat (sur Discord ou dans le chat) et vote : le joueur qui a le plus de voix est éliminé.");
            S("Les morts", "Un mort voit toutes les cartes et parle avec les autres morts (le Nécromancien les entend la nuit). Le Chasseur tire en mourant, le maire désigne son successeur.");
            S("Les camps", "Vert : tu gagnes avec le village. Rouge : avec les loups-garous. Bleu : tu gagnes seul. Lis bien ta carte (clique-la en haut à gauche pour la revoir) !");
        }
        else if (game.gameId == GameId.Limite)
        {
            S("Le but", "Le premier à atteindre le nombre de points choisi (5 par défaut) gagne. Il faut au moins 3 joueurs.");
            S("Une manche", "Chacun a 7 cartes rouges (des réponses). Le Boss retourne une carte noire, une question à trou. Les autres posent face cachée la carte de leur main qui la complète le mieux (ou le pire). Une question à deux trous : on pose deux cartes, dans l'ordre.");
            S("Le Boss choisit", "Quand tout le monde a joué, les réponses sont découvertes une à une, mélangées. Le Boss choisit sa préférée : un point pour son auteur, qui devient le Boss de la manche suivante. Chacun repioche pour avoir 7 cartes.");
            S("Plusieurs trous", "Sur une question à deux trous, le Boss choisit trou par trou : la meilleure réponse au premier trou, puis au second. Chaque choix rapporte un point, donc deux joueurs peuvent marquer. Le gagnant du premier trou devient le Boss.");
            S("Le temps", "75 secondes pour poser, une minute pour choisir. Au-delà, le jeu joue au hasard pour les retardataires.");
        }
        else if (game.gameId == GameId.Pouilleux)
        {
            S("Le but", "Ne pas finir avec le pouilleux ! On joue avec un jeu de 52 cartes dont on a retiré le valet de trèfle : le valet de pique n'a donc pas de paire, c'est lui le pouilleux.");
            S("Les paires", "Toutes les cartes sont distribuées. Chacun jette aussitôt ses paires : deux cartes de même valeur et de même couleur (deux rouges ou deux noires).");
            S("À ton tour", "Pioche une carte cachée dans la main de ton voisin (clique sur une de ses cartes de dos). Si elle forme une paire avec une des tiennes, la paire part au milieu. Ta main est ensuite mélangée : personne ne peut suivre le pouilleux des yeux !");
            S("La fin", "Qui n'a plus de cartes est tranquille. Le dernier joueur qui garde le valet de pique est le pouilleux et perd la partie.");
        }
        else if (game.gameId == GameId.BonnePaye)
        {
            S("Durée", "Avant la partie, choisissez le nombre de mois (de 1 à 24 ; un mois = un tour de plateau). Quand tout le monde a fini, l'hôte peut prolonger la partie d'autant de mois qu'il veut, ou la terminer.");
            S("Le but", "Être le plus riche à la fin du dernier mois. On commence avec 1500 €. Le capital final = votre argent + votre livret d'épargne − vos prêts restants. Les acquisitions non vendues ne valent rien à la fin !");
            S("À ton tour", "Lance le dé (maintiens le clic et lâche-le d'un geste, ou Espace) et suis la case où tu t'arrêtes : courrier, acquisition, Vendez !, Quoi de neuf ?, loterie... Un 6 au dé rafle la cagnotte du centre ! Le jeu fait tous les paiements lui-même.");
            S("Courrier", "Les factures (médecin, garage, vacances, cours...) se paient au Jour de paye ; les sommes « à régler comptant » tout de suite ; les « Coup de chance » rapportent aussitôt. Médic'Assur et Assur'Auto sont facultatives : payées une fois, elles annulent toutes vos factures de médecin ou de garage. « Besoin d'argent ? » se garde et se joue quand vous voulez à votre tour : misez moins de 300 €, un 5 ou un 6 rapporte 10 fois la mise, sinon elle va à la cagnotte.");
            S("Acquisitions et Vendez !", "Sur une case Acquisition, achetez l'affaire au prix d'achat si elle vous plaît. Sur Vendez !, revendez-en une à la banque à sa valeur réelle ; puis tout le monde lance le dé et le plus haut touche la commission.");
            S("Loterie", "La banque met 1000 € en jeu ; chacun peut miser 100 € sur un chiffre différent. On lance le dé jusqu'à ce qu'il tombe sur un chiffre joué : son joueur rafle tout.");
            S("Jour de paye", "On s'arrête toujours au 31 : salaire de 1500 €, intérêts du livret, intérêts des prêts (150 € par prêt), remboursement des prêts si vous voulez, puis vos factures. Ensuite on repart pour un nouveau mois.");
            S("Épargne et prêts", "À votre tour, avant de lancer, placez de l'argent sur votre livret (jusqu'au 22 du mois) : il rapporte 50 € par tranche de 500 € au Jour de paye. Retirer coûte 150 € de frais. Il vous manque de l'argent ? La banque vous prête automatiquement par tranches de 1500 € : jamais de solde négatif.");
            S("Changement d'heure", "Sur le 26, tout le monde recule d'une case et suit la case où il arrive. Revenir au départ envoie directement au Jour de paye !");
        }
        else if (game.gameId == GameId.Serpents)
        {
            S("Le but", "Être le premier à amener son pion sur la case 100, en haut du plateau. On part hors du plateau, à côté de la case 1.");
            S("À ton tour", "Lance le dé : maintiens le clic pour le prendre en main, puis lâche-le d'un geste vers le plateau (ou appuie sur Espace, ou sur le bouton). Ton pion avance tout seul d'autant de cases, en suivant les numéros.");
            S("Échelles et serpents", "Tu t'arrêtes au pied d'une échelle ? Tu grimpes tout en haut ! Tu t'arrêtes sur la tête d'un serpent ? Tu glisses jusqu'au bout de sa queue...");
            S("Case occupée", "Tu arrives sur une case où se trouve déjà un autre pion (même après une échelle ou un serpent) ? Pas de chance : tu retournes au départ !");
            S("L'arrivée", "Il faut tomber pile sur la case 100 : si le dé est trop fort, ton pion va jusqu'à 100 puis recule de ce qui dépasse.");
        }
        else if (game.gameId == GameId.Bac)
        {
            S("Le but", "Trouver et écrire des mots correspondant aux catégories choisies. À chaque manche, une lettre est tirée au hasard : tous tes mots doivent commencer par cette lettre. Le joueur qui a le plus de points à la fin des manches gagne.");
            S("Écrire", "Tape un mot dans chaque catégorie (Entrée passe à la suivante). Dès que tu as rempli toutes les catégories, tu peux appuyer sur STOP : la manche s'arrête pour tout le monde ! Sinon, elle s'arrête au bout de 90 secondes.");
            S("Voter", "Ensuite, tout le monde vérifie les mots des autres : clique sur un mot pour le refuser s'il ne va pas. Un mot refusé par plus de la moitié des autres joueurs ne compte pas. Les mots qui ne commencent pas par la bonne lettre sont refusés d'office.");
            S("Les points", "Champ vide : 0 point. Même mot qu'un autre joueur : 5 points. Réponse unique : 10 points. Tu es le seul à avoir trouvé un mot dans la catégorie : 20 points !");
            S("Les catégories", "L'hôte les choisit dans la liste, et peut en écrire lui-même (Super-héros, Dessert, Pokémon...). Moins de 3 catégories : on complète au hasard.");
        }
        else if (game.gameId == GameId.Rhythm)
        {
            S("Le but", "Tenna présente le Pique-Nique Live : tout le monde joue la même chanson en même temps, chacun sur son PC. À la fin, le meilleur score gagne.");
            S("Jouer", "Les notes descendent dans ton couloir, sur deux pistes. Frappe quand une note touche la ligne : piste de gauche avec la flèche gauche (ou F, D, S, Q), piste de droite avec la flèche droite (ou J, K, L, M). Une note allongée se tient jusqu'au bout de la barre.");
            S("Les points", "De 75 à 100 points par note selon ta précision (« Parfait ! » quand tu tombes pile), plus 50 points par seconde de note tenue jusqu'au bout. Enchaîne les notes pour faire grimper ton combo : ton personnage danse sur scène tant qu'il tient !");
            S("Les chansons", "121 morceaux de Deltarune (chapitres 1 à 4) et d'Undertale, de Toby Fox, d'après le jeu de rythme fait maison de la bande. Choisis-la dans la liste, ou laisse faire le hasard.");
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
        BuildRhythmHud();
        BuildUnoHud();
        BuildBacHud();
        BuildBonnePayeHud();
        BuildPouilleuxHud();
        BuildQuiSuisJeHud();
        BuildRoueHud();
        BuildLimiteHud();
        BuildLoupHud();

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
        bgArt.style.display = DisplayStyle.None;   // en jeu : la vraie scene
        Show(hud);
        card.AddToClassList("flip");
        card.style.display = DisplayStyle.None;
        bool bj = game.bj != null, rt = game.rt != null, qz = game.qz != null, rh = game.rh != null, un = game.uno != null, bc = game.bac != null;
        bacHud.style.display = bc ? DisplayStyle.Flex : DisplayStyle.None;
        bool bpg = game.bp != null || game.roue != null, pqg = game.pq != null || game.qsj != null;
        rfHud.style.display = game.roue != null ? DisplayStyle.Flex : DisplayStyle.None;
        qsHud.style.display = game.qsj != null ? DisplayStyle.Flex : DisplayStyle.None;
        llHud.style.display = game.ll != null ? DisplayStyle.Flex : DisplayStyle.None;
        wgHud.style.display = game.wg != null ? DisplayStyle.Flex : DisplayStyle.None;
        if (game.ll != null) LimiteNewRound();
        qsTags.Clear(); qsTagEls.Clear(); qsBubbles.Clear(); qsBubbleUntil.Clear();
        pqHud.style.display = game.pq != null ? DisplayStyle.Flex : DisplayStyle.None;
        pqTags.Clear(); pqTagEls.Clear();
        bpHud.style.display = game.bp != null ? DisplayStyle.Flex : DisplayStyle.None;
        if (bc) BacRound();
        unoHud.style.display = un ? DisplayStyle.Flex : DisplayStyle.None;
        if (un) ShowUnoHud();
        rhHud.style.display = rh ? DisplayStyle.Flex : DisplayStyle.None;
        if (rh) ShowRhythmHud();
        ccHud.style.display = game.rules != null || game.ch != null || game.sp != null ? DisplayStyle.Flex : DisplayStyle.None;
        hud.EnableInClassList("chx", game.ch != null || game.sp != null);
        hud.EnableInClassList("bacx", game.bac != null);
        hud.EnableInClassList("bpx", game.bp != null);
        hud.EnableInClassList("wgx", game.wg != null);
        hud.EnableInClassList("pqx", game.pq != null || game.qsj != null || game.ll != null || game.wg != null);
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
        playersBar.style.display = feed.style.display = bj || rt || qz || rh || un || bc || bpg || pqg ? DisplayStyle.None : DisplayStyle.Flex;
        hint.text = bpg || pqg ? "" : bj || rt || qz || rh || un || bc ? "" : game.ch != null || game.sp != null ? "Maintiens le clic pour prendre le dé, lâche-le d'un geste pour le lancer  ·  Molette : zoom  ·  Échap : pause" : "Clic droit : tourner  ·  Molette : zoom  ·  Échap : pause";
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
        // Illustrations des cartes (Resources/UI/CroqueCards, cf. Tools/cards_croque.py).
        card.style.backgroundImage = Resources.Load<Texture2D>("UI/CroqueCards/" + (c.carrot ? (c.turns == 2 ? "carotte2" : "carotte") : c.steps.ToString()));
        cardValue.text = cardSub.text = "";
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
        else if (game.rh != null) RefreshRhythm();
        else if (game.uno != null) RefreshUno();
        else if (game.ch != null) RefreshChevaux();
        else if (game.bac != null) RefreshBac();
        else if (game.sp != null) RefreshSerpents();
        else if (game.bp != null) RefreshBonnePaye();
        else if (game.pq != null) RefreshPouilleux();
        else if (game.qsj != null) RefreshQuiSuisJe();
        else if (game.ll != null) RefreshLimite();
        else if (game.wg != null) RefreshLoup();
        else if (game.roue != null) RefreshRoue();
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

    string Avatar(int seat) => game.Online ? (seat < game.net.LobbyAvatars.Count ? game.net.LobbyAvatars[seat] : Chars.Default)
                                           : (seat < game.avatars.Count ? game.avatars[seat] : Chars.Default);

    void RefreshCroque()
    {
        var r = game.rules;
        DrawLabel("Piocher une carte", "cards");
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
        var me = game.Online && game.mySeat >= 0 && game.mySeat < b.players.Count ? b.players[game.mySeat] : cur ?? b.players[0];
        if (!game.busy || balance.text == "")   // pendant le jeu du croupier, les gains ne sont pas encore montres
        {
            balance.text = $"Solde : <b>{me.chips}</b>";
            betInfo.text = $"Mise : <b>{me.hands.Sum(h => h.bet)}</b>";
        }
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
            // Solde mis a jour une fois l'animation finie (les gains sont deja calcules pendant que le croupier tire).
            if (!game.busy || ((Label)tag[1][1]).text == "") ((Label)tag[1][1]).text = b.players[i].broke ? "ruiné" : b.players[i].chips.ToString();
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
    Button replayBtn, winMenu;

    Button pauseLobby, pauseQuit;

    public void ShowPause()
    {
        // En ligne : "Retour au salon" (l'hote y ramene tout le monde, un invite y attend la prochaine partie).
        pauseLobby.style.display = game.Online ? DisplayStyle.Flex : DisplayStyle.None;
        pauseQuit.Q<Label>(className: "sk-item-label").text = game.Online ? "QUITTER LE SALON" : "QUITTER LA PARTIE";
        foreach (var b in homeItems) b.EnableInClassList("selected", b == pause.Q<Button>(className: "sk-item"));   // "Reprendre" choisi a l'ouverture
        history.Clear(); history.Push(hud); current = null; Go(pause, false);
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
        else if (game.roue != null)
        {
            var f = game.roue;
            var best = f.players.OrderByDescending(p => p.score).First();
            winTitle.text = $"{best.name} remporte {best.score} € !";
            winTitle.style.color = Board.Colors[best.seat % Board.Colors.Length];
            winSub.text = string.Join("\n", f.players.OrderByDescending(p => p.score).Select((p, i) => $"{i + 1}.  {p.name} : {p.score} €"));
        }
        else if (game.wg != null)
        {
            var g = game.wg;
            winTitle.text = g.winner == "Personne" ? "Personne ne gagne !" : $"{g.winner} gagne{(g.winner.StartsWith("Les") ? "nt" : "")} !";
            winTitle.style.color = g.winner.Contains("loups") ? new Color(0.9f, 0.25f, 0.2f) : new Color(0.5f, 0.9f, 0.35f);
            winSub.text = string.Join("\n", g.players.Select(p => $"{(g.winners.Contains(p.seat) ? "★ " : "")}{p.name} : {LoupGarou.Name(p.role)}{(p.alive ? "" : " (mort)")}"));
        }
        else if (game.ll != null)
        {
            var l = game.ll;
            var best = l.players.OrderByDescending(p => p.score).First();
            winTitle.text = $"{best.name} gagne !";
            winTitle.style.color = Board.Colors[best.seat % Board.Colors.Length];
            winSub.text = string.Join("\n", l.players.OrderByDescending(p => p.score).Select((p, i) => $"{i + 1}.  {p.name} : {p.score} pt{(p.score > 1 ? "s" : "")}"));
        }
        else if (game.qsj != null)
        {
            var w = game.qsj;
            var best = w.players.OrderByDescending(p => p.score).First();
            winTitle.text = $"{best.name} gagne !";
            winTitle.style.color = Board.Colors[best.seat % Board.Colors.Length];
            winSub.text = string.Join("\n", w.players.OrderByDescending(p => p.score).Select((p, i) => $"{i + 1}.  {p.name} : {p.score} pts  ({p.perso}{(p.Found ? "" : ", pas trouvé")})"));
        }
        else if (game.pq != null)
        {
            var q = game.pq;
            winTitle.text = $"{q.players[q.loser].name} est le pouilleux !";
            winTitle.style.color = Board.Colors[q.loser % Board.Colors.Length];
            winSub.text = string.Join("\n", q.players.Where(p => p.outRank >= 0).OrderBy(p => p.outRank).Select((p, i) => $"{i + 1}.  {p.name}")) + $"\nPouilleux : {q.players[q.loser].name}";
        }
        else if (game.bp != null)
        {
            var b = game.bp;
            var w = b.players[b.winner];
            winTitle.text = $"{w.name} gagne !";
            winTitle.style.color = BonnePayeView.ColorOf(b.winner);
            winSub.text = string.Join("\n", b.players.OrderByDescending(p => p.Capital).Select((p, i) => $"{i + 1}.  {p.name}  —  {p.Capital} €"));
        }
        else if (game.sp != null)
        {
            var w = game.sp.players[game.sp.winner];
            winTitle.text = $"{w.name} gagne !";
            winTitle.style.color = SerpentsView.ColorOf(game.sp.winner);
            winSub.text = string.Join("\n", game.sp.players.OrderByDescending(p => p.pos).Select((p, i) => $"{i + 1}.  {p.name}  —  case {p.pos}"));
        }
        else if (game.ch != null)
        {
            var c = game.ch;
            var w = c.players[c.winner];
            winTitle.text = $"{w.name} gagne !";
            winTitle.style.color = Board.Colors[c.ColorOf(c.winner)];
            winSub.text = string.Join("\n", c.players.OrderByDescending(p => p.seat == c.winner).ThenByDescending(p => p.Home).ThenByDescending(p => p.horses.Where(h => h >= 0).Sum())
                .Select((p, i) => $"{i + 1}.  {p.name}  —  {p.Home} cheva{(p.Home > 1 ? "ux" : "l")} au centre"));
        }
        else
        {
            var ranking = (game.bj != null ? game.bj.players.Select(p => (p.name, p.seat, p.chips))
                         : game.rt != null ? game.rt.players.Select(p => (p.name, p.seat, p.chips))
                         : game.qz != null || game.bac != null ? (game.qz?.players ?? game.bac.players).Select(p => (p.name, p.seat, chips: p.score))
                         : game.rh != null ? game.rh.players.Select(p => (p.name, p.seat, chips: p.score))
                         : game.uno.players.Select(p => (p.name, p.seat, chips: p.score)))
                .OrderByDescending(p => p.chips).ToList();
            winTitle.text = $"{ranking[0].name} gagne !";
            winTitle.style.color = Board.Colors[ranking[0].seat];
            winSub.text = string.Join("\n", ranking.Select((p, i) => $"{i + 1}.  {p.name}  —  {p.chips} {(game.qz != null || game.rh != null || game.uno != null || game.bac != null ? "points" : "jetons")}"));
        }
        replayBtn.style.display = !game.Online || game.net.IsHost ? DisplayStyle.Flex : DisplayStyle.None;
        ((Label)winMenu.Q(className: "gl-label")).text = game.Online ? "Retour au salon" : "Menu principal";
        current = null;
        Go(victory, false);
    }

    // --- En ligne -----------------------------------------------------------------------
    VisualElement lobbyList, lobbyOptions, myPortraitBox, lobbyGameCard;
    TextField onlineName, codeField;
    Label onlineStatus, lobbyCode, lobbyStatus, onlineTitle, lobbyGame;
    Button startBtn;

    void SaveName() { PlayerPrefs.SetString("cc-name", onlineName.value); RefreshProfile(); }

    VisualElement lobbyArt, lobbyPick;
    Label lobbyCount, lobbyMeta;
    Button inviteBtn;

    void OpenOnline()
    {
        if (game.net.Active) { ShowLobby(); return; }
        // Invitation dans le presse-papiers : on pre-remplit le code.
        var clip = GUIUtility.systemCopyBuffer ?? "";
        if (clip.Contains("Pique-Nique's Games")) codeField.value = Net.CodeFrom(clip);
        RefreshOnline();
        Go(onlineScreen);
    }

    public void ShowLobby()
    {
        history.Clear();
        foreach (var s in All) Hide(s);
        current = null;
        history.Push(title);
        Go(lobbyScreen, false);
        RefreshOnline();
    }

    public void RefreshOnline()
    {
        var n = game.net;
        onlineTitle.text = "Jouer en ligne";
        myPortraitBox.Clear();
        Portrait(myPortraitBox, game.myAvatar, () => OpenCreator(a => { game.SetMyAvatar(a); RefreshOnline(); }, game.myAvatar), 64);
        onlineStatus.text = n.Status;
        if (n.InGame && game.InMatch) return;
        if (n.Active && n.Lobby.Count > 0 && current == onlineScreen) Go(lobbyScreen);
        if (!n.Active && current == lobbyScreen) Go(onlineScreen, false);
        lobbyGame.text = Games.Name(n.LobbyGame);
        lobbyGameCard.style.display = n.LobbyGame == GameId.Rhythm ? DisplayStyle.None : DisplayStyle.Flex;   // le choix de chanson a deja son titre
        lobbyMeta.text = GameInfo[n.LobbyGame].meta;
        GameArt(lobbyArt, n.LobbyGame);
        int max = Games.MaxPlayers(n.LobbyGame);
        lobbyCount.text = $"{n.Lobby.Count} dans le salon · jusqu'à {max} joueurs";
        lobbyCode.text = n.Code;
        lobbyStatus.text = n.InGame ? "Partie en cours... Tu joueras à la prochaine !"
            : n.IsHost ? (n.Lobby.Count < n.MinPlayers ? "En attente d'au moins un autre joueur..." : n.Lobby.Count > max ? $"Les {max} premiers jouent, les autres regardent." : "Tout le monde est là ? Choisis le jeu et lance la partie !")
            : "L'hôte choisit le jeu...";
        // L'hote choisit le jeu parmi les vignettes.
        lobbyPick.Clear();
        lobbyPick.style.display = n.IsHost && !n.InGame ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (var g in GameInfo.Keys)
        {
            var gid = g;
            var b = new Button(() => { Sound.I.UI("tick"); n.SetGame(gid); });
            b.AddToClassList("m-pick");
            b.EnableInClassList("selected", g == n.LobbyGame);
            GameArt(b, g);
            foreach (var e in b.Query().ToList()) if (e != b) e.pickingMode = PickingMode.Ignore;
            b.tooltip = Games.Name(g);
            lobbyPick.Add(b);
        }
        lobbyList.Clear();
        for (int i = 0; i < n.Lobby.Count; i++)
        {
            var row = Div(lobbyList, "m-strip", "m-player");
            Ring(Portrait(row, i < n.LobbyAvatars.Count ? n.LobbyAvatars[i] : Chars.Default, null, 56), Board.Colors[i]);
            Text(row, n.Lobby[i], "m-player-name").style.color = Board.Colors[i];
            if (i == 0) Text(row, "Hôte", "pill", "pill-gold");
            if (i == game.mySeat) Text(row, "Toi", "pill", "pill-blue");
            if (n.InGame) Text(row, i < n.Players ? "En partie" : "Spectateur", "pill", i < n.Players ? "pill-green" : "pill-grey");
        }
        var g0 = game.gameId;
        game.gameId = n.LobbyGame;
        OptionCards(lobbyOptions, n.LobbyOption, v => n.SetOption(v));
        ThemeChips(lobbyThemes, n.LobbyGame, n.LobbyOption, v => n.SetOption(v), n.LobbyText, n.SetText);
        lobbyThemes.SetEnabled(n.IsHost);
        game.gameId = g0;
        lobbyOptions.SetEnabled(n.IsHost);
        startBtn.style.display = n.IsHost && !n.InGame ? DisplayStyle.Flex : DisplayStyle.None;
        startBtn.SetEnabled(n.Lobby.Count >= n.MinPlayers);
    }
}
