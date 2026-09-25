using System.Collections.Generic;
using UnityEngine;

public class Sound : MonoBehaviour
{
    public static Sound I;
    AudioSource music, sfx, ui;
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    Settings s;
    AudioClip next;
    float fade = 1;

    public static void Create(Settings s)
    {
        var g = new GameObject("Sound");
        I = g.AddComponent<Sound>();
        I.s = s;
        I.music = g.AddComponent<AudioSource>();
        I.music.loop = true;
        I.music.volume = 0;
        I.sfx = g.AddComponent<AudioSource>();
        I.ui = g.AddComponent<AudioSource>();
        I.ui.ignoreListenerPause = true;
        foreach (var c in Resources.LoadAll<AudioClip>("Audio")) I.clips[c.name] = c;
        I.Refresh();
    }

    public void Refresh()
    {
        AudioListener.volume = s.mute ? 0 : s.master;
        sfx.volume = s.sfx;
        ui.volume = s.ui;
    }

    // Fondu enchaine : on baisse la musique en cours, puis on lance la suivante.
    public void Music(string name)
    {
        var clip = clips[name];
        if (music.clip == clip) return;
        next = clip;
        fade = 0;
    }

    void Update()
    {
        if (held) return;
        if (next != null && music.volume <= 0.001f)
        {
            music.clip = next;
            music.Play();
            next = null;
            fade = 1;
        }
        music.volume = Mathf.MoveTowards(music.volume, s.music * fade, Time.unscaledDeltaTime * 0.6f);
    }

    public void Play(string n, float vol = 1, float pitchVar = 0.08f)
    {
        if (!clips.TryGetValue(n, out var c)) return;
        sfx.pitch = 1 + Random.Range(-pitchVar, pitchVar);
        sfx.PlayOneShot(c, vol);
    }

    // Son en boucle pilote par l'appelant (volume, hauteur), au volume des effets.
    public AudioSource Loop(string n)
    {
        var a = gameObject.AddComponent<AudioSource>();
        a.loop = true;
        a.volume = 0;
        if (clips.TryGetValue(n, out var c)) { a.clip = c; a.Play(); }
        return a;
    }

    public float SfxVolume => s.sfx;

    // Voix d'un personnage : une nouvelle syllabe ne part que si la precedente est finie.
    AudioSource voice;
    public void Voice(string n, float vol = 1)
    {
        if (!voice) voice = gameObject.AddComponent<AudioSource>();
        if (voice.isPlaying || !clips.TryGetValue(n, out var c)) return;
        voice.volume = s.sfx * vol;
        voice.clip = c;
        voice.Play();
    }
    public float MasterVolume => s.mute ? 0 : s.master;
    // Coupe la musique (generique video) : rien ne la relance tant que held est vrai, meme un changement de morceau.
    bool held;
    public void PauseMusic(bool pause) { held = pause; if (pause) music.Pause(); else music.UnPause(); }

    public void UI(string n) { if (clips.TryGetValue(n, out var c)) ui.PlayOneShot(c); }
}
