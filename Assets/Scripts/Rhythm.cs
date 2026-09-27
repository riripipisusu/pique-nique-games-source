using System;
using System.Collections.Generic;
using System.Linq;

// Jeu de rythme TV Time (d'apres le jeu Godot de l'utilisatrice, facon "Lightners Live") : tout le monde joue la
// meme chanson en meme temps, chacun sur sa machine ; on ne s'echange que les scores. Deux pistes (gauche/droite),
// notes simples et notes tenues. Charts : Tools/rhythm_charts.py -> Resources/Rhythm/charts.json.
[Serializable]
public class RSong
{
    public string id, t, ch, dif, d, by, audio;
    public float vol, bpm, end;
    public int ord;
    public bool cover;
    public float[] n;   // a plat : temps (s), piste (0 gauche, 1 droite), duree de tenue (0 = note simple)
    public int Notes => n.Length / 3;
    public string Chapter => ch.StartsWith("Chapter ") ? "Chapitre " + ch.Substring(8) : ch;
    public string Difficulty => dif switch
    {
        "Easy" => "Facile", "Normal" => "Normal", "Medium" => "Moyen", "Hard" => "Difficile", "Very Hard" => "Très difficile",
        "Impossible" => "Impossible", _ => "Surprise",
    };
}

[Serializable] class RSongFile { public RSong[] songs; }

public enum LivePhase { Wait, Play, GameOver }

public class LivePlayer
{
    public string name;
    public int seat, score, combo, best, perfect, good, miss;
    public bool done;
}

public class Rhythm : IMatch
{
    public const int MaxPlayers = 10;
    static List<RSong> songs;
    public static List<RSong> Songs => songs ??= UnityEngine.JsonUtility.FromJson<RSongFile>(
        UnityEngine.Resources.Load<UnityEngine.TextAsset>("Rhythm/charts").text).songs.ToList();
    public static RSong Song(int option) => Songs[Math.Abs(option) % Songs.Count];

    public readonly RSong song;
    public readonly List<LivePlayer> players = new List<LivePlayer>();
    public LivePhase phase = LivePhase.Wait;
    public string winner;

    public Rhythm(IEnumerable<string> names, int option)
    {
        song = Song(option);
        int s = 0;
        foreach (var n in names) players.Add(new LivePlayer { name = n, seat = s++ });
    }

    public int Actor => phase == LivePhase.Play ? Quiz.Everyone : -1;
    public bool Finished => phase == LivePhase.GameOver;
    public string[] Bot() => null;
    public int MaxScore => RhythmJudge.MaxScore(song);

    // "go" : la chanson demarre ; "sc|siege|score|combo|best|parfait|bien|rate" : score en cours (siege impose par
    // l'hote) ; "done|..." : idem, chanson terminee pour ce joueur ; "end" : resultats.
    public bool TryApply(string[] a)
    {
        switch (a[0])
        {
            case "go":
                if (phase != LivePhase.Wait) return false;
                phase = LivePhase.Play;
                return true;
            case "sc":
            case "done":
                if (phase != LivePhase.Play || a.Length < 8) return false;
                var v = new int[7];
                for (int i = 0; i < 7; i++) if (!int.TryParse(a[i + 1], out v[i]) || v[i] < 0) return false;
                if (v[0] >= players.Count || v[1] > MaxScore) return false;
                var p = players[v[0]];
                if (p.done) return false;
                (p.score, p.combo, p.best, p.perfect, p.good, p.miss) = (v[1], v[2], v[3], v[4], v[5], v[6]);
                p.done = a[0] == "done";
                return true;
            case "end":
                if (phase != LivePhase.Play) return false;
                phase = LivePhase.GameOver;
                int top = players.Max(x => x.score);
                winner = string.Join(" et ", players.Where(x => x.score == top).Select(x => x.name));
                return true;
        }
        return false;
    }

    public static string Report(string kind, int seat, RhythmJudge j) =>
        $"{kind}|{seat}|{j.score}|{j.combo}|{j.best}|{j.perfect}|{j.good}|{j.miss}";
}

