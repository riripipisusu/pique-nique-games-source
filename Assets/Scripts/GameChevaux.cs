using System.Collections;
using System.Linq;
using UnityEngine;

// Petits chevaux : a plusieurs sur le meme PC (chacun son tour) ou en ligne. On lance le de (bouton, Espace ou clic
// sur le de), puis on choisit le cheval (clic dessus, bouton ou touches 1 a 4).
public partial class Game
{
    public Chevaux ch;
    public ChevauxView cview;
    float chYaw, chPitch = 58, chDist = 1.35f;

    void StartChevaux(System.Collections.Generic.List<string> n, int opt, int seed)
    {
        ch = new Chevaux(n, opt, seed);
        cview.Speed = settings.animSpeed;
        cview.Build(ch);
        chYaw = cview.YawFor(Online ? mySeat : 0);   // vue fixe du cote de ma couleur, elle ne change jamais
        ui.ShowHud();
        ui.Say($"{ch.Current.name} commence : il faut un 6 pour sortir !", 3);
    }

    void ApplyChevaux(string[] p)
    {
        if (!ch.TryApply(p)) return;
        var evs = ch.events.ToList();
        StartCoroutine(Run(cview.Play(evs, e =>
        {
            var who = ch.players[e.seat].name;
            switch (e.type)
            {
                case CEv.Rolled: ui.Say($"{who} fait {e.value} !", 1.6f); break;
                case CEv.Sacrifice: ui.Say($"Trois 6 de suite ! {who} doit renvoyer un cheval à l'écurie.", 3); break;
                case CEv.NoMove: ui.Say(e.value == 6 ? $"{who} ne peut pas bouger... mais rejoue !" : $"{who} ne peut pas bouger."); break;
                case CEv.Captured: ui.Say($"Un cheval de {who} retourne à l'écurie !"); break;
                case CEv.Moved: if (e.to == Chevaux.Home) ui.Say($"{who} rentre un cheval !"); break;
                case CEv.Turn:
                    if (e.seat == evs[0].seat && evs.Any(x => (x.type == CEv.Moved || x.type == CEv.NoMove) && x.value == 6))   // un 6 : on rejoue
                        ui.Say($"6 : {who} rejoue !");
                    break;
            }
        }), () =>
        {
            if (ch.Finished) ui.ShowVictory();
        }));
    }

    // Clavier et souris : le de, puis les chevaux qui peuvent bouger.
    void ChevauxInput()
    {
        if (ch == null) return;
        cview.hover = -1;
        cview.de.Beckon(CanAct && ch.dice == 0 && !ch.sacrifice);   // le de sautille : a toi de le lancer
        if (!Focused || !CanAct) return;
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (ch.dice == 0 && !ch.sacrifice)
        {
            // Maintenir le clic : on tient le de au-dessus du plateau ; le lacher d'un geste : il roule. Espace : lancer auto.
            var de = cview.de;
            if (!de.Holding && Input.GetMouseButtonDown(0) && !ui.OverUi(Input.mousePosition)) de.Grab();
            if (de.Holding)
            {
                de.Hold(ray);
                if (!Input.GetMouseButton(0)) ThrowDie(de.Release(), true);
            }
            else if (Input.GetKeyDown(KeyCode.Space)) ThrowDie(de.AutoFling(ch.turn), false);
            return;
        }
        int h = cview.HorseUnder(ray);
        if (h >= 0 && ch.CanPick(h)) cview.hover = h;
        if (Input.GetMouseButtonDown(0) && cview.hover >= 0) Move(h);
        for (int k = 0; k < ch.count; k++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + k) || Input.GetKeyDown(KeyCode.Keypad1 + k)) Move(k);
    }

    // Le de roule d'abord ici (simulation) : l'action porte sa valeur et sa trajectoire, rejouees a l'identique partout.
    void ThrowDie(float[] fling, bool shaken)
    {
        int v = cview.de.Predict(fling);
        cview.thrownHere = shaken;   // lance a la main : pas d'elan rejoue
        Act("roll|" + v + "|" + string.Join("|", fling.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
    }
    public void ThrowDieAuto() { if (ch != null && CanAct && ch.dice == 0 && !ch.sacrifice && !cview.de.Holding) ThrowDie(cview.de.AutoFling(ch.turn), false); }

    // Camera fixe du cote de ma couleur (elle ne tourne jamais) ; seule la molette zoome.
    void ChevauxCamera(float dt, bool focus)
    {
        if (focus && !paused) chDist = Mathf.Clamp(chDist - Input.mouseScrollDelta.y * 0.12f, 0.7f, 2.2f);
        var pose = cview.CamPose(chYaw, chPitch, chDist);
        cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
    }

    // Autotest : une partie entiere jouee par les bots (tous les sieges), avec des captures d'ecran.
    IEnumerator ChevauxTest(string dir, System.Func<string, IEnumerator> shot)
    {
        var bad = new System.Text.StringBuilder();
        int turns = 0, shots = 0;
        bool capShot = false, ladderShot = false, rollShot = false;
        while (!ch.Finished && turns < 600)
        {
            if (Idle)
            {
                var a = ch.Bot();
                if (a[0] == "roll" && ch.turn == 0) ThrowDieAuto();   // joueur 1 : vrai lancer (simule ici, valeur envoyee)
                else Apply(string.Join("|", a));                     // bots : valeur tiree par les regles, de corrige a l'ecran
                turns++;
                if (a[0] == "roll" && ch.events.Count > 0)   // le de doit montrer le resultat
                {
                    int v = ch.events[0].value;
                    while (!Idle) yield return null;
                    if (cview.de.Top() != v) bad.AppendLine($"de : {cview.de.Top()} affiche pour {v}");
                }
                if (!rollShot && a[0] == "roll") { rollShot = true; yield return new WaitForSeconds(0.5f); yield return shot("c2-de"); }
            }
            if (busy && !capShot && ch.events.Any(e => e.type == CEv.Captured)) { capShot = true; yield return new WaitForSeconds(0.45f); yield return shot("c4-mange"); }
            if (Idle && !ladderShot && ch.players.Any(p => p.horses.Any(x => x > Chevaux.Foot))) { ladderShot = true; yield return shot("c5-escalier"); }
            if (Idle && turns > 0 && turns % 120 == 0 && shots < 3) yield return shot("c3-partie" + shots++);
            foreach (var p in ch.players)   // jamais deux chevaux d'une meme couleur sur une meme case
                for (int x = 0; x < ch.count; x++)
                    for (int y = x + 1; y < ch.count; y++)
                        if (p.horses[x] >= 0 && p.horses[x] < Chevaux.Home && p.horses[x] == p.horses[y]) bad.AppendLine($"{p.name} : deux chevaux en {p.horses[x]}");
            yield return null;
        }
        yield return new WaitForSeconds(1); yield return shot("c6-fin");
        yield return new WaitForSeconds(4); yield return shot("c7-victoire");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "chevaux.txt"), $"fini={ch.Finished} gagnant={ch.winner} actions={turns}" + System.Environment.NewLine + bad + string.Join(System.Environment.NewLine, ch.log));
    }
}
