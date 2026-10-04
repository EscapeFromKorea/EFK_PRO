#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 섹터8 "약물 조합(CH8)" 팀 기믹 배치·배선 — 계약 R2S68 K8(진행/지시서/R2S68/_계약.md K8-0·K8-1·K8-2), 지시서 S8-W3.
/// S8_Builder.Build가 지형·문·앵커를 만든 뒤 Wire → S8_Dress.Apply 순서로 부른다 [계약 K0-2].
///
/// [만드는 것 — 전부 generated/S8_Gimmicks 그룹 1개 아래, 씬 루트 잔여 0 [계약 K0-3]]
/// 팀 CH8 기믹에는 부품별 생성 메뉴가 없다(메뉴는 방 전체를 만드는 "Create CH8 Test Room" 하나 — 월드 고정 위치에
/// 시험 지형까지 짓고 Master의 RespawnController를 교차 씬 참조로 박으므로 맵 배치에 쓸 수 없다, 진행/조사/S8_팀API.md §6).
/// 그래서 팀 시험 방 코드(ManagerSurveillanceSystem/Editor/Ch8TestRoomMenuItem.cs:38-169)와 **같은 구성·이름·자식 관계·
/// 필드 값**을 AddComponent + public 필드로 조립한다. 시험 방 지형(Floor/Wall/Cover/Exit_Floor)·역할 책상(Desk)·
/// 열쇠 문 몸체는 만들지 않는다 — B의 GEO_S8_Table_*/GEO_S8_Desk_Console, refs.exitDoor(Map4Build.Door)를 쓴다.
///   CH8_RoleAssignmentManager · CH8_Slot_Book(+BookPanel) · CH8_Slot_Mixer(+MixingStation, Bottle) ·
///   CH8_Slot_Monitor(+ManagerCctvPanel) · CCTV_1..3 · C8_MANAGER(Rigidbody·PathChaserAgent·ManagerAgent·
///   ManagerStatusIndicator, 자식 Visual(+1 [판정 17])/Nose·C8_CATCH·C8_USE) · C8_KEY_POINT(+KeyVisual, ANCH_S8_Key [판정 19]) ·
///   C8_KEY_DOOR · CH8_START_0..2(SectionSafePoint [판정 19]) · S8_Zone_Check(팀 RespawnZone — 수동 R 체크포인트 [판정 19]) ·
///   CH8_ManagerChapterController · CH8_StartZone
///   S8_Zone_Check만 팀 생성 함수 RespawnMenuItem.CreateCheckpoint()(public static, 메뉴 "Tools/Respawn/Create Checkpoint Pole",
///   RespawnMenuItem.cs:27-28)로 만들고 그룹 아래로 옮긴다(S1_Builder.cs:196-201 선례). 나머지는 AddComponent.
///
/// [기믹 동작은 바꾸지 않는다 — HANDOFF §5-0, 공통 규칙 5]
/// - 감지·속도·시간 등 팀 수치는 전부 팀 기본값 그대로(아래 상수 표는 팀 시험 방이 넣는 배치 값과 초안 배치 값만).
/// - 팀 private 필드 리플렉션 없음. 팀 컴포넌트는 public 필드만으로 전부 배선된다(S8_팀API.md §7-1) [계약 K9].
/// - UnityEvent 영구 배선은 **0개**. CH8 기본 흐름(사용 → 수면 → 열쇠 → 문, 잡힘 → 전원 복귀 → 초기화)은 전부 참조 필드 +
///   팀 컴포넌트의 런타임 자동 구독으로 돈다(ManagerChapterController.cs:117-127 Bind, :249 NotifyCaught·:269 ReturnAllToStart).
///   특히 ManagerUsePoint.onUseRequested에 영구 리스너를 걸면 컨트롤러의 런타임 구독과 겹쳐 HandleUse가 두 번 불린다 — 금지.
///   ManagerKeyDoor.onOpened도 비워 둔다 — 받을 맵 완료 처리가 없다(엔딩은 설계서에 없음, 보고서 mismatchReports) [계약 K8-1].
/// - 잡힘 → 전원 복귀는 팀 기본 동작: 컨트롤러가 참가자(Kind 순)마다 startPoints[i]로 RespawnController.RespawnPlayer.
///   startPoints 3개는 refs.startAnchor에서 팀 시험 방 간격(2.5)으로 파생한다 [판정 19 · 계약 K8-0].
/// - RespawnController는 Master 씬에 있으므로 컨트롤러·역할 매니저의 respawnController는 **비워 둔다** — 둘 다 실행 중
///   FindObjectOfType로 스스로 찾는다(ManagerChapterController.cs:109·271, RoleAssignmentManager.cs:114). 씬 사이 참조 0.
///   수동 R 체크포인트(RespawnZone)도 밟으면 static RespawnController.SetCheckpoint를 부르므로(RespawnZone.cs:138) 씬 연결이 없다.
///
/// [1인 시험] 팀 "시험 감시 생략"(skipSurveillance, private 직렬화 필드 — 기본 false)은 **바꾸지 않는다**. 쓰는 법은
/// 보고서(진행/보고/S8-W3.md)에만: 실행 중 준비 상태에서 public SetSkipSurveillance(true)(ManagerChapterController.cs:142) [계약 K8-1].
///
/// 좌표·크기는 아래 상수 표 한 곳에 모았다. 앵커 위치·방향은 B(S8_Builder)의 refs와 이름 규약 앵커 ANCH_S8_Key가 정본이고
/// W는 앵커를 옮기거나 지우지 않는다(자기 인스턴스를 앵커 월드 포즈에 맞춘다) [계약 K0-3]. 대체 좌표로 조용히 메우지 않는다 [판정 19].
/// 출처: [확정]=설계서 확정, [판정 n]=진행/판정/2026-09-28_S6S8_판정.md, [계약 Kx]=R2S68 계약 조항, [명령 결정]=계약이 정한 세부,
/// [팀]=팀 코드(파일:행), [제안]=초안(진행/초안/S8_배치초안.md) 또는 이 파일이 정한 값, [계산]=계산값, [해석]=문구 해석,
/// [추정]=실측 필요 [판정 23]. 컴파일은 csc 형식 검사만(Unity 실행 금지 — 통합 후 대기열).
/// </summary>
public static class S8_Wiring
{
    // ═════════════════════════ 상수 표 (확인 뒤 여기만 고친다) ═════════════════════════

    // ── 그룹·이름 ──
    private const string GroupName = "S8_Gimmicks";                         // [계약 K0-3] generated 바로 아래 1개
    private const string RoleManagerName = "CH8_RoleAssignmentManager";     // [팀 Ch8TestRoomMenuItem.cs:52]
    private const string SlotNamePrefix = "CH8_Slot_";                      // [팀 :246]
    private const string BottleName = "Bottle";                              // [팀 :65]
    private const string LabelName = "Label";                                // [팀 :258]
    private const string CctvNamePrefix = "CCTV_";                           // [팀 :75-77] CCTV_1..3
    private const string ManagerName = "C8_MANAGER";                         // [팀 :90]
    private const string ManagerVisualName = "Visual";                       // [팀 :98]
    private const string ManagerNoseName = "Nose";                           // [팀 :99]
    private const string CatchName = "C8_CATCH";                             // [팀 :111]
    private const string UseName = "C8_USE";                                 // [팀 :119]
    private const string KeyPointName = "C8_KEY_POINT";                      // [팀 :124]
    private const string KeyVisualName = "KeyVisual";                        // [팀 :127]
    private const string KeyDoorName = "C8_KEY_DOOR";                        // [팀 :137]
    private const string StartPointPrefix = "CH8_START_";                    // [팀 :146] sectionId = 이름(:148)
    private const string ControllerName = "CH8_ManagerChapterController";    // [팀 :153]
    private const string StartZoneName = "CH8_StartZone";                    // [팀 :163]
    /// <summary>[판정 19 · 계약 K0-3·K8-0] 이름 규약 앵커(Refs 필드 아님) — B가 S8_Anchors 아래 빈 GO로 둔다. W는 generated
    /// 아래에서 이 이름으로 찾아 ManagerKeyPoint 자리로 쓰고, 없으면 LogError 후 열쇠를 만들지 않는다(대체 좌표 없음).</summary>
    private const string KeyAnchorName = "ANCH_S8_Key";
    private const string CheckpointName = "S8_Zone_Check";                  // [명령 결정 — S6 S6_Zone_* 선례] 팀 이름 "Checkpoint"(RespawnMenuItem.cs:32)를 바꿔 붙인다
    private const string CheckpointMenuPath = "Tools/Respawn/Create Checkpoint Pole"; // [팀 RespawnMenuItem.cs:27] 로그 표기용(실행은 public static 직접 호출)

