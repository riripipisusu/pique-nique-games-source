using System.Collections.Generic;
using UnityEngine;

public class Sound : MonoBehaviour
{
    public static Sound I;
    AudioSource music, sfx, ui;
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    Settings s;

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

    // Changement de morceau : l'ancien s'arrete net (on n'entend plus le jeu une fois revenu a l'accueil), le nouveau monte en fondu.
    public void Music(string name) => MusicClip(clips[name]);

    void Update()
    {
        if (held) return;
        music.volume = Mathf.MoveTowards(music.volume, s.music, Time.unscaledDeltaTime * 0.6f);
    }

    // Clip charge a part (sons du Uno) : joue tel quel, sans variation de hauteur.
    public void PlayClip(AudioClip c, float vol = 1)
    {
        if (!c) return;
        sfx.pitch = 1;
        sfx.PlayOneShot(c, vol);
    }

    // Musique chargee a part (Uno) : comme Music().
    public void MusicClip(AudioClip clip)
    {
        if (!clip || music.clip == clip) return;
        music.Stop();
        music.clip = clip;
        music.volume = 0;
        if (!held) music.Play();
    }

    public void Play(string n, float vol = 1, float pitchVar = 0.08f)
    {
        if (!clips.TryGetValue(n, out var c)) return;
        if (c.length > 8)   // son long (battement de coeur d'une minute) : sur sa propre source, coupee par StopLong
        {
            if (!longSfx) longSfx = gameObject.AddComponent<AudioSource>();
            longSfx.volume = s.sfx * vol; longSfx.pitch = 1; longSfx.clip = c; longSfx.Play();
            return;
        }
        sfx.pitch = 1 + Random.Range(-pitchVar, pitchVar);
        sfx.PlayOneShot(c, vol);
    }

    AudioSource longSfx;
    public void StopLong() { if (longSfx) longSfx.Stop(); }

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
    public void PauseMusic(bool pause) { held = pause; if (pause) music.Pause(); else { music.UnPause(); if (!music.isPlaying) music.Play(); } }

    public void UI(string n) { if (clips.TryGetValue(n, out var c)) ui.PlayOneShot(c); }
}
