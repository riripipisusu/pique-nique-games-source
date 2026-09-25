using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// HUD du quiz : categorie et chrono en haut, champ de reponse en bas, noms et scores au-dessus des pupitres,
// mauvaises reponses des autres en bulles et dans un fil, classement vers 100 points, revelation de la reponse.
public partial class Ui
{
    VisualElement qzHud, qzTags, qzBar, qzBarFill, qzBoard, qzFeed, qzReveal;
    Label qzCategory, qzTimer, qzStatus, qzAnswer, qzCredit;
    TextField qzInput;
    VisualElement qzChoices;
    readonly List<Button> qzChoiceBtns = new List<Button>();

    void BuildQuizHud()
    {
        qzHud = Div(hud, "layer");
        qzHud.pickingMode = PickingMode.Ignore;
        qzTags = Div(qzHud, "layer");
        qzTags.pickingMode = PickingMode.Ignore;

        var top = Div(qzHud, "qz-top");
        top.pickingMode = PickingMode.Ignore;
        qzCategory = Text(top, "", "qz-category");
        qzBar = Div(top, "qz-bar");
        qzBarFill = Div(qzBar, "qz-bar-fill");
        qzTimer = Text(top, "", "qz-timer");

        qzBoard = Div(qzHud, "panel", "qz-board");
        qzBoard.style.display = DisplayStyle.None;   // scores affiches sur les pupitres
        qzFeed = Div(qzHud, "qz-feed");
        qzFeed.pickingMode = PickingMode.Ignore;

        var bottom = Div(qzHud, "qz-bottom");
        qzStatus = Text(bottom, "", "qz-status");
        qzInput = Add(bottom, new TextField { maxLength = 60 }, "qz-input");
        qzInput.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
            var t = qzInput.value.Trim().Replace("|", "");
            if (t.Length > 0 && game.MyTurn) game.Act("guess|" + t);
            qzInput.value = "";
            qzInput.schedule.Execute(() => qzInput.Focus()).StartingIn(10);
        }, TrickleDown.TrickleDown);

        // QCM : 4 boutons de reponse a la place du champ de texte.
        qzChoices = Div(bottom, "qz-choices");
        for (int k = 0; k < 4; k++)
        {
            int idx = k;
            var b = Btn(qzChoices, "", () => { if (game.qz != null && game.MyTurn) game.Act("guess|" + game.qz.Current.p[idx]); }, "qz-choice", "choice-" + k);
            qzChoiceBtns.Add(b);
        }

        qzReveal = Div(qzHud, "panel", "qz-reveal");
        qzReveal.pickingMode = PickingMode.Ignore;
        Text(qzReveal, "C'était :", "qz-reveal-title");
        qzAnswer = Text(qzReveal, "", "qz-answer");
        qzCredit = Text(qzReveal, "", "qz-credit");
    }

    int Me => Mathf.Max(0, game.mySeat);

    // Generique TV Time en plein ecran (image a ses proportions sur fond noir).
    // Clic, Espace ou Entree : "Passer l'introduction ?" (video en pause) ; Oui la coupe, Non ou Echap la reprend.
    VisualElement intro, introAsk;
    public bool IntroShown => intro != null;
    public bool IntroAsking => introAsk != null && introAsk.style.display == DisplayStyle.Flex;
    public void ShowIntro(RenderTexture rt)
    {
        intro = Div(root, "intro");
        var img = Div(intro, "intro-video");
        img.style.backgroundImage = Background.FromRenderTexture(rt);
        Text(intro, "Clic ou Espace pour passer", "intro-skip");
        introAsk = Div(intro, "panel", "intro-ask");
        Text(introAsk, "Passer l'introduction ?", "panel-title");
        var row = Div(introAsk, "row");
        Btn(row, "Non", () => AskSkip(false), "ghost").style.width = 220;
        Btn(row, "Oui, passer", () => { game.introSkip = true; }, "green").style.width = 260;
        introAsk.style.display = DisplayStyle.None;
        img.RegisterCallback<PointerDownEvent>(_ => AskSkip(true));
    }
    void AskSkip(bool ask) { if (introAsk != null) introAsk.style.display = ask ? DisplayStyle.Flex : DisplayStyle.None; }
    public void IntroKey(bool escape)
    {
        if (!IntroAsking) { if (!escape) AskSkip(true); }
        else if (escape) AskSkip(false);
        else game.introSkip = true;
    }
    // Boite de dialogue de Tenna : texte lettre par lettre, un "bip" toutes les deux lettres (voix Deltarune).
    // Clic / Espace / Entree : finit la phrase, puis passe a la suivante. Echap ou "Passer" : confirmation.
    public class Dialogue
    {
        public Label text, hint;
        public VisualElement ask;
        public bool next, skip, typing;
        public IEnumerator Type(string line)
        {
            typing = true;
            text.text = "";
            for (int i = 1; i <= line.Length && typing && !skip; i++)
            {
                while (ask.style.display == DisplayStyle.Flex && !skip) yield return null;   // en pause pendant la question
                text.text = line.Substring(0, i);
                char c = line[i - 1];
                if (char.IsLetterOrDigit(c)) Sound.I.Voice("tenna_voice_" + UnityEngine.Random.Range(1, 11), 0.7f);   // syllabe au hasard, seulement si la precedente est finie
                yield return new WaitForSeconds(c == '.' || c == '!' || c == '?' ? 0.16f : c == ',' ? 0.08f : 0.028f);
            }
            text.text = line;
            typing = false;
            hint.text = "▼";
        }
    }
    Dialogue dialogue;
    VisualElement dialogueRoot;
    public bool DialogueShown => dialogueRoot != null;

    public Dialogue ShowDialogue()
    {
        dialogueRoot = Div(root, "dlg-layer");
        var d = new Dialogue();
        var box = Div(dialogueRoot, "dlg-box");
        Text(box, "TENNA", "dlg-name");
        d.text = Text(box, "", "dlg-text");
        d.hint = Text(box, "", "dlg-hint");
        var skipBtn = Btn(dialogueRoot, "Passer les règles", () => d.ask.style.display = DisplayStyle.Flex, "ghost", "small", "dlg-skip");
        d.ask = Div(dialogueRoot, "panel", "intro-ask");
        Text(d.ask, "Passer les règles ?", "panel-title");
        var row = Div(d.ask, "row");
        Btn(row, "Non", () => d.ask.style.display = DisplayStyle.None, "ghost").style.width = 220;
        Btn(row, "Oui, passer", () => d.skip = true, "green").style.width = 260;
        d.ask.style.display = DisplayStyle.None;
        box.RegisterCallback<PointerDownEvent>(_ => DialogueKey(false));
        dialogue = d;
        return d;
    }

    public void DialogueKey(bool escape)
    {
        var d = dialogue;
        if (d == null) return;
        bool asking = d.ask.style.display == DisplayStyle.Flex;
        if (escape) { d.ask.style.display = asking ? DisplayStyle.None : DisplayStyle.Flex; return; }
        if (asking) { d.skip = true; return; }
        if (d.typing) d.typing = false;
        else { d.next = true; d.hint.text = ""; }
    }

    public void HideDialogue() { dialogueRoot?.RemoveFromHierarchy(); dialogueRoot = null; dialogue = null; }

    public void HideIntro() { intro?.RemoveFromHierarchy(); intro = null; introAsk = null; }

    public void QuizQuestion()
    {
        var q = game.qz;
        qzCategory.text = q.trivia ? $"{q.Current.c}  ·  question {q.round}" : $"{Quiz.CategoryName(q.Current.c)}  ·  image {q.round}";
        qzInput.style.display = q.Mcq ? DisplayStyle.None : DisplayStyle.Flex;
        qzChoices.style.display = q.Mcq ? DisplayStyle.Flex : DisplayStyle.None;
        for (int k = 0; k < 4 && q.Mcq; k++)
        {
            var b = qzChoiceBtns[k];
            b.text = $"{k + 1}.  {q.Current.p[k]}";
            b.SetEnabled(true);
            b.RemoveFromClassList("good"); b.RemoveFromClassList("bad"); b.RemoveFromClassList("picked");
        }
        qzReveal.style.display = DisplayStyle.None;
        qzFeed.Clear();
        qzInput.SetEnabled(true);
        qzInput.value = "";
        qzInput.schedule.Execute(() => qzInput.Focus()).StartingIn(30);
    }

    public void QuizFound(int seat, int points)
    {
        var p = game.qz.players[seat];
        var line = Text(qzFeed, seat == Me ? $"Bien joué ! +{points}" : $"{p.name} a trouvé ! +{points}", "qz-feed-line", "found");
        line.style.color = Board.Colors[seat % Board.Colors.Length];
        if (seat == Me) { qzInput.SetEnabled(false); qzInput.value = ""; LockChoices(true); }
        game.qview.Strip(seat, $"Trouvé ! +{points}", Board.Hex("1c7a3c"));
    }

    public void QuizWrong(int seat, string text)
    {
        var p = game.qz.players[seat];
        if (seat == Me && game.qz.Mcq) LockChoices(false);
        var line = Text(qzFeed, game.qz.Mcq ? (seat == Me ? "Raté !" : $"{p.name} s'est trompé !") : $"{p.name} : {text}", "qz-feed-line");
        line.style.color = Board.Colors[seat % Board.Colors.Length];
        while (qzFeed.childCount > 8) qzFeed.RemoveAt(0);
        game.qview.Strip(seat, text, Board.Hex("b3262b"));
    }

    // QCM : mes boutons se figent apres ma reponse (vert si juste, rouge si faux).
    void LockChoices(bool good)
    {
        foreach (var b in qzChoiceBtns) b.SetEnabled(false);
    }

    public void QuizReveal()
    {
        var q = game.qz.Current;
        qzAnswer.text = q.d;
        if (game.qz.Mcq)
            for (int k = 0; k < 4; k++) qzChoiceBtns[k].EnableInClassList("good", q.p[k] == q.d);
        qzCredit.text = string.Join("\n", new[] { q.h, q.cr }.Where(x => !string.IsNullOrEmpty(x)));
        qzReveal.style.display = DisplayStyle.Flex;
        qzInput.SetEnabled(false);
        Sound.I.Play(game.qz.players[Me].found ? "bj_chips" : "lose", 0.6f);
    }

    void RefreshQuiz()
    {
        var q = game.qz;
        qzBoard.Clear();
        Text(qzBoard, $"Premier à {Quiz.Target}", "h2").style.marginTop = 0;
        foreach (var p in q.players.OrderByDescending(p => p.score))
        {
            var row = Div(qzBoard, "qz-row");
            var por = Portrait(row, Avatar(p.seat), null, 40);
            var c = Board.Colors[p.seat % Board.Colors.Length];
            por.style.borderTopColor = por.style.borderBottomColor = por.style.borderLeftColor = por.style.borderRightColor = c;
            var col = Div(row, "qz-col");
            Text(col, p.name + (p.found && q.phase == QPhase.Guess ? "  ✔" : ""), "seat-name");
            var bar = Div(col, "qz-score-bar");
            var fill = Div(bar, "qz-score-fill");
            fill.style.width = Length.Percent(Mathf.Clamp01(p.score / (float)Quiz.Target) * 100);
            fill.style.backgroundColor = c;
            Text(row, p.score.ToString(), "qz-score");
        }
        bool guessing = q.phase == QPhase.Guess;
        qzStatus.text = q.Finished ? "" : !guessing ? (q.round == 0 ? "La partie commence..." : q.trivia ? "Prochaine question..." : "Prochaine image...")
            : q.players[Me].found ? "Trouvé ! Attends les autres..." : q.players[Me].locked ? "Raté... attends la prochaine question !"
            : q.Mcq ? "Choisis ta réponse (clic ou touches 1 à 4)" : "Tape ta réponse puis Entrée";
    }

    // Chaque image : chrono, et etiquettes (nom, score, bulle) suivant les pupitres a l'ecran.
    public void UpdateQuiz(Camera cam)
    {
        var q = game.qz;
        if (q == null || qzTags.panel == null) return;
        bool guessing = q.phase == QPhase.Guess;
        float left = guessing ? Mathf.Max(0, Quiz.RoundMs / 1000f - game.QuizElapsedMs / 1000f) : 0;
        qzBar.style.display = qzTimer.style.display = guessing ? DisplayStyle.Flex : DisplayStyle.None;
        qzBarFill.style.width = Length.Percent(left / (Quiz.RoundMs / 1000f) * 100);
        qzBarFill.EnableInClassList("hurry", left < 5);
        qzTimer.text = Mathf.CeilToInt(left).ToString();
        if (q.round == 0) qzCategory.text = q.trivia ? "Le grand quiz de Tenna" : "Quiz d'images";

    }
}
