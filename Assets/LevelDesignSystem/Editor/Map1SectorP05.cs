using UnityEditor;
using UnityEngine;

internal static partial class Map1SectorUpgrade
{
    private static void UpgradeP05(Transform sector)
    {
        // In the playable scene these seven props occupy the lane needed by the shuttle.
        // Retain the authored instances so a designer can restore the old route.
        for (int i = 1; i <= 7; i++)
        {
            Transform step = sector.Find($"P05_Cube_Bypass_{i}");
            if (step == null) continue;
            Undo.RecordObject(step.gameObject, "Replace P05 bypass");
            step.gameObject.SetActive(false);
        }

        Color pier = new Color(0.26f, 0.51f, 0.66f);
        Color cargo = new Color(0.84f, 0.58f, 0.28f);
        Color ledge = new Color(0.37f, 0.65f, 0.58f);
        Color rampColor = new Color(0.55f, 0.51f, 0.77f);

        Box(sector, "P05_Pickup_Pier", new Vector3(6.1f, 2.7f, 157.95f),
            new Vector3(3.6f, 0.6f, 1.9f), pier);
        Box(sector, "P05_Destination_Pier", new Vector3(6.1f, 3.1f, 175.9f),
            new Vector3(3.6f, 0.6f, 2.2f), pier);
        Map1CoopSwitch call = Switch(sector, "P05_Cube_Call", new Vector3(6.1f, 3.35f, 156.7f),
            new Vector3(2.4f, 0.8f, 1.4f), PlayerShapeStats.ShapeKind.Cube);

        Box(sector, "P05_Anchor_Approach", new Vector3(-1.5f, 3.05f, 158.45f),
            new Vector3(2.6f, 0.6f, 2f), ledge);
        Box(sector, "P05_Transfer_Shelf", new Vector3(2.2f, 4.0f, 165.4f),
            new Vector3(3.4f, 0.6f, 2.9f), ledge);
        Box(sector, "P05_Swing_Rest", new Vector3(-0.1f, 3.95f, 172.2f),
            new Vector3(3.8f, 0.6f, 2.6f), ledge);
        Box(sector, "P05_Sphere_Exit_Shelf", new Vector3(3.2f, 3.1f, 175.9f),
            new Vector3(2.5f, 0.6f, 2.2f), ledge);
        Map1CoopSwitch depart = Switch(sector, "P05_Sphere_Send", new Vector3(2.2f, 4.65f, 165.4f),
            new Vector3(1.8f, 0.8f, 1.5f), PlayerShapeStats.ShapeKind.Sphere);

        Box(sector, "P05_Tetra_High_Shelf", new Vector3(-2.4f, 4.6f, 177.6f),
            new Vector3(3.2f, 0.6f, 2.4f), ledge);
        Map1CoopSwitch rampSwitch = Switch(sector, "P05_Tetra_Unfold_Ramp",
            new Vector3(-2.4f, 5.25f, 177.6f), new Vector3(2f, 0.8f, 1.4f),
            PlayerShapeStats.ShapeKind.Tetrahedron);
        GameObject ramp = Box(sector, "P05_Reunion_Ramp", new Vector3(4.7f, 3.35f, 178.35f),
            new Vector3(6.3f, 0.5f, 2.85f), rampColor);
        ramp.transform.rotation = Quaternion.Euler(-8f, 0f, 0f);
        ramp.SetActive(false);
        rampSwitch.reveal = new[] { ramp };

        GameObject shuttle = Box(sector, "P05_Cargo_Shuttle", new Vector3(6.1f, 3.1f, 167f),
            new Vector3(3.35f, 0.6f, 3.35f), cargo);
        Map1CoopPlatform transport = shuttle.AddComponent<Map1CoopPlatform>();
        transport.call = call;
        transport.depart = depart;
        transport.pickup = new Vector3(6.1f, 2.7f, 160.45f);
        transport.destination = new Vector3(6.1f, 3.1f, 173.14f);
        transport.passengers = new[] { PlayerShapeStats.ShapeKind.Cube };
    }
}
