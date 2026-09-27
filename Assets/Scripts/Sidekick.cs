using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Personnages modulaires Synty Sidekick (pack gratuit, hors depot) : pieces greffees sur un squelette commun,
// morphologie par blend shapes, couleurs dans une palette 32x32 (cases de 2x2 : peau, yeux, cheveux, vetements).
// Un personnage s'ecrit "SK:cle=valeur,..." (sauvegarde, reseau : ni ";" ni "|", separateurs des messages).
public static class Sidekick
{
    public class Look
    {
        public int head = 1, hair = 3, brows = 1, beard = 0, nose = 1, ears = 1;
        public string top = "SCFI_CIVL_09", hands = "HUMN_BASE_01", bottom = "SCFI_CIVL_09", feet = "SCFI_CIVL_09", hat = "", face = "", back = "";
        public int palette = 1, skin = 2, hairColor = 1, eyes = 0;
        public int fem, muscle, weight, height = 100;

        public string Encode() =>
            $"SK:hd={head},hr={hair},br={brows},bd={beard},no={nose},ea={ears},tp={top},hn={hands},bt={bottom},ft={feet},ht={hat},fc={face},bk={back}," +
            $"pl={palette},sk={skin},hc={hairColor},ey={eyes},fm={fem},mu={muscle},wg={weight},hg={height}";

        public static Look Decode(string s)
        {
            var l = new Look();
            if (s == null || !s.StartsWith("SK:")) return l;
            foreach (var kv in s.Substring(3).Split(','))
            {
                var p = kv.Split('=');
                if (p.Length != 2) continue;
                int.TryParse(p[1], out int n);
                switch (p[0])
                {
                    case "hd": l.head = n; break; case "hr": l.hair = n; break; case "br": l.brows = n; break; case "bd": l.beard = n; break;
                    case "no": l.nose = n; break; case "ea": l.ears = n; break; case "tp": l.top = p[1]; break; case "hn": l.hands = p[1]; break;
                    case "bt": l.bottom = p[1]; break; case "ft": l.feet = p[1]; break; case "ht": l.hat = p[1]; break; case "fc": l.face = p[1]; break;
                    case "bk": l.back = p[1]; break; case "pl": l.palette = n; break; case "sk": l.skin = n; break; case "hc": l.hairColor = n; break;
                    case "ey": l.eyes = n; break; case "fm": l.fem = n; break; case "mu": l.muscle = n; break; case "wg": l.weight = n; break; case "hg": l.height = n; break;
                }
            }
            return l;
        }
        public Look Clone() => Decode(Encode());
    }

    // Choix proposes par le createur.
    public const int Heads = 2, Hairs = 11, Brows = 10, Beards = 10, Noses = 11, Ears = 10, Palettes = 4;
    public static readonly (string set, string label)[] Outfits = { ("SCFI_CIVL_09", "Civil"), ("FANT_KNGT_17", "Chevalier"), ("HUMN_BASE_01", "Simple") };
    public static readonly (string set, string label)[] HatSets = { ("", "Aucun"), ("SCFI_CIVL_09", "Casque"), ("SCFI_CIVL_10", "Bonnet"), ("FANT_KNGT_17", "Heaume"), ("HORR_VILN_01", "Masque") };
    public static readonly (string set, string label)[] AccSets = { ("", "Aucun"), ("SCFI_CIVL_09", "Civil"), ("FANT_KNGT_17", "Chevalier") };
    public static readonly string[] SkinTones = { "fcc19c", "eab48c", "d5a57b", "b98a60", "93643f", "6e4a33", "4c3b34" };
    public static readonly string[] HairColors = { "151515", "352a1f", "5e3b22", "8a5a2e", "c9a064", "e8d9a8", "b0512a", "d9824a", "8a8a8a", "e8e8e8", "2f6fd0", "d94f9a", "7a3fb0", "3fae5a" };
    public static readonly string[] EyeColors = { "4b351d", "3d6fb0", "4f7a3a", "707070", "8a6b3a" };

