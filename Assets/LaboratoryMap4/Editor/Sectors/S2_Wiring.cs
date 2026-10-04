#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 섹터2 "격리실" 팀 기믹 배선(R2 계약 C1-0·C1-5 S2행·C3 S2행, 지시서 진행/지시서/R2/S2-W.md).
/// S2_Builder.Build가 지형·문·블록·레버를 만들고 Refs 검증(C1-1)을 마친 뒤 ③ 단계에서 1회 부른다.
/// 좌표 원천은 초안 진행/초안/S2_배치초안.md(2차 반영판) §2-1·§2-4·§2-5·§2-7·§3 팀 기믹 배치표뿐이다.
///
/// [만드는 것 — 전부 generated/S2_Isolation 그룹 1개 아래, 계약 C3 이름 그대로]
/// - S2_IsoController: 팀 IsolationRescueController + IsolationTimer(한 GO). 생성 메뉴가 없어 AddComponent [팀 S2-R §5-1].
///   인스펙터 값은 팀 기본 그대로 — stages 비움(퍼즐 [추후], 비어 있으면 격리 시작 거부 §7-1), timer null(같은 GO 탐색,
///   IsolationRescueController.cs:134-141), IsolationTimer.duration 60(팀 기본, PRD "미정" §7-8). 대입하지 않고 확인만 한다.
///
/// [배선 — 영구 1개뿐] onSuccess → refs.exitS4Door(DOOR_S2_ExitS4).SetPadPressed(true)
/// (UnityEventTools Bool 영구 리스너, 같은 씬 — S1_Builder의 출구 문 배선과 같은 방식) [초안 §3].
/// 그 밖 배선 0: onTimedOut 0(S3행은 문 없음 — 판정 1), onCaptureConfirmed·onCaptureCancelled·onAborted·onRestarted·
/// onFinalReleaseAwaiting 0(숨은 벽은 정지물 — 판정 2·§7-12). 반환값 있는 RequestCapture·SubmitLever·TryRelease는
/// 영구 배선 불가라 시도하지 않는다(S2-R §5-4). 복귀 장치(SectionHitCounter·SectionSafePoint·Lab_SectionRespawnBridge·
/// OutOfBoundsVolume·RespawnZone)는 만들지 않는다 — 계약 C1-5 "S2에는 복귀 장치 없음(추가 금지)", S2에는 피격 기믹도 없다.
///
/// [하지 않는 것 — S2-B(빌더) 몫, 계약 C1-5] 딱딱블록 39·레버 3·S4행 문·S3행 개구 표지·숨은 벽의 생성·이동·인스펙터 값.
/// 여기서는 **읽기 전용 확인**만 하고, 다르면 값을 고치지 않고 Debug.LogError(보고서 reviewRequests → S2-B).
///
/// 좌표는 섹터 로컬(generated 기준). 출처: [확정]=설계서 사용자 확정, [팀]=팀 코드, [제안]=초안 제안값, [계산]=계산값,
/// [추정]=실측 필요, [판정]=컨트롤타워·사용자 판정, [계약]=R2 계약·지시서에서 정한 구현 규약.
/// Refs는 읽기만 한다(필드 대입 없음, 계약 C1-0). 선택 항목이 비면 LogError 후 그 항목만 건너뛴다. 예외는 밖으로 던지지 않는다.
/// </summary>
public static class S2_Wiring
{
    // ── 이름 [계약 C3 S2행·C2-2] ──
    private const string GroupName = "S2_Isolation";
    private const string ControllerName = "S2_IsoController";
    private const string ExitS4DoorName = "DOOR_S2_ExitS4";
    private const string ExitS3OpeningName = "TEMP_S2_ExitS3_Open";
    private const string IsoHiddenWallName = "GEO_S2_Wall_IsoHidden";
    private const string GimmickRootName = "S2_Gimmicks";
    private const string DoorFrameName = "VIS_NoCollide_DoorFrame";   // [헬퍼 Map4Build.cs:207] 빌더가 삭제해야 하는 문틀
    private const string PivotName = "lever_pivot";                    // [팀] DoorSystemMenuItem.cs:165
    private const string HeadName = "lever_head";                      // [팀] DoorSystemMenuItem.cs:172
    private const string BodySuffix = "_Body";                         // [제안 초안 §2-4·§3 — 확인 대기 3] 받침대 = 부모 이름 + _Body
    private static readonly string[] MenuLeftoverRootNames = { "lever_body", "lever_pivot", "SnapBlock" }; // [팀] 메뉴 생성 이름 — 씬 루트 잔류 0(1차 C3)

    // ── 격리 컨트롤러 ──
    private static readonly Vector3 ControllerLocal = new Vector3(-14f, 1f, 42f); // [초안 §3] 위치 무관(콜라이더 없음)
    private const float TimerDurationTeam = 60f;                       // [팀] IsolationTimer.cs:25 기본값(PRD "미정", §7-8) — 대입하지 않는다

