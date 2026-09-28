using System.Collections;
using System.Linq;
using UnityEngine;

// La Bonne Paye : sur le meme PC (chacun son tour) ou en ligne. Le de se lance a la main, avec Espace ou le bouton ;
// toutes les decisions (acheter, assurer, vendre, miser, rembourser, epargner...) passent par l'interface.
public partial class Game
{
    public BonnePaye bp;
    public BonnePayeView bpview;

    void StartBonnePaye(System.Collections.Generic.List<string> n, int opt, int seed)
    {
        bp = new BonnePaye(n, opt, seed);
        bpview.Speed = settings.animSpeed;
        bpview.Build(bp);
        bpYaw = bpYawGoal = 0;
        ui.ShowHud();
        ui.Say($"{bp.Current.name} commence !", 2.5f);
    }

    void ApplyBonnePaye(string[] p)
    {
        if (!bp.TryApply(p)) return;
        var evs = bp.events.ToList();
        StartCoroutine(Run(bpview.Play(evs, e =>
        {
            switch (e.type)
            {
                case BPEv.Rolled: ui.Say($"{bp.players[e.seat].name} fait {e.amount} !", 1.4f); break;
                case BPEv.Jackpot: ui.Say(e.text, 3); break;
                case BPEv.Card: ui.BpShowCard(e.deck, e.img); break;
                case BPEv.Month: ui.Say(e.text, 2.5f); break;
                case BPEv.Finished: ui.Say(e.text, 4); break;
            }
        }), () => { if (bp.Finished) ui.ShowVictory(); }));
    }

    // Action de mon interface (decision, epargne...) : jouee si c'est bien a moi.
    public void BpAct(string action) { if (bp != null) Act(action); }

