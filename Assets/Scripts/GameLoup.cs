using System.Collections;
using System.Linq;
using UnityEngine;

// Loup-garou : assis autour du feu dans la clairiere, vue a la premiere personne (la table de Qui suis-je).
// L'hote (ou le jeu hors ligne) coupe chaque etape au bout du temps imparti ; hors ligne, des bots jouent les autres.
public partial class Game
{
    public LoupGarou wg;
    float wgPhaseAt, wgBotAt;
    WPh wgLastPhase;
    int wgLastStep;
    public static float WgTime(WPh ph) => ph switch
    {
        WPh.Night => 50, WPh.Witch => 30, WPh.Dawn => 7, WPh.Verdict => 7, WPh.Election => 60, WPh.Vote => 150,
        WPh.Hunter => 30, WPh.Heir => 30, WPh.Dictator => 30, WPh.Tie => 30, _ => 0,
    };
    public static float WgTime(LoupGarou l) => l.phase == WPh.Night ? l.StepTime : l.phase == WPh.Tie ? 30 : WgTime(l.phase);
    public float WgLeft => wg == null ? 0 : Mathf.Max(0, WgTime(wg) - (Time.time - wgPhaseAt));

    void StartLoup(System.Collections.Generic.List<string> n, int opt, int seed, System.Collections.Generic.List<string> av)
    {
        wg = new LoupGarou(n, opt, seed);
        wgHunter = -1; wgTiedSeats.Clear(); wgRevealed.Clear(); wgDeaths.Clear(); wgSeqBusy = wgAwaitHunter = false;
        qsview.BuildPlain(n.Count, MySeatOr0, av);
        int map = AgrouMap.Resolve(LoupGarou.MapOf(opt), seed);
        Clairiere.Nature(map == 0);
        AgrouMap.Show(map, Clairiere.Center + new Vector3(0, 0, 0.5f));
        wgPhaseAt = Time.time + 6; wgLastPhase = wg.phase; wgBotAt = Time.time + 7;   // 6 s pour decouvrir sa carte
        ui.ShowHud();
        ui.LoupStart();
    }

    void ApplyLoup(string[] p)
    {
        if (!wg.TryApply(p)) return;
        int me = MySeatOr0;
        WgActSound(p, me);
        if (p[0] == "hunt" && p.Length > 2 && int.TryParse(p[2], out int shot) && int.Parse(p[1]) == wgHunter) { wgHuntAim = shot; wgHunterKillAt = Time.time + 4.6f; }   // le temps de voir sa victime tomber et son role
        foreach (var e in wg.events) wgEvLog.Add($"{e.type} siege={e.seat} {e.text}");
        foreach (var e in wg.events)
        {
            switch (e.type)
            {
                case WGEv.Night: wgFadeUntil = Time.time + 3.5f; Sound.I.Play("wg_gong", 0.6f); Sound.I.Music("wg_nuitmusique"); break;   // "Le village dort..." suffit
                case WGEv.Day: Sound.I.Play("wg_coqjour", 0.6f); Sound.I.Music("wg_ambiancejourforet"); ui.Say(e.text + " : le village se réveille", 2.5f); break;
                case WGEv.Death:
                    // Mise en scene (WgDeathSeq) : camera sur le mort, suspense, chute, revelation. La victime du chasseur tombe aussitot.
                    if (p[0] == "hunt") StartCoroutine(WgQuickDeath(e.seat, e.text));
                    else { wgDeaths.Enqueue((e.seat, e.text)); if (!wgSeqBusy) StartCoroutine(WgDeathSeq()); } if (e.seat == me && !Spectating) ui.LoupNote("Tu es mort... Tu peux maintenant voir tous les rôles et parler aux autres morts."); break;
                case WGEv.Mayor: Sound.I.Play("wg_maireelu", 0.7f, 0); ui.Say($"{wg.players[e.seat].name} est maire !", 2.5f); break;
                case WGEv.Seen: case WGEv.Info: if (e.seat == me && !Spectating) ui.LoupNote(e.text); break;
                case WGEv.Saved: break;
                case WGEv.Chat: Sound.I.UI("tick"); break;
                case WGEv.Step: if (!Spectating && wg.NightRole(wg.players[me])) Sound.I.Play("wg_gong", 0.5f, 0); break;
                case WGEv.Tie:
                {
                    // Les ex aequo montent a la potence (deux cordes, comme dans Agrou) pendant le revote.
                    wgTiedSeats.Clear(); wgTiedSeats.AddRange(wg.tied);
                    var mayorT = wg.players.FirstOrDefault(x => x.mayor && x.alive);
                    qsview.Gallows(wg.tied.Take(2).ToList(), qsview.SeatWorld(mayorT != null ? mayorT.seat : me));   // la camera reste sur la potence jusqu'a la pendaison
                    Sound.I.Play("wg_battementcoeur", 0.8f, 0);
                    ui.Say("Égalité ! Nouveau vote", 2.5f);
                    break;
                }
                case WGEv.Left: qsview.Leave(e.seat); wgRevealed.Add(e.seat); ui.Say($"{wg.players[e.seat].name} a quitté la partie", 2.5f); break;
                case WGEv.Over: Sound.I.Play(wg.winner == "L'Ange" ? "wg_angegagne" : wg.winners.Contains(me) ? "wg_victoire" : "wg_defaite", 0.9f, 0); StartCoroutine(LoupEnd()); break;
            }
        }
        if (wgLastPhase == WPh.Tie && wg.phase != WPh.Tie && wg.eliminated < 0) { Sound.I.StopLong(); foreach (var x in wgTiedSeats) qsview.Release(x); qsview.EndCinematic(); qsview.EndGallows(); wgTiedSeats.Clear(); }
        if (wg.phase != wgLastPhase || wg.stepSerial != wgLastStep) { wgLastPhase = wg.phase; wgLastStep = wg.stepSerial; wgPhaseAt = Time.time; wgBotAt = Time.time + Random.Range(2f, 4f); ui.LoupPhase(); }
        ui.Refresh();
    }

