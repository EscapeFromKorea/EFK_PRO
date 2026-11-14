#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Master+8섹터 PlayMode 전용 CH8 흐름 검사. 부모 하네스가 세 도형을 전달한다.
/// 순간이동과 공개 역할/혼합/사용 API를 쓰는 자동 검사이며 사람의 은신 동선 난도와 구분한다.
/// 감시 생략, 강제 수면/열쇠 노출, 이벤트 직접 발신, private 상태 변경은 하지 않는다.
/// </summary>
public static class Map4Ch8FlowChecks
{
    /// <summary>새 PlayMode 세션 전용 난도 계측. 공개 역할 API로 자리만 점유하고 실제 FixedUpdate 감시를 기다린다.</summary>
    public static IEnumerator RunPressure(SectorController sector, PlayerMover[] players, Action<string, bool, string> record)
    {
        if (record == null) yield break;
        if (sector == null || sector.sectorId != 8 || players == null || players.Length != 3 ||
            players[0] == null || players[1] == null || players[2] == null)
        { record("CH8-P01", false, "S8과 세 도형이 필요하다."); yield break; }

        ManagerChapterController chapter = sector.GetComponentInChildren<ManagerChapterController>(true);
        ManagerAgent manager = sector.GetComponentInChildren<ManagerAgent>(true);
        RoleAssignmentManager roles = sector.GetComponentInChildren<RoleAssignmentManager>(true);
        BookPanel book = sector.GetComponentInChildren<BookPanel>(true);
        MixingStation mixer = sector.GetComponentInChildren<MixingStation>(true);
        ManagerCctvPanel cctv = sector.GetComponentInChildren<ManagerCctvPanel>(true);
        if (chapter == null || manager == null || roles == null || book == null || mixer == null ||
            cctv == null || book.slot == null || mixer.slot == null || cctv.slot == null ||
            chapter.Current != ManagerChapterController.State.Ready || chapter.SkipSurveillance ||
            chapter.startPoints == null || chapter.startPoints.Length < 3 || manager.agent == null)
        {
            record("CH8-P01", false, "새 Ready 세션·감시 ON·Book/Mixer/CCTV·시작점/순찰 참조가 필요하다.");
            yield break;
        }

        float minStart = float.PositiveInfinity;
        foreach (SectionSafePoint point in chapter.startPoints)
        {
            if (point == null) { record("CH8-P01", false, "CH8 시작점 null."); yield break; }
            minStart = Mathf.Min(minStart, NearestRouteDistanceXZ(point.transform.position, manager.agent.waypoints));
        }
        float console = NearestRouteDistanceXZ(cctv.slot.transform.position, manager.agent.waypoints);
        bool safe = minStart >= manager.detectDistance && console >= manager.detectDistance;
        record("CH8-P01", safe,
            $"정적 순찰로 최근접: 시작점 {minStart:0.##}U, CCTV {console:0.##}U, 감지 D={manager.detectDistance:0.##}U. " +
            "직접 시야/사람 회피 난도는 별도.");

        RoleSlot[] slots = { book.slot, mixer.slot, cctv.slot };
        for (int i = 0; i < 3; i++)
            Teleport(players[i], FixturePosition(slots[i]));
        for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
        bool grounded = true;
        for (int i = 0; i < 3; i++) grounded &= AtFixtureFloor(players[i], slots[i]);
        for (int i = 0; i < 3; i++)
        {
            slots[i].HandleInteract(players[i]);
            roles.ClosePanelsOf(players[i]); // Tab의 화면 닫힘과 같은 공개 API; 점유는 유지
        }
        bool assigned = grounded && book.slot.CurrentOwner == players[0] && mixer.slot.CurrentOwner == players[1] &&
                        cctv.slot.CurrentOwner == players[2] &&
                        !book.slot.IsPanelOpen && !mixer.slot.IsPanelOpen && !cctv.slot.IsPanelOpen;
        record("CH8-P02", assigned,
            $"세 도형을 Book/Mixer/CCTV 앞 1U로 배치·착지={grounded}, 공개 HandleInteract 점유, ClosePanelsOf로 화면만 닫음. " +
            "입력·Tab 동선 대신 API/고정 위치로 압박 시각 측정.");
        if (!assigned) yield break;

        for (int i = 0; i < 40 && chapter.Current != ManagerChapterController.State.Running; i++)
            yield return new WaitForFixedUpdate();
        if (chapter.Current != ManagerChapterController.State.Running || !manager.Surveillance)
        { record("CH8-P03", false, "자동 시작 또는 감시 활성화 실패."); yield break; }

        float startedAt = Time.time;
        ManagerAgent.State firstState = manager.Current;
        for (int i = 0; i < 2250 && Time.time - startedAt <= 45f &&
                        manager.Current == ManagerAgent.State.Patrol; i++)
            yield return new WaitForFixedUpdate();
        float elapsed = Time.time - startedAt;
        firstState = manager.Current;
        PlayerShapeIdentity target = manager.Target;
        bool roleTarget = target != null &&
                          (target.GetComponent<PlayerMover>() == players[0] || target.GetComponent<PlayerMover>() == players[1]);
        bool pressured = (firstState == ManagerAgent.State.Suspect || firstState == ManagerAgent.State.Chase) &&
                         roleTarget && elapsed <= 45f && chapter.Current == ManagerChapterController.State.Running;
        Vector3 pos = sector.transform.InverseTransformPoint(manager.transform.position);
        record("CH8-P03", pressured,
            $"감시 실제 Time.time 경과 {elapsed:0.##}초(상한45), 첫 상태={firstState}, " +
            $"표적={(target != null ? target.Kind.ToString() : "없음")}, 관리자 섹터 위치={pos}. " +
            "책/Mixer 고정 역할 압박 계측이며 사람 조작 난도 아님.");
    }