    // ── 확인 기대값: S4행 문 [초안 §2-1 행 38·§3] ──
    private static readonly Vector3 DoorMin = new Vector3(29.1f, 0.3f, 74.5f);
    private static readonly Vector3 DoorMax = new Vector3(29.5f, 3.8f, 79.5f);
    private const float DoorOpenHeight = 3.7f;                         // [제안 초안 §2-1] doorTargetYOffset(+3.7 → 열림 y4.0~7.5)
    private const float DoorSpeedTeam = 2f;                            // [팀] doorPhysics.cs:8 기본값

    // ── 확인 기대값: 숨은 벽(정지) [판정 2·초안 §2-1 행 17] ──
    private static readonly Vector3 IsoWallMin = new Vector3(-14.2f, 4.3f, 30.5f);
    private static readonly Vector3 IsoWallMax = new Vector3(-13.8f, 8.3f, 53.5f);

    // ── 확인 기대값: S3행 개구 표지 [판정 1·초안 §2-7] ──
    private static readonly Vector3 ExitS3Local = new Vector3(0f, 0f, 84f);

    // ── 확인 기대값: 레버 3 [계산 초안 §2-4]·[팀 메뉴값 §2-4] ──
    private static readonly string[] LeverNames = { "S2_Lever1_Trap", "S2_Lever2", "S2_Lever3" };   // [계약 C1-1·C2-2] 순서
    private static readonly Vector3[] LeverLocal =
    {
        new Vector3(-37.1038f, 0.485f, 42.0f),
        new Vector3(37.1038f, 0.485f, 42.0f),
        new Vector3(20.0f, 0.485f, 0.8962f),
    };
    private static readonly float[] LeverYaw = { 180f, 0f, 90f };      // [계산 초안 §2-4]
    private const float PivotLocalYaw = -45f;                          // [팀] DoorSystemMenuItem.cs:33 LeverClosedAngleY
    private static readonly Vector3 PivotLocalScaleTeam = new Vector3(0.21565f, 0.110621125f, 0.22799f); // [팀] DoorSystemMenuItem.cs:29
    // [2차판정 10 — R3, 컨트롤타워 M3R1 정정] 받침대를 S2_Builder(LeverBodyLocalScale :97)와 같은 값으로 대조한다 — 높이를 바닥 0 ~ 팀 pivot
    // 아랫면(0.4296894)으로 줄여 팀 pivot·head와의 관통 6건을 없앤 판. 옛 값(0.415 / 1.8)은 초안 §2-4 원안이라 M3R1 Generate에서 레버 3개 '불일치' 오류를 냈다.
    private static readonly Vector3 BodyLocalPos = new Vector3(0.2f, -0.2701553f, 0.4338f);    // [2차판정 10 · S2_Builder 받침대 로컬 위치]
    private static readonly Vector3 BodyLocalScale = new Vector3(1.3924f, 0.4296894f, 2.4055f); // [2차판정 10 · S2_Builder LeverBodyLocalScale]
    // LeverHead 메뉴값 [팀 DoorSystemMenuItem.cs:41-46]
    private const float LeverRotateSpeed = 3f, LeverMaxAngle = 45f, LeverNormalSmooth = 5f;
    private const float LeverReturnDelay = 1f, LeverReturnSpeed = 0.1f, LeverPushExitGrace = 0.3f;

    // ── 확인 기대값: 딱딱블록 39 [제안 초안 §2-5] — 순서 = 계약 C1-1 ──
    private static readonly Vector3[] ScatterCenters =
    {
        new Vector3(-9f, 0.5f, 6f), new Vector3(9f, 0.5f, 4f), new Vector3(-3f, 0.5f, 13f), new Vector3(11f, 0.5f, 13f),
        new Vector3(-16f, 0.5f, 20f), new Vector3(18f, 0.5f, 19f), new Vector3(-2f, 0.5f, 30f), new Vector3(9f, 0.5f, 36f),
        new Vector3(-5f, 0.5f, 62f), new Vector3(10f, 0.5f, 66f), new Vector3(19f, 0.5f, 58f), new Vector3(-13f, 0.5f, 74f),
        new Vector3(-26f, 0.5f, 35f), new Vector3(-24f, 0.5f, 49f), new Vector3(-20f, 0.5f, 42f),
    };
    private const float BlockMassTeam = 1f;                            // [팀] SnapBlockMenuItem.cs:25
    private const int ExpectedBlocks = 39;                             // [계약 C1-1] Pile 24 + Scatter 15

    private const float Tol = 0.02f;                                   // [확정 00_기반 F1 판정 11] 좌표 ±0.02
    private const float AngleTol = 0.1f;                               // [계약] 회전 확인 허용(도)
    private const string Tag = "[S2_Wiring]";

