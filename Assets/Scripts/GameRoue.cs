using System.Collections;
using System.Linq;
using UnityEngine;

// Roue de la fortune : sur son propre plateau (RoueSet, modelise d'apres l'emission). L'hote (ou le jeu hors ligne) pilote les etapes (RoueTick) ; hors ligne, des bots jouent.
public partial class Game
{
    public Roue roue;
    public RoueSet rset;
    public RoueView wview => rset ? rset.wheel : null;
    float rfPhaseAt, rfRevealAt, rfBotAt;
    public int rfLogShown;
    FPhase rfLastPhase;
    public bool rfSpinCam;
    public float RoueLeft(float ms) => Mathf.Max(0, ms / 1000f - (Time.time - rfPhaseAt));

    void StartRoue(System.Collections.Generic.List<string> n, int opt, int seed, System.Collections.Generic.List<string> av)
    {
        roue = new Roue(n, opt, seed);
        if (!rset) rset = new GameObject("PlateauRoue").AddComponent<RoueSet>();
        rset.Build(roue.players, av);
        wview.Paint(Roue.Wheels[0], false);
        rset.ShowBoard("", "LA ROUE DE LA FORTUNE", _ => true);
        rfLastPhase = roue.phase; rfPhaseAt = Time.time; rfBotAt = Time.time + 2;
        quizPhaseStart = Time.time;
        ui.ShowHud();
        StartCoroutine(TvOpening());
    }

    void RoueBoard()
    {
        var r = roue;
        if (r.answer == null) return;
        rset.ShowBoard(r.category, r.answer, r.Shown);
        rset.Plates(r);
    }

    void ApplyRoue(string[] p)
    {
        if (p[0] == "buzz" && p.Length == 2) p = new[] { "buzz", MySeatOr0.ToString(), p[1] };   // hors ligne
        if (!roue.TryApply(p)) return;
        var evs = roue.events.ToList();
        StartCoroutine(Run(RoueEvents(evs), null));
    }

