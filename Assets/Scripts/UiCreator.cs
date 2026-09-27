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
    Action<string> onCreate;
    bool creatorFromPicker;
    Transform stage, previewChar;
    Camera previewCam;

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
        var bottom = Div(panel, "row", "bottom-row");
        Ico(Btn(bottom, "Annuler", CloseCreator, "ghost", "small"), "back");
        Btn(bottom, "Au hasard", () => { look = RandomLook(); CreatorRefresh(); }, "blue", "small");
        Div(bottom, "grow");
        Ico(Btn(bottom, "Enregistrer", SaveCreator, "green"), "check");
    }

    void OpenCreator(string start, Action<string> save, bool fromPicker)
    {
        look = Sidekick.IsLook(start) ? Sidekick.Look.Decode(start) : new Sidekick.Look();
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
        var id = look.Encode();
        var cb = onCreate;
        Back();
        if (creatorFromPicker) Back();
        cb?.Invoke(id);
    }

    // L'apercu ne tourne (et ne coute) que pendant que l'ecran est ouvert.
    void LateUpdate()
    {
        bool open = current == creatorScreen;
        if (previewCam) previewCam.enabled = open;
        if (open && previewChar) previewChar.Rotate(0, 25 * Time.deltaTime, 0);
    }

    void CreatorRefresh()
    {
        float rot = previewChar ? previewChar.localEulerAngles.y : 180;
        if (previewChar) Destroy(previewChar.gameObject);
        previewChar = Chars.Spawn(look.Encode(), stage, Vector3.zero, rot, out _);
        CreatorTab(creatorTab);
    }

    void CreatorTab(int k)
    {
        creatorTab = k;
        for (int i = 0; i < creatorTabs.Count; i++) creatorTabs[i].EnableInClassList("selected", i == k);
        creatorBody.Clear();
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
    public void CreatorTest(int tab, bool random) { if (random) { look = RandomLook(); CreatorRefresh(); } CreatorTab(tab); }
    public string CreatorLook => look.Encode();

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
            if (label == "Taille") { float rot = previewChar.localEulerAngles.y; Destroy(previewChar.gameObject); previewChar = Chars.Spawn(look.Encode(), stage, Vector3.zero, rot, out _); }
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