    /// <summary>계약 C1-0. 예외를 밖으로 던지지 않는다(항목별 try). 지형·기믹은 지우지도 고치지도 않는다.</summary>
    public static void Wire(Transform generated, S2_Refs refs)
    {
        if (generated == null) { Debug.LogError($"{Tag} generated가 null이다 — 격리 컨트롤러를 만들지 않는다."); return; }
        if (refs == null) { Debug.LogError($"{Tag} refs가 null이다 — 격리 컨트롤러를 만들지 않는다.", generated); return; }
        if (refs.generated != null && refs.generated != generated)
            Debug.LogError($"{Tag} refs.generated '{refs.generated.name}' ≠ 인자 generated '{generated.name}' — 인자 기준으로 진행한다.", generated);

        Transform group;
        try { group = NewGroup(generated); }
        catch (System.Exception e) { Debug.LogError($"{Tag} 그룹 생성 예외 — 중단: {e}", generated); return; }

        IsolationRescueController ctrl = null;
        bool wired = false;
        Try(ControllerName, () => ctrl = BuildController(group));
        Try("onSuccess 배선", () => wired = WireSuccess(ctrl, refs.exitS4Door));
        int ctrlBad = 0;
        Try("컨트롤러 값·배선 확인", () => ctrlBad = VerifyController(ctrl, refs.exitS4Door));

        // 읽기 전용 확인(S2-B 몫 값) — 다르면 LogError만.
        int doorBad = 0, openingBad = 0, wallBad = 0, leverBad = 0, blockBad = 0, forbidden = 0;
        Try("S4행 문 확인", () => doorBad = VerifyExitS4Door(generated, refs.exitS4Door));
        Try("S3행 개구 표지 확인", () => openingBad = VerifyExitS3Opening(generated, refs.exitS3Opening));
        Try("숨은 벽 확인", () => wallBad = VerifyIsoHiddenWall(generated, refs.isoHiddenWall));
        Try("레버 확인", () => leverBad = VerifyLevers(generated, refs));
        Try("딱딱블록 확인", () => blockBad = VerifyBlocks(generated, refs));
        Try("금지 부품 확인", () => forbidden = VerifyForbidden(generated));
        Try("그룹 확인", () => VerifyGroup(generated, group));

        string summary = $"{Tag} 생성 {(ctrl != null ? "1/1" : "실패")} — {ControllerName} {Ok(ctrl)}; 영구 배선 onSuccess→SetPadPressed(true) {(wired ? "1/1" : "0/1")}; " +
                         $"확인 불일치 — 컨트롤러 {ctrlBad}, S4행 문 {doorBad}, S3행 개구 {openingBad}, 숨은 벽 {wallBad}, 레버 {leverBad}, 블록 {blockBad}, 금지 부품 {forbidden}";
        if (ctrl != null && wired && ctrlBad + doorBad + openingBad + wallBad + leverBad + blockBad + forbidden == 0) Debug.Log(summary, group);
        else Debug.LogError(summary + " — 위 로그 확인.", group);
    }

    private static string Ok(Object o) => o != null ? "O" : "X";

    private static void Try(string label, System.Action a)
    {
        try { a(); }
        catch (System.Exception e) { Debug.LogError($"{Tag} '{label}' 예외 — 이 항목만 건너뛴다: {e}"); }
    }

    // ───────────────────────── 그룹 ─────────────────────────

    /// <summary>generated/S2_Isolation 1개. 같은 빌드에서 두 번 불려도 겹치지 않게 기존 그룹(우리 생성물만 있음)을 지운다(S3·S5_Wiring과 같음).</summary>
    private static Transform NewGroup(Transform generated)
    {
        for (int i = generated.childCount - 1; i >= 0; i--)
        {
            Transform c = generated.GetChild(i);
            if (c.name == GroupName) Object.DestroyImmediate(c.gameObject);
        }
        GameObject go = new GameObject(GroupName);
        go.transform.SetParent(generated, false); // 로컬 원점·무회전 = 섹터 로컬 좌표계 그대로
        return go.transform;
    }

    // ───────────────────────── 격리 컨트롤러 ─────────────────────────

    /// <summary>팀 IsolationRescueController + IsolationTimer를 한 GO에 AddComponent(팀 생성 메뉴 없음 — S2-R §5-1).
    /// 인스펙터 값은 대입하지 않는다(팀 기본 = stages 빈 배열 :72, timer null :75, duration 60 IsolationTimer.cs:25).</summary>
    private static IsolationRescueController BuildController(Transform group)
    {
        GameObject go = new GameObject(ControllerName);
        go.transform.SetParent(group, false);
        go.transform.localPosition = ControllerLocal;
        go.transform.localRotation = Quaternion.identity;
        IsolationRescueController ctrl = go.AddComponent<IsolationRescueController>();
        go.AddComponent<IsolationTimer>();
        EditorUtility.SetDirty(go);
        return ctrl;
    }

    /// <summary>onSuccess → exitS4Door.SetPadPressed(true) Bool 영구 배선 1개(S1_Builder 출구 문과 같은 API).
    /// exitS4Door가 null이거나 다른 씬이면 LogError 후 배선만 건너뛴다(컨트롤러는 남긴다).</summary>
    private static bool WireSuccess(IsolationRescueController ctrl, doorPhysics door)
    {
        if (ctrl == null) { Debug.LogError($"{Tag} {ControllerName}가 없어 onSuccess 배선을 건너뛴다(0/1)."); return false; }
        if (door == null) { Debug.LogError($"{Tag} refs.exitS4Door가 null이다 — onSuccess 배선만 건너뛴다(컨트롤러는 유지, S2-B 확인).", ctrl); return false; }
        if (door.gameObject.scene != ctrl.gameObject.scene)
        {
            Debug.LogError($"{Tag} {door.name}가 {ControllerName}와 다른 씬이다 — 영구 배선 불가, 건너뛴다.", door);
            return false;
        }
        if (ctrl.onSuccess == null) ctrl.onSuccess = new UnityEvent();
        int before = ctrl.onSuccess.GetPersistentEventCount();
        UnityEventTools.AddBoolPersistentListener(ctrl.onSuccess, door.SetPadPressed, true);
        EditorUtility.SetDirty(ctrl);
        bool ok = ctrl.onSuccess.GetPersistentEventCount() == before + 1 && ctrl.onSuccess.GetPersistentTarget(before) == door &&
                  ctrl.onSuccess.GetPersistentMethodName(before) == nameof(doorPhysics.SetPadPressed);
        if (!ok) Debug.LogError($"{Tag} onSuccess → {door.name}.SetPadPressed(true) 영구 배선 확인 실패.", ctrl);
        return ok;
    }

