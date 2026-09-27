using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Createur de personnage (pieces Sidekick) : onglets de reglages a gauche, apercu 3D qui tourne a droite.
public partial class Ui
{
    VisualElement creatorScreen, creatorBody, creatorPreview;
    readonly List<Button> creatorTabs = new List<Button>();
    int creatorTab;
    Sidekick.Look look = new Sidekick.Look();
    Mix.Look mix = new Mix.Look();
    bool mixMode = true;
    Button modeBtn;
    string CurrentId => mixMode ? mix.Encode() : look.Encode();
    static readonly string[][] TabNames = { new[] { "Visage", "Cheveux", "Tenue", "Couleurs", "Silhouette" }, new[] { "Tête", "Haut", "Bas", "Chaussures", "Peau et taille" } };
    static readonly string[] SkinSwatches = { "f1c7a4", "c8905f", "6e4a33" };
    Action<string> onCreate;
    bool creatorFromPicker;
    Transform stage, previewChar;
    Camera previewCam;
    Animator previewAn;
    float spinPause, zoom = 1;
    bool dragging;

    void BuildCreator()
    {
        creatorScreen = Screen("dim");
        var panel = Panel(creatorScreen);
        panel.style.width = 1560;
        panel.style.height = 860;
        Text(panel, "Crée ton personnage", "panel-title");
        var cols = Div(panel, "row", "cr-cols");
        var left = Div(cols, "cr-left");
        var bar = Div(left, "tabs");
        string[] names = { "Visage", "Cheveux", "Tenue", "Couleurs", "Silhouette" };
        for (int i = 0; i < names.Length; i++)
        {
            int k = i;
            var t = new Button(() => { Sound.I.UI("tick"); CreatorTab(k); }) { text = names[i] };
            t.AddToClassList("tab");
            bar.Add(t);
            creatorTabs.Add(t);
        }
        creatorBody = Add(left, new ScrollView(), "settings-scroll");
        creatorPreview = Div(cols, "cr-preview");
        // Glisser pour tourner le personnage, molette pour zoomer.
        creatorPreview.RegisterCallback<PointerDownEvent>(e => { dragging = true; creatorPreview.CapturePointer(e.pointerId); });
        creatorPreview.RegisterCallback<PointerUpEvent>(e => { dragging = false; creatorPreview.ReleasePointer(e.pointerId); spinPause = 4; });
        creatorPreview.RegisterCallback<PointerMoveEvent>(e => { if (dragging && previewChar) { previewChar.Rotate(0, -e.deltaPosition.x * 0.5f, 0); spinPause = 4; } });
        creatorPreview.RegisterCallback<WheelEvent>(e => { zoom = Mathf.Clamp(zoom + e.delta.y * 0.05f, 0.5f, 1.6f); e.StopPropagation(); });
        Text(creatorPreview, "Glisse pour tourner · molette pour zoomer", "cr-hint").pickingMode = PickingMode.Ignore;
        var bottom = Div(panel, "row", "bottom-row");
        Ico(Btn(bottom, "Annuler", CloseCreator, "ghost", "small"), "back");
        Btn(bottom, "Au hasard", () => { if (mixMode) mix = RandomMix(); else look = RandomLook(); CreatorRefresh(); }, "blue", "small");
        modeBtn = Btn(bottom, "", () => { mixMode = !mixMode; creatorTab = -1; CreatorRefresh(); }, "ghost", "small");
        Div(bottom, "grow");
        Ico(Btn(bottom, "Enregistrer", SaveCreator, "green"), "check");
    }

