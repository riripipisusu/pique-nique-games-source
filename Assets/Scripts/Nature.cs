using System;
using System.Collections.Generic;
using UnityEngine;

// Paysage en Terrain Unity : relief, sols Synty, riviere, foret en bosquets et herbe dessinee par instanciation GPU.
public static class Nature
{
    public const float Size = 700, Y0 = -6, H = 80;

    // Riviere : descend des collines de l'est, traverse l'etang, repart vers le sud.
    static readonly Vector2[] River =
    {
        new Vector2(360, -70), new Vector2(260, -35), new Vector2(175, -50), new Vector2(105, -22), new Vector2(55, -30), new Vector2(18, -17),
        new Vector2(9, -24), new Vector2(0, -42), new Vector2(-14, -72), new Vector2(-8, -120), new Vector2(-38, -200), new Vector2(-28, -360),
    };
    const float RiverHalf = 3.2f;

    public static float RiverDist(float x, float z)
    {
        var p = new Vector2(x, z);
        float best = float.MaxValue;
        for (int i = 0; i < River.Length - 1; i++)
        {
            Vector2 a = River[i], ab = River[i + 1] - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, (p - (a + ab * t)).sqrMagnitude);
        }
        return Mathf.Sqrt(best);
    }

    public static float RiverCarve(float x, float z) => 1.4f * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(RiverHalf * 0.7f, RiverHalf * 2.2f, RiverDist(x, z))));

    // Densite de foret : clairiere centrale, bosquets autour, foret dense au loin avec quelques trouees.
    static float Forest(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float groves = Mathf.InverseLerp(0.55f, 0.75f, Mathf.PerlinNoise(x * 0.035f + 7, z * 0.035f + 3));
        float dense = Mathf.InverseLerp(40, 75, r);
        float gaps = Mathf.InverseLerp(0.72f, 0.62f, Mathf.PerlinNoise(x * 0.012f + 40, z * 0.012f + 90));
        return Mathf.Clamp01(Mathf.Max(groves * Mathf.InverseLerp(18, 30, r), dense * gaps));
    }

    public static Terrain Build(Transform parent, Func<Vector3, float, bool> free)
    {
        var reg = Synty.I;
        var td = new TerrainData { heightmapResolution = 1025, alphamapResolution = 512 };
        td.size = new Vector3(Size, H, Size);
        float W(int i, int n) => -Size / 2 + i * Size / (n - 1);

        int hn = td.heightmapResolution;
        var h = new float[hn, hn];
        for (int z = 0; z < hn; z++)
            for (int x = 0; x < hn; x++)
                h[z, x] = (Board.Ground(W(x, hn), W(z, hn)) - Y0) / H;
        td.SetHeights(0, 0, h);

        // Sols : deux herbes melangees, taches fleuries, boue au bord de l'eau, mousse et feuilles sous les arbres.
        td.terrainLayers = reg.layers;
        int an = td.alphamapResolution;
        var al = new float[an, an, reg.layers.Length];
        for (int z = 0; z < an; z++)
            for (int x = 0; x < an; x++)
            {
                float wx = W(x, an), wz = W(z, an);
                float g2 = Mathf.PerlinNoise(wx * 0.02f + 5, wz * 0.02f + 5);
                float fl = Mathf.InverseLerp(0.6f, 0.75f, Mathf.PerlinNoise(wx * 0.05f + 20, wz * 0.05f + 60));
                float mud = Mathf.InverseLerp(RiverHalf * 2.4f, RiverHalf, RiverDist(wx, wz));
                float f = Forest(wx, wz);
                var w = new[] { 1 - g2, g2, fl * (1 - f), mud * 3, f * 0.8f * Mathf.PerlinNoise(wx * 0.08f, wz * 0.08f), f * 0.6f };
                float sum = 0; foreach (var v in w) sum += v;
                for (int k = 0; k < w.Length; k++) al[z, x, k] = w[k] / sum;
            }
        td.SetAlphamaps(0, 0, al);

        // Arbres, buissons, rochers : instances de terrain (LOD Synty, cartes au loin).
        string[] protos =
        {
            "SM_Env_Tree_Meadow_01", "SM_Env_Tree_Meadow_02", "SM_Env_Tree_Fruit_01", "SM_Env_Tree_Fruit_02", "SM_Env_Tree_Fruit_03",
            "SM_Env_Tree_Birch_01", "SM_Env_Tree_Birch_02", "SM_Env_Bush_01", "SM_Env_Bush_02", "SM_Env_Bush_03",
            "SM_Env_Rock_01", "SM_Env_Rock_02", "SM_Env_Rock_03", "SM_Env_Rock_Pile_01", "SM_Env_Rock_Pile_03", "SM_Env_Ground_Cover_01", "SM_Env_Ground_Cover_02",
        };
        td.treePrototypes = Array.ConvertAll(protos, n => new TreePrototype { prefab = Synty.Get(n) });
        var rng = new System.Random(21);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        var trees = new List<TreeInstance>();
        void Add(int proto, float x, float z, float s)
        {
            if (Mathf.Abs(x) > Size / 2 - 2 || Mathf.Abs(z) > Size / 2 - 2) return;
            trees.Add(new TreeInstance
            {
                prototypeIndex = proto,
                position = new Vector3(x / Size + 0.5f, 0, z / Size + 0.5f),
                widthScale = s, heightScale = s * R(0.9f, 1.1f), rotation = R(0, Mathf.PI * 2),
                color = Color.white, lightmapColor = Color.white,
            });
        }
        for (float gz = -Size / 2; gz < Size / 2; gz += 6.5f)
            for (float gx = -Size / 2; gx < Size / 2; gx += 6.5f)
            {
                float x = gx + R(-3, 3), z = gz + R(-3, 3);
                var p = new Vector3(x, 0, z);
                if (!free(p, 5) || RiverDist(x, z) < RiverHalf * 2.5f) continue;
                float f = Forest(x, z);
                if (rng.NextDouble() < f * 0.85f)
                {
                    int k = rng.Next(20);
                    int proto = k < 6 ? 0 : k < 11 ? 1 : k < 14 ? 2 + rng.Next(3) : k < 17 ? 5 + rng.Next(2) : 7 + rng.Next(3);
                    Add(proto, x, z, R(0.85f, 1.35f));
                    bool near = x * x + z * z < 130 * 130; // sous-bois seulement la ou on le voit
                    if (near && rng.NextDouble() < 0.5) Add(7 + rng.Next(3), x + R(-3, 3), z + R(-3, 3), R(0.6f, 1f)); // sous-bois
                    if (near && rng.NextDouble() < 0.4) Add(15 + rng.Next(2), x + R(-4, 4), z + R(-4, 4), R(0.8f, 1.2f));
                }
                else if (rng.NextDouble() < 0.05) Add(10 + rng.Next(5), x, z, R(0.6f, 1.3f));
                else if (rng.NextDouble() < 0.06 * (1 - f)) Add(7 + rng.Next(3), x, z, R(0.6f, 1f));
            }
        td.SetTreeInstances(trees.ToArray(), true);

        // Herbe et fleurs : details instancies GPU, dessines seulement pres de la camera.
        string[] details =
        {
            "SM_Env_Grass_Tall_Clump_01", "SM_Env_Grass_Tall_Clump_03", "SM_Env_Grass_Med_Clump_01", "SM_Env_Grass_Med_Clump_02",
            "SM_Env_Grass_Short_Clump_01", "SM_Env_Grass_Short_Clump_02", "SM_Env_Wildflowers_01", "SM_Env_Wildflowers_02", "SM_Env_Wildflowers_03",
        };
        td.SetDetailResolution(1024, 32);
        td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        td.detailPrototypes = Array.ConvertAll(details, n =>
        {
            var prefab = Synty.Get(n);
            var lod0 = prefab.transform.Find(n + "_LOD0") ?? prefab.transform;
            return new DetailPrototype
            {
                prototype = lod0.gameObject, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit,
                minWidth = 0.9f, maxWidth = 1.5f, minHeight = 0.8f, maxHeight = 1.4f, noiseSpread = 0.4f, alignToGround = 0.4f,
            };
        });
        int dn = td.detailResolution;
        var maps = new int[details.Length][,];
        for (int k = 0; k < maps.Length; k++) maps[k] = new int[dn, dn];
        for (int z = 0; z < dn; z++)
            for (int x = 0; x < dn; x++)
            {
                float wx = -Size / 2 + (x + 0.5f) * Size / dn, wz = -Size / 2 + (z + 0.5f) * Size / dn;
                var p = new Vector3(wx, 0, wz);
                if (!free(p, 0.5f) || RiverDist(wx, wz) < RiverHalf * 1.3f) continue;
                float f = Forest(wx, wz);
                float lush = Mathf.PerlinNoise(wx * 0.06f + 3, wz * 0.06f + 8);
                float flowers = Mathf.InverseLerp(0.6f, 0.78f, Mathf.PerlinNoise(wx * 0.05f + 20, wz * 0.05f + 60));
                int c = rng.Next(100);
                if (c < 22 * lush * (1 - f * 0.6f)) maps[rng.Next(2)][z, x] = 1;
                else if (c < 55) maps[2 + rng.Next(2)][z, x] = 1;
                else if (c < 80) maps[4 + rng.Next(2)][z, x] = 1;
                if (rng.NextDouble() < flowers * 0.7f * (1 - f)) maps[6 + rng.Next(3)][z, x] = 1;
            }
        for (int k = 0; k < maps.Length; k++) td.SetDetailLayer(0, 0, k, maps[k]);

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = "Terrain";
        go.transform.SetParent(parent);
        go.transform.position = new Vector3(-Size / 2, Y0, -Size / 2);
        var t = go.GetComponent<Terrain>();
        t.materialTemplate = Resources.Load<Material>("TerrainLit"); // reference explicite, sinon le shader est retire de la build
        t.treeDistance = 360;
        t.treeBillboardDistance = 420;
        t.detailObjectDistance = 85;
        t.heightmapPixelError = 3;
        t.basemapDistance = 150;
        t.drawInstanced = true;
        return t;
    }

    // Ruban d'eau qui suit la riviere, un peu sous les berges.
    public static void Water(Transform parent, Material mat)
    {
        var pts = new List<Vector2>();
        for (int i = 0; i < River.Length - 1; i++)
        {
            int n = Mathf.CeilToInt(Vector2.Distance(River[i], River[i + 1]) / 3f);
            for (int k = 0; k < n; k++) pts.Add(Vector2.Lerp(River[i], River[i + 1], k / (float)n));
        }
        pts.Add(River[River.Length - 1]);
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        float along = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var dir = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
            var side = new Vector2(-dir.y, dir.x) * RiverHalf * 1.6f;
            float y = Board.GroundBase(pts[i].x, pts[i].y) - 0.55f;
            if (i > 0) along += Vector2.Distance(pts[i], pts[i - 1]);
            v.Add(new Vector3(pts[i].x + side.x, y, pts[i].y + side.y)); uv.Add(new Vector2(0, along / 8));
            v.Add(new Vector3(pts[i].x - side.x, y, pts[i].y - side.y)); uv.Add(new Vector2(1, along / 8));
            if (i > 0) { int b = v.Count - 4; tri.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 }); }
        }
        var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
        var g = new GameObject("Riviere");
        g.transform.SetParent(parent);
        g.AddComponent<MeshFilter>().sharedMesh = m;
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
