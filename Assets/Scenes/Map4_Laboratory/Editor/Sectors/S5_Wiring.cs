#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 섹터5 "외다리 함정(CH5)" 복귀·안전망 배선 — 설계/S5_설계.md §4(복귀·안전망), §8-1(배선 방식 차이).
/// S5_Builder.Build가 모든 지형·기믹을 만든 **뒤 마지막에 1회** 부른다(계약 C2, 진행/지시서/_계약.md).
///
/// [만드는 것 — 전부 generated/S5_Respawn 그룹 1개 아래]
/// - 팀 SectionSafePoint "CH5_START"(startLocalPos) + 보조 지점 2개, 같은 오브젝트에 팀 SectionHitCounter(임계 1).
///   팀 SampleScene의 SectionSafePoint_CH5_Start와 같은 "안전점 + 카운터 한 오브젝트" 조립이다(S5_팀API.md SectionRespawn).
/// - 팀 OutOfBoundsVolume(허공 아래 y (로컬 killY − 2)~−2 — S5는 −50~−2, x −30~30, z 0~136) — 팀 메뉴 Tools/Respawn/Create Respawn Scale.
///   설계 §4 [제안] 아랫면 −8을 내린 이유는 BuildVoidOutOfBounds 주석(수정 라운드 S5검산1).
/// - 팀 RespawnZone 체크포인트(A 플랫폼 입구) — 팀 메뉴 Tools/Respawn/Create Checkpoint Pole.
/// - ADAPT_S5_RespawnBridge(Lab_SectionRespawnBridge) — 카운터 → Master 씬 RespawnController를 실행 중에 잇는다.
///
/// [배선 — 팀 기믹 동작은 바꾸지 않는다(HANDOFF §5-0, 공통 규칙 5)]
/// - 도끼·망치·가시(PeriodicTrapBase.OnHazardHit), 발사구(ProjectileLauncher.OnHazardHit), 고정 레이저
///   (LaserBeam.OnHazardHit — FixedPeriodicLaser가 아니라 LaserBeam에 있다), 회전 다리(StepRotatingBridge.OnPlayerFell)
///   → 카운터 RegisterHitEvent(GameObject)를 UnityEventTools.AddPersistentListener로 **동적 인자 영구 배선**한다.
///   같은 씬 안이라 저장된다. 팀 SampleScene CH5 기믹 6개가 같은 방식(m_Mode 0, m_CallState 2)이다 [팀 씬].
/// - 카운터 OnThresholdReached에는 영구 배선을 **두지 않는다** — Lab_SectionRespawnBridge는 영구 배선이 1개라도
///   있는 카운터를 건너뛴다(Lab_SectionRespawnBridge.cs:34). RespawnController가 Master 씬에 있어 씬 사이
///   영구 참조가 불가능하기 때문이다(S1과 같은 방식).
/// - 팀 배선 메뉴 Tools/*/Wire Hits To Respawn은 쓰지 않는다 — 씬의 모든 트랩을 공용 체크포인트
///   (RespawnController.RespawnPlayer(GameObject))로 직결해 설계 §4(PRD: CH5 시작 안전점 + 카운터 임계 1)와 다르다(§8-1).
/// - 팀 private 필드 리플렉션 없음. 팀 생성 메뉴 중 public static인 RespawnMenuItem만 직접 부른다.
///
/// 좌표는 섹터 로컬(+Z 진행, 다리·플랫폼 윗면 y=0 = 세계 18). 출처: [확정]=설계서 사용자 확정, [팀]=팀 코드·PRD,
/// [제안]=설계서 제안값, [계산]=계산값, [구현 결정]=설계서에 값이 없어 여기서 정한 것(보고서 진행/보고/S5-W.md), [추정]=실측 필요.
/// </summary>
public static class S5_Wiring
{
    // ── 이름 ──
    private const string GroupName = "S5_Respawn";                    // [확정 계약 C2]
    private const string SectionId = "CH5_START";                     // [확정 계약 C2 / 설계 §4]
    private const string SafePointName = "S5_SafePoint_CH5_START";    // [구현 결정]
    private const string CheckpointName = "S5_Checkpoint_A";          // [구현 결정]
    private const string OutOfBoundsName = "S5_VoidOutOfBounds";      // [구현 결정]
    private const string BridgeAdapterName = "ADAPT_S5_RespawnBridge";// [확정 계약 C2]