    void OpenCreator(string start, Action<string> save, bool fromPicker)
    {
        mixMode = !Sidekick.IsLook(start) || !Sidekick.Available;
        look = Sidekick.IsLook(start) ? Sidekick.Look.Decode(start) : new Sidekick.Look();
        mix = Mix.IsLook(start) ? Mix.Look.Decode(start) : new Mix.Look();
        modeBtn.style.display = Sidekick.Available ? DisplayStyle.Flex : DisplayStyle.None;
        onCreate = save;
        creatorFromPicker = fromPicker;
        if (!stage)
        {
            stage = new GameObject("StudioCreateur").transform;
            stage.position = new Vector3(0, -300, 0);
            previewCam = new GameObject("CameraCreateur").AddComponent<Camera>();
            previewCam.transform.SetParent(stage, false);
            previewCam.transform.localPosition = new Vector3(0, 1.05f, -3.9f);
            previewCam.transform.LookAt(stage.position + Vector3.up * 0.95f);
            previewCam.fieldOfView = 32;
            previewCam.clearFlags = CameraClearFlags.SolidColor;
            previewCam.backgroundColor = new Color(0, 0, 0, 0);
            previewCam.targetTexture = new RenderTexture(900, 1100, 24, RenderTextureFormat.ARGB32);
            creatorPreview.style.backgroundImage = Background.FromRenderTexture(previewCam.targetTexture);
        }
        CreatorRefresh();
        CreatorTab(0);
        Go(creatorScreen);
    }

    void CloseCreator() => Back();

    void SaveCreator()
    {
        var id = CurrentId;
        var cb = onCreate;
        Back();
        if (creatorFromPicker) Back();
        cb?.Invoke(id);
    }

    // L'apercu ne tourne (et ne coute) que pendant que l'ecran est ouvert.
    // Visage et cheveux : gros plan sur la tete ; le reste : en pied.
    void LateUpdate()
    {
        bool open = current == creatorScreen;
        if (previewCam) previewCam.enabled = open;
        if (!open || !previewChar) return;
        if (!dragging && (spinPause -= Time.deltaTime) <= 0) previewChar.Rotate(0, 20 * Time.deltaTime, 0);
        bool face = mixMode ? creatorTab == 0 : creatorTab <= 1;
        var head = previewAn ? previewAn.GetBoneTransform(HumanBodyBones.Head) : null;
        var look = face && head ? head.position + Vector3.up * 0.06f : stage.position + Vector3.up * 0.95f;
        float dist = (face ? 1.35f : 3.9f) * zoom;
        var want = look + new Vector3(0, face ? 0.02f : 0.1f, -dist);
        float k = 1 - Mathf.Exp(-8 * Time.unscaledDeltaTime);
        var t = previewCam.transform;
        t.position = Vector3.Lerp(t.position, want, k);
        camLook = Vector3.Lerp(camLook, look, k);
        t.LookAt(camLook);
    }

    Vector3 camLook;

    void CreatorRefresh()
    {
        float rot = previewChar ? previewChar.localEulerAngles.y : 180;
        if (previewChar) Destroy(previewChar.gameObject);
        previewChar = Chars.Spawn(CurrentId, stage, Vector3.zero, rot, out previewAn);
        ((Label)modeBtn.Q(className: "gl-label")).text = mixMode ? "Style fantaisie" : "Style à la carte";
        CreatorTab(Mathf.Max(0, creatorTab));
    }

