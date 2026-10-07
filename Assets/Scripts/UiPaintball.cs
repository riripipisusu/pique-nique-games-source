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

    VisualElement pbPick, pbPickCols, pbPickFoot, pbTab;
    Label pbPickTime;

    void BuildPaintballHud()
    {
        pbHud = Div(hud, "layer", "pb");
        pbHud.pickingMode = PickingMode.Ignore;
        pbPick = Div(pbHud, "pb-pick");
        Text(pbPick, "CHOISIS TON ÉQUIPE", "pb-pick-title");
        pbPickCols = Div(pbPick, "row", "pb-pick-cols");
        pbPickFoot = Div(pbPick, "row", "pb-pick-foot");
        pbPickTime = Text(pbPick, "", "pb-pick-time");
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
        pbTab = Div(pbHud, "pb-tab"); pbTab.pickingMode = PickingMode.Ignore; pbTab.style.display = DisplayStyle.None;
        pbDown = Text(pbHud, "", "pb-down"); pbDown.pickingMode = PickingMode.Ignore;
    }

    // Tableau : par equipe, joueurs tries par touches (K), elimine (D), ratio ; en manches, l'historique des manches.
    void FillTab()
    {
        var p = game.pb;
        pbTab.Clear();
        var head = Div(pbTab, "row", "pb-tab-head");
        Text(head, p.deathmatch ? $"MATCH À MORT · premiers à {p.target}" : $"MANCHE {p.round} · premiers à {Paintball.RoundsToWin}", "pb-tab-title");
        if (!p.deathmatch)
        {
            var hist = Div(pbTab, "row", "pb-tab-hist");
            for (int i = 0; i < Paintball.MaxRounds; i++)
            {
                var d = Div(hist, "pb-tab-round");
                if (i < p.history.Count) d.style.backgroundColor = p.history[i] < 0 ? new Color(0.5f, 0.5f, 0.55f) : PaintballView.TeamColor[p.history[i]];
                if (i == Paintball.MaxRounds / 2 - 1) d.style.marginRight = 14;
            }
        }
        for (int t = 0; t < 2; t++)
        {
            var box = Div(pbTab, "pb-tab-team");
            box.style.borderLeftColor = PaintballView.TeamColor[t];
            var th = Div(box, "row", "pb-tab-row", "pb-tab-th");
            var name = Text(th, $"{Paintball.TeamName[t].ToUpperInvariant()}   {p.score[t]}", "pb-tab-name"); name.style.color = PaintballView.TeamColor[t];
            foreach (var h in new[] { "K", "D", "K/D", "" }) Text(th, h, "pb-tab-num");
            foreach (var x in p.players.Where(x => x.team == t).OrderByDescending(x => x.hits).ThenBy(x => x.outs))
            {
                var r = Div(box, "row", "pb-tab-row");
                r.EnableInClassList("me", x.seat == game.MySeatOrZero);
                r.EnableInClassList("out", x.down && !p.deathmatch);
                Text(r, x.name, "pb-tab-name");
                Text(r, x.hits.ToString(), "pb-tab-num");
                Text(r, x.outs.ToString(), "pb-tab-num");
                Text(r, x.outs == 0 ? x.hits.ToString("0.00") : (x.hits / (float)x.outs).ToString("0.00"), "pb-tab-num");
                Text(r, x.down && !p.deathmatch ? "hors jeu" : "", "pb-tab-num", "pb-tab-state");
            }
        }
    }

    public void ClearFocus() { var f = root?.panel?.focusController?.focusedElement as VisualElement; f?.Blur(); }

    void RefreshPaintball()
    {
        var p = game.pb;
        pbPick.style.display = p.picking ? DisplayStyle.Flex : DisplayStyle.None;
        pbCross.style.display = pbFeed.style.display = p.picking ? DisplayStyle.None : DisplayStyle.Flex;
        pbScoreA.parent.style.display = p.picking ? DisplayStyle.None : DisplayStyle.Flex;
        if (p.picking)
        {
            int me = game.MySeatOrZero;
            pbPickCols.Clear();
            for (int t = 0; t < 2; t++)
            {
                int team = t;
                var col = Div(pbPickCols, "pb-pick-col");
                col.style.borderTopColor = PaintballView.TeamColor[t];
                var head = Text(col, $"{Paintball.TeamName[t].ToUpperInvariant()}  {p.Count(t)}/{Paintball.TeamMax}", "pb-pick-team");
                head.style.color = PaintballView.TeamColor[t];
                foreach (var x in p.players.Where(x => x.team == t)) Text(col, x.name + (x.seat == me ? "  (toi)" : ""), "pb-pick-name");
                var b = Btn(col, p.TeamOf(me) == t ? "Ton équipe" : "Rejoindre", () => game.PaintballPick(team), p.TeamOf(me) == t ? "m-dark" : "m-gold", "small");
                b.SetEnabled(p.TeamOf(me) != t && p.Count(t) < Paintball.TeamMax);
            }
            var undecided = p.players.Where(x => x.team < 0).Select(x => x.name).ToList();
            pbPickFoot.Clear();
            Text(pbPickFoot, undecided.Count > 0 ? "Pas encore choisi : " + string.Join(", ", undecided) : "Tout le monde a choisi !", "pb-pick-hint");
            if (game.PaintballCanLaunch && game.Online) Ico(Btn(pbPickFoot, "Lancer la partie", game.PaintballLaunch, "m-gold", "small"), "play");
            return;
        }
        // Manches : manches gagnees, et joueurs encore en jeu (pastilles) ; match a mort : touches.
        string dots(int t) => p.deathmatch ? "" : "  " + new string('●', p.Alive(t)) + new string('○', p.players.Count(x => x.team == t) - p.Alive(t));
        pbScoreA.text = $"{Paintball.TeamName[0].ToUpperInvariant()}  {p.score[0]}{dots(0)}";
        pbScoreB.text = $"{dots(1).Trim()}{(p.deathmatch ? "" : "  ")}{p.score[1]}  {Paintball.TeamName[1].ToUpperInvariant()}";
    }

    // Chaque image : chrono, retour en jeu, taches qui s'effacent, fil des touches.
    public void UpdatePaintball()
    {
        if (game.pb == null || pbTime == null) return;
        if (game.pb.picking) { pbPickTime.text = game.Online ? $"Départ dans {Mathf.CeilToInt(game.PaintballPickLeft)} s" : ""; return; }
        int t = Mathf.CeilToInt(game.PaintballLeft);
        float fz = game.PaintballFreeze;
        pbTime.text = fz > 0 ? $"Manche {game.pb.round}  ·  {Mathf.CeilToInt(fz)}" : $"{t / 60}:{t % 60:00}";
        var v = game.pbview;
        bool down = v.Down && !game.pb.Finished;
        pbCross.style.display = down || game.pb.Finished ? DisplayStyle.None : DisplayStyle.Flex;
        // Viseur dynamique (CS:GO) : s'ouvre quand on court ou saute, se resserre a l'arret et accroupi.
        float cs = 16 + v.Spread * 1400;
        pbCross.style.width = pbCross.style.height = cs; pbCross.style.marginLeft = pbCross.style.marginTop = -cs / 2;
        pbCross.style.borderTopLeftRadius = pbCross.style.borderTopRightRadius = pbCross.style.borderBottomLeftRadius = pbCross.style.borderBottomRightRadius = cs / 2;
        pbDown.style.display = down ? DisplayStyle.Flex : DisplayStyle.None;
        if (down)
        {
            string by = v.HitBy >= 0 ? game.pb.players[v.HitBy].name : "?";
            pbDown.text = game.pb.Respawn ? $"Touché par {by} !\nRetour dans {Mathf.CeilToInt(v.RespawnIn)}"
                : v.SpectatingName != null ? $"Tu regardes {v.SpectatingName}" : $"Touché par {by} !\nRetour à la prochaine manche";
        }
        float a = Mathf.Clamp01((pbSplatUntil - Time.time) / 1.2f);
        pbSplat.style.opacity = v.SpectatingName != null ? 0 : down ? Mathf.Max(a, 0.55f) : a;   // en spectateur : l'ecran est propre
        // Tableau des scores (Tab maintenu), comme CS:GO.
        bool tab = Input.GetKey(KeyCode.Tab) || game.PaintballTabForced;
        pbTab.style.display = tab ? DisplayStyle.Flex : DisplayStyle.None;
        if (tab && Time.frameCount % 10 == 0) FillTab();
        for (int i = pbFeedLines.Count - 1; i >= 0; i--)
            if (Time.time > pbFeedLines[i].until) { pbFeedLines[i].l.RemoveFromHierarchy(); pbFeedLines.RemoveAt(i); }
    }

    public void PaintballFeed(int by, int victim)
    {
        var p = game.pb;
        var l = Text(pbFeed, $"<color={PaintballView.Hex(p.TeamOf(by))}>{p.players[by].name}</color>  ●  <color={PaintballView.Hex(p.TeamOf(victim))}>{p.players[victim].name}</color>", "pb-feed-line");
        l.enableRichText = true;
        pbFeedLines.Add((l, Time.time + 5));
        while (pbFeedLines.Count > 5) { pbFeedLines[0].l.RemoveFromHierarchy(); pbFeedLines.RemoveAt(0); }
        if (victim == game.MySeatOrZero)
        {
            pbSplat.style.unityBackgroundImageTintColor = PaintballView.TeamColor[game.pb.TeamOf(by)];
            pbSplatUntil = Time.time + 3.5f;
        }
    }

    void PaintballVictory()
    {
        var p = game.pb;
        winTitle.text = p.winner < 0 ? "Égalité !" : $"L'équipe {Paintball.TeamName[p.winner]} gagne !";
        winTitle.style.color = p.winner < 0 ? Color.white : PaintballView.TeamColor[p.winner];
        winSub.text = (p.deathmatch ? $"{p.score[0]} - {p.score[1]}\n" : $"{p.score[0]} - {p.score[1]} manches\n") + string.Join("\n", p.players.OrderByDescending(x => x.hits).ThenBy(x => x.outs)
            .Select((x, i) => $"{i + 1}.  {x.name} ({Paintball.TeamName[x.team]}) : {x.hits} touche{(x.hits > 1 ? "s" : "")}, {x.outs} fois touché"));
    }
}
