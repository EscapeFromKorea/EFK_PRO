using System;
using UnityEngine;
using ShapeKind = PlayerShapeStats.ShapeKind;

internal static partial class Map1SectorUpgrade
{
    private static void UpgradeP07(Transform sector)
    {
        Vector3 At(float x, float y, float z) => sector.TransformPoint(new Vector3(x, y, z));
        var stone = new Color(0.29f, 0.34f, 0.56f);
        var brass = new Color(0.71f, 0.53f, 0.25f);

        // Move the disabled pedestal onto the entry lip so the cube can dock without crossing the gap.
        Transform pedestal = sector.Find("P07_Cube_Pedestal");
        pedestal.position += At(-4.4f, 6.8f, 225.5f) -
            pedestal.Find("P07_Cube_Pedestal_Collision").position;
        pedestal.gameObject.SetActive(true);
        Box(sector, "P07_AnchorApproach", At(-4.4f, 5.85f, 224f), new Vector3(3.4f, 0.6f, 1.4f), stone);
        Map1CoopSwitch dock = Switch(sector, "P07_Cube_Dock", At(-4.4f, 7.23f, 225.5f),
            new Vector3(2.5f, 0.85f, 2.45f), ShapeKind.Cube);

        // The lower shelf gives the sphere space to release the cube's thread and run up to the leap.
        GameObject swingShelf = Box(sector, "P07_Sphere_SwingShelf", At(-4.4f, 4.35f, 230.4f),
            new Vector3(3.6f, 0.5f, 3.8f), stone);
        GameObject launchLip = Box(sector, "P07_Sphere_Runup", At(-4.4f, 4.64f, 232.45f),
            new Vector3(3.6f, 0.5f, 0.6f), brass);
        dock.reveal = new[] { swingShelf, launchLip };
        swingShelf.SetActive(false);
        launchLip.SetActive(false);
        Map1CoopSwitch release = Switch(sector, "P07_Sphere_Release", At(-4.4f, 6.26f, 236.4f),
            new Vector3(2.6f, 0.85f, 2.1f), ShapeKind.Sphere);
        release.requires = new[] { dock };

        // Keep the catch anchor on its own approach so it won't steal the sphere's cube attachment.
        Box(sector, "P07_Tetra_Approach", At(0.8f, 6.03f, 225.5f), new Vector3(2.7f, 0.55f, 1.8f), stone);
        Box(sector, "P07_Tetra_LowLedge", At(0.8f, 6.46f, 229.2f), new Vector3(2.7f, 0.55f, 3f), stone);
        Transform catchAnchor = sector.Find("P07_CatchAnchor");
        catchAnchor.position = At(0.8f, 8.85f, 231.1f);
        Box(sector, "P07_Tetra_HighLedge", At(0.8f, 7.22f, 235.25f), new Vector3(3f, 0.6f, 2.2f), stone);
        Map1CoopSwitch brace = Switch(sector, "P07_Tetra_BridgeBrace", At(0.8f, 8.02f, 235.25f),
            new Vector3(2.5f, 0.9f, 1.8f), ShapeKind.Tetrahedron);
        brace.requires = new[] { release };

        // Reuse the original rainbow segments, but drive them with the far-side relay's lasting state.
        Transform bridge = sector.Find("P07_Cube_ReturnBridge");
        bridge.Find("Switch").gameObject.SetActive(false);
        for (int i = 1; i <= 6; i++)
        {
            Transform segment = bridge.Find($"Segment_{i}");
            if (segment == null)
                throw new InvalidOperationException($"P07 rainbow segment {i} is missing.");
            segment.position = At(4.8f, 5.66f, 226.57f + (i - 1) * 1.48f);
            segment.rotation = sector.rotation;
            segment.localScale = new Vector3(2.8f, 0.25f, 1.48f);
            segment.GetComponent<Collider>().enabled = true;
            segment.GetComponent<Renderer>().enabled = true;
        }
        brace.reveal = new[] { bridge.gameObject };
        bridge.gameObject.SetActive(false);
        RefineP07(sector);
    }

    private static bool RefineP07(Transform sector)
    {
        if (sector.Find("P07_Tetra_PinWall") != null) return false;

        Vector3 At(float x, float y, float z) => sector.TransformPoint(new Vector3(x, y, z));
        var sphereColor = new Color(.27f, .63f, .94f);
        var tetraColor = new Color(.64f, .42f, .88f);
        var cubeColor = new Color(.88f, .55f, .24f);

        // The central lane is an actual gap. Only the high catch anchor crosses it.
        sector.Find("P07_Tetra_LowLedge").gameObject.SetActive(false);
        Transform catchAnchor = sector.Find("P07_CatchAnchor");
        catchAnchor.position = At(.8f, 9f, 230.4f);
        catchAnchor.GetComponent<ThreadAnchor>().connectRange = 5.8f;
        ColorDeck("P07_Tetra_Approach", tetraColor);

        // Put the tetra switch over the far bank, above unassisted jump height.
        Box(sector, "P07_Tetra_PinWall", At(.8f, 8.57f, 238.2f),
            new Vector3(3.6f, 5.55f, .65f), tetraColor);
        Transform highLedge = sector.Find("P07_Tetra_HighLedge");
        highLedge.position = At(.8f, 11.4f, 239.12f);
        ColorDeck(highLedge.name, tetraColor);
        var brace = sector.Find("P07_Tetra_BridgeBrace").GetComponent<Map1CoopSwitch>();
        brace.transform.position = At(.8f, 12.14f, 239.12f);

        // Shorten the sphere shelf from the near side so it is no longer a convenient cube step.
        Transform shelf = sector.Find("P07_Sphere_SwingShelf");
        shelf.position = At(-4.4f, 4.35f, 232.06f);
        var shelfCollider = shelf.GetComponent<BoxCollider>();
        shelfCollider.size = new Vector3(3.6f, .5f, 1.72f);
        shelf.Find("Visual").localScale = shelfCollider.size;
        sector.Find("P07_Sphere_Runup").position = At(-4.4f, 4.57f, 233.22f);
        ColorDeck(shelf.name, sphereColor);
        ColorDeck("P07_Sphere_Runup", sphereColor);
        ColorDeck("P07_AnchorApproach", cubeColor);

        brace.reveal = new[] { sector.Find("P07_Cube_ReturnBridge").gameObject };
        return true;

        void ColorDeck(string name, Color color) =>
            sector.Find(name + "/Visual").GetComponent<Renderer>().sharedMaterial = MaterialFor(color);

    }
}
