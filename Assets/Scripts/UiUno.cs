using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// HUD du Uno (facon jeu Uno) : ma main en bas, vignettes des joueurs (portrait, nom, nombre de cartes) a cote
// de leur eventail sur la nappe, boutons Piocher / UNO ! / Contre-UNO !, roue des couleurs pour les jokers,
// question "jouer la carte piochee ?", decompte des points a chaque fin de manche.
public partial class Ui
{
    VisualElement unoHud, unoHand, unoTags, unoWheel, unoDrawnBox, unoDrawnCard, unoRound, unoRoundList, unoChallenge, unoChallengeCard, unoSwap, unoSwapList;
    Button unoKeep;
    Label unoChallengeText;
    Button unoDraw, unoCall, unoCatch;
    Label unoTurn, unoColor, unoRoundTitle, unoStatus, unoToast, unoCatchWho;
    IVisualElementScheduledItem toastHide;
    readonly List<VisualElement> unoTagEls = new List<VisualElement>();
    int unoPicked = -1;   // joker en attente de sa couleur
    VisualElement unoCallGlow;
    bool wasMine, callShown, drawnShown, challengeShown, swapShown;   // pour jouer les sons d'apparition une seule fois
    readonly HashSet<int> handShown = new HashSet<int>();   // cartes deja affichees dans ma main
    int handRound = -1;
    readonly HashSet<int> incoming = new HashSet<int>();   // cartes en vol vers ma main : place reservee, invisible
    readonly Dictionary<int, VisualElement> handButtons = new Dictionary<int, VisualElement>();

    // La prochaine carte de ma main encore a recevoir (ordre de la main = ordre de reception) : sa place est reservee.
    public int ReserveIncoming()
    {
        var u = game.uno;
        if (u == null) return -1;
        foreach (int c in u.players[UnoMe].hand)
            if (!handShown.Contains(c) && incoming.Add(c)) { RefreshUno(); return c; }
        return -1;
    }
    // Rectangle a l'ecran (pixels, origine en bas a gauche) de la carte dans ma main.
    public Rect? IncomingScreen(int card)
    {
        if (!handButtons.TryGetValue(card, out var b) || float.IsNaN(b.worldBound.width) || root.worldBound.width <= 0) return null;
        float k = UnityEngine.Screen.width / root.worldBound.width;
        var w = b.worldBound;
        return new Rect(w.x * k, UnityEngine.Screen.height - w.yMax * k, w.width * k, w.height * k);
    }
    public void LandIncoming(int card) { incoming.Remove(card); handShown.Add(card); RefreshUno(); }

    int UnoMe => Mathf.Max(0, game.mySeat);
    bool UnoPlaying => game.uno != null && !game.Spectating;

    static readonly Color[] UnoColors = { Board.Hex("e5322d"), Board.Hex("f7c315"), Board.Hex("3aa84a"), Board.Hex("1f6fc5") };

    // Pulsation des boutons a ne pas rater (UNO !, Contre-UNO !) : on alterne une classe toutes les 0,45 s.
    void UnoPulse()
    {
        foreach (var b in new[] { unoCall, unoCatch }) if (b != null) b.EnableInClassList("big", !b.ClassListContains("big"));
        if (unoHand != null) foreach (var g in unoHand.Query(className: "uno-glow").ToList()) g.EnableInClassList("low", !g.ClassListContains("low"));
        if (unoCallGlow != null) unoCallGlow.EnableInClassList("low", !unoCallGlow.ClassListContains("low"));
    }

