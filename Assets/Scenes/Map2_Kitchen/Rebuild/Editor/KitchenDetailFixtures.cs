using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Map2KitchenDraft.Editor
{
    /// <summary>Static, collider-free kitchen fittings for the first four route chapters.</summary>
    public static class KitchenDetailFixtures
    {
        private const string MaterialRoot = "Assets/Scenes/Map2_Kitchen/Rebuild/Materials/V3/";
        private const string MeshPath = "Assets/Scenes/Map2_Kitchen/Rebuild/Art/Meshes/V3/KitchenDetailFixtures.asset";
        private static readonly string[] MaterialNames = { "Steel", "SteelDark", "Trim", "Water", "Warning" };

        public static void Add(Transform parent)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));

            var materials = new Material[MaterialNames.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = AssetDatabase.LoadAssetAtPath<Material>(MaterialRoot + MaterialNames[i] + ".mat");
                if (materials[i] == null) throw new InvalidOperationException("Missing kitchen detail material: " + MaterialNames[i]);
            }

            var geometry = new FixtureMesh(parent);
            AddReturnHatch(geometry);
            AddSterilizer(geometry);
            AddTransferTunnel(geometry);
            AddSinkFittings(geometry);

            var fresh = geometry.Build();
            var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (persistent == null)
            {
                AssetDatabase.CreateAsset(fresh, MeshPath);
                persistent = fresh;
            }
            else
            {
                EditorUtility.CopySerialized(fresh, persistent);
                UnityEngine.Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(persistent);
            }

            var fixture = new GameObject("CH1-4 Kitchen Equipment Fine Details (visual only)");
            fixture.transform.SetParent(parent, false);
            fixture.AddComponent<MeshFilter>().sharedMesh = persistent;
            fixture.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }

        private static void AddReturnHatch(FixtureMesh mesh)
        {
            // Return hatch surrounds the first landing. All details sit on the outer frame.
            foreach (float x in new[] { -123.4f, -112.6f })
            {
                float inward = x < -118f ? .31f : -.31f;
                mesh.Box(0, new Vector3(x + inward, 4.8f, -112), new Vector3(.08f, 5.2f, 1.45f));
                mesh.Box(1, new Vector3(x + inward * 1.25f, 5.0f, -112), new Vector3(.09f, 1.4f, .16f));
                for (int i = 0; i < 5; i++)
                    mesh.Cylinder(2, new Vector3(x + inward * 1.42f, 2.3f + i * 1.18f, -112.6f),
                        .09f, .045f, Quaternion.FromToRotation(Vector3.up, Vector3.right));
            }
            mesh.Box(0, new Vector3(-118, 8.83f, -112), new Vector3(8.1f, .10f, .35f));
            for (int i = 0; i < 7; i++)
                mesh.Cylinder(2, new Vector3(-121.9f + i * 1.3f, 8.88f, -112), .085f, .035f);
        }

        private static void AddSterilizer(FixtureMesh mesh)
        {
            // The small overhead line visually supplies the eight fixed jet emitters.
            mesh.Beam(0, new Vector3(-122.37f, 16.03f, -19), new Vector3(-122.37f, 16.03f, 88), .16f);
            mesh.Beam(0, new Vector3(-122.37f, 16.03f, -19), new Vector3(-127f, 16.03f, -19), .16f);
            mesh.Beam(0, new Vector3(-127f, 16.03f, -19), new Vector3(-127f, 10.5f, -19), .16f);
            mesh.Beam(0, new Vector3(-127f, 10.5f, -19), new Vector3(-141.4f, 10.5f, -19), .16f);
            for (int i = 0; i < 8; i++)
            {
                float z = -10 + i * 13;
                float jetX = -118f + (i % 2 == 0 ? -1.45f : 1.45f);
                mesh.Beam(0, new Vector3(-122.37f, 16.03f, z), new Vector3(jetX, 16.03f, z), .13f);
                mesh.Cylinder(1, new Vector3(jetX, 15.88f, z), .35f, .13f);
                mesh.Cylinder(0, new Vector3(jetX, 15.72f, z), .22f, .07f);
                mesh.Cylinder(3, new Vector3(jetX, 15.64f, z), .09f, .015f);
            }
            for (int bay = 0; bay < 4; bay++)
            {
                float z = -13 + bay * 30;
                mesh.Box(1, new Vector3(-122.37f, 11.03f, z), new Vector3(.22f, 1.15f, 1.5f));
                mesh.Box(0, new Vector3(-122.23f, 11.03f, z), new Vector3(.03f, .83f, 1.13f));
                mesh.Box(4, new Vector3(-122.2f, 11.31f, z + .25f), new Vector3(.04f, .17f, .17f));
                mesh.Box(3, new Vector3(-122.2f, 11.31f, z - .18f), new Vector3(.04f, .17f, .17f));
                mesh.Box(1, new Vector3(-122.19f, 10.75f, z), new Vector3(.04f, .08f, .73f));
                for (int k = 0; k < 3; k++)
                    mesh.Cylinder(2, new Vector3(-122.33f, 13.65f + k * 1.07f, z),
                        .07f, .03f, Quaternion.FromToRotation(Vector3.up, Vector3.right));
            }
        }

        private static void AddTransferTunnel(FixtureMesh mesh)
        {
            // Access plates and fasteners are on the tunnel's back, away from the dish route.
            for (int i = 0; i < 4; i++)
            {
                float x = -98 + i * 11;
                mesh.Box(0, new Vector3(x, 14.2f, 108.01f), new Vector3(8.2f, 3.7f, .06f));
                mesh.Box(1, new Vector3(x, 14.2f, 108.05f), new Vector3(7.8f, 3.3f, .025f));
                mesh.Box(0, new Vector3(x, 14.2f, 108.07f), new Vector3(6.8f, 2.3f, .025f));
                mesh.Box(1, new Vector3(x + 2.9f, 14.2f, 108.10f), new Vector3(.14f, 1.2f, .08f));
                foreach (float side in new[] { -1f, 1f })
                    foreach (float height in new[] { -1.4f, 1.4f })
                        mesh.Cylinder(2, new Vector3(x + side * 3.62f, 14.2f + height, 108.11f),
                            .075f, .025f, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
            }
        }

        private static void AddSinkFittings(FixtureMesh mesh)
        {
            // Valves and splash strips sit behind the existing faucets, high on the basin wall.
            mesh.Box(0, new Vector3(-59.81f, 10.2f, 55.4f), new Vector3(.09f, .16f, 43.0f));
            for (int i = 0; i < 5; i++)
            {
                float z = 39 + i * 8.2f;
                mesh.Cylinder(1, new Vector3(-59.54f, 11.0f, z), .42f, .10f,
                    Quaternion.FromToRotation(Vector3.up, Vector3.right));
                mesh.Cylinder(0, new Vector3(-59.39f, 11.0f, z), .26f, .06f,
                    Quaternion.FromToRotation(Vector3.up, Vector3.right));
                mesh.Box(4, new Vector3(-59.31f, 11.01f, z + .28f), new Vector3(.035f, .08f, .15f));
                mesh.Cylinder(1, new Vector3(-50f, 14.13f, z), .31f, .045f);
            }
            // Flush floor bolts and an edge reveal make the recessed well easier to read.
            foreach (float x in new[] { -59.7f, -36.3f })
            {
                mesh.Box(1, new Vector3(x, 12.36f, 52), new Vector3(.055f, .055f, 58));
                for (int i = 0; i < 10; i++)
                    mesh.Cylinder(2, new Vector3(x, 12.42f, 24.5f + i * 6), .072f, .025f);
            }
            foreach (float z in new[] { 34f, 50f, 66f })
            {
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * Mathf.PI / 3f;
                    mesh.Cylinder(2, new Vector3(-54.7f + Mathf.Cos(angle) * 1.53f, 1.12f,
                        z + Mathf.Sin(angle) * 1.53f), .055f, .018f);
                }
            }
        }

        private sealed class FixtureMesh
        {
            private readonly Transform parent;
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<int>[] triangles = new List<int>[MaterialNames.Length];

            public FixtureMesh(Transform parent)
            {
                this.parent = parent;
                for (int i = 0; i < triangles.Length; i++) triangles[i] = new List<int>();
            }

            public void Box(int material, Vector3 center, Vector3 size)
            {
                Vector3 half = size * .5f;
                Vector3[] corners =
                {
                    new Vector3(-half.x,-half.y,-half.z), new Vector3(half.x,-half.y,-half.z),
                    new Vector3(half.x,half.y,-half.z), new Vector3(-half.x,half.y,-half.z),
                    new Vector3(-half.x,-half.y,half.z), new Vector3(half.x,-half.y,half.z),
                    new Vector3(half.x,half.y,half.z), new Vector3(-half.x,half.y,half.z)
                };
                int[,] faces = { { 0, 3, 2, 1 }, { 5, 6, 7, 4 }, { 4, 7, 3, 0 },
                    { 1, 2, 6, 5 }, { 3, 7, 6, 2 }, { 4, 0, 1, 5 } };
                for (int f = 0; f < 6; f++)
                {
                    int first = vertices.Count;
                    for (int k = 0; k < 4; k++) vertices.Add(parent.InverseTransformPoint(center + corners[faces[f,k]]));
                    AddQuad(material, first);
                }
            }

            public void Beam(int material, Vector3 a, Vector3 b, float radius)
            {
                Vector3 direction = b - a;
                Cylinder(material, (a + b) * .5f, radius, direction.magnitude * .5f,
                    Quaternion.FromToRotation(Vector3.up, direction));
            }

            public void Cylinder(int material, Vector3 center, float radius, float halfHeight,
                Quaternion rotation = default)
            {
                if (rotation == default) rotation = Quaternion.identity;
                const int sides = 12;
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    float b = (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 p = new Vector3(Mathf.Cos(a) * radius, -halfHeight, Mathf.Sin(a) * radius);
                    Vector3 q = new Vector3(Mathf.Cos(b) * radius, -halfHeight, Mathf.Sin(b) * radius);
                    int side = vertices.Count;
                    Push(center, rotation, p);
                    Push(center, rotation, p + Vector3.up * (halfHeight * 2f));
                    Push(center, rotation, q + Vector3.up * (halfHeight * 2f));
                    Push(center, rotation, q);
                    AddQuad(material, side);
                    int caps = vertices.Count;
                    Push(center, rotation, Vector3.down * halfHeight);
                    Push(center, rotation, q);
                    Push(center, rotation, p);
                    Push(center, rotation, Vector3.up * halfHeight);
                    Push(center, rotation, p + Vector3.up * (halfHeight * 2f));
                    Push(center, rotation, q + Vector3.up * (halfHeight * 2f));
                    triangles[material].AddRange(new[] { caps, caps + 2, caps + 1, caps + 3, caps + 5, caps + 4 });
                }
            }

            private void Push(Vector3 center, Quaternion rotation, Vector3 local)
            {
                vertices.Add(parent.InverseTransformPoint(center + rotation * local));
            }

            private void AddQuad(int material, int first)
            {
                triangles[material].AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }

            public Mesh Build()
            {
                var result = new Mesh { name = "Kitchen CH1-4 visual fittings" };
                result.SetVertices(vertices);
                result.subMeshCount = triangles.Length;
                for (int i = 0; i < triangles.Length; i++) result.SetTriangles(triangles[i], i);
                result.RecalculateNormals();
                result.RecalculateBounds();
                return result;
            }
        }
    }
}