    // ── 역할 사물(RoleSlot) ──
    private const string RoleIdBook = "Book";                                // [팀 :57 / S8_팀API.md RoleSlot]
    private const string RoleIdMixer = "Mixer";                              // [팀 :61]
    private const string RoleIdMonitor = "Monitor";                          // [팀 :70]
    private static readonly Vector3 RoleTriggerCenter = new Vector3(0f, 1f, 0f);    // [팀 :249] 원점 = 바닥 윗면
    private static readonly Vector3 RoleTriggerSize = new Vector3(3.5f, 2f, 3.5f);  // [팀 :250]
    private const bool CreateRoleLabels = true;                              // [팀 :258-267] 역할 이름 표지(TextMesh, 콜라이더 없음)
    private const float LabelHeight = 2.2f;                                  // [팀 :258]
    private const float LabelCharacterSize = 0.25f;                          // [팀 :262]
    private const int LabelFontSize = 48;                                    // [팀 :263]
    private static readonly Color LabelColorBook = new Color(0.3f, 0.6f, 1f);     // [팀 :57]
    private static readonly Color LabelColorMixer = new Color(1f, 0.6f, 0.2f);    // [팀 :61]
    private static readonly Color LabelColorMonitor = new Color(0.5f, 1f, 0.5f);  // [팀 :70]
    /// <summary>완성 병(Bottle, 비활성 — MixingStation이 완성 때 켠다) 슬롯 로컬 위치. y 1.4 [팀 :66]. z −1.0 [제안]:
    /// 팀 시험 방은 슬롯 = 책상 중심이지만 S8은 앵커가 작업대 남면 0.25 앞(forward = 참가자 쪽)이라, 병을 작업대 위
    /// (앵커 뒤 1.0 = 작업대 남면에서 0.75 안쪽, 윗면 1.0 위) 에 올린다 [계산, 초안 §1-4 작업대 깊이 2·높이 1.0].</summary>
    private static readonly Vector3 BottleLocal = new Vector3(0f, 1.4f, -1.0f);
    private static readonly Vector3 BottleScale = new Vector3(0.3f, 0.3f, 0.3f);  // [팀 :66]

    // ── CCTV ──
    private const int ExpectedCameras = 3;                                   // [계약 K8-1 · 설계 §3-10]
    private const float CctvFieldOfView = 70f;                               // [팀 :278] 세로 FOV

    // ── 관리자 ──
    /// <summary>[팀 :98] Capsule 기본 높이 2(스케일 1) → 반높이 1. 팀 시험 방은 피벗 = 캡슐 중심(바닥 + 1)이다(:80).
    /// [판정 17 · 계약 K8-0] S8은 피벗 = 바닥(순찰점·관리자 시작 자리 y0, 눈 = 피벗 + eyeHeight 0.5 = 설계 §3-8 전제)이라
    /// Visual 자식만 올려 캡슐 밑면을 바닥에 맞춘다: lift = max(0, 반높이 − 피벗 높이) → 피벗 y0이면 Visual 로컬 (0,+1,0).
    /// ApplyVisual은 회전만 바꾸므로(ManagerAgent.cs:526-529) 올린 높이는 실행 중 유지된다. 눈 높이·감지값은 팀 기본 그대로.</summary>
    private const float ManagerCapsuleHalfHeight = 1f;
    private const float ManagerPivotY = 0f;                                  // [판정 17 · 계약 K8-0] 피벗 = 섹터 바닥 윗면(B PatrolPivotY)
    private const float PivotTolerance = 0.02f;                              // [계약 K0-2 ±0.02] 이보다 어긋나면 Warn
    private static readonly Vector3 NoseLocal = new Vector3(0f, 0.5f, 0.5f);      // [팀 :99]
    private static readonly Vector3 NoseScale = new Vector3(0.4f, 0.2f, 0.4f);    // [팀 :100]
    private const float CatchRadius = 1.2f;                                  // [팀 :115] Reset이 1로 덮으므로 AddComponent 뒤에 넣는다
    private const float WaypointYTolerance = 0.02f;                          // [제안] 순찰점 y 동일 판정(S8_팀API.md ManagerAgent — 추격은 수평 이동)

    // ── 열쇠·열쇠 문 ──
    // 열쇠 자리는 이름 규약 앵커 ANCH_S8_Key 하나뿐이다(위 KeyAnchorName) [판정 19]. 좌표 상수를 두지 않는다.
    private static readonly Vector3 KeyVisualScale = new Vector3(0.4f, 0.4f, 0.4f);   // [팀 :128]
    /// <summary>[팀 :137] 열쇠 문 사용 지점 높이 — 시험 방은 바닥 + 0.5에 둔다(useRadius는 이 점 ↔ 참가자 피벗 3D 거리,
    /// ManagerKeyDoor.cs:23). 앵커(바닥 윗면)는 옮기지 않고 인스턴스만 위로 0.5.</summary>
    private const float KeyDoorHeight = 0.5f;

    // ── 시작 지점·자동 시작 구역 ──
    private const int StartPointCount = 3;                                   // [팀 :144 · 판정 19] 참가자마다 1개
    private const float StartPointSpacing = 2.5f;                            // [팀 :146 · 판정 19 · 계약 K8-0] startAnchor.right × −2.5/0/+2.5 (점유 반경 0.6 × 2 = 1.2 이상)
    private const float StartZoneBottomY = -1f;                              // [팀 b15c8f9 :162, ManagerChapterController.cs:59-60] 밑면 = 바닥 − 1
    private const float StartZoneHeight = 6f;                                // [팀 :164 startZoneSize.y 6]
    /// <summary>[계산 초안 §6-7] archiveBounds가 비었을 때만 쓰는 대체값(섹터 로컬): 서고 안쪽 x −43.5~43.5 · z 0.5~131.5.</summary>
    private static readonly Vector3 StartZoneCenterFallback = new Vector3(0f, 2f, 66f);
    private static readonly Vector3 StartZoneSizeFallback = new Vector3(87f, 6f, 131f);

    // ── 수동 R 체크포인트(팀 RespawnZone 1개) [판정 19 · 계약 K8-0] — 값은 초안 [제안], Unity 실측(R 눌러 복귀 위치) 전까지 (추정) ──
    /// <summary>[제안 초안 §1-2·§3 W 파생값] 루트 = startAnchor − Flat(startAnchor.forward) × 1.0 → 섹터 로컬 (0,0,4).
    /// 1.0 뒤로 빼는 이유: 팀 막대(Pole, 콜라이더 없음)가 루트 로컬 x −2.6에 생긴다(RespawnMenuItem.cs:47) — 루트를 startAnchor에
    /// 두면 시작점 (−2.5,0,5)과 0.1 거리로 겹친다(시각), 1.0 빼면 1.0 떨어진다 [계산].</summary>
    private const float CheckpointBackOffset = 1.0f;
    /// <summary>[제안 초안 §1-2·C17 · 계약 K8-0 "W가 초안 근거로 정함"] BoxCollider size — 폭 8 = 입구 개구 x−4~4 [계약 K0-2],
    /// 깊이 6 = 팀 ZoneWidth [팀 RespawnMenuItem.cs:22], 높이 3 = S6 시작 구역 선례 [판정 4]. **팀 기본 높이 18(:23)은 쓰지 않는다** —
    /// 수동 R 바닥은 구역 중심에서 아래로 쏜 레이의 가장 높은 비트리거 면이라(RespawnZone.cs:153-184) 높이 18이면 중심 y9에서
    /// 천장 윗면 y8.5를 바닥으로 잡아 R이 천장 위로 보낸다(C17 [계산]). 높이 3이면 레이 바닥 y0, 낙하 스폰 y2.5(dropSpawnInset 0.5, :41).
    /// 구역 = 섹터 로컬 x−4~4 · y0~3 · z1~7(시작점 3개와 공용 Checkpoint 마커 (0,0.1,3)을 품는다).</summary>
    private static readonly Vector3 CheckpointSize = new Vector3(8f, 3f, 6f);
    /// <summary>[팀 RespawnMenuItem.cs:39 규칙] center = (0, 높이/2, 0) — 원점 = 구역 밑면 = 바닥 윗면. = (0, 1.5, 0).</summary>
    private static readonly Vector3 CheckpointCenter = new Vector3(0f, 1.5f, 0f);