    // --- Tenna presente : repliques d'animateur tele (au hasard dans chaque liste, {0} = joueur, {1} = montant/lettre) ---
    static readonly System.Collections.Generic.Dictionary<string, string[]> HostLines = new System.Collections.Generic.Dictionary<string, string[]>
    {
        ["open"] = new[] { "Bonsoir, bonsoir, BONSOIR ! Bienvenue dans... LA ROUE DE LA FORTUNE !", "Mesdames et messieurs, installez-vous, ça va tourner ! C'est LA ROUE DE LA FORTUNE !" },
        ["round"] = new[] { "Manche {1} ! On commence par une énigme rapide, les doigts sur les claviers !", "Et voici la manche {1} ! Énigme rapide, le premier qui trouve prend la main !", "Manche {1}, on repart ! Ouvrez grand les yeux !" },
        ["rapid_win"] = new[] { "{0} a trouvé ! 500 euros et la main, bravo !", "Quelle rapidité ! {0}, c'est à vous !", "{0}, formidable ! 500 euros dans la poche !" },
        ["rapid_wrong"] = new[] { "Ah non {0}, ce n'est pas ça ! Vous passez votre tour sur celle-là.", "Oh, raté {0} ! Trop pressé !" },
        ["no_winner"] = new[] { "Personne ? Vraiment personne ? On passe à la suite !", "Eh bien, elle était coriace celle-là !" },
        ["turn"] = new[] { "{0}, à vous de jouer !", "La main est à {0} !", "{0}, on vous regarde !", "Allez {0}, faites tourner !" },
        ["big"] = new[] { "{1} euros ! Oh là là, c'est une grosse case ça !", "{1} euros ! Choisissez bien votre consonne, {0} !" },
        ["jackpot"] = new[] { "LES 10 000 EUROS ! Le studio retient son souffle !", "10 000 euros sur la roue ! {0}, une consonne, et pas n'importe laquelle !" },
        ["bankrupt"] = new[] { "Aïe aïe aïe... BANQUEROUTE ! Désolé {0} !", "Oh non ! BANQUEROUTE ! Tout s'envole, {0} !", "BANQUEROUTE ! Ah, la roue est cruelle ce soir !" },
        ["pass"] = new[] { "PASSE ! Pas de chance, {0}, la main passe !", "Oh, PASSE ! On change de candidat !" },
        ["cave"] = new[] { "LA CAVERNE ! {1} euros pour {0}, et on rejoue !", "La caverne aux trésors ! {1} euros, {0} rejoue !" },
        ["many"] = new[] { "{2} fois la lettre {1} ! Magnifique !", "Oh là là, {2} « {1} » ! Le tableau s'illumine !", "{2} « {1} » ! Ça, c'est du jeu !" },
        ["one"] = new[] { "Il y a un « {1} » !", "Un « {1} », bien vu !", "Oui, un « {1} » !" },
        ["miss"] = new[] { "Pas de « {1} », désolé {0} !", "Ah non, pas de « {1} » dans cette énigme !", "Pas de « {1} »... la main passe !" },
        ["vowel"] = new[] { "{0} achète une voyelle !", "Une voyelle pour {0}, 200 euros !" },
        ["solved"] = new[] { "BRAVO {0} ! Vous gardez {1} euros !", "C'est ÇA ! {0} remporte la manche et {1} euros !", "Formidable {0} ! Applaudissez-le bien fort !" },
        ["wrong"] = new[] { "Ah non {0}, ce n'est pas la bonne réponse ! Vous ne jouez plus cette manche.", "Raté {0} ! Il fallait être sûr de soi !" },
        ["double"] = new[] { "Et maintenant, l'énigme à double sens ! {0}, qu'est-ce que c'est ? 500 euros en jeu !" },
        ["bonus_win"] = new[] { "Exactement, « {1} » ! 500 euros de plus !" },
        ["bonus_lose"] = new[] { "Dommage ! C'était « {1} » !" },
        ["final"] = new[] { "Mesdames et messieurs... C'est LA FINALE ! Chacun sa chance, chacun son enveloppe !" },
        ["final_spin"] = new[] { "{0}, faites tourner la roue des enveloppes... Qu'y a-t-il dedans ? Surprise !" },
        ["final_letters"] = new[] { "On a déjà R, S, T, L, N et E ! {0}, vingt secondes, c'est parti !" },
        ["final_win"] = new[] { "OUI ! {0} a trouvé ! Et dans l'enveloppe... {1} EUROS !", "INCROYABLE {0} ! {1} euros pour vous !" },
        ["final_lose"] = new[] { "Ah, le temps est écoulé... Dans l'enveloppe, il y avait {1} euros. Dommage {0} !" },
        ["end"] = new[] { "Merci à nos candidats ! Et bravo à {0}, le grand gagnant ! À très vite pour une nouvelle ROUE DE LA FORTUNE !" },
    };

    // Replique de Tenna : bulle tapee lettre par lettre (sa voix), geste et visage ; plan sur lui si c'est important.
    float Host(string kind, int seat = -1, object a1 = null, object a2 = null, string react = null, string face = "SmileSketchfab", bool shot = false)
    {
        if (!HostLines.TryGetValue(kind, out var lines)) return 0;
        var line = string.Format(lines[Random.Range(0, lines.Length)], seat >= 0 ? roue.players[seat].name : "", a1, a2);
        float dur = ui.HostLine(line);
        rset.HostFace(face, dur * 0.6f);
        if (react != null) rset.HostReact(react);
        if (shot && rset.HasHost) CutTo("host", -1, Mathf.Min(dur, 3.2f));
        return dur;
    }

    // --- Realisation : plans de camera coupes comme a la tele ---
    string rfShot = "wide"; int rfShotSeat = -1; float rfShotUntil, rfNextIdleCut;
    void CutTo(string shot, int seat, float dur) { rfShot = shot; rfShotSeat = seat; rfShotUntil = Time.time + dur; }
    public Pose RouePose()
    {
        if (tvCloseUp) return rset.HostPose;   // Tenna explique les regles
        if (rfSpinCam) return wview.SpinPose;
        if (Time.time < rfShotUntil)
            switch (rfShot)
            {
                case "host": return rset.HostPose;
                case "player": return rset.PlayerPose(rfShotSeat);
                case "board": return rset.BoardPose;
                case "far": return rset.FarPose;
            }
        if (roue.phase == FPhase.Rapid || roue.phase == FPhase.FinalPick || roue.phase == FPhase.FinalSolve || roue.phase == FPhase.Bonus || roue.phase == FPhase.FinalEnd || roue.phase == FPhase.RoundEnd) return rset.BoardPose;
        // Au repos : on alterne plateau rapproche, large et vue d'ensemble.
        if (Time.time > rfNextIdleCut) { rfNextIdleCut = Time.time + Random.Range(5f, 8f); rfShot = new[] { "close", "wide", "close", "far" }[Random.Range(0, 4)]; }
        return rfShot == "far" ? rset.FarPose : rfShot == "wide" ? rset.CamPose : rset.ClosePose;
    }

