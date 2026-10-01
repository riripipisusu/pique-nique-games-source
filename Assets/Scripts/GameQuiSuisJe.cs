using System.Collections;
using System.Linq;
using UnityEngine;

// Qui suis-je ? : camera a la premiere personne et reactions des joueurs assis ; hors ligne, des bots (mode guide).
public partial class Game
{
    public QuiSuisJe qsj;
    public QuiSuisJeView qsview;
    float qsjVoteAt, qsjBotAt;

    void StartQuiSuisJe(System.Collections.Generic.List<string> n, int opt, int seed, System.Collections.Generic.List<string> av)
    {
        qsj = new QuiSuisJe(n, Online ? opt | 1 : opt & ~1, seed);   // hors ligne : mode guide contre des bots
        qsjBotAt = Time.time + 2;
        qsview.Build(qsj, MySeatOr0, av);
        ui.ShowHud();
    }

    void ApplyQuiSuisJe(string[] p)
    {
        // Hors ligne (autotest) : "pick|nom" et "vote|v" sont pour mon siege.
        if ((p[0] == "pick" || p[0] == "vote") && p.Length == 2) p = new[] { p[0], MySeatOr0.ToString(), p[1] };
        if (!qsj.TryApply(p)) return;
        foreach (var e in qsj.events)
        {
            var who = qsj.players[e.seat < 0 ? 0 : e.seat].name;
            switch (e.type)
            {
                case WEv.Chosen: qsview.Sync(); Sound.I.Play("bj_place1", 0.6f); break;
                case WEv.Ask: qsview.Anim(e.seat, "SitTalk"); ui.QsjBubble(e.seat, e.text); qsjVoteAt = Time.time; Sound.I.Play("tick"); break;
                case WEv.Answer: ui.QsjBubble(e.seat, QuiSuisJe.AnswerText(e.answer), e.answer); Sound.I.Play(e.answer == QuiSuisJe.Oui ? "bj_chip1" : "tick", 0.8f); break;
                case WEv.Found:
                    qsview.Anim(e.seat, "SitClap"); qsview.AllBut(e.seat, "SitClap"); qsview.Sync();
                    ui.Say(e.seat == MySeatOr0 ? $"Bravo ! Tu étais {e.text} ! +{e.points}" : $"{who} a trouvé : {e.text} ! +{e.points}", 3);
                    Sound.I.Play("win");
                    break;
                case WEv.Wrong: qsview.AllBut(e.seat, "SitLaugh"); ui.QsjBubble(e.seat, $"Je suis {e.text} ?", QuiSuisJe.Non); Sound.I.Play("lose", 0.6f); break;
                case WEv.Pass: ui.QsjBubble(e.seat, "Je passe."); break;
                case WEv.Turn: if (e.seat == MySeatOr0) Sound.I.Play("open", 0.7f); break;
                case WEv.Over: StartCoroutine(QsjEnd()); break;
                case WEv.Round: qsview.Sync(); ui.Say($"Manche {qsj.round} !", 2.5f); Sound.I.Play("open"); break;
            }
        }
        qsjBotAt = Time.time + Random.Range(1.4f, 2.4f);
        ui.Refresh();
    }

    IEnumerator QsjEnd() { yield return new WaitForSeconds(3); ui.ShowVictory(); }

    // L'hote coupe le vote au bout de 30 s (quelqu'un d'absent ou d'indecis).
    public string QsjTick(QuiSuisJe q) => q.phase == WPhase.Vote && Time.time - qsjVoteAt > 30 ? "voteend" : null;

    void QuiSuisJeUpdate()
    {
        if (qsj == null) return;
        // Bots hors ligne : ils choisissent le personnage de leur voisin, puis jouent leur tour.
        if (!Online && Idle && !paused && !qsj.Finished && Time.time > qsjBotAt)
        {
            if (qsj.phase == WPhase.Choose)
            {
                var bot = qsj.players.FirstOrDefault(p => p.seat != MySeatOr0 && qsj.players[qsj.TargetOf(p.seat)].perso == null);
                if (bot != null) { Apply($"pick|{bot.seat}|{qsj.BotPick()}"); return; }
            }
            else if (qsj.phase == WPhase.Ask && qsj.turn != MySeatOr0)
            {
                var a = qsj.Bot();
                if (a != null) { Apply(string.Join("|", a)); return; }
            }
        }
        if (!Focused || paused) return;
        // Regarder autour de soi (clic droit), zoomer (molette), clic sur un joueur pour lire son post-it.
        if (Input.GetMouseButton(1)) qsview.Look(Input.GetAxis("Mouse X") * 3 * settings.camSens, Input.GetAxis("Mouse Y") * 3 * settings.camSens);
        if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f && !ui.OverUi(Input.mousePosition)) qsview.Zoom(Input.mouseScrollDelta.y);
        if (Input.GetMouseButtonDown(0) && !ui.OverUi(Input.mousePosition))
        {
            int s = qsview.HeadUnder(cam.ScreenPointToRay(Input.mousePosition));
            if (s >= 0) qsview.LookAt(s); else qsview.ResetView();
        }
    }

    // Autotest hors ligne (mode guide) : je choisis une proposition, les bots font le reste, je joue avec la strategie du bot.
    IEnumerator QuiSuisJeTest(string dir, System.Func<string, IEnumerator> shot)
    {
        yield return new WaitForSeconds(2); yield return shot("w1-choix");
        Act("pick|" + qsj.BotPick());
        while (qsj.phase == WPhase.Choose) yield return null;
        yield return new WaitForSeconds(1); yield return shot("w2-vue");
        // FPS en tournant la tete (le cas qui saccadait) : moyenne et pire image sur 4 s.
        for (int pass = 1; pass <= 2; pass++)
        { int f0 = Time.frameCount, slow = 0; float t0 = Time.realtimeSinceStartup, worst = 0;
          while (Time.realtimeSinceStartup - t0 < 4) { qsview.Look(Mathf.Sin(Time.realtimeSinceStartup * 2) * 1.5f, 0); worst = Mathf.Max(worst, Time.unscaledDeltaTime); if (Time.unscaledDeltaTime > 0.03f) slow++; yield return null; }
          System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "fps.txt"), $"quisuisje {pass}: {(Time.frameCount - f0) / (Time.realtimeSinceStartup - t0):0} fps, pire image {worst * 1000:0} ms, images > 30 ms : {slow}\n"); }
        qsview.ResetView();
        qsview.ResetView(); qsview.Look(0, -66); yield return new WaitForSeconds(0.5f); yield return shot("w2b-corps");
        qsview.LookAt(1); yield return new WaitForSeconds(1.2f); yield return shot("w3-zoom-postit");
        qsview.ResetView();
        int n = 0; bool mine = false;
        while (!qsj.Finished && n < 300)
        {
            if (Idle && qsj.turn == MySeatOr0 && qsj.phase == WPhase.Ask && Time.time > qsjBotAt)
            {
                if (!mine) { mine = true; ui.Refresh(); yield return new WaitForSeconds(0.5f); yield return shot("w4-mon-tour"); }
                Apply(string.Join("|", qsj.Bot())); n++;
                if (n == 2) { yield return new WaitForSeconds(0.4f); yield return shot("w5-reponse"); }
            }
            yield return null;
        }
        yield return new WaitForSeconds(1); yield return shot("w7-fin");
        yield return new WaitForSeconds(3); yield return shot("w8-victoire");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "quisuisje.txt"), string.Join("\n", qsj.log));
    }
}
