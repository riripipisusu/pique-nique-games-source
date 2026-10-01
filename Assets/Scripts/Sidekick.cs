using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

// Personnages sur mesure (pack Synty Sidekick "Modern Civilians") : pieces modulaires greffees sur un squelette commun,
// morphologie par blend shapes, couleurs dans une grille 32x32 (une case = une zone : peau, cheveux, haut...).
// Un personnage tient dans un code texte "sk1_..." (sans | ni ;) : il passe tel quel en ligne et dans les sauvegardes.
// Donnees : Resources/Sidekick (Editor/SidekickSetup), pieces : Resources/Meshes du pack.
public static class Sidekick
{
    [Serializable] class Part { public string slot, name, path; }
    [Serializable] public class Preset { public string name; public string[] parts; public float type, size, muscle; public string colors; }
    [Serializable] class Catalog { public Part[] parts; public Preset[] presets; }

    static Catalog cat;
    static bool loaded;
    static Catalog Cat
    {
        get
        {
            if (loaded) return cat;
            loaded = true;
            var t = Resources.Load<TextAsset>("Sidekick/catalog");
            if (t) cat = JsonUtility.FromJson<Catalog>(t.text);
            return cat;
        }
    }
    public static bool Available => Cat != null && Cat.parts.Length > 0 && Resources.Load<GameObject>("Sidekick/Skeleton");
    public static Preset[] Presets => Cat?.presets ?? new Preset[0];

    // --- Emplacements proposes (paire gauche/droite : meme modele des deux cotes) --------------------------
    public class Slot
    {
        public string key, label, left, right; public bool optional;
        public List<string> options = new List<string>();   // "b01" (corps de base), "c07" (tenue n7) ; "" = rien
    }
    public static readonly Slot[] Slots =
    {
        new Slot { key = "HR", label = "Coupe", left = "HAIR", optional = true },
        new Slot { key = "EB", label = "Sourcils", left = "EBRL", right = "EBRR", optional = true },
        new Slot { key = "EA", label = "Oreilles", left = "EARL", right = "EARR" },
        new Slot { key = "NO", label = "Nez", left = "NOSE" },
        new Slot { key = "FH", label = "Barbe", left = "FCHR", optional = true },
        new Slot { key = "TE", label = "Dents", left = "TETH" },
        new Slot { key = "TO", label = "Haut", left = "TORS" },
        new Slot { key = "AU", label = "Épaules et bras", left = "AUPL", right = "AUPR" },
        new Slot { key = "AL", label = "Avant-bras", left = "ALWL", right = "ALWR" },
        new Slot { key = "HN", label = "Mains", left = "HNDL", right = "HNDR" },
        new Slot { key = "HI", label = "Bas (taille)", left = "HIPS" },
        new Slot { key = "LE", label = "Jambes", left = "LEGL", right = "LEGR" },
        new Slot { key = "FO", label = "Chaussures", left = "FOTL", right = "FOTR" },
        new Slot { key = "AH", label = "Chapeau", left = "AHED", optional = true },
        new Slot { key = "AF", label = "Lunettes, masque", left = "AFAC", optional = true },
        new Slot { key = "AB", label = "Sac, dos", left = "ABAC", optional = true },
        new Slot { key = "AS", label = "Épaulettes", left = "ASHL", right = "ASHR", optional = true },
    };
    static readonly string[] Fixed = { "HEAD", "EYEL", "EYER", "TONG" };   // toujours le corps de base 01
    static Dictionary<string, Part> byName;
    public static Slot SlotOf(string key) => Slots.First(s => s.key == key);

