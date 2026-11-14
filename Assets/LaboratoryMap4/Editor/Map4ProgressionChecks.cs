#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

/// <summary>
/// 맵4 구간 진행 검사. 각 챕터 퍼즐을 완료했다고 가정하지 않는다.
/// 이음매는 지형 Raycast, 이동은 문 뒤 연결 통로에 배치한 실제 도형을 ExternallyDriven으로
/// 하나씩 전진시키는 합성 물리 입력이다. 정답 입력·Tab 키·전체 동선 완주 시험이 아니다.
/// </summary>
public static class Map4ProgressionChecks
{
    private const float FloorTolerance = 0.12f;
    private const float LaneTolerance = 0.8f;
    private const float CpHorizontalTolerance = 0.6f;

    public static IEnumerator Run(SectorController[] sectors, PlayerMover[] players,
        Action<string, bool, string> record)
    {
        if (record == null) yield break;
        bool setup = sectors != null && sectors.Length >= 8 && players != null && players.Length == 3 &&
                     players.All(p => p != null && p.GetComponent<Rigidbody>() != null);
        record("진행/준비", setup, setup ? "8섹터·실제 도형3개" : "섹터 또는 도형/강체 부족");
        if (!setup) yield break;

        SectorController[] byId = new SectorController[9];
        foreach (SectorController sector in sectors)
            if (sector != null && sector.sectorId >= 1 && sector.sectorId <= 8) byId[sector.sectorId] = sector;
        for (int id = 1; id <= 8; id++)
            if (byId[id] == null) { record("진행/섹터등록", false, $"섹터 {id} 누락"); yield break; }
        record("진행/섹터등록", true, "CH1~8 모두 등록됨");

        RespawnController respawn = UnityEngine.Object.FindObjectOfType<RespawnController>();
        RespawnZone cp1 = byId[1].GetComponentInChildren<RespawnZone>(true);
        if (respawn != null && cp1 != null) RespawnController.SetCheckpoint(cp1); // 시험 초기 조건
        record("진행/CP초기조건", respawn != null && cp1 != null,
            "공용 CP1을 공개 API로 고정한 합성 시험 초기 조건");

        // CH2 성공 분기는 Map4FollowupS2Checks의 S4첫통과3인/CH4체크포인트복귀가 검증한다.
        Lab_IsolationFlow s2Flow = byId[2].GetComponentInChildren<Lab_IsolationFlow>(true);
        record("진행/CH2성공분기구조", s2Flow != null && s2Flow.moveWholeTeam,
            "성공 S4문 뒤 감지·3도형 CH4 이동 구조. 행동 증거: S2/S4첫통과3인");
        record("진행/CH2실패분기구조",
            byId[2].GetComponentsInChildren<doorPhysics>(true).Any(d => d.name == "DOOR_S2_ExitS3"),
            "실패 S3문 구조. 행동 증거: S2/시간초과복구");

        for (int id = 1; id <= 7; id++)
        {
            SectorController from = byId[id], to = byId[id + 1];
            float floorY = to.transform.position.y;
            float seamZ = to.transform.position.z;
            float gap = seamZ - (from.transform.position.z + from.length);
            bool seam = from.exit != null && to.entrance != null && Mathf.Abs(gap - 12f) <= 0.03f &&
                        Mathf.Abs(from.exit.position.y - (floorY + 0.1f)) <= 0.2f &&
                        Mathf.Abs(from.exit.position.z - (from.transform.position.z + from.length)) <= 0.2f &&
                        Mathf.Abs(to.entrance.position.y - (floorY + 0.1f)) <= 0.2f &&
                        Mathf.Abs(to.entrance.position.z - seamZ) <= 0.2f;
            record($"진행/{id}→{id + 1}/원점이음매", seam,
                $"간격={gap:0.###}U, 출구Y={(from.exit != null ? from.exit.position.y.ToString("0.###") : "없음")}, 다음바닥Y={floorY:0.###} [정적마커]");

            bool floor = true;
            foreach (float laneX in new[] { -2f, 0f, 2f })
                foreach (float zOffset in new[] { -11.5f, -6f, 0.5f })
                    floor &= HasFloor(new Vector3(to.transform.position.x + laneX, floorY, seamZ + zOffset));
            record($"진행/{id}→{id + 1}/바닥", floor,
                "x=-2/0/2, 연결 통로 앞·중·다음 입구 9점 Raycast [정적 충돌 지형]");

            doorPhysics gate = ExitGate(from, id);
            bool gateWasClosed = gate != null && gate.IsAtClosedPosition;
            record($"진행/{id}→{id + 1}/관문범위", true,
                gate == null ? "별도 출구문 없음" :
                $"{gate.name} {(gateWasClosed ? "닫힘" : "열림/이동중")} — 문 뒤 통로에서만 합성 이동; 관문 퍼즐은 각 focused 검사");

            // 문 뒤 0.5U부터 다음 섹터 진입부까지 실제 Rigidbody를 한 도형씩 진행시킨다.
            // 원위치 배치는 fixture, 전진 자체는 MovePosition/텔레포트가 아닌 물리 velocity다.
            float endZ = seamZ + (id + 1 == 6 ? 8f : 3f); // S6 시작 CP는 z5~11
            for (int i = 0; i < 3; i++)
            {
                PlayerMover player = players[i];
                Rigidbody rb = player.GetComponent<Rigidbody>();
                float footOffset = PivotToFoot(player);
                Vector3 start = new Vector3(to.transform.position.x + (i - 1) * 2f,
                    floorY + footOffset + 0.12f, seamZ - 11.5f);
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = start;
                rb.transform.position = start;
            }
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            bool allCrossed = true;
            for (int i = 0; i < 3; i++)
            {
                PlayerMover player = players[i];
                Rigidbody rb = player.GetComponent<Rigidbody>();
                bool priorDriven = player.ExternallyDriven;
                bool priorGravity = rb.useGravity;
                bool priorKinematic = rb.isKinematic;
                if (priorKinematic)
                {
                    allCrossed = false;
                    record($"진행/{id}→{id + 1}/도형{i + 1}", false,
                        "강체가 다른 기믹에 붙잡혀 isKinematic. 합성 모터를 적용하지 않음");
                    continue;
                }
                player.ExternallyDriven = true; // PlayerMover의 입력·감쇠 간섭만 중지, 도형 Rigidbody/콜라이더는 그대로
                float speed = Mathf.Max(0.1f, player.moveSpeed); // 도형별 이동 속도 통계 보존
                bool reached = false;
                for (int step = 0; step < 450; step++)
                {
                    rb.velocity = new Vector3(0f, rb.velocity.y, speed);
                    yield return new WaitForFixedUpdate();
                    if (rb.position.z >= endZ - 0.25f) { reached = true; break; }
                }
                rb.velocity = Vector3.zero;
                player.ExternallyDriven = priorDriven;
                rb.useGravity = priorGravity;
                Vector3 end = rb.position;
                float lane = Mathf.Abs(end.x - (to.transform.position.x + (i - 1) * 2f));
                bool ok = reached && lane <= LaneTolerance && end.y >= floorY - 0.5f;
                allCrossed &= ok;
                record($"진행/{id}→{id + 1}/도형{i + 1}", ok,
                    $"{player.name}: z={end.z - seamZ:0.##}, x이탈={lane:0.##}, y={end.y:0.##}, 속도={speed:0.##} [공개 ExternallyDriven+물리 velocity]");
            }
            record($"진행/{id}→{id + 1}/Tab순차합류", allCrossed,
                "세 도형을 각자 한 번씩 합성 전진. 실제 Tab 키 입력/퍼즐 완료 아님");

            if (gate != null)
                record($"진행/{id}→{id + 1}/관문간섭", gate.IsAtClosedPosition == gateWasClosed,
                    "통로 검사 중 출구문을 직접 열거나 이벤트를 호출하지 않았음");

            RespawnZone expected = ExpectedStartCheckpoint(to, id + 1);
            if (id + 1 == 2)
            {
                record("진행/CH2_CP이월", to.GetComponentsInChildren<RespawnZone>(true).Length == 0,
                    "CH2 자체 CP 없음, CH1 CP 이월. 성공 분기 CP4 전진은 S2 focused 검사");
            }
            else if (expected == null)
                record($"진행/CH{id + 1}_CP", false, "시작 RespawnZone 없음");
            else if (respawn != null && allCrossed)
                yield return CheckCheckpoint(to, players[1], expected, respawn, record);
        }

        record("진행/완주범위", true,
            "연결 통로 7개·순차 도형·CP 경계의 합성 검사까지. CH1~8 퍼즐 실제 입력/전체 동선 완주는 미검증");
    }

    private static IEnumerator CheckCheckpoint(SectorController sector, PlayerMover victim,
        RespawnZone expected, RespawnController respawn, Action<string, bool, string> record)
    {
        int id = sector.sectorId;
        yield return new WaitForFixedUpdate(); // 진입으로 RespawnZone.OnTriggerEnter 처리
        Vector3 ground = expected.FindGroundPoint();
        Rigidbody rb = victim.GetComponent<Rigidbody>();
        Vector3 away = sector.transform.TransformPoint(new Vector3(0f, 1f, -6f));
        rb.velocity = Vector3.zero;
        rb.position = away;
        rb.transform.position = away;
        Physics.SyncTransforms();
        respawn.RespawnPlayer(victim.gameObject); // 현재 공유 CP로 복귀하는 공개 API
        bool returned = false;
        float horizontal = float.PositiveInfinity;
        for (int step = 0; step < 350; step++)
        {
            yield return new WaitForFixedUpdate();
            Vector3 p = victim.transform.position;
            horizontal = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(ground.x, ground.z));
            if (horizontal <= CpHorizontalTolerance) { returned = true; break; }
        }
        record($"진행/CH{id}_CP실복귀", returned,
            $"{expected.name}, 수평거리={horizontal:0.###}U [실제 RespawnPlayer 행동; 트리거 진입 후 호출]");
        if (returned) yield return new WaitForSeconds(0.8f); // 페이드 종료 후 다음 구간 fixture 배치
    }

