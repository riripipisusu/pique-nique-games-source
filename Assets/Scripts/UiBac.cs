using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Petit bac : la lettre et le chrono en haut ; pendant l'ecriture, une case par categorie et le bouton STOP ;
// pendant le vote, les mots de tout le monde (clic sur un mot d'un autre = le refuser) ; puis les points de la manche.
public partial class Ui
{
    VisualElement bacHud, bacWrite, bacVote, bacScores, bacFields;
    ScrollView bacVoteList;
    Label bacLetter, bacTimer, bacWriteTitle, bacVoteStatus, bacScoresTitle;
    Button bacStop, bacReady;
    readonly List<TextField> bacInputs = new List<TextField>();
    readonly List<string> bacSent = new List<string>();
    int bacBuiltRound = -1;
    int BacMe => game.bac == null ? 0 : Mathf.Clamp(game.mySeat, 0, game.bac.players.Count - 1);

    void BuildBacHud()
    {
        bacHud = Div(hud, "layer");
        bacHud.pickingMode = PickingMode.Ignore;
        var top = Div(bacHud, "bac-top");
        top.pickingMode = PickingMode.Ignore;
        bacLetter = Text(top, "", "bac-letter");
        bacTimer = Text(top, "", "bac-timer");

        bacWrite = Div(bacHud, "panel", "bac-panel");
        bacWriteTitle = Text(bacWrite, "", "h2");
        bacFields = Div(bacWrite, "bac-fields");
        bacStop = Btn(bacWrite, "STOP !", () => BacStop(), "red", "lg");
        bacStop.AddToClassList("bac-stop");

        bacVote = Div(bacHud, "panel", "bac-panel");
        Text(bacVote, "Vérifiez les mots ! Clique sur un mot pour le refuser.", "h2");
        bacVoteList = Add(bacVote, new ScrollView(), "bac-vote-list");
        var vr = Div(bacVote, "row", "spread");
        bacVoteStatus = Text(vr, "", "muted");
        bacReady = Ico(Btn(vr, "J'ai fini de voter", () => game.BacAct("ready"), "green"), "check");

        bacScores = Div(bacHud, "panel", "bac-panel");
        bacScoresTitle = Text(bacScores, "", "h2");
    }

