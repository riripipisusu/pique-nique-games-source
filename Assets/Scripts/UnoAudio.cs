using System.Collections.Generic;
using UnityEngine;

// Sons du Uno : ceux du jeu Uno officiel (fournis par l'utilisatrice, Resources/UnoSfx, hors depot), charges a la
// demande par leur nom d'origine. Une partie tire au sort l'une des 5 musiques "Classic" et son jingle de fin.
public static class UnoAudio
{
    static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
    static string theme = "A";

    public static AudioClip Get(string name)
    {
        if (!cache.TryGetValue(name, out var c)) cache[name] = c = Resources.Load<AudioClip>("UnoSfx/" + name);
        return c;
    }

    public static void Play(string name, float vol = 1) { if (Sound.I) Sound.I.PlayClip(Get(name), vol); }

    // Une variante au hasard : Any("FL_Card_Play_{0:00}", 1, 20) -> FL_Card_Play_01 .. _20.
    public static void Any(string format, int from, int to, float vol = 1) => Play(string.Format(format, Random.Range(from, to + 1)), vol);
    public static void OneOf(float vol, params string[] names) => Play(names[Random.Range(0, names.Length)], vol);

    public static void CardPlayed() => Any("FL_Card_Play_{0:00}", 1, 20, 0.9f);
    public static void CardDealt(float vol = 0.7f) => Play("FL_Card_Deal", vol);
    public static void UnoVoice() => OneOf(0.9f, "VO_Card_UNO_Call_Speech_Female01", "VO_Card_UNO_Call_Speech_Female02", "VO_Card_UNO_Call_Speech_Male_Old");
    public static void Draw2Voice() => OneOf(0.9f, "VO_Card_Function_Draw2_Speech_Female01", "VO_Card_Function_Draw2_Speech_Female02", "VO_Card_Function_Draw2_Speech_Female03", "VO_Card_Function_Draw2_Speech_Male_Old");
    public static void Draw4Voice() => OneOf(0.9f, "VO_Card_Function_Draw4_Speech_Female01", "VO_Card_Function_Draw4_Speech_Female02", "VO_Card_Function_Draw4_Speech_Male_Old");
    static readonly string[] ColorWords = { "Red", "Yellow", "Green", "Blue" };
    public static string ColorWord(int c) => ColorWords[Mathf.Clamp(c, 0, 3)];

    public static void StartMusic()
    {
        theme = "ABCDE"[Random.Range(0, 5)].ToString();
        if (Sound.I) Sound.I.MusicClip(Get($"Mus_Classic_Main_Game_{theme}_Basic_LP"));
    }
    public static void Jingle(bool match) => Play($"Jingle_Classic_{theme}_{(match ? "Match" : "Round")}_End");
}
