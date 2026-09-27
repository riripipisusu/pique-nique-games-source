using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

// Scene du jeu de rythme : l'estrade "Pique-Nique Live" (Tools/rhythm_stage_blender.py), les personnages des
// joueurs alignes dessus (ils dansent quand le combo monte, encaissent quand ils ratent), Tenna qui presente
// au pied de la scene, et la musique (StreamingAssets/Rhythm, lue sur ce PC).
public class RhythmView : MonoBehaviour
{
    public static readonly Vector3 Center = new Vector3(0, -900, 0);
    const float Deck = 0.9f;   // hauteur du dessus de l'estrade

    class Seat { public Transform root, guitar; public Animator an; public string state; public float hitUntil; }
    readonly List<Seat> seats = new List<Seat>();
    Transform cast;
    AudioSource src;
    public AudioClip clip;
    public string loaded;
    Material lit, glowBase;
    readonly List<Light> beams = new List<Light>(), signLights = new List<Light>();
    Material ledA, ledB;

    // Cadrage et place de Tenna : reglables a la souris dans Unity (Assets/Resources/LiveLayout.prefab, objets
    // "Camera" et "Tenna"), sinon valeurs par defaut ci-dessous.
    Vector3 camPos = new Vector3(0, 3.6f, -12.5f);
    Quaternion camRot = Quaternion.LookRotation(new Vector3(0, 3.1f, 0) - new Vector3(0, 3.6f, -12.5f));
    public float Fov { get; private set; } = 50;
    public Pose CamPose => new Pose(transform.TransformPoint(camPos), transform.rotation * camRot);

    void Awake()
    {
        transform.position = Center;
        lit = Resources.Load<Material>("Lit");
        glowBase = Resources.Load<Material>("LitGlow");
        src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        var model = Resources.Load<GameObject>("Rhythm/LiveStage");
        if (model)
        {
            var m = Instantiate(model, transform).transform;
            m.localRotation = Quaternion.Euler(0, 180, 0);   // le devant du modele Blender regarde +z : on le tourne vers le public
            Paint(m);
        }
        // Salle noire tout autour (comme le fond du jeu d'origine) : une sphere vue de l'interieur.
        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(dome.GetComponent<Collider>());
        dome.transform.SetParent(transform, false);
        dome.transform.localScale = Vector3.one * 90;
        var black = Mat("000000", 0);
        black.SetFloat("_Cull", 1);   // faces avant masquees : on voit l'interieur
        dome.GetComponent<Renderer>().sharedMaterial = black;
        // Sol de salle noir brillant, public dans l'ombre.
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(floor.GetComponent<Collider>());
        floor.transform.SetParent(transform, false);
        floor.transform.localScale = new Vector3(40, 0.02f, 40);
        floor.GetComponent<Renderer>().sharedMaterial = Mat("0a0812", 0.8f);
        // Projecteurs colores qui balaient la scene (au rythme, cf. Pulse) + une face blanche pour les visages.
        foreach (var (x, c) in new[] { (-6f, "ff4fd8"), (-2f, "3fd0ff"), (2f, "ffe45c"), (6f, "7cff6b") })
            beams.Add(Spot(new Vector3(x, 8, -1), new Vector3(x * 0.4f, Deck, 0), Board.Hex(c), 10, 34));
        Spot(new Vector3(0, 7, -10), new Vector3(0, 1.8f, 0), Board.Hex("fff1e0"), 14, 36);   // face : les musiciens
        // L'enseigne eclaire la scene : violet de ses lettres, dore de ses LEDs.
        foreach (var (p, c) in new[] { (new Vector3(-3.2f, 4.6f, 1.2f), "b24dff"), (new Vector3(3.2f, 4.6f, 1.2f), "b24dff"), (new Vector3(0, 3.6f, 1.0f), "ffc14d") })
        {
            var l = new GameObject("lumiere enseigne").AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.localPosition = p;
            l.type = LightType.Point; l.range = 11; l.intensity = 6; l.color = Board.Hex(c);
            signLights.Add(l);
        }
        var vol = gameObject.AddComponent<UnityEngine.Rendering.Volume>();
        vol.isGlobal = true; vol.priority = 10;
        vol.profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        var bloom = vol.profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
        bloom.intensity.value = 1.0f; bloom.threshold.value = 0.9f; bloom.scatter.value = 0.75f;
        bloom.tint.value = Color.white;
        var layout = Resources.Load<GameObject>("LiveLayout");
        var mark = layout ? layout.transform.Find("Camera") : null;
        if (mark)
        {
            camPos = mark.localPosition;
            camRot = mark.localRotation;
            var c = mark.GetComponent<Camera>();
            if (c) Fov = c.fieldOfView;
        }
        SpawnTenna(layout ? layout.transform.Find("Tenna") : null);
        gameObject.SetActive(false);
    }