    IEnumerator RoueEvents(System.Collections.Generic.List<FEvent> evs)
    {
        foreach (var e in evs)
        {
            float w = 0;
            switch (e.type)
            {
                case FEv.Round:
                    wview.Paint(roue.Wheel, false);
                    if (e.text.StartsWith("Énigme rapide")) rset.HostGoTo(-1);
                    Sound.I.Play("open");
                    if (e.text.StartsWith("Énigme rapide"))
                    {
                        w = Host("round", -1, roue.round, react: "point", face: "Pog", shot: true);
                    }
                    else CutTo("board", -1, 2.5f);
                    break;
                case FEv.Reveal: Sound.I.Play("tick", 0.5f); break;
                case FEv.RapidWin:
                    rset.Anim(e.seat, "Victory"); Sound.I.Play("win");
                    // L'enigme rapide s'affiche en entier sur le tableau avant de passer a la suite.
                    rset.ShowBoard("", e.text, _ => true); CutTo("board", -1, 2.6f);
                    Host("rapid_win", e.seat, react: "good", face: "Pog");
                    yield return new WaitForSeconds(2.6f / settings.animSpeed);
                    CutTo("player", e.seat, 1.8f);
                    w = 1.2f;
                    break;
                case FEv.RapidWrong: rset.Anim(e.seat, "RecieveHit"); Sound.I.Play("lose", 0.5f); Host("rapid_wrong", e.seat, react: "bad", face: "HmmmSketchfab"); break;
                case FEv.NoWinner: w = Host("no_winner", face: "HmmmSketchfab", shot: true); break;
                case FEv.Spin:
                case FEv.FinalSpin:
                    rfSpinCam = true;
                    if (e.type == FEv.FinalSpin) wview.Paint(Roue.Envelopes, true);
                    yield return wview.Spin(e.segment, settings.animSpeed);
                    yield return new WaitForSeconds(0.5f);
                    rfSpinCam = false;
                    if (e.type == FEv.FinalSpin) w = Host("final_spin", e.seat, face: "Pog");
                    else if (e.amount == Roue.Jackpot) w = Host("jackpot", e.seat, react: "win", face: "Pog", shot: true);
                    else if (e.amount >= 700) w = Host("big", e.seat, e.amount, react: "point", face: "Pog");
                    else if (e.amount >= 0) ui.Say($"{e.amount} € ! Une consonne ?", 2);
                    if (e.amount >= 0 && e.type == FEv.Spin) CutTo("board", -1, 30);   // on regarde le tableau pendant le choix
                    break;
                case FEv.Bankrupt: rset.Anim(e.seat, "RecieveHit"); Sound.I.Play("lose"); CutTo("player", e.seat, 2); w = Host("bankrupt", e.seat, react: "bad", face: "HmmmSketchfab"); break;
                case FEv.Pass: Sound.I.Play("lose", 0.6f); w = Host("pass", e.seat, face: "HmmmSketchfab"); break;
                case FEv.Cave: Sound.I.Play("bj_chips"); w = Host("cave", e.seat, e.amount, react: "good", face: "Pog"); break;
                case FEv.Vowel: Sound.I.Play("bj_chip1"); Host("vowel", e.seat); CutTo("board", -1, 30); break;
                case FEv.Letter:
                    CutTo("board", -1, 1.2f + e.count * 0.4f);
                    for (int i = 0; i < roue.answer.Length; i++)
                        if (roue.answer[i] == e.letter) { Sound.I.Play("tick", 0.8f, 0.05f); RoueBoard(); yield return new WaitForSeconds(0.35f / settings.animSpeed); }
                    if (e.count >= 3) Host("many", e.seat, e.letter, e.count, react: "good", face: "Pog");
                    else if (Random.value < 0.45f) Host("one", e.seat, e.letter);
                    if (e.amount > 0) ui.Say($"+{e.amount} €", 1.5f);
                    break;
                case FEv.Miss: Sound.I.Play("lose", 0.5f); w = Host("miss", e.seat, e.letter, face: "HmmmSketchfab") * 0.5f; break;
                case FEv.Solved:
                    rset.Anim(e.seat, "Victory"); Sound.I.Play("win");
                    RoueBoard(); CutTo("player", e.seat, 2.5f);
                    w = Host("solved", e.seat, e.amount, react: "good", face: "Pog");
                    break;
                case FEv.WrongSolve: rset.Anim(e.seat, "RecieveHit"); Sound.I.Play("lose"); w = Host("wrong", e.seat, react: "bad", face: "HmmmSketchfab", shot: true); break;
                case FEv.Bonus:
                    if (roue.phase == FPhase.Bonus) w = Host("double", e.seat, react: "point", face: "Pog", shot: true);
                    else w = Host(e.amount > 0 ? "bonus_win" : "bonus_lose", e.seat, e.text, react: e.amount > 0 ? "good" : null, face: e.amount > 0 ? "Pog" : "HmmmSketchfab");
                    break;
                case FEv.Final: rset.HostGoTo(-1); Sound.I.Play("win"); CutTo("far", -1, 2.5f); yield return new WaitForSeconds(1); w = Host("final", react: "win", face: "Pog", shot: true); break;
                case FEv.FinalLetters: RoueBoard(); Sound.I.Play("open"); w = Host("final_letters", e.seat, face: "Pog") * 0.5f; break;
                case FEv.FinalWin: rset.Anim(e.seat, "Victory"); Sound.I.Play("win"); CutTo("player", e.seat, 2.5f); w = Host("final_win", e.seat, e.amount, react: "win", face: "Pog"); break;
                case FEv.FinalLose: Sound.I.Play("lose"); w = Host("final_lose", e.seat, e.amount, face: "HmmmSketchfab", shot: true); break;
                case FEv.Turn:
                    if (e.seat == MySeatOr0 && !Spectating) Sound.I.Play("open", 0.7f);
                    if (roue.phase == FPhase.Spin || roue.phase == FPhase.FinalSpin) { rset.HostGoTo(e.seat); CutTo("player", e.seat, 2.2f); if (Random.value < 0.6f) Host("turn", e.seat); }
                    break;
                case FEv.Over:
                {
                    var best = roue.players.OrderByDescending(p => p.score).First();
                    CutTo("far", -1, 3); Host("end", best.seat, react: "win", face: "Pog");
                    StartCoroutine(RoueEnd());
                    break;
                }
            }
            RoueBoard();
            ui.Refresh();
            if (w > 0) yield return new WaitForSeconds(Mathf.Min(w, 3.5f) / settings.animSpeed);
        }
        rfLogShown = roue.log.Count;   // le journal ne montre une action qu'une fois jouee a l'ecran (pas de resultat avant la fin de la roue)
        ui.Refresh();
        if (roue.phase != rfLastPhase) { rfLastPhase = roue.phase; rfPhaseAt = Time.time; rfRevealAt = Time.time + 1.5f; }
        rfBotAt = Time.time + Random.Range(1.2f, 2.2f) / settings.animSpeed;
    }

