using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Tous les ecrans (UI Toolkit, construits en code, styles dans Resources/UI/Menu.uss).
public class Ui : MonoBehaviour
{
    Game game;
    VisualElement root, title, newGame, settingsScreen, rulesScreen, hud, pause, victory;
    VisualElement current;
    readonly Stack<VisualElement> history = new Stack<VisualElement>();

    VisualElement modeClassic, modeImproved, playerList, settingsBody, playersBar, action, card, rabbitRow, cycle, cycleGrid, feed;
    Label turn, cardValue, cardSub, deck, banner, winTitle, winSub;
    Button drawBtn, addPlayer;
    readonly List<Button> tabs = new List<Button>();
    int tab;
    IVisualElementScheduledItem bannerHide;

    public void Init(Game g, UIDocument doc)
    {
        game = g;
        root = doc.rootVisualElement;
        root.styleSheets.Add(Resources.Load<StyleSheet>("UI/Menu"));
        root.AddToClassList("root");
        root.pickingMode = PickingMode.Ignore;
        BuildTitle();
        BuildNewGame();
        BuildSettings();
        BuildRules();
        BuildHud();
        BuildPause();
        BuildVictory();
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
        Go(history.Pop(), false);
        if (current == hud) game.Resume();
    }

    public bool InSubMenu => history.Count > 0;

    public void OpenForTest(string screen)
    {
        if (screen == "settings") { SelectTab(0); Go(settingsScreen); }
        else { Back(); RefreshPlayers(); Go(newGame); }
    }

    public void ShowTitle()
    {
        history.Clear();
        foreach (var s in new[] { newGame, settingsScreen, rulesScreen, hud, pause, victory }) Hide(s);
        current = null;
        Go(title, false);
    }

    // --- Ecran titre ----------------------------------------------------------------
    void BuildTitle()
    {
        title = Screen("title-screen");
        var logo = Text(title, "Croque-Carotte", "logo");
        Text(title, "La course de lapins la plus traître de la montagne !", "tagline");
        var col = Div(title, "menu-col");
        Btn(col, "Jouer", () => { RefreshPlayers(); Go(newGame); });
        Btn(col, "Règles", () => Go(rulesScreen), "green");
        Btn(col, "Paramètres", () => { SelectTab(tab); Go(settingsScreen); }, "green");
        Btn(col, "Quitter", Application.Quit, "ghost");
        Text(title, "v1.0  ·  Modèles Kenney & Quaternius (CC0)  ·  Musique CC0", "credits");
        logo.schedule.Execute(() =>
        {
            float t = Time.unscaledTime;
            logo.style.rotate = new Rotate(Angle.Degrees(Mathf.Sin(t * 1.3f) * 2f));
            logo.style.translate = new Translate(0, Mathf.Sin(t * 2.1f) * 8f);
        }).Every(16);
    }

    // --- Nouvelle partie --------------------------------------------------------------
    void BuildNewGame()
    {
        newGame = Screen();
        var panel = Div(newGame, "panel");
        panel.style.width = 1120;
        Text(panel, "Nouvelle partie", "panel-title");
        Text(panel, "Mode de jeu", "h2");
        var modes = Div(panel, "row");
        modeClassic = ModeCard(modes, Mode.Classique, "Classique", "19 cases en spirale. Quand la carotte tourne, 1 à 3 trous s'ouvrent au hasard. Pur suspense !");
        modeImproved = ModeCard(modes, Mode.Ameliore, "Amélioré", "25 cases sur deux anneaux. Les trous s'ouvrent un par un selon un cycle secret : observe-le pour le déjouer.");
        Text(panel, "Joueurs (sur ce PC, chacun son tour)", "h2");
        playerList = Div(panel);
        addPlayer = Btn(panel, "+ Ajouter un joueur", () => { game.names.Add("Joueur " + (game.names.Count + 1)); RefreshPlayers(); }, "ghost", "small");
        var bottom = Div(panel, "row", "spread");
        bottom.style.marginTop = 20;
        Btn(bottom, "Retour", Back, "ghost", "small").style.width = 260;
        Btn(bottom, "Lancer la partie !", () => game.StartGame(), "green").style.width = 480;
    }