    // ── 카운터 ──
    private const int HitsBeforeRespawn = 1; // [확정 설계 §4 "임계 1 = 즉시 복귀", 팀 PeriodicTraps.md §1] 팀 기본값은 2(SectionHitCounter.cs:36)

    // ── A 시작 플랫폼·입구 ── [확정 설계 §2 표]
    private const float PlatformAX0 = -30f, PlatformAX1 = 4f, PlatformAZ0 = 0f, PlatformAZ1 = 16f;
    private const float EntranceX0 = -4f, EntranceX1 = 4f;   // [확정 설계 §2] S4 연결 통로 폭 8
    private const float EntranceZ1 = 2f;                      // [제안 지시서 S5-W] 안전점을 두지 않을 입구 통로 끝부분 z 0~2

    // ── 보조 지점 ──
    private const float OccupancyRadius = 0.6f;  // [팀] SectionSafePoint.occupancyRadius 기본값(SectionSafePoint.cs:29) — 바꾸지 않는다
    /// <summary>[계산] 안전점끼리 최소 간격. 점유 반경 0.6 겹침 없음 → ≥ 1.2, 네모(1×1) 대각 반폭 0.707 × 2 = 1.414 → 1.2보다 크게.
    /// 값 2는 공용 스폰 슬롯 간격(Map4SceneBuilder Spawn_0~2, x −2/0/2)과 같게 한 [구현 결정].</summary>
    private const float BackupSpacing = 2f;
    /// <summary>[구현 결정] 보조 지점이 A 가장자리(허공)에서 떨어져 있어야 하는 거리 — 도형 반폭 0.5 + 여유 0.5.</summary>
    private const float PlatformEdgeMargin = 1f;
    /// <summary>[구현 결정] 보조 후보 오프셋(시작 안전점 기준, 우선순위 순). 다리 1이 +X(x 4)에 붙어 있으므로 좌우 → 앞뒤 → 먼 쪽 순.</summary>
    private static readonly Vector2[] BackupCandidates =
    {
        new Vector2(-2f, 0f), new Vector2(2f, 0f), new Vector2(0f, 2f), new Vector2(0f, -2f),
        new Vector2(-4f, 0f), new Vector2(-2f, 2f), new Vector2(-2f, -2f), new Vector2(2f, 2f),
        new Vector2(2f, -2f), new Vector2(0f, 4f), new Vector2(4f, 0f), new Vector2(0f, -4f),
        new Vector2(4f, 2f), new Vector2(2f, 4f), new Vector2(-4f, 2f), new Vector2(-2f, 4f),
        new Vector2(4f, -2f), new Vector2(2f, -4f), new Vector2(-4f, -2f), new Vector2(-2f, -4f),
    };

    // ── 체크포인트(팀 RespawnZone) ──
    /// <summary>[구현 결정] 공용 마커 Manual/Markers/Checkpoint(0, 0.1, 3)와 같은 xz. 원점 = 바닥(윗면 y 0).
    /// 구역 z 0~6이 입구(z 0) 전체를 덮어, S4에서 들어오는 도형이 반드시 밟는다.</summary>
    private static readonly Vector3 CheckpointLocal = new Vector3(0f, 0f, 3f);
    /// <summary>[구현 결정] 구역 폭 X — 팀 기본 6(RespawnMenuItem.cs:22)을 입구 통로 폭 8(x −4~4)로 넓힌다. 폭 6(x −3~3)이면
    /// 통로 벽에 붙어 들어오는 도형(중심 |x| 3.5, 반폭 0.5)이 경계에 스치기만 해 진입 판정이 불확실하다 [계산]. 높이 18·깊이 6은 팀 기본 그대로.</summary>
    private const float CheckpointWidthX = 8f;

