using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// HUD du jeu de rythme : couloir a deux pistes a gauche (les notes descendent vers la ligne), score et combo,
// classement de tout le monde a droite, titre et avancement de la chanson en haut. Choix de la chanson au salon.
public partial class Ui
{
    // Mesures du jeu d'origine (960 x 540, ici en 1920 x 1080) : couloir de 180 px au centre, ligne a 420 px
    // du haut du couloir, notes a 200 px/s d'origine = 400 px/s ici (un peu plus d'une seconde a l'avance).
    VisualElement rhHud, rhBox, rhTrack, rhBoard, rhProgressFill, rhBeats, rhScores;
    readonly VisualElement[] rhLanes = new VisualElement[2];
    readonly Label[] rhGain = new Label[2];
    readonly IVisualElementScheduledItem[] gainFade = new IVisualElementScheduledItem[2];
    readonly List<VisualElement> rhPool = new List<VisualElement>(), rhBeatPool = new List<VisualElement>();
    Label rhTitle, rhScore, rhMax, rhCombo, rhCount, rhStatus, rhOffset;
    const float RhTarget = 420, RhSpeed = 400;
    int rhFirst;

    void BuildRhythmHud()
    {
        rhHud = Div(hud, "layer");
        rhHud.pickingMode = PickingMode.Ignore;
        rhTitle = Text(rhHud, "", "rh-title");
        var bar = Div(rhHud, "rh-progress");
        rhProgressFill = Div(bar, "rh-progress-fill");

        rhBox = Div(rhHud, "rh-box");
        rhTrack = Div(rhBox, "rh-track");
        rhBeats = Div(rhTrack, "layer");
        rhCombo = Text(rhTrack, "", "rh-combo");
        Text(rhTrack, "COMBO", "rh-combo-text");
        for (int l = 0; l < 2; l++)
        {
            rhLanes[l] = Div(rhTrack, "rh-lane", l == 0 ? "rh-left" : "rh-right");
            Div(rhLanes[l], "rh-flash").style.backgroundImage = FlashTexture;
            Div(rhLanes[l], "rh-receptor");
            Text(rhLanes[l], l == 0 ? "← F" : "J →", "rh-key");
            rhGain[l] = Text(rhBox, "", "rh-gain", l == 0 ? "rh-gain-left" : "rh-gain-right");
        }
        rhScores = Div(rhHud, "rh-scores");
        var sc = Div(rhScores, "rh-pair");
        rhScore = Text(sc, "0", "rh-num", "rh-teal");
        Text(sc, "Score", "rh-lbl", "rh-teal");
        Div(rhScores, "grow");
        var mc = Div(rhScores, "rh-pair");
        Text(mc, "Max Combo", "rh-lbl", "rh-cyan");
        rhMax = Text(mc, "0", "rh-num", "rh-cyan");

        rhBoard = Div(rhHud, "rh-board");
        rhCount = Text(rhHud, "", "rh-count");
        rhOffset = Text(rhHud, "", "rh-offset");
        rhStatus = Text(rhHud, "", "rh-status");
    }

    public void ShowRhythmHud()
    {
        rhFirst = 0;
        foreach (var e in rhPool) e.RemoveFromHierarchy();
        rhPool.Clear();
        bool me = game.judge != null;
        rhBox.style.display = rhScores.style.display = me ? DisplayStyle.Flex : DisplayStyle.None;
        rhGain[0].text = rhGain[1].text = rhCombo.text = "";
        rhTitle.text = $"{game.rh.song.t}   ·   {game.rh.song.Chapter}   ·   {game.rh.song.Difficulty}";
        foreach (var sp in sparks) { sp.life = 0; sp.e.style.display = DisplayStyle.None; }
        rhOffset.text = "";
    }

    public void RhythmKey(int lane)
    {
        var r = rhLanes[lane];
        r.AddToClassList("pressed");
        r.schedule.Execute(() => r.RemoveFromClassList("pressed")).StartingIn(90);
    }

    // "+97" a cote de la piste, comme l'original ("Raté" en rouge).
    public void RhythmHit(int lane, RhythmJudge.Hit hit)
    {
        var ln = rhLanes[lane];
        string fx = hit == RhythmJudge.Hit.Miss ? "missed" : "hit";
        ln.AddToClassList(fx);
        ln.schedule.Execute(() => ln.RemoveFromClassList(fx)).StartingIn(hit == RhythmJudge.Hit.Miss ? 220 : 70);
        if (hit != RhythmJudge.Hit.Miss) Sparks(lane, hit == RhythmJudge.Hit.Perfect ? 22 : 12);
        if (hit == RhythmJudge.Hit.HoldDone) return;
        var g = rhGain[lane];
        g.text = hit == RhythmJudge.Hit.Miss ? "Raté" : "+" + game.judge.lastGain;
        g.EnableInClassList("miss", hit == RhythmJudge.Hit.Miss);
        g.EnableInClassList("perfect", hit == RhythmJudge.Hit.Perfect);
        g.RemoveFromClassList("pop");
        gainFade[lane]?.Pause();
        gainFade[lane] = g.schedule.Execute(() => g.AddToClassList("pop")).StartingIn(450);   // affiche ~0.45 s, puis fondu 0.6 s
    }