    VisualElement ModeCard(VisualElement parent, Mode m, string name, string desc)
    {
        var b = new Button(() => { Sound.I.UI("tick"); game.mode = m; RefreshPlayers(); });
        b.AddToClassList("mode-card");
        Text(b, name, "mode-name");
        Text(b, desc, "mode-desc");
        parent.Add(b);
        return b;
    }

    void RefreshPlayers()
    {
        modeClassic.EnableInClassList("selected", game.mode == Mode.Classique);
        modeImproved.EnableInClassList("selected", game.mode == Mode.Ameliore);
        playerList.Clear();
        for (int i = 0; i < game.names.Count; i++)
        {
            int idx = i;
            var row = Div(playerList, "player-row");
            Div(row, "chip").style.backgroundColor = Board.Colors[i];
            var field = Add(row, new TextField { value = game.names[i], maxLength = 16 }, "name-field");
            field.RegisterValueChangedCallback(e => game.names[idx] = e.newValue);
            var rm = Btn(row, "Retirer", () => { game.names.RemoveAt(idx); RefreshPlayers(); }, "ghost", "small");
            rm.style.marginLeft = 12;
            rm.SetEnabled(game.names.Count > 2);
        }
        addPlayer.style.display = game.names.Count < Rules.MaxPlayers ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // --- Parametres -------------------------------------------------------------------
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
                Check("Caméra qui suit l'action", s.autoCam, v => s.autoCam = v);
                Check("Numéros sur les cases", s.tileNumbers, v => s.tileNumbers = v);
                break;
            default:
                Info("Piocher une carte", "Espace  ·  bouton Piocher");
                Info("Choisir un lapin", "1  ·  2  ·  3");
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
    void BuildRules()
    {
        rulesScreen = Screen("dim");
        var panel = Div(rulesScreen, "panel", "settings-panel");
        Text(panel, "Règles du jeu", "panel-title");
        var sv = Add(panel, new ScrollView(), "settings-scroll");
        Text(sv, "Le but", "h2");
        Text(sv, "Chaque joueur a 3 lapins. Le premier à les amener tous les trois au potager, en haut de la montagne, gagne la partie.", "p");
        Text(sv, "À ton tour", "h2");
        Text(sv, "Pioche une carte. Une carte chiffrée (1, 2 ou 3) fait avancer le lapin de ton choix d'autant de cases. Une case ne porte qu'un lapin : si elle est prise, on saute jusqu'à la suivante libre.", "p");
        Text(sv, "La carte Carotte", "h2");
        Text(sv, "Elle fait tourner la grosse carotte du sommet... et des trous s'ouvrent sous certaines cases ! Les lapins qui s'y trouvent dégringolent jusqu'à l'enclos de départ. Les trous restent ouverts jusqu'au prochain tour de carotte : un lapin qui s'arrête dessus tombe aussi !", "p");
        Text(sv, "Mode Classique", "h2");
        Text(sv, "19 cases en spirale. Chaque tour de carotte ouvre 1 à 3 trous tirés au hasard, n'importe où à partir de la case 3.", "p");
        Text(sv, "Mode Amélioré", "h2");
        Text(sv, "25 cases sur deux anneaux. Les trous s'ouvrent un par un, selon un cycle de 25 crans tiré au début de la partie mais qui ne change plus. La carte Double carotte avance de deux crans. Le tableau des crans garde la trace de tout ce qui s'est ouvert : quand un cran est connu, la case menacée s'allume en rouge sur le plateau. Observe, déduis, et place tes lapins là où ça ne tombera pas !", "p");
        Text(sv, "D'après la vidéo d'Hydrios « Il manque 2 cases à Croque-Carotte ».", "p").style.color = new Color(0.55f, 0.43f, 0.31f);
        var bottom = Div(panel, "row");
        bottom.style.justifyContent = Justify.Center;
        Btn(bottom, "Retour", Back, "small").style.width = 280;
    }

    // --- HUD --------------------------------------------------------------------------
    void BuildHud()
    {
        hud = Screen("hud");
        playersBar = Div(hud, "players-bar");
        Btn(hud, "II", () => game.Pause(), "round", "ghost", "pause-btn");

        cycle = Div(hud, "panel", "cycle");
        Text(cycle, "Crans des aiguilles", "h2").style.marginTop = 0;
        cycleGrid = Div(cycle, "cycle-grid");
        Text(cycle, "Orange : cran actuel. Rouge : les 2 prochains crans (carotte simple ou double).", "small-note");

        feed = Div(hud, "panel", "feed");

        action = Div(hud, "panel", "action");
        card = Div(action, "card", "flip");
        cardValue = Text(card, "", "card-value");
        cardSub = Text(card, "", "card-sub");
        var col = Div(action, "action-col");
        turn = Text(col, "", "turn");
        drawBtn = Btn(col, "Piocher une carte", () => game.Draw(), "small");
        drawBtn.style.width = 360;
        rabbitRow = Div(col, "rabbit-row");
        deck = Text(col, "", "deck");

        banner = Text(hud, "", "banner");
        banner.pickingMode = PickingMode.Ignore;
        Text(hud, "Clic droit : tourner  ·  Molette : zoom  ·  Échap : pause", "hint");
    }

    public void ShowHud()
    {
        history.Clear();
        foreach (var s in new[] { title, newGame, settingsScreen, rulesScreen, pause, victory }) Hide(s);
        current = hud;
        Show(hud);
        card.AddToClassList("flip");
        card.style.display = DisplayStyle.None;
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
        var r = game.rules;
        if (r == null) return;

        playersBar.Clear();
        for (int i = 0; i < r.players.Count; i++)
        {
            var p = r.players[i];
            var pc = Div(playersBar, "pcard");
            pc.EnableInClassList("active", i == r.turn && !r.Over);
            Div(pc, "chip").style.backgroundColor = Board.Colors[p.color];
            Text(pc, p.name, "pname");
            foreach (int pos in p.rabbits.OrderByDescending(x => x))
            {
                var pip = Text(pc, pos > 0 && pos < r.summit ? pos.ToString() : "", "pip");
                if (pos >= r.summit) pip.style.backgroundColor = Board.Colors[p.color];
                else if (pos > 0) pip.AddToClassList("field");
            }
        }

        bool mine = !game.busy && !r.Over;
        drawBtn.style.display = r.drawn == null && mine ? DisplayStyle.Flex : DisplayStyle.None;
        drawBtn.SetEnabled(mine);
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

        feed.Clear();
        var lines = r.log.Skip(Math.Max(0, r.log.Count - 5)).ToList();
        for (int i = 0; i < lines.Count; i++) Text(feed, lines[i], "feed-line").EnableInClassList("last", i == lines.Count - 1);
    }

    // --- Pause & victoire -------------------------------------------------------------
    void BuildPause()
    {
        pause = Screen("dim");
        var panel = Div(pause, "panel");
        panel.style.width = 560;
        Text(panel, "Pause", "panel-title");
        Btn(panel, "Reprendre", Back, "green");
        Btn(panel, "Paramètres", () => { SelectTab(tab); Go(settingsScreen); });
        Btn(panel, "Règles", () => Go(rulesScreen));
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
        var row = Div(panel, "row");
        row.style.marginTop = 20;
        Btn(row, "Menu principal", () => game.ToMenu(), "ghost").style.width = 360;
        Btn(row, "Rejouer !", () => game.StartGame(), "green").style.width = 360;
        row.Children().First().style.marginRight = 20;
    }

    public void ShowVictory()
    {
        var w = game.rules.players[game.rules.winner];
        winTitle.text = $"{w.name} gagne !";
        winTitle.style.color = Board.Colors[w.color];
        winSub.text = "Ses trois lapins festoient au potager.";
        current = null;
        Go(victory, false);
    }
}
