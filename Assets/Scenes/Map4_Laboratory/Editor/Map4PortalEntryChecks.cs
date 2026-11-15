#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

/// <summary>P1의 한 방향만 새 Master 세션에서 물리 이동으로 검사한다. 키 입력/CH6 완주는 별개다.</summary>
public static class Map4PortalEntryChecks
{
    public static IEnumerator Run(SectorController sector, PlayerMover[] players,
        Action<string, bool, string> record) => RunNorth(sector, players, record);

    public static IEnumerator RunNorth(SectorController sector, PlayerMover[] players,
        Action<string, bool, string> record) => RunDirection(sector, players, record, false);

    public static IEnumerator RunSouth(SectorController sector, PlayerMover[] players,
        Action<string, bool, string> record) => RunDirection(sector, players, record, true);

    public static IEnumerator RunSouthCubeBrake(SectorController sector, PlayerMover[] players,
        Action<string, bool, string> record) => RunDirection(sector, players, record, true, "Cube", true);

    public static IEnumerator RunSouthSphere(SectorController sector, PlayerMover[] players,
        Action<string, bool, string> record) => RunDirection(sector, players, record, true, "Sphere");

    public static IEnumerator RunSouthSphereBrake(SectorController sector, PlayerMover[] players,
        Action<string, bool, string> record) => RunDirection(sector, players, record, true, "Sphere", true);

    public static IEnumerator RunSouthTetra(SectorController sector, PlayerMover[] players,
        Action<string, bool, string> record) => RunDirection(sector, players, record, true, "Tetrahedron");