    private static float NearestRouteDistanceXZ(Vector3 point, Transform[] waypoints)
    {
        if (waypoints == null || waypoints.Length < 2) return float.PositiveInfinity;
        Vector2 p = new Vector2(point.x, point.z);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i + 1 < waypoints.Length; i++)
        {
            if (waypoints[i] == null || waypoints[i + 1] == null) continue;
            Vector2 a = new Vector2(waypoints[i].position.x, waypoints[i].position.z);
            Vector2 b = new Vector2(waypoints[i + 1].position.x, waypoints[i + 1].position.z);
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            nearest = Mathf.Min(nearest, Vector2.Distance(p, a + t * ab));
        }
        return nearest;
    }

    private static Vector3 FixturePosition(RoleSlot slot) =>
        slot.transform.position + Vector3.ProjectOnPlane(slot.transform.forward, Vector3.up).normalized * 1f +
        Vector3.up * 0.8f;

    private static bool AtFixtureFloor(PlayerMover player, RoleSlot slot)
    {
        PlayerShapeIdentity id = player != null ? player.GetComponent<PlayerShapeIdentity>() : null;
        Collider solid = id != null ? id.solidCollider : null;
        return solid != null && Mathf.Abs(solid.bounds.min.y - slot.transform.position.y) <= 0.15f &&
               Vector3.Distance(Vector3.ProjectOnPlane(player.transform.position - slot.transform.position, Vector3.up),
                                Vector3.ProjectOnPlane(slot.transform.forward, Vector3.up).normalized) <= 0.2f;
    }

    // 기본 완주 검사는 피벗이 바닥 아래에 있는 세모를 선두로 삼는다.
    public static IEnumerator Run(SectorController sector, PlayerMover[] players, Action<string, bool, string> record) =>
        RunLead(sector, players, record, (int)PlayerShapeStats.ShapeKind.Tetrahedron);

    /// <summary>shapeIndex: ShapeKind enum 값(구0·네모1·세모2). 선두 도형별 시험은 각각 새 세션에서 실행한다.</summary>
    public static IEnumerator RunLead(SectorController sector, PlayerMover[] players,
        Action<string, bool, string> record, int shapeIndex)
    {
        if (record == null) yield break;
        if (shapeIndex < (int)PlayerShapeStats.ShapeKind.Sphere ||
            shapeIndex > (int)PlayerShapeStats.ShapeKind.Tetrahedron)
        { record("CH8-01", false, $"지원하지 않는 선두 도형 ShapeKind={shapeIndex}."); yield break; }
        if (sector == null || sector.sectorId != 8 || players == null || players.Length != 3 ||
            players[0] == null || players[1] == null || players[2] == null ||
            players[0] == players[1] || players[0] == players[2] || players[1] == players[2])
        {
            record("CH8-01", false, "S8 SectorController와 서로 다른 PlayerMover 3개가 필요하다.");
            yield break;
        }

        ManagerChapterController chapter = sector.GetComponentInChildren<ManagerChapterController>(true);
        ManagerAgent manager = sector.GetComponentInChildren<ManagerAgent>(true);
        RoleAssignmentManager roles = sector.GetComponentInChildren<RoleAssignmentManager>(true);
        MixingStation station = sector.GetComponentInChildren<MixingStation>(true);
        BookPanel book = sector.GetComponentInChildren<BookPanel>(true);
        ManagerCctvPanel cctv = sector.GetComponentInChildren<ManagerCctvPanel>(true);
        ManagerUsePoint use = sector.GetComponentInChildren<ManagerUsePoint>(true);
        ManagerKeyPoint key = sector.GetComponentInChildren<ManagerKeyPoint>(true);
        ManagerKeyDoor keyDoor = sector.GetComponentInChildren<ManagerKeyDoor>(true);
        ManagerCatchZone catchZone = sector.GetComponentInChildren<ManagerCatchZone>(true);
        Lab_FinalExitFlow finalFlow = sector.GetComponentInChildren<Lab_FinalExitFlow>(true);
        if (chapter == null || manager == null || roles == null || station == null || book == null ||
            cctv == null || use == null || key == null || keyDoor == null || catchZone == null || finalFlow == null ||
            station.slot == null || book.slot == null || cctv.slot == null || keyDoor.door == null ||
            finalFlow.exitFloor == null || finalFlow.finalCheckpoint == null ||
            finalFlow.finalSlots == null || finalFlow.finalSlots.Length != 3 ||
            chapter.startPoints == null || chapter.startPoints.Length < 3 || manager.agent == null)
        {
            record("CH8-01", false, "감시·책/Mixer/CCTV·완성물 사용·열쇠/문·시작점의 필수 참조가 비었다.");
            yield break;
        }

        bool configured = !chapter.SkipSurveillance && Mathf.Abs(manager.detectDistance - 12f) < 0.05f &&
                          Mathf.Abs(manager.fieldOfView - 90f) < 0.05f && manager.agent.waypoints != null &&
                          manager.agent.waypoints.Length >= 3 && cctv.cameras != null && cctv.cameras.Length == 3;
        record("CH8-01", configured,
            $"감시={(!chapter.SkipSurveillance)}, D={manager.detectDistance:0.##}, FOV={manager.fieldOfView:0.##}, " +
            $"순찰점={manager.agent.waypoints?.Length ?? 0}, CCTV={cctv.cameras?.Length ?? 0}. 현재 후보 12U/90° 검증.");
        if (!configured) yield break;

        bool route = CheckCentralFirstRoute(sector, manager, out string routeDetail);
        record("CH8-01R", route, routeDetail);

        // 실제 자동 시작 구역에 세 도형을 배치한다. RequestStart를 직접 호출하지 않는다.
        for (int i = 0; i < 3; i++)
        {
            if (chapter.startPoints[i] == null) { record("CH8-02", false, $"startPoints[{i}]가 비었다."); yield break; }
            Teleport(players[i], chapter.startPoints[i].transform.position);
        }
        for (int i = 0; i < 40 && chapter.Current != ManagerChapterController.State.Running; i++)
            yield return new WaitForFixedUpdate();
        bool started = chapter.Current == ManagerChapterController.State.Running && manager.Surveillance &&
                       manager.Current == ManagerAgent.State.Patrol && !chapter.SkipSurveillance;
        record("CH8-02", started, "3도형을 CH8 시작점에 순간이동 후 자동 시작·실제 감시 순찰 상태 확인.");
        if (!started) yield break;

        Vector3 patrolStart = manager.agent.transform.position;
        for (int i = 0; i < 40 && Vector3.Distance(manager.agent.transform.position, patrolStart) < 0.15f; i++)
            yield return new WaitForFixedUpdate();
        float patrolMoved = Vector3.Distance(manager.agent.transform.position, patrolStart);
        Vector3 displacement = manager.agent.transform.position - patrolStart;
        bool patrolled = patrolMoved >= 0.15f && manager.Current == ManagerAgent.State.Patrol &&
                         Vector3.Dot(displacement.normalized, sector.transform.right) > 0.9f;
        record("CH8-03", patrolled, $"실제 PathChaser 첫 순찰 +X 이동 {displacement}, {patrolMoved:0.##}U / 최대 40 FixedUpdate.");
        if (!patrolled) yield break;

        // 책장 가림은 실제 Linecast/CanSee로 검사한다. 현재 순찰 위치에서 표본을 못 찾으면 실패로 기록한다.
        bool occlusion = TryShelfOcclusion(sector, manager, players[2], out string occlusionDetail);
        record("CH8-04", occlusion, occlusionDetail);
        // 책장 표본을 찾지 못해도 뒤의 독립된 완주 단계를 계속 진단한다.

        // 관리자 현재 전방 60~85% D의 보이는 지점으로 한 도형을 옮긴다. 반경 D/2 즉시 추격을 피한다.
        bool visibleSample = FindVisibleSuspect(manager, players[2], out string visibleDetail);
        if (!visibleSample)
        {
            record("CH8-05", false, visibleDetail);
            yield break;
        }
        for (int i = 0; i < 5 && manager.Current == ManagerAgent.State.Patrol; i++)
            yield return new WaitForFixedUpdate();
        bool suspected = manager.Current == ManagerAgent.State.Suspect;
        record("CH8-05", suspected, "가시 표본 " + visibleDetail + $", 실제 FixedUpdate 후 상태={manager.Current}.");
        if (!suspected) yield break;

        for (int i = 0; i < 100 && manager.Current == ManagerAgent.State.Suspect; i++)
            yield return new WaitForFixedUpdate();
        bool chased = manager.Current == ManagerAgent.State.Chase && manager.CatchActive;
        record("CH8-06", chased, $"의심 후 실제 FixedUpdate(최대 100회), 기준 {manager.suspectToChaseSeconds:0.##}초, 상태={manager.Current}, 잡힘활성={manager.CatchActive}.");
        if (!chased) yield break;

        // 실제 잡힘 트리거로 전원 초기화를 일으킨다. NotifyCaught 직접 호출은 하지 않는다.
        Teleport(players[0], FixturePosition(book.slot));
        book.slot.HandleInteract(players[0]);
        if (book.PageCount > 1) book.NextPage();
        Teleport(players[1], FixturePosition(station.slot));
        station.slot.HandleInteract(players[1]);
        if (station.Recipe != null && station.Recipe.Count > 0)
        {
            int first = FindReagent(station, station.Recipe[0]);
            if (first >= 0 && station.Select(first, players[1])) station.Submit(players[1]);
        }
        bool dirtyBeforeCatch = book.slot.CurrentOwner == players[0] && station.slot.CurrentOwner == players[1] &&
                                (book.PageCount <= 1 || book.PageIndex > 0);
        int failuresBefore = chapter.FailureCount;
        Teleport(players[2], manager.transform.position + Vector3.up * 0.6f);
        for (int i = 0; i < 40 && chapter.FailureCount == failuresBefore; i++)
            yield return new WaitForFixedUpdate();
        bool caught = dirtyBeforeCatch && chapter.FailureCount == failuresBefore + 1 &&
                      chapter.Current == ManagerChapterController.State.Ready &&
                      !station.HasToken && !key.HasKey && !key.Revealed &&
                      book.PageIndex == 0 && book.slot.CurrentOwner == null && station.slot.CurrentOwner == null;
        record("CH8-07", caught,
            $"잡힘 전 역할/책 변경={dirtyBeforeCatch}, 실제 ManagerCatchZone 접촉 후 실패횟수 {failuresBefore}→{chapter.FailureCount}, 상태={chapter.Current}, 역할·혼합·열쇠 초기화.");
        if (!caught) yield break;

        for (int i = 0; i < 240 && (chapter.Current != ManagerChapterController.State.Running ||
                                      players[0].ExternallyDriven || players[1].ExternallyDriven || players[2].ExternallyDriven); i++)
            yield return new WaitForFixedUpdate();
        bool resumed = chapter.Current == ManagerChapterController.State.Running &&
                       !players[0].ExternallyDriven && !players[1].ExternallyDriven && !players[2].ExternallyDriven &&
                       manager.Surveillance && manager.IsAwake && !chapter.SkipSurveillance;
        record("CH8-08", resumed, "전원 CH8 시작 복귀 연출 종료 후 감시를 켠 채 자동 재시작 확인.");
        if (!resumed) yield break;

        // 각 도형을 역할 사물에 배치하고 공개 HandleInteract(E의 실제 역할 경로)로 순차 점유한다.
        Teleport(players[0], FixturePosition(book.slot));
        Teleport(players[1], FixturePosition(station.slot));
        Teleport(players[2], FixturePosition(cctv.slot));
        for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
        bool fixtureGrounded = AtFixtureFloor(players[0], book.slot) &&
                               AtFixtureFloor(players[1], station.slot) && AtFixtureFloor(players[2], cctv.slot);
        if (!fixtureGrounded)
        { record("CH8-09", false, "Book/Mixer/CCTV 앞 1U 도형이 바닥에 착지하지 못함."); yield break; }
        book.slot.HandleInteract(players[0]);
        roles.ClosePanelsOf(players[0]);
        station.slot.HandleInteract(players[1]);
        roles.ClosePanelsOf(players[1]);
        cctv.slot.HandleInteract(players[2]);
        int cameraBefore = cctv.CameraIndex;
        cctv.Step(1);
        bool cameraStepped = cctv.CameraIndex != cameraBefore;
        roles.ClosePanelsOf(players[2]);
        station.slot.HandleInteract(players[1]); // 점유 유지 상태에서 Mixer 화면 재개
        bool rolesAssigned = book.slot.CurrentOwner == players[0] && station.slot.CurrentOwner == players[1] &&
                             cctv.slot.CurrentOwner == players[2] && cctv.cameras.Length == 3 &&
                             !book.slot.IsPanelOpen && station.slot.PanelUser == players[1] && !cctv.slot.IsPanelOpen;
        record("CH8-09", rolesAssigned && cameraStepped,
            "서로 다른 3도형 Book/Mixer/CCTV 점유, 화면 닫힘 중 점유 유지·재개, CCTV Step(API 시험, 실제 E/Tab 동선은 별도).");
        if (!rolesAssigned || !cameraStepped) yield break;

        if (station.Recipe == null || station.Recipe.Count == 0 || book.pages == null || book.pages.Length == 0)
        {
            record("CH8-10", false, "혼합 레시피 또는 가이드북 본문이 비었다.");
            yield break;
        }
        bool mixed = true;
        string mixDetail = "";
        for (int i = 0; i < station.Recipe.Count; i++)
        {
            int index = FindReagent(station, station.Recipe[i]);
            if (index < 0 || !station.Select(index, players[1])) { mixed = false; mixDetail = $"재료 {i} 선택 거부"; break; }
            MixingStation.MixResult result = station.Submit(players[1]);
            if (i + 1 == station.Recipe.Count ? result != MixingStation.MixResult.Completed : result != MixingStation.MixResult.Added)
            { mixed = false; mixDetail = $"재료 {i} 투입 결과 {result}"; break; }
        }
        mixed &= station.IsComplete && station.HasToken && station.bottleVisual != null && station.bottleVisual.activeSelf;
        record("CH8-10", mixed, "책에 생성된 현 시도 조합식을 실제 Select/Submit으로 투입·병/토큰 생성. " + mixDetail);
        if (!mixed) yield break;

        roles.ClosePanelsOf(players[1]); // Tab 전환 시 화면 닫힘과 같은 공개 API
        // 사용 지점에서 접촉 반경 바깥·사용 거리 안으로 접근한다. 같은 프레임에 E 요청을 보내 잡힘을 피하는 시험이다.
        Vector3 fromManager = Vector3.forward;
        Vector3 usePos = use.transform.position + fromManager * Mathf.Min(2.2f, chapter.useDistance - 0.2f);
        Teleport(players[1], usePos);
        float useDistance = Vector3.Distance(players[1].transform.position, use.transform.position);
        bool requested = use.RequestUse(players[1]);
        bool slept = requested && chapter.Current == ManagerChapterController.State.Asleep &&
                     manager.Current == ManagerAgent.State.Asleep && !manager.CatchActive &&
                     !station.HasToken && station.TokenUsed && key.Revealed;
        record("CH8-11", slept,
            $"실제 ManagerUsePoint.RequestUse → 거리 {useDistance:0.##}/{chapter.useDistance:0.##}U, 토큰 소비·수면·열쇠 노출.");
        if (!slept) yield break;

        Teleport(players[1], key.transform.position + Vector3.up * 0.2f);
        bool taken = key.TryTake(players[1]) && key.HasKey;
        record("CH8-12", taken, "실제 열쇠 지점에서 공개 TryTake 거리/권한 판정으로 팀 열쇠 획득.");
        if (!taken) yield break;

        Teleport(players[1], keyDoor.transform.position + Vector3.up * 0.2f);
        bool opened = keyDoor.TryOpen(players[1]) && keyDoor.IsOpen;
        float doorY = keyDoor.door.transform.position.y;
        for (int i = 0; i < 180 && keyDoor.door.transform.position.y < doorY + 2.5f; i++)
            yield return new WaitForFixedUpdate();
        bool doorRaised = opened && keyDoor.door.transform.position.y >= doorY + 2.5f;
        record("CH8-13", doorRaised, "열쇠 문 공개 TryOpen 후 doorPhysics 물리 상승 2.5U 확인.");
        if (!doorRaised) yield break;

        // 문 앞까지만 배치하고 Rigidbody 물리 이동으로 문 뒤 트리거에 들어간다.
        PlayerShapeStats.ShapeKind expectedLead = (PlayerShapeStats.ShapeKind)shapeIndex;
        PlayerMover lead = Array.Find(players, p => p != null && p.GetComponent<PlayerShapeIdentity>() != null &&
            p.GetComponent<PlayerShapeIdentity>().Kind == expectedLead);
        PlayerShapeIdentity leadId = lead != null ? lead.GetComponent<PlayerShapeIdentity>() : null;
        Collider leadSolid = leadId != null ? leadId.solidCollider : null;
        if (leadSolid == null) { record("CH8-14", false, $"선두 도형 {expectedLead}의 solidCollider 없음."); yield break; }
        float footBelowPivot = lead.transform.position.y - leadSolid.bounds.min.y;
        Vector3 inside = sector.transform.TransformPoint(new Vector3(0f, 0f, 130f));
        inside.y = finalFlow.exitFloor.bounds.max.y + footBelowPivot + 0.06f;
        Teleport(lead, inside);
        Rigidbody walker = lead.GetComponent<Rigidbody>();
        if (walker == null) { record("CH8-14", false, "이동 도형에 Rigidbody가 없다."); yield break; }
        Vector3 forward = sector.transform.forward;
        for (int i = 0; i < 140 && !finalFlow.Completed; i++)
        {
            walker.MovePosition(walker.position + forward * 0.08f);
            yield return new WaitForFixedUpdate();
        }
        walker.velocity = Vector3.zero;
        Vector3 end = sector.transform.InverseTransformPoint(lead.transform.position);
        bool escaped = finalFlow.Completed && finalFlow.CompletionCount == 1 &&
                       finalFlow.FirstEntrantKind == expectedLead &&
                       end.z >= 136f && end.z <= 140f && Mathf.Abs(end.x) < 4f;
        record("CH8-14", escaped,
            $"선두={expectedLead}, 실제 첫 통과={finalFlow.FirstEntrantKind}, 문 앞 z130에서 Rigidbody.MovePosition 후 완료={finalFlow.Completed}, 횟수={finalFlow.CompletionCount}, 위치 {end}. 실패={finalFlow.LastFailure}.");
        if (!escaped) yield break;

        PlayerMover[] ordered = (PlayerMover[])players.Clone();
        Array.Sort(ordered, (a, b) => a.GetComponent<PlayerShapeIdentity>().Kind.CompareTo(b.GetComponent<PlayerShapeIdentity>().Kind));
        bool gathered = finalFlow.finalSlots != null && finalFlow.finalSlots.Length == 3;
        Collider floor = finalFlow.exitFloor;
        for (int i = 0; i < 3 && gathered; i++)
        {
            PlayerShapeIdentity id = ordered[i].GetComponent<PlayerShapeIdentity>();
            Collider solid = id != null ? id.solidCollider : null;
            if (solid == null || floor == null ||
                Vector2.Distance(new Vector2(ordered[i].transform.position.x, ordered[i].transform.position.z),
                                 new Vector2(finalFlow.finalSlots[i].position.x, finalFlow.finalSlots[i].position.z)) > 0.12f ||
                solid.bounds.min.y < floor.bounds.max.y + 0.03f) gathered = false;
            for (int j = 0; j < i; j++)
            {
                PlayerShapeIdentity otherId = ordered[j].GetComponent<PlayerShapeIdentity>();
                if (solid != null && otherId != null && otherId.solidCollider != null &&
                    solid.bounds.Intersects(otherId.solidCollider.bounds)) gathered = false;
            }
        }
        record("CH8-15", gathered, "완료 후 세 도형이 서로 다른 최종 슬롯에 있고 collider 발이 탈출 바닥 위이며 도형끼리 겹치지 않음.");
        if (!gathered) yield break;

        RespawnController respawn = UnityEngine.Object.FindObjectOfType<RespawnController>();
        if (respawn == null) { record("CH8-16", false, "공용 RespawnController 없음."); yield break; }
        Teleport(ordered[0], sector.transform.TransformPoint(new Vector3(0f, 0.8f, 130f)));
        respawn.RespawnPlayer(ordered[0].gameObject); // 수동 R과 같은 공개 공용 체크포인트 경로
        yield return new WaitForFixedUpdate();
        for (int i = 0; i < 240 && ordered[0].ExternallyDriven; i++) yield return new WaitForFixedUpdate();
        Vector3 back = sector.transform.InverseTransformPoint(ordered[0].transform.position);
        Collider returnedSolid = ordered[0].GetComponent<PlayerShapeIdentity>().solidCollider;
        bool noOverlap = returnedSolid != null;
        for (int i = 1; i < 3 && noOverlap; i++)
        {
            Collider occupied = ordered[i].GetComponent<PlayerShapeIdentity>().solidCollider;
            noOverlap = occupied != null && !returnedSolid.bounds.Intersects(occupied.bounds);
        }
        bool finalCheckpoint = back.z >= 140.5f && back.z <= 141.5f && noOverlap &&
                               finalFlow.Completed && finalFlow.CompletionCount == 1;
        record("CH8-16", finalCheckpoint,
            $"공용 RespawnPlayer(R 경로) 후 도형 위치 {back}(목표 z141), 다른 두 도형과 collider 겹침={(!noOverlap)}, 완료 횟수={finalFlow.CompletionCount}.");
    }

    private static bool CheckCentralFirstRoute(SectorController sector, ManagerAgent manager, out string detail)
    {
        Transform[] points = manager.agent.waypoints;
        if (points == null || points.Length < 3 || manager.patrolSpeed <= 0f)
        { detail = "순찰점 3개 이상·양수 순찰속도 필요."; return false; }

        float lengthToCenter = 0f;
        bool foundCenter = false;
        int blockedSegments = 0;
        string firstBlock = "";
        for (int i = 0; i + 1 < points.Length; i++)
        {
            if (points[i] == null || points[i + 1] == null)
            { detail = $"순찰점 [{i}] 참조 누락."; return false; }
            Vector3 a = points[i].position + Vector3.up * manager.eyeHeight;
            Vector3 b = points[i + 1].position + Vector3.up * manager.eyeHeight;
            Vector3 delta = b - a;
            float distance = delta.magnitude;
            if (distance < 0.01f) continue;
            if (!foundCenter)
            {
                lengthToCenter += distance;
                if (Mathf.Abs(sector.transform.InverseTransformPoint(points[i + 1].position).x) < 0.1f)
                    foundCenter = true;
            }
            foreach (RaycastHit hit in Physics.SphereCastAll(a, manager.pathClearRadius, delta / distance,
                         distance, manager.sightMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(manager.transform) ||
                    hit.collider.GetComponentInParent<PlayerMover>() != null) continue;
                blockedSegments++;
                if (firstBlock.Length == 0) firstBlock = $"{i + 1}→{i + 2}:{hit.collider.name}";
            }
        }
        float seconds = lengthToCenter / manager.patrolSpeed;
        detail = $"첫 중앙 x≈0까지 {lengthToCenter:0.##}U / {manager.patrolSpeed:0.##}U/s = {seconds:0.##}초(정적 후보), " +
                 $"전 경로 SphereCast 반경 {manager.pathClearRadius:0.##}U 지형충돌 {blockedSegments}개" +
                 (blockedSegments > 0 ? $" 첫 {firstBlock}." : ".");
        return foundCenter && Mathf.Abs(lengthToCenter - 42f) < 0.1f &&
               Mathf.Abs(seconds - 21f) < 0.1f && blockedSegments == 0;
    }

    private static bool FindVisibleSuspect(ManagerAgent manager, PlayerMover actor, out string detail)
    {
        Vector3 origin = manager.transform.position;
        Vector3 facing = manager.Facing;
        for (int angle = -30; angle <= 30; angle += 10)
        {
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * facing;
            foreach (float fraction in new[] { 0.7f, 0.8f })
            {
                Teleport(actor, origin + direction.normalized * manager.detectDistance * fraction + Vector3.up * 0.5f);
                PlayerShapeIdentity identity = actor.GetComponent<PlayerShapeIdentity>();
                if (manager.CanSee(identity, manager.transform.position, out _, out float distance) &&
                    distance > manager.detectDistance * 0.5f)
                {
                    detail = $"각도 {angle}°, 거리 {distance:0.##}/{manager.detectDistance:0.##}U";
                    return true;
                }
            }
        }
        detail = "실제 시야(Linecast)에서 D/2~D 가시 지점을 찾지 못했다. 순찰·책장 배치/시야를 확인.";
        return false;
    }

    private static bool TryShelfOcclusion(SectorController sector, ManagerAgent manager, PlayerMover actor, out string detail)
    {
        Transform[] all = sector.GetComponentsInChildren<Transform>(true);
        Vector3 eye = manager.transform.position + Vector3.up * manager.eyeHeight;
        foreach (Transform shelf in all)
        {
            if (shelf == null || !shelf.name.StartsWith("GEO_S8_Shelf_")) continue;
            Collider solid = shelf.GetComponent<Collider>();
            if (solid == null || solid.isTrigger) continue;
            Bounds b = solid.bounds;
            Vector3[] targets =
            {
                new Vector3(b.center.x, 0f, b.max.z + 0.8f),
                new Vector3(b.center.x, 0f, b.min.z - 0.8f),
                new Vector3(b.max.x + 0.8f, 0f, b.center.z),
                new Vector3(b.min.x - 0.8f, 0f, b.center.z)
            };
            foreach (Vector3 sample in targets)
            {
                Vector3 target = sample;
                target.y = manager.transform.position.y + 0.5f;
                Teleport(actor, target);
                PlayerShapeIdentity identity = actor.GetComponent<PlayerShapeIdentity>();
                if (identity == null) continue;
                Vector3 center = identity.solidCollider != null ? identity.solidCollider.bounds.center : actor.transform.position;
                if (Vector3.Distance(eye, center) > manager.detectDistance) continue;
                Vector3 toTarget = Vector3.ProjectOnPlane(center - eye, Vector3.up);
                if (Vector3.Angle(manager.Facing, toTarget) > manager.fieldOfView * 0.5f) continue;
                if (!Physics.Linecast(eye, center, out RaycastHit hit, manager.sightMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider == null || !hit.collider.name.StartsWith("GEO_S8_Shelf_")) continue;
                bool unseen = !manager.CanSee(identity, manager.transform.position, out _, out _);
                detail = $"책장 {shelf.name} 차폐 hit={hit.collider.name}, 표본 거리 {Vector3.Distance(eye, center):0.##}U, CanSee={(!unseen)}.";
                return unseen;
            }
        }
        detail = "현재 순찰 위치에서 D/FOV 내 책장 차폐 표본을 구성하지 못했다(미검증).";
        return false;
    }

    private static int FindReagent(MixingStation station, string id)
    {
        if (station.reagents == null) return -1;
        for (int i = 0; i < station.reagents.Length; i++)
            if (station.reagents[i] != null && station.reagents[i].id == id) return i;
        return -1;
    }

    private static void Teleport(PlayerMover actor, Vector3 worldPosition)
    {
        Rigidbody rb = actor.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = worldPosition;
        }
        actor.transform.position = worldPosition;
        Physics.SyncTransforms();
    }
}
#endif