    // Sons d'Agrou : l'action d'un role ne s'entend que chez son auteur (la nuit reste secrete), les morts chez tout le monde.
    void WgActSound(string[] p, int me)
    {
        if (p.Length < 2 || Spectating || p[1] != me.ToString()) return;
        string n = p[0] switch
        {
            "night" => p[2] switch
            {
                "wolf" => "wg_loupson", "see" => "wg_voyantecible", "guard" => "wg_gardeson", "love" => "wg_tirerfleche", "crow" => "wg_tourcorbeau",
                "white" => "wg_loupblanctue", "sleep" => "wg_anesthesisteaction", "convert" => "wg_loupnoirconvertis", "fog" => "wg_brume",
                "coup" => "wg_dictateurnuit", "douse" => "wg_jethuile", "ignite" => "wg_choixbruler", "kill" => "wg_tourassassin", _ => null,
            },
            "witch" => p[2] == "save" ? "wg_potiondesoinsorciere" : p[2] == "kill" ? "wg_casserpotion" : null,
            "shuriken" => "wg_shurikenninja",
            _ => null,
        };
        if (n != null) Sound.I.Play(n, 0.8f, 0);
    }
    static string WgDeathSound(string why) =>
        why.Contains("village") ? "wg_pendu" : why.Contains("Chasseur") ? "wg_tirchasseur" : why.Contains("Pyromane") ? "wg_lesgensbrulent"
        : why.Contains("bombe") ? "wg_bombeexplosion" : why.Contains("amour") ? "wg_battementcoeur" : why.Contains("loup") || why == "dévoré" ? "wg_loupson" : "lose";