    void CreatorTab(int k)
    {
        if (k != creatorTab) zoom = 1;
        creatorTab = k;
        for (int i = 0; i < creatorTabs.Count; i++) { creatorTabs[i].EnableInClassList("selected", i == k); creatorTabs[i].text = TabNames[mixMode ? 1 : 0][i]; }
        creatorBody.Clear();
        if (mixMode) { MixTab(k); return; }
        var l = look;
        switch (k)
        {
            case 0:
                Step("Visage", $"{l.head} / {Sidekick.Heads}", d => l.head = Cycle(l.head, d, 1, Sidekick.Heads));
                Step("Nez", $"{l.nose} / {Sidekick.Noses}", d => l.nose = Cycle(l.nose, d, 1, Sidekick.Noses));
                Step("Oreilles", $"{l.ears} / {Sidekick.Ears}", d => l.ears = Cycle(l.ears, d, 1, Sidekick.Ears));
                Step("Sourcils", $"{l.brows} / {Sidekick.Brows}", d => l.brows = Cycle(l.brows, d, 1, Sidekick.Brows));
                Step("Barbe", l.beard == 0 ? "Aucune" : $"{l.beard} / {Sidekick.Beards}", d => l.beard = Cycle(l.beard, d, 0, Sidekick.Beards));
                Swatches("Yeux", Sidekick.EyeColors, l.eyes, i => l.eyes = i);
                break;
            case 1:
                Step("Coupe", l.hair == 0 ? "Chauve" : $"{l.hair} / {Sidekick.Hairs}", d => l.hair = Cycle(l.hair, d, 0, Sidekick.Hairs));
                Swatches("Couleur", Sidekick.HairColors, l.hairColor, i => l.hairColor = i);
                break;
            case 2:
                Choice("Haut", Sidekick.Outfits, l.top, v => l.top = v);
                Choice("Mains", Sidekick.Outfits, l.hands, v => l.hands = v);
                Choice("Bas", Sidekick.Outfits, l.bottom, v => l.bottom = v);
                Choice("Chaussures", Sidekick.Outfits, l.feet, v => l.feet = v);
                Choice("Couvre-chef", Sidekick.HatSets, l.hat, v => l.hat = v);
                Choice("Visage (accessoire)", Sidekick.AccSets, l.face, v => l.face = v);
                Choice("Dos", Sidekick.AccSets, l.back, v => l.back = v);
                break;
            case 3:
                Swatches("Peau", Sidekick.SkinTones, l.skin, i => l.skin = i);
                Step("Couleurs de la tenue", $"{l.palette} / {Sidekick.Palettes}", d => l.palette = Cycle(l.palette, d, 1, Sidekick.Palettes));
                Swatches("Cheveux et barbe", Sidekick.HairColors, l.hairColor, i => l.hairColor = i);
                Swatches("Yeux", Sidekick.EyeColors, l.eyes, i => l.eyes = i);
                break;
            default:
                Shape("Masculin — Féminin", -100, 100, l.fem, v => l.fem = v);
                Shape("Musculature", 0, 100, l.muscle, v => l.muscle = v);
                Shape("Mince — Enveloppé", -100, 100, l.weight, v => l.weight = v);
                Shape("Taille", 85, 115, l.height, v => l.height = v);
                break;
        }
    }

    // Autotest : onglet, tirage au hasard, look courant.
    public void CreatorTest(int tab, bool random) { if (random) { if (mixMode) mix = RandomMix(); else look = RandomLook(); CreatorRefresh(); } CreatorTab(tab); }
    public string CreatorLook => CurrentId;
    public void CreatorMode(bool mixed) { mixMode = mixed; CreatorRefresh(); }

    // Style a la carte : chaque morceau (tete, haut, bas, chaussures) pris sur n'importe quel perso des packs.
    void MixTab(int k)
    {
        if (k == 4)
        {
            int cur = Math.Max(0, Array.IndexOf(new[] { "A", "B", "C" }, mix.skin));
            Swatches("Peau", SkinSwatches, cur, i => mix.skin = "ABC".Substring(i, 1));
            MixHeight();
            return;
        }
        var part = (Mix.Part)k;
        var p = mix[part].Split('/');
        var models = Chars.Models;
        int model = Math.Max(0, Array.FindIndex(models, m => m.pack == p[0] && m.mesh == p[1]));
        int pal = 1;
        if (p.Length > 2) int.TryParse(p[2], out pal);
        Step(k == 0 ? "Tête (et coiffure)" : "Modèle", models[model].label, d =>
        {
            var m = models[Cycle(model, d, 0, models.Length - 1)];
            mix[part] = $"{m.pack}/{m.mesh}/{Mathf.Min(pal, Mix.Palettes(m.pack))}";
        });
        Step(k == 0 ? "Couleur des cheveux" : "Couleurs", $"{pal} / {Mix.Palettes(p[0])}", d => mix[part] = $"{p[0]}/{p[1]}/{Cycle(pal, d, 1, Mix.Palettes(p[0]))}");
        if (k == 0) Text(creatorBody, "Astuce : prends le haut, le bas et les chaussures de personnages différents !", "small-note");
    }

    void MixHeight()
    {
        var box = CreatorRow("Taille");
        var s = Add(box, new Slider(85, 115) { value = mix.height });
        s.RegisterValueChangedCallback(e => mix.height = Mathf.RoundToInt(e.newValue));
        s.RegisterCallback<PointerCaptureOutEvent>(_ => CreatorRefresh());
    }