    public static IEnumerator RunDirection(SectorController sector, PlayerMover[] players,
        Action<string, bool, string> record, bool south, string shape = "Cube", bool brakeAtCenter = false)
    {
        if (record == null) yield break;
        string label = shape + (brakeAtCenter ? "중앙정지" : "") + (south ? "남쪽" : "북쪽");
        PlayerMover cube = players == null ? null : players.FirstOrDefault(p => p != null && p.name.Contains(shape));
        PortalSurface[] panels = sector == null ? null : sector.GetComponentsInChildren<PortalSurface>(true);
        PortalSurface p1 = panels == null ? null : panels.FirstOrDefault(p => p.name == "S6_Panel_1");
        PortalSurface p2 = panels == null ? null : panels.FirstOrDefault(p => p.name == "S6_Panel_2");
        Rigidbody body = cube == null ? null : cube.GetComponent<Rigidbody>();
        bool ready = sector != null && sector.sectorId == 6 && body != null && !body.isKinematic &&
                     !cube.ExternallyDriven && !cube.InputLocked &&
                     p1 != null && p2 != null && SpacePortal.Orange == null && SpacePortal.Blue == null;
        record("S6/P1" + label + "/준비", ready, ready ? "P1·P2, " + shape + " 강체, 빈 포탈 슬롯" : "구성 또는 강체 상태 누락");
        if (!ready) yield break;

        Vector3 originalPosition = body.position, originalVelocity = body.velocity;
        Vector3 originalAngularVelocity = body.angularVelocity;
        bool originalControlled = cube.IsControlled;
        SpacePortal entrance = null, exit = null;
        Map4PortalVelocityFixture motor = null;
        try
        {
            // 별도 Master 세션의 합성 W 입력: 대상 도형을 실제 Tab 선택 상태에 둔다.
            // PlayerMover가 무입력 틱의 수평속도/각속도 처리에 계속 참여한다.
            cube.SetControlled(true);
            entrance = Place(p1, EnergyColor.Orange);
            exit = Place(p2, EnergyColor.Blue);
            yield return new WaitForFixedUpdate();
            bool paired = entrance != null && exit != null && entrance.Partner == exit;
            PlayerShapeIdentity identity = cube.GetComponent<PlayerShapeIdentity>();
            bool panelIgnored = identity != null && identity.solidCollider != null &&
                Physics.GetIgnoreCollision(p1.Box, identity.solidCollider);
            record("S6/P1" + label + "/설치", paired && panelIgnored,
                "공개 SpacePortal.PlaceAt으로 P1→P2 설치, P1패널↔실제도형 충돌무시=" + panelIgnored);
            if (!paired) yield break;
            Vector3 ground = south ? new Vector3(-10f, 0f, 19.7f) : new Vector3(-10f, 0f, 28.3f);
            Vector3 start = sector.transform.TransformPoint(ground + Vector3.up * 0.6f);
            Vector3 direction = sector.transform.TransformDirection(south ? Vector3.forward : Vector3.back).normalized;
            body.velocity = Vector3.zero;
            body.position = start;
            Physics.SyncTransforms();
            Vector3 previous = body.position;
            bool crossed = false, sawKinematic = body.isKinematic, sawDriven = cube.ExternallyDriven;
            bool prePushP2 = AtExit(body, exit, out _, out _);
            float crossZ = float.NaN;
            string timeline = $"fixture local{Local(sector, previous)} signed={Signed(entrance, previous):0.00}";
            for (int tick = 1; tick <= 30; tick++)
            {
                yield return new WaitForFixedUpdate();
                Vector3 now = body.position;
                ObserveCross(sector, entrance, previous, now, ref crossed, ref crossZ);
                if (!sawKinematic && body.isKinematic) { sawKinematic = true; timeline += $"; settle#{tick} kinematic"; }
                if (!sawDriven && cube.ExternallyDriven) { sawDriven = true; timeline += $"; settle#{tick} driven"; }
                if (!prePushP2 && AtExit(body, exit, out _, out _))
                { prePushP2 = true; timeline += $"; settle#{tick} P2도착"; }
                previous = now;
            }
            timeline += $"; settle끝 local{Local(sector, body.position)} signed={Signed(entrance, body.position):0.00}";
            bool pushed = false, reachedP2 = false;
            int ticks = 0;
            float minInsideSigned = float.PositiveInfinity, maxInsideSigned = float.NegativeInfinity;
            float firstInsideSigned = float.NaN, lastInsideSigned = float.NaN;
            float minInsideZ = float.PositiveInfinity, maxInsideZ = float.NegativeInfinity;
            bool ignoredAtEnd = false;
            float speed = cube.moveSpeed > 0.1f ? cube.moveSpeed : 5f;
            PlayerShapeController shapeController = cube.GetComponent<PlayerShapeController>();
            motor = cube.gameObject.AddComponent<Map4PortalVelocityFixture>();
            motor.Configure(body, cube, shapeController, sector.transform, direction, speed, brakeAtCenter);
            if (prePushP2) motor.Active = false;
            float began = Time.time;
            while (Time.time - began < 8f && ticks < 400)
            {
                yield return new WaitForFixedUpdate();
                ticks++;
                Vector3 now = body.position;
                Vector3 portalLocal = entrance.transform.InverseTransformPoint(now);
                if (Mathf.Abs(portalLocal.x) <= entrance.width * 0.5f + 0.01f &&
                    Mathf.Abs(portalLocal.y) <= entrance.height * 0.5f + 0.01f)
                {
                    float signed = portalLocal.z;
                    if (float.IsNaN(firstInsideSigned)) firstInsideSigned = signed;
                    lastInsideSigned = signed;
                    minInsideSigned = Mathf.Min(minInsideSigned, signed);
                    maxInsideSigned = Mathf.Max(maxInsideSigned, signed);
                    float z = Local(sector, now).z;
                    minInsideZ = Mathf.Min(minInsideZ, z);
                    maxInsideZ = Mathf.Max(maxInsideZ, z);
                }
                ObserveCross(sector, entrance, previous, now, ref crossed, ref crossZ);
                if (!sawKinematic && body.isKinematic) { sawKinematic = true; timeline += $"; push#{ticks} kinematic"; }
                if (!sawDriven && cube.ExternallyDriven) { sawDriven = true; timeline += $"; push#{ticks} driven"; }
                if (!reachedP2 && AtExit(body, exit, out _, out _))
                { reachedP2 = true; motor.Active = false; timeline += $"; push#{ticks} P2도착"; }
                previous = now;
                if (reachedP2 && ticks >= 20 && !body.isKinematic) break;
            }
            motor.Active = false;
            pushed = motor.DrivenTicks > 0;
            bool finalP2 = AtExit(body, exit, out float distance, out float side);
            ignoredAtEnd = identity != null && identity.solidCollider != null &&
                Physics.GetIgnoreCollision(p1.Box, identity.solidCollider);
            bool walked = pushed && !prePushP2 && reachedP2 && finalP2 &&
                          (!brakeAtCenter || motor.Released) && motor.MaxCommandedHorizontal <= speed + 0.05f;
            record("S6/P1" + label + "/실제도착", walked,
                $"{timeline}; 공개강체 velocity fixture(실제 키 입력 아님), 목표지상/공중속력={speed:0.0}/{speed * cube.airControlMultiplier:0.0}U/s, 공중구동={motor.AirTicks}Fixed, 강제수평최대={motor.MaxCommandedHorizontal:0.00}U/s, 중앙정지#{motor.ReleaseTick}@z{motor.ReleaseZ:0.00}(공중={motor.ReleaseAirborne}, 전 수평/각속력={motor.ReleaseHorizontalBefore:0.00}/{motor.ReleaseAngularBefore:0.00}), 물리구동={pushed}, 입구평면교차={crossed}, 교차z={crossZ:0.00}, " +
                $"포탈직사각형내 signed 처음/최소/최대/끝={firstInsideSigned:0.00}/{minInsideSigned:0.00}/{maxInsideSigned:0.00}/{lastInsideSigned:0.00}, z={minInsideZ:0.00}..{maxInsideZ:0.00}, " +
                $"P1패널충돌무시 시작/끝={panelIgnored}/{ignoredAtEnd}, kinematic={sawKinematic}, driven={sawDriven}, {ticks}Fixed 뒤 local{Local(sector, body.position)}, " +
                $"P2거리={distance:0.00}U, 출구앞쪽={side:0.00}U, 준비중P2={prePushP2}, 구동중P2={reachedP2}, 최종P2={finalP2}");
        }
        finally
        {
            if (motor != null) { motor.Active = false; UnityEngine.Object.Destroy(motor); }
            cube.SetControlled(originalControlled);
            body.velocity = originalVelocity;
            body.angularVelocity = originalAngularVelocity;
            body.position = originalPosition;
            Physics.SyncTransforms();
            if (entrance != null) UnityEngine.Object.Destroy(entrance.gameObject);
            if (exit != null) UnityEngine.Object.Destroy(exit.gameObject);
        }
    }

