using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Createur de personnages (Sidekick) : apercu 3D a gauche (glisser pour tourner), onglets a droite :
// Modeles (personnages du pack, mes creations, anciens personnages), Corps (silhouette, corpulence, muscles, taille, peau),
// Visage, Cheveux, Haut, Bas, Accessoires (pieces et couleurs). Au hasard, Annuler, Valider.
public partial class Ui
{
    VisualElement crPreview, crBody;
    readonly List<Button> crTabs = new List<Button>();
    Label crInfo;
    CreatorStudio studio;
    Sidekick.Look crLook;
    Action<string> crPick;
    string crTab = "Modèles";
    int crColorOpen = -1;
    static readonly string[] CrTabNames = { "Modèles", "Corps", "Visage", "Cheveux", "Haut", "Bas", "Accessoires" };
    const string SavesKey = "sk-creations";

    void BuildCreator()
    {
        creator = Page(out var panel, out var bottom, "Crée ton", "personnage", "m-creator");
        var bar = Div(panel, "m-tabs-row", "center");
        foreach (var n in CrTabNames)
        {
            var name = n;
            var t = new Button(() => { Sound.I.UI("tick"); CreatorTab(name); }) { text = n };
            t.AddToClassList("m-tab");
            Corners(t);
            bar.Add(t);
            crTabs.Add(t);
        }
        var row = Div(panel, "row", "creator-row");
        var left = Div(row, "creator-left");
        crPreview = Div(left, "creator-preview");
        // Glisser pour tourner (avec elan au lacher).
        bool drag = false; float last = 0, vel = 0;
        crPreview.RegisterCallback<PointerDownEvent>(e => { drag = true; last = e.position.x; vel = 0; crPreview.CapturePointer(e.pointerId); });
        crPreview.RegisterCallback<PointerMoveEvent>(e => { if (!drag) return; float dx = e.position.x - last; last = e.position.x; vel = dx; studio?.Drag(dx); });
        crPreview.RegisterCallback<PointerUpEvent>(e => { drag = false; crPreview.ReleasePointer(e.pointerId); studio?.Fling(vel); });
        var zoomRow = Div(left, "row", "creator-zoom");
        Btn(zoomRow, "En pied", () => studio?.Focus(false), "m-dark", "small");
        Btn(zoomRow, "Visage", () => studio?.Focus(true), "m-dark", "small");
        Btn(zoomRow, "Coucou !", () => studio?.Wave(), "m-dark", "small");
        Text(left, "Glisse sur le personnage pour le faire tourner", "creator-hint");
        crBody = Div(Add(row, new ScrollView(), "settings-scroll", "creator-scroll"), "creator-body");
        Ico(Btn(bottom, "Annuler", () => { studio?.Show(false); Back(); }, "m-dark", "small"), "back");
        Btn(bottom, "Au hasard", () => { crLook = Sidekick.Random(); CreatorRefresh(); }, "m-blue", "small");
        Div(bottom, "grow");
        crInfo = Text(bottom, "", "pick-name");
        Ico(Btn(bottom, "Valider", () => { var code = crLook.Code(); studio?.Show(false); Back(); crPick?.Invoke(code); }, "m-gold"), "check");
    }

    // Ouvre le createur (ou l'ancien choix de personnage si le pack n'est pas installe).
    void OpenCreator(Action<string> pick, string current)
    {
        crPick = pick;
        var code = current != null && Chars.Friends.TryGetValue(current, out var f) ? f : current;
        crLook = Sidekick.IsCode(code) ? Sidekick.Look.Parse(code) : Sidekick.Look.FromPreset(4);
        if (!studio) studio = CreatorStudio.Make();
        studio.Show(true);
        crPreview.style.backgroundImage = Background.FromRenderTexture(studio.Target);
        CreatorTab(Sidekick.IsCode(current) ? "Corps" : "Modèles");
        Go(creator);
    }

    void CreatorTab(string name)
    {
        crTab = name;
        crColorOpen = -1;
        for (int i = 0; i < crTabs.Count; i++) crTabs[i].EnableInClassList("selected", CrTabNames[i] == name);
        studio?.Focus(name == "Visage" || name == "Cheveux");
        CreatorRefresh();
    }