    void BuildUnoHud()
    {
        hud.schedule.Execute(UnoPulse).Every(450);
        unoHud = Div(hud, "layer");
        unoHud.pickingMode = PickingMode.Ignore;
        unoTags = Div(unoHud, "layer");
        unoTags.pickingMode = PickingMode.Ignore;

        var top = Div(unoHud, "uno-top");
        top.pickingMode = PickingMode.Ignore;
        unoTurn = Text(top, "", "uno-turn");
        unoColor = Text(top, "", "uno-color");
        var toastRow = Div(unoHud, "uno-toast-row");   // en bas, au-dessus de ma main : ne cache aucune vignette
        toastRow.pickingMode = PickingMode.Ignore;
        unoToast = Text(toastRow, "", "uno-toast", "hide");

        unoHand = Div(unoHud, "uno-hand");
        unoStatus = Text(unoHud, "", "uno-status");

        unoDraw = Btn(unoHud, "Piocher", () => { if (game.uno?.phase == UPhase.Play) game.Act("draw"); }, "uno-draw");
        // Bouton UNO : le logo lui-meme (Resources/Uno/uno_logo), qui pulse quand il faut appuyer.
        unoCallGlow = Div(unoHud, "uno-call-glow");   // lueur d'origine "PrepareCallUNO" derriere le bouton
        unoCallGlow.pickingMode = PickingMode.Ignore;
        unoCallGlow.style.backgroundImage = Resources.Load<Texture2D>("UnoFX/PrepareCallUNO");
        unoCall = new Button(() => { Sound.I.UI("click"); callShown = false; game.UnoCall("uno|" + UnoMe); });
        unoCall.AddToClassList("uno-call");
        unoCall.style.backgroundImage = Resources.Load<Texture2D>("Uno/uno_logo");
        unoCall.RegisterCallback<MouseEnterEvent>(_ => Sound.I.UI("hover"));
        unoHud.Add(unoCall);
        // Contre-UNO : le logo barre d'un bandeau rouge "CONTRE !", au-dessus du bouton UNO.
        unoCatch = new Button(() => { Sound.I.UI("click"); if (game.uno != null && game.uno.vulnerable >= 0) game.UnoCall($"catch|{UnoMe}|{game.uno.vulnerable}"); });
        unoCatch.AddToClassList("uno-catch");
        unoCatch.style.backgroundImage = Resources.Load<Texture2D>("Uno/uno_logo");
        unoCatch.RegisterCallback<MouseEnterEvent>(_ => Sound.I.UI("hover"));
        Text(unoCatch, "CONTRE !", "uno-catch-band").pickingMode = PickingMode.Ignore;
        unoCatchWho = Text(unoCatch, "", "uno-catch-who");
        unoCatchWho.pickingMode = PickingMode.Ignore;
        unoHud.Add(unoCatch);

        // Roue des 4 couleurs (joker) : quatre quarts de cercle.
        unoWheel = Div(unoHud, "uno-wheel-layer");
        var wheel = Div(unoWheel, "uno-wheel");
        for (int c = 0; c < 4; c++)
        {
            int col = c;
            var q = new Button(() => { Sound.I.UI("click"); PlayUno(unoPicked, col); }) { text = "" };
            q.AddToClassList("uno-quarter"); q.AddToClassList("q" + c);
            q.style.backgroundColor = UnoColors[c];
            wheel.Add(q);
        }
        Text(unoWheel, "Choisis la couleur", "uno-wheel-title");
        unoWheel.RegisterCallback<PointerDownEvent>(e => { if (e.target == unoWheel) { unoPicked = -1; unoWheel.style.display = DisplayStyle.None; } });

        // Carte piochee jouable : la poser tout de suite ou la garder.
        unoDrawnBox = Div(unoHud, "uno-popup", "uno-drawn");
        Text(unoDrawnBox, "Tu as pioché :", "h2").style.marginTop = 0;
        unoDrawnCard = Div(unoDrawnBox, "uno-drawn-card");
        var row = Div(unoDrawnBox, "row");
        Btn(row, "La jouer", () => { if (game.uno != null) TryPlayUno(game.uno.drawn); }, "green", "small");
        unoKeep = Btn(row, "La garder", () => game.Act("keep"), "ghost", "small");

        // Contestation du +4 : Denoncer (le poseur avait la couleur demandee ?) ou Accepter.
        unoChallenge = Div(unoHud, "uno-popup", "uno-challenge");
        unoChallengeText = Text(unoChallenge, "", "uno-challenge-text");
        var crow = Div(unoChallenge, "row", "uno-challenge-row");
        Btn(crow, "Dénoncer", () => game.Act("challenge"), "red");
        unoChallengeCard = Div(crow, "uno-challenge-card");
        Btn(crow, "Accepter", () => game.Act("accept"), "blue");
        Text(unoChallenge, "Bluff démasqué : il pioche 4 cartes. Sinon, tu en pioches 6 !", "uno-challenge-hint");

        // Regle du 7 : avec qui echanger sa main ?
        unoSwap = Div(unoHud, "uno-popup", "uno-swap");
        Text(unoSwap, "Échanger ta main avec…", "panel-title");
        unoSwapList = Div(unoSwap, "row", "uno-swap-list");

        // Fin de manche : qui a gagne, combien de points, et le total de chacun.
        unoRound = Div(unoHud, "panel", "uno-round");
        unoRoundTitle = Text(unoRound, "", "panel-title");
        unoRoundList = Div(unoRound);
    }