    public void RhythmOffset()
    {
        rhOffset.text = $"Décalage audio : {RhythmView.UserOffsetMs:+0;-0;0} ms   (flèches haut / bas)";
        rhOffset.RemoveFromClassList("pop");
        rhOffset.schedule.Execute(() => rhOffset.AddToClassList("pop")).StartingIn(10);
    }

    void RefreshRhythm()
    {
        var r = game.rh;
        rhBoard.Clear();
        rhBoard.style.display = r.players.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (var p in r.players.OrderByDescending(p => p.score))
        {
            var row = Div(rhBoard, "rh-row");
            Text(row, p.name + (p.done ? " ✔" : ""), "rh-name").style.color = Board.Colors[p.seat % Board.Colors.Length];
            Div(row, "grow");
            Text(row, p.combo >= 5 ? $"x{p.combo}" : "", "rh-row-combo");
            Text(row, p.score.ToString(), "rh-row-score");
        }
        rhStatus.text = game.Spectating ? "Tu regardes le concert : tu joueras au prochain !" : "";
        rhStatus.style.display = rhStatus.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // Chaque image : les notes descendent vers la ligne (barre = note, trait vertical = tenue), lignes des temps.
    public void UpdateRhythm()
    {
        var r = game.rh;
        if (r == null || rhHud.panel == null) return;
        float now = game.live.SongTime;
        bool playing = r.phase == LivePhase.Play && game.live.Running;
        rhProgressFill.style.width = Length.Percent(playing ? Mathf.Clamp01(now / Mathf.Max(1, r.song.end)) * 100 : 0);
        rhCount.text = !playing ? "" : now < -1.6f ? "3" : now < -0.8f ? "2" : now < 0 ? "1" : now < 0.5f ? "C'est parti !" : "";
        var j = game.judge;
        if (j == null) return;
        rhScore.text = j.ScoreAt(now).ToString();
        rhMax.text = j.best.ToString();
        rhCombo.text = j.combo.ToString("00");
        float h = rhTrack.contentRect.height, laneW = rhTrack.contentRect.width / 2;   // sans la bordure du couloir
        if (float.IsNaN(h) || h < 10) return;
        float ahead = RhTarget / RhSpeed + 0.1f;
        // Une ligne grise par temps, qui defile avec les notes.
        float beat = 60f / Mathf.Max(30, r.song.bpm);
        int b0 = Mathf.CeilToInt((now - (h - RhTarget) / RhSpeed) / beat), used = 0;
        for (int b = b0; b * beat < now + ahead; b++)
        {
            if (used == rhBeatPool.Count) rhBeatPool.Add(Div(rhBeats, "rh-beat"));
            var e = rhBeatPool[used++];
            e.style.display = DisplayStyle.Flex;
            e.style.top = RhTarget - (b * beat - now) * RhSpeed;
        }
        for (int k = used; k < rhBeatPool.Count; k++) rhBeatPool[k].style.display = DisplayStyle.None;

        while (rhFirst < j.t.Length && j.t[rhFirst] + j.dur[rhFirst] < now - 0.4f) rhFirst++;
        used = 0;
        for (int i = rhFirst; i < j.t.Length && j.t[i] < now + ahead; i++)
        {
            bool hold = j.dur[i] > 0;
            if (j.state[i] == 2) continue;                                // jouee : disparait
            if (used == rhPool.Count)
            {
                var e = Div(rhTrack, "rh-note");
                e.pickingMode = PickingMode.Ignore;
                Div(e, "rh-stem");
                Div(e, "rh-head");
                rhPool.Add(e);
            }
            var n = rhPool[used++];
            n.style.display = DisplayStyle.Flex;
            float head = j.state[i] == 1 ? RhTarget : RhTarget - (j.t[i] - now) * RhSpeed;   // tenue en cours : reste sur la ligne
            float tail = hold ? Mathf.Max(-20, RhTarget - (j.t[i] + j.dur[i] - now) * RhSpeed) : head;
            n.style.left = j.lane[i] * laneW + laneW * 0.1f;
            n.style.width = laneW * 0.8f;
            n.style.top = tail - 6;
            n.style.height = head - tail + 12;
            n.EnableInClassList("rh-hold", hold);
            n.EnableInClassList("rh-missed", j.state[i] == 3);
            n.EnableInClassList("rh-holding", j.state[i] == 1);
        }
        for (int k = used; k < rhPool.Count; k++) rhPool[k].style.display = DisplayStyle.None;
        for (int l = 0; l < 2; l++)
        {
            rhLanes[l].EnableInClassList("held", j.Holding(l));
            if (j.Holding(l)) { holdEmit[l] += Time.deltaTime * 45; while (holdEmit[l] >= 1) { holdEmit[l]--; Sparks(l, 1); } }
        }
        UpdateSparks(laneW);
    }

    // --- Effets facon jeu d'origine : halo degrade au-dessus de la cible, etincelles qui jaillissent -------
    static Texture2D flashTex;
    static Texture2D FlashTexture
    {
        get
        {
            if (flashTex) return flashTex;
            flashTex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) flashTex.SetPixel(0, y, new Color(1, 1, 1, Mathf.Pow(y / 63f, 1.4f) * 0.55f));   // transparent en haut
            flashTex.Apply();
            return flashTex;
        }
    }