    // ── 자기 검사 기준(경고만 — 값을 바꾸지 않는다) ──
    private const float BookMixerMin = 2f;                                   // [확정 설계 §3-9] 책 ↔ 혼합 ≥ 2
    private const float ShapeRadius = 0.5f;                                  // [추정] 도형 크기 약 1U(공통 규칙 11)
    private const float CheckEps = 1e-3f;

    // ═════════════════════════ 진입점 ═════════════════════════

    private static int errorCount;
    private static int warningCount;

    /// <summary>[계약 K8-2] 예외를 던지지 않는다(로그만) — S8_Builder는 지형을 지우지 않는다 [계약 K0-2]. 앵커가 null이면
    /// LogError 후 그 항목만 건너뛴다.</summary>
    public static void Wire(Transform generated, S8_Refs refs)
    {
        errorCount = 0;
        warningCount = 0;
        if (generated == null)
        {
            Debug.LogError("[S8_Wiring] generated가 null이다 — 팀 기믹을 배치하지 않는다.");
            return;
        }
        if (refs == null)
        {
            Debug.LogError("[S8_Wiring] refs(S8_Refs)가 null이다 — 팀 기믹을 배치하지 않는다.", generated);
            return;
        }

        HashSet<GameObject> rootsBefore = SnapshotRoots(generated);
        Transform group = null;
        try
        {
            group = NewGroup(generated);

            // 1) 역할 매니저 — CH8 전용 1개(S7과 따로) [팀 :52-54]. respawnController는 비운다(실행 중 스스로 찾음).
            RoleAssignmentManager roles = NewChild(group, RoleManagerName, generated.position, generated.rotation)
                .AddComponent<RoleAssignmentManager>();
            EditorUtility.SetDirty(roles);

            // 2) 역할 사물 3개 [팀 :56-78]
            RoleSlot bookSlot = BuildSlot(group, roles, RoleIdBook, refs.roleBookAnchor, "roleBookAnchor", LabelColorBook);
            BookPanel book = null;
            if (bookSlot != null)
            {
                book = bookSlot.gameObject.AddComponent<BookPanel>();
                book.slot = bookSlot; // 본문은 MixingStation이 시도마다 다시 쓴다(:59)
                EditorUtility.SetDirty(book);
            }

            RoleSlot mixerSlot = BuildSlot(group, roles, RoleIdMixer, refs.roleMixerAnchor, "roleMixerAnchor", LabelColorMixer);
            MixingStation station = null;
            if (mixerSlot != null)
            {
                station = mixerSlot.gameObject.AddComponent<MixingStation>();
                station.slot = mixerSlot;
                station.book = book; // 책이 빠졌으면 null — 팀 MixingStation은 book 없이도 동작(조합식만 못 봄)
                if (book == null) Err("책 슬롯이 없어 MixingStation.book이 비었다 — 조합식을 읽을 곳이 없다.");
                GameObject bottle = Primitive(PrimitiveType.Cylinder, mixerSlot.transform, BottleName, BottleLocal, BottleScale);
                bottle.SetActive(false);
                station.bottleVisual = bottle;
                EditorUtility.SetDirty(station);
            }

            RoleSlot monitorSlot = BuildSlot(group, roles, RoleIdMonitor, refs.roleMonitorAnchor, "roleMonitorAnchor", LabelColorMonitor);
            if (monitorSlot != null)
            {
                ManagerCctvPanel cctv = monitorSlot.gameObject.AddComponent<ManagerCctvPanel>();
                cctv.slot = monitorSlot;
                cctv.cameras = BuildCctvCameras(group, refs.cctvCameraAnchors);
                EditorUtility.SetDirty(cctv);
            }

            // 3) 관리자 + 잡힘 구역 + 사용 지점 [팀 :80-121]
            ManagerCatchZone catchZone;
            ManagerUsePoint usePoint;
            ManagerAgent managerAgent = BuildManager(group, generated, refs, station, out catchZone, out usePoint);

            // 4) 열쇠 [팀 :123-128 · 판정 19] · 열쇠 문 [팀 :137-140 · 계약 K8-1]
            ManagerKeyPoint keyPoint = BuildKeyPoint(group, generated, refs, roles);
            BuildKeyDoor(group, refs, roles, keyPoint);

            // 5) 참가자별 시작 지점 [팀 :142-150 · 판정 19]
            SectionSafePoint[] startPoints = BuildStartPoints(group, refs.startAnchor);

            // 5-1) 수동 R 체크포인트 — 팀 RespawnZone 1개 [판정 19 · 계약 K8-0]
            RespawnZone checkpoint = BuildCheckpoint(group, generated, refs.startAnchor);

            // 6) 컨트롤러 [팀 :152-166]
            ManagerChapterController controller = BuildController(group, generated, refs, roles, station, usePoint,
                managerAgent, keyPoint, startPoints);
            if (catchZone != null)
            {
                catchZone.controller = controller;
                EditorUtility.SetDirty(catchZone);
            }

            // 7) 자기 검사(아무 값도 바꾸지 않는다)
            SelfCheck(generated, refs, managerAgent, usePoint, controller, startPoints, monitorSlot, checkpoint);
        }
        catch (System.Exception e)
        {
            Err($"배선 중 예외 — 지형은 그대로 둔다: {e}");
        }
        finally
        {
            AdoptStrayRoots(generated, rootsBefore, group);
        }

        if (errorCount > 0)
            Debug.LogError($"[S8_Wiring] 끝 — 에러 {errorCount}개, 경고 {warningCount}개(위 로그 확인). 영구 UnityEvent 배선 0개(설계상).", generated);
        else
            Debug.Log($"[S8_Wiring] 완료 — 팀 CH8 기믹을 '{GroupName}' 아래 배치·배선(경고 {warningCount}개). 영구 UnityEvent 배선 0개(설계상).", generated);
    }

    // ═════════════════════════ 그룹 ═════════════════════════

    private static Transform NewGroup(Transform generated)
    {
        // 같은 빌드 안에서 두 번 불려도 겹치지 않게 기존 그룹을 지운다(우리 생성물만 있는 그룹 — S5_Wiring과 같은 방식).
        Transform old = generated.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        GameObject go = new GameObject(GroupName);
        go.transform.SetParent(generated, false); // 로컬 원점·무회전 = 섹터 로컬 좌표계 그대로
        return go.transform;
    }

    // ═════════════════════════ 역할 사물 ═════════════════════════

    /// <summary>팀 Ch8TestRoomMenuItem.Slot(:244-269)과 같은 구성: 슬롯 GO에 트리거 BoxCollider + RoleSlot(roleId, manager),
    /// 자식 Label. 팀 Desk(솔리드 책상)는 시험 지형이라 만들지 않는다 — B의 작업대·콘솔 책상 GEO를 쓴다.</summary>
    private static RoleSlot BuildSlot(Transform group, RoleAssignmentManager roles, string roleId, Transform anchor,
        string anchorField, Color labelColor)
    {
        if (anchor == null)
        {
            Err($"refs.{anchorField}가 null — 역할 사물 '{roleId}'를 건너뛴다.");
            return null;
        }

        GameObject go = NewChild(group, SlotNamePrefix + roleId, anchor.position, FlatRotation(anchor.forward, group));
        BoxCollider trigger = go.AddComponent<BoxCollider>(); // RoleSlot GO에는 Rigidbody가 없으므로 트리거도 이 GO에(RoleSlot.cs:82-104)
        trigger.isTrigger = true;
        trigger.center = RoleTriggerCenter;
        trigger.size = RoleTriggerSize;

        RoleSlot slot = go.AddComponent<RoleSlot>();
        slot.roleId = roleId;
        slot.manager = roles;
        // interactKey E·allowMultipleUsers false는 팀 기본 그대로.
        EditorUtility.SetDirty(slot);

        if (CreateRoleLabels) BuildLabel(go.transform, roleId, labelColor);
        return slot;
    }