    // ── 일반 추락(팀 OutOfBoundsVolume) ──
    // [수정 라운드 S5검산1] 설계 §4 [제안] "y −8~−2"에서 **아랫면만** 바꾼다. 팀 판정은 "볼륨 안(또는 killY 아래)에 연속
    // outOfBoundsSeconds(3초)"이고 벗어나면 타이머가 지워진다(RespawnController.cs:59, 315-344). 두께 6 볼륨은 자유낙하 도형이
    // 약 0.6초에 지나가 스스로는 복귀를 일으키지 못했다(허공 바닥 솔리드가 없어 killY까지 3.1초 + 3초 = 약 6.1초 [계산]).
    // 볼륨 아랫면을 Master killY 아래까지 내리면 "볼륨 ∪ killY 아래"가 y −2 아래 전체를 끊김 없이 덮어, 타이머가 y −2 통과
    // 순간 시작해 3초 뒤 발화한다 — 떨어진 뒤 약 3.7초에 공용 체크포인트(A) 복귀 [계산]. 윗면 −2는 설계값 그대로다.
    private const float OobY1 = -2f;                              // [제안 설계 §4 "허공 아래 y −8~−2"의 윗면 — 유지]
    private const float OobY0Design = -8f;                        // [제안 설계 §4] 아랫면 설계값 — killY 계산이 불가할 때만 쓰는 대체값
    /// <summary>[팀] RespawnController.killY 기본값(RespawnController.cs:55). Map4_Master.unity:554도 −30 [씬 실측 09-28].
    /// Master의 값을 바꾸면 이 상수도 같이 바꿔야 한다(씬 사이 참조가 없어 생성 때 읽을 수 없다).</summary>
    private const float MasterKillYWorld = -30f;
    /// <summary>[구현 결정] 볼륨 아랫면을 로컬 killY보다 이만큼 더 내려 경계 프레임에서도 판정이 끊기지 않게 한다.</summary>
    private const float KillLineOverlap = 2f;
    private const float OutOfBoundsSeconds = 3f;                  // [팀] RespawnController.outOfBoundsSeconds 기본값(:59) — 로그·주석용, 바꾸지 않는다
    private const float ShapeHalfHeight = 0.5f;                   // [확정 공통 규칙 11] 도형 크기 약 1U → 선 도형 중심 = 윗면 + 0.5 [추정: 팀 도형 피벗 = 중심]
    private const float OobX0 = -30f, OobX1 = 30f;                // [구현 결정 지시서 S5-W: 섹터 폭 전 범위]
    private const float OobZ0 = 0f, OobZ1 = 136f;                 // [구현 결정 지시서 S5-W: 섹터 길이 전 범위, Map4Layout S5 길이 136]

    // ── 계약 개수 ──
    private const int ExpectedHazards = 11; // [확정 계약 C2] 다리2 7개(s10·18·26·36·44·52·62) + 다리3 4개(s3·7·11·15)
    private const int ExpectedBridges = 2;  // [확정 계약 C2] 다리1 회전 판 2개

    /// <summary>계약 C2. 에러가 나도 예외를 던지지 않는다(로그만) — S5_Builder는 지형을 지우지 않는다.</summary>
    public static void Wire(Transform generated, Vector3 startLocalPos, IList<Component> hazards, IList<Component> bridges)
    {
        if (generated == null)
        {
            Debug.LogError("[S5_Wiring] generated가 null이다 — 복귀·안전망을 만들지 않는다.");
            return;
        }

        try
        {
            Transform group = NewGroup(generated);

            SectionHitCounter counter = BuildSafePointAndCounter(group, startLocalPos);
            BuildCheckpoint(group);
            float oobY0 = BuildVoidOutOfBounds(group, generated);

            GameObject adapter = new GameObject(BridgeAdapterName);
            adapter.transform.SetParent(group, false);
            adapter.AddComponent<Lab_SectionRespawnBridge>();

            int expected = 0, wired = 0;
            if (counter != null)
            {
                wired += WireHazards(hazards, counter, ref expected);
                wired += WireBridges(bridges, counter, ref expected);
                if (counter.OnThresholdReached != null && counter.OnThresholdReached.GetPersistentEventCount() > 0)
                    Debug.LogError("[S5_Wiring] 카운터 OnThresholdReached에 영구 배선이 있다 — Lab_SectionRespawnBridge가 이 카운터를 건너뛴다.", counter);
                EditorUtility.SetDirty(counter);
            }

            if (wired != expected || expected != ExpectedHazards + ExpectedBridges)
                Debug.LogError($"[S5_Wiring] 영구 배선 {wired}/{expected}개(계약 {ExpectedHazards + ExpectedBridges}개 = 함정 {ExpectedHazards} + 회전 다리 {ExpectedBridges}) — 위 로그 확인.");
            else
                Debug.Log($"[S5_Wiring] 완료 — 안전점 '{SectionId}' + 보조 2, 카운터 임계 {HitsBeforeRespawn}, 영구 배선 {wired}개, " +
                          $"체크포인트·장외 볼륨(y {oobY0:0.##}~{OobY1})·브리지 생성.");

            WarnStandableSurfacesAboveVolume(generated);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[S5_Wiring] 배선 중 예외 — 지형은 그대로 둔다: {e}");
        }
    }

