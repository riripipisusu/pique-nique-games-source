using System.Collections.Generic;
using UnityEngine;

// Personnages Quaternius : apparition, taille, animation et couleur de peau.
public static class Chars
{
    // Le pack (2019) laisse la peau quasi noire, a colorer soi-meme :
    // chaque personnage recoit une teinte de peau stable, tiree de son nom.
    static readonly Color[] Tones =
    {
        new Color(0.98f, 0.82f, 0.69f), new Color(0.93f, 0.73f, 0.58f), new Color(0.80f, 0.58f, 0.42f),
        new Color(0.60f, 0.42f, 0.30f), new Color(0.42f, 0.29f, 0.21f),
    };

    // La bande d'amis : un modele Quaternius recolore (cheveux, peau, vetements) par personne.
    public static readonly Dictionary<string, (string model, string[] colors)> Looks = new Dictionary<string, (string, string[])>
    {
        ["Ami_Caramel"] = ("Casual2_Female", new[] { "Skin=f3d2bd", "Hair=a86c3a", "Shirt=161414", "Pants=141214", "Belt=2a2222" }),
        ["Ami_Brune"] = ("Casual3_Female", new[] { "Skin=e9c2a2", "Hair=0e0a0a", "Shirt=1b1a1d", "Pants=1e1c26", "Belt=2a2222" }),
        ["Ami_Platine"] = ("Cowboy_Hair", new[] { "Skin=f1cfb6", "Hair=f0e6c2", "Jacket=ecebe6", "Top=ecebe6", "Scarf=ecebe6", "Pants=4a5d7c" }),
        ["Ami_Brun"] = ("Casual_Male", new[] { "Skin=dcab86", "Hair=16100c", "Shirt=eeebe4", "Pants=2b3444", "Belt=3a2a1c" }),
        ["Ami_Roux"] = ("Casual2_Male", new[] { "Skin=f2cdb0", "Hair=b0512a", "Shirt=2f6b4a", "Pants=6b5a44", "Belt=3a2a1c" }),
    };

    public static string ModelOf(string character) => Looks.TryGetValue(character, out var l) ? l.model : character;

    // Recolore les materiaux dont le nom commence par une cle (Hair, Skin, Shirt...).
    public static void Recolor(GameObject g, string character)
    {
        if (!Looks.TryGetValue(character, out var look)) return;
        foreach (var r in g.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                foreach (var c in look.colors)
                {
                    var kv = c.Split('=');
                    if (mats[i] && mats[i].name.StartsWith(kv[0])) { mats[i] = new Material(mats[i]) { color = Board.Hex(kv[1]) }; break; }
                }
            r.sharedMaterials = mats;
        }
    }

    public static void ApplySkin(GameObject g, string character)
    {
        if (Looks.ContainsKey(character)) { Recolor(g, character); return; }
        int h = 0;
        foreach (char c in character) h = h * 31 + c;
        var tone = Tones[Mathf.Abs(h) % Tones.Length];
        foreach (var r in g.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!mats[i] || !mats[i].name.StartsWith("Skin") || mats[i].color.maxColorComponent > 0.05f) continue;
                mats[i] = new Material(mats[i]) { color = tone };
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    public static Transform Spawn(string model, Transform parent, Vector3 localPos, float rotY, out Animator an, float height = 1.8f)
    {
        var prefab = Resources.Load<GameObject>("Characters/" + ModelOf(model)) ?? Resources.Load<GameObject>("Characters/Casual_Male");
        var g = Object.Instantiate(prefab, parent);
        ApplySkin(g, Looks.ContainsKey(model) ? model : prefab.name);
        g.transform.localPosition = localPos;
        g.transform.localRotation = Quaternion.Euler(0, rotY, 0);
        var rs = g.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        g.transform.localScale *= height / Mathf.Max(0.01f, b.size.y);
        an = g.GetComponentInChildren<Animator>() ?? g.AddComponent<Animator>();
        an.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("CharAnim");
        an.applyRootMotion = false;
        return g.transform;
    }
}