    static Mix.Look RandomMix()
    {
        var r = new System.Random();
        string Piece() { var m = Chars.Models[r.Next(Chars.Models.Length)]; return $"{m.pack}/{m.mesh}/{r.Next(1, Mix.Palettes(m.pack) + 1)}"; }
        return new Mix.Look { head = Piece(), top = Piece(), bottom = Piece(), feet = Piece(), skin = "ABC".Substring(r.Next(3), 1), height = r.Next(92, 109) };
    }

    static int Cycle(int v, int d, int min, int max) => v + d > max ? min : v + d < min ? max : v + d;

    VisualElement CreatorRow(string label)
    {
        var row = Div(creatorBody, "setting");
        Text(row, label, "setting-label");
        return Div(row, "row", "cr-field");
    }

    void Step(string label, string value, Action<int> change)
    {
        var box = CreatorRow(label);
        Ico(Btn(box, "", () => { change(-1); CreatorRefresh(); }, "ghost", "small", "square"), "back");
        Text(box, value, "cr-value");
        var next = Ico(Btn(box, "", () => { change(1); CreatorRefresh(); }, "ghost", "small", "square"), "back");
        next.Q(className: "gl-ico").style.rotate = new Rotate(180);
    }

    void Choice(string label, (string set, string label)[] options, string value, Action<string> set)
    {
        int i = Math.Max(0, Array.FindIndex(options, o => o.set == value));
        Step(label, options[i].label, d => set(options[Cycle(i, d, 0, options.Length - 1)].set));
    }

    void Swatches(string label, string[] colors, int value, Action<int> set)
    {
        var box = CreatorRow(label);
        box.AddToClassList("cr-swatches");
        for (int i = 0; i < colors.Length; i++)
        {
            int k = i;
            var b = new Button(() => { Sound.I.UI("tick"); set(k); CreatorRefresh(); });
            b.AddToClassList("swatch");
            b.EnableInClassList("selected", i == value);
            b.style.backgroundColor = Board.Hex(colors[i]);
            box.Add(b);
        }
    }

    // Curseur : la morphologie change en direct, sans reconstruire le personnage.
    void Shape(string label, int min, int max, int value, Action<int> set)
    {
        var box = CreatorRow(label);
        var s = Add(box, new Slider(min, max) { value = value });
        s.RegisterValueChangedCallback(e =>
        {
            set(Mathf.RoundToInt(e.newValue));
            if (!previewChar) return;
            Sidekick.Shape(previewChar.gameObject, look);
            if (label == "Taille") { float rot = previewChar.localEulerAngles.y; Destroy(previewChar.gameObject); previewChar = Chars.Spawn(CurrentId, stage, Vector3.zero, rot, out previewAn); }
        });
    }

    static Sidekick.Look RandomLook()
    {
        var r = new System.Random();
        var o = Sidekick.Outfits;
        return new Sidekick.Look
        {
            head = r.Next(1, Sidekick.Heads + 1), hair = r.Next(0, Sidekick.Hairs + 1), brows = r.Next(1, Sidekick.Brows + 1),
            beard = r.Next(3) == 0 ? r.Next(1, Sidekick.Beards + 1) : 0, nose = r.Next(1, Sidekick.Noses + 1), ears = r.Next(1, Sidekick.Ears + 1),
            top = o[r.Next(o.Length)].set, hands = o[r.Next(o.Length)].set, bottom = o[r.Next(o.Length)].set, feet = o[r.Next(o.Length)].set,
            hat = r.Next(4) == 0 ? Sidekick.HatSets[r.Next(1, Sidekick.HatSets.Length)].set : "",
            palette = r.Next(1, Sidekick.Palettes + 1), skin = r.Next(Sidekick.SkinTones.Length), hairColor = r.Next(Sidekick.HairColors.Length),
            eyes = r.Next(Sidekick.EyeColors.Length), fem = r.Next(-100, 101), muscle = r.Next(0, 60), weight = r.Next(-60, 61), height = r.Next(90, 111),
        };
    }
}