    void CreatorRefresh()
    {
        studio?.Set(crLook.Code());
        crBody.Clear();
        crInfo.text = "";
        switch (crTab)
        {
            case "Modèles": ModelsTab(); break;
            case "Corps":
                CrTitle("Silhouette");
                CrSlider("Masculin  ↔  Féminin", -100, 100, crLook.type, v => crLook.type = v, v => v < -33 ? "Masculin" : v > 33 ? "Féminin" : "Entre les deux");
                CrSlider("Mince  ↔  Corpulent", -100, 100, crLook.size, v => crLook.size = v, v => v < -50 ? "Mince" : v < 20 ? "Moyen" : v < 60 ? "Rond" : "Corpulent");
                CrSlider("Muscles", -100, 100, crLook.muscle, v => crLook.muscle = v, v => v < -40 ? "Peu" : v < 40 ? "Normal" : "Costaud");
                CrSlider("Taille", 85, 115, crLook.height * 100, v => crLook.height = v / 100, v => Mathf.RoundToInt(170 * v / 100) + " cm");
                CrColors("Corps");
                break;
            case "Visage":
                CrTitle("Expression");
                CrSlider("Sourire", 0, 100, crLook.smile, v => crLook.smile = v, v => v < 15 ? "Neutre" : v < 60 ? "Léger" : "Grand");
                CrParts("EB", "EA", "NO", "FH"); CrColors("Visage"); break;   // pas les dents : la bouche reste fermee
            case "Cheveux": CrParts("HR"); CrColors("Cheveux"); break;
            case "Haut": CrParts("TO", "AU", "AL", "HN"); CrColors("Haut"); break;
            case "Bas": CrParts("HI", "LE", "FO"); CrColors("Bas"); break;
            case "Accessoires": CrParts("AH", "AF", "AB", "AS"); CrColors("Accessoires"); break;
        }
    }

    void CrTitle(string t) => Text(crBody, t, "h2", "creator-h2");