    // ───────────────────────── 그룹 ─────────────────────────

    private static Transform NewGroup(Transform generated)
    {
        // Generated는 Generate마다 비워지지만, 같은 빌드 안에서 두 번 불려도 겹치지 않게 기존 그룹을 지운다(우리 생성물만 있는 그룹).
        Transform old = generated.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        GameObject go = new GameObject(GroupName);
        go.transform.SetParent(generated, false); // 로컬 원점·무회전 = 섹터 로컬 좌표계 그대로
        return go.transform;
    }

    private static Vector3 P(Transform group, Vector3 local) => group.TransformPoint(local);

    // ───────────────────────── 안전점 + 카운터 ─────────────────────────

    private static SectionHitCounter BuildSafePointAndCounter(Transform group, Vector3 start)
    {
        if (!InsidePlatformA(start, 0f))
            Debug.LogError($"[S5_Wiring] startLocalPos {start}가 A 플랫폼(x {PlatformAX0}~{PlatformAX1}, z {PlatformAZ0}~{PlatformAZ1}) 밖이다 — 그대로 두되 S5_Builder 값 확인 필요.");
        else if (!InsidePlatformA(start, PlatformEdgeMargin))
            Debug.LogWarning($"[S5_Wiring] startLocalPos {start}가 A 가장자리 {PlatformEdgeMargin} 이내다 — 복귀한 도형이 허공 옆에 선다.");
        if (InsideEntrance(start, 0f))
            Debug.LogWarning($"[S5_Wiring] startLocalPos {start}가 입구 통로 끝(x {EntranceX0}~{EntranceX1}, z 0~{EntranceZ1}) 안이다 — 들어오는 도형과 겹칠 수 있다.");
        if (Mathf.Abs(start.y) > 0.02f)
            Debug.LogWarning($"[S5_Wiring] startLocalPos.y {start.y} ≠ 0 — 안전점은 '놓인 자리 = 착지 바닥'이다(SectionSafePoint.cs:8-11).");

        SectionSafePoint point = CreateByTeamMenu<SectionSafePoint>(RespawnMenuItem.CreateSectionSafePoint, "Tools/Respawn/Create Section Safe Point");
        if (point == null) return null;
        point.gameObject.name = SafePointName;
        point.transform.SetParent(group, false);
        point.transform.position = P(group, start);
        point.transform.rotation = group.rotation;
        point.sectionId = SectionId;
        // occupancyRadius는 팀 기본 0.6 그대로. counter(수동 복귀 리셋)는 비운다 — 트리거 콜라이더를 두지 않으므로 어차피 꺼진 기능이고,
        // 임계 1이면 발화 즉시 0으로 되돌아가 리셋할 값이 없다(SectionHitCounter.cs:87).

        List<Vector3> chosen = new List<Vector3> { start };
        List<Transform> backups = new List<Transform>();
        foreach (Vector2 off in BackupCandidates)
        {
            if (backups.Count == 2) break;
            Vector3 c = new Vector3(start.x + off.x, start.y, start.z + off.y);
            if (!InsidePlatformA(c, PlatformEdgeMargin)) continue;
            if (InsideEntrance(c, OccupancyRadius)) continue;
            bool tooClose = false;
            foreach (Vector3 q in chosen)
                if (Vector3.Distance(q, c) < BackupSpacing - 1e-4f) { tooClose = true; break; }
            if (tooClose) continue;
            if (SolidAt(P(group, c), group)) continue; // S5_Builder가 A 위에 둔 솔리드와 겹치는 후보는 뺀다

            GameObject b = new GameObject($"{SafePointName}_Backup{backups.Count}");
            b.transform.SetParent(group, false);
            b.transform.localPosition = c;
            backups.Add(b.transform);
            chosen.Add(c);
        }
        if (backups.Count < 2)
            Debug.LogError($"[S5_Wiring] 보조 지점을 {backups.Count}/2개만 찾았다(시작 {start}) — A 안·입구 밖·간격 {BackupSpacing} 조건을 만족하는 후보 부족.");
        point.backupPoints = backups.ToArray();
        if (SolidAt(point.transform.position, group))
            Debug.LogWarning($"[S5_Wiring] 시작 안전점 {start} 위(높이 0.05~1.05)에 솔리드 콜라이더가 있다 — 복귀한 도형이 끼일 수 있다.", point);

        SectionHitCounter counter = point.gameObject.AddComponent<SectionHitCounter>(); // 팀도 메뉴 없이 AddComponent(PathChaserMenuItem.cs:147)
        counter.destination = point;
        counter.hitsBeforeRespawn = HitsBeforeRespawn;
        // hitCooldown은 팀 기본 0.5 그대로.
        if (counter.OnThresholdReached == null) counter.OnThresholdReached = new SectionHitCounter.SectionRespawnEvent();
        EditorUtility.SetDirty(point);
        return counter;
    }

