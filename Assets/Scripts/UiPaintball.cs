using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Paintball : viseur au centre, score des deux equipes et chrono en haut, fil des touches a droite, et quand on est
// touche : l'ecran se tache de la peinture du tireur, avec le compte a rebours du retour en jeu.
public partial class Ui
{
    VisualElement pbHud, pbFeed, pbSplat, pbCross;
    Label pbScoreA, pbScoreB, pbTime, pbDown;
    readonly List<(Label l, float until)> pbFeedLines = new List<(Label, float)>();
    float pbSplatUntil;

    void BuildPaintballHud()
    {
        pbHud = Div(hud, "layer", "pb");
        pbHud.pickingMode = PickingMode.Ignore;
        pbSplat = Div(pbHud, "pb-splat"); pbSplat.pickingMode = PickingMode.Ignore;
        pbSplat.style.backgroundImage = PaintballView.SplatImage;
        pbCross = Div(pbHud, "pb-cross"); pbCross.pickingMode = PickingMode.Ignore;
        Div(pbCross, "pb-cross-dot").pickingMode = PickingMode.Ignore;
        var top = Div(pbHud, "pb-top"); top.pickingMode = PickingMode.Ignore;
        var row = Div(top, "row", "pb-score"); row.pickingMode = PickingMode.Ignore;
        pbScoreA = Text(row, "0", "pb-team"); pbScoreA.style.backgroundColor = PaintballView.TeamColor[0];
        pbTime = Text(row, "", "pb-time");
        pbScoreB = Text(row, "0", "pb-team"); pbScoreB.style.backgroundColor = PaintballView.TeamColor[1];
        pbFeed = Div(pbHud, "pb-feed"); pbFeed.pickingMode = PickingMode.Ignore;
        pbDown = Text(pbHud, "", "pb-down"); pbDown.pickingMode = PickingMode.Ignore;
    }

    void RefreshPaintball()
    {
        var p = game.pb;
        pbScoreA.text = $"{Paintball.TeamName[0].ToUpperInvariant()}  {p.score[0]}";
        pbScoreB.text = $"{p.score[1]}  {Paintball.TeamName[1].ToUpperInvariant()}";
    }

    // Chaque image : chrono, retour en jeu, taches qui s'effacent, fil des touches.
    public void UpdatePaintball()
    {
        if (game.pb == null || pbTime == null) return;
        int t = Mathf.CeilToInt(game.PaintballLeft);
        pbTime.text = $"{t / 60}:{t % 60:00}";
        var v = game.pbview;
        bool down = v.Down && !game.pb.Finished;
        pbCross.style.display = down || game.pb.Finished ? DisplayStyle.None : DisplayStyle.Flex;
        pbDown.style.display = down ? DisplayStyle.Flex : DisplayStyle.None;
        if (down)
        {
            string by = v.HitBy >= 0 ? game.pb.players[v.HitBy].name : "?";
            pbDown.text = $"Touché par {by} !\nRetour dans {Mathf.CeilToInt(v.RespawnIn)}";
        }
        float a = Mathf.Clamp01((pbSplatUntil - Time.time) / 1.2f);
        pbSplat.style.opacity = down ? Mathf.Max(a, 0.55f) : a;
        for (int i = pbFeedLines.Count - 1; i >= 0; i--)
            if (Time.time > pbFeedLines[i].until) { pbFeedLines[i].l.RemoveFromHierarchy(); pbFeedLines.RemoveAt(i); }
    }

    public void PaintballFeed(int by, int victim)
    {
        var p = game.pb;
        var l = Text(pbFeed, $"<color={PaintballView.Hex(Paintball.TeamOf(by))}>{p.players[by].name}</color>  ●  <color={PaintballView.Hex(Paintball.TeamOf(victim))}>{p.players[victim].name}</color>", "pb-feed-line");
        l.enableRichText = true;
        pbFeedLines.Add((l, Time.time + 5));
        while (pbFeedLines.Count > 5) { pbFeedLines[0].l.RemoveFromHierarchy(); pbFeedLines.RemoveAt(0); }
        if (victim == game.MySeatOrZero)
        {
            pbSplat.style.unityBackgroundImageTintColor = PaintballView.TeamColor[Paintball.TeamOf(by)];
            pbSplatUntil = Time.time + 3.5f;
        }
    }

    void PaintballVictory()
    {
        var p = game.pb;
        winTitle.text = p.winner < 0 ? "Égalité !" : $"L'équipe {Paintball.TeamName[p.winner]} gagne !";
        winTitle.style.color = p.winner < 0 ? Color.white : PaintballView.TeamColor[p.winner];
        winSub.text = $"{p.score[0]} - {p.score[1]}\n" + string.Join("\n", p.players.OrderByDescending(x => x.hits).ThenBy(x => x.outs)
            .Select((x, i) => $"{i + 1}.  {x.name} ({Paintball.TeamName[x.team]}) : {x.hits} touche{(x.hits > 1 ? "s" : "")}, {x.outs} fois touché"));
    }
}