    // Nouvelle manche (ou affichage du HUD) : une case par categorie, vide.
    public void BacRound()
    {
        var b = game.bac;
        if (b == null || bacFields == null || b.round == bacBuiltRound) return;
        bacBuiltRound = b.round;
        bacFields.Clear(); bacInputs.Clear(); bacSent.Clear();
        for (int c = 0; c < b.CatCount; c++)
        {
            int cat = c;
            var row = Div(bacFields, "bac-row");
            Text(row, b.categories[c], "bac-cat");
            var f = Add(row, new TextField { maxLength = 40 }, "bac-field");
            bacInputs.Add(f); bacSent.Add("");
            int version = 0;
            // Envoi 0,35 s apres la derniere frappe (et tout de suite avec Entree, qui passe a la case suivante).
            f.RegisterValueChangedCallback(e => { int v = ++version; f.schedule.Execute(() => { if (v == version) BacSend(cat); }).StartingIn(350); BacStopState(); });
            f.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter && e.keyCode != KeyCode.Tab) return;
                BacSend(cat);
                if (cat + 1 < bacInputs.Count) { var next = bacInputs[cat + 1]; next.schedule.Execute(() => next.Focus()).StartingIn(10); }
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
        }
        if (bacInputs.Count > 0) bacInputs[0].schedule.Execute(() => bacInputs[0].Focus()).StartingIn(50);
        BacStopState();
    }

    void BacSend(int cat)
    {
        if (cat >= bacInputs.Count) return;
        var t = bacInputs[cat].value.Trim().Replace("|", "");
        if (t == bacSent[cat] || game.bac == null || game.bac.phase != BPhase.Write) return;
        bacSent[cat] = t;
        game.BacAct($"ans|{cat}|{t}");
    }

    // STOP : seulement quand toutes mes cases commencent par la bonne lettre.
    bool BacAllFilled => game.bac != null && bacInputs.Count > 0 && bacInputs.All(f => game.bac.RightLetter(f.value));
    void BacStopState() { if (bacStop != null) bacStop.SetEnabled(BacAllFilled && game.bac.phase == BPhase.Write); }
    void BacStop()
    {
        if (!BacAllFilled) return;
        for (int c = 0; c < bacInputs.Count; c++) BacSend(c);   // mes derniers mots partent avant le STOP
        game.BacAct("stop");
    }

    // Vote : les mots de chacun, categorie par categorie ; mes refus en rouge, les refus des autres comptes.
    public void BacVote()
    {
        var b = game.bac;
        if (b == null || bacVoteList == null || b.phase != BPhase.Vote) return;
        var keep = bacVoteList.scrollOffset;
        bacVoteList.Clear();
        int me = BacMe;
        for (int c = 0; c < b.CatCount; c++)
        {
            Text(bacVoteList, b.categories[c], "bac-vote-cat");
            var row = Div(bacVoteList, "row", "bac-vote-row");
            for (int s = 0; s < b.players.Count; s++)
            {
                int seat = s, cat = c;
                var ans = b.answers[s, c];
                bool right = b.RightLetter(ans);
                bool mineRejected = b.rejects[s, c] != null && b.rejects[s, c].Contains(me);
                int refusals = b.rejects[s, c]?.Count ?? 0;
                var chip = new Button(() => { if (seat != me && right) { Sound.I.UI("tick"); game.BacAct($"vote|{seat}|{cat}|{(mineRejected ? 0 : 1)}"); } });
                chip.AddToClassList("bac-word");
                chip.EnableInClassList("empty", string.IsNullOrWhiteSpace(ans));
                chip.EnableInClassList("wrong", !string.IsNullOrWhiteSpace(ans) && !right);
                chip.EnableInClassList("refused", right && b.Rejected(seat, cat));
                chip.EnableInClassList("mine-no", mineRejected);
                chip.EnableInClassList("self", seat == me);
                var who = Text(chip, b.players[s].name, "bac-word-who");
                who.style.color = Board.Colors[s % Board.Colors.Length];
                Text(chip, string.IsNullOrWhiteSpace(ans) ? "—" : ans, "bac-word-text");
                if (refusals > 0) Text(chip, $"✗ {refusals}", "bac-word-no");
                row.Add(chip);
            }
        }
        bacVoteList.schedule.Execute(() => bacVoteList.scrollOffset = keep).StartingIn(1);
        RefreshBac();
    }

    // Fin de manche : les points de chacun, et le detail de ses mots.
    public void BacScores()
    {
        var b = game.bac;
        if (b == null || bacScores == null) return;
        while (bacScores.childCount > 1) bacScores.RemoveAt(1);
        bacScoresTitle.text = b.Finished ? "Dernière manche !" : $"Manche {b.round} / {b.rounds} : les points";
        foreach (var p in b.players.OrderByDescending(p => p.gained).ThenByDescending(p => p.score))
        {
            var row = Div(bacScores, "bac-score-row");
            var name = Text(row, p.name, "bac-score-name");
            name.style.color = Board.Colors[p.seat % Board.Colors.Length];
            Text(row, $"+{p.gained}", "bac-score-gain");
            Text(row, $"{p.score} pts", "bac-score-total");
            var words = Enumerable.Range(0, b.CatCount).Select(c => string.IsNullOrWhiteSpace(b.answers[p.seat, c]) ? null : $"{b.answers[p.seat, c]} ({b.points[p.seat, c]})").Where(w => w != null);
            Text(bacScores, string.Join("  ·  ", words), "bac-score-words");
        }
    }

    void RefreshBac()
    {
        var b = game.bac;
        if (b == null) return;
        bacWrite.style.display = b.phase == BPhase.Write ? DisplayStyle.Flex : DisplayStyle.None;
        bacVote.style.display = b.phase == BPhase.Vote ? DisplayStyle.Flex : DisplayStyle.None;
        bacScores.style.display = b.phase == BPhase.Scores || b.phase == BPhase.GameOver && bacScores.childCount > 1 ? DisplayStyle.Flex : DisplayStyle.None;
        bacWriteTitle.text = $"Manche {b.round} / {b.rounds}  ·  Lettre {b.letter}";
        foreach (var f in bacInputs) f.SetEnabled(b.phase == BPhase.Write && !game.Spectating);
        BacStopState();
        int readyN = b.ready.Count;
        bacVoteStatus.text = $"{readyN} / {b.players.Count} ont fini de voter";
        bacReady.SetEnabled(!b.ready.Contains(BacMe) && !game.Spectating);
    }

    // Chaque image : la lettre et le chrono.
    public void UpdateBac()
    {
        var b = game.bac;
        if (b == null || bacLetter == null) return;
        bacLetter.text = b.round > 0 ? b.letter.ToString() : "?";
        float left = game.BacTimeLeft;
        bacTimer.text = b.phase == BPhase.Write || b.phase == BPhase.Vote ? Mathf.CeilToInt(Mathf.Max(0, left)).ToString() : "";
        bacTimer.EnableInClassList("hurry", b.phase == BPhase.Write && left < 10);
    }
}