    // La nuit, tout le monde ferme les yeux sauf ceux dont c'est le tour. Pour ne rien devoiler, je ne vois ouverts
    // que mes propres yeux et ceux de mes partenaires (les loups entre eux) ; mort ou spectateur, je vois tout.
    float[] wgEye;
    int wgHunter = -1, wgHuntAim = -2;
    float wgHunterKillAt = float.MaxValue;
    bool wgSeeAll, wgTestDay;
    (int, int) wgTestPoint = (-1, -1);   // autotest : voir tous les loups, en plein jour
    void WgEyes()
    {
        int n = wg.players.Count, me = MySeatOr0;
        if (wgHunter >= 0)
        {
            // Son tir n'est plus attendu (temps ecoule, ou partie finie) : il tombe. Avant la phase de tir (mort la nuit), on attend.
            if (!wg.pending.Any(x => x.kind == WPh.Hunter && x.seat == wgHunter) && wgHunterKillAt == float.MaxValue) wgHunterKillAt = Time.time + 0.5f;
            if (Time.time > wgHunterKillAt) wgHunter = -1;   // la sequence (WgDeathSeq) le fait tomber
        }
        if (wgEye == null || wgEye.Length != n) wgEye = new float[n];
        var mine = wg.players[me];
        bool all = Spectating || !mine.alive || wg.Finished || wgSeeAll;
        for (int s = 0; s < n; s++)
        {
            var p = wg.players[s];
            bool awake = wg.phase == WPh.Night ? wg.NightRole(p) && Time.time >= wgFadeUntil : wg.phase == WPh.Witch ? p.role == Role.Sorciere : true;
            bool spyOn = mine.alive && mine.role == Role.PetiteFille && wg.phase == WPh.Night && wg.step == LoupGarou.WolfStep && Time.time >= wgFadeUntil;   // la Petite fille epie
            bool visible = all || s == me || LoupGarou.IsWolf(p.role) && (LoupGarou.IsWolf(mine.role) && wg.phase == WPh.Night && wg.NightRole(mine) || spyOn);   // loups reveilles en meme temps que moi, ou epies
            bool closed = !WgShownAlive(s) || !(awake && (visible || wg.phase != WPh.Night && wg.phase != WPh.Witch));
            // Tour des loups : ils prennent leur forme de loup-garou (vue par les memes yeux que ci-dessus).
            qsview.SetWolf(s, wg.phase == WPh.Night && awake && LoupGarou.IsWolf(p.role) && visible ? WolfSkin(p.role) : null);
            // Les autres roles sortent leur accessoire pendant leur tour (memes regles de visibilite).
            var pr = wgProps.TryGetValue(p.role, out var x) && p.alive && !closed && (wg.phase == WPh.Night || wg.phase == WPh.Witch) && awake ? x : default;
            if (s == wgHunter) qsview.SetProp(s, "Fusil", HumanBodyBones.RightHand, Vector3.zero, 1.1f, true, true);
            else qsview.SetProp(s, pr.model, pr.bone, pr.off, pr.size, pr.up);
            // Doigt pointe : votes du jour (sauf brouillard), votes des loups la nuit (vus par les loups), fusil du chasseur.
            int pt = -1;
            if (p.alive && !wg.fog && (wg.phase == WPh.Vote || wg.phase == WPh.Election || wg.phase == WPh.Tie) && wg.votes.TryGetValue(s, out var vt)) pt = vt;
            if (p.alive && wg.phase == WPh.Night && wg.step == LoupGarou.WolfStep && awake && LoupGarou.IsWolf(p.role) && visible && wg.wolfVotes.TryGetValue(s, out var wv)) pt = wv;
            if (s == wgHunter) pt = wgHuntAim;
            if (s == me && pt < 0 && p.alive && !Spectating) pt = ui.WgFirstSel;   // je pointe celui que je choisis
            if (s == wgTestPoint.Item1) pt = wgTestPoint.Item2;
            qsview.Point(s, pt);
            // Gestes d'Agrou : pointer, viser/tirer, dormir, sortir l'arc, la bombe, le sort de soin.
            string g = null;
            bool nightNow = wg.phase == WPh.Night || wg.phase == WPh.Witch;
            if (s == wgHunter) g = wgHuntAim >= 0 ? "Firing_Rifle_Anim_mixamo_com" : "Idle_Aiming_Anim_mixamo_com";
            else if (!p.alive) g = null;
            else if (pt >= 0) g = "Pointe";
            else if (nightNow && closed) g = "Sit_Asleep_anim";
            else if (nightNow && awake && !closed)
                g = p.role switch { Role.Cupidon => "aim_Bow_Anim_mixamo_com", Role.Blaster => "PorteBombe", Role.Sorciere => "Magic_Heal_Anim_mixamo_com", _ => null };
            qsview.Gesture(s, g);
            float t = Mathf.MoveTowards(wgEye[s], closed ? 1 : 0, Time.deltaTime * 3);
            if (t != wgEye[s]) { wgEye[s] = t; qsview.SetEyes(s, t); }
        }
    }