    // --- Tenna en smoking : danse (Swing) pendant la chanson, Silly a la fin, visage sur la planche ------------
    Transform tenna;
    Animator tennaAn;
    Material tennaFace;
    string tennaState;
    float faceUntil;

    void SpawnTenna(Transform mark)
    {
        var prefab = Synty.I ? Synty.I.tennaTux : null;
        if (!prefab) { Debug.LogWarning("Tenna en smoking absent du registre (LiveSetup)"); return; }
        tenna = Instantiate(prefab, transform).transform;
        tenna.name = "Tenna";
        if (mark) { tenna.localPosition = mark.localPosition; tenna.localRotation = mark.localRotation; tenna.localScale = mark.localScale; }
        else { tenna.localPosition = new Vector3(5.5f, 0.02f, -3.6f); tenna.localRotation = Quaternion.Euler(0, 205, 0); }
        foreach (var smr in tenna.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            smr.updateWhenOffscreen = true;
            foreach (var m in smr.materials) if (m.name.StartsWith("TennaTux_Face")) tennaFace = m;
        }
        tennaAn = tenna.GetComponent<Animator>();
        // Taille de la canne : celle de l'os "Cane" du Tenna de LiveLayout (reglable dans Unity), 70 % par defaut.
        // Deux objets s'appellent "Cane" (le maillage et son os) : seul l'os compte, c'est celui des os du maillage.
        Transform CaneBone(Transform t) => t ? t.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).FirstOrDefault(b => b && b.name == "Cane") : null;
        var cane = CaneBone(tenna);
        var markCane = CaneBone(mark);
        if (cane) cane.localScale = markCane && !Mathf.Approximately(markCane.localScale.x, 1) ? markCane.localScale : cane.localScale * 0.7f;   // 1 = pas retouchee
        foreach (var r in tenna.GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials)
            {
                // Emission remise en marche ici : l'editeur Unity retire le mot-cle quand on ouvre le materiau,
                // et l'ecran du visage (base noire) devient alors tout noir.
                bool face = m.name.StartsWith("TennaTux_Face");
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                if (face) { m.SetTexture("_EmissionMap", m.GetTexture("_BaseMap")); m.SetColor("_EmissionColor", Color.white * 0.95f); tennaFace = m; }
                m.SetFloat("_EnvironmentReflections", 0); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                m.SetFloat("_SpecularHighlights", 0); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            }
    }

    public bool HasTenna => tenna;
    public Pose TennaPose => QuizView.CloseUp(tenna);

    public void TennaDance(string state)
    {
        if (!tennaAn || state == tennaState) return;
        tennaState = state;
        tennaAn.CrossFadeInFixedTime(state, 0.3f);
    }

    // Expressions : cases de la planche (4 x 4) que l'ecran du smoking affiche par decalage (cf. le .blend).
    public void TennaFace(string shape, float hold = 1.5f)
    {
        if (!tennaFace) return;
        var cell = shape == "Pog" ? new Vector2(0.75f, 0) : shape == "HmmmSketchfab" ? new Vector2(0.25f, -0.5f) : shape == "Grin" ? new Vector2(0.5f, -0.5f) : Vector2.zero;
        tennaFace.SetTextureOffset("_BaseMap", cell);
        tennaFace.SetTextureOffset("_EmissionMap", cell);
        faceUntil = Time.time + hold;
    }

    void Update()
    {
        if (tennaFace && faceUntil > 0 && Time.time > faceUntil) { faceUntil = 0; TennaFace("Smile", 0); faceUntil = 0; }
    }

    // Les materiaux du FBX ne sont que des noms : on leur donne les couleurs de la scene d'origine.
    void Paint(Transform t)
    {
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string n = mats[i] ? mats[i].name.Replace(" (Instance)", "") : "";
                mats[i] = n switch
                {
                    "StageTop" => Mat("3f5fb0", 0.35f),
                    "StageSide" => Mat("2a3d84", 0.3f),
                    "StageDark" => Mat("18204a", 0.2f),
                    "Neon" => Glow(Board.Hex("6fb8ff"), 0.8f),
                    "SpotPool" => Glow(Board.Hex("7d9be0"), 0.35f),
                    "Lamp" => Mat("5a5670", 0.3f),
                    "BulbA" => Glow(Board.Hex("ffc9d6"), 0.6f),
                    "BulbB" => Glow(Board.Hex("d8ffd0"), 0.6f),
                    "BulbC" => Glow(Board.Hex("d9d4ff"), 0.6f),
                    "Speaker" => Mat("1d1b26", 0.25f),
                    "Cone" => Mat("3a3848", 0.5f),
                    "SignEdge" => Glow(Board.Hex("ffc93a"), 0.9f),
                    "SignFill" => Glow(Board.Hex("7d1fd6"), 1.1f),
                    "LedA" => ledA = Glow(Board.Hex("ffc14d"), 3),
                    "LedB" => ledB = Glow(Board.Hex("ffc14d"), 3),
                    "Truss" => Mat("3b3848", 0.4f),
                    "Star" => Glow(Board.Hex("ffd84a"), 2.5f),
                    _ => Mat("808080"),
                };
            }
            r.sharedMaterials = mats;
        }
    }

    // Mat : pas de reflets du ciel ni de taches speculaires (salle noire : sinon l'estrade se raye de clair).
    Material Mat(string hex, float smooth = 0.3f)
    {
        var m = new Material(lit) { color = Board.Hex(hex) };
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_EnvironmentReflections", 0); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        m.SetFloat("_SpecularHighlights", 0); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        return m;
    }
    Material Glow(Color c, float k) { var m = new Material(glowBase) { color = c }; m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * k); return m; }

    Light Spot(Vector3 pos, Vector3 at, Color c, float intensity, float angle)
    {
        var l = new GameObject("projecteur").AddComponent<Light>();
        l.transform.SetParent(transform, false);
        l.transform.localPosition = pos;
        l.transform.LookAt(transform.TransformPoint(at));
        l.type = LightType.Spot;
        l.spotAngle = angle;
        l.range = 22;
        l.intensity = intensity;
        l.color = c;
        return l;
    }

    // Les joueurs en ligne sur l'estrade, face au public ; jusqu'a 10, en arc leger.
    public void Build(Rhythm r, IList<string> avatars)
    {
        gameObject.SetActive(true);
        if (cast) Destroy(cast.gameObject);
        cast = new GameObject("musiciens").transform;
        cast.SetParent(transform, false);
        seats.Clear();
        int n = r.players.Count;
        float spacing = Mathf.Min(2.4f, 11f / Mathf.Max(1, n));
        for (int i = 0; i < n; i++)
        {
            float x = (i - (n - 1) / 2f) * spacing;
            var s = new Seat();
            float height = n > 6 ? 1.6f : 1.8f;
            s.root = Chars.Spawn(avatars.Count > i ? avatars[i] : Chars.Default, cast, new Vector3(x, Deck, -0.4f + x * x * 0.02f), 180 - x * 2, out s.an, height);
            s.guitar = GiveGuitar(s.root, s.an, height);
            seats.Add(s);
        }
        tennaState = null;
        TennaDance("Idle");
        TennaFace("Smile", 0);
    }

    public void Hide()
    {
        src.Stop();
        gameObject.SetActive(false);
    }

    // Guitare de Kris (Tools/rhythm_guitar_blender.py) : placee contre le ventre, manche vers la gauche du musicien,
    // puis accrochee a la poitrine pour suivre l'animation "Guitar Playing".
    static GameObject guitarModel;
    Transform GiveGuitar(Transform who, Animator an, float height)
    {
        guitarModel ??= Resources.Load<GameObject>("Rhythm/Guitar");
        if (!guitarModel || !an) return null;
        var g = Instantiate(guitarModel).transform;
        g.name = "guitare";
        foreach (var r in g.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = Regex.Match(mats[i] ? mats[i].name : "", "Guitar_([0-9a-f]{6})");
                mats[i] = Mat(m.Success ? m.Groups[1].Value : "70b8ff", 0.4f);
            }
            r.sharedMaterials = mats;
        }
        float k = height / 1.8f;
        g.SetParent(who, false);
        g.localPosition = GuitarPos * k / who.localScale.y;
        g.localRotation = Quaternion.Euler(GuitarRot);
        g.localScale = Vector3.one * 0.95f * k / who.localScale.y;
        var chest = an.GetBoneTransform(HumanBodyBones.Chest) ?? an.GetBoneTransform(HumanBodyBones.Spine);
        if (chest) g.SetParent(chest, true);
        return g;
    }
    // Reglage de la tenue de la guitare, dans le repere du personnage (face au public = +z, sa gauche = -x).
    static readonly Vector3 GuitarPos = new Vector3(-0.02f, 1.02f, 0.2f), GuitarRot = new Vector3(0, 0, 62);

    // Pendant les regles, Tenna est seul en scene.
    public void ShowCast(bool on) { if (cast) cast.gameObject.SetActive(on); }

    public Vector3 HeadOf(int seat) => seats[seat].root.position + Vector3.up * 2.1f;

    // Chaque image : chacun danse si son combo tient, et encaisse un moment apres un rate.
    public void Animate(Rhythm r)
    {
        for (int i = 0; i < seats.Count && i < r.players.Count; i++)
        {
            var s = seats[i];
            var p = r.players[i];
            // Pendant la chanson, tout le monde joue de la guitare ; a la fin, le gagnant exulte, les autres applaudissent.
            string want = r.Finished ? (p.score == r.players.Max(x => x.score) ? "Victory" : "Clap")
                        : r.phase == LivePhase.Play && running ? "Guitar" : "Idle";
            if (s.guitar) s.guitar.gameObject.SetActive(!r.Finished);
            if (want == s.state) continue;
            s.state = want;
            s.an.CrossFadeInFixedTime(want, 0.25f);
        }
    }

    public void Missed(int seat) { if (seat < seats.Count && Time.time > seats[seat].hitUntil) seats[seat].hitUntil = Time.time + 1.1f; }

    // Les projecteurs pulsent sur les temps de la chanson.
    public void Pulse(float songTime, float bpm)
    {
        float beat = songTime * bpm / 60f;
        for (int i = 0; i < beams.Count; i++)
        {
            float k = Mathf.Pow(1 - Mathf.Repeat(beat + i * 0.25f, 1), 3);
            beams[i].intensity = 5 + 14 * k;
        }
        // LEDs en chenillard : une sur deux allumee, on alterne a chaque demi-temps ; l'enseigne respire sur le temps.
        if (ledA && ledB)
        {
            bool odd = Mathf.FloorToInt(beat * 2) % 2 == 0;
            ledA.SetColor("_EmissionColor", Board.Hex("ffc14d") * (odd ? 6f : 0.35f));
            ledB.SetColor("_EmissionColor", Board.Hex("ffc14d") * (odd ? 0.35f : 6f));
        }
        float glow = Mathf.Pow(1 - Mathf.Repeat(beat, 1), 2);
        foreach (var l in signLights) l.intensity = 5 + 6 * glow;
    }

    // --- Musique ----------------------------------------------------------------------------
    public static string AudioPath(RSong s) => System.IO.Path.Combine(Application.streamingAssetsPath, "Rhythm", s.audio);

    public IEnumerator Load(RSong s)
    {
        if (loaded == s.id && clip) yield break;
        clip = null; loaded = null;
        string path = AudioPath(s);
        if (!System.IO.File.Exists(path)) { Debug.LogWarning("Musique absente : " + path); yield break; }
        using (var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), s.audio.EndsWith(".mp3") ? AudioType.MPEG : AudioType.OGGVORBIS))
        {
            ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = false;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { Debug.LogWarning("Musique illisible : " + req.error); yield break; }
            clip = DownloadHandlerAudioClip.GetContent(req);
        }
        clip.LoadAudioData();
        while (clip.loadState == AudioDataLoadState.Loading) yield return null;
        loaded = s.id;
    }

    // Horloge de la chanson : avance avec le temps du jeu (s'arrete en pause), recalee sur la lecture audio.
    float clock, startAt;
    bool running;
    public bool Running => running;
    // Ce qu'on entend a du retard sur ce qui est joue (tampons audio + reglage du joueur, fleches haut/bas).
    public static float Latency
    {
        get { AudioSettings.GetDSPBufferSize(out int len, out int num); return len * num / (float)AudioSettings.outputSampleRate + UserOffsetMs / 1000f; }
    }
    public static int UserOffsetMs { get => PlayerPrefs.GetInt("rh-offset", 0); set => PlayerPrefs.SetInt("rh-offset", Mathf.Clamp(value, -300, 300)); }
    public float SongTime => clock - Latency;
    public float Length => clip ? clip.length : 0;

    public void Play(RSong s, float leadIn, float volume)
    {
        running = true;
        clock = -leadIn;
        startAt = Time.time + leadIn;
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume * Mathf.Pow(10, s.vol / 20));   // "volume" du jeu d'origine, en dB
        src.Stop();
    }

    public void Stop() { running = false; src.Stop(); }

    public void Tick()
    {
        if (!running) return;
        clock += Time.deltaTime;
        if (!clip) return;
        if (!src.isPlaying && clock >= 0 && clock < clip.length - 0.1f && Time.timeScale > 0)
        {
            src.time = clock;
            src.Play();
        }
        if (src.isPlaying)
        {
            float audio = src.timeSamples / (float)clip.frequency;
            if (Mathf.Abs(audio - clock) > 0.05f) clock = audio;          // decrochage : on se recale franchement
            else clock = Mathf.Lerp(clock, audio, 0.1f);                     // sinon en douceur (pas de saccade)
        }
    }
}
