using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Personnages "a la carte" : les persos d'un bloc des packs City / Fantasy / Farm (meme squelette Synty)
// decoupes a l'execution selon l'os dominant de chaque triangle (tete, haut, bas, pieds), puis recombines.
// Un personnage s'ecrit "MX:h=Pack/Maillage/palette,t=...,b=...,f=...,s=A,hg=100" (pas de ";" ni de "|").
public static class Mix
{
    public enum Part { Head, Top, Bottom, Feet }

    public class Look
    {
        public string head = "City/Character_Male_Hoodie/1", top = "City/Character_Male_Hoodie/1", bottom = "City/Character_Male_Hoodie/1", feet = "City/Character_Male_Hoodie/1";
        public string skin = "A";
        public int height = 100;

        public string Encode() => $"MX:h={head},t={top},b={bottom},f={feet},s={skin},hg={height}";

        public static Look Decode(string s)
        {
            var l = new Look();
            if (s == null || !s.StartsWith("MX:")) return l;
            foreach (var kv in s.Substring(3).Split(','))
            {
                var p = kv.Split('=');
                if (p.Length != 2) continue;
                switch (p[0])
                {
                    case "h": l.head = p[1]; break; case "t": l.top = p[1]; break; case "b": l.bottom = p[1]; break; case "f": l.feet = p[1]; break;
                    case "s": l.skin = p[1]; break; case "hg": int.TryParse(p[1], out l.height); break;
                }
            }
            return l;
        }
        public string this[Part p] { get => p == Part.Head ? head : p == Part.Top ? top : p == Part.Bottom ? bottom : feet;
            set { if (p == Part.Head) head = value; else if (p == Part.Top) top = value; else if (p == Part.Bottom) bottom = value; else feet = value; } }
    }

    public static bool IsLook(string id) => id != null && id.StartsWith("MX:");
    public static int Palettes(string pack) => pack == "Fantasy" ? 5 : 4;

    static Part Region(string bone)
    {
        if (bone == "Head" || bone == "Eyes" || bone == "Eyebrows" || bone == "Neck") return Part.Head;
        if (bone.StartsWith("Ankle") || bone.StartsWith("Ball") || bone.StartsWith("Toes")) return Part.Feet;
        if (bone.StartsWith("UpperLeg") || bone.StartsWith("LowerLeg") || bone == "Hips") return Part.Bottom;
        return Part.Top;
    }

    static readonly Dictionary<(Mesh, Part), Mesh> cut = new Dictionary<(Mesh, Part), Mesh>();

    // Garde les triangles dont la majorite des sommets suit un os de la region.
    static Mesh Cut(SkinnedMeshRenderer src, Part part)
    {
        var mesh = src.sharedMesh;
        if (cut.TryGetValue((mesh, part), out var m) && m) return m;
        var region = src.bones.Select(b => Region(b.name)).ToArray();
        var weights = mesh.boneWeights;
        var tris = mesh.triangles;
        var keep = new List<int>();
        for (int i = 0; i < tris.Length; i += 3)
        {
            int n = 0;
            for (int k = 0; k < 3; k++) if (region[weights[tris[i + k]].boneIndex0] == part) n++;
            if (n >= 2) keep.AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
        }
        m = new Mesh { name = mesh.name + "_" + part, indexFormat = mesh.indexFormat };
        m.vertices = mesh.vertices;
        m.normals = mesh.normals;
        m.uv = mesh.uv;
        m.boneWeights = weights;
        m.bindposes = mesh.bindposes;
        m.triangles = keep.ToArray();
        m.RecalculateBounds();
        return cut[(mesh, part)] = m;
    }

    static readonly Dictionary<string, GameObject> packs = new Dictionary<string, GameObject>();
    static GameObject Pack(string name) => packs.TryGetValue(name, out var g) && g ? g : packs[name] = Resources.Load<GameObject>("Chars/" + name);

    static (GameObject pack, SkinnedMeshRenderer smr, Material mat) Source(string piece, string skin)
    {
        var p = piece.Split('/');
        var pack = p.Length == 3 ? Pack(p[0]) : null;
        var smr = pack ? pack.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == p[1]) : null;
        if (!smr) return Source(new Look().head, skin);
        int.TryParse(p[2], out int pal);
        var mat = Resources.Load<Material>($"CharMats/{p[0]}_0{Mathf.Clamp(pal, 1, Palettes(p[0]))}_{skin}") ?? Resources.Load<Material>($"CharMats/{p[0]}_01_A");
        return (pack, smr, mat);
    }

    public static GameObject Build(Look l, Transform parent)
    {
        var head = Source(l.head, l.skin);
        var root = Object.Instantiate(head.pack, parent);
        foreach (var s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { if (Application.isPlaying) Object.Destroy(s.gameObject); else Object.DestroyImmediate(s.gameObject); }
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
        var bones = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>()) bones.TryAdd(t.name, t);
        foreach (Part part in System.Enum.GetValues(typeof(Part)))
        {
            var (_, src, mat) = Source(l[part], l.skin);
            var go = new GameObject(part.ToString());
            go.transform.SetParent(root.transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = Cut(src, part);
            smr.bones = src.bones.Select(b => bones.TryGetValue(b.name, out var t) ? t : null).ToArray();
            smr.rootBone = src.rootBone && bones.TryGetValue(src.rootBone.name, out var rb) ? rb : null;
            smr.sharedMaterial = mat;
            smr.updateWhenOffscreen = true;
        }
        return root;
    }
}