    IEnumerator RoueEnd()
    {
        var best = roue.players.Max(pl => pl.score);
        foreach (var pl in roue.players.Where(pl => pl.score == best)) rset.Anim(pl.seat, "Victory");
        yield return new WaitForSeconds(6);
        ui.ShowVictory();
    }

    // Pilotage des etapes (hote ou hors ligne).
    public string RoueTick(Roue r)
    {
        if (busy) return null;
        float t = Time.time - rfPhaseAt;
        switch (r.phase)
        {
            case FPhase.Intro: return t > 2 ? "next" : null;
            case FPhase.Rapid: if (Time.time > rfRevealAt) { rfRevealAt = Time.time + 1.3f; return "reveal"; } return null;
            case FPhase.Bonus: return t * 1000 > Roue.BonusMs ? "bonus|" : null;
            case FPhase.FinalSolve: return t * 1000 > Roue.FinalMs ? "fsolve|" : null;
            case FPhase.RoundEnd: case FPhase.FinalEnd: return t > 6 ? "next" : null;
        }
        return null;
    }

    void RoueUpdate()
    {
        if (roue == null) return;
        if (!Online && Idle && !paused && !roue.Finished && !tvCloseUp)
        {
            var tick = RoueTick(roue);
            if (tick != null) { Apply(tick); return; }
            // Bots : l'enigme rapide (buzz quand ils la devinent), puis leur tour.
            if (roue.phase == FPhase.Rapid && Time.time > rfBotAt)
            {
                rfBotAt = Time.time + 0.9f;
                foreach (var pl in roue.players.Where(pl => pl.seat != MySeatOr0 && !roue.outs.Contains(pl.seat)))
                    if (roue.BotKnows(-0.15f) && Random.value < 0.25f) { Apply($"buzz|{pl.seat}|{roue.answer}"); return; }
            }
            int actor = roue.Actor;
            if (actor >= 0 && actor != MySeatOr0 && Time.time > rfBotAt)
            {
                if (roue.phase == FPhase.FinalSolve)
                {
                    if (Time.time - rfPhaseAt > 4 && roue.BotKnows(0.2f)) Apply("fsolve|" + roue.answer);
                    rfBotAt = Time.time + 1.5f;
                    return;
                }
                var a = roue.Bot();
                if (a != null) Apply(string.Join("|", a));
            }
        }
        // Lettres au clavier (pendant mon choix de consonne).
        if (Focused && CanAct && roue.phase == FPhase.Letter && !ui.TypingText)
            foreach (char ch in Input.inputString.ToUpperInvariant())
                if (Roue.Consonants.IndexOf(ch) >= 0 && !roue.used.Contains(ch)) { Act("cons|" + ch); break; }
    }