    private static bool InsidePlatformA(Vector3 p, float margin) =>
        p.x >= PlatformAX0 + margin - 1e-4f && p.x <= PlatformAX1 - margin + 1e-4f &&
        p.z >= PlatformAZ0 + margin - 1e-4f && p.z <= PlatformAZ1 - margin + 1e-4f;

    private static bool InsideEntrance(Vector3 p, float margin) =>
        p.x > EntranceX0 - margin && p.x < EntranceX1 + margin && p.z >= PlatformAZ0 && p.z < EntranceZ1 + margin;

    /// <summary>바닥(윗면) 바로 위 0.05~1.05에 트리거 아닌 콜라이더가 있는가 — 도형 크기 약 1U [확정 공통 규칙 11].
    /// 편집기 물리 질의이므로 먼저 SyncTransforms. 결과가 불확실한 환경(배치 모드 등)에서는 '없음'으로 떨어져 후보를 빼지 않을 뿐이다 [추정].</summary>
    private static bool SolidAt(Vector3 worldFloorPoint, Transform group)
    {
        Physics.SyncTransforms();
        Vector3 up = group.up;
        Collider[] hits = Physics.OverlapSphere(worldFloorPoint + up * 0.55f, 0.5f, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider h in hits)
            if (h != null && h.gameObject.scene == group.gameObject.scene) return true;
        return false;
    }

    // ───────────────────────── 체크포인트 ─────────────────────────

    /// <summary>팀 RespawnZone(공용 체크포인트). 일반 추락(장외 볼륨·killY)은 여기로 페이드 복귀 → 결과가 CH5 시작(설계 §4).
    /// 팀 FindGroundPoint는 구역 중앙(높이 9)에서 아래로 9+2 레이를 쏜다(RespawnZone.cs:153-183) — A 윗면 y 0을 찾는다.</summary>
    private static void BuildCheckpoint(Transform group)
    {
        RespawnZone zone = CreateByTeamMenu<RespawnZone>(RespawnMenuItem.CreateCheckpoint, "Tools/Respawn/Create Checkpoint Pole");
        if (zone == null) return;
        GameObject go = zone.gameObject;
        go.name = CheckpointName;
        go.transform.SetParent(group, false);
        go.transform.localPosition = CheckpointLocal;
        go.transform.localRotation = Quaternion.identity; // 팀 전제: 구역은 축 정렬(RespawnZone.cs:185-186)
        BoxCollider box = go.GetComponent<BoxCollider>();
        if (box != null)
        {
            box.size = new Vector3(CheckpointWidthX, box.size.y, box.size.z); // 높이 18·깊이 6·center(0,9,0) 팀 기본 유지
            EditorUtility.SetDirty(box);
        }
        else Debug.LogError("[S5_Wiring] 팀 체크포인트에 BoxCollider가 없다.", go);
    }

    // ───────────────────────── 일반 추락 ─────────────────────────

