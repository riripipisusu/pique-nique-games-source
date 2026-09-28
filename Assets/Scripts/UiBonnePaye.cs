using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// La Bonne Paye, facon jeu officiel (couleurs du plateau : turquoise, jaune dore, bleu marine) :
//  - en haut : le jour du joueur actif (onglet de calendrier comme sur le plateau) et la cagnotte (gros jeton dore) ;
//  - a cote du coin de table de chaque joueur : son etiquette (solde en gros, livret / prets / factures en pastilles) ;
//  - en bas : la question et les boutons quand il faut decider (la carte, elle, est montree en 3D par BonnePayeView) ;
//  - a mon tour : "Lancer le de" et la banque (epargner, retirer, emprunter, Besoin d'argent ?).
// Aucune saisie libre de montant : tout passe par les regles (BonnePaye).
public partial class Ui
{
    VisualElement bpHud, bpPlayers, bpCenter, bpCardImg, bpActions, bpTurnRow, bpBank, bpLog;
    Label bpPot, bpDayNum, bpDayName, bpMonth, bpPrompt;
    bool bpBankOpen;
    int bpCardDeck = -1, bpCardImgId = -1;
    IVisualElementScheduledItem bpCardHide;
    static readonly string[] BpDecks = { "courrier", "acquisition", "evenement" };
    static readonly string[] WeekDays = { "Dimanche", "Lundi", "Mardi", "Mercredi", "Jeudi", "Vendredi", "Samedi" };

    void BuildBonnePayeHud()
    {
        bpHud = Div(hud, "layer", "bp");
        bpHud.pickingMode = PickingMode.Ignore;
        var top = Div(bpHud, "bp-top"); top.pickingMode = PickingMode.Ignore;
        var day = Div(top, "bp-day");
        bpDayNum = Text(day, "", "bp-day-num");
        var dcol = Div(day, "bp-day-col");
        bpDayName = Text(dcol, "", "bp-day-name");
        bpMonth = Text(dcol, "", "bp-day-month");
        var pot = Div(top, "bp-pot");
        Text(pot, "CAGNOTTE", "bp-pot-title");
        bpPot = Text(pot, "", "bp-pot-value");
        bpLog = Div(bpHud, "bp-log"); bpLog.pickingMode = PickingMode.Ignore;

        bpCenter = Div(bpHud, "bp-center");
        bpCardImg = Div(bpCenter, "bp-card-img");
        bpPrompt = Text(bpCenter, "", "bp-prompt");
        bpActions = Div(bpCenter, "row", "bp-actions");
        bpCenter.style.display = DisplayStyle.None;
        bpCenter.AddToClassList("show");
        bpCenter.pickingMode = bpCardImg.pickingMode = bpPrompt.pickingMode = bpActions.pickingMode = PickingMode.Ignore;   // les boutons restent cliquables

        bpTurnRow = Div(bpHud, "bp-turn");
        bpTurnRow.pickingMode = PickingMode.Ignore;   // bande pleine largeur : seuls ses boutons captent la souris
        bpBank = Div(bpHud, "bp-bank");
        bpPlayers = Div(bpHud, "bp-players"); bpPlayers.pickingMode = PickingMode.Ignore;
        // Quart de tour du plateau, a tout moment (au-dessus du reste du HUD).
        var rot = Div(bpHud, "row", "bp-rotate");
        Ico(Btn(rot, "", () => game.BpRotate(1), "blue", "round"), "rot_left");
        Ico(Btn(rot, "", () => game.BpRotate(-1), "blue", "round"), "rot_right");
    }

    readonly System.Collections.Generic.List<VisualElement> bpTags = new System.Collections.Generic.List<VisualElement>();

    // Chaque image : etiquette de chaque joueur au-dessus de son coin de table.
    public void UpdateBonnePaye(Camera cam)
    {
        var b = game.bp;
        if (b == null || bpPlayers.panel == null) return;
        for (int i = 0; i < bpTags.Count && i < b.players.Count; i++)
        {
            var t = bpTags[i];
            var sp = RuntimePanelUtils.CameraTransformWorldToPanel(bpPlayers.panel, game.bpview.ZoneTag(i), cam);
            float w = float.IsNaN(t.resolvedStyle.width) ? 250 : t.resolvedStyle.width;
            float h = float.IsNaN(t.resolvedStyle.height) ? 110 : t.resolvedStyle.height;
            // Camera tournee : l'etiquette reste dans l'ecran.
            var size = bpPlayers.panel.visualTree.layout;
            t.style.left = Mathf.Clamp(sp.x - w / 2, 8, Mathf.Max(8, size.width - w - 8));
            t.style.top = Mathf.Clamp(sp.y - h, 130, Mathf.Max(130, size.height - h - 90));
        }
    }

