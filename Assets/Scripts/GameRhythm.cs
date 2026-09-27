using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Jeu de rythme (TV Time) : chaque PC joue la chanson et arbitre son joueur (RhythmJudge), puis envoie son score
// toutes les secondes ; l'hote (ou ce PC hors ligne) lance la chanson ("go") et donne les resultats ("end").
public partial class Game
{
    public Rhythm rh;
    public RhythmView live;
    public RhythmJudge judge;
    public List<string> rhAvatars = new List<string>();
    const float LeadIn = 2.5f;   // les premieres notes ont le temps de descendre
    readonly List<(RhythmJudge j, float skill, System.Random rng, int seat)> rbots = new List<(RhythmJudge, float, System.Random, int)>();
    readonly Dictionary<int, string> lastReport = new Dictionary<int, string>();
    float reportAt, rhGoAt;
    bool rhDone, rhAuto;
    readonly System.Random autoRng = new System.Random(1);

    // Touches : gauche = fleche gauche, F, D, S, Q ; droite = fleche droite, J, K, L, M (QWERTY comme AZERTY).
    public static readonly KeyCode[][] LaneKeys =
    {
        new[] { KeyCode.LeftArrow, KeyCode.F, KeyCode.D, KeyCode.S, KeyCode.Q },
        new[] { KeyCode.RightArrow, KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.M },
    };

    int MySeatOr0 => System.Math.Max(0, mySeat);

    void StartRhythm(List<string> n, int opt, List<string> av)
    {
        rh = new Rhythm(n, opt);
        rhAvatars = av;
        live.Build(rh, av);
        judge = Spectating ? null : new RhythmJudge(rh.song);
        rbots.Clear();
        lastReport.Clear();
        rhDone = false;
        rhGoAt = Time.time;
        if (!Online)
            for (int s = 1; s < n.Count; s++)   // bots : 70 a 94 % des notes
                rbots.Add((new RhythmJudge(rh.song), 0.7f + 0.08f * (s % 4), new System.Random(s * 7919 + opt), s));
        StartCoroutine(live.Load(rh.song));
        ui.ShowHud();
        ui.Say($"{rh.song.t} !", 3);
        StartCoroutine(TvOpening());
    }

    float SongLength => live.Length > 0 ? live.Length : rh.song.end + 2;

    // Pilotage (hote ou hors ligne) : la chanson part quand les regles sont passees et la musique chargee ;
    // resultats quand tout le monde a fini (ou 10 s apres la fin, pour ceux qui ont quitte).
    public string RhythmTick(Rhythm r)
    {
        if (r.phase == LivePhase.Wait) return !busy && (live.loaded == r.song.id || Time.time - rhGoAt > 30) ? "go" : null;
        if (r.phase != LivePhase.Play) return null;
        return r.players.All(p => p.done) || Time.time - rhGoAt > LeadIn + SongLength + 10 ? "end" : null;
    }

    void ApplyRhythm(string[] p)
    {
        int seat = p.Length > 1 && int.TryParse(p[1], out int s) ? s : -1;
        int before = seat >= 0 && seat < rh.players.Count ? rh.players[seat].combo : 0;
        if (!rh.TryApply(p)) return;
        switch (p[0])
        {
            case "go":
                rhGoAt = Time.time;
                if (!catchingUp) live.Play(rh.song, LeadIn, settings.music > 0 ? Mathf.Max(0.5f, settings.music) : 0);
                live.TennaFace("Pog", 2);
                live.TennaDance("Swing");   // Tenna danse tout le long de la chanson
                break;
            case "sc":
            case "done":
                if (seat != MySeatOr0 || Online) { if (rh.players[seat].combo < before && before >= 4) live.Missed(seat); }
                break;
            case "end":
                live.TennaFace("Pog", 6);
                live.TennaDance("Silly");   // ... et se lache a la fin
                Sound.I.Play("win");
                StartCoroutine(QuizEnd());
                break;
        }
        ui.Refresh();
    }

    void Report(string kind, int seat, RhythmJudge j)
    {
        var msg = Rhythm.Report(kind, seat, j);
        if (kind == "sc" && lastReport.TryGetValue(seat, out var last) && last == msg) return;
        lastReport[seat] = msg;
        if (Online) net.Act(msg); else Apply(msg);
    }

    // Chaque image : horloge, entrees, arbitrage, bots, scores envoyes, scene animee.
    void RhythmUpdate()
    {
        if (!inGame || rh == null) return;
        live.Tick();
        live.Animate(rh);
        live.Pulse(live.SongTime, rh.song.bpm);
        if (!Online && Idle && !paused && !rh.Finished) { var tick = RhythmTick(rh); if (tick != null) Apply(tick); }
        if (rh.phase != LivePhase.Play || !live.Running) return;
        float now = live.SongTime;
        bool report = Time.time > reportAt;
        if (report) reportAt = Time.time + 1;
        bool over = now > System.Math.Min(SongLength, rh.song.end + 3);
        if (judge != null && !rhDone)
        {
            if (Focused && Input.GetKeyDown(KeyCode.UpArrow)) { RhythmView.UserOffsetMs += 10; ui.RhythmOffset(); }
            if (Focused && Input.GetKeyDown(KeyCode.DownArrow)) { RhythmView.UserOffsetMs -= 10; ui.RhythmOffset(); }
            if (rhAuto) RhythmJudge.BotStep(judge, now, 0.97f, autoRng);   // test automatique
            else if (Focused && !paused)
                for (int l = 0; l < 2; l++)
                {
                    bool down = false, held = false;
                    foreach (var k in LaneKeys[l]) { down |= Input.GetKeyDown(k); held |= Input.GetKey(k); }
                    if (down) { judge.Press(l, now); ui.RhythmKey(l); }
                    if (judge.Holding(l) && !held) judge.Release(l, now);
                }
            int combo = judge.combo;
            judge.Update(now);
            foreach (var e in judge.events)
            {
                ui.RhythmHit(e.lane, e.hit);
                if (e.hit == RhythmJudge.Hit.Miss) { live.Missed(MySeatOr0); if (combo >= 20) live.TennaFace("HmmmSketchfab", 0.8f); }
                else if (judge.combo > 0 && judge.combo % 50 == 0) live.TennaFace("Grin", 1.5f);
            }
            judge.events.Clear();
            if (over && judge.Finished) { rhDone = true; Report("done", MySeatOr0, judge); }
            else if (report) Report("sc", MySeatOr0, judge);
        }
        foreach (var b in rbots)
        {
            if (rh.players[b.seat].done) continue;
            RhythmJudge.BotStep(b.j, now, b.skill, b.rng);
            b.j.Update(now);
            b.j.events.Clear();
            if (over && b.j.Finished) Report("done", b.seat, b.j);
            else if (report) Report("sc", b.seat, b.j);
        }
    }
}
