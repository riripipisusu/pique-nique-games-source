using System.Collections;
using System.Linq;
using UnityEngine;

// Limite Limite : autour de la nappe (la table de Qui suis-je, sans post-its), vue a la premiere personne.
// L'hote (ou le jeu hors ligne) passe a la manche suivante apres le resultat, et coupe les attentes trop longues.
public partial class Game
{
    public Limite ll;
    public LimiteView lview;
    float llDist = 1.35f;
    float llPhaseAt, llBotAt;
    LPhase llLastPhase;
    public float LimiteLeft => Mathf.Max(0, (ll != null && ll.phase == LPhase.Play ? 75 : 60) - (Time.time - llPhaseAt));

    void StartLimite(System.Collections.Generic.List<string> n, int opt, int seed, System.Collections.Generic.List<string> av)
    {
        ll = new Limite(n, opt, seed);
        lview.Build(ll, MySeatOr0);
        llPhaseAt = Time.time; llLastPhase = ll.phase; llBotAt = Time.time + 2;
        ui.ShowHud();
        ui.Say($"{ll.Boss.name} est le Boss !", 2.5f);
    }

    void ApplyLimite(string[] p)
    {
        if (p[0] == "play" && p.Length == 2) p = new[] { "play", MySeatOr0.ToString(), p[1] };   // hors ligne : mon siege
        if (!ll.TryApply(p)) return;
        foreach (var e in ll.events)
        {
            switch (e.type)
            {
                case LEv.Round:
                    ui.LimiteNewRound();
                    Sound.I.Play("bj_shuffle", 0.6f);
                    break;
                case LEv.Played: break;   // le son vient des cartes qui se posent (LimiteView)
                case LEv.Reveal: ui.Say($"{ll.Boss.name} découvre les réponses...", 2.2f); Sound.I.Play("open"); break;
                case LEv.Win:
                    ui.Say(ll.Blanks > 1 ? $"Trou {ll.picks.Count} : « {e.text} » de {ll.players[e.seat].name} ! +1" : $"{ll.players[e.seat].name} remporte la manche !", 3);
                    Sound.I.Play("win"); Sound.I.Play("bj_chips", 0.6f);
                    break;
                case LEv.Over: StartCoroutine(LimiteEnd()); break;
            }
        }
        if (ll.phase != llLastPhase) { llLastPhase = ll.phase; llPhaseAt = Time.time; }
        llBotAt = Time.time + Random.Range(1.5f, 3f);
        ui.Refresh();
    }

    IEnumerator LimiteEnd() { yield return new WaitForSeconds(3); ui.ShowVictory(); }

    // Hote / hors ligne : manche suivante 7 s apres le resultat ; attente trop longue -> au hasard.
    public string LimiteTick(Limite l)
    {
        float t = Time.time - llPhaseAt;
        if (l.phase == LPhase.Result) return t > 7 ? "next" : null;
        if (l.phase == LPhase.Play && t > 75) return "timeout";
        if (l.phase == LPhase.Judge && t > 60 + 2.5f * l.order.Count) return "timeout";
        return null;
    }

    void LimiteUpdate()
    {
        if (ll == null) return;
        if (!Online && Idle && !paused && !ll.Finished)
        {
            var tick = LimiteTick(ll);
            if (tick != null) { Apply(tick); return; }
            if (Time.time > llBotAt)
            {
                // Les bots posent leurs cartes un par un, et choisissent quand ils sont le Boss (apres la lecture).
                if (ll.phase == LPhase.Play)
                {
                    var bot = ll.players.FirstOrDefault(x => x.seat != MySeatOr0 && x.seat != ll.boss && !ll.played.ContainsKey(x.seat));
                    if (bot != null) { Apply(string.Join("|", ll.BotPlay(bot.seat))); return; }
                }
                else if (ll.phase == LPhase.Judge && ll.boss != MySeatOr0 && ui.LimiteRevealDone && Time.time - llPhaseAt > 3)
                {
                    var a = ll.Bot();
                    if (a != null) { Apply(string.Join("|", a)); return; }
                }
            }
        }
        if (!Focused || paused) return;
        if (Input.GetMouseButton(1)) lview.Turn(Input.GetAxis("Mouse X") * 3 * settings.camSens);
        if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f && !ui.OverUi(Input.mousePosition)) llDist = Mathf.Clamp(llDist - Input.mouseScrollDelta.y * 0.1f, 0.6f, 1.8f);
        // Survol : la carte s'affiche en grand ; clic : choisir une carte de ma main, ou (Boss) la meilleure reponse.
        var c = ui.OverUi(Input.mousePosition) ? null : lview.Pick(cam.ScreenPointToRay(Input.mousePosition));
        lview.hover = c;
        ui.LimitePreview(LimiteView.Readable(c) ? c : null);
        if (c && Input.GetMouseButtonDown(0))
        {
            if (c.mine && ll.phase == LPhase.Play && ll.boss != MySeatOr0 && !ll.played.ContainsKey(MySeatOr0) && !Spectating) ui.LimiteToggle(c.text);
            else if (ll.phase == LPhase.Judge && ll.boss == MySeatOr0 && ui.LimiteRevealDone && c.key.StartsWith("p") && CanAct) { Sound.I.UI("click"); Act("pick|" + c.seat); }
        }
    }

    // Autotest : moi contre 3 bots ; je joue comme un bot, captures des etapes.
    IEnumerator LimiteTest(string dir, System.Func<string, IEnumerator> shot)
    {
        yield return new WaitForSeconds(2); yield return shot("l1-main");
        var mine = lview.AnyMine();
        if (mine) { lview.hover = mine; ui.LimitePreview(mine); yield return new WaitForSeconds(0.6f); yield return shot("l1b-survol"); ui.LimitePreview(null); lview.hover = null; }
        if (ll.boss != MySeatOr0)   // bulle "Poser" au-dessus de la carte choisie
        {
            foreach (var t in ll.players[MySeatOr0].hand.Take(ll.Blanks)) ui.LimiteToggle(t);
            yield return new WaitForSeconds(0.8f); yield return shot("l1c-poser");
            foreach (var t in ll.players[MySeatOr0].hand.Take(ll.Blanks)) ui.LimiteToggle(t);
        }
        bool judgeShot = false, resultShot = false, bossShot = false;
        int n = 0;
        while (!ll.Finished && n < 400)
        {
            n++;
            if (Idle && ll.phase == LPhase.Play && ll.boss != MySeatOr0 && !ll.played.ContainsKey(MySeatOr0) && Time.time > llBotAt)
            {
                ui.Refresh(); yield return new WaitForSeconds(0.5f);
                Apply(string.Join("|", ll.BotPlay(MySeatOr0)));
            }
            if (!judgeShot && ll.phase == LPhase.Judge) { judgeShot = true; yield return new WaitForSeconds(4); yield return shot("l2-reponses"); }
            if (!bossShot && ll.phase == LPhase.Judge && ll.boss == MySeatOr0) { bossShot = true; yield return new WaitForSeconds(4); yield return shot("l4-boss"); }
            if (Idle && ll.phase == LPhase.Judge && ll.boss == MySeatOr0 && ui.LimiteRevealDone) Apply(string.Join("|", ll.Bot()));
            if (!resultShot && ll.phase == LPhase.Result) { resultShot = true; yield return new WaitForSeconds(0.8f); yield return shot("l3-resultat"); }
            yield return new WaitForSeconds(0.25f);
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "limite.txt"), string.Join("\n", ll.log));
        yield return new WaitForSeconds(4); yield return shot("l5-victoire");
    }
}
