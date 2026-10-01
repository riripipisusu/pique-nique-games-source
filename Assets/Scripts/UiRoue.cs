using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Roue de la fortune : consigne et chrono en haut, gains a droite (banque + manche), et en bas selon l'etape :
// champ "buzz" (enigme rapide), actions de mon tour (tourner, voyelle, solution), clavier des consonnes, choix de la
// finale (3 consonnes + 1 voyelle), reponse a l'enigme a double sens ou a la finale.
public partial class Ui
{
    VisualElement rfHud, rfPanel, rfActions, rfKeys, rfMoney;
    Label rfStatus, rfHint, rfHostText;
    VisualElement rfHost;
    Coroutine rfHostTyping;
    TextField rfText;
    Button rfSpin, rfVowel, rfSend;
    bool rfVowelMode;
    readonly List<char> rfPick = new List<char>();
    readonly Dictionary<char, Button> rfKeyBtns = new Dictionary<char, Button>();

    int RfMe => game.roue == null ? 0 : Mathf.Clamp(game.mySeat, 0, game.roue.players.Count - 1);

    // Replique de Tenna : bulle en haut, tapee lettre par lettre avec sa voix. Renvoie la duree de lecture.
    public float HostLine(string line)
    {
        if (rfHostTyping != null) StopCoroutine(rfHostTyping);
        rfHostTyping = StartCoroutine(TypeHost(line));
        return line.Length * 0.03f + 1.6f;
    }
    System.Collections.IEnumerator TypeHost(string line)
    {
        rfHost.style.display = DisplayStyle.Flex;
        for (int i = 1; i <= line.Length; i++)
        {
            rfHostText.text = line.Substring(0, i);
            char c = line[i - 1];
            if (char.IsLetterOrDigit(c) && i % 2 == 0) Sound.I.Voice("tenna_voice_" + Random.Range(1, 11), 0.6f);
            yield return new WaitForSeconds(c == '.' || c == '!' || c == '?' ? 0.16f : c == ',' ? 0.08f : 0.028f);
        }
        yield return new WaitForSeconds(2.2f);
        rfHost.style.display = DisplayStyle.None;
        rfHostTyping = null;
        RefreshRoue();   // la bulle est fermee : le panneau du bas peut apparaitre
    }

    public bool TypingText
    {
        get
        {
            var f = root?.panel?.focusController?.focusedElement as VisualElement;
            return f != null && (f is TextField || f.GetFirstAncestorOfType<TextField>() != null);
        }
    }