    /// <summary>팀 OutOfBoundsVolume — 이 안(또는 killY 아래)에 연속 3초 있으면 공용 체크포인트로 복귀(RespawnController.cs:59, 306-344).
    /// 트리거라 Map4Audit 관통 검사 대상이 아니다(Map4Audit.cs:102). 판정은 도형 좌표를 ClosestPoint로 묻는다(OutOfBoundsVolume.cs).
    /// [수정 라운드 S5검산1] 아랫면 = 로컬 killY − KillLineOverlap(S5: 세계 −30 − 18 = −48 → −50) [계산], 윗면 −2 [제안 설계 §4].
    /// 돌려주는 값 = 실제 쓴 아랫면(로컬 y).</summary>
    private static float BuildVoidOutOfBounds(Transform group, Transform generated)
    {
        float oobY0 = VoidVolumeBottom(generated);
        OutOfBoundsVolume oob = CreateByTeamMenu<OutOfBoundsVolume>(RespawnMenuItem.CreateRespawnScale, "Tools/Respawn/Create Respawn Scale");
        if (oob == null) return oobY0;
        GameObject go = oob.gameObject;
        go.name = OutOfBoundsName;
        go.transform.SetParent(group, false);
        go.transform.localPosition = new Vector3((OobX0 + OobX1) / 2f, (oobY0 + OobY1) / 2f, (OobZ0 + OobZ1) / 2f); // [계산] S5: (0, −26, 68)
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        BoxCollider box = go.GetComponent<BoxCollider>();
        if (box == null) { Debug.LogError("[S5_Wiring] 팀 장외 볼륨에 BoxCollider가 없다.", go); return oobY0; }
        box.isTrigger = true;
        box.center = Vector3.zero;
        box.size = new Vector3(OobX1 - OobX0, OobY1 - oobY0, OobZ1 - OobZ0); // [계산] S5: 60 × 48 × 136
        EditorUtility.SetDirty(box);
        return oobY0;
    }

    /// <summary>볼륨 아랫면(섹터 로컬 y). Master killY를 섹터 로컬로 옮겨 KillLineOverlap만큼 더 내린다 — "볼륨 ∪ killY 아래"가
    /// y OobY1 아래를 빈틈없이 덮게 하는 것이 목적이다. 섹터가 기울어 있거나 killY가 볼륨 윗면보다 위면 설계값 −8로 떨어뜨리고 Error.</summary>
    private static float VoidVolumeBottom(Transform generated)
    {
        if (Vector3.Dot(generated.up, Vector3.up) < 0.9999f)
        {
            Debug.LogError($"[S5_Wiring] Generated가 기울어 있다(up {generated.up}) — killY(수평면)를 로컬 y 하나로 옮길 수 없어 " +
                           $"장외 볼륨 아랫면을 설계값 {OobY0Design}로 둔다. 이 경우 허공 추락 복귀가 약 6초 걸린다.", generated);
            return OobY0Design;
        }
        float killLocalY = generated.InverseTransformPoint(new Vector3(generated.position.x, MasterKillYWorld, generated.position.z)).y;
        float bottom = killLocalY - KillLineOverlap;
        if (bottom >= OobY1 - 0.01f)
        {
            Debug.LogError($"[S5_Wiring] 로컬 killY {killLocalY:0.##}가 볼륨 윗면 {OobY1} 근처·위다 — 아랫면을 설계값 {OobY0Design}로 둔다.", generated);
            return OobY0Design;
        }
        return bottom;
    }