    // Cases de la palette (coin bas-gauche en coordonnees UV x32).
    static readonly (int x, int y)[] SkinLight = { (0, 2), (2, 2), (0, 6), (0, 8), (0, 10), (2, 10), (4, 10) };
    static readonly (int x, int y)[] SkinShade = { (0, 4), (2, 4), (4, 4), (2, 6), (4, 6), (2, 8), (4, 8) };
    static readonly (int x, int y)[] Iris = { (8, 4), (10, 4) };
    static readonly (int x, int y)[] HairCells = { (12, 2), (14, 2), (12, 6), (14, 6), (12, 8), (14, 8), (12, 10), (14, 10) };

    static string Code(string slot) => slot switch
    {
        "HEAD" => "01HEAD", "HAIR" => "02HAIR", "EBRL" => "03EBRL", "EBRR" => "04EBRR", "EYEL" => "05EYEL", "EYER" => "06EYER",
        "EARL" => "07EARL", "EARR" => "08EARR", "FCHR" => "09FCHR", "TORS" => "10TORS", "AUPL" => "11AUPL", "AUPR" => "12AUPR",
        "ALWL" => "13ALWL", "ALWR" => "14ALWR", "HNDL" => "15HNDL", "HNDR" => "16HNDR", "HIPS" => "17HIPS", "LEGL" => "18LEGL",
        "LEGR" => "19LEGR", "FOTL" => "20FOTL", "FOTR" => "21FOTR", "AHED" => "22AHED", "AFAC" => "23AFAC", "ABAC" => "24ABAC",
        "AHPF" => "25AHPF", "AHPB" => "26AHPB", "AHPL" => "27AHPL", "AHPR" => "28AHPR", "ASHL" => "29ASHL", "ASHR" => "30ASHR",
        "AEBL" => "31AEBL", "AEBR" => "32AEBR", "AKNL" => "33AKNL", "AKNR" => "34AKNR", "NOSE" => "35NOSE", "TETH" => "36TETH", _ => "37TONG",
    };

    static GameObject Part(string set, string slot)
    {
        if (string.IsNullOrEmpty(set)) return null;
        string dir = set.StartsWith("HUMN") ? "Meshes/Species/Humans/" : "Meshes/Outfits/Starter/";
        return Resources.Load<GameObject>($"{dir}SK_{set}_{Code(slot)}_HU01");
    }

    static string Base(int n) => "HUMN_BASE_" + n.ToString("00");