    private static RespawnZone ExpectedStartCheckpoint(SectorController sector, int id)
    {
        string name = id == 3 ? "CP_S3_Start" : id == 4 ? "CP_S4_Start" :
                      id == 5 ? "S5_Checkpoint_A" : id == 6 ? "S6_Zone_Start" :
                      id == 7 ? "S7_Zone_Check" : id == 8 ? "S8_Zone_Check" : null;
        return name == null ? null : sector.GetComponentsInChildren<RespawnZone>(true)
            .FirstOrDefault(zone => zone.name == name);
    }

    private static doorPhysics ExitGate(SectorController sector, int id)
    {
        string name = id == 1 ? "S1_ExitDoor" : id == 2 ? "DOOR_S2_ExitS3" :
                      id == 7 ? "GEO_S7_Door_Exit" : null;
        return name == null ? null : sector.GetComponentsInChildren<doorPhysics>(true)
            .FirstOrDefault(door => door.name == name);
    }

    private static bool HasFloor(Vector3 point)
    {
        RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * 2f, Vector3.down, 4f,
            ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.GetComponentInParent<PlayerMover>() != null) continue;
            if (hit.collider.GetComponentInParent<doorPhysics>() != null) continue;
            if (Mathf.Abs(hit.point.y - point.y) <= FloorTolerance) return true;
        }
        return false;
    }

    private static float PivotToFoot(PlayerMover player)
    {
        float bottom = float.PositiveInfinity;
        foreach (Collider collider in player.GetComponentsInChildren<Collider>())
            if (collider.enabled && !collider.isTrigger) bottom = Mathf.Min(bottom, collider.bounds.min.y);
        return float.IsPositiveInfinity(bottom) ? 0.5f : Mathf.Max(0.2f, player.transform.position.y - bottom);
    }
}
#endif
