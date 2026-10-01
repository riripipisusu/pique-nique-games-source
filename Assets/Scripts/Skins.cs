using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Personnages : tous faits avec le createur (Sidekick, cf. Sidekick.cs), animes par les clips Mixamo (CharAnim).
// Un personnage = un code "sk1_..." ; la bande d'amis a des noms courts ("Ami_Roux") qui renvoient a leur code.
public static class Chars
{
    // Une tenue du pack comme personnage par defaut.
    public static string Default => All[0];

    // Quelques retouches sur un personnage du pack : morphologie et couleurs (zones du createur, par nom).
    static string Make(int preset, float type, float size, float muscle, params (string zone, string hex)[] colors)
    {
        var l = Sidekick.Look.FromPreset(preset);
        l.type = type; l.size = size; l.muscle = muscle;
        foreach (var (zone, hex) in colors)
        {
            int z = System.Array.FindIndex(Sidekick.Zones, x => x.label == zone);
            if (z >= 0 && ColorUtility.TryParseHtmlString("#" + hex, out var c)) l.colors[z] = c;
        }
        return l.Code();
    }

    // La bande d'amis (d'apres leurs photos), refaite avec le createur.
    static Dictionary<string, string> friends;
    public static Dictionary<string, string> Friends => friends ??= new Dictionary<string, string>
    {
        ["Ami_Caramel"] = Make(4, 100, -40, -60, ("Peau", "e0b18f"), ("Cheveux", "a8703a"), ("Sourcils", "6b4226"), ("Haut", "c9a227")),
        ["Ami_Brune"] = Make(5, 100, -30, -60, ("Peau", "f1c7a5"), ("Cheveux", "2b1d14"), ("Sourcils", "2b1d14"), ("Haut", "2a4d8f")),
        ["Ami_Platine"] = Make(6, 100, -40, -70, ("Peau", "fcd9c0"), ("Cheveux", "f3e6c4"), ("Sourcils", "c9a15b"), ("Haut", "e8408a")),
        ["Ami_Brun"] = Make(7, -100, 0, -20, ("Peau", "eab38b"), ("Cheveux", "4a2e1c"), ("Sourcils", "4a2e1c"), ("Barbe", "4a2e1c"), ("Haut", "f7f4ec")),
        ["Ami_Roux"] = Make(9, -100, -10, -30, ("Peau", "fcc19c"), ("Cheveux", "b06545"), ("Sourcils", "b06545"), ("Barbe", "b06545"), ("Haut", "2e7a3f")),
    };

    // Croupiers du casino : costume noir (veste ouverte sur chemise blanche, manches longues, pantalon droit).
    // Le pack n'a pas de vrai costard : on l'assemble avec ses pieces les plus habillees.
    static string Suit(string code, string hair, string shoes)
    {
        var l = Sidekick.Look.Parse(code);
        foreach (var (k, v) in new[] { ("TO", "c08"), ("AU", "c04"), ("AL", "c04"), ("HI", "c02"), ("LE", "c02"), ("FO", shoes), ("HR", hair), ("AH", ""), ("AF", ""), ("AB", ""), ("AS", "") }) l.parts[k] = v;
        return l.Code();
    }
    static readonly (string, string)[] SuitColors = { ("Haut", "16161a"), ("Haut : détails", "f7f4ec"), ("Gants, poignets", "16161a"), ("Bas", "16161a"), ("Ceinture", "0e0e10"), ("Chaussures", "0e0e10"), ("Chaussures : détails", "0e0e10"), ("Semelles", "0e0e10") };
    public static string DealerMan => Suit(Make(7, -100, -10, 10, SuitColors), "b08", "c03");
    public static string DealerWoman => Suit(Make(4, 100, -40, -50, SuitColors.Append(("Cheveux", "2b1d14")).ToArray()), "c02", "c03");

