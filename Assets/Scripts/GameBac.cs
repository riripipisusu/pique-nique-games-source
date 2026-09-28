using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Petit bac : sur le plateau TV de Tenna (pupitres et ecran geant du quiz). Hors ligne, des bots ecrivent des mots
// tires de Resources/PetitBac/mots.txt et votent vite ; en ligne, l'hote pilote les phases (BacTick).
public partial class Game
{
    public PetitBac bac;
    public string bacText = "";   // categories ecrites a la main (hors ligne ; en ligne : net.LobbyText)
    readonly List<(float at, string act)> bacPlan = new List<(float, string)>();
    static Dictionary<string, List<string>> bacWords;

    void StartBac(List<string> n, int opt, int seed, List<string> av, string text)
    {
        bac = new PetitBac(n, opt, seed, text);
        qview.Build(bac.players, av);
        qview.imageUrl = null;
        qview.ShowQuestion(new QuizQuestion { c = "Petit bac", q = "À vos stylos !" });
        for (int i = 0; i < bac.players.Count; i++) qview.SetFound(i, false);
        quizPhaseStart = Time.time;
        bacPlan.Clear();
        ui.ShowHud();
        ui.Say("Tenez-vous prêts !");
        StartCoroutine(TvOpening());
    }

    // Pilotage (hote ou hors ligne) : lettre 2 s apres l'intro, 90 s d'ecriture (ou STOP), vote jusqu'a ce que tout le
    // monde ait fini (45 s au plus), 8 s de resultats, puis lettre suivante.
    public string BacTick(PetitBac b)
    {
        if (busy) return null;
        float t = Time.time - quizPhaseStart;
        switch (b.phase)
        {
            case BPhase.Intro: return t > 2 ? "next" : null;
            case BPhase.Write: return t * 1000 > PetitBac.WriteMs ? "end" : null;
            case BPhase.Vote: return b.ready.Count >= b.players.Count || t * 1000 > PetitBac.VoteMs ? "score" : null;
            case BPhase.Scores: return t > 8 ? "next" : null;
        }
        return null;
    }
    public float BacTimeLeft => bac == null ? 0 : bac.phase == BPhase.Write ? PetitBac.WriteMs / 1000f - (Time.time - quizPhaseStart)
                                               : bac.phase == BPhase.Vote ? PetitBac.VoteMs / 1000f - (Time.time - quizPhaseStart) : 0;

    // Action de mon interface ("ans|categorie|texte", "stop", "vote|cible|categorie|1", "ready") : l'hote y ajoute
    // mon siege ; hors ligne, on l'ajoute ici.
    public void BacAct(string action)
    {
        if (bac == null || !inGame || Spectating || paused || bac.Finished) return;
        if (Online) { net.Act(action); return; }
        var p = action.Split('|').ToList();
        p.Insert(1, MySeatOr0.ToString());
        Apply(string.Join("|", p));
    }

    void ApplyBac(string[] p)
    {
        if (!bac.TryApply(p)) return;
        foreach (var e in bac.events)
            switch (e.type)
            {
                case BEv.Letter:
                    quizPhaseStart = Time.time;
                    qview.ShowQuestion(new QuizQuestion { c = $"Manche {bac.round} / {bac.rounds}", q = bac.letter.ToString() });
                    for (int i = 0; i < bac.players.Count; i++) qview.SetFound(i, false);
                    qview.TennaFace("Pog", 2);
                    qview.TennaReact("point");
                    Sound.I.Play("open");
                    PlanBacBots();
                    ui.BacRound();
                    break;
                case BEv.Stop:
                    quizPhaseStart = Time.time;
                    Sound.I.Play("tick");
                    ui.Say(e.seat >= 0 ? $"{bac.players[e.seat].name} crie STOP !" : "Temps écoulé !", 2.5f);
                    if (e.seat >= 0) { qview.SetFound(e.seat, true); qview.TennaReact("good"); }
                    qview.ShowQuestion(new QuizQuestion { c = $"Manche {bac.round} / {bac.rounds}", q = "On vote !" });
                    PlanBacBots();
                    ui.BacVote();
                    break;
                case BEv.Vote:
                    ui.BacVote();
                    break;
                case BEv.Scored:
                    quizPhaseStart = Time.time;
                    Sound.I.Play("win");
                    qview.TennaFace("SmileSketchfab", 3);
                    qview.TennaReact("win");
                    var best = bac.players.Max(pl => pl.gained);
                    for (int i = 0; i < bac.players.Count; i++) qview.SetFound(i, bac.players[i].gained == best && best > 0);
                    qview.ShowQuestion(new QuizQuestion { c = $"Manche {bac.round} / {bac.rounds}", q = "Les points !" });
                    ui.BacScores();
                    break;
                case BEv.GameOver:
                    var top = bac.players.Max(pl => pl.score);
                    foreach (var pl in bac.players.Where(pl => pl.score == top)) qview.Winner(pl.seat);
                    qview.TennaFace("Pog", 4);
                    StartCoroutine(QuizEnd());
                    break;
            }
        ui.Refresh();
    }

    // --- Bots hors ligne ---------------------------------------------------------------------------------------------
    static List<string> BacWords(string category)
    {
        if (bacWords == null)
        {
            bacWords = new Dictionary<string, List<string>>();
            var txt = Resources.Load<TextAsset>("PetitBac/mots");
            if (txt)
                foreach (var line in txt.text.Split('\n'))
                {
                    int k = line.IndexOf(':');
                    if (k > 0) bacWords[line.Substring(0, k).Trim()] = line.Substring(k + 1).Split(',').Select(w => w.Trim()).Where(w => w.Length > 0).ToList();
                }
        }
        return bacWords.TryGetValue(category, out var l) ? l : new List<string>();
    }

    void PlanBacBots()
    {
        bacPlan.Clear();
        if (Online || bac == null) return;
        var rng = new System.Random(bac.round * 7919 + (int)bac.phase);
        for (int s = 0; s < bac.players.Count; s++)
        {
            if (s == MySeatOr0) continue;
            if (bac.phase == BPhase.Write)
            {
                float skill = 0.55f + 0.1f * (s % 4);   // 55 a 85 % des categories remplies
                for (int c = 0; c < bac.CatCount; c++)
                {
                    var words = BacWords(bac.categories[c]).Where(w => PetitBac.Norm(w).Length > 0 && char.ToUpperInvariant(PetitBac.Norm(w)[0]) == bac.letter).ToList();
                    if (words.Count == 0 || rng.NextDouble() > skill) continue;
                    bacPlan.Add((4 + (float)rng.NextDouble() * 31, $"ans|{s}|{c}|{words[rng.Next(words.Count)]}"));
                }
            }
            else if (bac.phase == BPhase.Vote) bacPlan.Add((3 + (float)rng.NextDouble() * 8, $"ready|{s}"));
        }
    }

    void BacUpdate()
    {
        if (Online || bac == null || !Idle || paused || bac.Finished) return;
        float t = Time.time - quizPhaseStart;
        foreach (var b in bacPlan.Where(b => b.at <= t).ToList()) { bacPlan.Remove(b); Apply(b.act); }
        var tick = BacTick(bac);
        if (tick != null) Apply(tick);
    }
}