    static void Index()
    {
        if (byName != null || Cat == null) return;
        byName = Cat.parts.ToDictionary(p => p.name);
        foreach (var s in Slots)
        {
            s.options.Clear();
            if (s.optional) s.options.Add("");
            s.options.AddRange(Cat.parts.Where(p => p.slot == s.left).Select(p => PartCode(p.name)).Distinct().OrderBy(c => c));
        }
    }
    // SK_HUMN_BASE_03_02HAIR_HU01 -> "b03" ; SK_MDRN_CIVL_12_10TORS_HU01 -> "c12"
    static string PartCode(string name) { var p = name.Split('_'); return (p[1] == "HUMN" ? "b" : "c") + p[3]; }
    static Part Find(string slot, string code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        string fam = code[0] == 'b' ? "HUMN_BASE" : "MDRN_CIVL";
        return Cat.parts.FirstOrDefault(p => p.slot == slot && p.name.StartsWith($"SK_{fam}_{code.Substring(1)}_"));
    }
    // Une zone de couleur sert-elle au personnage ? (une de ses cases est utilisee par une de ses pieces)
    // Les levres n'ont pas de case a elles (peintes avec la peau) ; les meches n'existent que sur quelques coupes.
    static readonly Dictionary<string, HashSet<int>> partCells = new Dictionary<string, HashSet<int>>();
    public static bool ZoneUsed(Look l, int zone)
    {
        if (Zones[zone].label == "Lèvres") return false;
        Index();
        var codes = Slots.Select(s => (s.left, l.parts.TryGetValue(s.key, out var c) ? c : null)).Concat(Fixed.Select(f => (f, "b01")));
        foreach (var (slot, code) in codes)
        {
            var p = Find(slot, code);
            if (p == null) continue;
            if (!partCells.TryGetValue(p.path, out var cells))
            {
                var smr = Resources.Load<GameObject>(p.path)?.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var m = smr ? smr.sharedMesh : null;
                if (m == null || !m.isReadable) return true;   // illisible : on la montre
                cells = new HashSet<int>(m.uv.Select(u => Mathf.Clamp(Mathf.FloorToInt(u.x * 32), 0, 31) + 32 * Mathf.Clamp(Mathf.FloorToInt(u.y * 32), 0, 31)));
                partCells[p.path] = cells;
            }
            if (Zones[zone].cells.Any(cells.Contains)) return true;
        }
        return false;
    }

    public static string OptionLabel(Slot s, string code) =>
        string.IsNullOrEmpty(code) ? "Aucun" : code[0] == 'b' ? (s.optional ? $"Naturel {int.Parse(code.Substring(1))}" : "De base") : $"Modèle {int.Parse(code.Substring(1))}";