    // Carte piochee : elle arrive au centre ; sans decision a prendre, elle se range toute seule apres 2,4 s.
    public void BpShowCard(int deck, int img)
    {
        bpCardDeck = deck; bpCardImgId = img;
        bpCardHide?.Pause();
        bpCardHide = bpCenter.schedule.Execute(() => { bpCardDeck = -1; RefreshBonnePaye(); }).StartingIn(2400);
        RefreshBonnePaye();
    }

    Button BpButton(VisualElement parent, string text, string action, bool on, params string[] cls)
    {
        var b = Btn(parent, text, () => { bpBankOpen = false; game.BpAct(action); }, cls);
        b.SetEnabled(on);
        return b;
    }

    // Pastille d'information sur la vignette d'un joueur.
    static void Badge(VisualElement parent, string text, string cls) { var l = new Label(text); l.AddToClassList("bp-badge"); l.AddToClassList(cls); parent.Add(l); }

    void RefreshBonnePaye()
    {
        var b = game.bp;
        if (b == null || bpPlayers == null) return;
        int actor = b.Actor >= 0 ? b.Actor : b.turn;
        var cur = b.players[actor];
        // Haut : jour et cagnotte.
        bpDayNum.text = cur.pos == 0 ? "—" : cur.pos.ToString();
        bpDayName.text = cur.pos == 0 ? "Départ" : WeekDays[cur.pos % 7];
        bpMonth.text = $"Mois {cur.month} / {b.months}";
        bpPot.text = $"{b.pot} €";
        bpLog.Clear();
        foreach (var l in b.log.Skip(System.Math.Max(0, b.log.Count - 4))) Text(bpLog, l, "bp-log-line");

        // Etiquettes des joueurs (placees chaque image a cote de leur coin de table : UpdateBonnePaye).
        bpPlayers.Clear(); bpTags.Clear();
        for (int i = 0; i < b.players.Count; i++)
        {
            var p = b.players[i];
            var tile = Div(bpPlayers, "bp-player");
            tile.pickingMode = PickingMode.Ignore;
            bpTags.Add(tile);
            tile.EnableInClassList("active", i == actor && !b.Finished);
            tile.EnableInClassList("done", p.done);
            var head = Div(tile, "bp-player-head");
            Ring(Portrait(head, Avatar(i), null, 40), BonnePayeView.ColorOf(i));
            var nc = Div(head, "bp-player-col");
            Text(nc, p.name, "bp-name");
            Text(nc, p.done ? "Terminé !" : p.pos == 0 ? "Départ" : $"{WeekDays[p.pos % 7]} {p.pos}", "bp-where");
            Text(tile, $"{p.money} €", "bp-money");
            var badges = Div(tile, "bp-badges");
            if (p.savings > 0) Badge(badges, $"Livret {p.savings} €", "save");
            if (p.loans > 0) Badge(badges, $"Prêts {p.loans * BonnePaye.LoanSize} €", "loan");
            if (p.unread.Count > 0) Badge(badges, $"Courrier ×{p.unread.Count}", "mail");
            if (p.bills.Count > 0) Badge(badges, $"Factures {p.BillsTotal} €", "bill");
            if (p.acqs.Count > 0) Badge(badges, $"Affaires ×{p.acqs.Count}", "acq");
            if (p.medic) Badge(badges, "Médic'Assur", "ins");
            if (p.auto) Badge(badges, "Assur'Auto", "ins");
            if (p.besoin.Count > 0) Badge(badges, $"Besoin d'argent ×{p.besoin.Count}", "need");
        }

        // Centre : carte et decision.
        bpActions.Clear(); bpTurnRow.Clear(); bpBank.Clear();
        var job = b.Pending;
        bool mine = !game.busy && game.MyTurn && !game.Spectating;
        int deck = -1, img = -1;
        if (job != null && job.t == BonnePaye.Task.Insure) { deck = 0; img = BonnePaye.Mails[job.a].img; }
        else if (job != null && job.t == BonnePaye.Task.Buy) { deck = 1; img = BonnePaye.Acqs[job.a].img; }
        else if (bpCardDeck >= 0) { deck = bpCardDeck; img = bpCardImgId; }
        bool tex = deck >= 0;
        bpCardImg.style.display = DisplayStyle.None;   // la carte est montree en 3D
        bpPrompt.text = "";
        if (job == null && deck == 0 && img < 0) bpPrompt.text = "Courrier mis de côté : surprise au Jour de paye !";
        else if (job == null && deck == 0)   // courrier ouvert : ce qu'il devient
        {
            var m = BonnePaye.Mails.FirstOrDefault(x => x.img == img);
            if (m != null) bpPrompt.text = m.t == "bill" ? $"Facture de {m.v} € : payée avec les autres factures du mois." : m.t == "cash" ? $"À régler comptant : {m.v} €." : m.t == "gain" ? $"Coup de chance : +{m.v} € !" : m.t == "besoin" ? "Carte gardée : jouez-la quand vous voulez à votre tour (Banque)." : "";
        }
        bool drawing = job != null && BonnePaye.IsDraw(job.t);
        bool center = tex || (job != null && !drawing && !game.busy);
        if (drawing && !game.busy)
        {
            string pile = job.t == BonnePaye.Task.MailDraw ? "Courrier" : job.t == BonnePaye.Task.AcqDraw ? "Acquisition" : "Événement";
            var who = b.players[job.seat];
            Text(bpTurnRow, mine ? $"Pioche une carte {pile} : clique sur la pioche !" : $"{who.name} pioche une carte {pile}...", "bp-turn-label");
            if (mine) Btn(bpTurnRow, "Piocher", () => game.BpAct("draw"), "green");
        }
        else if (job != null && !game.busy)
        {
            var who = b.players[job.seat];
            if (!mine) bpPrompt.text = job.t == BonnePaye.Task.CommRoll ? $"{who.name} lance le dé pour la commission..." + (b.commRolls.Count > 0 ? $"  (à battre : {b.commRolls.Values.Max()})" : "")
                : job.t == BonnePaye.Task.LottoRoll ? $"{who.name} lance le dé de la loterie..." + (b.lottoRolls.Count > 0 ? $"  (à battre : {b.lottoRolls.Values.Max()})" : "")
                : job.t == BonnePaye.Task.BesoinRoll ? $"{who.name} tente sa chance : 5 ou 6 au dé !" : $"{who.name} réfléchit...";
            else switch (job.t)
            {
                case BonnePaye.Task.Insure:
                {
                    var m = BonnePaye.Mails[job.a];
                    bpPrompt.text = $"Souscrire {m.nom} ? Elle annulera toutes vos factures de {(m.cat == "med" ? "médecin" : "garagiste")}.";
                    BpButton(bpActions, $"Souscrire  {m.v} €", "yes", true, "green");
                    BpButton(bpActions, "Non merci", "no", true, "ghost");
                    break;
                }
                case BonnePaye.Task.Buy:
                {
                    var q = BonnePaye.Acqs[job.a];
                    bpPrompt.text = $"Acheter cette affaire ? Revendue {q.valeur} € sur une case « Vendez ! » (bénéfice {q.valeur - q.achat} €).";
                    BpButton(bpActions, $"Acheter  {q.achat} €", "yes", true, "green");
                    BpButton(bpActions, "Pas intéressé", "no", true, "ghost");
                    break;
                }
                case BonnePaye.Task.Sell:
                    bpPrompt.text = "Vendez ! Quelle affaire revendez-vous à la banque ?";
                    for (int k = 0; k < who.acqs.Count; k++)
                    {
                        var q = BonnePaye.Acqs[who.acqs[k]];
                        BpButton(bpActions, $"{q.nom.Substring(2)}\n<size=17>{q.valeur} €  (+{q.valeur - q.achat} €)</size>", "sell|" + k, true, "green", "small");
                    }
                    BpButton(bpActions, "Rien", "sell|-1", true, "ghost", "small");
                    break;
                case BonnePaye.Task.LottoJoin:
                    bpPrompt.text = $"Loterie : {b.lottoPot} € en jeu ! Participer pour 100 € ? Chaque joueur lance le dé : le plus gros chiffre rafle tout."
                        + (b.lottoIn.Count > 0 ? $"\nDéjà inscrits : {string.Join(", ", b.lottoIn.Select(s => b.players[s].name))}" : "");
                    BpButton(bpActions, "Je joue  (100 €)", "lotto|1", who.money >= 100, "green");
                    BpButton(bpActions, "Je ne joue pas", "lotto|0", true, "ghost");
                    break;
                case BonnePaye.Task.LottoRoll:
                    bpPrompt.text = $"Loterie ({b.lottoPot} €) : à toi de lancer le dé ! Maintiens le clic et lâche-le d'un geste."
                        + (b.lottoRolls.Count > 0 ? $"\nÀ battre : {string.Join(" · ", b.lottoRolls.Select(kv => $"{b.players[kv.Key].name} {kv.Value}"))}" : "");
                    Ico(Btn(bpActions, "Lancer le dé", () => game.ThrowBonnePayeAuto(), "lg"), "dice");
                    break;
                case BonnePaye.Task.BesoinBet:
                    bpPrompt.text = "Fin du tour : joue ta carte « Besoin d'argent ? » ! Combien mises-tu ? (5 ou 6 au dé : 10 fois la mise ; sinon elle part à la cagnotte)";
                    foreach (int x in new[] { 50, 100, 200, 250 }) BpButton(bpActions, $"Miser {x} €", "besoin|" + x, true, "red", "small");
                    break;
                case BonnePaye.Task.CommRoll:
                    bpPrompt.text = $"Commission de {b.commission} € : lance le dé, le plus gros chiffre la touche !"
                        + (b.commRolls.Count > 0 ? $"\nÀ battre : {string.Join(" · ", b.commRolls.Select(kv => $"{b.players[kv.Key].name} {kv.Value}"))}" : "");
                    Ico(Btn(bpActions, "Lancer le dé", () => game.ThrowBonnePayeAuto(), "lg"), "dice");
                    break;
                case BonnePaye.Task.BesoinRoll:
                    bpPrompt.text = $"Besoin d'argent ? Lance le dé : 5 ou 6, tu touches {job.a * 10} € ; sinon tes {job.a} € partent à la cagnotte.";
                    Ico(Btn(bpActions, "Lancer le dé", () => game.ThrowBonnePayeAuto(), "lg"), "dice");
                    break;
                case BonnePaye.Task.Extend:
                    bpPrompt.text = $"Fin du mois {b.months} ! Prolonger la partie ?";
                    BpButton(bpActions, "Terminer la partie", "extend|0", true, "red", "small");
                    foreach (int n in new[] { 1, 2, 3, 6, 12 }) BpButton(bpActions, $"+{n} mois", "extend|" + n, true, "green", "small");
                    break;
                case BonnePaye.Task.Repay:
                    bpPrompt.text = $"Jour de paye : rembourser des prêts ? (150 € d'intérêts par prêt et par mois)";
                    for (int k = 0; k <= System.Math.Min(who.loans, who.money / BonnePaye.LoanSize); k++)
                        BpButton(bpActions, k == 0 ? "Plus tard" : $"Rembourser {k * BonnePaye.LoanSize} €", "repay|" + k, true, k == 0 ? "ghost" : "green", "small");
                    break;
            }
        }
        bpCenter.style.display = center ? DisplayStyle.Flex : DisplayStyle.None;
        bpPrompt.style.display = bpPrompt.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

        // A mon tour : lancer le de, et la banque.
        if (b.Finished || game.busy || job != null || b.rolled || !mine) { bpBank.style.display = DisplayStyle.None; return; }
        var me = b.players[b.turn];
        Ico(Btn(bpTurnRow, "Lancer le dé", () => { bpBankOpen = false; game.ThrowBonnePayeAuto(); }, "lg"), "dice");
        Btn(bpTurnRow, bpBankOpen ? "Fermer la banque" : "Banque", () => { bpBankOpen = !bpBankOpen; RefreshBonnePaye(); }, "blue");
        bpBank.style.display = bpBankOpen ? DisplayStyle.Flex : DisplayStyle.None;
        if (!bpBankOpen) return;
        Text(bpBank, b.CanDeposit(b.turn) ? "Livret d'épargne : 50 € d'intérêts par tranche de 500 € au Jour de paye" : "Livret : plus de dépôt après le 22 du mois", "bp-bank-title");
        var row = Div(bpBank, "row", "bp-bank-row");
        foreach (int x in new[] { 100, 500, 1000 }) BpButton(row, $"Épargner {x} €", "save|" + x, b.CanDeposit(b.turn) && me.money >= x, "green", "small");
        if (me.savings > 0) BpButton(row, $"Retirer {me.savings} €\n<size=15>frais 150 €</size>", "withdraw|" + me.savings, true, "ghost", "small");
        BpButton(row, "Emprunter 1500 €", "borrow|1", true, "ghost", "small");
        if (me.besoin.Count > 0)
        {
            Text(bpBank, "Besoin d'argent ? Un 5 ou un 6 rapporte 10 fois la mise, sinon elle part à la cagnotte", "bp-bank-title");
            var r2 = Div(bpBank, "row", "bp-bank-row");
            foreach (int x in new[] { 50, 100, 200, 250 }) BpButton(r2, $"Miser {x} €", "besoin|" + x, me.money >= x, "red", "small");
        }
    }
}
