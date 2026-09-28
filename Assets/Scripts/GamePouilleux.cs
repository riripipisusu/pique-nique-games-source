using System.Collections;
using System.Linq;
using UnityEngine;

// Le pouilleux : contre des bots hors ligne, ou en ligne. A mon tour, je clique une carte de l'eventail de mon voisin.
public partial class Game
{
    public Pouilleux pq;
    public PouilleuxView pview;
    float pqBotAt;

    void StartPouilleux(System.Collections.Generic.List<string> n, int opt, int seed, System.Collections.Generic.List<string> av)
    {
        pq = new Pouilleux(n, opt, seed);
        pview.speed = settings.animSpeed;
        pview.Build(pq, MySeatOr0);
        ui.ShowHud();
        var first = pq.events.ToList();
        StartCoroutine(Run(pview.Play(first, null), null));
        pqBotAt = Time.time + 2;
    }

    void ApplyPouilleux(string[] p)
    {
        if (!pq.TryApply(p)) return;
        var evs = pq.events.ToList();
        StartCoroutine(Run(pview.Play(evs, e =>
        {
            int me = MySeatOr0;
            if (e.type == PEv.Take && e.seat == me && !Spectating)
            {
                bool pair = evs.Any(x => x.type == PEv.Pair && x.seat == me);
                ui.Say(pair ? $"Tu pioches le {Pouilleux.Name(e.card)} : paire, tu la jettes !" : $"Tu pioches le {Pouilleux.Name(e.card)}.", 2.5f);
                ui.pqNew = pair ? -1 : e.card;
            }
            if (e.type == PEv.Take && e.other == me && !Spectating)
            {
                ui.Say($"{pq.players[e.seat].name} te prend le {Pouilleux.Name(e.card)} !", 2.5f);
                ui.Refresh();   // la carte quitte ma main tout de suite
            }
            if (e.type == PEv.Out) ui.Say($"{pq.players[e.seat].name} est tranquille !", 2);
            if (e.type == PEv.Lose) ui.Say($"{pq.players[e.seat].name} est le POUILLEUX !", 4);
        }), () =>
        {
            pqBotAt = Time.time + Random.Range(0.8f, 1.5f);
            if (pq.Finished) StartCoroutine(PouilleuxEnd());
        }));
    }

    IEnumerator PouilleuxEnd() { yield return new WaitForSeconds(2.5f); ui.ShowVictory(); }

    void PouilleuxUpdate()
    {
        if (pq == null) return;
        pview.hover = -1;
        // Bots (hors ligne) : sieges autres que le mien.
        if (!Online && Idle && !paused && !pq.Finished && pq.turn != MySeatOr0 && Time.time > pqBotAt)
        {
            var a = pq.Bot();
            if (a != null) Apply(string.Join("|", a));
            return;
        }
        if (!Focused || !CanAct) return;
        int k = pview.CardUnder(cam.ScreenPointToRay(Input.mousePosition));
        pview.hover = k;
        if (k >= 0 && Input.GetMouseButtonDown(0)) Act("take|" + k);
    }

    // Autotest : moi (joue automatiquement) contre 3 bots, jusqu'au pouilleux.
    IEnumerator PouilleuxTest(string dir, System.Func<string, IEnumerator> shot)
    {
        int turns = 0; bool mineShot = false;
        while (!pq.Finished && turns < 400)
        {
            if (Idle && pq.turn == MySeatOr0)
            {
                if (!mineShot) { mineShot = true; ui.Refresh(); pview.hover = 0; yield return new WaitForSeconds(0.4f); yield return shot("q2-mon-tour"); }
                Apply(string.Join("|", pq.Bot()));
                turns++;
                if (turns == 3) { yield return new WaitForSeconds(0.3f); yield return shot("q3-pioche"); }
            }
            yield return null;
        }
        yield return new WaitForSeconds(1.5f); yield return shot("q4-fin");
        yield return new WaitForSeconds(3); yield return shot("q5-victoire");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "pouilleux.txt"), $"fini={pq.Finished} pouilleux={pq.loser}\n" + string.Join("\n", pq.log));
    }
}