    // --- Zones de couleur (cases de la grille) ---------------------------------------------------------
    public class Zone { public string label, tab; public int[] cells; public int[] shade; public float shadeMul = 1; public string[] swatches; }
    static readonly string[] SkinSw = { "fcd9c0", "fcc19c", "f1c7a5", "eab38b", "d9a78b", "bf9062", "ac7c56", "8f644a", "805843", "68493e", "553d34", "3d2b24" };
    static readonly string[] HairSw = { "101010", "2b1d14", "4a2e1c", "6b4226", "905c32", "b06545", "d4884c", "c9a15b", "e6c98a", "f3e6c4", "9a9a9a", "e8e8e8", "722e3b", "c2366b", "7a3fa8", "3a5fa8", "2e8a8a", "3f9a52" };
    static readonly string[] EyeSw = { "282119", "3b3125", "6b4a27", "8a6a2f", "5d7095", "3f6fb7", "5fa0d8", "2e8a57", "6f8f4f", "7a7a82", "9b3fb0", "c0392b" };
    public static readonly string[] ClothSw =
    {
        "f7f4ec", "d6d2c8", "9a9a9a", "5a5a5a", "2a2a2e", "121214", "e8403a", "b3262b", "7a1f2b", "f28c1c", "f7d61c", "c9a227",
        "5fbf3a", "2e7a3f", "1f4d33", "3fc4c9", "3a86d6", "2a4d8f", "1b2a52", "8a5cff", "5a2f8f", "e8408a", "f7a8c8", "9c7842",
        "6b4a2b", "3d2b1f", "d8c3a0", "a89370",
    };
    public static readonly Zone[] Zones =
    {
        new Zone { label = "Peau", tab = "Corps", cells = new[] { 320, 352, 322, 324, 66 }, shade = new[] { 258, 260, 194, 196, 130 }, shadeMul = 0.86f, swatches = SkinSw },
        new Zone { label = "Yeux", tab = "Visage", cells = new[] { 264, 266 }, shade = new[] { 136, 138 }, shadeMul = 1.35f, swatches = EyeSw },
        new Zone { label = "Sourcils", tab = "Visage", cells = new[] { 332, 334 }, swatches = HairSw },
        new Zone { label = "Barbe", tab = "Visage", cells = new[] { 270 }, swatches = HairSw },
        new Zone { label = "Lèvres", tab = "Visage", cells = new[] { 68, 70, 198 }, swatches = new[] { "e79a9c", "d97b80", "c45c6a", "a8455a", "8f3a4a", "e8a08a", "b0706a", "7a3a3a" } },
        new Zone { label = "Cheveux", tab = "Cheveux", cells = new[] { 268, 300 }, swatches = HairSw },
        new Zone { label = "Mèches, reflets", tab = "Cheveux", cells = new[] { 108, 76 }, swatches = HairSw },
        new Zone { label = "Haut", tab = "Haut", cells = new[] { 960, 992, 962, 964, 994, 996 }, swatches = ClothSw },
        new Zone { label = "Haut : détails", tab = "Haut", cells = new[] { 896, 928, 898, 900, 929, 993 }, swatches = ClothSw },
        new Zone { label = "Haut : motif", tab = "Haut", cells = new[] { 832, 834, 836, 864, 865, 768 }, swatches = ClothSw },
        new Zone { label = "Gants, poignets", tab = "Haut", cells = new[] { 400, 404, 336 }, swatches = ClothSw },
        new Zone { label = "Bas", tab = "Bas", cells = new[] { 640, 642, 644, 674, 676, 672, 673 }, swatches = ClothSw },
        new Zone { label = "Bas : détails", tab = "Bas", cells = new[] { 514, 516, 512, 546, 548, 450, 452, 480, 448, 437, 340 }, swatches = ClothSw },
        new Zone { label = "Ceinture", tab = "Bas", cells = new[] { 576, 608, 609, 577, 544, 545, 624 }, swatches = ClothSw },
        new Zone { label = "Chaussures", tab = "Bas", cells = new[] { 582, 584, 614, 616 }, swatches = ClothSw },
        new Zone { label = "Chaussures : détails", tab = "Bas", cells = new[] { 646, 648, 518, 520, 454, 456, 532, 550, 552, 678, 680 }, swatches = ClothSw },
        new Zone { label = "Semelles", tab = "Bas", cells = new[] { 212 }, swatches = ClothSw },
        new Zone { label = "Chapeau", tab = "Accessoires", cells = new[] { 650, 586, 522, 587, 144, 458, 394, 491, 682, 683 }, swatches = ClothSw },
        new Zone { label = "Lunettes", tab = "Accessoires", cells = new[] { 652, 588, 734, 542, 222 }, swatches = ClothSw },
        new Zone { label = "Verres", tab = "Accessoires", cells = new[] { 272 }, swatches = new[] { "90bae4", "a4d1c8", "8f94ab", "2a2a2e", "5a3a2a", "cfa33a", "e8408a", "f7f4ec" } },
        new Zone { label = "Sac", tab = "Accessoires", cells = new[] { 654, 656, 590, 526, 462, 398, 687, 623, 639, 606, 592, 559, 575 }, swatches = ClothSw },
        new Zone { label = "Épaulettes", tab = "Accessoires", cells = new[] { 970, 972, 906, 908, 842, 844, 990, 1003, 1005, 939, 941, 767, 670 }, swatches = ClothSw },
    };

    // --- Le personnage ------------------------------------------------------------------------------------
    public class Look
    {
        public int preset;                                   // grille de couleurs de depart (personnage du pack)
        public float type, size, muscle, height = 1f;        // -100..100 (feminin/masculin, mince/corpulent, muscles), taille relative
        public float smile;                                  // 0..100 : sourire (formes du visage)
        public readonly Dictionary<string, string> parts = new Dictionary<string, string>();
        public readonly Dictionary<int, Color32> colors = new Dictionary<int, Color32>();   // zone -> couleur choisie