    // Autotest : moi (joue par la strategie du bot) contre 3 bots, 2 manches puis la finale.
    IEnumerator RoueTest(string dir, System.Func<string, IEnumerator> shot)
    {
        bool rapidShot = false, spinShot = false, letterShot = false, pickShot = false, bonusShot = false, hostShot = false, playerShot = false, farShot = false;
        while (!roue.Finished)
        {
            if (!hostShot && rfShot == "host" && Time.time < rfShotUntil) { hostShot = true; yield return new WaitForSeconds(1.2f); yield return shot("g1-tenna"); }
            if (!playerShot && rfShot == "player" && Time.time < rfShotUntil) { playerShot = true; yield return new WaitForSeconds(0.5f); yield return shot("g2-joueur"); }
            if (!farShot && rfShot == "far" && !rfSpinCam && roue.phase == FPhase.Spin) { farShot = true; yield return shot("g3-large"); }
            if (!rapidShot && roue.phase == FPhase.Rapid && roue.shownCells.Count >= 4) { rapidShot = true; yield return shot("f2-rapide"); }
            if (!spinShot && rfSpinCam && wview.spinning) { spinShot = true; yield return new WaitForSeconds(1.2f); yield return shot("f3-roue"); }
            if (Idle && roue.Actor == MySeatOr0)
            {
                if (!letterShot && roue.phase == FPhase.Letter) { letterShot = true; ui.Refresh(); yield return new WaitForSeconds(0.4f); yield return shot("f4-consonne"); }
                if (!pickShot && roue.phase == FPhase.FinalPick) { pickShot = true; ui.Refresh(); yield return new WaitForSeconds(0.4f); yield return shot("f6-finale"); }
                if (!bonusShot && roue.phase == FPhase.Bonus) { bonusShot = true; ui.Refresh(); yield return new WaitForSeconds(0.4f); yield return shot("f5-double-sens"); }
                if (roue.phase == FPhase.FinalSolve) { yield return new WaitForSeconds(3); Apply("fsolve|" + roue.answer); }
                else { var a = roue.Bot(); if (a != null) Apply(string.Join("|", a)); }
                yield return new WaitForSeconds(0.3f);
            }
            if (roue.phase == FPhase.Rapid && Idle && roue.BotKnows(-0.1f) && Random.value < 0.02f) Apply($"buzz|{MySeatOr0}|{roue.answer}");
            yield return null;
        }
        yield return new WaitForSeconds(1); yield return shot("f7-fin");
        yield return new WaitForSeconds(4); yield return shot("f8-victoire");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "roue.txt"), string.Join("\n", roue.log));
    }
}
