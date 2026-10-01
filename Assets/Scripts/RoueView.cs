using System.Collections;
using UnityEngine;

// La roue de la fortune, modelisee ici : 24 parts colorees (maillage en camembert epais), montants ecrits le long du
// rayon, picots dores entre les parts, jante et moyeu dores, socle, et une languette rouge qui claque sur les picots.
// Posee a plat sur le plateau de Tenna, devant les pupitres. Le lancer tourne la roue et s'arrete sur la case tiree.
public class RoueView : MonoBehaviour
{
    const float R = 1.35f, Thick = 0.07f;
    public static readonly Vector3 Place = new Vector3(0, 0.8f, -4.3f);   // au premier plan, devant les pupitres
    Transform wheel, flapper;
    Material gold;
    int[] painted;
    bool paintedEnv;
    Material lit;
    Font font;
    float angle;   // rotation courante de la roue (degres, autour de Y)
    int lastPeg;
    public bool spinning;

    static readonly string[] Palette = { "e8408a", "f28c1c", "f7d61c", "5fbf3a", "5aa8e0", "6a3fa8", "d8262d", "f07ab8", "3fc4c9" };

    Material Mat(string hex, float smooth = 0.35f, float metal = 0)
    {
        var m = new Material(lit) { color = Board.Hex(hex) };
        m.SetFloat("_Smoothness", smooth);
        if (metal > 0) m.SetFloat("_Metallic", metal);
        return m;
    }