// Arbitrage local d'un joueur (ou d'un bot) : fenetre de +-0.1 s comme l'original (20 px a 200 px/s),
// 75 a 100 points selon la precision, plus 50 points par seconde de note tenue jusqu'au bout.
public class RhythmJudge
{
    public const float Window = 0.1f, Perfect = 0.035f, HoldRate = 50;
    public enum Hit { None, Perfect, Good, Miss, HoldDone }
    public readonly float[] t, dur;
    public readonly int[] lane;
    public readonly byte[] state;              // 0 a jouer, 1 jouee (tenue en cours), 2 finie, 3 ratee
    public int score, combo, best, perfect, good, miss, lastGain;
    readonly int[] next = new int[2];          // premiere note non jugee de chaque piste
    readonly int[] holding = { -1, -1 };
    public readonly List<(int lane, Hit hit, int note)> events = new List<(int, Hit, int)>();

    public RhythmJudge(RSong s)
    {
        int n = s.Notes;
        t = new float[n]; lane = new int[n]; dur = new float[n]; state = new byte[n];
        for (int i = 0; i < n; i++) { t[i] = s.n[3 * i]; lane[i] = (int)s.n[3 * i + 1]; dur[i] = s.n[3 * i + 2]; }
    }

    public static int MaxScore(RSong s)
    {
        float sum = 0;
        for (int i = 2; i < s.n.Length; i += 3) sum += 100 + (int)(s.n[i] * HoldRate);
        return (int)sum;
    }

    public bool Holding(int l) => holding[l] >= 0;

    // Score affiche : les points de tenue montent en direct pendant qu'on tient (acquis au lacher / a la fin).
    public int ScoreAt(float now)
    {
        int s = score;
        for (int l = 0; l < 2; l++)
            if (holding[l] >= 0) s += (int)(Math.Min(dur[holding[l]], Math.Max(0, now - t[holding[l]])) * HoldRate);
        return s;
    }
    public bool Finished => next[0] >= t.Length && next[1] >= t.Length && holding[0] < 0 && holding[1] < 0;

    int Pending(int l)
    {
        while (next[l] < t.Length && (lane[next[l]] != l || state[next[l]] != 0)) next[l]++;
        return next[l];
    }

    public void Press(int l, float now)
    {
        int i = Pending(l);
        if (i >= t.Length || Math.Abs(t[i] - now) > Window) return;   // frappe dans le vide : pas de penalite
        float err = Math.Abs(t[i] - now);
        lastGain = 75 + (int)Math.Round(25 * (1 - err / Window));
        score += lastGain;
        combo++; best = Math.Max(best, combo);
        bool p = err <= Perfect;
        if (p) perfect++; else good++;
        state[i] = (byte)(dur[i] > 0 ? 1 : 2);
        if (dur[i] > 0) holding[l] = i;
        events.Add((l, p ? Hit.Perfect : Hit.Good, i));
    }

    public void Release(int l, float now)
    {
        int i = holding[l];
        if (i < 0) return;
        holding[l] = -1;
        state[i] = 2;
        float end = t[i] + dur[i];
        if (now >= end - Window) { score += (int)(dur[i] * HoldRate); events.Add((l, Hit.HoldDone, i)); return; }
        score += (int)(Math.Max(0, now - t[i]) * HoldRate);   // lachee trop tot : points au prorata, combo casse
        combo = 0;
        events.Add((l, Hit.Miss, i));
    }

    public void Update(float now)
    {
        for (int l = 0; l < 2; l++)
        {
            if (holding[l] >= 0 && now >= t[holding[l]] + dur[holding[l]]) Release(l, now);
            int i;
            while ((i = Pending(l)) < t.Length && t[i] < now - Window)
            {
                state[i] = 3; miss++; combo = 0;
                events.Add((l, Hit.Miss, i));
            }
        }
    }

    // Bot : joue chaque note avec un ecart aleatoire ; rate une note sur (1 - skill), lache parfois une tenue.
    public static void BotStep(RhythmJudge j, float now, float skill, Random rng)
    {
        for (int l = 0; l < 2; l++)
        {
            int h = j.holding[l];
            if (h >= 0 && now >= j.t[h] + j.dur[h] * (rng.NextDouble() < skill ? 1 : 0.5f)) j.Release(l, now);
            int i = j.Pending(l);
            if (i < j.t.Length && now >= j.t[i] - 0.02f)
            {
                if (rng.NextDouble() < skill) j.Press(l, j.t[i] + (float)(rng.NextDouble() - 0.5) * 0.12f * (1.2f - skill));
                else { j.state[i] = 3; j.miss++; j.combo = 0; }
            }
        }
    }
}