        public Look Clone() { var l = new Look { preset = preset, type = type, size = size, muscle = muscle, height = height, smile = smile }; foreach (var kv in parts) l.parts[kv.Key] = kv.Value; foreach (var kv in colors) l.colors[kv.Key] = kv.Value; return l; }

        static string F(float v) => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);
        public string Code()
        {
            var t = new List<string> { "sk1", "p" + preset, "t" + F(type), "s" + F(size), "m" + F(muscle), "h" + F(height * 100), "e" + F(smile) };
            foreach (var s in Slots) t.Add(s.key + (parts.TryGetValue(s.key, out var c) && c.Length > 0 ? c : "x"));
            foreach (var kv in colors.OrderBy(k => k.Key)) t.Add($"z{kv.Key:00}{kv.Value.r:x2}{kv.Value.g:x2}{kv.Value.b:x2}");
            return string.Join("_", t);
        }

        public static Look Parse(string code)
        {
            if (code == null || !code.StartsWith("sk1_")) return null;
            var l = new Look();
            foreach (var tok in code.Split('_').Skip(1))
            {
                if (tok.Length < 2) continue;
                int.TryParse(tok.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
                switch (tok[0])
                {
                    case 'p': l.preset = Mathf.Clamp(n, 0, Math.Max(0, Presets.Length - 1)); continue;
                    case 't': l.type = Mathf.Clamp(n, -100, 100); continue;
                    case 's': l.size = Mathf.Clamp(n, -100, 100); continue;
                    case 'm': l.muscle = Mathf.Clamp(n, -100, 100); continue;
                    case 'h': l.height = Mathf.Clamp(n, 80, 120) / 100f; continue;
                    case 'e': l.smile = Mathf.Clamp(n, 0, 100); continue;
                    case 'z':
                        if (tok.Length == 9 && int.TryParse(tok.Substring(1, 2), out int z) && z < Zones.Length && ColorUtility.TryParseHtmlString("#" + tok.Substring(3), out var c))
                            l.colors[z] = c;
                        continue;
                }
                var key = tok.Substring(0, 2);
                if (Slots.Any(s => s.key == key)) l.parts[key] = tok.Substring(2) == "x" ? "" : tok.Substring(2);
            }
            return l;
        }

        // Depuis un personnage du pack : ses pieces, sa morphologie, sa grille de couleurs.
        public static Look FromPreset(int i)
        {
            Index();
            var p = Presets[i];
            var l = new Look { preset = i, type = p.type, size = p.size, muscle = p.muscle };
            foreach (var s in Slots)
            {
                var part = p.parts.Select(n => byName.TryGetValue(n, out var x) ? x : null).FirstOrDefault(x => x != null && x.slot == s.left);
                l.parts[s.key] = part != null ? PartCode(part.name) : s.optional ? "" : s.options.FirstOrDefault(o => o.StartsWith("b")) ?? s.options.First();
            }
            return l;
        }
    }

    public static bool IsCode(string id) => id != null && id.StartsWith("sk1_");