    /// <summary>진단만(생성·수정 없음). 볼륨 아랫면이 killY 아래라 y OobY1 아래는 어디서 멈춰도 3초 뒤 복귀한다. 남는 구멍은
    /// "윗면이 OobY1 − 0.5 ~ −0.5 사이인 솔리드" 하나다 — 그 위에 선 도형의 중심(윗면 + 0.5)은 볼륨 위·다리 아래에 떠 있어
    /// 자동 복귀하지 않는다(수동 R만). 지금 S5_Builder 구성(플랫폼·다리 윗면 0, 기둥 0.1·0.3, 벽 6)에는 해당이 없다 [계산].</summary>
    private static void WarnStandableSurfacesAboveVolume(Transform generated)
    {
        Physics.SyncTransforms();
        float lo = OobY1 - ShapeHalfHeight, hi = -ShapeHalfHeight;
        List<string> found = new List<string>();
        foreach (Collider c in generated.GetComponentsInChildren<Collider>(true))
        {
            if (c == null || c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            float top = generated.InverseTransformPoint(c.bounds.max).y;
            if (top >= lo - 0.01f && top < hi - 0.01f) found.Add($"{c.name}(윗면 y {top:0.##})");
        }
        if (found.Count > 0)
            Debug.LogWarning($"[S5_Wiring] 윗면이 y {lo}~{hi} 사이인 솔리드 {found.Count}개: " +
                             string.Join(", ", found.GetRange(0, Mathf.Min(5, found.Count))) +
                             $" — 떨어진 도형이 그 위에 서면 중심이 장외 볼륨(윗면 {OobY1}) 밖·killY 위라 자동 복귀하지 않는다.", generated);
    }

    // ───────────────────────── 피격·낙하 배선 ─────────────────────────

    private static int WireHazards(IList<Component> hazards, SectionHitCounter counter, ref int expected)
    {
        if (hazards == null) { Debug.LogError("[S5_Wiring] hazards가 null이다."); return 0; }
        if (hazards.Count != ExpectedHazards)
            Debug.LogError($"[S5_Wiring] hazards {hazards.Count}개 — 계약은 {ExpectedHazards}개.");

        int wired = 0;
        for (int i = 0; i < hazards.Count; i++)
        {
            expected++;
            Component h = hazards[i];
            if (h == null) { Debug.LogError($"[S5_Wiring] hazards[{i}]가 null이다."); continue; }
            if (h.gameObject.scene != counter.gameObject.scene)
            {
                Debug.LogError($"[S5_Wiring] hazards[{i}] '{h.name}'가 카운터와 다른 씬에 있다 — 영구 배선 불가.", h);
                continue;
            }

            UnityEvent<GameObject> evt = ResolveHazardEvent(h, out Object owner);
            if (evt == null)
            {
                Debug.LogError($"[S5_Wiring] hazards[{i}] '{h.name}'({h.GetType().Name})에서 OnHazardHit을 찾지 못했다 — " +
                               "AxeTrap·HammerTrap·SpikeTrap·ProjectileLauncher·FixedPeriodicLaser(LaserBeam) 중 하나여야 한다.", h);
                continue;
            }
            if (AddHitListener(evt, counter, owner)) wired++;
        }
        return wired;
    }

    private static int WireBridges(IList<Component> bridges, SectionHitCounter counter, ref int expected)
    {
        if (bridges == null) { Debug.LogError("[S5_Wiring] bridges가 null이다."); return 0; }
        if (bridges.Count != ExpectedBridges)
            Debug.LogError($"[S5_Wiring] bridges {bridges.Count}개 — 계약은 {ExpectedBridges}개.");

        int wired = 0;
        for (int i = 0; i < bridges.Count; i++)
        {
            expected++;
            Component c = bridges[i];
            StepRotatingBridge bridge = c as StepRotatingBridge;
            if (bridge == null && c != null) bridge = c.GetComponent<StepRotatingBridge>();
            if (bridge == null) { Debug.LogError($"[S5_Wiring] bridges[{i}]가 null이거나 StepRotatingBridge가 아니다.", c); continue; }
            if (bridge.gameObject.scene != counter.gameObject.scene)
            {
                Debug.LogError($"[S5_Wiring] bridges[{i}] '{bridge.name}'가 카운터와 다른 씬에 있다 — 영구 배선 불가.", bridge);
                continue;
            }
            if (bridge.OnPlayerFell == null) bridge.OnPlayerFell = new StepRotatingBridge.PlayerFellEvent();
            if (AddHitListener(bridge.OnPlayerFell, counter, bridge)) wired++;
        }
        return wired;
    }

    /// <summary>피격 이벤트가 있는 팀 컴포넌트를 찾는다(모두 public 필드 — 리플렉션 없음).
    /// 레이저는 OnHazardHit이 FixedPeriodicLaser가 아니라 LaserBeam에 있다(LaserBeam.cs:37, S5_팀API.md FixedPeriodicLaser §7).</summary>
    private static UnityEvent<GameObject> ResolveHazardEvent(Component h, out Object owner)
    {
        owner = null;

        PeriodicTrapBase trap = h as PeriodicTrapBase;
        if (trap == null) trap = h.GetComponent<PeriodicTrapBase>();
        if (trap != null)
        {
            if (trap.OnHazardHit == null) trap.OnHazardHit = new PeriodicTrapBase.PlayerHitEvent();
            owner = trap;
            return trap.OnHazardHit;
        }

        ProjectileLauncher launcher = h as ProjectileLauncher;
        if (launcher == null) launcher = h.GetComponent<ProjectileLauncher>();
        if (launcher != null)
        {
            if (launcher.OnHazardHit == null) launcher.OnHazardHit = new ProjectileLauncher.PlayerHitEvent();
            owner = launcher;
            return launcher.OnHazardHit;
        }

        LaserBeam beam = h as LaserBeam;
        if (beam == null)
        {
            FixedPeriodicLaser laser = h as FixedPeriodicLaser;
            if (laser == null) laser = h.GetComponent<FixedPeriodicLaser>();
            if (laser != null && laser.beam != null) beam = laser.beam;            // 팀 메뉴가 대입(SecurityLaserMenuItem.cs:32)
            if (beam == null) beam = h.GetComponentInChildren<LaserBeam>(true);
        }
        if (beam != null)
        {
            if (beam.OnHazardHit == null) beam.OnHazardHit = new LaserBeam.PlayerHitEvent();
            owner = beam;
            return beam.OnHazardHit;
        }
        return null;
    }

    /// <summary>카운터 RegisterHitEvent(GameObject)를 동적 인자 영구 배선(m_Mode EventDefined, RuntimeOnly)으로 건다.
    /// RegisterHit은 bool을 반환해 UnityAction&lt;GameObject&gt;에 맞지 않으므로 팀 래퍼 RegisterHitEvent를 쓴다(SectionHitCounter.cs:94-95).
    /// 같은 이벤트에 남은 RegisterHitEvent 배선(재실행·사라진 옛 카운터)은 먼저 걷어 중복 발화를 막는다.</summary>
    private static bool AddHitListener(UnityEvent<GameObject> evt, SectionHitCounter counter, Object owner)
    {
        for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            Object target = evt.GetPersistentTarget(i);
            if (evt.GetPersistentMethodName(i) == nameof(SectionHitCounter.RegisterHitEvent) &&
                (target == null || target is SectionHitCounter))
                UnityEventTools.RemovePersistentListener(evt, i);
        }

        int before = evt.GetPersistentEventCount();
        UnityEventTools.AddPersistentListener(evt, new UnityAction<GameObject>(counter.RegisterHitEvent));
        if (owner != null) EditorUtility.SetDirty(owner);

        bool ok = evt.GetPersistentEventCount() == before + 1 && evt.GetPersistentTarget(before) == counter;
        if (!ok) Debug.LogError($"[S5_Wiring] '{(owner != null ? owner.name : "?")}' 영구 배선 확인 실패.", owner);
        return ok;
    }

    // ───────────────────────── 팀 생성 메뉴 ─────────────────────────

    /// <summary>팀 public static 생성 메뉴(RespawnMenuItem)를 직접 부르고 생성물을 받는다. 메뉴는 끝에 Selection을 바꾸지만
    /// 배치 모드에서 Selection 수신은 실측 기록이 없으므로(S5_팀API.md §0-2 "모르겠다") 호출 전후 목록 비교로도 찾는다.</summary>
    private static T CreateByTeamMenu<T>(System.Action create, string menuLabel) where T : Component
    {
        HashSet<T> before = new HashSet<T>(Object.FindObjectsOfType<T>(true));
        Selection.activeGameObject = null;
        create();

        T made = null;
        GameObject sel = Selection.activeGameObject;
        if (sel != null)
        {
            T c = sel.GetComponent<T>();
            if (c != null && !before.Contains(c)) made = c;
        }
        if (made == null)
        {
            foreach (T c in Object.FindObjectsOfType<T>(true))
                if (!before.Contains(c)) { made = c; break; }
        }
        if (made == null) Debug.LogError($"[S5_Wiring] 팀 메뉴 '{menuLabel}' 생성물({typeof(T).Name})을 찾지 못했다.");
        return made;
    }
}
#endif