    class Spark { public VisualElement e; public Vector2 p, v; public float life, max; public int lane; }
    readonly List<Spark> sparks = new List<Spark>();
    readonly float[] holdEmit = new float[2];

    void Sparks(int lane, int count)
    {
        for (int k = 0; k < count; k++)
        {
            var s = sparks.Find(x => x.life <= 0);
            if (s == null) { s = new Spark { e = Div(rhTrack, "rh-spark") }; s.e.pickingMode = PickingMode.Ignore; sparks.Add(s); }
            float a = Random.Range(-35f, 35f) * Mathf.Deg2Rad, speed = Random.Range(480f, 640f);
            s.lane = lane;
            s.p = new Vector2(Random.Range(-24f, 24f), RhTarget);
            s.v = new Vector2(Mathf.Sin(a) * speed, -Mathf.Cos(a) * speed);
            s.life = s.max = Random.Range(0.28f, 0.42f);
            s.e.style.display = DisplayStyle.Flex;
        }
    }

    void UpdateSparks(float laneW)
    {
        float dt = Time.deltaTime;
        foreach (var s in sparks)
        {
            if (s.life <= 0) continue;
            s.life -= dt;
            if (s.life <= 0) { s.e.style.display = DisplayStyle.None; continue; }
            s.p += s.v * dt;
            s.v *= 1 - 2 * dt;   // freinage
            float k = s.life / s.max;
            s.e.style.left = s.lane * laneW + laneW / 2 + s.p.x - 3;   // p.x : pixels depuis le centre de la piste
            s.e.style.top = s.p.y - 3;
            s.e.style.opacity = k;
            s.e.style.scale = new Scale(Vector2.one * (0.5f + k));
        }
    }

    // --- Choix de la chanson (installation et salon) ---------------------------------------
    string songTab;
    static readonly Dictionary<string, Texture2D> covers = new Dictionary<string, Texture2D>();

    static Texture2D Cover(RSong s)
    {
        if (covers.TryGetValue(s.id, out var t)) return t;
        var path = System.IO.Path.Combine(Application.streamingAssetsPath, "Rhythm", s.id + ".png");
        if (s.cover && System.IO.File.Exists(path))
        {
            t = new Texture2D(2, 2) { filterMode = FilterMode.Point };   // pochettes en pixel art
            t.LoadImage(System.IO.File.ReadAllBytes(path));
        }
        return covers[s.id] = t;
    }

    void SongPicker(VisualElement parent, int current, System.Action<int> pick)
    {
        var songs = Rhythm.Songs;
        var cur = Rhythm.Song(current);
        songTab ??= cur.ch;
        var box = Div(parent, "rh-picker");
        var head = Div(box, "row", "rh-pick-head");
        var art = Div(head, "rh-cover");
        art.style.backgroundImage = Cover(cur);
        var txt = Div(head, "grow");
        Text(txt, cur.t, "mode-name");
        Text(txt, $"{cur.Chapter}  ·  {cur.Difficulty}  ·  {cur.Notes} notes  ·  {cur.by}", "mode-desc");
        if (!string.IsNullOrEmpty(cur.d) && cur.d != cur.t) Text(txt, cur.d, "mode-desc", "rh-alt");
        Ico(Btn(head, "Au hasard", () => pick(Random.Range(0, songs.Count)), "ghost", "small"), "play");
        var tabs = Div(box, "row", "rh-tabs");
        foreach (var ch in songs.Select(s => s.ch).Distinct())
        {
            var c = ch;
            Btn(tabs, songs.First(s => s.ch == c).Chapter, () => { songTab = c; pick(current); }, "small", c == songTab ? "blue" : "ghost");
        }
        var list = Add(box, new ScrollView(), "rh-list");
        for (int i = 0; i < songs.Count; i++)
        {
            if (songs[i].ch != songTab) continue;
            int idx = i;
            var s = songs[i];
            var row = new Button(() => { Sound.I.UI("tick"); pick(idx); });
            row.AddToClassList("rh-song");
            row.EnableInClassList("selected", idx == Rhythm.Songs.IndexOf(cur));
            Text(row, s.t, "rh-song-name");
            Text(row, s.Difficulty, "pill", "rh-dif-" + (s.dif == "..." ? "impossible" : s.dif.Replace(" ", "").ToLower()));
            list.Add(row);
        }
    }
}
