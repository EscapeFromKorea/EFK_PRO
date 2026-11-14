#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 11-03 맵4 후속 변경 전용 PlayMode 점검. 도형 Root를 직접 옮겨 진입 구역을 시험하는
/// 기동 검사이며, 실제 입력·카메라·사람 플레이를 보증하지 않는다. 호출자는 로드 완료 뒤 Run을 구동한다.
/// record의 false에는 미실측/검사불가도 포함하므로 detail을 함께 읽어야 한다.
/// </summary>
public static class Map4FollowupS5S8Checks
{
    public static IEnumerator Run(SectorController sector, PlayerMover[] players, Action<string, bool, string> record)
    {
        if (record == null) yield break;
        if (sector == null) { record("FOLLOWUP.sector", false, "SectorController 없음"); yield break; }
        if (sector.sectorId == 5) yield return RunS5(sector, players, record);
        else if (sector.sectorId == 8) RunS8(sector, players, record);
        else record("FOLLOWUP.sector", false, "지원 섹터 5/8 아님: " + sector.sectorId);
    }

    private static IEnumerator RunS5(SectorController sector, PlayerMover[] players, Action<string, bool, string> record)
    {
        Transform[] children = sector.GetComponentsInChildren<Transform>(true);
        Transform bridge = Named(children, "GEO_S5_Bridge3");
        Transform l2 = Named(children, "GEO_S5_Floor_L2");
        Transform finish = Named(children, "GEO_S5_Floor_F");
        BoxCollider bridgeBox = bridge != null ? bridge.GetComponent<BoxCollider>() : null;
        BoxCollider l2Box = l2 != null ? l2.GetComponent<BoxCollider>() : null;
        BoxCollider finishBox = finish != null ? finish.GetComponent<BoxCollider>() : null;
        float bridgeLeft = bridgeBox != null ? FaceX(bridge, bridgeBox, sector.transform, false) : float.NaN;
        float bridgeRight = bridgeBox != null ? FaceX(bridge, bridgeBox, sector.transform, true) : float.NaN;
        float l2Right = l2Box != null ? FaceX(l2, l2Box, sector.transform, true) : float.NaN;
        float finishLeft = finishBox != null ? FaceX(finish, finishBox, sector.transform, false) : float.NaN;
        bool bridgeOk = bridgeBox != null && l2Box != null && finishBox != null &&
                        Mathf.Abs((bridgeRight - bridgeLeft) - 20f) < 0.05f &&
                        Mathf.Abs(bridgeLeft - l2Right) < 0.05f &&
                        Mathf.Abs(bridgeRight - finishLeft) < 0.05f;
        record("CH5.bridge20", bridgeOk,
            $"섹터 로컬 다리3 x {bridgeLeft:F2}~{bridgeRight:F2}, 길이 {bridgeRight - bridgeLeft:F2}; L2 끝 {l2Right:F2}, F 시작 {finishLeft:F2}");

        int roofCount = 0;
        for (int i = 1; i <= 3; i++)
        {
            Transform roof = Named(children, "GEO_S5_AxeCanopy_" + i);
            if (roof != null && roof.GetComponent<Collider>() != null) roofCount++;
        }
        record("CH5.canopies", roofCount == 3, $"도끼 천장 솔리드 {roofCount}/3; 낙하·카메라 간섭은 별도 실측 필요");

        string[] traps = { "S5_Axe_1", "S5_Spike_1", "S5_Axe_2", "S5_Spike_2", "S5_Axe_3" };
        Lab_FirstEntryTrap[] activators = new Lab_FirstEntryTrap[traps.Length];
        GameObject[] targets = new GameObject[traps.Length];
        Transform gimmickRoot = Named(children, "S5_Gimmicks");
        bool configured = true, initiallyOff = true;
        for (int i = 0; i < traps.Length; i++)
        {
            Transform target = Named(children, traps[i]);
            Transform zoneObject = Named(children, "S5_FirstEntryZone_" + traps[i]);
            targets[i] = target != null ? target.gameObject : null;
            activators[i] = zoneObject != null ? zoneObject.GetComponent<Lab_FirstEntryTrap>() : null;
            Lab_FirstEntryTrap a = activators[i];
            BoxCollider zone = zoneObject != null ? zoneObject.GetComponent<BoxCollider>() : null;
            bool one = target != null && a != null && zone != null && zone.isTrigger &&
                       zoneObject.GetComponent<Renderer>() == null && a.target == target.gameObject &&
                       !a.Activated && a.enabled &&
                       gimmickRoot != null && !zoneObject.IsChildOf(gimmickRoot) &&
                       (i % 2 == 0 ? target.GetComponent<AxeTrap>() != null : target.GetComponent<SpikeTrap>() != null);
            configured &= one;
            initiallyOff &= target != null && !target.gameObject.activeSelf;
        }
        record("CH5.transparentZones", configured, $"렌더러 없는 트리거/1:1 대상 구성 {CountConfigured(activators, targets)}/5; 접촉식 압력판은 아님");
        record("CH5.initiallyHidden", initiallyOff, $"도끼3·가시2 시작 비활성 {(initiallyOff ? "5/5" : "불일치")}");

        PlayerMover player = FirstBody(players);
        Rigidbody body = player != null ? player.GetComponent<Rigidbody>() : null;
        if (!configured || !initiallyOff || body == null || !body.useGravity)
        {
            record("CH5.entryActivation", false, "구성/초기 비활성/배치 완료된 PlayerMover 중 하나가 없어 기동 시험 불가");
            yield break;
        }

        Vector3 original = body.position, oldVelocity = body.velocity, oldAngular = body.angularVelocity;
        try
        {
            // 가시를 먼저 시험하면 먼저 켠 도끼의 물리 날이 다음 구역 접근을 막는 교란을 피할 수 있다.
            int[] order = { 1, 3, 0, 2, 4 };
            bool[] visited = new bool[traps.Length];
            foreach (int i in order)
            {
                bool othersOff = true;
                for (int j = 0; j < targets.Length; j++)
                    if (!visited[j] && j != i) othersOff &= !targets[j].activeSelf;
                record("CH5.otherHidden." + traps[i], othersOff, "아직 시험하지 않은 다른 도끼·가시가 비활성인지 확인");

                BoxCollider zone = activators[i].GetComponent<BoxCollider>();
                Vector3 dir = zone.transform.forward;
                Vector3 start = zone.transform.TransformPoint(zone.center - Vector3.forward * (zone.size.z * 0.5f + 1f));
                Put(body, start); // 구역 밖에서 출발; 이벤트를 직접 부르지 않는다.
                yield return new WaitForFixedUpdate();
                for (int step = 0; step < 30 && !activators[i].Activated; step++)
                {
                    Put(body, body.position + dir * 0.14f); // fixedDeltaTime 0.02 기준 7 U/s 상당
                    yield return new WaitForFixedUpdate();
                }
                yield return new WaitForFixedUpdate(); // 마지막 물리 Trigger 처리 확인
                bool nowActive = targets[i].activeSelf && activators[i].Activated && !activators[i].enabled;
                record("CH5.entryActivation." + traps[i], nowActive,
                    $"구역 밖에서 고정 스텝당 0.14U(현재 {0.14f / Time.fixedDeltaTime:F1}U/s 상당) 연속 이동; 대상 activeSelf={targets[i].activeSelf}, Activated={activators[i].Activated}, 활성기 enabled={activators[i].enabled}, Root 최종 위치={body.position}. 콜라이더 전방 접촉으로 Root가 구역에 들어오기 전에 발동할 수 있음");
                visited[i] = true;
            }

            // 진입 후에도 팀 함정의 기존 주기 운동이 진행되는지 공개 Transform으로 관찰한다.
            Put(body, original);
            AxeTrap axe = targets[0].GetComponent<AxeTrap>();
            SpikeTrap spike = targets[1].GetComponent<SpikeTrap>();
            Quaternion firstRot = axe.transform.rotation;
            Vector3 firstPos = spike.transform.position;
            float maxAngle = 0f, maxRise = 0f;
            for (int step = 0; step < 180; step++)
            {
                yield return new WaitForFixedUpdate();
                maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(firstRot, axe.transform.rotation));
                maxRise = Mathf.Max(maxRise, spike.transform.position.y - firstPos.y);
            }
            record("CH5.periodicMotion", maxAngle > 1f && maxRise > 0.05f,
                $"첫 활성 뒤 180 물리 스텝 최대 도끼 회전 {maxAngle:F1}°, 가시 상승 {maxRise:F2}U");

            SectionHitCounter counter = FindCounter(sector);
            bool destinationOk = counter != null && counter.destination != null &&
                                 counter.destination.sectionId == "CH5_START" && counter.hitsBeforeRespawn == 1;
            bool wiringOk = destinationOk;
            for (int i = 0; i < targets.Length; i++)
            {
                PeriodicTrapBase trap = targets[i].GetComponent<PeriodicTrapBase>();
                bool linked = false;
                if (trap != null && trap.OnHazardHit != null)
                    for (int k = 0; k < trap.OnHazardHit.GetPersistentEventCount(); k++)
                        linked |= trap.OnHazardHit.GetPersistentTarget(k) == counter &&
                                  trap.OnHazardHit.GetPersistentMethodName(k) == "RegisterHitEvent";
                wiringOk &= linked;
            }
            record("CH5.respawnWiring", wiringOk, $"카운터 목적지={((counter != null && counter.destination != null) ? counter.destination.sectionId : "없음")}, 임계={(counter != null ? counter.hitsBeforeRespawn : -1)}, 도끼·가시 5개 영구 배선");

            // 실제 피격은 앞 구역을 이미 통과한 뒤 가시 자리로 옮겨 관찰한다. 충돌이 재현되지 않으면 실패/미실측으로 기록한다.
            bool hit = false, threshold = false;
            SectionSafePoint eventDestination = null;
            if (spike.OnHazardHit == null || counter == null || counter.OnThresholdReached == null)
            {
                record("CH5.spikeHitAfterEntry", false, "피격 또는 복귀 UnityEvent가 없어 동적 검사를 수행할 수 없음");
                yield break;
            }
            UnityAction<GameObject> onHit = root => { if (root == player.gameObject) hit = true; };
            UnityAction<GameObject, SectionSafePoint> onThreshold = (root, dest) =>
            { if (root == player.gameObject) { threshold = true; eventDestination = dest; } };
            spike.OnHazardHit.AddListener(onHit);
            counter.OnThresholdReached.AddListener(onThreshold);
            try
            {
                Put(body, spike.transform.position + Vector3.up * (spike.popDistance + 0.35f));
                for (int step = 0; step < 350 && !threshold; step++) yield return new WaitForFixedUpdate();
                record("CH5.spikeHitAfterEntry", hit && threshold && eventDestination == counter.destination,
                    $"진입 후 가시 위치 기동 시험: OnHazardHit={hit}, 카운터 임계={threshold}, 목적지={(eventDestination != null ? eventDestination.sectionId : "없음")}; 실제 입력 플레이 아님");
            }
            finally
            {
                spike.OnHazardHit.RemoveListener(onHit);
                counter.OnThresholdReached.RemoveListener(onThreshold);
            }
        }
        finally { Put(body, original); body.velocity = oldVelocity; body.angularVelocity = oldAngular; }
    }

    private static void RunS8(SectorController sector, PlayerMover[] players, Action<string, bool, string> record)
    {
        ManagerChapterController chapter = sector.GetComponentInChildren<ManagerChapterController>(true);
        ManagerAgent manager = chapter != null ? chapter.managerAgent : null;
        bool tuning = manager != null && Mathf.Abs(manager.detectDistance - 12f) < 0.01f &&
                      Mathf.Abs(manager.fieldOfView - 90f) < 0.01f;
        record("CH8.trialDetection", tuning, manager == null ? "관리자 없음" :
            $"감지거리 {manager.detectDistance:F1}U(검증용 후보), 시야각 {manager.fieldOfView:F1}°; 실제 난이도 미확정");

        Transform[] route = manager != null && manager.agent != null ? manager.agent.waypoints : null;
        ManagerCctvPanel panel = sector.GetComponentInChildren<ManagerCctvPanel>(true);
        float consoleDistance = panel != null ? RouteDistance(panel.transform.position, route) : float.NaN;
        bool consoleSafe = !float.IsNaN(consoleDistance) && manager != null && consoleDistance >= manager.detectDistance;
        record("CH8.consoleDistance", consoleSafe,
            $"CCTV 콘솔 ↔ 순찰선 수평거리 {consoleDistance:F2}U, 감지거리 {(manager != null ? manager.detectDistance : float.NaN):F2}U; 시야/차폐는 별도");

        bool startsSafe = chapter != null && chapter.startPoints != null && chapter.startPoints.Length == 3;
        float nearestStart = float.PositiveInfinity;
        if (startsSafe)
            foreach (SectionSafePoint point in chapter.startPoints)
            {
                if (point == null) { startsSafe = false; break; }
                nearestStart = Mathf.Min(nearestStart, RouteDistance(point.transform.position, route));
            }
        startsSafe &= manager != null && nearestStart >= manager.detectDistance;
        record("CH8.startDistance", startsSafe,
            $"시작점 3개 중 순찰선까지 최소 {nearestStart:F2}U, 감지거리 {(manager != null ? manager.detectDistance : float.NaN):F2}U; 복귀 직후 탐지는 별도");

        // 팀 공개 CanSee를 같은 관리자 시선에서 앞쪽 통로(양성)·책장 뒤(음성)에 질의한다.
        // 이는 위치를 직접 놓는 Linecast 기동 시험이며 실제 순찰 중 인지/추격/난이도 판정은 아니다.
        PlayerMover player = FirstBody(players);
        PlayerShapeIdentity identity = player != null ? player.GetComponent<PlayerShapeIdentity>() : null;
        Rigidbody body = player != null ? player.GetComponent<Rigidbody>() : null;
        Transform[] waypoints = manager != null && manager.agent != null ? manager.agent.waypoints : null;
        if (body == null || identity == null || waypoints == null || waypoints.Length < 2 ||
            waypoints[0] == null || waypoints[1] == null || !ManagerAgent.IsAlive(player)) return;

        Vector3 forward = waypoints[1].position - waypoints[0].position;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f || Vector3.Angle(manager.Facing, forward) > 5f) return;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 observer = waypoints[0].position;
        Vector3 oldPosition = body.position, oldVelocity = body.velocity, oldAngular = body.angularVelocity;
        try
        {
            Put(body, observer + forward * 6f + Vector3.up * 0.5f); // 서쪽 외곽 통로, 책장 밖
            bool visibleInAisle = manager.CanSee(identity, observer, out Vector3 clearCenter, out float clearDistance);
            Put(body, observer + forward * 6f + right * 5f + Vector3.up * 0.5f); // 첫 서쪽 책장 뒤
            bool visibleBehindShelf = manager.CanSee(identity, observer, out Vector3 hiddenCenter, out float hiddenDistance);
            record("CH8.bookshelfLinecast", visibleInAisle && !visibleBehindShelf,
                $"공개 CanSee: 통로 거리 {clearDistance:F2}U/보임 {visibleInAisle}, 책장 뒤 거리 {hiddenDistance:F2}U/보임 {visibleBehindShelf}; 같은 시작 시선, Root 직접 배치 기동 시험");
        }
        finally { Put(body, oldPosition); body.velocity = oldVelocity; body.angularVelocity = oldAngular; }
    }

    private static PlayerMover FirstBody(PlayerMover[] players)
    {
        if (players == null) return null;
        foreach (PlayerMover p in players) if (p != null && p.GetComponent<Rigidbody>() != null) return p;
        return null;
    }

    private static int CountConfigured(Lab_FirstEntryTrap[] a, GameObject[] targets)
    {
        int count = 0;
        for (int i = 0; i < a.Length; i++) if (a[i] != null && targets[i] != null) count++;
        return count;
    }

    private static Transform Named(Transform[] children, string name)
    {
        foreach (Transform t in children) if (t != null && t.name == name) return t;
        return null;
    }

    private static SectionHitCounter FindCounter(SectorController sector)
    {
        foreach (SectionHitCounter c in sector.GetComponentsInChildren<SectionHitCounter>(true))
            if (c.destination != null && c.destination.sectionId == "CH5_START") return c;
        return null;
    }

    private static void Put(Rigidbody body, Vector3 position)
    {
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = position;
        Physics.SyncTransforms();
    }

    private static float RouteDistance(Vector3 position, Transform[] route)
    {
        if (route == null || route.Length < 2) return float.NaN;
        float best = float.PositiveInfinity;
        for (int i = 1; i < route.Length; i++)
        {
            if (route[i - 1] == null || route[i] == null) return float.NaN;
            Vector2 a = new Vector2(route[i - 1].position.x, route[i - 1].position.z);
            Vector2 b = new Vector2(route[i].position.x, route[i].position.z);
            Vector2 p = new Vector2(position.x, position.z);
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }

    private static float FaceX(Transform box, BoxCollider collider, Transform sector, bool right)
    {
        Vector3 local = collider.center + Vector3.right * (collider.size.x * (right ? 0.5f : -0.5f));
        return sector.InverseTransformPoint(box.TransformPoint(local)).x;
    }
}
#endif