    // Personnages tout faits (bots, tests, joueurs en local) : les tenues du pack, puis des tirages fixes.
    static string[] all;
    public static string[] All => all ??= Enumerable.Range(4, System.Math.Max(0, Sidekick.Presets.Length - 4)).Select(i => Sidekick.Look.FromPreset(i).Code())
        .Concat(Enumerable.Range(0, 48).Select(i => Sidekick.Random(new System.Random(1000 + i)).Code())).ToArray();

    static string Resolve(string id) => id != null && Friends.TryGetValue(id, out var f) ? f : id;

    public static string Label(string id)
    {
        if (id != null && Friends.ContainsKey(id)) return id.Substring(4);   // "Ami_Roux" -> "Roux"
        return "Personnage";
    }

    public static bool Valid(string id) => id != null && (Friends.ContainsKey(id) || Sidekick.IsCode(id) && Sidekick.Look.Parse(id) != null);

    // Portrait (tete) et buste (jusqu'a la taille) : rendus a la volee, mis en cache, fond transparent.
    static readonly Dictionary<string, Texture2D> shots = new Dictionary<string, Texture2D>(), busts = new Dictionary<string, Texture2D>();
    public static Texture2D Portrait(string id) => Shot(id, shots, 256, 256, 22, 1.6f, 0f);
    public static Texture2D Bust(string id) => Shot(id, busts, 512, 640, 30, 2.3f, -0.4f);

    static Texture2D Shot(string id, Dictionary<string, Texture2D> cache, int w, int h, float fov, float dist, float down)
    {
        if (cache.TryGetValue(id ?? "", out var tex) && tex) return tex;
        var stage = new GameObject("PortraitStudio").transform;
        stage.position = new Vector3(3000, -3000, 3000);   // loin de tout autre decor (rien ne doit entrer dans le cadre)
        Spawn(id, stage, Vector3.zero, w == h ? 180 : 196, out var an);
        // Pose de repos de l'animation (bras le long du corps), bouche fermee (cf. SidekickJaw).
        var jaw = an.GetComponentInChildren<SidekickJaw>()?.jaw;
        var jawRest = jaw ? jaw.localRotation : Quaternion.identity;
        an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        an.Update(0f);
        if (jaw) jaw.localRotation = jawRest;
        var head = an.GetBoneTransform(HumanBodyBones.Head).position + Vector3.up * (0.1f + down);
        var cam = new GameObject("PortraitCam").AddComponent<Camera>();
        cam.transform.SetParent(stage, false);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.transform.position = head + new Vector3(0, 0.03f, -dist);
        cam.transform.LookAt(head);
        var lamp = new GameObject("PortraitLamp").AddComponent<Light>();
        lamp.transform.SetParent(stage, false);
        lamp.type = LightType.Point;
        lamp.range = 4 + dist;
        lamp.intensity = 3;
        lamp.transform.position = head + new Vector3(-0.5f, 0.4f, -1.0f);
        var rim = new GameObject("PortraitRim").AddComponent<Light>();   // liseré de lumiere par derriere
        rim.transform.SetParent(stage, false);
        rim.type = LightType.Point; rim.range = 4; rim.intensity = 2.5f; rim.color = new Color(0.7f, 0.85f, 1f);
        rim.transform.position = head + new Vector3(0.8f, 0.5f, 0.9f);
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        rt.Release();
        Object.DestroyImmediate(stage.gameObject);
        return cache[id ?? ""] = tex;
    }

    // Personnage anime ; sa taille relative (curseur "Taille" du createur) est gardee.
    public static Transform Spawn(string id, Transform parent, Vector3 localPos, float rotY, out Animator an, float height = 1.8f)
    {
        var look = Sidekick.Look.Parse(Valid(id) ? Resolve(id) : Default) ?? Sidekick.Look.FromPreset(4);
        var sk = Sidekick.Build(look, parent);
        sk.transform.localPosition = localPos;
        sk.transform.localRotation = Quaternion.Euler(0, rotY, 0);
        sk.transform.localScale = Vector3.one * (height / Sidekick.NaturalHeight * look.height);
        an = sk.GetComponent<Animator>();
        an.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("CharAnim");
        an.applyRootMotion = false;
        an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        return sk.transform;
    }
}