    // Couleur actuelle d'une zone : choisie, sinon celle de la grille de depart.
    public static Color32 ZoneColor(Look l, int zone)
    {
        if (l.colors.TryGetValue(zone, out var c)) return c;
        return Base(l.preset)[Zones[zone].cells[0]];
    }
    static readonly Dictionary<int, Color32[]> bases = new Dictionary<int, Color32[]>();
    static Color32[] Base(int preset)
    {
        if (bases.TryGetValue(preset, out var b)) return b;
        var hex = Presets.Length > preset ? Presets[preset].colors : null;
        b = new Color32[1024];
        for (int i = 0; i < 1024; i++)
            b[i] = hex != null && hex.Length >= (i + 1) * 6 ? new Color32(Convert.ToByte(hex.Substring(i * 6, 2), 16), Convert.ToByte(hex.Substring(i * 6 + 2, 2), 16), Convert.ToByte(hex.Substring(i * 6 + 4, 2), 16), 255) : new Color32(128, 128, 128, 255);
        return bases[preset] = b;
    }
    static Color32 Mul(Color32 c, float k) => new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), 255);

    // Texture 32x32 du personnage (mise en cache par couleurs).
    static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
    static Texture2D ColorMap(Look l)
    {
        string key = l.preset + ":" + string.Join(",", l.colors.OrderBy(k => k.Key).Select(k => $"{k.Key}{k.Value.r:x2}{k.Value.g:x2}{k.Value.b:x2}"));
        if (texCache.TryGetValue(key, out var t) && t) return t;
        var px = (Color32[])Base(l.preset).Clone();
        foreach (var kv in l.colors)
        {
            var z = Zones[kv.Key];
            foreach (var c in z.cells) px[c] = kv.Value;
            if (z.shade != null) foreach (var c in z.shade) px[c] = Mul(kv.Value, z.shadeMul);
        }
        t = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "SidekickColors" };
        t.SetPixels32(px);
        t.Apply();
        return texCache[key] = t;
    }
    static readonly Dictionary<Texture2D, Material> matCache = new Dictionary<Texture2D, Material>();
    static Material MaterialFor(Look l)
    {
        var tex = ColorMap(l);
        if (matCache.TryGetValue(tex, out var m) && m) return m;
        m = new Material(Resources.Load<Material>("Lit")) { name = "Sidekick", color = Color.white };
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Smoothness", 0.25f);
        return matCache[tex] = m;
    }

    // --- Assemblage ----------------------------------------------------------------------------------------
    public const float NaturalHeight = 1.78f;
    static readonly Dictionary<string, GameObject> meshCache = new Dictionary<string, GameObject>();

    public static GameObject Build(Look l, Transform parent)
    {
        Index();
        var g = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Sidekick/Skeleton"), parent);
        g.name = "Sidekick";
        var bones = new Dictionary<string, Transform>();
        foreach (var t in g.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;
        var mat = MaterialFor(l);
        var names = new List<string>();
        foreach (var f in Fixed) { var p = Find(f, "b01"); if (p != null) names.Add(p.name); }
        foreach (var s in Slots)
        {
            l.parts.TryGetValue(s.key, out var code);
            if (code == null) code = s.optional ? "" : s.options.FirstOrDefault(o => o.StartsWith("b")) ?? s.options.FirstOrDefault();
            foreach (var slot in s.right != null ? new[] { s.left, s.right } : new[] { s.left })
            {
                var p = Find(slot, code) ?? (s.optional ? null : Find(slot, "b01") ?? Cat.parts.FirstOrDefault(x => x.slot == slot));
                if (p != null) names.Add(p.name);
            }
        }
        foreach (var n in names) Attach(byName[n], g.transform, bones, mat, l);
        if (bones.TryGetValue("jaw", out var jaw)) g.AddComponent<SidekickJaw>().jaw = jaw;
        return g;
    }

    static void Attach(Part p, Transform root, Dictionary<string, Transform> bones, Material mat, Look l)
    {
        if (!meshCache.TryGetValue(p.path, out var src)) meshCache[p.path] = src = Resources.Load<GameObject>(p.path);
        var from = src ? src.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        if (!from) return;
        var go = new GameObject(p.slot);
        go.transform.SetParent(root, false);
        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = from.sharedMesh;
        smr.bones = from.bones.Select(b => Bone(b, bones)).ToArray();
        smr.rootBone = from.rootBone && bones.TryGetValue(from.rootBone.name, out var rb) ? rb : root;
        smr.sharedMaterials = Enumerable.Repeat(mat, Math.Max(1, from.sharedMesh.subMeshCount)).ToArray();
        smr.updateWhenOffscreen = true;
        // Morphologie : feminin (masculineFeminine), corpulent / mince, muscles.
        var mesh = from.sharedMesh;
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            var n = mesh.GetBlendShapeName(i);
            float w = n.EndsWith(".masculineFeminine") ? (l.type + 100) / 2
                : n.EndsWith(".defaultHeavy") ? Mathf.Max(0, l.size)
                : n.EndsWith(".defaultSkinny") ? Mathf.Max(0, -l.size)
                : n.EndsWith(".defaultBuff") ? (l.muscle + 100) / 2
                : n.EndsWith(".mouthSmileLeft") || n.EndsWith(".mouthSmileRight") ? l.smile
                : n.EndsWith(".mouthClose") ? 0 : 0;
            smr.SetBlendShapeWeight(i, w);
        }
    }

    // Os de la piece dans le squelette ; s'il manque (os de meches de cheveux...), il est recree sous son parent,
    // a la meme pose que dans le fichier de la piece.
    static Transform Bone(Transform src, Dictionary<string, Transform> bones)
    {
        if (!src) return null;
        if (bones.TryGetValue(src.name, out var t)) return t;
        var parent = Bone(src.parent, bones);
        if (!parent) return null;
        t = new GameObject(src.name).transform;
        t.SetParent(parent, false);
        t.localPosition = src.localPosition; t.localRotation = src.localRotation; t.localScale = src.localScale;
        return bones[src.name] = t;
    }

    // Personnage au hasard : un personnage du pack comme base, puis tout est tire au sort.
    public static Look Random(System.Random rng = null)
    {
        rng = rng ?? new System.Random();
        Index();
        var l = Look.FromPreset(rng.Next(Presets.Length));
        l.type = rng.Next(2) == 0 ? -100 : 100;
        l.size = rng.Next(-80, 60); l.muscle = rng.Next(-100, 60); l.height = 0.92f + (float)rng.NextDouble() * 0.14f;
        foreach (var s in Slots)
        {
            var opts = s.options;
            if (s.key == "AH" || s.key == "AF" || s.key == "AB" || s.key == "AS") { l.parts[s.key] = rng.NextDouble() < 0.3 ? opts[rng.Next(opts.Count)] : ""; continue; }
            if (s.key == "FH") { l.parts[s.key] = l.type < 0 && rng.NextDouble() < 0.45 ? opts[1 + rng.Next(opts.Count - 1)] : ""; continue; }
            if (s.key == "HN") { l.parts[s.key] = opts.FirstOrDefault(o => o.StartsWith("b")) ?? opts[0]; continue; }
            if (s.key == "HR") { l.parts[s.key] = opts[1 + rng.Next(opts.Count - 1)]; continue; }
            if (s.key == "TO" || s.key == "HI" || s.key == "FO")   // habille : jamais les sous-vetements du corps de base
            { var worn = opts.Where(o => o.StartsWith("c")).ToList(); l.parts[s.key] = worn[rng.Next(worn.Count)]; continue; }
            l.parts[s.key] = opts[rng.Next(opts.Count)];
        }
        var hair = HairSw[rng.Next(HairSw.Length)];
        void Set(string label, string hex) { int z = Array.FindIndex(Zones, x => x.label == label); ColorUtility.TryParseHtmlString("#" + hex, out var c); l.colors[z] = c; }
        Set("Peau", SkinSw[rng.Next(SkinSw.Length)]);
        Set("Cheveux", hair); Set("Sourcils", hair); Set("Barbe", hair);
        Set("Yeux", EyeSw[rng.Next(EyeSw.Length)]);
        foreach (var z in new[] { "Haut", "Haut : détails", "Bas", "Chaussures", "Chapeau", "Sac" }) Set(z, ClothSw[rng.Next(ClothSw.Length)]);
        return l;
    }
}

// Garde la machoire de Sidekick fermee : l'animation humanoide la remet a une position neutre qui ouvre la bouche.
public class SidekickJaw : MonoBehaviour
{
    public Transform jaw;
    Quaternion rest;
    void Start() { if (jaw) rest = jaw.localRotation; }
    void LateUpdate() { if (jaw) jaw.localRotation = rest; }
}
