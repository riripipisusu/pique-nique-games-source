using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Limite Limite : les cartes sont sur la table (LimiteView). L'interface : la consigne, la question en clair en haut,
// les scores, la carte survolee en grand, et en bas (a mon tour) "ecrire ma reponse" et "Poser".
public partial class Ui
{
    VisualElement llHud, llHandBox, llScores, llPreview;
    Label llBlack, llStatus, llHint, llPreviewText;
    Button llSend;
    List<string> llSel => game.lview.selection;
    int llRevealed, llRevealRound = -1;
    IVisualElementScheduledItem llRevealTimer;

    int LlMe => game.ll == null ? 0 : Mathf.Clamp(game.mySeat, 0, game.ll.players.Count - 1);
    public bool LimiteRevealDone => game.ll != null && llRevealed >= game.ll.order.Count;

    void BuildLimiteHud()
    {
        llHud = Div(hud, "layer"); llHud.pickingMode = PickingMode.Ignore;
        llStatus = Text(llHud, "", "qsj-status", "ll-status"); llStatus.pickingMode = PickingMode.Ignore;
        var black = Div(llHud, "ll-black"); black.pickingMode = PickingMode.Ignore;
        llBlack = Text(black, "", "ll-black-text"); llBlack.pickingMode = PickingMode.Ignore;
        llScores = Div(llHud, "panel", "ll-scores"); llScores.pickingMode = PickingMode.Ignore;
        llPreview = Div(llHud, "ll-preview"); llPreview.pickingMode = PickingMode.Ignore;
        llPreviewText = Text(llPreview, "", "ll-preview-text"); llPreviewText.pickingMode = PickingMode.Ignore;
        llPreview.style.display = DisplayStyle.None;
        llHandBox = Div(llHud, "ll-handbox");
        llHint = Text(llHandBox, "", "ll-hint");
        llHint.style.display = DisplayStyle.None;   // la consigne du haut suffit ; rien ne doit cacher ma main
        var actions = Div(llHandBox, "row", "ll-actions");
        llSend = Btn(actions, "Poser", LlSend, "green");
    }

    // Carte survolee sur la table : en grand, pour la lire.
    public void LimitePreview(LimiteView.Card c)
    {
        if (c == null) { llPreview.style.display = DisplayStyle.None; return; }
        llPreview.style.display = DisplayStyle.Flex;
        llPreview.EnableInClassList("black", c.black);
        llPreviewText.text = c.black ? c.text.Replace("_", "______") : c.text;
    }

    // Clic sur une carte de ma main : choisie / reposee (dans l'ordre des trous).
    public void LimiteToggle(string card)
    {
        var l = game.ll;
        if (l == null) return;
        Sound.I.UI("tick");
        if (llSel.Contains(card)) llSel.Remove(card);
        else if (llSel.Count < l.Blanks) llSel.Add(card);
        else { llSel.RemoveAt(llSel.Count - 1); llSel.Add(card); }
        RefreshLimite();
    }

    void LlSend()
    {
        var l = game.ll;
        if (l == null || llSel.Count != l.Blanks || !game.CanAct) return;
        var play = string.Join(";", llSel);
        llSel.Clear();
        game.Act("play|" + play);   // le siege est ajoute par l'hote (ou hors ligne)
    }

    public void LimiteNewRound() { llSel.Clear(); llRevealed = 0; }