    // Accessoires d'Agrou par role : modele, os, decalage (repere du perso), taille, axe long vertical (sinon horizontal).
    static readonly System.Collections.Generic.Dictionary<Role, (string model, HumanBodyBones bone, Vector3 off, float size, bool up)> wgProps =
        new System.Collections.Generic.Dictionary<Role, (string, HumanBodyBones, Vector3, float, bool)>
    {
        [Role.Ange] = ("Angel_Wings_01", HumanBodyBones.UpperChest, new Vector3(0, 0.05f, -0.2f), 1.1f, false),
        [Role.Cupidon] = ("arcArcher", HumanBodyBones.LeftHand, new Vector3(0, 0.05f, 0.03f), 0.8f, true),
        [Role.Sorciere] = ("S_Flask", HumanBodyBones.RightHand, new Vector3(0, 0.08f, 0.04f), 0.2f, true),
        [Role.Assassin] = ("SM_Wep_Dagger_01", HumanBodyBones.RightHand, new Vector3(0, 0.1f, 0.03f), 0.35f, true),
        [Role.Chasseur] = ("Fusil", HumanBodyBones.RightHand, new Vector3(0, 0.3f, 0.04f), 1.1f, true),
        [Role.Ninja] = ("SM_Wep_Shuriken_01", HumanBodyBones.RightHand, new Vector3(0, 0.07f, 0.04f), 0.18f, true),
        [Role.Blaster] = ("SM_Item_Bomb_01", HumanBodyBones.RightHand, new Vector3(0, 0.08f, 0.04f), 0.22f, true),
        [Role.Voyante] = ("SM_Prop_Crystal_Ball_01", HumanBodyBones.Hips, new Vector3(0, 0.12f, 0.28f), 0.3f, true),
        [Role.Pyromane] = ("SM_Prop_GasCan_01", HumanBodyBones.RightHand, new Vector3(0, 0.05f, 0.05f), 0.35f, true),
        [Role.Garde] = ("SM_Wep_Shield_01", HumanBodyBones.LeftLowerArm, new Vector3(-0.08f, 0, 0.06f), 0.55f, true),
        [Role.Dictateur] = ("SM_Prop_Sceptre_01", HumanBodyBones.RightHand, new Vector3(0, 0.25f, 0.03f), 0.8f, true),
        [Role.Influenceur] = ("SM_Prop_SmartPhone_01", HumanBodyBones.RightHand, new Vector3(0, 0.06f, 0.05f), 0.15f, true),
        [Role.Necromancien] = ("SM_Item_Skull_01", HumanBodyBones.RightHand, new Vector3(0, 0.1f, 0.05f), 0.2f, true),
        [Role.Medium] = ("SM_Item_Candle_01", HumanBodyBones.RightHand, new Vector3(0, 0.1f, 0.05f), 0.22f, true),
        [Role.Corbeau] = ("SK_Raven_PolyArt", HumanBodyBones.LeftUpperArm, new Vector3(0, 0.15f, 0), 0.4f, false),
    };

    // Pelage par role : la palette du loup d'Agrou (3 gris + ventre beige) recoloree.
    readonly System.Collections.Generic.Dictionary<Role, Texture2D> wolfSkins = new System.Collections.Generic.Dictionary<Role, Texture2D>();
    Texture2D WolfSkin(Role r)
    {
        if (wolfSkins.TryGetValue(r, out var t)) return t;
        var src = Resources.Load<Texture2D>("LoupGarou/Models/werewolf_texture");
        string[] fur = r switch
        {
            Role.LoupNoir => new[] { "3a363f", "2a272e", "19171c", "5c5263" },
            Role.LoupBlanc => new[] { "f4f3ef", "dcdcd6", "c2c2bc", "fffaf0" },
            Role.Brumeux => new[] { "9fb0c9", "7f90ab", "5f6d88", "c8d3e6" },
            Role.Anesthesiste => new[] { "8fb3a6", "6f9385", "4e6e62", "e6efe9" },
            _ => null,
        };
        if (!src || fur == null) return wolfSkins[r] = src;
        Color32[] from = { new Color32(137, 136, 137, 255), new Color32(112, 111, 112, 255), new Color32(79, 79, 79, 255), new Color32(209, 191, 153, 255) };
        var px = src.GetPixels32();
        for (int i = 0; i < px.Length; i++)
            for (int k = 0; k < from.Length; k++)
                if (px[i].r == from[k].r && px[i].g == from[k].g && px[i].b == from[k].b) { px[i] = Board.Hex(fur[k]); break; }
        t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        t.SetPixels32(px); t.Apply();
        return wolfSkins[r] = t;
    }

    // Profondeur de champ du cercle (comme Agrou) : net jusqu'aux joueurs d'en face, flou au-dela.
    UnityEngine.Rendering.Volume wgVol;
    void WgDof()
    {
        wgVol = new GameObject("Flou loup-garou").AddComponent<UnityEngine.Rendering.Volume>();
        wgVol.isGlobal = true; wgVol.priority = 10;
        wgVol.profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        var d = wgVol.profile.Add<UnityEngine.Rendering.Universal.DepthOfField>(true);
        d.mode.value = UnityEngine.Rendering.Universal.DepthOfFieldMode.Gaussian;
        d.gaussianStart.value = 9; d.gaussianEnd.value = 40; d.gaussianMaxRadius.value = 1.1f; d.highQualitySampling.value = true;
    }

