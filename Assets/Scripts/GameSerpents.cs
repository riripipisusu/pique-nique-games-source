using System.Collections;
using System.Linq;
using UnityEngine;

// Serpents et echelles : a plusieurs sur le meme PC (chacun son tour) ou en ligne. Le de se lance a la main (maintenir
// le clic, lacher d'un geste), avec Espace ou avec le bouton ; le pion avance tout seul.
public partial class Game
{
    public Serpents sp;
    public SerpentsView sview;
    float spDist = 1.35f;

    void StartSerpents(System.Collections.Generic.List<string> n, int opt, int seed)
    {
        sp = new Serpents(n, opt, seed);
        sview.Speed = settings.animSpeed;
        sview.Build(sp);
        ui.ShowHud();
        ui.Say($"{sp.Current.name} commence !", 2.5f);
    }

    void ApplySerpents(string[] p)
    {
        if (!sp.TryApply(p)) return;
        var evs = sp.events.ToList();
        StartCoroutine(Run(sview.Play(evs, e =>
        {
            var who = sp.players[e.seat].name;
            switch (e.type)
            {
                case SEv.Rolled: ui.Say($"{who} fait {e.value} !", 1.6f); break;
                case SEv.Ladder: ui.Say($"Une échelle ! {who} grimpe en {e.to} !", 2.2f); break;
                case SEv.Snake: ui.Say($"Un serpent ! {who} glisse en {e.to}...", 2.2f); break;
                case SEv.Bumped: ui.Say($"Case {e.from} déjà prise : {who} retourne au départ !", 2.4f); break;
            }
        }), () => { if (sp.Finished) ui.ShowVictory(); }));
    }

    void SerpentsInput()
    {
        if (sp == null) return;
        sview.de.Beckon(CanAct);
        if (!Focused || !CanAct) return;
        var de = sview.de;
        if (!de.Holding && Input.GetMouseButtonDown(0) && !ui.OverUi(Input.mousePosition)) de.Grab();
        if (de.Holding)
        {
            de.Hold(cam.ScreenPointToRay(Input.mousePosition));
            if (!Input.GetMouseButton(0)) ThrowSerpents(de.Release(), true);
        }
        else if (Input.GetKeyDown(KeyCode.Space)) ThrowSerpents(de.AutoFling(sp.turn), false);
    }

    // Comme aux petits chevaux : le lancer est simule ici, sa valeur et sa trajectoire partent dans l'action.
    void ThrowSerpents(float[] fling, bool byHand)
    {
        int v = sview.de.Predict(fling);
        sview.thrownHere = byHand;
        Act("roll|" + v + "|" + string.Join("|", fling.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
    }
    public void ThrowSerpentsAuto() { if (sp != null && CanAct && !sview.de.Holding) ThrowSerpents(sview.de.AutoFling(sp.turn), false); }

    // Camera fixe face au plateau ; la molette zoome.
    void SerpentsCamera(bool focus)
    {
        if (focus && !paused) spDist = Mathf.Clamp(spDist - Input.mouseScrollDelta.y * 0.12f, 0.8f, 2.3f);
        var pose = sview.CamPose(spDist);
        cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
    }

    // Autotest : une partie entiere, tous les sieges joues automatiquement, captures des moments cles.
    IEnumerator SerpentsTest(string dir, System.Func<string, IEnumerator> shot)
    {
        int turns = 0;
        bool ladderShot = false, snakeShot = false, rollShot = false;
        var bad = new System.Text.StringBuilder();
        while (!sp.Finished && turns < 400)
        {
            if (Idle)
            {
                ThrowSerpentsAuto();
                turns++;
                int v = sp.events.Count > 0 ? sp.events[0].value : 0;
                if (!rollShot) { rollShot = true; yield return new WaitForSeconds(0.45f); yield return shot("s2-de"); }
                if (!ladderShot && sp.events.Any(e => e.type == SEv.Ladder)) { ladderShot = true; yield return new WaitForSeconds(0.9f); yield return shot("s3-echelle"); }
                if (!snakeShot && sp.events.Any(e => e.type == SEv.Snake)) { snakeShot = true; yield return new WaitForSeconds(0.9f); yield return shot("s4-serpent"); }
                while (!Idle) yield return null;
                if (v > 0 && sview.de.Top() != v) bad.AppendLine($"de : {sview.de.Top()} affiche pour {v}");
            }
            yield return null;
        }
        yield return new WaitForSeconds(1); yield return shot("s5-fin");
        yield return new WaitForSeconds(3); yield return shot("s6-victoire");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "serpents.txt"), $"fini={sp.Finished} gagnant={sp.winner} tours={turns}" + System.Environment.NewLine + bad + string.Join(System.Environment.NewLine, sp.log));
    }
}