    /// <summary>[팀 :258-267] 역할 이름 표지. 팀 시험 방은 무회전이지만 S8 슬롯은 참가자 쪽(forward)을 보므로, 참가자가
    /// forward 쪽에서 −forward를 보며 읽도록 글자 앞면을 돌린다 [계산 — TextMesh는 자기 +Z 방향으로 볼 때 바로 읽힌다].</summary>
    private static void BuildLabel(Transform slot, string roleId, Color color)
    {
        GameObject label = new GameObject(LabelName);
        label.transform.SetParent(slot, false);
        label.transform.localPosition = new Vector3(0f, LabelHeight, 0f);
        Vector3 back = Flat(-slot.forward);
        if (back.sqrMagnitude > 1e-6f) label.transform.rotation = Quaternion.LookRotation(back.normalized, Vector3.up);

        TextMesh text = label.AddComponent<TextMesh>();
        text.text = roleId;
        text.anchor = TextAnchor.MiddleCenter;
        text.characterSize = LabelCharacterSize;
        text.fontSize = LabelFontSize;
        text.color = color;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            text.font = font;
            MeshRenderer mr = label.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = font.material;
        }
        else Warn("내장 글꼴 LegacyRuntime.ttf를 찾지 못했다 — 역할 표지 글자가 보이지 않을 수 있다.");
    }

    // ═════════════════════════ CCTV ═════════════════════════

    /// <summary>[팀 :271-280] 빈 GO + Camera(enabled=false, FOV 70). MainCamera 태그·AudioListener 없음. 배열 순서 =
    /// refs.cctvCameraAnchors 순서 = ←/→ 전환 순서(CAM 1/3…). null 앵커는 LogError 후 빼고(팀 Awake는 null을 건너뛰지만
    /// 계약상 null 요소 금지) 나머지 순서는 유지한다. 방향 = 앵커 forward(시선), up = 월드 +Y(계약 K0-3).</summary>
    private static Camera[] BuildCctvCameras(Transform group, List<Transform> anchors)
    {
        List<Camera> cams = new List<Camera>();
        if (anchors == null)
        {
            Err("refs.cctvCameraAnchors가 null — CCTV 카메라 0대.");
            return cams.ToArray();
        }
        if (anchors.Count != ExpectedCameras)
            Err($"refs.cctvCameraAnchors 개수 {anchors.Count} ≠ 계약 {ExpectedCameras}.");

        for (int i = 0; i < anchors.Count; i++)
        {
            Transform a = anchors[i];
            if (a == null)
            {
                Err($"refs.cctvCameraAnchors[{i}]가 null — 그 카메라를 건너뛴다(화면 번호가 하나씩 당겨진다).");
                continue;
            }
            Vector3 f = a.forward;
            Quaternion rot = Mathf.Abs(Vector3.Dot(f.normalized, Vector3.up)) < 0.999f
                ? Quaternion.LookRotation(f, Vector3.up)
                : a.rotation;
            GameObject go = NewChild(group, CctvNamePrefix + (i + 1), a.position, rot);
            Camera cam = go.AddComponent<Camera>();
            cam.enabled = false;               // 패널이 열 때만 켠다(ManagerCctvPanel.cs:80-85)
            cam.fieldOfView = CctvFieldOfView;
            EditorUtility.SetDirty(cam);
            cams.Add(cam);
        }
        return cams.ToArray();
    }

    // ═════════════════════════ 관리자 ═════════════════════════

    /// <summary>[팀 :80-121] 관리자 루트 = managerSpawnAnchor 위치(피벗, y0 [판정 17]). waypoints = refs.patrolWaypoints 순서 그대로 +
    /// 첫 점(WP_1)을 끝에 한 번 더 [판정 19 · 계약 K8-0] — 닫힌 경로(팀은 경로 끝에서 RejoinPath(0)으로 첫 점부터 다시 돈다,
    /// ManagerAgent.cs:298-301). refs는 WP_1..WP_k를 한 번씩만 담는다 [계약 K8-1] — 중복 Transform이면 LogError.
    /// 초안 기준 refs 8개 → waypoints 9개(끝 = ANCH_S8_WP_1). 순찰점은 B의 앵커 Transform을 그대로 쓴다(팀 C8_ROUTE/WP 자식은
    /// 만들지 않는다). 감지·속도 등은 팀 기본값.</summary>
    private static ManagerAgent BuildManager(Transform group, Transform generated, S8_Refs refs, MixingStation station,
        out ManagerCatchZone catchZone, out ManagerUsePoint usePoint)
    {
        catchZone = null;
        usePoint = null;

        List<Transform> wps = refs.patrolWaypoints;
        if (wps == null || wps.Count < 2)
        {
            Err($"refs.patrolWaypoints가 {(wps == null ? "null" : wps.Count + "개")} — 순찰로가 없어 관리자를 건너뛴다(컨트롤러가 시작 거부).");
            return null;
        }
        for (int i = 0; i < wps.Count; i++)
        {
            if (wps[i] == null)
            {
                Err($"refs.patrolWaypoints[{i}]가 null — 순찰로 모양이 깨지므로 관리자를 건너뛴다(계약: null 요소 금지).");
                return null;
            }
        }

        // [계약 K8-1] 각 점 1회만 — 중복 Transform(WP_1 두 번 등)은 계약 위반. 배치는 계속한다(아래 닫힘 규칙이 끝 중복을 흡수).
        HashSet<Transform> seen = new HashSet<Transform>();
        for (int i = 0; i < wps.Count; i++)
            if (!seen.Add(wps[i]))
                Err($"refs.patrolWaypoints[{i}] '{wps[i].name}'가 앞에서 이미 나왔다 — 계약은 WP_1..WP_k를 한 번씩만 담는다(닫힘은 W가 끝에 WP_1 재삽입) [판정 19].");

        // 닫힌 경로 [판정 19]: 끝에 첫 점을 다시 넣는다. 마지막 점이 이미 첫 점과 같으면(같은 Transform 또는 같은 자리) 덧붙이지 않는다.
        List<Transform> path = new List<Transform>(wps);
        Transform first = wps[0], last = wps[wps.Count - 1];
        if (last != first && (last.position - first.position).sqrMagnitude > CheckEps * CheckEps) path.Add(first);

        for (int i = 1; i < wps.Count; i++)
            if (Mathf.Abs(wps[i].position.y - first.position.y) > WaypointYTolerance)
                Warn($"순찰점 y가 다르다: [{i}] {wps[i].position.y:0.###} ≠ [0] {first.position.y:0.###} — 추격은 수평 이동이라 높이가 섞이면 뜨거나 묻힌다(ManagerAgent.cs:264).");

        Vector3 spawn;
        if (refs.managerSpawnAnchor == null)
        {
            Err("refs.managerSpawnAnchor가 null — 관리자 루트를 순찰 첫 점(팀 ResetToStart 자리, PathChaserAgent.cs:79)에 둔다.");
            spawn = first.position;
        }
        else
        {
            spawn = refs.managerSpawnAnchor.position;
            if ((spawn - first.position).sqrMagnitude > 0.05f * 0.05f)
                Warn($"managerSpawnAnchor {spawn}가 순찰 첫 점 {first.position}과 다르다 — 잡힌 뒤 ResetToStart는 첫 점으로 순간이동한다(계약 K8-1: 관리자 시작 자리 = WP_1 자리).");
        }

        GameObject agentGo = NewChild(group, ManagerName, spawn, group.rotation);
        Rigidbody rb = agentGo.AddComponent<Rigidbody>();
        rb.isKinematic = true;   // [팀 :92]
        rb.useGravity = false;   // [팀 :93]
        PathChaserAgent agent = agentGo.AddComponent<PathChaserAgent>();
        agent.waypoints = path.ToArray();
        // speed·maxWaypointIndex는 팀 기본(Activate가 patrolSpeed로 덮는다).
        EditorUtility.SetDirty(agent);

        // 시각물(콜라이더 없음 — 있으면 시야 Linecast가 자기 몸에 막힌다, 팀 :97). 첫 구간 방향을 바라보고 시작(:101) —
        // ManagerAgent.Awake가 visual.forward를 시작 방향으로 읽는다(ManagerAgent.cs:125).
        // [판정 17] 피벗 y0 → Visual 로컬 (0,+1,0). 피벗이 y0에서 어긋나면(±0.02 초과) 경고 — 공식은 그 경우에도 캡슐 밑면을 바닥에 맞춘다.
        float pivotAboveFloor = generated.InverseTransformPoint(spawn).y; // 섹터 로컬 y = 바닥 윗면 기준 [계약 K0-2]
        float lift = Mathf.Max(0f, ManagerCapsuleHalfHeight - pivotAboveFloor);
        GameObject visual = Primitive(PrimitiveType.Capsule, agentGo.transform, ManagerVisualName, new Vector3(0f, lift, 0f), Vector3.one);
        Primitive(PrimitiveType.Cube, visual.transform, ManagerNoseName, NoseLocal, NoseScale);
        Vector3 firstDir = Flat(path[1].position - path[0].position);
        if (firstDir.sqrMagnitude < 1e-6f && refs.managerSpawnAnchor != null) firstDir = Flat(refs.managerSpawnAnchor.forward);
        if (firstDir.sqrMagnitude > 1e-6f) visual.transform.rotation = Quaternion.LookRotation(firstDir.normalized, Vector3.up);
        if (Mathf.Abs(pivotAboveFloor - ManagerPivotY) > PivotTolerance)
            Warn($"[판정 17] 관리자 피벗이 바닥 위 {pivotAboveFloor:0.###} ≠ {ManagerPivotY}(±{PivotTolerance}) — Visual을 +{lift:0.##} 올렸다. 눈 높이 = 피벗 + eyeHeight(ManagerAgent.cs:49·462)라 설계 §3-8 전제(눈 0.5)가 어긋난다.");

        ManagerAgent managerAgent = agentGo.AddComponent<ManagerAgent>();
        managerAgent.agent = agent;
        managerAgent.visual = visual.transform;
        // detectDistance 10 · fieldOfView 90 · loseSeconds 3 · suspectToChaseSeconds 1 · chaseLoseSeconds 2 · alertLoseSeconds 2 ·
        // sightMask ~0 · eyeHeight 0.5 · pathClearRadius 0.3 · patrolSpeed 2 · chaseSpeed 4 · turnSpeed 360 — 전부 팀 기본값 그대로.
        EditorUtility.SetDirty(managerAgent);
        Debug.Log($"[S8_Wiring] [판정 17] 관리자 피벗 섹터 로컬 y{pivotAboveFloor:0.###} · Visual 로컬 (0,+{lift:0.##},0) · 눈 높이 y{pivotAboveFloor + managerAgent.eyeHeight:0.##}(팀 eyeHeight 기본) · waypoints {path.Count}개(끝 '{path[path.Count - 1].name}').");

        ManagerStatusIndicator indicator = agentGo.AddComponent<ManagerStatusIndicator>();
        indicator.managerAgent = managerAgent;
        indicator.renderers = new[] { visual.GetComponent<Renderer>() };   // [팀 :109] 색은 팀 기본
        EditorUtility.SetDirty(indicator);

        // 잡힘 구역: 콜라이더를 먼저 붙이고 → ManagerCatchZone(Reset이 반경 1로 덮음) → 반경 1.2 [팀 :111-116]
        GameObject catchGo = NewLocalChild(agentGo.transform, CatchName, Vector3.zero);
        SphereCollider catchCol = catchGo.AddComponent<SphereCollider>();
        catchCol.isTrigger = true;
        catchZone = catchGo.AddComponent<ManagerCatchZone>();
        catchCol.radius = CatchRadius;
        catchZone.managerAgent = managerAgent;
        EditorUtility.SetDirty(catchCol);
        EditorUtility.SetDirty(catchZone);

        // 사용 지점 = 관리자 자식, 콜라이더 없음(거리로 잰다 — ManagerUsePoint.cs:13-16) [팀 :118-121]
        GameObject useGo = NewLocalChild(agentGo.transform, UseName, Vector3.zero);
        usePoint = useGo.AddComponent<ManagerUsePoint>();
        usePoint.station = station;
        if (station == null) Err("혼합대(MixingStation)가 없어 ManagerUsePoint.station이 비었다 — 완성물 사용 불가.");
        // onUseRequested에는 영구 리스너를 걸지 않는다 — 컨트롤러가 OnEnable→Bind에서 런타임 구독(ManagerChapterController.cs:121).
        EditorUtility.SetDirty(usePoint);

        return managerAgent;
    }

    // ═════════════════════════ 열쇠·열쇠 문 ═════════════════════════

    /// <summary>[팀 :123-128] 수면 확정 뒤 1회 나타나는 열쇠(Start에서 숨김, ManagerKeyPoint.cs:45). 자리 = 이름 규약 앵커
    /// ANCH_S8_Key의 월드 위치 [판정 19 · 계약 K8-0]. 앵커가 없으면 LogError 후 열쇠를 만들지 않고 null을 돌려준다 —
    /// 대체 좌표로 조용히 메우지 않는다(오류를 가림, 계약 확인 요청 5). null이면 열쇠 문·컨트롤러의 keyPoint가 비고,
    /// 팀 코드가 null을 견딘다(ManagerKeyDoor.cs:63 잠긴 문 유지, ManagerChapterController.cs:224 Reveal 생략).</summary>
    private static ManagerKeyPoint BuildKeyPoint(Transform group, Transform generated, S8_Refs refs, RoleAssignmentManager roles)
    {
        Transform keyAnchor = FindDeep(generated, KeyAnchorName);
        if (keyAnchor == null)
        {
            Err($"이름 규약 앵커 '{KeyAnchorName}'가 generated 아래에 없다 — 열쇠(ManagerKeyPoint)를 만들지 않는다. 출구 문이 열리지 않는다 [판정 19 · 계약 K8-0].");
            return null;
        }
        Vector3 pos = keyAnchor.position;

        // 자리 검사(경고만): 서고 안쪽(archiveBounds, 섹터 로컬) — 탈출 공간·섹터 밖이면 문을 열기 전에 집을 수 없다.
        Bounds ab = refs.archiveBounds;
        if (ab.size.x > 1f && ab.size.z > 1f)
        {
            Vector3 kl = generated.InverseTransformPoint(pos);
            if (kl.x < ab.min.x - CheckEps || kl.x > ab.max.x + CheckEps || kl.z < ab.min.z - CheckEps || kl.z > ab.max.z + CheckEps)
                Warn($"'{KeyAnchorName}' 섹터 로컬 {kl}가 archiveBounds {ab.min}~{ab.max}의 XZ 밖이다 — 출구 문 안쪽에서 집을 수 없을 수 있다.");
        }

        GameObject keyGo = NewChild(group, KeyPointName, pos, group.rotation);
        ManagerKeyPoint key = keyGo.AddComponent<ManagerKeyPoint>();
        key.manager = roles;
        key.visual = Primitive(PrimitiveType.Cube, keyGo.transform, KeyVisualName, Vector3.zero, KeyVisualScale);
        // takeKey LeftControl · pickupRadius 1.5는 팀 기본 그대로.
        EditorUtility.SetDirty(key);
        return key;
    }

    /// <summary>[팀 :137-140] 열쇠 문 상호작용 지점. door = refs.exitDoor(B의 Map4Build.Door — Rigidbody+doorPhysics 이미 있음,
    /// 서고 출구 벽 z131.55~131.95 [계약 K8-0·K8-1]). 자리 = exitDoorAnchor + 0.5(팀 시험 방 높이 :137, 초안 §4 'W 값 우선') →
    /// 초안 기준 섹터 로컬 (0,0.5,130). 문 값(doorTargetYOffset 4.5·doorSpeed)은 B 몫이라 건드리지 않는다. useRadius 2.5 팀 기본.
    /// onOpened는 비워 둔다 — 받을 맵 완료 처리가 없다(mismatchReports) [계약 K8-1].</summary>
    private static void BuildKeyDoor(Transform group, S8_Refs refs, RoleAssignmentManager roles, ManagerKeyPoint keyPoint)
    {
        if (refs.exitDoorAnchor == null)
        {
            Err("refs.exitDoorAnchor가 null — 열쇠 문(ManagerKeyDoor)을 건너뛴다. 출구 문이 열리지 않는다.");
            return;
        }
        if (refs.exitDoor == null)
        {
            Err("refs.exitDoor가 null — 열쇠 문(ManagerKeyDoor)을 건너뛴다. 출구 문이 열리지 않는다.");
            return;
        }

        Vector3 pos = refs.exitDoorAnchor.position + Vector3.up * KeyDoorHeight;
        GameObject go = NewChild(group, KeyDoorName, pos, FlatRotation(refs.exitDoorAnchor.forward, group));
        ManagerKeyDoor keyDoor = go.AddComponent<ManagerKeyDoor>();
        keyDoor.manager = roles;
        keyDoor.keyPoint = keyPoint;
        keyDoor.door = refs.exitDoor;
        // useKey LeftControl · useRadius 2.5 · onOpened(비움)은 팀 기본 그대로.
        EditorUtility.SetDirty(keyDoor);

        // 사용 지점 ↔ 문 솔리드 면 거리 ≤ useRadius(경고만)
        BoxCollider solid = SolidBoxOf(refs.exitDoor.gameObject);
        if (solid == null) Warn("출구 문에 솔리드 BoxCollider가 없다 — 사용 거리 검사를 건너뛴다.");
        else
        {
            float d = Vector3.Distance(pos, WorldBoundsOf(solid).ClosestPoint(pos));
            if (d > keyDoor.useRadius + CheckEps)
                Warn($"열쇠 문 사용 지점 ↔ 문 면 {d:0.##} > useRadius {keyDoor.useRadius} — 문 앞에서 Ctrl이 안 먹을 수 있다(참가자 피벗 기준 3D 거리).");
        }
    }

    // ═════════════════════════ 시작 지점 ═════════════════════════

    /// <summary>[팀 :142-150] 참가자별 SectionSafePoint 3개(바닥 윗면 — RespawnController가 도형별 바닥 오프셋을 얹는다).
    /// refs.startAnchor 위치를 가운데로, 앵커 right 축으로 −2.5 / 0 / +2.5 [판정 19 · 계약 K8-0 · 팀 간격 :146] →
    /// 초안 기준 섹터 로컬 (−2.5,0,5)·(0,0,5)·(2.5,0,5). 팀과 같이 AddComponent(시험 방 방식) — 메뉴 Create Section Safe Point와
    /// 결과 같음(S8_팀API.md §6). 잡히면 컨트롤러가 이 3곳으로 전원 복귀시킨다(팀 기본 동작) [계약 K8-1].</summary>
    private static SectionSafePoint[] BuildStartPoints(Transform group, Transform startAnchor)
    {
        if (startAnchor == null)
        {
            Err("refs.startAnchor가 null — CH8 시작 지점 0개. 잡히면 팀 폴백(공용 체크포인트)으로 복귀하고 자동 재시작이 안 될 수 있다.");
            return new SectionSafePoint[0];
        }

        Vector3 right = Flat(startAnchor.right);
        if (right.sqrMagnitude < 1e-6f) right = Flat(group.right);
        right.Normalize();
        Quaternion rot = FlatRotation(startAnchor.forward, group);

        SectionSafePoint[] points = new SectionSafePoint[StartPointCount];
        for (int i = 0; i < StartPointCount; i++)
        {
            float off = (i - (StartPointCount - 1) * 0.5f) * StartPointSpacing; // −2.5, 0, +2.5
            string name = StartPointPrefix + i;
            GameObject go = NewChild(group, name, startAnchor.position + right * off, rot);
            SectionSafePoint p = go.AddComponent<SectionSafePoint>();
            p.sectionId = name; // [팀 :148]
            // occupancyRadius 0.6 · backupPoints 없음 · counter 없음 — 팀 시험 방과 같다.
            EditorUtility.SetDirty(p);
            points[i] = p;
        }
        return points;
    }

    // ═════════════════════════ 수동 R 체크포인트 ═════════════════════════

    /// <summary>[판정 19 · 계약 K8-0] 팀 RespawnZone 1개 — S8 입구 대기실. 없으면 S8 안에서 R이 S7 체크포인트로 보낸다
    /// (S8_팀API.md §8-11). 팀 생성 함수 RespawnMenuItem.CreateCheckpoint()(public static, RespawnMenuItem.cs:27-28)를 부르고,
    /// 선택물에 RespawnZone이 있는지 확인한 뒤 S8_Gimmicks 아래로 옮긴다(S1_Builder.cs:196-201 선례 — 배치 모드에서 선택물을
    /// 못 받으면 호출 전후 목록 비교로 찾는다, S6_Wiring 선례). 그다음 배치 변환과 BoxCollider 인스펙터 값(size·center)만 넣는다
    /// [해석 K0-3 판정 11 — 인스펙터 값]. 막대·깃발·RespawnZone 필드는 팀이 만든 그대로.
    /// 루트 = startAnchor − Flat(forward) × 1.0, 회전 없음(축 정렬 전제, RespawnZone.cs:186-187). 값은 초안 [제안] (추정).</summary>
    private static RespawnZone BuildCheckpoint(Transform group, Transform generated, Transform startAnchor)
    {
        if (startAnchor == null)
        {
            Err("refs.startAnchor가 null — 수동 R 체크포인트(RespawnZone)를 만들지 않는다. S8 안에서 R이 S7 체크포인트로 간다.");
            return null;
        }

        Vector3 back = Flat(startAnchor.forward);
        if (back.sqrMagnitude < 1e-6f)
        {
            back = Flat(generated.forward);
            Warn("startAnchor.forward가 수직·0이라 수동 R 체크포인트 루트를 섹터 +Z 기준으로 뺐다.");
        }
        back.Normalize();
        Vector3 root = startAnchor.position - back * CheckpointBackOffset;

        if (Quaternion.Angle(generated.rotation, Quaternion.identity) > 0.01f)
            Warn($"섹터가 월드 축과 어긋나 있다(회전 {generated.rotation.eulerAngles}) — RespawnZone은 월드 AABB를 쓰므로(RespawnZone.cs:186-195) 구역을 월드 축 정렬로 둔다.");

        // 팀 생성 함수 호출 → 선택물 확인(S1 선례) → 없으면 전후 목록 비교(S6 선례)
        HashSet<RespawnZone> before = new HashSet<RespawnZone>(Object.FindObjectsOfType<RespawnZone>(true));
        Selection.activeGameObject = null;
        RespawnMenuItem.CreateCheckpoint();
        GameObject sel = Selection.activeGameObject;
        RespawnZone zone = sel != null ? sel.GetComponent<RespawnZone>() : null;
        if (zone != null && before.Contains(zone)) zone = null;
        if (zone == null)
        {
            foreach (RespawnZone z in Object.FindObjectsOfType<RespawnZone>(true))
                if (!before.Contains(z)) { zone = z; break; }
            if (zone != null)
                Warn($"팀 '{CheckpointMenuPath}' 생성물을 선택물로 받지 못해 호출 전후 목록 비교로 찾았다('{zone.name}').");
        }
        if (zone == null)
        {
            Err($"팀 '{CheckpointMenuPath}'(RespawnMenuItem.CreateCheckpoint) 생성물에서 RespawnZone을 찾지 못했다 — 수동 R 체크포인트 없음.");
            return null;
        }

        GameObject go = zone.gameObject;
        go.transform.SetParent(group, true);  // Adopt — 씬 루트 잔여 0 [계약 K0-3]
        go.name = CheckpointName;
        go.transform.SetPositionAndRotation(root, Quaternion.identity); // 축 정렬 [팀 RespawnZone.cs:186-187]

        BoxCollider box = go.GetComponent<BoxCollider>();
        if (box == null)
        {
            Err($"'{CheckpointName}'에 BoxCollider가 없다 — 팀 생성 구조가 바뀌었다(RespawnMenuItem.cs:36-39).");
            return zone;
        }
        // isTrigger true는 팀이 넣은 값 그대로(RespawnMenuItem.cs:37). size·center만 초안 값으로(팀 높이 18 대신 3 — C17).
        box.center = CheckpointCenter;
        box.size = CheckpointSize;
        EditorUtility.SetDirty(box);
        EditorUtility.SetDirty(go);
        return zone;
    }

    // ═════════════════════════ 컨트롤러 ═════════════════════════

    /// <summary>[팀 :152-166] 컨트롤러 + 자동 시작 구역. 구역 = 서고 안쪽(refs.archiveBounds, 섹터 로컬, z ≤131.5) x·z 전체,
    /// 밑면 바닥 − 1 · 높이 6 [팀]. 초안 기준 중심 (0,2,66)·크기 (87,6,131) [계산 초안 §6-7]. S7 연결 통로(z &lt; 0)와
    /// 탈출 공간(z132~144)은 넣지 않는다(미리 시작 방지, S8_팀API.md §8-8) [판정 18 · 계약 K8-0].
    /// respawnController는 비운다(Start/ReturnAllToStart에서 FindObjectOfType). useDistance 3·skipSurveillance false 팀 기본.</summary>
    private static ManagerChapterController BuildController(Transform group, Transform generated, S8_Refs refs,
        RoleAssignmentManager roles, MixingStation station, ManagerUsePoint usePoint, ManagerAgent managerAgent,
        ManagerKeyPoint keyPoint, SectionSafePoint[] startPoints)
    {
        GameObject ctrlGo = NewChild(group, ControllerName, generated.position, generated.rotation);
        ManagerChapterController controller = ctrlGo.AddComponent<ManagerChapterController>();
        controller.manager = roles;
        controller.station = station;
        controller.usePoint = usePoint;
        controller.managerAgent = managerAgent;
        controller.keyPoint = keyPoint;
        if (managerAgent == null) Err("관리자가 없어 ManagerChapterController.managerAgent가 비었다 — 실행 시 컨트롤러가 스스로 꺼진다(ManagerChapterController.cs:110-114).");

        Bounds ab = refs.archiveBounds;
        Vector3 centerLocal, size;
        if (ab.size.x > 1f && ab.size.z > 1f)
        {
            centerLocal = new Vector3(ab.center.x, StartZoneBottomY + StartZoneHeight * 0.5f, ab.center.z);
            size = new Vector3(ab.size.x, StartZoneHeight, ab.size.z);
        }
        else
        {
            Err($"refs.archiveBounds가 비었다({ab.size}) — 시작 구역을 초안 계산값(중심 {StartZoneCenterFallback}, 크기 {StartZoneSizeFallback})으로 둔다.");
            centerLocal = StartZoneCenterFallback;
            size = StartZoneSizeFallback;
        }
        GameObject zone = NewChild(group, StartZoneName, generated.TransformPoint(centerLocal), generated.rotation);
        controller.startZone = zone.transform;
        controller.startZoneSize = size;
        controller.startPoints = startPoints;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    // ═════════════════════════ 자기 검사 (경고만) ═════════════════════════

    private static void SelfCheck(Transform generated, S8_Refs refs, ManagerAgent managerAgent, ManagerUsePoint usePoint,
        ManagerChapterController controller, SectionSafePoint[] startPoints, RoleSlot monitorSlot, RespawnZone checkpoint)
    {
        // (a) 영구 배선 0 — 특히 onUseRequested(중복 호출 방지)
        if (usePoint != null && usePoint.onUseRequested != null && usePoint.onUseRequested.GetPersistentEventCount() > 0)
            Err("ManagerUsePoint.onUseRequested에 영구 리스너가 있다 — 컨트롤러 런타임 구독과 겹쳐 HandleUse가 두 번 불린다.");

        // (b) 시작 지점이 자동 시작 구역 안(아니면 잡힌 뒤 자동 재시작이 안 된다, S8_팀API.md §8-8)
        if (controller != null && startPoints != null)
            foreach (SectionSafePoint p in startPoints)
                if (p != null && !controller.InStartZone(p.transform.position))
                    Err($"시작 지점 '{p.name}'이 자동 시작 구역 밖이다 — 복귀 뒤 자동 재시작이 안 된다.");

        // (b2) 수동 R 체크포인트 [판정 19 · 초안 C17]: 트리거인지, 서고 안쪽·천장 아래인지(구역 윗면 > 천장이면 중앙 레이가 천장
        //      윗면을 바닥으로 잡는다), 시작점 3개를 품는지. 섹터 로컬 AABB로 본다.
        if (checkpoint != null)
        {
            BoxCollider cb = checkpoint.GetComponent<BoxCollider>();
            if (cb != null)
            {
                if (!cb.isTrigger) Err($"'{checkpoint.name}' BoxCollider가 트리거가 아니다 — 팀 RespawnZone이 Awake에서 거부한다(RespawnZone.cs:79-80).");
                Bounds zl = ToLocalAabb(generated, WorldBoundsOf(cb));
                Bounds ab = refs.archiveBounds;
                if (ab.size.x > 1f && ab.size.z > 1f)
                {
                    if (zl.min.x < ab.min.x - CheckEps || zl.max.x > ab.max.x + CheckEps || zl.min.z < ab.min.z - CheckEps || zl.max.z > ab.max.z + CheckEps)
                        Warn($"수동 R 구역 {zl.min}~{zl.max}가 archiveBounds {ab.min}~{ab.max}의 XZ 밖으로 나간다.");
                    if (ab.size.y > 1f && zl.max.y > ab.max.y + CheckEps)
                        Warn($"수동 R 구역 윗면 y{zl.max.y:0.##} > 서고 안쪽 윗면 y{ab.max.y:0.##} — 중앙 레이가 천장 윗면을 바닥으로 잡아 R이 천장 위로 보낼 수 있다(초안 C17).");
                }
                if (startPoints != null)
                    foreach (SectionSafePoint p in startPoints)
                    {
                        if (p == null) continue;
                        Vector3 pl = generated.InverseTransformPoint(p.transform.position);
                        if (pl.x < zl.min.x - CheckEps || pl.x > zl.max.x + CheckEps || pl.y < zl.min.y - CheckEps || pl.y > zl.max.y + CheckEps ||
                            pl.z < zl.min.z - CheckEps || pl.z > zl.max.z + CheckEps)
                            Warn($"시작 지점 '{p.name}' {pl}가 수동 R 구역 {zl.min}~{zl.max} 밖이다(초안 C17: 시작점 3개를 품는다).");
                    }
            }
        }

        // (c) 책 ↔ 혼합 간격 ≥ 2 [확정 §3-9], 팀 트리거 비겹침(중심 간격 ≥ 3.5) [팀 :250]
        if (refs.roleBookAnchor != null && refs.roleMixerAnchor != null)
        {
            float d = Flat(refs.roleBookAnchor.position - refs.roleMixerAnchor.position).magnitude;
            if (d < BookMixerMin - CheckEps)
                Err($"책 ↔ 혼합 간격 {d:0.##} < {BookMixerMin} [확정 §3-9].");
            else if (d < RoleTriggerSize.x - CheckEps)
                Warn($"책 ↔ 혼합 간격 {d:0.##} < 팀 트리거 폭 {RoleTriggerSize.x} — 두 트리거가 겹쳐 E가 한쪽만 먹을 수 있다.");
        }

        // (d) 순찰로와의 거리 — 순찰로가 있을 때만(닫힌 폴리라인, 수평 거리)
        List<Transform> wps = refs.patrolWaypoints;
        if (managerAgent == null || wps == null || wps.Count < 2 || wps.Contains(null)) return;
        float detect = managerAgent.detectDistance;             // 팀 기본 10(ManagerAgent.cs:35)
        float catchReach = CatchRadius + ShapeRadius;           // [계산] 1.2 + 0.5

        CheckRoute("roleBookAnchor", refs.roleBookAnchor, wps, catchReach, true);
        CheckRoute("roleMixerAnchor", refs.roleMixerAnchor, wps, catchReach, true);
        if (monitorSlot != null)
        {
            float d = RouteDistance(monitorSlot.transform.position, wps);
            if (d < detect - CheckEps)
                Warn($"CCTV 콘솔 ↔ 순찰로 {d:0.##} < 감지 거리 {detect} — 보는 동안 이동이 잠겨(RoleAssignmentManager.cs:149-164) 들키면 못 피한다.");
        }
        if (startPoints != null)
            foreach (SectionSafePoint p in startPoints)
                if (p != null)
                {
                    float d = RouteDistance(p.transform.position, wps);
                    if (d < detect - CheckEps)
                        Warn($"시작 지점 '{p.name}' ↔ 순찰로 {d:0.##} < 감지 거리 {detect} — 복귀 직후 다시 들킬 수 있다(팀 시험 방 원칙 :11).");
                }
    }

    /// <summary>역할 앵커 ↔ 순찰로. 앵커 한 점 기준 잡힘 거리(1.7) 안이면 Error, 트리거 끝에 몸만 걸친 칸(앵커 − 트리거 반폭
    /// − 도형 반지름)이 잡힘 거리 안이면 Warning(초안 C9d). [판정 20] 앵커 x ±4.0이면 순찰선 4.0 − 1.75 − 0.5 = 1.75 ≥ 1.7 → 경고 0.</summary>
    private static void CheckRoute(string field, Transform anchor, List<Transform> wps, float catchReach, bool triggerEdge)
    {
        if (anchor == null) return;
        float d = RouteDistance(anchor.position, wps);
        if (d < catchReach - CheckEps)
        {
            Err($"{field} ↔ 순찰로 {d:0.##} < 잡힘 거리 {catchReach:0.##} — 역할 자리에 서 있기만 해도 잡힌다.");
            return;
        }
        if (!triggerEdge) return;
        float edge = d - RoleTriggerSize.x * 0.5f - ShapeRadius;
        if (edge < catchReach - CheckEps)
            Warn($"{field}: 트리거 끝에 몸만 걸친 칸이 순찰로에서 {edge:0.##}(< 잡힘 {catchReach:0.##}) — 초안 C9d, [판정 20] 앵커 x ±4.0 기대.");
    }

    // ═════════════════════════ 헬퍼 ═════════════════════════

    private static GameObject NewChild(Transform parent, string name, Vector3 worldPos, Quaternion worldRot)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(worldPos, worldRot);
        return go;
    }

    private static GameObject NewLocalChild(Transform parent, string name, Vector3 localPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }

    /// <summary>콜라이더 없는 시각용 프리미티브 [팀 Ch8TestRoomMenuItem.Primitive :219-230]. 팀은 MPB로 색만 칠하는데 그 색은
    /// 저장되지 않으므로(:232-233) 칠하지 않는다 — 머티리얼은 프리미티브 기본 그대로(팀 기믹 렌더러는 L·W 모두 건드리지 않음).</summary>
    private static GameObject Primitive(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 scale)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Collider col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = scale;
        return go;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>수평 forward로 도는 회전(up = 월드 +Y, 계약 K0-3). forward가 수직·0이면 fallback 회전.</summary>
    private static Quaternion FlatRotation(Vector3 forward, Transform fallback)
    {
        Vector3 f = Flat(forward);
        return f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f.normalized, Vector3.up) : fallback.rotation;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static BoxCollider SolidBoxOf(GameObject go)
    {
        foreach (BoxCollider b in go.GetComponents<BoxCollider>())
            if (b != null && !b.isTrigger) return b;
        return null;
    }

    /// <summary>BoxCollider의 월드 AABB — 물리 동기화 없이 변환만으로 계산(편집 중 새로 만든 콜라이더의 bounds에 기대지 않는다).</summary>
    private static Bounds WorldBoundsOf(BoxCollider b)
    {
        Transform t = b.transform;
        Vector3 e = b.size * 0.5f;
        Bounds r = new Bounds(t.TransformPoint(b.center), Vector3.zero);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    r.Encapsulate(t.TransformPoint(b.center + new Vector3(sx * e.x, sy * e.y, sz * e.z)));
        return r;
    }

    /// <summary>월드 AABB를 섹터 로컬 AABB로(8 꼭짓점 변환). 섹터는 무회전이 정상이라 결과는 같은 상자다.</summary>
    private static Bounds ToLocalAabb(Transform generated, Bounds world)
    {
        Vector3 mn = world.min, mx = world.max;
        Bounds r = new Bounds(generated.InverseTransformPoint(mn), Vector3.zero);
        for (int i = 1; i < 8; i++)
        {
            Vector3 c = new Vector3((i & 1) != 0 ? mx.x : mn.x, (i & 2) != 0 ? mx.y : mn.y, (i & 4) != 0 ? mx.z : mn.z);
            r.Encapsulate(generated.InverseTransformPoint(c));
        }
        return r;
    }

    /// <summary>점 ↔ 닫힌 순찰 폴리라인(마지막 → 첫 점 포함) 수평 최소 거리.</summary>
    private static float RouteDistance(Vector3 p, List<Transform> wps)
    {
        float best = float.MaxValue;
        for (int i = 0; i < wps.Count; i++)
        {
            Vector3 a = Flat(wps[i].position);
            Vector3 b = Flat(wps[(i + 1) % wps.Count].position);
            best = Mathf.Min(best, DistPointSegment(Flat(p), a, b));
        }
        return best;
    }

    private static float DistPointSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        float t = len2 > 1e-9f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }

    // ── 씬 루트 잔여 0 (S1_Builder.ReportStrayRoots/Adopt 방식) ──

    private static HashSet<GameObject> SnapshotRoots(Transform generated)
    {
        HashSet<GameObject> set = new HashSet<GameObject>();
        foreach (Scene s in ScenesOf(generated))
            foreach (GameObject r in s.GetRootGameObjects()) set.Add(r);
        return set;
    }

    private static IEnumerable<Scene> ScenesOf(Transform generated)
    {
        Scene own = generated.gameObject.scene;
        yield return own;
        Scene active = SceneManager.GetActiveScene();
        if (active.IsValid() && active != own) yield return active;
    }

    /// <summary>팀 생성 함수는 수동 R 체크포인트(BuildCheckpoint가 바로 옮김) 하나만 부르므로 원칙적으로 새 루트는 0이다.
    /// 그래도 생기면(예외로 옮기기 전에 끊긴 경우 등) 그룹 아래로 옮기고(Adopt) 경고한다 — 루트에 남으면 Generated 재생성 때
    /// 지워지지 않아 중복된다 [계약 K0-3 씬 루트 잔여 0].</summary>
    private static void AdoptStrayRoots(Transform generated, HashSet<GameObject> before, Transform group)
    {
        if (generated == null) return;
        List<GameObject> strays = new List<GameObject>();
        foreach (Scene s in ScenesOf(generated))
            foreach (GameObject r in s.GetRootGameObjects())
                if (!before.Contains(r)) strays.Add(r);
        foreach (GameObject r in strays)
        {
            if (group != null)
            {
                r.transform.SetParent(group, true);
                Warn($"씬 루트에 새로 생긴 '{r.name}'을 {GroupName} 아래로 옮겼다.");
            }
            else Err($"씬 루트에 남은 오브젝트 '{r.name}' — 그룹이 없어 옮기지 못했다(재생성 시 중복).");
        }
    }

    private static void Err(string msg)
    {
        errorCount++;
        Debug.LogError("[S8_Wiring] " + msg);
    }

    private static void Warn(string msg)
    {
        warningCount++;
        Debug.LogWarning("[S8_Wiring] " + msg);
    }
}
#endif
