using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// L'illustration de l'accueil (facon visuels du pack Modern Menus) : ton personnage fige en pleine partie de Uno
// sur la nappe de la clairiere, en train d'abattre un +4. Rendue une fois en haute definition, affichee en fond
// fixe derriere les menus, refaite quand tu changes de personnage.
public static class MenuArt
{
    public const int W = 2560, H = 1440;
    static Texture2D art;
    public static string PoseState = "Victory";
    public static float PoseTime = 0.4f;

    static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(type);
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    // Une carte de Uno (Resources/Uno), fine plaque.
    static Transform Card(Transform parent, Material lit, string face, float size = 1)
    {
        var tex = Resources.Load<Texture2D>("Uno/" + face);
        return Prim(PrimitiveType.Cube, parent, Vector3.zero, new Vector3(0.2f, 0.3f, 0.004f) * size, new Material(lit) { mainTexture = tex, color = Color.white }).transform;
    }

    public static IEnumerator Render(Hub hub, List<string> cast, DepthOfField dof, System.Action<Texture2D> done, string savePath = null)
    {
        var lit = Resources.Load<Material>("Lit");
        // Le pique-nique de l'accueil est range le temps de la photo : la scene a son propre decor.
        var hidden = new List<GameObject>();
        foreach (Transform c in hub.transform) if (c.gameObject.activeSelf) { hidden.Add(c.gameObject); c.gameObject.SetActive(false); }
        var set = new GameObject("IllustrationMenus").transform;
        set.SetParent(hub.transform, false);
        set.localRotation = Quaternion.Euler(0, 65, 0);   // la camera regarde depuis le cote degage de la clairiere
        var feet = set.TransformPoint(new Vector3(0, 0, -0.6f));
        set.position += Vector3.up * (Board.Ground(feet.x, feet.z) + 0.03f - feet.y);   // la nappe sur l'herbe, pas dessous

        // La nappe et la partie en cours : defausse, pioche.
        Material red = new Material(lit) { color = Board.Hex("e5322d") }, white = new Material(lit) { color = Board.Hex("fbf3e4") };
        for (int x = 0; x < 8; x++)
            for (int z = 0; z < 8; z++)
                Prim(PrimitiveType.Cube, set, new Vector3(-1.75f + x * 0.5f, 0.012f, -1.75f + z * 0.5f), new Vector3(0.5f, 0.02f, 0.5f), (x + z) % 2 == 0 ? red : white);
        var rng = new System.Random(5);
        string[] pile = { "r7", "b7", "b2", "g2", "gS", "y5", "yR", "r3" };
        for (int i = 0; i < pile.Length; i++)
        {
            var c = Card(set, lit, pile[i], 1.2f);
            c.localPosition = new Vector3(-0.2f + (float)rng.NextDouble() * 0.25f, 0.03f + i * 0.005f, -1.6f + (float)rng.NextDouble() * 0.25f);
            c.localRotation = Quaternion.Euler(90, rng.Next(0, 360), 0);
        }
        for (int i = 0; i < 14; i++)
        {
            var c = Card(set, lit, "back", 1.2f);
            c.localPosition = new Vector3(0.45f, 0.03f + i * 0.005f, -1.45f);
            c.localRotation = Quaternion.Euler(-90, 20, 0);
        }

        // Les 5 du casting autour de la nappe ; au centre, face camera, celui qui abat un +4.
        Animator Who(int i, Vector3 pos, float rot, string state, float t)
        {
            Chars.Spawn(cast[i % cast.Count], set, pos, rot, out var a);
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            a.Play(state, 0, t);
            a.Update(0);
            a.speed = 0;
            return a;
        }
        void Fan(Animator a, string[] faces)
        {
            var lh = a.GetBoneTransform(HumanBodyBones.LeftHand);
            for (int i = 0; i < faces.Length; i++)
            {
                var c = Card(set, lit, faces[i]);
                c.rotation = lh.rotation * Quaternion.Euler(80, 0, (i - faces.Length / 2) * 14);
                c.position = lh.position + lh.up * 0.08f + c.up * 0.07f + c.forward * (i * 0.004f);
            }
        }
        var an = Who(0, new Vector3(0, 0, -0.6f), 180, PoseState, PoseTime);
        Fan(Who(1, new Vector3(-1.3f, 0, 0.0f), 145, "RecieveHit", 0.35f), new[] { "b3", "y8", "g6", "r1" });
        Who(2, new Vector3(1.35f, 0, 0.1f), 215, "Clap", 0.25f);
        Fan(Who(3, new Vector3(-0.65f, 0, 1.0f), 165, "Think", 0.3f), new[] { "gD", "b4", "yS" });
        Who(4, new Vector3(0.75f, 0, 1.1f), 198, "Dance", 0.4f);
        var hand = an.GetBoneTransform(HumanBodyBones.RightHand);
        var lhand = an.GetBoneTransform(HumanBodyBones.LeftHand);
        var plus4 = Card(set, lit, "W4", 1.3f);
        plus4.position = hand.position + hand.up * 0.1f;
        plus4.rotation = hand.rotation * Quaternion.Euler(80, 0, 0);
        string[] fan = { "r5", "gR", "b9", "y1", "W" };
        for (int i = 0; i < fan.Length; i++)
        {
            var c = Card(set, lit, fan[i]);
            c.rotation = lhand.rotation * Quaternion.Euler(80, 0, (i - 2) * 14);
            c.position = lhand.position + lhand.up * 0.08f + c.up * 0.07f + c.forward * (i * 0.004f);
        }

        // Une gerbe de cartes qui s'envole derriere toi.
        string[] burst = { "r0", "b5", "gD", "y7", "bR", "W", "r9", "gS", "y2", "b1", "rD", "g4" };
        for (int i = 0; i < burst.Length; i++)
        {
            float a = Mathf.Lerp(-150, -30, i / (float)(burst.Length - 1)) * Mathf.Deg2Rad;
            float rad = 0.95f + (float)rng.NextDouble() * 0.5f;
            var c = Card(set, lit, burst[i], 1.15f);
            c.localPosition = new Vector3(Mathf.Cos(a) * rad * 1.25f, 1.75f - Mathf.Sin(a) * rad * 0.7f, -0.35f + (float)rng.NextDouble() * 0.6f);
            c.localRotation = Quaternion.Euler(rng.Next(-35, 35), 180 + rng.Next(-50, 50), rng.Next(-60, 60));
        }

        // Lumiere de studio : cle chaude de face, contres rose et bleu.
        void Lamp(Vector3 p, Color c, float intensity, float range)
        {
            var l = new GameObject("Lampe").AddComponent<Light>();
            l.transform.SetParent(set, false);
            l.transform.localPosition = p;
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range;
        }
        Lamp(new Vector3(-1.2f, 2.6f, -3.6f), new Color(1f, 0.88f, 0.7f), 12, 8);
        Lamp(new Vector3(1.6f, 2.2f, 0.4f), new Color(1f, 0.4f, 0.8f), 9, 6);
        Lamp(new Vector3(-1.8f, 2.0f, 0.2f), new Color(0.35f, 0.8f, 1f), 9, 6);

        yield return null;
        yield return null;

        var camGo = new GameObject("AppareilPhoto");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 38;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 900;
        var camPos = set.TransformPoint(new Vector3(0.3f, 0.4f, -5.4f));   // contre-plongee, cadre penche
        var look = set.TransformPoint(new Vector3(0f, 1.15f, -0.2f));
        cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(look - camPos) * Quaternion.Euler(0, 0, -5));
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        bool dofWas = dof && dof.active;
        var mode0 = dof ? dof.mode.value : DepthOfFieldMode.Off;
        if (dof)
        {
            dof.active = true; dof.mode.value = DepthOfFieldMode.Bokeh;
            dof.focusDistance.value = Vector3.Distance(camPos, look); dof.aperture.value = 1.2f; dof.focalLength.value = 90; dof.bladeCount.value = 6;
        }

        var shot = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = shot;
        cam.aspect = (float)W / H;   // sinon la camera garde les proportions de la fenetre et l'image sort etiree
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = shot;
        if (!art) art = new Texture2D(W, H, TextureFormat.RGB24, true);
        art.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        art.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        shot.Release();
        if (savePath != null) System.IO.File.WriteAllBytes(savePath, art.EncodeToPNG());

        if (dof) { dof.active = dofWas; dof.mode.value = mode0; }
        Object.Destroy(camGo);
        Object.Destroy(set.gameObject);
        foreach (var g in hidden) if (g) g.SetActive(true);
        done(art);
    }
}