    void BuildRoueHud()
    {
        rfHud = Div(hud, "layer"); rfHud.pickingMode = PickingMode.Ignore;
        rfHost = Div(rfHud, "dlg-box", "rf-host"); rfHost.pickingMode = PickingMode.Ignore;
        Text(rfHost, "TENNA", "dlg-name").pickingMode = PickingMode.Ignore;
        rfHostText = Text(rfHost, "", "dlg-text"); rfHostText.pickingMode = PickingMode.Ignore;
        rfHost.style.display = DisplayStyle.None;
        rfStatus = Text(rfHud, "", "qsj-status"); rfStatus.pickingMode = PickingMode.Ignore;
        rfMoney = Div(rfHud, "panel", "rf-money"); rfMoney.pickingMode = PickingMode.Ignore;
        rfPanel = Div(rfHud, "panel", "rf-panel");
        rfHint = Text(rfPanel, "", "rf-hint");
        rfActions = Div(rfPanel, "row", "qsj-row");
        rfSpin = Btn(rfActions, "Tourner la roue", () => { if (game.CanAct) game.Act(game.roue?.phase == FPhase.FinalSpin ? "fspin" : "spin"); }, "green");
        rfVowel = Btn(rfActions, "Acheter une voyelle (200 €)", () => { rfVowelMode = !rfVowelMode; RefreshRoue(); }, "ghost");
        var row = Div(rfPanel, "row", "qsj-row");
        rfText = Add(row, new TextField { maxLength = 60 }, "qsj-input");
        rfText.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) SendRoueText(); }, TrickleDown.TrickleDown);
        rfSend = Btn(row, "Proposer", SendRoueText, "green");
        rfKeys = Div(rfPanel, "rf-keys");
        foreach (char c in "AEIOUY" + Roue.Consonants)
        {
            char k = c;
            var b = Btn(rfKeys, c.ToString(), () => RoueKey(k), "ghost", "small", "rf-key");
            if (Roue.Vowels.IndexOf(c) >= 0) b.AddToClassList("rf-vowel");
            rfKeyBtns[c] = b;
        }
    }

    void SendRoueText()
    {
        var r = game.roue;
        var t = rfText.value.Trim().Replace("|", "");
        if (r == null || t.Length == 0) return;
        string act = r.phase == FPhase.Rapid ? "buzz" : r.phase == FPhase.Bonus ? "bonus" : r.phase == FPhase.FinalSolve ? "fsolve" : "solve";
        if (!game.CanAct) return;
        game.Act(act + "|" + t);
        rfText.value = "";
        rfText.schedule.Execute(() => rfText.Focus()).StartingIn(20);
    }

    void RoueKey(char c)
    {
        var r = game.roue;
        if (r == null || !game.CanAct) return;
        Sound.I.UI("tick");
        if (r.phase == FPhase.Letter) game.Act("cons|" + c);
        else if (r.phase == FPhase.Spin && rfVowelMode) { rfVowelMode = false; game.Act("vowel|" + c); }
        else if (r.phase == FPhase.FinalPick)
        {
            if (rfPick.Contains(c)) rfPick.Remove(c);
            else if (Roue.Vowels.IndexOf(c) >= 0) { rfPick.RemoveAll(x => Roue.Vowels.IndexOf(x) >= 0); rfPick.Add(c); }
            else if (rfPick.Count(x => Roue.Consonants.IndexOf(x) >= 0) < 3) rfPick.Add(c);
            if (rfPick.Count(x => Roue.Consonants.IndexOf(x) >= 0) == 3 && rfPick.Any(x => Roue.Vowels.IndexOf(x) >= 0))
            {
                game.Act("fpick|" + new string(rfPick.Where(x => Roue.Consonants.IndexOf(x) >= 0).Concat(rfPick.Where(x => Roue.Vowels.IndexOf(x) >= 0)).ToArray()));
                rfPick.Clear();
            }
            RefreshRoue();
        }
    }

    void RefreshRoue()
    {
        var r = game.roue;
        if (r == null) return;
        int me = RfMe;
        bool spect = game.Spectating, mine = r.Actor == me && !spect;
        // Gains.
        rfMoney.Clear();
        Text(rfMoney, r.InFinal ? "Finale" : $"Manche {Mathf.Max(1, r.round)} / {r.rounds}", "rf-money-title");
        foreach (var p in r.players.OrderByDescending(p => p.score))
        {
            var row = Div(rfMoney, "row", "rf-money-row");
            Div(row, "rf-dot").style.backgroundColor = Board.Colors[p.seat % Board.Colors.Length];
            var line = Text(row, $"{p.name} : {p.score} €" + (!r.InFinal && r.roundMoney[p.seat] > 0 ? $"  (+{r.roundMoney[p.seat]} €)" : ""), "rf-money-line");
            if (p.seat == r.turn && (r.phase == FPhase.Spin || r.phase == FPhase.Letter)) row.AddToClassList("rf-active");
        }
        // Ce que font les autres (propositions, lettres...), une fois l'action jouee a l'ecran.
        Feed(r.log.Take(Mathf.Min(game.rfLogShown, r.log.Count)).ToList());
        // Consigne.
        string who = r.phase == FPhase.Intro ? "" : r.players[r.Actor >= 0 ? r.Actor : r.turn].name;
        rfStatus.text = r.phase switch
        {
            FPhase.Intro => "La Roue de la fortune !",
            FPhase.Rapid => "Énigme rapide : le premier qui trouve gagne 500 € et prend la main !",
            FPhase.Spin => mine ? "À toi ! Tourne la roue, achète une voyelle ou propose la solution." : $"Au tour de {who}",
            FPhase.Letter => mine ? $"{Roue.SegmentText(r.value)} ! Choisis une consonne (clic ou clavier)." : $"{who} choisit une consonne ({Roue.SegmentText(r.value)})",
            FPhase.Bonus => $"Énigme à double sens : {who} a {Mathf.CeilToInt(game.RoueLeft(Roue.BonusMs))} s pour répondre (+500 €)",
            FPhase.RoundEnd => "Fin de la manche !",
            FPhase.FinalSpin => mine ? "FINALE ! Tourne la roue des enveloppes." : $"FINALE de {who} : la roue des enveloppes",
            FPhase.FinalPick => mine ? "Choisis 3 consonnes et 1 voyelle." : $"{who} choisit 3 consonnes et 1 voyelle",
            FPhase.FinalSolve => $"{(mine ? "À toi" : who)} : {Mathf.CeilToInt(game.RoueLeft(Roue.FinalMs))} s pour trouver !",
            FPhase.FinalEnd => "Fin de la finale !",
            _ => "Fin de la partie !",
        };
        if (game.rfSpinCam) rfStatus.text = $"{who} fait tourner la roue...";   // le resultat seulement quand elle s'arrete
        // Panneau du bas.
        bool rapid = r.phase == FPhase.Rapid && !spect && !r.outs.Contains(me);
        bool quiet = !game.busy && !game.tvCloseUp && rfHostTyping == null && rfHost.style.display == DisplayStyle.None;
        // L'enigme rapide : le champ reste affiche pendant que les cases s'allument (sinon on perd ce qu'on tape).
        bool show = rapid && !game.tvCloseUp || quiet && (mine && (r.phase == FPhase.Spin || r.phase == FPhase.Letter || r.phase == FPhase.Bonus || r.phase >= FPhase.FinalSpin && r.phase <= FPhase.FinalSolve));
        rfPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (!show) { rfVowelMode = false; return; }
        rfActions.style.display = r.phase == FPhase.Spin || r.phase == FPhase.FinalSpin ? DisplayStyle.Flex : DisplayStyle.None;
        rfSpin.text = r.phase == FPhase.FinalSpin ? "Tourner la roue des enveloppes" : "Tourner la roue";
        rfSpin.SetEnabled(r.phase == FPhase.FinalSpin ? true : r.CanSpin);
        rfVowel.style.display = r.phase == FPhase.Spin ? DisplayStyle.Flex : DisplayStyle.None;
        rfVowel.SetEnabled(r.CanBuyVowel);
        rfVowel.text = rfVowelMode ? "Choisis la voyelle..." : "Acheter une voyelle (200 €)";
        bool text = r.phase == FPhase.Rapid || r.phase == FPhase.Spin || r.phase == FPhase.Bonus || r.phase == FPhase.FinalSolve;
        rfText.parent.style.display = text ? DisplayStyle.Flex : DisplayStyle.None;
        rfText.textEdition.placeholder = r.phase == FPhase.Rapid ? "Je sais ! (tape l'énigme)" : r.phase == FPhase.Bonus ? "La réponse..." : "La solution de l'énigme...";
        rfSend.text = r.phase == FPhase.Rapid ? "Buzzer" : "Proposer";
        bool keys = r.phase == FPhase.Letter || r.phase == FPhase.FinalPick || r.phase == FPhase.Spin && rfVowelMode;
        rfKeys.style.display = keys ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (var kv in rfKeyBtns)
        {
            bool vowel = Roue.Vowels.IndexOf(kv.Key) >= 0;
            bool visible = r.phase == FPhase.FinalPick || (r.phase == FPhase.Letter ? !vowel : vowel);
            kv.Value.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            kv.Value.SetEnabled(!r.used.Contains(kv.Key));
            kv.Value.EnableInClassList("selected", rfPick.Contains(kv.Key));
        }
        rfHint.text = r.phase == FPhase.FinalPick ? $"Choisis : {string.Join(" ", rfPick)} ({rfPick.Count(x => Roue.Consonants.IndexOf(x) >= 0)}/3 consonnes, {rfPick.Count(x => Roue.Vowels.IndexOf(x) >= 0)}/1 voyelle)"
            : r.phase == FPhase.Rapid ? "Le premier qui tape la bonne énigme gagne 500 €. Attention : une erreur et tu es éliminé de l'énigme rapide !" : "";
        rfHint.style.display = rfHint.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // Chaque image : les chronos (double sens, finale).
    public void UpdateRoue()
    {
        var r = game.roue;
        if (r == null) return;
        if (r.phase == FPhase.Bonus || r.phase == FPhase.FinalSolve) RefreshRoueStatusOnly();
    }
    void RefreshRoueStatusOnly()
    {
        var r = game.roue;
        string who = r.players[r.Actor >= 0 ? r.Actor : r.turn].name;
        bool mine = r.Actor == RfMe && !game.Spectating;
        rfStatus.text = r.phase == FPhase.Bonus ? $"Énigme à double sens : {who} a {Mathf.CeilToInt(game.RoueLeft(Roue.BonusMs))} s pour répondre (+500 €)"
            : $"{(mine ? "À toi" : who)} : {Mathf.CeilToInt(game.RoueLeft(Roue.FinalMs))} s pour trouver !";
    }
}