    static IEnumerable<GameObject> Parts(Look l)
    {
        IEnumerable<(string, string)> list = new[]
        {
            (Base(Mathf.Clamp(l.head, 1, Heads)), "HEAD"), (Base(1), "EYEL"), (Base(1), "EYER"), (Base(1), "TETH"), (Base(1), "TONG"),
            (Base(Mathf.Clamp(l.brows, 1, Brows)), "EBRL"), (Base(Mathf.Clamp(l.brows, 1, Brows)), "EBRR"),
            (Base(Mathf.Clamp(l.ears, 1, Ears)), "EARL"), (Base(Mathf.Clamp(l.ears, 1, Ears)), "EARR"), (Base(Mathf.Clamp(l.nose, 1, Noses)), "NOSE"),
            (l.beard > 0 ? Base(Mathf.Min(l.beard, Beards)) : "", "FCHR"),
            (l.hair <= 0 ? "" : l.hair <= 10 ? Base(l.hair) : "SCFI_CIVL_09", "HAIR"),
            (l.hands, "HNDL"), (l.hands, "HNDR"), (l.feet, "FOTL"), (l.feet, "FOTR"),
            (l.hat, "AHED"), (l.face, "AFAC"), (l.back, "ABAC"),
        };
        list = list.Concat(new[] { "TORS", "AUPL", "AUPR", "ALWL", "ALWR", "ASHL", "ASHR", "AEBL", "AEBR" }.Select(s => (l.top, s)));
        list = list.Concat(new[] { "HIPS", "LEGL", "LEGR", "AKNL", "AKNR", "AHPF", "AHPB", "AHPL", "AHPR" }.Select(s => (l.bottom, s)));
        return list.Select(p => Part(p.Item1, p.Item2)).Where(g => g);
    }

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    static Material MaterialFor(Look l)
    {
        string key = $"{l.palette}/{l.skin}/{l.hairColor}/{l.eyes}";
        if (mats.TryGetValue(key, out var m) && m) return m;
        var src = Resources.Load<Texture2D>("Palettes/Palette_" + Mathf.Clamp(l.palette, 1, Palettes));
        var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels(src.GetPixels());
        var skin = Board.Hex(SkinTones[Mathf.Clamp(l.skin, 0, SkinTones.Length - 1)]);
        void Fill((int x, int y)[] cells, Color c) { foreach (var (x, y) in cells) for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++) tex.SetPixel(x + i, y + j, c); }
        Fill(SkinLight, skin);
        Fill(SkinShade, skin * 0.87f);
        Fill(HairCells, Board.Hex(HairColors[Mathf.Clamp(l.hairColor, 0, HairColors.Length - 1)]));
        Fill(Iris, Board.Hex(EyeColors[Mathf.Clamp(l.eyes, 0, EyeColors.Length - 1)]));
        tex.Apply();
        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Smoothness", 0.15f);
        return mats[key] = m;
    }

    static void Kill(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

    // Monte le personnage : la tete fournit le squelette et l'Animator humanoide, les autres pieces y sont greffees.
    public static GameObject Build(Look l, Transform parent)
    {
        var parts = Parts(l).ToList();
        var root = Object.Instantiate(parts[0], parent);
        var bones = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>()) bones.TryAdd(t.name, t);
        foreach (var prefab in parts.Skip(1))
        {
            var inst = Object.Instantiate(prefab);
            // Os propres a la piece (meches, pans de tissu...) : greffes sur le squelette commun, sous leur parent.
            foreach (var t in inst.GetComponentsInChildren<Transform>().ToArray())
            {
                if (bones.ContainsKey(t.name) || t.GetComponent<SkinnedMeshRenderer>() || !t.parent || !bones.TryGetValue(t.parent.name, out var parentBone)) continue;
                t.SetParent(parentBone, false);
                foreach (var c in t.GetComponentsInChildren<Transform>()) bones.TryAdd(c.name, c);
            }
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.bones = smr.bones.Select(b => b && bones.TryGetValue(b.name, out var t) ? t : null).ToArray();
                if (smr.rootBone && bones.TryGetValue(smr.rootBone.name, out var r)) smr.rootBone = r;
                smr.transform.SetParent(root.transform, false);
            }
            Kill(inst);
        }
        // Pieces importees sans Animator : on pose l'avatar humanoide du squelette complet (Resources/Rig).
        var an = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
        if (!an.avatar || !an.avatar.isHuman) { an.avatar = Resources.Load<GameObject>("Rig").GetComponent<Animator>().avatar; an.Rebind(); }
        var mat = MaterialFor(l);
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            smr.sharedMaterial = mat;
            smr.updateWhenOffscreen = true;
        }
        Shape(root, l);
        return root;
    }

    // Morphologie (blend shapes communes a toutes les pieces) : modifiable en direct depuis le createur.
    public static void Shape(GameObject root, Look l)
    {
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = smr.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string n = mesh.GetBlendShapeName(i);
                n = n.Substring(n.LastIndexOf('.') + 1);
                float w = n switch
                {
                    "masculineFeminine" => (l.fem + 100) * 0.5f,
                    "defaultBuff" => Mathf.Max(0, l.muscle),
                    "defaultHeavy" => Mathf.Max(0, l.weight),
                    "defaultSkinny" => Mathf.Max(0, -l.weight),
                    _ => 0,
                };
                smr.SetBlendShapeWeight(i, w);
            }
        }
    }

    public static bool IsLook(string id) => id != null && id.StartsWith("SK:");
    public static bool Available => Resources.Load<Texture2D>("Palettes/Palette_1") != null && Resources.Load<GameObject>("Rig") != null;
}