    public void ShowUnoHud()
    {
        unoTags.Clear(); unoTagEls.Clear();
        var u = game.uno;
        for (int i = 0; i < u.players.Count; i++)
        {
            var tag = Div(unoTags, "uno-tag");
            tag.pickingMode = PickingMode.Ignore;
            var por = Portrait(tag, AvatarUno(i), null, 64);
            por.pickingMode = PickingMode.Ignore;
            Ring(por, Board.Colors[i % Board.Colors.Length]);
            var col = Div(tag, "uno-tag-col");
            Text(col, u.players[i].name, "uno-tag-name");
            var cards = Div(col, "row", "uno-tag-cards");
            Div(cards, "uno-tag-card-icon");
            Text(cards, "", "uno-tag-count");
            Text(tag, "", "uno-bubble", "pop");
            unoTagEls.Add(tag);
        }
        unoWheel.style.display = DisplayStyle.None;
        unoRound.style.display = DisplayStyle.None;
        unoPicked = -1;
        RefreshUno();
    }

    string AvatarUno(int seat) => game.unoAvatars.Count > seat ? game.unoAvatars[seat] : Chars.Default;

    public void UnoBubble(int seat, string text)
    {
        if (seat < 0 || seat >= unoTagEls.Count) return;
        var b = unoTagEls[seat].Q<Label>(className: "uno-bubble");
        b.text = text;
        b.style.backgroundImage = Resources.Load<Texture2D>("UI/uno_burst");
        b.RemoveFromClassList("pop");
        b.AddToClassList("burst");
        b.schedule.Execute(() => b.RemoveFromClassList("burst")).StartingIn(40);    // surgit en grossissant
        b.schedule.Execute(() => b.AddToClassList("pop")).StartingIn(1300);
    }

    // Passe son tour : le logo "interdit" (a la couleur du jeu) surgit par-dessus la vignette du joueur.
    public void UnoSkipMark(int seat, int color)
    {
        if (seat < 0 || seat >= unoTagEls.Count) return;
        var m = Div(unoTagEls[seat], "uno-skip-mark", "pop");
        m.pickingMode = PickingMode.Ignore;
        m.style.backgroundImage = Resources.Load<Texture2D>("UI/fx_skip");
        m.style.unityBackgroundImageTintColor = UnoColors[Mathf.Clamp(color, 0, 3)];
        m.schedule.Execute(() => m.RemoveFromClassList("pop")).StartingIn(20);      // surgit en grossissant
        m.schedule.Execute(() => m.AddToClassList("gone")).StartingIn(1100);        // puis s'efface
        m.schedule.Execute(() => m.RemoveFromHierarchy()).StartingIn(1600);
    }

    public void UnoToast(string text)
    {
        unoToast.text = text;
        unoToast.RemoveFromClassList("hide");
        toastHide?.Pause();
        toastHide = unoToast.schedule.Execute(() => unoToast.AddToClassList("hide")).StartingIn(2200);
    }

    public void UnoNewRound() { unoRound.style.display = DisplayStyle.None; RefreshUno(); }

    public void UnoRoundOver()
    {
        var u = game.uno;
        var w = u.players[u.roundWinner];
        unoRoundTitle.text = $"{w.name} remporte la manche !";
        unoRoundList.Clear();
        foreach (var p in u.players.OrderByDescending(p => p.score))
        {
            var row = Div(unoRoundList, "qz-row");
            Portrait(row, AvatarUno(p.seat), null, 44);
            Text(row, p.name, "seat-name").style.flexGrow = 1;
            Text(row, p.lastGain > 0 ? $"+{p.lastGain}" : $"{p.hand.Count} cartes", "uno-gain");
            Text(row, p.score + (u.target > 0 ? $" / {u.target}" : "") + " pts", "qz-score");
        }
        unoRound.style.display = DisplayStyle.Flex;
    }

    public void TryPlayUno(int card)
    {
        var u = game.uno;
        if (u != null && UnoPlaying && u.CanJumpIn(UnoMe, card)) { game.UnoCall($"jump|{UnoMe}|{card}"); return; }   // intervention
        if (u == null || !u.CanPlay(UnoMe, card) || !game.MyTurn)
        {
            return;
        }
        if (Uno.IsWild(card)) { unoPicked = card; game.uview.ShowWheelPick(); return; }   // choix sur la roue 3D
        PlayUno(card, -1);
    }

    public void UnoPlayColor(int color) { if (unoPicked >= 0) PlayUno(unoPicked, color); }

    void PlayUno(int card, int color)
    {
        unoWheel.style.display = DisplayStyle.None;
        if (card < 0) return;
        unoPicked = -1;
        // UNO dit d'avance (bouton appuye avec deux cartes) : deja note par les regles ; sinon rien.
        game.Act($"play|{card}|{color}|0");
    }

