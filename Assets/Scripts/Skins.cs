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

    public static void ApplySkin(GameObject g, string character)
    {
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
        var prefab = Resources.Load<GameObject>("Characters/" + model) ?? Resources.Load<GameObject>("Characters/Casual_Male");
        var g = Object.Instantiate(prefab, parent);
        ApplySkin(g, prefab.name);
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
