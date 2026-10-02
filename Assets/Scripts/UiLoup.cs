using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Loup-garou : ma carte (en haut a gauche, clic pour la relire), mon carnet (ce que j'ai appris), les noms au-dessus des
// tetes (votes, maire, morts), la nuit qui assombrit l'ecran, le panneau d'action de mon role, et le chat.
// On cible un joueur en cliquant sur lui (sa tete).
public partial class Ui
{
    ScrollView wgNotesScroll;
    string wgNotesKey;
    Label wgBigHint, wgVotesMe, wgTimerText;
    VisualElement wgTimerFill;
    VisualElement wgPeek, wgOvDoused, wgOvPoison, wgOvMayor, wgOvLove, wgRoles;
    string wgRolesKey;

    // Logos carres d'Agrou (LogosCartesFin) ; l'Assassin n'en a pas : sa carte.
    static string WgLogo(Role r) => r switch
    {
        Role.Villageois => "villageois", Role.Loup => "logo-loupgarou", Role.Voyante => "logo-voyante", Role.Sorciere => "logo-sociere",
        Role.Chasseur => "chasseur", Role.Cupidon => "logo-cupidon", Role.Garde => "logo-garde", Role.PetiteFille => "logo-petitefille",
        Role.LoupBlanc => "logo-loupblanc", Role.LoupNoir => "logo-loupnoir", Role.Brumeux => "logo-loupbrumeux", Role.Anesthesiste => "logo-loupanesthesiste",
        Role.Corbeau => "logo-corbeau", Role.Dictateur => "logo-dictateur", Role.Dresseur => "logo-dresseur", Role.Pyromane => "logo-pyromane",
        Role.Ange => "logo-ange", Role.Blaster => "logo-blaster-fix", Role.Influenceur => "logo-influenceur", Role.Medium => "logo-medium",
        Role.Necromancien => "logo-necromancien", Role.Ninja => "logo-ninja", Role.Assassin => "logo-assassin", _ => null,
    };
    static Texture2D WgLogoTex(Role r) => (WgLogo(r) is string n ? Resources.Load<Texture2D>("LoupGarou/Hud/" + n) : null) ?? Resources.Load<Texture2D>("LoupGarou/" + LoupGarou.Card(r));
    float wgRevealUntil;
    VisualElement wgHud, wgTags, wgNight, wgCardBig, wgActions, wgChatBox, wgChatList, wgNotesBox, wgButtons;
    Label wgStatus, wgNightText, wgHint, wgBigTitle;
    VisualElement wgCardSmall, wgBigFront, wgBigBack;
    TextField wgChatField;
    readonly List<VisualElement> wgTagEls = new List<VisualElement>();
    readonly List<string> wgNotes = new List<string>();
    readonly List<int> wgSel = new List<int>();
    bool wgShuriken;
    IVisualElementScheduledItem wgBigHide;

    int WgMe => game.wg == null ? 0 : Mathf.Clamp(game.mySeat, 0, game.wg.players.Count - 1);
    LoupGarou.Player WgMeP => game.wg.players[WgMe];
    bool WgCanAct => !game.Spectating && WgMeP.alive;

    void BuildLoupHud()
    {
        wgHud = Div(hud, "layer"); wgHud.pickingMode = PickingMode.Ignore;
        wgNight = Div(wgHud, "wg-night"); wgNight.pickingMode = PickingMode.Ignore;
        wgNightText = Text(wgNight, "Le village dort...", "wg-night-text"); wgNightText.pickingMode = PickingMode.Ignore;
        wgTags = Div(wgHud, "layer"); wgTags.pickingMode = PickingMode.Ignore;
        wgStatus = Text(wgHud, "", "qsj-status"); wgStatus.pickingMode = PickingMode.Ignore;
        // HUD d'Agrou (HUDPartie) : vision de la Petite fille, votes contre moi en haut a gauche, logo de mon role en haut
        // a droite (echarpe de maire, essence, poison du ninja par-dessus), roles en jeu en bas.
        wgPeek = Div(wgHud, "wg-peek"); wgPeek.pickingMode = PickingMode.Ignore;
        Div(wgPeek, "wg-peek-top").pickingMode = PickingMode.Ignore;      // paupieres entrouvertes : on ne voit
        Div(wgPeek, "wg-peek-bottom").pickingMode = PickingMode.Ignore;   // qu'une fente au milieu de l'ecran
        wgPeek.SendToBack();
        var timer = Div(wgHud, "wg-timer"); timer.pickingMode = PickingMode.Ignore;
        wgTimerFill = Div(timer, "wg-timer-fill"); wgTimerFill.pickingMode = PickingMode.Ignore;
        wgTimerText = Text(timer, "", "wg-timer-text"); wgTimerText.pickingMode = PickingMode.Ignore;
        var votes = Div(wgHud, "wg-votes"); votes.pickingMode = PickingMode.Ignore;
        votes.style.backgroundImage = Resources.Load<Texture2D>("LoupGarou/Hud/FondNoirLoup");
        Text(votes, "Votes contre vous :", "wg-votes-label").pickingMode = PickingMode.Ignore;
        wgVotesMe = Text(votes, "0", "wg-votes-n"); wgVotesMe.pickingMode = PickingMode.Ignore;
        wgCardSmall = new Button(() => { Sound.I.UI("click"); LoupShowCard(0); });
        wgCardSmall.AddToClassList("wg-logo");
        wgHud.Add(wgCardSmall);
        VisualElement Over(string tex) { var o = Div(wgCardSmall, "wg-logo-over"); o.pickingMode = PickingMode.Ignore; o.style.backgroundImage = Resources.Load<Texture2D>("LoupGarou/Hud/" + tex); return o; }
        wgOvDoused = Over("flaquepyro"); wgOvPoison = Over("add_ninja"); wgOvMayor = Over("maire__1_");
        wgOvLove = Div(wgCardSmall, "wg-logo-love"); wgOvLove.pickingMode = PickingMode.Ignore; wgOvLove.style.backgroundImage = Resources.Load<Texture2D>("LoupGarou/Hud/logo-cupidon");
        wgRoles = Div(wgHud, "wg-roles"); wgRoles.pickingMode = PickingMode.Ignore;
        var left = Div(wgHud, "wg-left"); left.pickingMode = PickingMode.Ignore;
        wgNotesBox = Div(left, "panel", "wg-notes");
        wgNotesScroll = new ScrollView(ScrollViewMode.Vertical);   // on remonte a la molette (votes precedents...)
        wgNotesScroll.AddToClassList("wg-notes-scroll");
        wgNotesScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden; wgNotesScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        wgNotesBox.Add(wgNotesScroll);
        // Panneau d'action.
        wgActions = Div(wgHud, "panel", "wg-actions");
        wgHint = Text(wgActions, "", "wg-hint");
        wgButtons = Div(wgActions, "row", "wg-buttons");
        // Chat.
        wgChatBox = Div(wgHud, "panel", "wg-chat");
        wgChatList = Div(wgChatBox, "wg-chat-list"); wgChatList.pickingMode = PickingMode.Ignore;
        wgChatField = Add(wgChatBox, new TextField { maxLength = 120 }, "qsj-input");
        wgChatField.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) LoupSendChat(); }, TrickleDown.TrickleDown);
        // Ma carte en grand (recto + verso).
        wgCardBig = Div(wgHud, "wg-big");
        wgCardBig.RegisterCallback<PointerDownEvent>(_ => wgCardBig.style.display = DisplayStyle.None);
        wgBigTitle = Text(wgCardBig, "", "wg-big-title");
        var row = Div(wgCardBig, "row");
        wgBigFront = Div(row, "wg-big-card"); wgBigBack = Div(row, "wg-big-card");
        wgBigHint = Text(wgCardBig, "Clique pour fermer", "wg-big-hint");
        wgCardBig.style.display = DisplayStyle.None;
    }

    // --- Debut, carte, carnet -----------------------------------------------------------------------------------------
    public void LoupStart()
    {
        wgNotes.Clear(); wgSel.Clear(); wgShuriken = false; wgNotesKey = null;
        wgTags.Clear(); wgTagEls.Clear();
        var l = game.wg;
        var me = WgMeP;
        if (!game.Spectating)
        {
            if (LoupGarou.IsWolf(me.role) && LoupGarou.TeamOf(me.role) == Team.Loups || me.role == Role.LoupBlanc)
            {
                var mates = l.players.Where(p => p.seat != me.seat && LoupGarou.IsWolf(p.role)).Select(p => p.name).ToList();
                if (mates.Count > 0) LoupNote("Tes compagnons loups : " + string.Join(", ", mates));
            }
            if (me.role == Role.Influenceur) LoupNote($"Ta mission : fais éliminer {l.players[me.missionTarget].name} par le vote du village.");
        }
        LoupShowCard(6000);
        RefreshLoup();
    }

    void LoupShowCard(long hideMs)
    {
        var l = game.wg;
        if (l == null) return;
        var r = WgMeP.role;
        wgBigTitle.text = game.Spectating ? "Tu regardes la partie" : $"Tu es {LoupGarou.Name(r)}" + (WgMeP.mayor ? " (et maire)" : "");
        wgBigFront.style.backgroundImage = Resources.Load<Texture2D>("LoupGarou/" + LoupGarou.Card(r));
        wgBigBack.style.backgroundImage = Resources.Load<Texture2D>("LoupGarou/" + LoupGarou.Card(r) + "_dos");
        wgBigBack.style.display = DisplayStyle.Flex;
        wgBigHint.style.display = DisplayStyle.Flex;
        wgRevealUntil = 0;
        wgCardBig.style.display = DisplayStyle.Flex;
        wgBigHide?.Pause();
        if (hideMs > 0) wgBigHide = wgCardBig.schedule.Execute(() => wgCardBig.style.display = DisplayStyle.None).StartingIn(hideMs);
    }

    // Revelation du role d'un mort : sa carte en grand quelques secondes.
    public void LoupReveal(int seat)
    {
        var l = game.wg;
        if (l == null || seat < 0 || seat >= l.players.Count) return;
        var p = l.players[seat];
        wgBigTitle.text = $"{p.name} était {LoupGarou.Name(p.role)}";
        wgBigFront.style.backgroundImage = Resources.Load<Texture2D>("LoupGarou/" + LoupGarou.Card(p.role));
        wgBigBack.style.display = DisplayStyle.None;
        wgBigHint.style.display = DisplayStyle.None;
        wgCardBig.style.display = DisplayStyle.Flex;
        wgRevealUntil = Time.time + 3.5f;   // se ferme toute seule (UpdateLoup), personne ne reste bloque dessus
        Sound.I.Play("wg_gong", 0.5f, 0);
        wgBigHide?.Pause();
        wgBigHide = wgCardBig.schedule.Execute(() => { wgCardBig.style.display = DisplayStyle.None; wgBigBack.style.display = DisplayStyle.Flex; }).StartingIn(3000);
    }

    public void LoupNote(string text)
    {
        wgNotes.Add(text);
        if (!text.StartsWith("Vote ")) Say(text.Length > 60 ? "Nouvelle information dans ton carnet !" : text, 3);
        RefreshLoup();
    }

    public void LoupPhase() { wgSel.Clear(); wgShuriken = false; }

    // --- Clic sur un joueur -----------------------------------------------------------------------------------------
    public void LoupClick(int seat)
    {
        var l = game.wg;
        if (l == null || !WgCanAct) return;
        int me = WgMe;
        var p = WgMeP;
        Sound.I.UI("tick");
        switch (l.phase)
        {
            case WPh.Night:
                if (!l.NightRole(p)) return;
                if (LoupGarou.IsWolf(p.role) && !LoupGarou.IsWolf(l.players[seat].role) && !SelectingSpecial(p)) { game.Act($"night|{me}|wolf|{seat}"); return; }
                Toggle(seat, p.role == Role.Cupidon || p.role == Role.Pyromane ? 2 : 1);
                break;
            case WPh.Witch: if (p.role == Role.Sorciere) Toggle(seat, 1); break;
            case WPh.Tie:
                if (l.CanVote(me) && l.tied.Contains(seat)) game.Act($"vote|{me}|{seat}");
                break;
            case WPh.Election: case WPh.Vote:
                if (wgShuriken && p.role == Role.Ninja) { game.Act($"shuriken|{me}|{seat}"); wgShuriken = false; break; }
                if (l.CanVote(me)) game.Act($"vote|{me}|{seat}");
                break;
            case WPh.Hunter: case WPh.Heir: case WPh.Dictator:
                if (l.pending.Count > 0 && l.pending[0].seat == me) Toggle(seat, 1);
                break;
        }
        RefreshLoup();
    }
    // Les loups a pouvoir choisissent d'abord la cible de leur pouvoir s'ils ont un vote deja pose.
    bool SelectingSpecial(LoupGarou.Player p) => game.wg.wolfVotes.ContainsKey(p.seat) && (p.role == Role.Anesthesiste && game.wg.CanAnesth || p.role == Role.LoupBlanc && game.wg.WhiteNight);
    void Toggle(int seat, int max) { if (wgSel.Contains(seat)) wgSel.Remove(seat); else { if (wgSel.Count >= max) wgSel.RemoveAt(0); wgSel.Add(seat); } }

    // Journal visible : jusqu'a la premiere mort pas encore montree a l'ecran (et la ligne d'explosion qui l'annonce).
    int WgLogShown(LoupGarou l)
    {
        int n = l.log.Count;
        foreach (var p in l.players)
            if (!p.alive && p.deathLog >= 0 && game.WgShownAlive(p.seat))
            {
                int k = p.deathLog;
                if (k > 0 && l.log[k - 1].StartsWith("BOUM")) k--;
                n = Mathf.Min(n, k);
            }
        return n;
    }

    public string LoupDebug()
    {
        var b = string.Join(" / ", wgButtons.Query<Label>(className: "gl-label").ToList().Select(x => x.text).Concat(wgButtons.Query<Button>().ToList().Select(x => x.text).Where(t => !string.IsNullOrEmpty(t))));
        return $"statut=[{wgStatus.text}] nuit=[{(wgNight.resolvedStyle.display == DisplayStyle.Flex ? wgNightText.text : "-")}] aide=[{wgHint.text}] boutons=[{b}] carnet=[{string.Join(" | ", wgNotes)}] peek=[{wgPeek.resolvedStyle.display}/{(wgPeek.resolvedStyle.backgroundImage.texture ? wgPeek.resolvedStyle.backgroundImage.texture.name : "null")}/{wgPeek.worldBound}]";
    }

    public bool OverLoupNotes(Vector2 screen)
    {
        if (wgNotesBox == null || root.panel == null) return false;
        var e = root.panel.Pick(RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, UnityEngine.Screen.height - screen.y)));
        for (; e != null; e = e.parent) if (e == wgNotesBox) return true;
        return false;
    }

    void LoupSendChat()
    {
        var l = game.wg;
        var t = (wgChatField.value ?? "").Replace("|", " ").Trim();
        if (l == null || t.Length == 0) return;
        var chan = ChatChannel();
        if (chan == null) return;
        game.Act($"chat|{WgMe}|{chan}|{t}");
        wgChatField.value = "";
        wgChatField.schedule.Execute(() => wgChatField.Focus()).StartingIn(20);
    }
    string ChatChannel()
    {
        var l = game.wg;
        int me = WgMe;
        if (game.Spectating) return null;
        foreach (var c in new[] { "morts", "loups", "village" }) if (l.CanChat(me, c)) return c;
        return null;
    }

    // --- Affichage ----------------------------------------------------------------------------------------------------
    static string N(LoupGarou l, int s) => s >= 0 && s < l.players.Count ? l.players[s].name : "?";

    void RefreshLoup()
    {
        var l = game.wg;
        if (l == null) return;
        int me = WgMe;
        var p = WgMeP;
        bool night = l.phase == WPh.Night || l.phase == WPh.Witch;
        bool acting = WgCanAct && (l.phase == WPh.Night && l.NightRole(p) || l.phase == WPh.Witch && p.role == Role.Sorciere);
        wgNight.style.display = night ? DisplayStyle.Flex : DisplayStyle.None;
        wgNight.EnableInClassList("acting", acting || !p.alive);
        bool spying = l.phase == WPh.Night && l.step == LoupGarou.WolfStep && p.alive && p.role == Role.PetiteFille;
        wgNightText.text = acting || spying ? "" : !p.alive ? "" : l.phase == WPh.Witch ? "La Sorcière se réveille..." : "Le village dort...";
        wgCardSmall.style.backgroundImage = WgLogoTex(p.role);
        void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        Show(wgOvMayor, p.mayor); Show(wgOvDoused, p.doused); Show(wgOvPoison, p.poisoned); Show(wgOvLove, p.lover >= 0);
        Show(wgPeek, l.phase == WPh.Night && l.step == LoupGarou.WolfStep && p.alive && p.role == Role.PetiteFille && !game.Spectating);   // elle epie les loups   // elle epie entre ses doigts
        int against = l.phase == WPh.Vote || l.phase == WPh.Election || l.phase == WPh.Tie ? l.votes.Count(v => v.Value == me) + (l.phase == WPh.Vote && l.crowed == me ? 2 : 0) : 0;
        wgVotesMe.text = l.fog ? "?" : against.ToString();
        // Roles en jeu (comme les cartes en bas de l'ecran d'Agrou), avec leur nombre.
        var deck = l.players.GroupBy(x => x.role).OrderBy(g => LoupGarou.TeamOf(g.Key)).ThenBy(g => g.Key).Select(g => (g.Key, g.Count(x => game.WgShownAlive(x.seat)))).ToList();   // baisse a la revelation, pas avant
        var key = string.Join(",", deck);
        if (key != wgRolesKey)
        {
            wgRolesKey = key; wgRoles.Clear();
            foreach (var (r, c) in deck)
            {
                var cell = Div(wgRoles, "wg-role"); cell.pickingMode = PickingMode.Ignore;
                cell.style.backgroundImage = WgLogoTex(r);
                cell.EnableInClassList("out", c == 0);
                Text(cell, c.ToString(), "wg-role-n").pickingMode = PickingMode.Ignore;   // encore en vie
            }
        }

        // Consigne.
        wgStatus.text = l.Finished ? $"{l.winner} remporte la partie !" : l.phase switch
        {
            WPh.Night => $"Nuit {l.night} · " + (acting ? l.Hint(p) : l.StepName + (l.StepName.StartsWith("Les") ? " se réveillent..." : " se réveille...")),
            WPh.Tie => (l.CanVote(me) ? "Égalité ! Revote entre " : "Égalité : le village revote entre ") + string.Join(" et ", l.tied.Select(x => N(l, x))) + ".",
            WPh.Witch => p.role == Role.Sorciere && p.alive ? "Sorcière : sauve la victime, empoisonne quelqu'un, ou termine." : "La Sorcière se réveille...",
            WPh.Dawn => $"Jour {l.day} : le village se réveille",
            WPh.Election => "Élisons le maire ! Clique sur un joueur pour voter.",
            WPh.Vote => (l.CanVote(me) ? "Débattez, puis clique sur le joueur à éliminer." : !p.alive ? "Tu es mort : tu regardes le vote." : "Tu es anesthésié : tu ne peux ni parler ni voter.") + (l.fog ? "  (brouillard : votes cachés)" : ""),
            WPh.Verdict => l.eliminated >= 0 ? $"{N(l, l.eliminated)} quitte le village." : "Personne n'est éliminé.",
            WPh.Hunter => l.pending.Count > 0 && l.pending[0].seat == me ? "Tu es Chasseur : tire sur quelqu'un !" : $"Le Chasseur {N(l, l.pending.Count > 0 ? l.pending[0].seat : -1)} va tirer...",
            WPh.Heir => l.pending.Count > 0 && l.pending[0].seat == me ? "Désigne le nouveau maire." : "Le maire désigne son successeur...",
            WPh.Dictator => l.pending.Count > 0 && l.pending[0].seat == me ? "Coup d'État : choisis qui renverser !" : "Le Dictateur fait un coup d'État !",
            _ => "",
        };   // les secondes sont dans la barre du minuteur

        // Carnet + journal public.
        // Carnet : tout l'historique ; colle en bas a chaque nouvelle ligne, sauf si on est remonte lire.
        var notesKey = wgNotes.Count + "/" + WgLogShown(l);
        if (notesKey != wgNotesKey)
        {
            var sv = wgNotesScroll;
            bool atBottom = wgNotesKey == null || sv.scrollOffset.y >= sv.contentContainer.layout.height - sv.contentViewport.layout.height - 8;
            wgNotesKey = notesKey;
            sv.Clear();
            Text(sv, "Mon carnet", "rf-money-title");
            foreach (var n in wgNotes) Text(sv, n, "wg-note");
            Text(sv, "Village", "rf-money-title");
            foreach (var n in l.log.Take(WgLogShown(l))) Text(sv, n, "wg-note", "pub");
            if (atBottom) sv.schedule.Execute(() => sv.scrollOffset = new Vector2(0, float.MaxValue)).StartingIn(30);
        }

        // Panneau d'action.
        wgButtons.Clear();
        string hint = "";
        void B(string text, System.Action a, string cls = "green") => Btn(wgButtons, text, () => { a(); wgSel.Clear(); RefreshLoup(); }, cls, "small");
        string sel = string.Join(" et ", wgSel.Select(s => N(l, s)));
        if (WgCanAct)
        {
            if (l.phase == WPh.Night && l.NightRole(p))
            {
                bool wolf = LoupGarou.IsWolf(p.role);
                if (wolf) hint = l.wolfVotes.TryGetValue(me, out int wv) ? $"Ton vote : {N(l, wv)}. Les loups votent la victime (clique un joueur pour changer)." : "Clique sur la victime de cette nuit.";
                bool special = !l.nightDone.Contains(me) || p.role == Role.Loup;
                if (special) switch (p.role)   // action faite (ou passee) : plus de boutons
                {
                    case Role.Voyante: hint = "Clique un joueur, puis Espionner."; if (wgSel.Count == 1) B($"Espionner {sel}", () => game.Act($"night|{me}|see|{wgSel[0]}")); break;
                    case Role.Garde: hint = "Clique un joueur (ou toi-même), puis Protéger."; if (wgSel.Count == 1) B($"Protéger {sel}", () => game.Act($"night|{me}|guard|{wgSel[0]}")); if (p.guardLast != me) B("Me protéger", () => game.Act($"night|{me}|guard|{me}"), "blue"); break;
                    case Role.Cupidon: hint = "Clique deux joueurs (tu peux en faire partie), puis Lier."; if (wgSel.Count == 2) B($"Lier {sel}", () => game.Act($"night|{me}|love|{wgSel[0]}|{wgSel[1]}")); if (wgSel.Count == 1) B($"Lier {sel} et moi", () => game.Act($"night|{me}|love|{wgSel[0]}|{me}"), "blue"); break;
                    case Role.Corbeau: hint = "Clique un joueur : 2 voix contre lui demain."; if (wgSel.Count == 1) B($"Désigner {sel}", () => game.Act($"night|{me}|crow|{wgSel[0]}")); break;
                    case Role.Pyromane:
                        hint = "Clique jusqu'à deux joueurs à imbiber, ou immole tous les imbibés.";
                        if (wgSel.Count > 0) B($"Imbiber {sel}", () => game.Act($"night|{me}|douse|{wgSel[0]}" + (wgSel.Count > 1 ? $"|{wgSel[1]}" : "")));
                        if (l.players.Any(o => o.alive && o.doused)) B("Immoler !", () => game.Act($"night|{me}|ignite"), "red");
                        break;
                    case Role.Assassin: hint = "Clique un joueur, puis Assassiner."; if (wgSel.Count == 1) B($"Assassiner {sel}", () => game.Act($"night|{me}|kill|{wgSel[0]}"), "red"); break;
                    case Role.Blaster: hint = "Clique un joueur, puis lance ta bombe."; if (wgSel.Count == 1) B($"Bombe sur {sel}", () => game.Act($"night|{me}|bomb|{wgSel[0]}"), "red"); break;
                    case Role.Dictateur: hint = "Coup d'État : demain matin, tu renverses quelqu'un (s'il n'est pas loup, tu tomberas)."; B("Préparer le coup d'État", () => game.Act($"night|{me}|coup"), "red"); break;
                    case Role.LoupNoir: if (!p.convertUsed && !l.nightDone.Contains(me)) B("Mordre (transformer en loup)", () => game.Act($"night|{me}|convert"), "red"); break;
                    case Role.Brumeux: if (!p.fogUsed && !l.nightDone.Contains(me)) B("Invoquer le brouillard", () => game.Act($"night|{me}|fog"), "blue"); break;
                    case Role.Anesthesiste: if (l.CanAnesth && !l.nightDone.Contains(me)) { hint += "  Puis clique un joueur à anesthésier."; if (wgSel.Count == 1) B($"Anesthésier {sel}", () => game.Act($"night|{me}|sleep|{wgSel[0]}"), "blue"); } break;
                    case Role.LoupBlanc: if (l.step == LoupGarou.WhiteStep && !l.nightDone.Contains(me)) { hint = "Loup blanc : clique ta victime à toi (en secret), ou passe."; if (wgSel.Count == 1) B($"Dévorer {sel} seul", () => game.Act($"night|{me}|white|{wgSel[0]}"), "red"); } break;
                }
                // Au tour des loups, un loup vote d'abord ; seul celui qui a un pouvoir peut ensuite passer son pouvoir.
                bool wolfTurn = l.step == LoupGarou.WolfStep && wolf;
                if (special && !l.nightDone.Contains(me) && (!wolfTurn || l.WolfPower(p) && l.wolfVotes.ContainsKey(me))) B(wolfTurn ? "Ne pas utiliser mon pouvoir" : "Passer", () => game.Act($"night|{me}|done"), "ghost");
                if (!wolf && l.nightDone.Contains(me)) hint = "C'est fait. Le village dort...";
            }
            else if (l.phase == WPh.Witch && p.role == Role.Sorciere)
            {
                hint = l.victim >= 0 ? $"Les loups ont choisi {N(l, l.victim)}." : "Les loups n'ont choisi personne.";
                if (!p.lifeUsed && l.victim >= 0 && !l.witchSave) B($"Sauver {N(l, l.victim)}", () => game.Act($"witch|{me}|save"));
                if (!p.deathUsed) { if (wgSel.Count == 1) B($"Empoisonner {sel}", () => game.Act($"witch|{me}|kill|{wgSel[0]}"), "red"); else hint += "  Clique un joueur pour l'empoisonner."; }
                B("Terminer", () => game.Act($"witch|{me}|none"), "ghost");
            }
            else if ((l.phase == WPh.Vote || l.phase == WPh.Election) && p.role == Role.Ninja && !l.players.Any(o => o.poisoned))
            {
                hint = wgShuriken ? "Clique la cible de ton shuriken empoisonné." : "Ninja : tu peux lancer discrètement ton shuriken.";
                B(wgShuriken ? "Annuler" : "Lancer le shuriken", () => wgShuriken = !wgShuriken, "red");
            }
            else if ((l.phase == WPh.Hunter || l.phase == WPh.Heir || l.phase == WPh.Dictator) && l.pending.Count > 0 && l.pending[0].seat == me)
            {
                string verb = l.phase == WPh.Hunter ? "hunt" : l.phase == WPh.Heir ? "heir" : "dict";
                hint = "Clique un joueur, puis confirme.";
                if (wgSel.Count == 1) B(l.phase == WPh.Hunter ? $"Tirer sur {sel}" : l.phase == WPh.Heir ? $"{sel} devient maire" : $"Renverser {sel}", () => game.Act($"{verb}|{me}|{wgSel[0]}"), "red");
            }
        }
        wgHint.text = hint;
        wgActions.style.display = hint.Length > 0 || wgButtons.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;

        // Chat.
        wgChatList.Clear();
        foreach (var (s, chan, text) in TakeLastN(l.chat.Where(c => game.Spectating || l.CanRead(me, c.chan)), 8))
        {
            bool anon = chan == "loups" && p.alive && p.role == Role.PetiteFille;   // la petite fille espionne sans savoir qui parle
            var who = anon ? "Un loup" : N(l, s);
            var line = Text(wgChatList, (chan == "village" ? "" : chan == "loups" ? "[Loups] " : "[Morts] ") + who + " : " + text, "wg-chat-line");
            line.EnableInClassList(chan, true);
        }
        var cc = ChatChannel();
        wgChatField.SetEnabled(cc != null);
        wgChatField.textEdition.placeholder = cc == null ? "(tu ne peux pas parler maintenant)" : cc == "loups" ? "Message aux loups..." : cc == "morts" ? "Message aux morts..." : "Message au village...";
    }

    static IEnumerable<T> TakeLastN<T>(IEnumerable<T> e, int n) { var l = e.ToList(); return l.Skip(Mathf.Max(0, l.Count - n)); }

    // Chaque image : les noms au-dessus des tetes (et le chrono de la consigne).
    public void UpdateLoup(Camera cam)
    {
        var l = game.wg;
        if (wgRevealUntil > 0 && Time.time > wgRevealUntil) { wgRevealUntil = 0; wgCardBig.style.display = DisplayStyle.None; wgBigBack.style.display = DisplayStyle.Flex; wgBigHint.style.display = DisplayStyle.Flex; }
        if (l == null || wgTags.panel == null) return;
        int me = WgMe;
        var p = WgMeP;
        if (wgTagEls.Count != l.players.Count)
        {
            wgTags.Clear(); wgTagEls.Clear();
            for (int i = 0; i < l.players.Count; i++)
            {
                var t = Div(wgTags, "qsj-tag", "wg-tag"); t.pickingMode = PickingMode.Ignore;
                Text(t, "", "qsj-tag-name").pickingMode = PickingMode.Ignore;
                Text(t, "", "qsj-tag-score").pickingMode = PickingMode.Ignore;
                wgTagEls.Add(t);
            }
        }
        var tally = l.phase == WPh.Vote || l.phase == WPh.Election || l.phase == WPh.Tie ? l.Tally() : new Dictionary<int, int>();
        var wolfTally = new Dictionary<int, int>();
        foreach (var v in l.wolfVotes.Values) wolfTally[v] = (wolfTally.TryGetValue(v, out int c) ? c : 0) + 1;
        bool iAmWolf = p.alive && LoupGarou.IsWolf(p.role);
        bool seeAll = !p.alive || game.Spectating;
        for (int i = 0; i < l.players.Count; i++)
        {
            var o = l.players[i];
            var t = wgTagEls[i];
            if (i == me) { t.style.display = DisplayStyle.None; continue; }
            t.style.display = DisplayStyle.Flex;
            var head = game.qsview.HeadOf(i) + Vector3.up * 0.12f;
            bool front = Vector3.Dot(cam.transform.forward, head - cam.transform.position) > 0;
            var sp = RuntimePanelUtils.CameraTransformWorldToPanel(wgTags.panel, head, cam);
            float w = float.IsNaN(t.resolvedStyle.width) ? 160 : t.resolvedStyle.width;
            t.style.left = sp.x - w / 2; t.style.top = sp.y - 60;
            t.style.visibility = front ? Visibility.Visible : Visibility.Hidden;
            string name = (o.mayor ? "★ " : "") + o.name + (p.lover == i ? " ♥" : "");
            var extra = new List<string>();
            if (!game.WgShownAlive(o.seat)) extra.Add("mort · " + LoupGarou.Name(o.role));
            else
            {
                if (seeAll) extra.Add(LoupGarou.Name(o.role));
                else if (iAmWolf && LoupGarou.IsWolf(o.role)) extra.Add("loup");
                else if (iAmWolf && o.role == Role.Assassin) extra.Add("Assassin");
                if (p.role == Role.Pyromane && o.doused) extra.Add("imbibé");
                if (tally.TryGetValue(i, out int vt) && !l.fog) extra.Add($"{vt} voix");
                if (l.phase == WPh.Night && iAmWolf && wolfTally.TryGetValue(i, out int wv)) extra.Add($"{wv} loup{(wv > 1 ? "s" : "")}");
                if (o.asleep) extra.Add("anesthésié");
            }
            ((Label)t[0]).text = name;
            ((Label)t[1]).text = string.Join(" · ", extra);
            t.EnableInClassList("active", wgSel.Contains(i) || (l.votes.TryGetValue(me, out int mv) && mv == i && (l.phase == WPh.Vote || l.phase == WPh.Election)) || (l.wolfVotes.TryGetValue(me, out int wm) && wm == i && l.phase == WPh.Night));
            t.EnableInClassList("dead", !game.WgShownAlive(o.seat));
        }
        RefreshLoupStatusOnly();   // a chaque image : le minuteur ne clignote plus
    }
    void RefreshLoupStatusOnly()
    {
        var l = game.wg;
        var s = wgStatus.text;
        int k = s.LastIndexOf("  ·  ");
        if (k > 0 && s.EndsWith(" s")) wgStatus.text = s.Substring(0, k);   // les secondes sont dans la barre
        float max = l == null ? 0 : Game.WgTime(l);
        var shown = l != null && !l.Finished ? DisplayStyle.Flex : DisplayStyle.None;
        if (wgTimerFill.parent.style.display != shown) wgTimerFill.parent.style.display = shown;
        if (max > 0) { wgTimerFill.style.width = Length.Percent(100 * Mathf.Clamp01(game.WgLeft / max)); wgTimerText.text = Mathf.CeilToInt(game.WgLeft).ToString(); }
    }
}