    Transform Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m, Quaternion? rot = null)
    {
        var g = GameObject.CreatePrimitive(t);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.transform.localRotation = rot ?? Quaternion.identity;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g.transform;
    }

    // Part de camembert epaisse : dessus, dessous et bord exterieur.
    // Materiau de police avec test de profondeur : les lettres ne traversent plus Tenna ni les candidats.
    static readonly System.Collections.Generic.Dictionary<Font, Material> textMats = new System.Collections.Generic.Dictionary<Font, Material>();
    public static Material TextMat(Font f)
    {
        if (textMats.TryGetValue(f, out var m) && m) return m;
        var sh = Resources.Load<Shader>("TextDepth");
        m = sh ? new Material(sh) : new Material(f.material);
        m.mainTexture = f.material.mainTexture;
        return textMats[f] = m;
    }

    public static Mesh Slice(float a0, float a1, float r, float h, int steps = 6)
    {
        var v = new System.Collections.Generic.List<Vector3>();
        var tr = new System.Collections.Generic.List<int>();
        Vector3 P(float a, float rad, float y) => new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * rad, y, Mathf.Cos(a * Mathf.Deg2Rad) * rad);
        // dessus
        int c = v.Count; v.Add(new Vector3(0, h, 0));
        for (int s = 0; s <= steps; s++) v.Add(P(Mathf.Lerp(a0, a1, s / (float)steps), r, h));
        for (int s = 0; s < steps; s++) { tr.Add(c); tr.Add(c + 1 + s); tr.Add(c + 2 + s); }
        // bord
        int b = v.Count;
        for (int s = 0; s <= steps; s++) { float a = Mathf.Lerp(a0, a1, s / (float)steps); v.Add(P(a, r, h)); v.Add(P(a, r, 0)); }
        for (int s = 0; s < steps; s++) { int k = b + s * 2; tr.Add(k); tr.Add(k + 1); tr.Add(k + 2); tr.Add(k + 2); tr.Add(k + 1); tr.Add(k + 3); }
        var m = new Mesh(); m.SetVertices(v); m.SetTriangles(tr, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    bool built;
    public void Build(Transform parent, Vector3 at)
    {
        if (built) { Paint(Roue.Wheels[0], false); return; }
        built = true;
        lit = Resources.Load<Material>("Lit");
        font = Resources.Load<Font>("Fonts/Fredoka");
        transform.SetParent(parent, false);
        transform.localPosition = at;
        transform.localRotation = Quaternion.identity;
        gold = Mat("e9b949", 0.7f, 0.6f);
        Paint(Roue.Wheels[0], false);
        // Languette (cote camera) : petite fleche rouge qui pointe vers le centre.
        flapper = new GameObject("languette").transform;
        flapper.SetParent(transform, false);
        flapper.localPosition = new Vector3(0, Thick + 0.08f, -(R + 0.12f));
        Prim(PrimitiveType.Cube, flapper, new Vector3(0, 0, 0.1f), new Vector3(0.07f, 0.05f, 0.24f), Mat("d62828", 0.6f), Quaternion.Euler(0, 0, 0));
        Prim(PrimitiveType.Cube, flapper, new Vector3(0, 0, 0.22f), new Vector3(0.1f, 0.05f, 0.1f), Mat("d62828", 0.6f), Quaternion.Euler(0, 45, 0));
        Prim(PrimitiveType.Cylinder, transform, new Vector3(0, Thick + 0.05f, -(R + 0.12f)), new Vector3(0.1f, 0.06f, 0.1f), gold);
        lastPeg = 0;
    }


    // (Re)peint la roue : une roue par manche (les montants montent), la roue des enveloppes en finale.
    // Facon plateau TF1 : parts de couleurs vives, chiffres noirs empiles le long du rayon (lus depuis le bord),
    // "€" pres du moyeu blanc, piquets argentes tout autour.
    public void Paint(int[] values, bool envelopes)
    {
        if (painted == values && paintedEnv == envelopes) return;
        painted = values; paintedEnv = envelopes;
        if (wheel) Destroy(wheel.gameObject);
        wheel = new GameObject("roue").transform;
        wheel.SetParent(transform, false);
        int n = values.Length;
        float step = 360f / n;
        var black = Board.Hex("141414");
        var silver = Mat("d9dce2", 0.85f, 0.8f);
        // Tranche sombre sous les parts.
        Prim(PrimitiveType.Cylinder, wheel, new Vector3(0, Thick * 0.5f - 0.01f, 0), new Vector3(R * 2 + 0.02f, Thick * 0.5f, R * 2 + 0.02f), Mat("26262c", 0.3f));
        for (int i = 0; i < n; i++)
        {
            int val = values[i];
            string hex = envelopes ? Palette[i % Palette.Length]
                : val == Roue.Bankrupt ? "111114" : val == Roue.PassTurn ? "f7f4ec" : val == Roue.Cave ? "c9c9cf" : val == Roue.Jackpot ? "e3b82c" : Palette[i % Palette.Length];
            var g = new GameObject("part " + i);
            g.transform.SetParent(wheel, false);
            g.AddComponent<MeshFilter>().sharedMesh = Slice(i * step, (i + 1) * step, R, Thick);
            g.AddComponent<MeshRenderer>().sharedMaterial = val == Roue.Cave ? silver : Mat(hex, 0.45f);
            float mid = (i + 0.5f) * step;
            var dir = new Vector3(Mathf.Sin(mid * Mathf.Deg2Rad), 0, Mathf.Cos(mid * Mathf.Deg2Rad));
            string text = envelopes ? "?" : val == Roue.Bankrupt ? "BANQUEROUTE" : val == Roue.PassTurn ? "PASSE" : val == Roue.Cave ? "CAVERNE" : val.ToString();
            bool word = val < 0 || envelopes;   // BANQUEROUTE, PASSE, CAVERNE : pas de montant, donc pas de €
            // Un caractere par ligne, du bord vers le centre, le haut du caractere tourne vers l'exterieur.
            float size = word ? Mathf.Min(0.06f, (R * 0.62f) / text.Length) : 0.19f;
            float r0 = R - (word ? 0.08f : 0.14f);
            var col = val == Roue.Bankrupt ? Color.white : black;
            for (int k = 0; k < text.Length; k++)
            {
                var ch = new GameObject("c").AddComponent<TextMesh>();
                ch.transform.SetParent(wheel, false);
                ch.transform.localPosition = dir * (r0 - k * size * (word ? 1.0f : 0.95f)) + Vector3.up * (Thick + 0.002f);
                ch.transform.localRotation = Quaternion.LookRotation(Vector3.down, -dir);
                ch.font = font; ch.GetComponent<MeshRenderer>().sharedMaterial = TextMat(font);
                ch.fontSize = 96; ch.anchor = TextAnchor.MiddleCenter; ch.fontStyle = FontStyle.Bold;
                ch.text = text[text.Length - 1 - k].ToString(); ch.color = col;
                ch.characterSize = size * 0.105f;
            }
            if (!word && !envelopes)
            {
                var eu = new GameObject("euro").AddComponent<TextMesh>();
                eu.transform.SetParent(wheel, false);
                eu.transform.localPosition = dir * (R * 0.33f) + Vector3.up * (Thick + 0.002f);
                eu.transform.localRotation = Quaternion.LookRotation(Vector3.down, -dir);
                eu.font = font; eu.GetComponent<MeshRenderer>().sharedMaterial = TextMat(font);
                eu.fontSize = 96; eu.anchor = TextAnchor.MiddleCenter; eu.fontStyle = FontStyle.Bold;
                eu.text = "€"; eu.color = black; eu.characterSize = 0.008f;
            }
            // Piquet argente a la frontiere, sur le bord.
            float edge = i * step * Mathf.Deg2Rad;
            Prim(PrimitiveType.Cylinder, wheel, new Vector3(Mathf.Sin(edge), 0, Mathf.Cos(edge)) * (R - 0.015f) + Vector3.up * (Thick + 0.11f), new Vector3(0.022f, 0.11f, 0.022f), silver);
        }
        // Moyeu blanc cercle de bleu clair.
        Prim(PrimitiveType.Cylinder, wheel, new Vector3(0, Thick + 0.004f, 0), new Vector3(0.72f, 0.004f, 0.72f), Mat("8fd3f0", 0.6f));
        Prim(PrimitiveType.Cylinder, wheel, new Vector3(0, Thick + 0.008f, 0), new Vector3(0.66f, 0.006f, 0.66f), Mat("f6f6f8", 0.7f));
        wheel.localRotation = Quaternion.Euler(0, angle, 0);
    }

    // Case sous la languette (cote -z) pour une rotation donnee.
    static float TargetFor(int segment)
    {
        float step = 360f / 24, mid = (segment + 0.5f) * step;
        // La part est a l'angle mid (depuis +z) ; la languette est a 180 deg. Rotation r telle que mid + r = 180.
        return 180 - mid;
    }

    // Lancer : 3 a 4 tours puis ralentissement jusqu'a la case tiree (le petit decalage dans la part est deterministe).
    public IEnumerator Spin(int segment, float speed = 1)
    {
        spinning = true;
        float step = 360f / 24;
        float target = TargetFor(segment) + ((segment * 37) % 11 - 5) * step * 0.06f;
        float start = angle;
        float end = start + 360 * (3 + segment % 2) + Mathf.Repeat(target - start, 360);
        float dur = 4.2f / speed;
        for (float t = 0; t < 1; t += Time.deltaTime / dur)
        {
            float e = 1 - Mathf.Pow(1 - t, 3);
            angle = Mathf.Lerp(start, end, e);
            Apply();
            yield return null;
        }
        angle = end; Apply();
        spinning = false;
    }

    void Apply()
    {
        wheel.localRotation = Quaternion.Euler(0, angle, 0);
        // Un "tac" par picot qui passe sous la languette, et la languette qui saute.
        int peg = Mathf.FloorToInt(angle / (360f / 24));
        if (peg != lastPeg) { lastPeg = peg; Sound.I.Play("tick", 0.35f, 0.05f); flapper.localRotation = Quaternion.Euler(0, 18, 0); }
        else flapper.localRotation = Quaternion.Slerp(flapper.localRotation, Quaternion.identity, Time.deltaTime * 18);
    }

    // Vue plongeante sur la roue pendant le lancer.
    // Vue plongeante sur la roue ; visee sous le centre : la roue (et sa fleche) monte dans l'image, au-dessus des panneaux.
    public Pose SpinPose => new Pose(transform.TransformPoint(new Vector3(0, 3.0f, -3.1f)),
                                     Quaternion.LookRotation(transform.TransformPoint(new Vector3(0, -0.1f, -0.45f)) - transform.TransformPoint(new Vector3(0, 3.0f, -3.1f))));
}
