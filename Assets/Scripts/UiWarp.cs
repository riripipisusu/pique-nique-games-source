using UnityEngine;
using UnityEngine.UIElements;

// Affiche en perspective : dessine une zone d'une texture (l'atlas des affiches, rendu a plat hors ecran)
// deformee en quadrilatere, par une vraie homographie appliquee a un quadrillage fin.
// Coins en fractions du rectangle de l'element (agrandi de "pad" pour la lueur) : haut-gauche, haut-droit, bas-droit, bas-gauche.
public class Warp : VisualElement
{
    public Texture tex;
    public Vector2 atlasPos, atlasSize;   // position de l'affiche dans l'atlas et taille de l'atlas (px logiques)
    public float pad;
    public Vector2[] corners = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
    public static bool FlipV = true;      // une RenderTexture a son origine en bas

    public Warp()
    {
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    void Draw(MeshGenerationContext mgc)
    {
        float w = layout.width + 2 * pad, h = layout.height + 2 * pad;
        if (!tex || w <= 0 || h <= 0) return;
        var q = new Vector2[4];
        for (int i = 0; i < 4; i++) q[i] = new Vector2(-pad + corners[i].x * w, -pad + corners[i].y * h);

        // Carre unite -> quadrilatere (Heckbert) : x = (a u + b v + c) / (g u + h v + 1), idem y.
        float dx1 = q[1].x - q[2].x, dx2 = q[3].x - q[2].x, dx3 = q[0].x - q[1].x + q[2].x - q[3].x;
        float dy1 = q[1].y - q[2].y, dy2 = q[3].y - q[2].y, dy3 = q[0].y - q[1].y + q[2].y - q[3].y;
        float g = 0, hh = 0;
        float den = dx1 * dy2 - dx2 * dy1;
        if (Mathf.Abs(den) > 1e-6f) { g = (dx3 * dy2 - dx2 * dy3) / den; hh = (dx1 * dy3 - dx3 * dy1) / den; }
        float a = q[1].x - q[0].x + g * q[1].x, b = q[3].x - q[0].x + hh * q[3].x, c = q[0].x;
        float d = q[1].y - q[0].y + g * q[1].y, e = q[3].y - q[0].y + hh * q[3].y, f = q[0].y;

        const int N = 16;
        var md = mgc.Allocate((N + 1) * (N + 1), N * N * 6, tex);
        for (int j = 0; j <= N; j++)
            for (int i = 0; i <= N; i++)
            {
                float u = i / (float)N, v = j / (float)N, z = g * u + hh * v + 1;
                float sx = (atlasPos.x - pad + u * w) / atlasSize.x, sy = (atlasPos.y - pad + v * h) / atlasSize.y;
                md.SetNextVertex(new Vertex
                {
                    position = new Vector3((a * u + b * v + c) / z, (d * u + e * v + f) / z, Vertex.nearZ),
                    tint = Color.white,
                    uv = new Vector2(sx, FlipV ? 1 - sy : sy),
                });
            }
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                int k = j * (N + 1) + i;
                md.SetNextIndex((ushort)k); md.SetNextIndex((ushort)(k + 1)); md.SetNextIndex((ushort)(k + N + 2));
                md.SetNextIndex((ushort)k); md.SetNextIndex((ushort)(k + N + 2)); md.SetNextIndex((ushort)(k + N + 1));
            }
    }
}