    // Morts deja montres (revelation faite) : le HUD ne les compte morts qu'a ce moment-la.
    readonly System.Collections.Generic.HashSet<int> wgRevealed = new System.Collections.Generic.HashSet<int>();
    public bool WgShownAlive(int seat) => wg == null || seat < 0 || seat >= wg.players.Count || wg.players[seat].alive || !wgRevealed.Contains(seat) && !wg.Finished;
    readonly System.Collections.Generic.List<int> wgTiedSeats = new System.Collections.Generic.List<int>();
    readonly System.Collections.Generic.Queue<(int seat, string why)> wgDeaths = new System.Collections.Generic.Queue<(int, string)>();
    bool wgSeqBusy, wgAwaitHunter;
    // Bots et fin d'etape attendent la fin de la mise en scene (sauf le tir du chasseur, attendu pendant).
    bool WgHold => wgSeqBusy && !wgAwaitHunter || Time.time < wgFadeUntil;   // mise en scene, ou la nuit qui tombe
    float wgFadeUntil;
    readonly System.Collections.Generic.List<string> wgEvLog = new System.Collections.Generic.List<string>();   // fondu de la tombee de la nuit : personne ne se reveille avant le noir

    IEnumerator WgDeathSeq()
    {
        wgSeqBusy = true;
        int me = MySeatOr0;
        while (wgDeaths.Count > 0 && wg != null)
        {
            var (s, why) = wgDeaths.Dequeue();
            if (why.Contains("village"))
            {
                // Pendaison (Agrou) : le condamne, et son amoureux s'il meurt de chagrin, montent a la potence face au maire.
                var condemned = new System.Collections.Generic.List<int> { s };
                if (wgDeaths.Count > 0 && wgDeaths.Peek().why.Contains("amour")) condemned.Add(wgDeaths.Dequeue().seat);
                var mayor = wg.players.FirstOrDefault(x => x.mayor && x.alive);
                var faceTo = qsview.SeatWorld(mayor != null ? mayor.seat : me);
                bool up = condemned.All(qsview.OnGallows);
                if (up) { foreach (var x in wgTiedSeats) if (!condemned.Contains(x)) qsview.Release(x); qsview.GallowsCamera(); }   // egalite : l'epargne redescend
                if (up || qsview.Gallows(condemned, faceTo))
                {
                    Sound.I.Play("wg_gong", 0.7f, 0);
                    Sound.I.Play("wg_battementcoeur", 0.9f, 0);
                    yield return new WaitForSeconds(3.2f);   // suspense sur l'echafaud
                    if (wg == null) break;
                    qsview.Drop();
                    Sound.I.StopLong();
                    Sound.I.Play("wg_pendu", 0.9f, 0);
                    yield return new WaitForSeconds(1.6f);
                    foreach (var c in condemned) { wgRevealed.Add(c); ui.LoupReveal(c); ui.Refresh(); yield return new WaitForSeconds(3.4f); }
                    qsview.EndCinematic();
                    yield return new WaitForSeconds(0.4f);
                    qsview.EndGallows();
                    continue;
                }
            }
            qsview.Cinematic(s);
            Sound.I.Play("wg_gong", 0.7f, 0);   // Agrou : gong au passage sur la camera du mort
            Sound.I.Play("wg_battementcoeur", 0.9f, 0);
            yield return new WaitForSeconds(2.6f);   // suspense
            if (wg == null) break;
            if (wg.pending.Any(x => x.kind == WPh.Hunter && x.seat == s))
            {
                // Le chasseur epaule et tire ; il ne tombe qu'apres son coup (ou si le temps s'ecoule).
                // Il prend son fusil ; retour a la vue FPS : on regarde autour de soi qui il va viser.
                wgHunter = s; wgHuntAim = -2; wgHunterKillAt = float.MaxValue; wgAwaitHunter = true;
                qsview.EndCinematic();
                while (wgHunter >= 0 && wg != null) yield return null;
                wgAwaitHunter = false;
                if (wg == null) break;
                qsview.Cinematic(s);   // puis le chasseur s'effondre a son tour
                Sound.I.Play("wg_gong", 0.7f, 0);
                yield return new WaitForSeconds(0.6f);
                qsview.SetAlive(s, false);
                Sound.I.Play(WgDeathSound(why), 0.8f, 0);
                yield return new WaitForSeconds(1.2f);
                wgRevealed.Add(s); ui.LoupReveal(s); ui.Refresh();
                yield return new WaitForSeconds(3.2f);
                continue;
            }
            qsview.SetAlive(s, false);
            Sound.I.Play(WgDeathSound(why), 0.8f, 0);
            yield return new WaitForSeconds(1.2f);
            wgRevealed.Add(s); ui.LoupReveal(s); ui.Refresh();
            yield return new WaitForSeconds(3.2f);
        }
        qsview.EndCinematic();
        Sound.I.StopLong();
        wgSeqBusy = false;
    }