    private static SpacePortal Place(PortalSurface surface, EnergyColor color)
    {
        GameObject go = new GameObject("SpacePortal_" + color + "_P1EntryCheck");
        SpacePortal portal = go.AddComponent<SpacePortal>();
        portal.color = color;
        BoxCollider box = surface.Box;
        Vector3 center = surface.transform.TransformPoint(box.center + new Vector3(0f, 0f, box.size.z * 0.5f));
        portal.PlaceAt(center, surface.transform.forward, surface.transform.up, surface.transform);
        return portal;
    }

    private static Vector3 Local(SectorController sector, Vector3 world) => sector.transform.InverseTransformPoint(world);
    private static float Signed(SpacePortal portal, Vector3 world) =>
        Vector3.Dot(world - portal.transform.position, portal.transform.forward);

    private static bool AtExit(Rigidbody body, SpacePortal exit, out float distance, out float side)
    {
        distance = exit == null ? float.PositiveInfinity : Vector3.Distance(body.position, exit.transform.position);
        side = exit == null ? float.NegativeInfinity :
            Vector3.Dot(body.position - exit.transform.position, exit.transform.forward);
        return distance <= 6f && side >= -0.3f;
    }

    private static void ObserveCross(SectorController sector, SpacePortal entrance, Vector3 before, Vector3 after,
        ref bool crossed, ref float crossZ)
    {
        if (crossed || (after - before).magnitude >= 2.5f) return;
        if (Signed(entrance, before) >= 0f && Signed(entrance, after) < 0f)
        {
            crossed = true;
            crossZ = Local(sector, after).z;
        }
    }
}

/// <summary>검사 전용 합성 모터. 팀 PlayerMover 이후에만 공개 Rigidbody 수평 속도를 설정한다.</summary>
[DefaultExecutionOrder(10000)]
public sealed class Map4PortalVelocityFixture : MonoBehaviour
{
    private Rigidbody body;
    private PlayerMover mover;
    private PlayerShapeController shape;
    private Transform sector;
    private Vector3 direction;
    private float groundSpeed;
    private bool brakeAtCenter;
    public bool Active { get; set; }
    public bool Released { get; private set; }
    public int DrivenTicks { get; private set; }
    public int AirTicks { get; private set; }
    public int ReleaseTick { get; private set; } = -1;
    public float ReleaseZ { get; private set; } = float.NaN;
    public bool ReleaseAirborne { get; private set; }
    public float ReleaseHorizontalBefore { get; private set; } = float.NaN;
    public float ReleaseAngularBefore { get; private set; } = float.NaN;
    public float MaxCommandedHorizontal { get; private set; }

    public void Configure(Rigidbody rb, PlayerMover player, PlayerShapeController controller,
        Transform sectorRoot, Vector3 worldDirection, float speed, bool brake)
    {
        body = rb;
        mover = player;
        shape = controller;
        sector = sectorRoot;
        direction = new Vector3(worldDirection.x, 0f, worldDirection.z).normalized;
        groundSpeed = speed;
        brakeAtCenter = brake;
        Active = true;
    }

    private void FixedUpdate()
    {
        if (!Active || body == null || mover == null || body.isKinematic || mover.ExternallyDriven) return;
        bool grounded = shape != null && shape.IsGrounded();
        float localZ = sector.InverseTransformPoint(body.position).z;
        if (brakeAtCenter && Mathf.Abs(localZ - 24f) <= 0.1f)
        {
            Vector3 before = body.velocity;
            ReleaseTick = DrivenTicks;
            ReleaseZ = localZ;
            ReleaseAirborne = !grounded;
            ReleaseHorizontalBefore = new Vector2(before.x, before.z).magnitude;
            ReleaseAngularBefore = body.angularVelocity.magnitude;
            body.velocity = new Vector3(0f, before.y, 0f);
            Released = true;
            Active = false; // 다음 틱부터 팀 PlayerMover 무입력 처리만 적용한다.
            return;
        }
        float commanded = groundSpeed * (grounded ? 1f : mover.airControlMultiplier);
        Vector3 current = body.velocity;
        Vector3 horizontal = direction * commanded;
        body.velocity = new Vector3(horizontal.x, current.y, horizontal.z);
        MaxCommandedHorizontal = Mathf.Max(MaxCommandedHorizontal, horizontal.magnitude);
        if (!grounded) AirTicks++;
        DrivenTicks++;
    }
}
#endif