    /// <summary>팀 기본값·배선 개수 확인(값은 쓰지 않는다). Timer 프로퍼티는 부르지 않는다 — getter가 timer 필드를 채운다(Controller.cs:138).</summary>
    private static int VerifyController(IsolationRescueController ctrl, doorPhysics door)
    {
        if (ctrl == null) return 0;
        List<string> d = new List<string>();
        if (ctrl.stages != null && ctrl.stages.Length != 0) d.Add($"stages {ctrl.stages.Length}개 ≠ 0(퍼즐 [추후], 초안 §3)");
        if (ctrl.timer != null) d.Add("timer ≠ null(같은 GO 탐색이 팀 기본)");
        IsolationTimer[] timers = ctrl.GetComponents<IsolationTimer>();
        if (timers.Length != 1) d.Add($"같은 GO IsolationTimer {timers.Length}개 ≠ 1");
        else if (!Near(timers[0].duration, TimerDurationTeam)) d.Add($"duration {timers[0].duration} ≠ 팀 기본 {TimerDurationTeam}");
        if (ctrl.GetComponents<IsolationRescueController>().Length != 1) d.Add("같은 GO IsolationRescueController ≠ 1");

        // 영구 배선: onSuccess 1개(SetPadPressed, 대상 = door) · 그 밖 이벤트 0 [판정 1·2, 계약 C1-5].
        int s = ctrl.onSuccess != null ? ctrl.onSuccess.GetPersistentEventCount() : 0;
        int expectS = door != null ? 1 : 0;
        if (s != expectS) d.Add($"onSuccess 영구 배선 {s}개 ≠ {expectS}");
        CountZero(d, "onTimedOut", ctrl.onTimedOut);
        CountZero(d, "onCaptureConfirmed", ctrl.onCaptureConfirmed);
        CountZero(d, "onCaptureCancelled", ctrl.onCaptureCancelled);
        CountZero(d, "onRoundStarted", ctrl.onRoundStarted);
        CountZero(d, "onPuzzleChanged", ctrl.onPuzzleChanged);
        CountZero(d, "onFinalReleaseAwaiting", ctrl.onFinalReleaseAwaiting);
        CountZero(d, "onAborted", ctrl.onAborted);
        CountZero(d, "onRestarted", ctrl.onRestarted);
        if (d.Count > 0) Debug.LogError($"{Tag} {ControllerName} 값·배선 불일치: {string.Join(", ", d)}", ctrl);
        return d.Count > 0 ? 1 : 0;
    }

    private static void CountZero(List<string> d, string label, UnityEventBase evt)
    {
        int n = evt != null ? evt.GetPersistentEventCount() : 0;
        if (n != 0) d.Add($"{label} 영구 배선 {n}개 ≠ 0");
    }

    // ───────────────────────── 읽기 전용 확인(S2-B 몫 값) ─────────────────────────