    string LlStatus()
    {
        var l = game.ll;
        int me = LlMe;
        bool iAmBoss = l.boss == me && !game.Spectating;
        return l.Finished ? "Fin de la partie !"
            : l.phase == LPhase.Play ? (iAmBoss ? $"Tu es le Boss ! Attends les réponses ({l.played.Count}/{l.players.Count - 1})  ·  {Mathf.CeilToInt(game.LimiteLeft)} s"
                : l.played.ContainsKey(me) ? $"Réponse posée ! On attend les autres ({l.played.Count}/{l.players.Count - 1})"
                : $"{l.Boss.name} est le Boss. Clique {(l.Blanks > 1 ? $"{l.Blanks} cartes de ta main (dans l'ordre des trous)" : "une carte de ta main")}  ·  {Mathf.CeilToInt(game.LimiteLeft)} s")
            : l.phase == LPhase.Judge ? (!LimiteRevealDone ? $"{l.Boss.name} découvre les réponses..."
                : iAmBoss ? (l.Blanks > 1 ? $"Clique la meilleure réponse pour le trou {l.PickStep + 1} !" : "Clique la meilleure réponse !")
                : l.Blanks > 1 ? $"{l.Boss.name} choisit pour le trou {l.PickStep + 1}..." : $"{l.Boss.name} choisit la meilleure réponse...")
            : string.Join(" et ", l.picks.Distinct().Select(s => l.players[s].name)) + (l.picks.Distinct().Count() > 1 ? " marquent un point !" : " remporte la manche !");
    }

    void RefreshLimite()
    {
        var l = game.ll;
        if (l == null) return;
        int me = LlMe;
        bool spect = game.Spectating, iAmBoss = l.boss == me && !spect;

        // Decouverte des reponses au centre : une toutes les 1,6 s.
        if (l.phase == LPhase.Judge && llRevealRound != l.round)
        {
            llRevealRound = l.round; llRevealed = 0;
            llRevealTimer?.Pause();
            llRevealTimer = llHud.schedule.Execute(() =>
            {
                if (game.ll == null || game.ll.phase != LPhase.Judge || llRevealed >= game.ll.order.Count) return;
                llRevealed++;
                Sound.I.Play("bj_flip", 0.8f);
                RefreshLimite();
            }).Every(1600).StartingIn(1400);   // le temps que les cartes arrivent au centre
        }
        if (l.phase == LPhase.Play) llRevealed = 0;

        // La question en clair, remplie avec la reponse gagnante (ou la derniere decouverte).
        string[] shown = null;
        // Trous deja choisis : la reponse choisie ; trou en cours : la derniere reponse decouverte.
        if (l.phase == LPhase.Result || l.phase == LPhase.Judge && l.picks.Count > 0)
        {
            var list = l.picks.Select((s, i) => l.played[s][i]).ToList();
            if (l.phase == LPhase.Judge && llRevealed > 0) list.Add(l.played[l.order[llRevealed - 1]][Mathf.Min(list.Count, l.Blanks - 1)]);
            shown = list.ToArray();
        }
        else if (l.phase == LPhase.Judge && llRevealed > 0) shown = l.played[l.order[llRevealed - 1]];
        if (shown != null) shown = shown.Select(x => x.StartsWith("*") ? x.Substring(1) : x).ToArray();
        llBlack.text = shown != null ? Limite.Fill(l.question, shown, "<color=#FF5A5A>", "</color>") : l.question.Replace("_", "______");

        // Scores.
        llScores.Clear();
        Text(llScores, $"Premier à {l.target} points", "rf-money-title");
        foreach (var p in l.players.OrderByDescending(p => p.score))
        {
            var row = Div(llScores, "row", "rf-money-row");
            Div(row, "rf-dot").style.backgroundColor = Board.Colors[p.seat % Board.Colors.Length];
            string tag = p.seat == l.boss ? "  ★ BOSS" : l.phase == LPhase.Play ? (l.played.ContainsKey(p.seat) ? "  ✔" : "  ...") : "";
            Text(row, $"{p.name} : {p.score} pt{(p.score > 1 ? "s" : "")}{tag}", "rf-money-line");
            if (l.picks.Contains(p.seat) && l.phase == LPhase.Result) row.AddToClassList("rf-active");
        }
        llStatus.text = LlStatus();

        // La table.
        bool canPlay = l.phase == LPhase.Play && !iAmBoss && !spect && !l.played.ContainsKey(me);
        if (!canPlay) llSel.Clear();
        llSel.RemoveAll(s => !l.players[me].hand.Contains(s));
        game.lview.revealed = llRevealed;
        game.lview.Sync();

        // Mon tour : ecrire sa reponse, poser.
        llHandBox.style.display = canPlay ? DisplayStyle.Flex : DisplayStyle.None;
        if (!canPlay) return;
        var picked = llSel.Select(s => $"« {s} »").ToList();
        llHint.text = (l.Blanks > 1 ? $"{l.Blanks} trous : clique {l.Blanks} cartes dans l'ordre." : "Clique une carte de ta main (survole pour la lire).")
            + (picked.Count > 0 ? "   Choix : " + string.Join("  puis  ", picked) : "");
        llSend.SetEnabled(llSel.Count == l.Blanks);
        llHandBox.style.visibility = llSel.Count == l.Blanks ? Visibility.Visible : Visibility.Hidden;   // la bulle n'apparait qu'une fois le choix complet
    }

    // Chaque image : seulement le chrono de la consigne (reconstruire ferait perdre le texte en cours de frappe).
    public void UpdateLimite()
    {
        if (game.ll == null) return;
        if (Time.frameCount % 20 == 0) llStatus.text = LlStatus();
        // Bulle "Poser" au-dessus de la derniere carte choisie (elle suit la carte, qui se souleve).
        if (llHandBox.style.display == DisplayStyle.Flex && llSel.Count > 0 && llHud.panel != null && Camera.main)
        {
            var c = game.lview.MineWith(llSel[llSel.Count - 1]);
            if (c)
            {
                var sp = RuntimePanelUtils.CameraTransformWorldToPanel(llHud.panel, c.transform.position + Vector3.up * 0.02f, Camera.main);
                float w = float.IsNaN(llHandBox.resolvedStyle.width) ? 200 : llHandBox.resolvedStyle.width;
                float h = float.IsNaN(llHandBox.resolvedStyle.height) ? 80 : llHandBox.resolvedStyle.height;
                llHandBox.style.left = sp.x - w / 2;
                llHandBox.style.top = sp.y - h - 55;
            }
        }
    }
}