    void BonnePayeInput()
    {
        if (bp == null) return;
        var job = bp.Pending;
        int want = job != null && BonnePaye.IsDraw(job.t) ? (job.t == BonnePaye.Task.MailDraw ? 0 : job.t == BonnePaye.Task.AcqDraw ? 1 : 2) : -1;
        bpview.DeckWanted = want;
        // Piocher soi-meme : clic sur la bonne pioche (ou Espace).
        if (want >= 0 && CanAct && Focused)
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0) && !ui.OverUi(Input.mousePosition) && bpview.DeckUnder(cam.ScreenPointToRay(Input.mousePosition)) == want) Act("draw");
            return;
        }
        // Lancer du de : pour avancer (debut du tour), pour la loterie ou pour "Besoin d'argent ?".
        bool myRoll = CanAct && (job == null && !bp.rolled || job != null && BonnePaye.IsRoll(job.t));
        bpview.de.Beckon(myRoll);
        if (!Focused || !myRoll) return;
        var de = bpview.de;
        if (!de.Holding && Input.GetMouseButtonDown(0) && !ui.OverUi(Input.mousePosition)) de.Grab();
        if (de.Holding)
        {
            de.Hold(cam.ScreenPointToRay(Input.mousePosition));
            if (!Input.GetMouseButton(0)) ThrowBonnePaye(de.Release(), true, RollAction());
        }
        else if (Input.GetKeyDown(KeyCode.Space)) ThrowBonnePaye(de.AutoFling(bp.Actor), false, RollAction());
    }

    string RollAction() { var j = bp.Pending; return j == null ? "roll" : j.t == BonnePaye.Task.LottoRoll ? "lottoroll" : j.t == BonnePaye.Task.CommRoll ? "commroll" : "besoinroll"; }
    void ThrowBonnePaye(float[] fling, bool byHand, string action = "roll")
    {
        int v = bpview.de.Predict(fling);
        bpview.thrownHere = byHand;
        Act(action + "|" + v + "|" + string.Join("|", fling.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
    }
    public void ThrowBonnePayeAuto()
    {
        if (bp == null || !CanAct || bpview.de.Holding) return;
        var j = bp.Pending;
        if (j == null ? bp.rolled : !BonnePaye.IsRoll(j.t)) return;
        ThrowBonnePaye(bpview.de.AutoFling(bp.Actor), false, RollAction());
    }

    // Camera fixe, cadree automatiquement. Le plateau se tourne par quarts de tour (boutons du HUD) : chacun le met dans
    // son sens sans qu'il deborde sur les coins de table des joueurs (en biais, ses coins passeraient dessus).
    float bpYaw, bpYawGoal;
    public void BpRotate(int dir) => bpYawGoal += 90 * dir;
    void BonnePayeCamera(bool focus)
    {
        bpYaw = Mathf.MoveTowards(bpYaw, bpYawGoal, 360 * Time.unscaledDeltaTime);
        bpview.BoardYaw = bpYaw;
        var pose = bpview.CamPose(cam.fieldOfView, cam.aspect);
        cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
    }

    // Autotest : une partie d'un mois a 4, toutes les decisions prises par Bot(), captures aux moments cles.
    IEnumerator BonnePayeTest(string dir, System.Func<string, IEnumerator> shot)
    {
        bpYawGoal = 90; yield return new UnityEngine.WaitForSeconds(0.6f); yield return shot("p1b-tourne90"); bpYawGoal = 0; yield return new UnityEngine.WaitForSeconds(0.4f);   // cadrage apres rotation
        int acts = 0;
        bool besoinTried = false, extended = false, cardShot = false, buyShot = false, decideShot = false, lottoShot = false, paydayShot = false;
        while (!bp.Finished && acts < 600)
        {
            if (Idle)
            {
                var job = bp.Pending;
                if (job != null && job.t == BonnePaye.Task.Buy && !buyShot) { buyShot = true; ui.Refresh(); yield return new WaitForSeconds(0.3f); yield return shot("p3b-achat"); }
                if (job != null && !decideShot) { decideShot = true; ui.Refresh(); yield return new WaitForSeconds(0.3f); yield return shot("p3-decision"); }
                if (job != null && job.t == BonnePaye.Task.LottoRoll && !lottoShot) { lottoShot = true; ui.Refresh(); yield return new WaitForSeconds(0.3f); yield return shot("p4-loterie"); }
                var a = bp.Bot();
                if (job == null && !bp.rolled && bp.Current.besoin.Count > 0 && bp.Current.money >= 100 && !besoinTried) { besoinTried = true; a = new[] { "besoin", "100" }; }   // test : Besoin d'argent ?
                if (job != null && job.t == BonnePaye.Task.Extend && !extended) { extended = true; yield return shot("p5b-prolonger"); a = new[] { "extend", "1" }; }   // test : on prolonge d'un mois
                if (a[0] == "roll" || a[0] == "lottoroll" || a[0] == "commroll" || a[0] == "besoinroll") ThrowBonnePayeAuto(); else Apply(string.Join("|", a));
                acts++;
                if (!cardShot && bp.events.Any(e => e.type == BPEv.Card)) { cardShot = true; yield return new WaitForSeconds(0.9f); yield return shot("p2-carte"); }
                if (!paydayShot && bp.events.Any(e => e.type == BPEv.Month)) { paydayShot = true; yield return new WaitForSeconds(0.9f); yield return shot("p5-paye"); }
            }
            foreach (var pl in bp.players) if (pl.money < 0) { System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "bonnepaye.txt"), $"SOLDE NEGATIF {pl.name} {pl.money}\n"); }
            yield return null;
        }
        yield return new WaitForSeconds(1); yield return shot("p6-fin");
        yield return new WaitForSeconds(3); yield return shot("p7-victoire");
        System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "bonnepaye.txt"), $"fini={bp.Finished} gagnant={bp.winner} actions={acts} cagnotte={bp.pot}\n"
            + string.Join("\n", bp.players.Select(p => $"{p.name}: argent {p.money}, livret {p.savings}, prets {p.loans}, capital {p.Capital}")) + "\n" + string.Join("\n", bp.log));
    }
}