    void RefreshUno()
    {
        var u = game.uno;
        if (u == null) return;
        bool mine = UnoPlaying && u.Actor == UnoMe && game.MyTurn;
        var me = u.players[UnoMe];
        // Ma main, triee par couleur puis valeur ; jouables surelevees quand c'est mon tour.
        unoHand.Clear();
        unoHand.style.display = UnoPlaying ? DisplayStyle.Flex : DisplayStyle.None;
        if (handRound != game.uview.DealRound) { handRound = game.uview.DealRound; handShown.Clear(); incoming.Clear(); }   // nouvelle donne
        incoming.RemoveWhere(c => !me.hand.Contains(c));
        handButtons.Clear();
        int fresh = 0;
        var cards = me.hand.OrderBy(c => Uno.CardColor(c)).ThenBy(Uno.Kind).ToList();
        // Cartes pas encore arrivees (donne, pioche en cours) : absentes, sauf celles en vol dont la place est reservee.
        if (game.uview.MyPending > 0) cards = cards.Where(c => handShown.Contains(c) || incoming.Contains(c)).ToList();
        float overlap = cards.Count <= 7 ? -18 : -Mathf.Min(110, 18 + (cards.Count - 7) * 10);
        float spread = Mathf.Min(4.5f, 40f / Mathf.Max(1, cards.Count));   // degres entre deux cartes
        for (int k = 0; k < cards.Count; k++)
        {
            int card = cards[k];
            float t = k - (cards.Count - 1) / 2f;
            var slot = Div(unoHand, "uno-slot");
            slot.style.marginLeft = slot.style.marginRight = overlap / 2;
            slot.style.rotate = new Rotate(new Angle(t * spread));
            slot.style.translate = new Translate(0, t * t * spread * 0.9f);
            var b = new Button(() => { Sound.I.UI("click"); TryPlayUno(card); });
            b.AddToClassList("uno-card");
            b.style.backgroundImage = Resources.Load<Texture2D>("Uno/" + Uno.Code(card));
            handButtons[card] = b;
            if (incoming.Contains(card)) { b.style.opacity = 0; b.pickingMode = PickingMode.Ignore; }   // en vol : place reservee
            // Carte arrivee autrement (echange de mains...) : elle monte du bas de l'ecran, les unes apres les autres.
            else if (handShown.Add(card))
            {
                b.AddToClassList("enter"); b.AddToClassList("entering");
                b.schedule.Execute(() => b.RemoveFromClassList("enter")).StartingIn(30 + 55 * fresh++);
                b.schedule.Execute(() => b.RemoveFromClassList("entering")).StartingIn(500 + 55 * fresh);
            }
            bool ok = mine && u.CanPlay(UnoMe, card) || UnoPlaying && u.CanJumpIn(UnoMe, card);
            if (ok)   // lueur doree d'origine (Classic_Highlight) derriere la carte jouable
            {
                var glow = Div(slot, "uno-glow");
                glow.pickingMode = PickingMode.Ignore;
                glow.style.backgroundImage = Resources.Load<Texture2D>("UnoFX/Classic_Highlight");
                glow.SendToBack();
            }
            b.EnableInClassList("playable", ok);
            b.EnableInClassList("dim", mine && !ok);
            b.RegisterCallback<MouseEnterEvent>(_ => Sound.I.UI("hover"));
            slot.Add(b);
        }
        wasMine = mine;
        unoDraw.style.display = DisplayStyle.None;   // on pioche en cliquant sur le paquet, sur la nappe
        game.uview.DeckReady = mine && u.phase == UPhase.Play;
        // UNO : seulement a mon tour, avec exactement deux cartes dont une qui peut etre posee.
        unoCall.style.display = UnoPlaying && game.MyTurn && u.Actor == UnoMe && !me.said && me.hand.Count == 2 && me.hand.Any(c => u.CanPlay(UnoMe, c)) ? DisplayStyle.Flex : DisplayStyle.None;
        unoCatch.EnableInClassList("uno-pulse", true);
        unoCall.EnableInClassList("uno-pulse", true);
        bool callOn = unoCall.style.display == DisplayStyle.Flex;
        unoCallGlow.style.display = callOn ? DisplayStyle.Flex : DisplayStyle.None;
        callShown = callOn;
        unoCatch.style.display = UnoPlaying && u.vulnerable >= 0 && u.vulnerable != UnoMe ? DisplayStyle.Flex : DisplayStyle.None;
        if (u.vulnerable >= 0) unoCatchWho.text = $"{u.players[u.vulnerable].name} a oublié !";
        unoDrawnBox.style.display = mine && u.phase == UPhase.Drawn ? DisplayStyle.Flex : DisplayStyle.None;
        unoKeep.style.display = u.forcePlay ? DisplayStyle.None : DisplayStyle.Flex;   // jeu force : on doit la poser
        bool drawnOn = unoDrawnBox.style.display == DisplayStyle.Flex;
        drawnShown = drawnOn;
        bool challenge = UnoPlaying && u.phase == UPhase.Challenge && u.turn == UnoMe;
        unoChallenge.style.display = challenge ? DisplayStyle.Flex : DisplayStyle.None;
        challengeShown = challenge;
        if (challenge)
        {
            unoChallengeText.text = $"{u.players[u.w4Seat].name} t'a mis un +4 !";
            unoChallengeCard.style.backgroundImage = Resources.Load<Texture2D>("Uno/" + UnoView.FaceCode(u.Top, u.color));
        }
        bool swap = UnoPlaying && u.phase == UPhase.SwapPick && u.turn == UnoMe;
        unoSwap.style.display = swap ? DisplayStyle.Flex : DisplayStyle.None;
        swapShown = swap;
        if (swap)
        {
            unoSwapList.Clear();
            foreach (var p in u.players.Where(p => p.seat != UnoMe))
            {
                int who = p.seat;
                var b = new Button(() => { Sound.I.UI("click"); game.Act("swap|" + who); });
                b.AddToClassList("uno-swap-btn");
                Ring(Portrait(b, AvatarUno(who), null, 70), Board.Colors[who % Board.Colors.Length]);
                Text(b, p.name, "uno-tag-name");
                Text(b, $"{p.hand.Count} cartes", "uno-tag-count");
                unoSwapList.Add(b);
            }
        }
        if (u.drawn >= 0) unoDrawnCard.style.backgroundImage = Resources.Load<Texture2D>("Uno/" + Uno.Code(u.drawn));

        unoTurn.text = u.phase == UPhase.RoundOver || u.Finished ? "Fin de la manche"
            : u.phase == UPhase.Challenge ? (u.turn == UnoMe && UnoPlaying ? "Dénoncer le +4 ?" : $"{u.players[u.turn].name} réfléchit au +4...")
            : u.phase == UPhase.SwapPick ? (u.turn == UnoMe && UnoPlaying ? "Avec qui échanger ?" : $"{u.players[u.turn].name} choisit avec qui échanger...")
            : u.Actor == UnoMe && UnoPlaying ? "À toi de jouer !" : $"Au tour de {u.players[u.Actor].name}";
        unoColor.text = $"Couleur : {Uno.ColorNames[u.color]}" + (u.pending > 0 ? $"   ·   +{u.pending} en attente" : "");
        unoColor.style.backgroundColor = UnoColors[u.color];
        unoStatus.text = game.Spectating ? "Tu regardes la partie : tu joueras à la prochaine !"
            : u.pending > 0 && mine ? $"Pose un +{(Uno.Kind(u.Top) == Uno.Draw2 ? "2 ou un +4" : "4")}, ou clique sur la pioche ({u.pending} cartes)."
            : mine && u.phase == UPhase.Play && !me.hand.Any(u.Playable) ? "Aucune carte ne va : clique sur la pioche !" : "";
        unoStatus.style.display = unoStatus.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;   // vide = invisible (sinon une pastille sombre reste)
        for (int i = 0; i < unoTagEls.Count; i++)
        {
            var t = unoTagEls[i];
            t.Q<Label>(className: "uno-tag-count").text = u.players[i].hand.Count.ToString();
            t.EnableInClassList("active", u.Actor == i);
            t.EnableInClassList("uno-said", u.players[i].said && u.players[i].hand.Count == 1);
        }
    }

    // Chaque image : les vignettes suivent l'eventail de chaque joueur a l'ecran (la mienne en bas a gauche).
    public void UpdateUno(Camera cam)
    {
        var u = game.uno;
        if (u == null || unoTags.panel == null) return;
        for (int i = 0; i < unoTagEls.Count; i++)
        {
            var t = unoTagEls[i];
            if (i == UnoMe && UnoPlaying) { t.style.left = 36; t.style.top = StyleKeyword.Auto; t.style.bottom = 30; continue; }
            // Au-dessus de son eventail, centree : rien ne passe devant ses cartes.
            var sp = RuntimePanelUtils.CameraTransformWorldToPanel(unoTags.panel, game.uview.TagOf(i), cam);
            float w = float.IsNaN(t.resolvedStyle.width) ? 230 : t.resolvedStyle.width;
            t.style.left = sp.x - w / 2;
            t.style.top = sp.y - 92;
            t.style.bottom = StyleKeyword.Auto;
        }
    }
}
