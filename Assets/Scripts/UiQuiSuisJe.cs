using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Qui suis-je ? : etiquettes (nom, score) et bulles (question, reponse) au-dessus des tetes, mes indices a gauche,
// en bas : le choix du personnage du voisin, puis le panneau de mon tour (question ecrite ou a l'oral, proposer un
// nom, passer) ou le vote des autres.
public partial class Ui
{
    VisualElement qsHud, qsTags, qsClues, qsPanel, qsVote, qsChoose, qsChooseFree, qsProps, qsFreeRow, qsTabs, qsGrid;
    string qsGroup = "Général";
    readonly List<string> qsPropNames = new List<string>();
    Label qsStatus, qsVoteText, qsChooseText;
    TextField qsGuess, qsFree, qsPick;
    Button qsOral, qsMoreProps;
    readonly List<VisualElement> qsTagEls = new List<VisualElement>(), qsBubbles = new List<VisualElement>();
    readonly List<float> qsBubbleUntil = new List<float>();

    int QsMe => game.qsj == null ? 0 : Mathf.Clamp(game.mySeat, 0, game.qsj.players.Count - 1);

    TextField QsField(VisualElement parent, string placeholder, int max, System.Action enter)
    {
        var f = Add(parent, new TextField { maxLength = max }, "qsj-input");
        f.textEdition.placeholder = placeholder;
        f.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) enter(); }, TrickleDown.TrickleDown);
        return f;
    }

    void BuildQuiSuisJeHud()
    {
        qsHud = Div(hud, "layer"); qsHud.pickingMode = PickingMode.Ignore;
        qsTags = Div(qsHud, "layer"); qsTags.pickingMode = PickingMode.Ignore;
        qsStatus = Text(qsHud, "", "qsj-status"); qsStatus.pickingMode = PickingMode.Ignore;
        qsClues = Div(qsHud, "panel", "qsj-clues"); qsClues.pickingMode = PickingMode.Ignore;
        Text(qsHud, "Clic droit : regarder autour · Molette : zoomer · Clic sur un joueur : lire son post-it", "qsj-hint").pickingMode = PickingMode.Ignore;

        // Choix du personnage du voisin.
        qsChoose = Div(qsHud, "panel", "qsj-vote");
        qsChooseText = Text(qsChoose, "", "qsj-vote-text");
        qsChooseFree = Div(qsChoose, "row", "qsj-row");
        qsPick = QsField(qsChooseFree, "Un personnage, une star, un animal, un objet...", 60, SendPick);
        Btn(qsChooseFree, "Valider", SendPick, "green");
        // Hors ligne : des propositions (les bots ne connaissent que la liste).
        qsProps = Div(qsChoose, "qsj-grid");
        qsMoreProps = Btn(qsChoose, "Autres propositions", () => { qsPropNames.Clear(); RefreshQuiSuisJe(); }, "ghost", "small");

        // Mon tour.
        qsPanel = Div(qsHud, "panel", "qsj-panel");
        qsTabs = Div(qsPanel, "row", "qsj-tabs");
        foreach (var g in QuiSuisJe.Questions.Select(x => x.group).Distinct())
        {
            var grp = g;
            Btn(qsTabs, grp, () => { qsGroup = grp; RefreshQuiSuisJe(); }, "ghost", "small", "qsj-tab");
        }
        qsGrid = Div(qsPanel, "qsj-grid");
        qsFreeRow = Div(qsPanel, "row", "qsj-row");
        qsFree = QsField(qsFreeRow, "Pose ta question (réponse oui ou non)...", 80, SendFree);
        Btn(qsFreeRow, "Demander", SendFree, "qsj-ask");
        qsOral = Btn(qsFreeRow, "Posée à l'oral", () => { if (game.CanAct) game.Act("askoral"); }, "ghost");
        var row = Div(qsPanel, "row", "qsj-row");
        qsGuess = QsField(row, "Je suis... (propose un nom)", 60, SendGuess);
        Btn(row, "Proposer", SendGuess, "green");
        Btn(row, "Passer la main", () => { if (game.CanAct) game.Act("pass"); }, "ghost");

        // Vote.
        qsVote = Div(qsHud, "panel", "qsj-vote");
        qsVoteText = Text(qsVote, "", "qsj-vote-text");
        var vr = Div(qsVote, "row", "qsj-row");
        Btn(vr, "Oui", () => Vote(QuiSuisJe.Oui), "green");
        Btn(vr, "Non", () => Vote(QuiSuisJe.Non), "qsj-no");
        Btn(vr, "Je ne sais pas", () => Vote(QuiSuisJe.NeSaitPas), "ghost");
    }

    static string QsClean(TextField f) { var t = f.value.Trim().Replace("|", ""); return t; }
    void SendPick() { var t = QsClean(qsPick); if (t.Length == 0 || game.qsj == null || game.qsj.phase != WPhase.Choose) return; game.Act("pick|" + t); qsPick.value = ""; }
    void SendFree() { var t = QsClean(qsFree); if (t.Length == 0 || !game.CanAct) return; game.Act("askfree|" + t); qsFree.value = ""; }
    void SendGuess() { var t = QsClean(qsGuess); if (t.Length == 0 || !game.CanAct) return; game.Act("guess|" + t); qsGuess.value = ""; }
    void Vote(int v) { if (game.qsj != null && game.qsj.phase == WPhase.Vote && !game.qsj.votes.ContainsKey(QsMe)) game.Act("vote|" + v); }

    public void QsjBubble(int seat, string text, int answer = -1)
    {
        if (seat < 0 || seat >= qsBubbles.Count) return;
        var b = qsBubbles[seat];
        b.Q<Label>().text = text;
        b.EnableInClassList("yes", answer == QuiSuisJe.Oui);
        b.EnableInClassList("no", answer == QuiSuisJe.Non);
        b.EnableInClassList("idk", answer == QuiSuisJe.NeSaitPas);
        b.style.display = DisplayStyle.Flex;
        qsBubbleUntil[seat] = Time.time + (answer >= 0 ? 3.5f : 30f);   // la question reste le temps du vote
    }

    void RefreshQuiSuisJe()
    {
        var q = game.qsj;
        if (q == null) return;
        if (qsTagEls.Count != q.players.Count)
        {
            qsTags.Clear(); qsTagEls.Clear(); qsBubbles.Clear(); qsBubbleUntil.Clear();
            for (int i = 0; i < q.players.Count; i++)
            {
                var tag = Div(qsTags, "qsj-tag"); tag.pickingMode = PickingMode.Ignore;
                tag.style.borderBottomColor = Board.Colors[i % Board.Colors.Length];
                Text(tag, q.players[i].name, "qsj-tag-name");
                Text(tag, "", "qsj-tag-score");
                qsTagEls.Add(tag);
                var bub = Div(qsTags, "qsj-bubble"); bub.pickingMode = PickingMode.Ignore;
                Text(bub, "");
                bub.style.display = DisplayStyle.None;
                qsBubbles.Add(bub); qsBubbleUntil.Add(0);
            }
        }
        for (int i = 0; i < q.players.Count; i++)
        {
            var p = q.players[i];
            qsTagEls[i].Q<Label>(className: "qsj-tag-score").text = q.phase == WPhase.Choose ? (q.players[q.TargetOf(i)].perso != null ? "a choisi ✔" : "choisit...")
                : $"{p.score} pts" + (p.Found ? " · trouvé !" : "");
            qsTagEls[i].EnableInClassList("active", i == q.turn && (q.phase == WPhase.Ask || q.phase == WPhase.Vote));
            qsTagEls[i].style.display = i == QsMe ? DisplayStyle.None : DisplayStyle.Flex;
            if (q.phase != WPhase.Vote && qsBubbleUntil.Count > i && qsBubbleUntil[i] > Time.time + 5) qsBubbleUntil[i] = Time.time + 3.5f;
        }
        var me = q.players[QsMe];
        bool spect = game.Spectating;
        bool myTurn = q.turn == QsMe && q.phase == WPhase.Ask && !spect;
        qsStatus.text = q.Finished ? "Fin de la partie !"
            : q.phase == WPhase.Choose ? "Chacun choisit le personnage de son voisin..."
            : $"Tour {Mathf.Min(q.round, q.maxRounds)} / {q.maxRounds}  ·  " + (myTurn ? (q.canAsk ? (q.free ? "À toi ! Pose une question (écrite ou à l'oral) ou propose un nom." : "À toi ! Choisis une question ou propose un nom.") : "Ce n'était pas « oui » : propose un nom ou passe la main.")
            : q.phase == WPhase.Vote ? $"{q.Current.name} attend vos votes..." : $"{q.Current.name} réfléchit...");
        // Mes indices.
        qsClues.Clear();
        Text(qsClues, me.Found ? $"Tu étais : {me.perso}" : "Qui suis-je ?", "qsj-clues-title");
        foreach (var (qq, a) in me.history.Skip(Mathf.Max(0, me.history.Count - 12)))
            Text(qsClues, $"{qq}  →  {QuiSuisJe.AnswerText(a)}", "qsj-clue", a == QuiSuisJe.Oui ? "yes" : a == QuiSuisJe.Non ? "no" : "idk");
        if (me.history.Count == 0 && !me.Found) Text(qsClues, "Pas encore d'indice.", "qsj-clue");
        // Choix du personnage du voisin.
        var target = q.players[q.TargetOf(QsMe)];
        bool choosing = q.phase == WPhase.Choose && !spect && target.perso == null;
        qsChoose.style.display = choosing ? DisplayStyle.Flex : DisplayStyle.None;
        if (choosing) qsChooseText.text = $"Choisis le personnage de {target.name} :\nil ne le verra pas, mais tous les autres oui !";
        qsChooseFree.style.display = q.free ? DisplayStyle.Flex : DisplayStyle.None;
        qsMoreProps.style.display = qsProps.style.display = q.free ? DisplayStyle.None : DisplayStyle.Flex;
        if (choosing && !q.free)
        {
            if (qsPropNames.Count == 0 || qsPropNames.Any(n => q.players.Any(p => p.perso == n)))
            {
                qsPropNames.Clear();
                qsPropNames.AddRange(QuiSuisJe.All.Where(p => q.players.All(x => x.perso != p.n)).OrderBy(_ => Random.value).Take(10).Select(p => p.n));
            }
            qsProps.Clear();
            foreach (var n in qsPropNames) { var nn = n; Btn(qsProps, nn, () => game.Act("pick|" + nn), "ghost", "small", "qsj-q"); }
        }
        // Mon tour.
        qsPanel.style.display = myTurn ? DisplayStyle.Flex : DisplayStyle.None;
        qsFreeRow.style.display = q.free ? DisplayStyle.Flex : DisplayStyle.None;
        qsTabs.style.display = qsGrid.style.display = q.free ? DisplayStyle.None : DisplayStyle.Flex;
        qsFreeRow.SetEnabled(q.canAsk);
        if (myTurn && !q.free)
        {
            foreach (var t in qsTabs.Query<Button>().ToList()) t.EnableInClassList("selected", t.text == qsGroup);
            qsGrid.Clear();
            var asked = new HashSet<string>(me.history.Select(h => h.q));
            foreach (var qu in QuiSuisJe.Questions.Where(x => x.group == qsGroup))
            {
                var id = qu.id;
                var b = Btn(qsGrid, qu.text, () => { if (game.CanAct) game.Act("ask|" + id); }, "ghost", "small", "qsj-q");
                b.SetEnabled(q.canAsk && !asked.Contains(qu.text));
            }
        }
        // Vote.
        bool voting = q.phase == WPhase.Vote && q.turn != QsMe && !spect && !q.votes.ContainsKey(QsMe);
        qsVote.style.display = voting ? DisplayStyle.Flex : DisplayStyle.None;
        if (voting) qsVoteText.text = (q.pending == QuiSuisJe.Oral ? $"{q.Current.name} a posé sa question à l'oral." : $"{q.Current.name} demande : « {q.pending} »")
            + $"\nSon personnage : {q.Current.perso}";
    }

    // Chaque image : etiquettes et bulles au-dessus des tetes.
    public void UpdateQuiSuisJe(Camera cam)
    {
        var q = game.qsj;
        if (q == null || qsTags.panel == null) return;
        for (int i = 0; i < qsTagEls.Count; i++)
        {
            var head = game.qsview.HeadOf(i);
            bool front = Vector3.Dot(cam.transform.forward, head - cam.transform.position) > 0;
            var sp = RuntimePanelUtils.CameraTransformWorldToPanel(qsTags.panel, head, cam);
            var t = qsTagEls[i];
            float w = float.IsNaN(t.resolvedStyle.width) ? 160 : t.resolvedStyle.width;
            t.style.left = sp.x - w / 2; t.style.top = sp.y - 70;
            t.style.visibility = front ? Visibility.Visible : Visibility.Hidden;
            var b = qsBubbles[i];
            if (b.style.display == DisplayStyle.Flex && Time.time > qsBubbleUntil[i]) b.style.display = DisplayStyle.None;
            float bw = float.IsNaN(b.resolvedStyle.width) ? 200 : b.resolvedStyle.width, bh = float.IsNaN(b.resolvedStyle.height) ? 60 : b.resolvedStyle.height;
            // Ma bulle (je ne me vois pas) : au milieu de l'ecran.
            if (i == QsMe) { b.style.left = (qsTags.resolvedStyle.width - bw) / 2; b.style.top = qsTags.resolvedStyle.height * 0.5f; }
            else { b.style.left = sp.x - bw / 2; b.style.top = sp.y - 80 - bh; }
            b.style.visibility = front || i == QsMe ? Visibility.Visible : Visibility.Hidden;
        }
    }
}