    // --- Modeles : personnages du pack, mes creations, anciens personnages ---
    void ModelsTab()
    {
        CrTitle("Pars d'un modèle");
        var grid = Div(crBody, "avatar-grid", "creator-grid");
        var presets = Sidekick.Presets;
        for (int i = 0; i < presets.Length; i++)
        {
            int k = i;
            var code = Sidekick.Look.FromPreset(i).Code();
            var label = presets[i].name.StartsWith("Human") ? "Corps " + presets[i].name.Substring(presets[i].name.Length - 1) : "Tenue " + presets[i].name.Substring(presets[i].name.Length - 1);
            PickCell(grid, code, label, 110, () => { crLook = Sidekick.Look.FromPreset(k); CreatorRefresh(); });
        }
        CrTitle("Mes créations");
        var saves = Saves();
        var sg = Div(crBody, "avatar-grid", "creator-grid");
        foreach (var s in saves)
        {
            var code = s;
            var cell = PickCell(sg, code, null, 110, () => { crLook = Sidekick.Look.Parse(code); CreatorRefresh(); });
            Btn(cell, "Effacer", () => { var l = Saves(); l.Remove(code); SetSaves(l); CreatorRefresh(); }, "m-dark", "small", "creator-del");
        }
        if (saves.Count == 0) Text(crBody, "Aucune création sauvegardée pour l'instant.", "muted");
        var r = Div(crBody, "row", "creator-actions");
        Ico(Btn(r, "Sauvegarder ce personnage", () =>
        {
            var l = Saves(); var c = crLook.Code();
            if (!l.Contains(c)) { l.Insert(0, c); if (l.Count > 12) l.RemoveAt(l.Count - 1); SetSaves(l); }
            CreatorRefresh();
        }, "m-gold", "small"), "check");
        CrTitle("Mes amis");
        var fg = Div(crBody, "avatar-grid", "creator-grid");
        // La bande du casting (outil -casting) ; a defaut, les amis d'origine.
        var band = LoadCast(Resources.Load<TextAsset>("MenuCast")?.text);
        if (band.Count == 0) foreach (var kv in Chars.Friends) band.Add((Chars.Label(kv.Key), kv.Value));
        foreach (var (name, code) in band)
            PickCell(fg, code, name, 110, () => { crLook = Sidekick.Look.Parse(code); CreatorRefresh(); });
    }
    static List<string> Saves() => PlayerPrefs.GetString(SavesKey, "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(Sidekick.IsCode).ToList();
    static void SetSaves(List<string> l) { PlayerPrefs.SetString(SavesKey, string.Join("|", l)); PlayerPrefs.Save(); }

    // --- Curseur de morphologie ---
    void CrSlider(string label, float min, float max, float value, Action<float> set, Func<float, string> fmt)
    {
        var row = Div(crBody, "creator-line");
        Text(row, label, "creator-label");
        var box = Div(row, "row", "creator-field");
        var s = Add(box, new Slider(min, max) { value = value }, "creator-slider");
        var v = Text(box, fmt(value), "value", "creator-value");
        s.RegisterValueChangedCallback(e => { v.text = fmt(e.newValue); set(e.newValue); studio?.Set(crLook.Code()); });
    }

    // --- Pieces : fleche gauche / droite, "Modele 3 / 15" ---
    void CrParts(params string[] keys)
    {
        CrTitle("Pièces");
        foreach (var key in keys)
        {
            var s = Sidekick.SlotOf(key);
            var row = Div(crBody, "creator-line");
            Text(row, s.label, "creator-label");
            var box = Div(row, "row", "creator-field");
            crLook.parts.TryGetValue(key, out var cur);
            if (cur == null) cur = s.optional ? "" : s.options.FirstOrDefault(o => o.StartsWith("b")) ?? s.options[0];
            int idx = Math.Max(0, s.options.IndexOf(cur));
            void Step(int d) { int i = (idx + d + s.options.Count) % s.options.Count; crLook.parts[key] = s.options[i]; CreatorRefresh(); }
            Btn(box, "‹", () => Step(-1), "m-dark", "small", "creator-arrow");
            Text(box, $"{Sidekick.OptionLabel(s, cur)}   ({idx + 1} / {s.options.Count})", "creator-part");
            Btn(box, "›", () => Step(1), "m-dark", "small", "creator-arrow");
        }
    }

    // --- Couleurs de l'onglet : pastilles ; "Plus…" ouvre teinte / saturation / luminosite ---
    void CrColors(string tab)
    {
        CrTitle("Couleurs");
        for (int z = 0; z < Sidekick.Zones.Length; z++)
        {
            var zone = Sidekick.Zones[z];
            if (zone.tab != tab || !Sidekick.ZoneUsed(crLook, z)) continue;
            int zi = z;
            var cur = (Color)Sidekick.ZoneColor(crLook, zi);
            var row = Div(crBody, "creator-color");
            var head = Div(row, "row");
            var dot = Div(head, "creator-current"); dot.style.backgroundColor = cur;
            Text(head, zone.label, "creator-label");
            Div(head, "grow");
            Btn(head, crColorOpen == zi ? "Moins" : "Plus…", () => { crColorOpen = crColorOpen == zi ? -1 : zi; CreatorRefresh(); }, "m-dark", "small");
            var sw = Div(row, "row", "creator-swatches");
            foreach (var hex in zone.swatches)
            {
                ColorUtility.TryParseHtmlString("#" + hex, out var c);
                var b = new Button(() => { Sound.I.UI("tick"); crLook.colors[zi] = c; CreatorRefresh(); });
                b.AddToClassList("creator-swatch");
                b.style.backgroundColor = c;
                b.EnableInClassList("selected", ColorUtility.ToHtmlStringRGB(cur).Equals(hex, StringComparison.OrdinalIgnoreCase));
                sw.Add(b);
            }
            if (crColorOpen != zi) continue;
            Color.RGBToHSV(cur, out float h, out float sa, out float va);
            var hsv = new[] { h, sa, va };
            string[] names = { "Teinte", "Saturation", "Luminosité" };
            for (int k = 0; k < 3; k++)
            {
                int kk = k;
                var line = Div(row, "row", "creator-hsv");
                Text(line, names[k], "creator-small");
                var s = Add(line, new Slider(0, 1) { value = hsv[k] }, "creator-slider");
                s.RegisterValueChangedCallback(e =>
                {
                    hsv[kk] = e.newValue;
                    var c = Color.HSVToRGB(hsv[0], hsv[1], hsv[2]);
                    crLook.colors[zi] = c;
                    dot.style.backgroundColor = c;
                    studio?.Set(crLook.Code());
                });
            }
        }
    }
}
