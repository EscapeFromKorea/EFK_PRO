using System;
using UnityEngine;
using Shape = PlayerShapeStats.ShapeKind;

internal static partial class Map1SectorUpgrade
{
    private static void UpgradeP06(Transform sector)
    {
        var baseFloor = sector.Find("P06_Base/P06_Base_Collision");
        var upperFloor = sector.Find("P06_Top/P06_Top_Collision");
        var oldGate = sector.Find("P06_TeamLiftGate");
        var oldTopSwitch = sector.Find("P06_Tetra_TopSwitch");
        if (baseFloor == null || upperFloor == null || oldGate == null || oldTopSwitch == null)
            throw new InvalidOperationException("P06의 기존 발판, 게이트 또는 스위치를 찾을 수 없습니다.");

        var lower = baseFloor.GetComponent<BoxCollider>().bounds;
        var upper = upperFloor.GetComponent<BoxCollider>().bounds;
        var baseY = lower.max.y;
        var topY = upper.max.y;
        var west = lower.min.x;
        var east = lower.max.x;
        var baseZ = lower.center.z;

        var lilac = new Color(0.52f, 0.46f, 0.76f);
        var teal = new Color(0.31f, 0.72f, 0.78f);
        var bronze = new Color(0.87f, 0.60f, 0.31f);

        // Sphere route: boost west over a channel, then use the powered return bridge to board.
        var landing = new Vector3(west - 7.3f, baseY - 0.38f, baseZ - 1.1f);
        Box(sector, "P06_PowerIsland", landing, new Vector3(8.8f, 0.56f, 5.2f), teal);
        Box(sector, "P06_PowerIsland_Backstop", landing + new Vector3(-4.4f, 0.75f, 0f),
            new Vector3(0.28f, 1.5f, 5.2f), lilac);
        Box(sector, "P06_PowerIsland_NorthRail", landing + new Vector3(-2.0f, 0.68f, 2.59f),
            new Vector3(4.7f, 1.15f, 0.22f), lilac);
        var boost = Box(sector, "P06_Sphere_RunUp", new Vector3(west + 1.15f, baseY + 0.1f, baseZ - 1.1f),
            new Vector3(0.95f, 0.3f, 1.8f), teal);
        boost.transform.rotation = Quaternion.LookRotation(Vector3.left);
        boost.GetComponent<BoxCollider>().isTrigger = true;
        var accelerator = boost.AddComponent<AccelPad>();
        accelerator.boostSpeed = 8.5f;
        accelerator.holdDuration = 0.38f;
        accelerator.decelDuration = 0.2f;

        var power = Switch(sector, "P06_Sphere_Power", landing + new Vector3(-0.5f, 0.72f, 1.05f),
            new Vector3(2.8f, 0.9f, 1.5f), Shape.Sphere);
        var returnBridge = Box(sector, "P06_Power_ReturnBridge",
            new Vector3(west - 1.5f, baseY - 0.2f, baseZ + 0.55f), new Vector3(3.2f, 0.4f, 1.65f), teal);
        returnBridge.SetActive(false);
        power.reveal = new[] { returnBridge };

        // Cube and tetrahedron can prepare their stations before or after the sphere.
        var weight = Switch(sector, "P06_Cube_LiftUnlock", new Vector3(east - 1.95f, baseY + 0.5f, baseZ + 0.6f),
            new Vector3(2.2f, 0.95f, 2.3f), Shape.Cube);
        var ready = Switch(sector, "P06_Tetra_UpperDock", new Vector3(oldTopSwitch.position.x, topY + 0.5f, oldTopSwitch.position.z),
            new Vector3(2.4f, 0.95f, 2.4f), Shape.Tetrahedron);

        // Shafts and catwalks sit outside the wall's footprint and outside the lift's sweep.
        var liftCenter = new Vector3(east + 2.4f, baseY - 0.24f, baseZ + 2.4f);
        var lift = Box(sector, "P06_TeamLift", liftCenter, new Vector3(4.6f, 0.48f, 4.4f), bronze);
        float catwalkStart = liftCenter.z + 2.28f;
        float catwalkEnd = upper.center.z + 0.1f;
        float inner = upper.max.x;
        float outer = liftCenter.x + 2.3f;
        Box(sector, "P06_UpperCatwalk", new Vector3((inner + outer) * 0.5f, topY - 0.21f, (catwalkStart + catwalkEnd) * 0.5f),
            new Vector3(outer - inner, 0.42f, catwalkEnd - catwalkStart), lilac);
        Box(sector, "P06_Catwalk_Edge", new Vector3(outer - 0.13f, topY + 0.45f, (catwalkStart + catwalkEnd) * 0.5f),
            new Vector3(0.26f, 0.9f, catwalkEnd - catwalkStart), lilac);
        Box(sector, "P06_Catwalk_End", new Vector3((inner + outer) * 0.5f, topY + 0.45f, catwalkEnd - 0.13f),
            new Vector3(outer - inner, 0.9f, 0.26f), lilac);
        var movement = lift.AddComponent<Map1CoopPlatform>();
        movement.pickup = liftCenter;
        movement.destination = liftCenter + Vector3.up * (topY - baseY);
        movement.requires = new[] { power, weight, ready };
        movement.passengers = new[] { Shape.Sphere, Shape.Cube };

        oldGate.gameObject.SetActive(false);
        oldTopSwitch.gameObject.SetActive(false);
    }

}