    /// <summary>DOOR_S2_ExitS4 [초안 §2-1 행 38·§3·계약 C2-2]: doorPhysics 1, Rigidbody 있음, leverHead null, doorTargetYOffset +3.7, doorSpeed 2,
    /// 솔리드 콜라이더 AABB(±0.02), 형제 VIS_NoCollide_DoorFrame 0. generated 아래 doorPhysics는 이 문 1개뿐(S3행 문·숨은 벽 doorPhysics 없음 — 판정 1·2).</summary>
    private static int VerifyExitS4Door(Transform generated, doorPhysics door)
    {
        int bad = 0;
        doorPhysics[] all = generated.GetComponentsInChildren<doorPhysics>(true);
        if (all.Length != 1 || (door != null && all[0] != door))
        {
            bad++;
            List<string> names = new List<string>();
            foreach (doorPhysics p in all) names.Add(p.name);
            Debug.LogError($"{Tag} generated 아래 doorPhysics {all.Length}개({string.Join(", ", names)}) — {ExitS4DoorName} 1개뿐이어야 한다 [판정 1·2](S2-B 대상).", generated);
        }
        foreach (doorPhysics p in all)
            if (p.leverHead != null) { bad++; Debug.LogError($"{Tag} {p.name}.leverHead = '{p.leverHead.name}' — null이어야 한다(레버 메뉴 자동 연결 해제, 초안 §7-6·계약 C1-5)(S2-B 대상).", p); }

        if (door == null) { Debug.LogError($"{Tag} refs.exitS4Door가 null이다 — S4행 문 확인 건너뜀(S2-B 대상).", generated); return bad + 1; }
        List<string> d = new List<string>();
        if (door.name != ExitS4DoorName) d.Add($"이름 '{door.name}' ≠ {ExitS4DoorName}");
        if (door.GetComponents<doorPhysics>().Length != 1) d.Add("같은 GO doorPhysics ≠ 1");
        Rigidbody rb = door.GetComponent<Rigidbody>();
        if (rb == null) d.Add("Rigidbody 없음(doorPhysics.Awake가 NullReference — S2-R §4-1)");
        else if (!rb.isKinematic) d.Add("Rigidbody isKinematic false(Awake에서 강제되지만 Map4Build.Door 기본은 true)");
        if (!Near(door.doorTargetYOffset, DoorOpenHeight)) d.Add($"doorTargetYOffset {door.doorTargetYOffset} ≠ {DoorOpenHeight}");
        if (!Near(door.doorSpeed, DoorSpeedTeam)) d.Add($"doorSpeed {door.doorSpeed} ≠ {DoorSpeedTeam}");
        BoxCollider solid = null;
        foreach (BoxCollider c in door.GetComponents<BoxCollider>()) if (!c.isTrigger) { solid = c; break; }
        if (solid == null) d.Add("솔리드 BoxCollider 없음");
        else CheckAabb(d, "콜라이더", LocalBoxAabb(generated, solid), DoorMin, DoorMax);
        Transform parent = door.transform.parent;
        if (parent != null)
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == DoorFrameName) { d.Add($"형제 {DoorFrameName} 남음(계약 C2-2 — 삭제 대상)"); break; }
        if (d.Count > 0) { bad++; Debug.LogError($"{Tag} {ExitS4DoorName} 불일치 [초안 §2-1·§3] — 고치지 않음(S2-B 대상): {string.Join(", ", d)}", door); }
        return bad;
    }

    /// <summary>TEMP_S2_ExitS3_Open [판정 1·초안 §2-7]: 섹터 로컬 (0,0,84)(±0.02), 회전 0, Transform만(콜라이더·컴포넌트 0).</summary>
    private static int VerifyExitS3Opening(Transform generated, Transform opening)
    {
        if (opening == null) { Debug.LogError($"{Tag} refs.exitS3Opening이 null이다 — 확인 건너뜀(S2-B 대상).", generated); return 1; }
        List<string> d = new List<string>();
        if (opening.name != ExitS3OpeningName) d.Add($"이름 '{opening.name}' ≠ {ExitS3OpeningName}");
        Vector3 p = generated.InverseTransformPoint(opening.position);
        if ((p - ExitS3Local).sqrMagnitude > Tol * Tol) d.Add($"위치 {Fmt(p)} ≠ {Fmt(ExitS3Local)}");
        float ang = Quaternion.Angle(Quaternion.Inverse(generated.rotation) * opening.rotation, Quaternion.identity);
        if (ang > AngleTol) d.Add($"회전 {ang:0.##}° ≠ 0");
        Component[] comps = opening.GetComponents<Component>();
        if (comps.Length != 1) d.Add($"컴포넌트 {comps.Length}개 — Transform만이어야 한다");
        if (opening.GetComponentsInChildren<Collider>(true).Length != 0) d.Add("콜라이더 있음(개구 상시 개방이어야 함)");
        if (opening.GetComponentsInChildren<doorPhysics>(true).Length != 0) d.Add("doorPhysics 있음(S3행 문 만들지 않음 — 판정 1)");
        if (d.Count > 0) { Debug.LogError($"{Tag} {ExitS3OpeningName} 불일치 [판정 1·초안 §2-7] — 고치지 않음(S2-B 대상): {string.Join(", ", d)}", opening); return 1; }
        return 0;
    }

    /// <summary>GEO_S2_Wall_IsoHidden [판정 2·초안 §2-1 행 17]: 정지 벽 — BoxCollider AABB(±0.02), doorPhysics·Rigidbody·트리거 없음.</summary>
    private static int VerifyIsoHiddenWall(Transform generated, Transform wall)
    {
        if (wall == null) { Debug.LogError($"{Tag} refs.isoHiddenWall이 null이다 — 확인 건너뜀(S2-B 대상).", generated); return 1; }
        List<string> d = new List<string>();
        if (wall.name != IsoHiddenWallName) d.Add($"이름 '{wall.name}' ≠ {IsoHiddenWallName}");
        BoxCollider[] boxes = wall.GetComponents<BoxCollider>();
        if (boxes.Length != 1) d.Add($"BoxCollider {boxes.Length}개 ≠ 1");
        foreach (BoxCollider b in boxes) if (b.isTrigger) d.Add("isTrigger true(트리거 없어야 함)");
        if (boxes.Length >= 1) CheckAabb(d, "콜라이더", LocalBoxAabb(generated, boxes[0]), IsoWallMin, IsoWallMax);
        if (wall.GetComponentsInChildren<doorPhysics>(true).Length != 0) d.Add("doorPhysics 있음(하강 흉내 금지 — 판정 2)");
        if (wall.GetComponentsInChildren<Rigidbody>(true).Length != 0) d.Add("Rigidbody 있음");
        if (d.Count > 0) { Debug.LogError($"{Tag} {IsoHiddenWallName} 불일치 [판정 2·초안 §2-1] — 고치지 않음(S2-B 대상): {string.Join(", ", d)}", wall); return 1; }
        return 0;
    }

    /// <summary>레버 3 [계산·팀·제안 초안 §2-4·§3]: 부모 이름·섹터 로컬 위치(±0.02)·yaw·스케일 1·부모 S2_Gimmicks, lever_pivot 로컬 위치 0·yaw −45,
    /// 받침대 = 부모 이름 + _Body 로컬 위치·회전 0·크기, LeverHead 메뉴값, 씬 루트에 메뉴 잔류물 0.</summary>
    private static int VerifyLevers(Transform generated, S2_Refs refs)
    {
        int bad = 0;
        List<Transform> roots = refs.leverRoots;
        List<LeverHead> levers = refs.levers;
        if (roots == null || levers == null) { Debug.LogError($"{Tag} refs.leverRoots/levers가 null이다 — 레버 확인 건너뜀(S2-B 대상).", generated); return 1; }
        if (roots.Count != LeverNames.Length || levers.Count != LeverNames.Length)
        {
            bad++;
            Debug.LogError($"{Tag} refs.leverRoots {roots.Count}개·levers {levers.Count}개 — 계약 C1-1은 {LeverNames.Length}개(S2-B 대상).", generated);
        }
        int n = Mathf.Min(roots.Count, LeverNames.Length);
        for (int i = 0; i < n; i++)
        {
            Transform root = roots[i];
            if (root == null) { bad++; Debug.LogError($"{Tag} leverRoots[{i}]가 null이다(S2-B 대상).", generated); continue; }
            List<string> d = new List<string>();
            if (root.name != LeverNames[i]) d.Add($"이름 '{root.name}' ≠ {LeverNames[i]}(C1-1 순서)");
            if (root.parent == null || root.parent.name != GimmickRootName || (refs.gimmickRoot != null && root.parent != refs.gimmickRoot))
                d.Add($"부모 '{(root.parent != null ? root.parent.name : "없음")}' ≠ {GimmickRootName}");
            Vector3 p = generated.InverseTransformPoint(root.position);
            if ((p - LeverLocal[i]).sqrMagnitude > Tol * Tol) d.Add($"위치 {Fmt(p)} ≠ {Fmt(LeverLocal[i])}");
            float ang = Quaternion.Angle(Quaternion.Inverse(generated.rotation) * root.rotation, Quaternion.Euler(0f, LeverYaw[i], 0f));
            if (ang > AngleTol) d.Add($"yaw ≠ {LeverYaw[i]}({ang:0.##}° 차이)");
            if ((root.localScale - Vector3.one).sqrMagnitude > 1e-6f) d.Add($"스케일 {Fmt(root.localScale)} ≠ 1");

            Transform pivot = root.Find(PivotName);
            if (pivot == null) d.Add($"자식 {PivotName} 없음");
            else
            {
                if (pivot.localPosition.sqrMagnitude > Tol * Tol) d.Add($"{PivotName} 로컬 위치 {Fmt(pivot.localPosition)} ≠ 0");
                float pa = Quaternion.Angle(pivot.localRotation, Quaternion.Euler(0f, PivotLocalYaw, 0f));
                if (pa > AngleTol) d.Add($"{PivotName} 로컬 yaw ≠ {PivotLocalYaw}({pa:0.##}° 차이)");
                if ((pivot.localScale - PivotLocalScaleTeam).sqrMagnitude > 1e-8f) d.Add($"{PivotName} 스케일 {Fmt(pivot.localScale)} ≠ 메뉴값 {Fmt(PivotLocalScaleTeam)}");
                if (pivot.Find(HeadName) == null) d.Add($"{PivotName}/{HeadName} 없음");
            }

            string bodyName = root.name + BodySuffix;
            Transform body = root.Find(bodyName);
            if (body == null) d.Add($"받침대 '{bodyName}' 없음(이름 = 부모 이름 + {BodySuffix} [제안 — 확인 대기 3])");
            else
            {
                if ((body.localPosition - BodyLocalPos).sqrMagnitude > Tol * Tol) d.Add($"받침대 로컬 위치 {Fmt(body.localPosition)} ≠ {Fmt(BodyLocalPos)}");
                if (Quaternion.Angle(body.localRotation, Quaternion.identity) > AngleTol) d.Add("받침대 로컬 회전 ≠ 0");
                if ((body.localScale - BodyLocalScale).sqrMagnitude > 1e-6f) d.Add($"받침대 로컬 크기 {Fmt(body.localScale)} ≠ {Fmt(BodyLocalScale)}");
            }

            LeverHead lh = i < levers.Count ? levers[i] : null;
            if (lh == null) d.Add($"levers[{i}] null");
            else
            {
                if (!lh.transform.IsChildOf(root)) d.Add($"levers[{i}] '{lh.name}'가 {root.name} 아래가 아니다(C1-1 같은 순서)");
                if (pivot != null && lh.leverPivot != pivot) d.Add("LeverHead.leverPivot ≠ 이 레버의 lever_pivot");
                if (!Near(lh.rotateSpeed, LeverRotateSpeed)) d.Add($"rotateSpeed {lh.rotateSpeed}≠{LeverRotateSpeed}");
                if (!Near(lh.maxAngle, LeverMaxAngle)) d.Add($"maxAngle {lh.maxAngle}≠{LeverMaxAngle}");
                if (!Near(lh.normalSmoothSpeed, LeverNormalSmooth)) d.Add($"normalSmoothSpeed {lh.normalSmoothSpeed}≠{LeverNormalSmooth}");
                if (!Near(lh.returnDelay, LeverReturnDelay)) d.Add($"returnDelay {lh.returnDelay}≠{LeverReturnDelay}");
                if (!Near(lh.returnSpeed, LeverReturnSpeed)) d.Add($"returnSpeed {lh.returnSpeed}≠{LeverReturnSpeed}");
                if (!Near(lh.pushExitGrace, LeverPushExitGrace)) d.Add($"pushExitGrace {lh.pushExitGrace}≠{LeverPushExitGrace}");
            }
            if (d.Count > 0) { bad++; Debug.LogError($"{Tag} 레버 '{root.name}' 불일치 [초안 §2-4·§3] — 고치지 않음(S2-B 대상): {string.Join(", ", d)}", root); }
        }

        // generated 아래 LeverHead는 3개뿐.
        int total = generated.GetComponentsInChildren<LeverHead>(true).Length;
        if (total != LeverNames.Length) { bad++; Debug.LogError($"{Tag} generated 아래 LeverHead {total}개 ≠ {LeverNames.Length}(S2-B 대상).", generated); }

        // 씬 루트에 팀 메뉴 잔류물(lever_body·lever_pivot·SnapBlock) 0 [1차 C3].
        List<string> left = new List<string>();
        foreach (GameObject r in generated.gameObject.scene.GetRootGameObjects())
            if (System.Array.IndexOf(MenuLeftoverRootNames, r.name) >= 0) left.Add(r.name);
        if (left.Count > 0) { bad++; Debug.LogError($"{Tag} 씬 루트에 팀 메뉴 잔류물 {left.Count}개({string.Join(", ", left)}) — 0이어야 한다(1차 C3, S2-B 대상).", generated); }
        return bad;
    }

    /// <summary>딱딱블록 39 [제안·팀 초안 §2-5·계약 C1-1]: 이름 순서, 섹터 로컬 중심(±0.02), 회전 0, Rigidbody mass 1, 부모 S2_Gimmicks.
    /// generated 아래 SnapBlock은 39개뿐.</summary>
    private static int VerifyBlocks(Transform generated, S2_Refs refs)
    {
        List<SnapBlock> blocks = refs.blocks;
        if (blocks == null) { Debug.LogError($"{Tag} refs.blocks가 null이다 — 블록 확인 건너뜀(S2-B 대상).", generated); return 1; }
        List<string> names = new List<string>();
        List<Vector3> centers = new List<Vector3>();
        BuildBlockTable(names, centers);

        int bad = 0;
        if (blocks.Count != ExpectedBlocks) { bad++; Debug.LogError($"{Tag} refs.blocks {blocks.Count}개 — 계약 C1-1은 {ExpectedBlocks}개(S2-B 대상).", generated); }
        int n = Mathf.Min(blocks.Count, names.Count);
        for (int i = 0; i < n; i++)
        {
            SnapBlock b = blocks[i];
            if (b == null) { bad++; Debug.LogError($"{Tag} blocks[{i}]가 null이다(S2-B 대상).", generated); continue; }
            List<string> d = new List<string>();
            if (b.name != names[i]) d.Add($"이름 '{b.name}' ≠ {names[i]}(C1-1 순서)");
            if (b.transform.parent == null || b.transform.parent.name != GimmickRootName || (refs.gimmickRoot != null && b.transform.parent != refs.gimmickRoot))
                d.Add($"부모 '{(b.transform.parent != null ? b.transform.parent.name : "없음")}' ≠ {GimmickRootName}");
            Vector3 p = generated.InverseTransformPoint(b.transform.position);
            if ((p - centers[i]).sqrMagnitude > Tol * Tol) d.Add($"중심 {Fmt(p)} ≠ {Fmt(centers[i])}");
            if (Quaternion.Angle(Quaternion.Inverse(generated.rotation) * b.transform.rotation, Quaternion.identity) > AngleTol) d.Add("회전 ≠ 0");
            Rigidbody rb = b.GetComponent<Rigidbody>();
            if (rb == null) d.Add("Rigidbody 없음");
            else if (!Near(rb.mass, BlockMassTeam)) d.Add($"mass {rb.mass} ≠ {BlockMassTeam}");
            if (d.Count > 0) { bad++; Debug.LogError($"{Tag} 블록 [{i}] 불일치 [초안 §2-5] — 고치지 않음(S2-B 대상): {string.Join(", ", d)}", b); }
        }
        int total = generated.GetComponentsInChildren<SnapBlock>(true).Length;
        if (total != ExpectedBlocks) { bad++; Debug.LogError($"{Tag} generated 아래 SnapBlock {total}개 ≠ {ExpectedBlocks}(S2-B 대상).", generated); }
        return bad;
    }

    /// <summary>계약 C1-1 순서 표: Pile1(B0~B3, T0~T3) → Pile2 → Pile3 → Scatter_00~14. 더미 중심은 초안 §2-5 행 1~24.</summary>
    private static void BuildBlockTable(List<string> names, List<Vector3> centers)
    {
        for (int p = 1; p <= 3; p++)
            for (int layer = 0; layer < 2; layer++)
                for (int i = 0; i < 4; i++)
                {
                    names.Add($"S2_Block_Pile{p}_{(layer == 0 ? "B" : "T")}{i}");
                    float y = layer == 0 ? 0.5f : 1.5f;                        // [제안 §2-5] B = 아랫단, T = 윗단
                    if (p == 1) centers.Add(new Vector3(-30f, y, 40.5f + i));   // [제안 §2-5] L1 칸 입구 더미
                    else if (p == 2) centers.Add(new Vector3(30f, y, 40.5f + i)); // [제안 §2-5] L2 칸 입구 더미
                    else centers.Add(new Vector3(18.5f + i, y, 8f));           // [제안 §2-5] L3 칸 입구 더미
                }
        for (int i = 0; i < ScatterCenters.Length; i++)
        {
            names.Add($"S2_Block_Scatter_{i:00}");
            centers.Add(ScatterCenters[i]);
        }
    }

    /// <summary>[계약 C1-5 "S2에는 복귀 장치 없음(추가 금지)"] generated 아래 복귀 부품 0개.</summary>
    private static int VerifyForbidden(Transform generated)
    {
        int n = 0;
        n += Forbid<SectionHitCounter>(generated);
        n += Forbid<SectionSafePoint>(generated);
        n += Forbid<Lab_SectionRespawnBridge>(generated);
        n += Forbid<OutOfBoundsVolume>(generated);
        n += Forbid<RespawnZone>(generated);
        return n;
    }

    private static int Forbid<T>(Transform generated) where T : Component
    {
        T[] found = generated.GetComponentsInChildren<T>(true);
        foreach (T c in found) Debug.LogError($"{Tag} {typeof(T).Name} '{c.name}' — S2 복귀 장치 추가 금지(계약 C1-5).", c);
        return found.Length;
    }

    /// <summary>계약 C1-0·C3: 이 Wire가 만든 새 그룹은 S2_Isolation 1개, 그 아래 S2_IsoController 1개(컨트롤러 1 + 타이머 1).</summary>
    private static void VerifyGroup(Transform generated, Transform group)
    {
        int groups = 0;
        for (int i = 0; i < generated.childCount; i++) if (generated.GetChild(i).name == GroupName) groups++;
        if (groups != 1) Debug.LogError($"{Tag} generated 아래 '{GroupName}' {groups}개 — 1개여야 한다.", generated);
        if (group.childCount != 1 || group.GetChild(0).name != ControllerName)
            Debug.LogError($"{Tag} '{GroupName}' 자식 {group.childCount}개 — '{ControllerName}' 1개여야 한다(계약 C3).", group);
        int c = group.GetComponentsInChildren<IsolationRescueController>(true).Length;
        int t = group.GetComponentsInChildren<IsolationTimer>(true).Length;
        if (c != 1 || t != 1) Debug.LogError($"{Tag} '{GroupName}' 아래 IsolationRescueController {c}·IsolationTimer {t} — 각 1이어야 한다.", group);
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    private static bool Near(float a, float b) => Mathf.Abs(a - b) <= 1e-4f;
    private static string Fmt(Vector3 v) => $"({v.x:0.####}, {v.y:0.####}, {v.z:0.####})";

    /// <summary>BoxCollider의 8개 꼭짓점을 generated 로컬로 옮긴 AABB(물리 동기화 불필요 — 에디터 배치 직후에도 정확).</summary>
    private static Bounds LocalBoxAabb(Transform generated, BoxCollider box)
    {
        Transform t = box.transform;
        Vector3 e = box.size * 0.5f;
        Bounds b = new Bounds(generated.InverseTransformPoint(t.TransformPoint(box.center)), Vector3.zero);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    b.Encapsulate(generated.InverseTransformPoint(t.TransformPoint(box.center + new Vector3(sx * e.x, sy * e.y, sz * e.z))));
        return b;
    }

    private static void CheckAabb(List<string> d, string label, Bounds b, Vector3 min, Vector3 max)
    {
        if (Mathf.Abs(b.min.x - min.x) > Tol || Mathf.Abs(b.min.y - min.y) > Tol || Mathf.Abs(b.min.z - min.z) > Tol ||
            Mathf.Abs(b.max.x - max.x) > Tol || Mathf.Abs(b.max.y - max.y) > Tol || Mathf.Abs(b.max.z - max.z) > Tol)
            d.Add($"{label} AABB min{Fmt(b.min)} max{Fmt(b.max)} ≠ min{Fmt(min)} max{Fmt(max)}(±{Tol})");
    }
}
#endif
