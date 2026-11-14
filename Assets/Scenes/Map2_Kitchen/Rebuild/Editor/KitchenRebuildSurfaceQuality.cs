using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Map2KitchenDraft.Editor
{
    /// <summary>Persistent surface treatment for the restaurant's modular steelwork.</summary>
    public static class KitchenRebuildSurfaceQuality
    {
        private const string AssetRoot = "Assets/Scenes/Map2_Kitchen/Rebuild";
        private const string MeshRoot = AssetRoot + "/Art/SurfaceMeshes";
        private const string TextureRoot = AssetRoot + "/Art/Textures";
        private const string MaterialRoot = AssetRoot + "/Materials/V3";

        public static void Apply(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            EnsureFolder(MeshRoot);
            EnsureFolder(TextureRoot);
            ApplyMaterialTextures();
            int treated = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || filter.sharedMesh == null || !filter.sharedMesh.name.StartsWith("Cube", StringComparison.Ordinal))
                    continue;
                var material = renderer.sharedMaterial;
                if (material == null) continue;
                string path = AssetDatabase.GetAssetPath(material);
                if (string.IsNullOrEmpty(path) || !path.StartsWith(MaterialRoot + "/", StringComparison.Ordinal))
                    continue;
                // Long, thin rails and metal equipment benefit from a real edge highlight.
                // Conveyor walking beds, tile floor and sink collision floor stay visually level.
                if (material.name != "Steel" && material.name != "SteelDark" && material.name != "Trim")
                    continue;
                Vector3 dimensions = filter.transform.lossyScale;
                dimensions = new Vector3(Mathf.Abs(dimensions.x), Mathf.Abs(dimensions.y), Mathf.Abs(dimensions.z));
                float shortest = Mathf.Min(dimensions.x, Mathf.Min(dimensions.y, dimensions.z));
                if (shortest < .08f || dimensions.magnitude > 90f) continue;
                float bevel = Mathf.Clamp(shortest * .16f, .018f, .105f);
                filter.sharedMesh = BeveledCube(dimensions, bevel);
                treated++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Map2 Rebuild] Persistent brushed surfaces and beveled steel pieces: " + treated);
        }

        private static void ApplyMaterialTextures()
        {
            var names = new[] { "Steel", "SteelDark", "Wall", "Floor" };
            foreach (string name in names)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialRoot + "/" + name + ".mat");
                if (material == null) continue;
                bool tile = name == "Wall" || name == "Floor";
                Texture2D texture = MakeTexture(name, tile);
                material.mainTexture = texture;
                material.mainTextureScale = tile ? new Vector2(8f, 8f) : new Vector2(5f, 5f);
                EditorUtility.SetDirty(material);
            }
        }

        private static Texture2D MakeTexture(string name, bool tile)
        {
            string path = TextureRoot + "/" + name + "_detail.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int side = 256;
            var scratch = new Texture2D(side, side, TextureFormat.RGBA32, true);
            scratch.name = name + "_detail";
            var pixels = new Color32[side * side];
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    int seed = ((x * 73856093) ^ (y * 19349663) ^ (name.Length * 83492791)) & 255;
                    int noise = seed % 11 - 5;
                    int value;
                    if (tile)
                    {
                        int grout = name == "Floor" ? 3 : 2;
                        bool seam = x % 32 < grout || y % 32 < grout;
                        value = seam ? 197 + noise : 244 + noise;
                        if (name == "Wall" && y % 64 < 2) value -= 7;
                    }
                    else
                    {
                        // Soft horizontal brushing, restrained enough to keep the stainless hue.
                        int brush = (y % 11 == 0 ? -8 : (y % 7 == 0 ? 5 : 0));
                        value = 239 + noise + brush;
                    }
                    byte v = (byte)Mathf.Clamp(value, 0, 255);
                    pixels[y * side + x] = new Color32(v, v, v, 255);
                }
            }
            scratch.SetPixels32(pixels);
            scratch.Apply(true, false);
            byte[] png = scratch.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(scratch);
            string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
            File.WriteAllBytes(fullPath, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Mesh BeveledCube(Vector3 dimensions, float worldRadius)
        {
            string label = Token(dimensions.x) + "_" + Token(dimensions.y) + "_" + Token(dimensions.z) + "_" + Token(worldRadius);
            string path = MeshRoot + "/Bevel_" + label + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            Vector3 radii = new Vector3(worldRadius / dimensions.x, worldRadius / dimensions.y, worldRadius / dimensions.z);
            radii.x = Mathf.Min(radii.x, .24f);
            radii.y = Mathf.Min(radii.y, .24f);
            radii.z = Mathf.Min(radii.z, .24f);
            var vertices = new List<Vector3>(200);
            var triangles = new List<int>(300);
            var uvs = new List<Vector2>(200);

            void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c + d) * .25f) < 0f)
                {
                    Vector3 temp = b; b = d; d = temp;
                }
                int i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                uvs.Add(Vector2.zero); uvs.Add(Vector2.up); uvs.Add(Vector2.one); uvs.Add(Vector2.right);
                triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
                triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
            }
            void AddCorner(Vector3 a, Vector3 b, Vector3 c)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f) < 0f)
                { Vector3 temp = b; b = c; c = temp; }
                int i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                uvs.Add(Vector2.zero); uvs.Add(Vector2.up); uvs.Add(Vector2.right);
                triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            }
            float[] inner = { .5f - radii.x, .5f - radii.y, .5f - radii.z };
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3;
                int v = (axis + 2) % 3;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 a = Vector3.zero, b = Vector3.zero, c = Vector3.zero, d = Vector3.zero;
                    a[axis] = b[axis] = c[axis] = d[axis] = sign * .5f;
                    a[u] = b[u] = -inner[u]; c[u] = d[u] = inner[u];
                    a[v] = d[v] = -inner[v]; b[v] = c[v] = inner[v];
                    AddFace(a, b, c, d);
                }
            }
            for (int axisA = 0; axisA < 3; axisA++)
            for (int axisB = axisA + 1; axisB < 3; axisB++)
            {
                int axisC = 3 - axisA - axisB;
                for (int sa = -1; sa <= 1; sa += 2)
                for (int sb = -1; sb <= 1; sb += 2)
                {
                    Vector3 a = Vector3.zero, b = Vector3.zero, c = Vector3.zero, d = Vector3.zero;
                    a[axisA] = b[axisA] = sa * .5f;
                    c[axisA] = d[axisA] = sa * inner[axisA];
                    a[axisB] = b[axisB] = sb * inner[axisB];
                    c[axisB] = d[axisB] = sb * .5f;
                    a[axisC] = d[axisC] = -inner[axisC];
                    b[axisC] = c[axisC] = inner[axisC];
                    AddFace(a, b, c, d);
                }
            }
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                AddCorner(new Vector3(sx * .5f, sy * inner[1], sz * inner[2]),
                          new Vector3(sx * inner[0], sy * .5f, sz * inner[2]),
                          new Vector3(sx * inner[0], sy * inner[1], sz * .5f));
            }
            var mesh = new Mesh { name = "Beveled steel box " + label };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static string Token(float value)
        { return value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', 'p'); }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
