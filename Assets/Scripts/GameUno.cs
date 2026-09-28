using System.Collections;
using System.Linq;
using UnityEngine;

// Uno : applique les actions (animations de la scene puis interface), fait jouer les bots hors ligne,
// et enchaine les manches (l'hote, ou ce PC hors ligne, lance "next" apres le decompte des points).
public partial class Game
{
    public Uno uno;
    public UnoView uview;
    public System.Collections.Generic.List<string> unoAvatars = new System.Collections.Generic.List<string>();
    float unoRoundAt, botAt, catchAt = -1, jumpAt = -1;
    public float botForget = 0.15f;   // chance qu'un bot oublie de dire UNO

    void StartUno(System.Collections.Generic.List<string> n, int opt, int seed, System.Collections.Generic.List<string> av)
    {
        uno = new Uno(n, opt, seed);
        unoAvatars = av;
        uview.Build(uno, av, mySeat);
        uview.MyCardArrived = () => ui.Refresh();
        uview.ReserveMine = ui.ReserveIncoming;
        uview.MineScreen = ui.IncomingScreen;
        uview.LandMine = ui.LandIncoming;
        uview.Dealing = true;
        ui.ShowHud();
        var first = uno.events.ToList();   // la premiere donne se joue en animation, comme les suivantes
        StartCoroutine(Run(uview.Play(first, null), null));
        ui.UnoToast(uno.Current.name == (mySeat >= 0 ? n[mySeat] : n[0]) ? "À toi de commencer !" : $"{uno.Current.name} commence !");
        botAt = Time.time + 1.5f;
    }

    // Pilotage (hote ou hors ligne) : manche suivante 6 s apres la fin de la precedente.
    public string UnoTick(Uno u) => u.phase == UPhase.RoundOver && Time.time - unoRoundAt > 6 ? "next" : null;

    // "UNO !" et "Contre-UNO !" se jouent a tout moment, pas seulement a son tour.
    public void UnoCall(string action)
    {
        if (uno == null || Spectating || uno.Finished) return;
        if (Online) net.Act(action); else Apply(action);
    }

    void ApplyUno(string[] p)
    {
        if (!uno.TryApply(p)) return;
        var evs = uno.events.ToList();
        StartCoroutine(Run(uview.Play(evs, e =>
        {
            switch (e.type)
            {
                case UEv.Uno: ui.UnoBubble(e.seat, "UNO !"); break;
                case UEv.Caught: ui.UnoBubble(e.seat, "Oublié ! +2"); ui.UnoToast($"{uno.players[e.other].name} a vu que {uno.players[e.seat].name} n'a pas dit UNO !"); break;
                case UEv.Skipped: if (e.seat != MySeatOr0) ui.UnoSkipMark(e.seat, uno.color); break;
                case UEv.Reversed: ui.UnoToast("Changement de sens !"); break;
                case UEv.RoundOver:
                    unoRoundAt = Time.time;
                    Sound.I.Play("win");
                    ui.UnoRoundOver();
                    break;
                case UEv.Deal: ui.UnoNewRound(); break;
                case UEv.Challenge: ui.UnoToast(e.seat == MySeatOr0 && !Online || e.seat == mySeat ? "+4 ! Tu peux le dénoncer si tu penses que c'est du bluff." : $"{uno.players[e.seat].name} peut dénoncer le +4..."); break;
                case UEv.ChallengeResult: ui.UnoBubble(e.seat, e.other == 1 ? "Bluff ! +4" : "Raté ! +6"); ui.UnoToast(e.other == 1 ? "Bluff démasqué !" : "Le +4 était réglo !"); break;
                case UEv.Swapped: ui.UnoToast($"{uno.players[e.seat].name} échange sa main avec {uno.players[e.other].name} !"); break;
                case UEv.Rotated: ui.UnoToast("Toutes les mains tournent !"); break;
                case UEv.JumpIn: ui.UnoBubble(e.seat, "Intervention !"); break;
            }
        }), () =>
        {
            if (uno.Finished) StartCoroutine(UnoEnd());
            else PlanUnoBots();
        }));
    }

    IEnumerator UnoEnd() { yield return new WaitForSeconds(4); ui.ShowVictory(); }

    // --- Bots hors ligne : ils reflechissent un instant, oublient parfois de dire UNO et guettent tes oublis ----
    void PlanUnoBots()
    {
        botAt = Time.time + Random.Range(0.8f, 1.6f);
        catchAt = uno.vulnerable >= 0 && Random.value < (uno.vulnerable == MySeatOr0 ? 0.7f : 0.35f) ? Time.time + Random.Range(1.2f, 3f) : -1;
        jumpAt = uno.jumpIn && Random.value < 0.5f ? Time.time + Random.Range(0.35f, 1.1f) : -1;   // intervention d'un bot
    }

    // Piocher = cliquer sur le paquet de la nappe (quand c'est mon tour et que je peux piocher).
    void UnoClick()
    {
        if (uno == null || !Focused || paused) return;
        if (uview.WheelPicking)
        {
            // Roue des couleurs : clic sur une part = couleur choisie ; clic droit, Echap ou clic a cote = annuler.
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) { uview.HideWheel(); return; }
            if (Input.GetMouseButtonDown(0)) WheelClick(Input.mousePosition);
            return;
        }
        if (!uview.DeckReady || !Input.GetMouseButtonDown(0)) return;
        if (uview.DeckUnder(cam.ScreenPointToRay(Input.mousePosition))) Act("draw");
    }

    public void WheelClick(Vector2 screen)
    {
        int c = uview.WedgeUnder(cam.ScreenPointToRay(screen));
        uview.HideWheel(c);
        if (c >= 0) ui.UnoPlayColor(c);
    }

    void UnoBots()
    {
        if (Online || uno == null || !Idle || paused || uno.Finished) return;
        var tick = UnoTick(uno);
        if (tick != null) { Apply(tick); return; }
        // Contre-UNO par un bot (sur toi ou sur un autre bot distrait).
        if (catchAt > 0 && Time.time > catchAt)
        {
            catchAt = -1;
            if (uno.vulnerable >= 0)
            {
                int by = Enumerable.Range(1, uno.players.Count - 1).Where(s => s != uno.vulnerable).OrderBy(_ => Random.value).FirstOrDefault();
                if (by > 0) { Apply($"catch|{by}|{uno.vulnerable}"); return; }
            }
        }
        // Intervention d'un bot qui a la carte identique.
        if (jumpAt > 0 && Time.time > jumpAt)
        {
            jumpAt = -1;
            foreach (var p in uno.players.Where(p => p.seat != 0).OrderBy(_ => Random.value))
            {
                int c = p.hand.FirstOrDefault(x => uno.CanJumpIn(p.seat, x));
                if (p.hand.Any(x => uno.CanJumpIn(p.seat, x))) { Apply($"jump|{p.seat}|{c}"); return; }
            }
        }
        if (uno.Actor <= 0 || Time.time < botAt) return;   // siege 0 : moi
        var a = uno.Bot();
        if (a[0] == "play" && a[3] == "1" && Random.value < botForget) a[3] = "0";   // oups, oublie
        Apply(string.Join("|", a));
    }
}
