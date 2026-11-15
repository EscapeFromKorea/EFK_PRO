using System;
using UnityEditor;
using UnityEngine;

namespace Map2KitchenDraft.Editor
{
    /// <summary>Noncolliding restaurant detail around the dish racks and cooking line.</summary>
    public static class KitchenDetailCooking
    {
        private const string MaterialRoot = "Assets/Scenes/Map2_Kitchen/Rebuild/Materials/V3/";

        public static void Add(Transform parent)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            var details = new GameObject("CH6-7 dish storage and cooking detail").transform;
            details.SetParent(parent, false);
            details.localPosition = Vector3.zero;
            details.localRotation = Quaternion.identity;
            details.localScale = Vector3.one;

            AddSortingRackDetails(details);
            AddUtensilRail(details);
            AddCookingRangeDetails(details);
            AddFoodPrepDetails(details);
        }

        private static void AddSortingRackDetails(Transform parent)
        {
            // Existing upper shelves are at Y11.2 and Z109.8. Decor sits on those shelves,
            // behind the playable conveyor centered on Z100 and away from the plate bridge.
            float[] rackCenters = { 16f, 32f, 80f, 96f, 112f };
            foreach (float center in rackCenters)
            {
                foreach (float offset in new[] { -4.6f, 4.6f })
                    Box("Dish rack floor upright", parent,
                        new Vector3(center + offset, -3.05f, 111.6f), new Vector3(.38f, 29.9f, .44f), "SteelDark");
                Box("Rack upper shelf front rail", parent,
                    new Vector3(center, 11.31f, 108.72f), new Vector3(11.1f, .13f, .12f), "Trim");
                for (int stack = 0; stack < 3; stack++)
                {
                    float x = center - 3.3f + stack * 3.3f;
                    // A shallow contrasting base and three closely stacked ceramic plates.
                    Cylinder("Dish stack base", parent,
                        new Vector3(x, 11.36f, 109.35f), new Vector3(.91f, .035f, .91f), "SteelDark");
                    for (int dish = 0; dish < 3; dish++)
                        Cylinder("Clean sorted ceramic plate", parent,
                            new Vector3(x, 11.43f + dish * .105f, 109.35f),
                            new Vector3(.79f, .038f, .79f), "Plate");
                }
            }
        }

        private static void AddUtensilRail(Transform parent)
        {
            // The service rail hangs above and behind the east end of the sorting racks.
            Box("Sorted utensils suspension bar", parent,
                new Vector3(98f, 14.0f, 108.0f), new Vector3(22f, .18f, .22f), "Steel");
            foreach (float x in new[] { 91.4f, 104.6f })
            {
                Box("Utensil rack return bracket", parent,
                    new Vector3(x, 14, 110), new Vector3(.24f, .20f, 4.2f), "Steel");
                Box("Utensil rack rear post", parent,
                    new Vector3(x, 12.8f, 111.6f), new Vector3(.25f, 2.6f, .24f), "Steel");
            }
            for (int i = 0; i < 6; i++)
            {
                float x = 89f + i * 3.5f;
                Box("Hanging utensil stem", parent,
                    new Vector3(x, 12.84f, 108.0f), new Vector3(.14f, 2.2f, .14f), "SteelDark");
                Cylinder(i % 2 == 0 ? "Hanging ladle bowl" : "Hanging skimmer head", parent,
                    new Vector3(x, 11.7f, 108.0f),
                    new Vector3(i % 2 == 0 ? .38f : .48f, .07f, i % 2 == 0 ? .38f : .48f),
                    i % 2 == 0 ? "Steel" : "Trim");
            }
        }

        private static void AddCookingRangeDetails(Transform parent)
        {
            // Six existing range islands sit east of the CH7 conveyor at X137.8.
            for (int station = 0; station < 6; station++)
            {
                float z = -24f - station * 18f;
                Box("Range front control strip", parent,
                    new Vector3(133.15f, 7.79f, z), new Vector3(.20f, .19f, 7.9f), "SteelDark");
                for (int burner = 0; burner < 2; burner++)
                {
                    float burnerZ = z + (burner == 0 ? -2.1f : 2.1f);
                    // Existing pans and handles remain; this gives each cooking zone a
                    // readable burner border and a visible heat control at the aisle side.
                    Cylinder("Induction burner perimeter", parent,
                        new Vector3(137.7f, 8.14f, burnerZ),
                        new Vector3(2.08f, .016f, 2.08f), "Trim");
                    Cylinder("Induction burner dark center", parent,
                        new Vector3(137.7f, 8.156f, burnerZ),
                        new Vector3(1.90f, .014f, 1.90f), "Drain");
                    Cylinder("Range control dial", parent,
                        new Vector3(133.06f, 7.97f, burnerZ),
                        new Vector3(.24f, .055f, .24f), "Steel");
                    Box("Pan handle mounting joint", parent,
                        new Vector3(138.77f, 8.32f, burnerZ),
                        new Vector3(.81f, .13f, .23f), "SteelDark");
                }

                // Baffle divisions hang under the existing extractor canopy at Y16.8.
                for (int fin = -1; fin <= 1; fin++)
                    Box("Extractor hood washable baffle", parent,
                        new Vector3(139.0f + fin * 1.8f, 16.04f, z),
                        new Vector3(.18f, .12f, 6.2f), "Steel");
                Box("Extractor front fascia seam", parent,
                    new Vector3(132.0f, 16.27f, z), new Vector3(.11f, .16f, 9.4f), "Trim");
            }
        }

        private static void AddFoodPrepDetails(Transform parent)
        {
            // Chopping boards occupy the foodline counter beyond the route at Z-130.2.
            // The pre-existing bowls remain at X(center +/-3.5), so boards fill the middle.
            for (int station = 0; station < 8; station++)
            {
                float x = 108f - station * 16f;
                Box("Food prep chopping board", parent,
                    new Vector3(x, 7.80f, -131.8f), new Vector3(2.3f, .13f, 1.4f), "Trim");
                Box("Food prep board handle", parent,
                    new Vector3(x + 1.25f, 7.80f, -131.8f), new Vector3(.42f, .09f, .48f), "Trim");
                Box("Food prep resting knife blade", parent,
                    new Vector3(x - .3f, 7.88f, -131.85f), new Vector3(1.22f, .035f, .14f), "Steel");
                Box("Food prep resting knife grip", parent,
                    new Vector3(x + .47f, 7.88f, -131.85f), new Vector3(.42f, .075f, .19f), "SteelDark");
            }
        }

        private static void Box(string name, Transform parent, Vector3 position, Vector3 scale, string material)
        { Shape(PrimitiveType.Cube, name, parent, position, scale, material); }

        private static void Cylinder(string name, Transform parent, Vector3 position, Vector3 scale, string material)
        { Shape(PrimitiveType.Cylinder, name, parent, position, scale, material); }

        private static void Shape(PrimitiveType primitive, string name, Transform parent,
            Vector3 position, Vector3 scale, string material)
        {
            var item = GameObject.CreatePrimitive(primitive);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.position = position;
            item.transform.localScale = scale;
            var collider = item.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            var shared = AssetDatabase.LoadAssetAtPath<Material>(MaterialRoot + material + ".mat");
            if (shared == null) throw new InvalidOperationException("Missing kitchen detail material: " + material);
            item.GetComponent<Renderer>().sharedMaterial = shared;
            GameObjectUtility.SetStaticEditorFlags(item, StaticEditorFlags.BatchingStatic);
        }
    }
}