    IEnumerator WgQuickDeath(int s, string why)
    {
        qsview.Cinematic(s);
        qsview.SetAlive(s, false);
        Sound.I.Play(WgDeathSound(why), 0.8f, 0);
        yield return new WaitForSeconds(1.2f);
        wgRevealed.Add(s); ui.LoupReveal(s); ui.Refresh();
        yield return new WaitForSeconds(2.5f);
        if (!wgSeqBusy) { qsview.EndCinematic(); Sound.I.StopLong(); }   // sinon la sequence en cours reprend la main
    }

    IEnumerator LoupEnd() { yield return new WaitForSeconds(4); ui.ShowVictory(); }

    // Hote / hors ligne : la fin de chaque etape.
    public string LoupTick(LoupGarou l)
    {
        if (WgHold) { wgPhaseAt += Time.deltaTime; return null; }   // l'etape ne court pas pendant la mise en scene
        float t = Time.time - wgPhaseAt, max = WgTime(l);
        if (max <= 0 || t < max) return null;
        return l.phase == WPh.Dawn || l.phase == WPh.Verdict ? "next" : "timeout";
    }

    void LoupUpdate()
    {
        if (wgVol) wgVol.enabled = wg != null && !paused;
        if (wg == null) return;
        if (!wgVol) WgDof();
        // La Petite fille epie les loups : pendant leur tour, la nuit est moins noire pour elle.
        bool spy = wg.phase == WPh.Night && wg.step == LoupGarou.WolfStep && !Spectating && wg.players[MySeatOr0].alive && wg.players[MySeatOr0].role == Role.PetiteFille;
        Clairiere.Darkness = (wg.phase == WPh.Night || wg.phase == WPh.Witch) && !wgTestDay ? (spy ? 0.45f : 1) : 0;
        WgEyes();
        if (!Online && Idle && !paused && !wg.Finished)
        {
            var tick = LoupTick(wg);
            if (tick != null) { Apply(tick); return; }
            if (Time.time > wgBotAt && !WgHold)
            {
                wgBotAt = Time.time + Random.Range(0.8f, 2f);
                foreach (var p in wg.players.Where(p => p.seat != MySeatOr0).OrderBy(_ => Random.value))
                {
                    var a = wg.BotFor(p.seat);
                    if (a != null) { Apply(string.Join("|", a)); return; }
                }
            }
        }
        int hov = Focused && !paused && !ui.OverUi(Input.mousePosition) ? qsview.HeadUnder(cam.ScreenPointToRay(Input.mousePosition)) : -1;
        for (int s = 0; s < wg.players.Count; s++) qsview.Outline(s, ui.WgChosen(s) ? 2 : s == hov && ui.WgClickable(s) ? 1 : 0);
        if (!Focused || paused) return;
        if (Input.GetMouseButton(1)) qsview.Look(Input.GetAxis("Mouse X") * 3 * settings.camSens, Input.GetAxis("Mouse Y") * 3 * settings.camSens);
        if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f && !ui.OverUi(Input.mousePosition)) qsview.Zoom(Input.mouseScrollDelta.y);
        if (Input.GetMouseButtonDown(0) && (!ui.OverUi(Input.mousePosition) || ui.OverLoupNotes(Input.mousePosition)))   // le carnet laisse passer les clics
        {
            int s = qsview.HeadUnder(cam.ScreenPointToRay(Input.mousePosition));
            if (s >= 0) ui.LoupClick(s);
        }
    }

    // Autotest d'un role : moi (eux=false) ou un adversaire (eux=true, moi villageois) a ce role. Journal de ce que montre
    // l'interface a chaque etape, captures pendant le tour du role ; deux nuits et deux jours au plus.
    IEnumerator LoupRoleTest(string dir, System.Func<string, IEnumerator> shot, Role role, bool them)
    {
        int me = MySeatOr0, owner = them ? (me + 1) % wg.players.Count : me;
        wg.TestSetRole(owner, role);
        if (role != Role.Chasseur) wg.testShield = owner;   // le chasseur doit pouvoir mourir (son tir)
        if (them) wg.TestSetRole(me, Role.Villageois);
        ui.LoupStart();
        var log = new System.Text.StringBuilder($"role {role} chez {(them ? "un adversaire (" + wg.players[owner].name + ")" : "moi")}\n");
        string Who() => string.Join(", ", wg.players.Select(p => $"{p.name}={LoupGarou.Name(p.role)}{(p.alive ? "" : "+")}"));
        log.AppendLine(Who());
        wgBotAt = Time.time + 1000;   // les adversaires attendent que le journal du test demarre
        string last = null; int shots = 0, n = 0, votedShots = 0;
        while (!wg.Finished && wg.night <= 3 && n < 4000)
        {
            n++;
            string key = $"{wg.phase}/{wg.step}/{wg.night}/{wg.day}";
            if (key != last)
            {
                last = key;
                yield return new WaitForSeconds(1.2f);
                ui.Refresh();
                log.AppendLine($"[{key} {wg.StepName}] {ui.LoupDebug()}");
                bool mine = wg.phase == WPh.Night && wg.NightRole(wg.players[owner])
                    || wg.phase == WPh.Witch && role == Role.Sorciere
                    || (wg.phase == WPh.Hunter || wg.phase == WPh.Heir || wg.phase == WPh.Dictator) && wg.pending.Count > 0 && wg.pending[0].seat == owner
                    || wg.phase == WPh.Dawn
                    || role == Role.PetiteFille && wg.phase == WPh.Night && wg.step == LoupGarou.WolfStep;
                if (mine && shots < 6) { shots++; yield return shot($"r{shots}-{key.Replace('/', '-')}"); }
                if (n == 1) wgBotAt = Time.time;
                if (wg.phase == WPh.Hunter && shots < 8) { yield return new WaitForSeconds(2.5f); shots++; yield return shot($"r{shots}-chasseur-vise"); }
            }
            if (Idle && !WgHold) { var a = wg.BotFor(me); if (a != null) { Apply(string.Join("|", a)); if (votedShots < 3 && (a[0] == "vote" || a.Length > 2 && a[2] == "wolf")) { votedShots++; yield return new WaitForSeconds(1.5f); yield return shot($"v{votedShots}-{last.Replace('/', '-')}"); } } }
            wgPhaseAt -= 0.4f;
            yield return new WaitForSeconds(0.1f);
        }
        log.AppendLine(Who());
        log.AppendLine("--- evenements ---");
        log.AppendLine(string.Join("\n", wgEvLog));
        log.AppendLine("--- journal ---");
        log.AppendLine(string.Join("\n", wg.log));
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "role.txt"), log.ToString());
    }

    // Autotest : moi (joue comme un bot) contre des bots, captures des etapes.
    IEnumerator LoupTest(string dir, System.Func<string, IEnumerator> shot)
    {
        yield return new WaitForSeconds(2); yield return shot("w1-carte");
        // Mesures d'echelle (perso assis vs objets de la map).
        var sb = new System.Text.StringBuilder();
        foreach (var an in FindObjectsByType<Animator>(FindObjectsSortMode.None)) if (an.isHuman)
        {
            var h = an.GetBoneTransform(HumanBodyBones.Head); var f = an.GetBoneTransform(HumanBodyBones.LeftFoot); var hp = an.GetBoneTransform(HumanBodyBones.Hips);
            if (h && f && hp) sb.AppendLine($"perso {an.name} tete-pied {h.position.y - f.position.y:F2} hanches {hp.position.y - f.position.y:F2} echelle {an.transform.lossyScale.y:F2}");
        }
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (r.transform.parent && (r.transform.parent.name.Contains("Barrel_01") || r.transform.parent.name.Contains("Door"))) { sb.AppendLine($"map {r.transform.parent.name} {r.bounds.size} {r.transform.lossyScale}"); if (sb.Length > 3000) break; }
        var fire = Clairiere.Center + new Vector3(0, 0, 0.5f);
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            if (Vector2.Distance(new Vector2(r.bounds.center.x, r.bounds.center.z), new Vector2(fire.x, fire.z)) < 1.2f)
                sb.AppendLine($"feu {r.name} <- {(r.transform.parent ? r.transform.parent.name : "")} {r.bounds.size}");
        foreach (var g in FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                     .Where(r => r.enabled && ((r.bounds.center - fire).magnitude < 25 || r.name.StartsWith("Foliage")) && r.transform.root.name.StartsWith("Map"))
                     .GroupBy(r => string.Join("+", r.sharedMaterials.Select(m => m ? m.name + "[" + (m.mainTexture ? m.mainTexture.name : "-") + "]" : "null"))))
            sb.AppendLine($"mat {g.Key} x{g.Count()} ex {g.First().name}<-{(g.First().transform.parent ? g.First().transform.parent.name : "")} {g.First().bounds.size}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "echelle.txt"), sb.ToString());
        yield return new WaitForSeconds(5);
        bool night = false, vote = false, dawn = false, elect = false, died = false, hang = false, tieShot = false, quit = false, forced = false, tieWatch = false;
        int n = 0;
        while (!wg.Finished && n < 3000)
        {
            n++;
            if (!night && wg.phase == WPh.Night) { night = true; yield return new WaitForSeconds(1.5f); yield return shot("w2-nuit"); wgSeeAll = true; yield return new WaitForSeconds(0.5f); yield return shot("w2b-loups"); wgTestDay = true; yield return new WaitForSeconds(2.5f); yield return shot("w2c-loups-jour"); wgTestDay = wgSeeAll = false; }
            if (!dawn && wg.phase == WPh.Dawn) { dawn = true; yield return new WaitForSeconds(1.5f); yield return shot("w3-aube"); }
            if (!elect && wg.phase == WPh.Election) { elect = true; yield return new WaitForSeconds(2); yield return shot("w4-election"); }
            if (!vote && wg.phase == WPh.Vote && wg.votes.Count >= 2) { vote = true; yield return new WaitForSeconds(0.5f); yield return shot("w5-vote");
                // Clic de vote : chaque tete visible doit etre cliquable (pas masquee par l'interface, retrouvee par le rayon).
                var clic = new System.Text.StringBuilder();
                foreach (var pl in wg.players)
                {
                    var sp = cam.WorldToScreenPoint(qsview.HeadOf(pl.seat) - Vector3.up * 0.3f);
                    if (sp.z < 0 || sp.x < 0 || sp.y < 0 || sp.x > Screen.width || sp.y > Screen.height) continue;
                    var over = ui.OverUi(sp); int hit = qsview.HeadUnder(cam.ScreenPointToRay(sp));
                    clic.AppendLine($"{pl.name} siege {pl.seat} ecran {sp} masque_ui={over} ({ui.PickName(sp)}) rayon={hit}");
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "clic.txt"), clic.ToString());
                var voter = wg.players.First(x => x.alive && x.seat != MySeatOr0).seat;
                wgTestPoint = (voter, wg.players.First(x => x.alive && x.seat != voter).seat);
                qsview.Cinematic(voter); yield return new WaitForSeconds(1.2f); yield return shot("w5b-main"); qsview.EndCinematic(); wgTestPoint = (-1, -1); }
            if (!died && wg.players.Any(p => !p.alive && p.seat != MySeatOr0)) { died = true; yield return new WaitForSeconds(1.5f); yield return shot("w7-suspense"); yield return new WaitForSeconds(1.7f); yield return shot("w7b-chute"); yield return new WaitForSeconds(1.2f); yield return shot("w8-revelation"); yield return new WaitForSeconds(4f); yield return shot("w9-tombe"); }
            if (!hang && wg.log.Any(x => x.Contains("éliminé par le village"))) { hang = true; yield return new WaitForSeconds(1.8f); yield return shot("w10-potence"); yield return new WaitForSeconds(2.2f); yield return shot("w11-pendu"); yield return new WaitForSeconds(1.6f); yield return shot("w12-revelation"); }
            if (n % 10 == 0) System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "assis.txt"), $"{wg.phase} {qsview.SitDiag()}" + System.Environment.NewLine);
            if (!forced && wg.phase == WPh.Vote && wg.crowed < 0 && wg.votes.Count == 0)
            {
                // Egalite forcee pour le test : les votants se partagent entre deux joueurs.
                forced = true;
                var al = wg.players.Where(x => wg.CanVote(x.seat)).ToList();
                if (al.Count % 2 == 0 && al.Count >= 4)
                {
                    int A = al[0].seat, B = al[1].seat;
                    for (int k = 0; k < al.Count; k++) Apply($"vote|{al[k].seat}|{(al[k].seat == A ? B : al[k].seat == B ? A : k % 2 == 0 ? A : B)}");
                }
            }
            if (!tieShot && wg.phase == WPh.Tie) { tieShot = true; yield return new WaitForSeconds(1.5f); yield return shot("w13-egalite"); tieWatch = true; }
            if (tieWatch && wg.phase != WPh.Tie) { tieWatch = false; yield return new WaitForSeconds(5.5f); yield return shot("w13b-pendu-epargne"); yield return new WaitForSeconds(4f); yield return shot("w13c-epargne-assis"); }
            if (!quit && wg.day >= 2 && wg.phase == WPh.Vote) { quit = true; var q = wg.players.LastOrDefault(x => x.alive && x.seat != MySeatOr0); if (q != null) Apply($"quit|{q.seat}"); yield return new WaitForSeconds(1f); yield return shot("w14-depart"); }
            if (Idle) { var a = wg.BotFor(MySeatOr0); if (a != null) Apply(string.Join("|", a)); }
            // en test, on accelere les etapes
            wgPhaseAt -= 0.5f;
            yield return new WaitForSeconds(0.1f);
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "loupgarou.txt"), string.Join("\n", wg.log));
        yield return new WaitForSeconds(5); yield return shot("w6-victoire");
    }
}
