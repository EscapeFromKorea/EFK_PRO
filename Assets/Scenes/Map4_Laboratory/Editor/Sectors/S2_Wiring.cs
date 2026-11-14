#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 2026-11-03 CH2: 구역 진입 후 문닫힘 안전 확인, 양쪽 준비, 2단계 3기호 레버,
/// 120초 제한, 바깥 전원 유지와 안쪽 최종 해제, 성공 뒤 S4 문 통과 시 세 도형 CH4 이동.
/// 팀 IsolationRescueSystem 공개 컴포넌트를 배치하고 맵4 전용 Lab_IsolationFlow로 씬 간 이동을 잇는다.
/// 이전 "첫 레버 함정으로 격리"는 현행 진입 격리 규칙에서 폐기했다. 레버 이름만 하위 호환을 위해 남긴다.
/// 지형·기존 블록·S3 열린 통로는 S2_Builder가 만든다.
/// </summary>
public static class S2_Wiring
{
    // ── 이름 [계약 C3 S2행·C2-2] ──
    private const string GroupName = "S2_Isolation";
    private const string ControllerName = "S2_IsoController";
    private const string ExitS4DoorName = "DOOR_S2_ExitS4";
    private const string ExitS3DoorName = "DOOR_S2_ExitS3";
    private const string IsoHiddenWallName = "GEO_S2_Wall_IsoHidden";
    private const string GimmickRootName = "S2_Gimmicks";
    private const string DoorFrameName = "VIS_NoCollide_DoorFrame";   // [헬퍼 Map4Build.cs:207] 빌더가 삭제해야 하는 문틀
    private const string PivotName = "lever_pivot";                    // [팀] DoorSystemMenuItem.cs:165
    private const string HeadName = "lever_head";                      // [팀] DoorSystemMenuItem.cs:172
    private const string BodySuffix = "_Body";                         // [제안 초안 §2-4·§3 — 확인 대기 3] 받침대 = 부모 이름 + _Body
    private static readonly string[] MenuLeftoverRootNames = { "lever_body", "lever_pivot", "SnapBlock" }; // [팀] 메뉴 생성 이름 — 씬 루트 잔류 0(1차 C3)

    // ── 격리 컨트롤러 ──
    private static readonly Vector3 ControllerLocal = new Vector3(-14f, 1f, 42f); // [초안 §3] 위치 무관(콜라이더 없음)
    private const float TimerDurationTeam = 120f;                      // 11/03 CH2 회의 방향

    // ── 확인 기대값: S4행 문 [초안 §2-1 행 38·§3] ──
    private static readonly Vector3 DoorMin = new Vector3(29.1f, 0.3f, 74.5f);
    private static readonly Vector3 DoorMax = new Vector3(29.5f, 3.8f, 79.5f);
    private const float DoorOpenHeight = 3.7f;                         // [제안 초안 §2-1] doorTargetYOffset(+3.7 → 열림 y4.0~7.5)
    private const float DoorSpeedTeam = 2f;                            // [팀] doorPhysics.cs:8 기본값

    // ── 확인 기대값: 숨은 벽(정지) [판정 2·초안 §2-1 행 17] ──
    private static readonly Vector3 IsoWallMin = new Vector3(-14.2f, 4.3f, 30.5f);
    private static readonly Vector3 IsoWallMax = new Vector3(-13.8f, 8.3f, 53.5f);

    // ── 확인 기대값: S3행 개구 표지 [판정 1·초안 §2-7] ──
    private static readonly Vector3 ExitS3Min = new Vector3(-4f, 0.3f, 83.8f);
    private static readonly Vector3 ExitS3Max = new Vector3(4f, 3.8f, 84.2f);

    // ── 확인 기대값: 레버 3 [계산 초안 §2-4]·[팀 메뉴값 §2-4] ──
    private static readonly string[] LeverNames = { "S2_Lever1_Trap", "S2_Lever2", "S2_Lever3" };   // [계약 C1-1·C2-2] 순서
    private static readonly Vector3[] LeverLocal =
    {
        new Vector3(-8f, 0.485f, 58f),
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
    private const float LeverReturnDelay = 0.5f, LeverReturnSpeed = 3f, LeverPushExitGrace = 0.3f;

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
        Try("CH2 퍼즐 생성", () => BuildPuzzle(group, refs, ctrl));
        Try("onSuccess 배선", () => wired = WireSuccess(ctrl, refs.exitS4Door));
        int ctrlBad = 0;
        Try("컨트롤러 값·배선 확인", () => ctrlBad = VerifyController(ctrl, refs.exitS4Door));

        // 읽기 전용 확인(S2-B 몫 값) — 다르면 LogError만.
        int doorBad = 0, openingBad = 0, wallBad = 0, leverBad = 0, blockBad = 0, forbidden = 0;
        Try("S4행 문 확인", () => doorBad = VerifyExitS4Door(generated, refs.exitS4Door));
        Try("S3행 비상문 확인", () => openingBad = VerifyExitS3Door(generated, refs.exitS3Door));
        Try("숨은 벽 확인", () => wallBad = VerifyIsoHiddenWall(generated, refs.isoHiddenWall));
        Try("레버 확인", () => leverBad = VerifyLevers(generated, refs));
        Try("딱딱블록 확인", () => blockBad = VerifyBlocks(generated, refs));
        Try("CH2 부품 확인", () => forbidden = VerifyPuzzle(generated, group, refs));
        Try("그룹 확인", () => VerifyGroup(generated, group));

        string summary = $"{Tag} CH2 퍼즐 생성 {(ctrl != null ? "1/1" : "실패")} — {ControllerName} {Ok(ctrl)}; S4행 문 배선 {(wired ? "1/1" : "0/1")}; " +
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
    /// 2단계 3기호 순서와 120초를 회의 방향대로 채운다.</summary>
    private static IsolationRescueController BuildController(Transform group)
    {
        GameObject go = new GameObject(ControllerName);
        go.transform.SetParent(group, false);
        go.transform.localPosition = ControllerLocal;
        go.transform.localRotation = Quaternion.identity;
        IsolationRescueController ctrl = go.AddComponent<IsolationRescueController>();
        IsolationTimer timer = go.AddComponent<IsolationTimer>();
        timer.duration = TimerDurationTeam;
        ctrl.timer = timer;
        ctrl.stages = new[]
        {
            new IsolationRescueController.Stage { answerOrder = new[] { 2, 1, 3 } },
            new IsolationRescueController.Stage { answerOrder = new[] { 3, 2, 1 } }
        };
        EditorUtility.SetDirty(go);
        return ctrl;
    }

    // 격리실은 x < -13.35, 바깥은 x > -12.95. 기존 첫 레버의 이름만 남기고 배치는 바깥으로 옮겼다.
    private static void BuildPuzzle(Transform group, S2_Refs refs, IsolationRescueController ctrl)
    {
        if (ctrl == null || refs.entryDoor == null || refs.exitS3Door == null || refs.exitS4Door == null || refs.levers.Count != 3)
            throw new System.InvalidOperationException("CH2 필수 문·레버·컨트롤러 참조 부족");

        IsolationEntryDoor entry = refs.entryDoor.gameObject.AddComponent<IsolationEntryDoor>();
        entry.controller = ctrl;
        entry.door = refs.entryDoor;
        UnityEventTools.AddVoidPersistentListener(ctrl.onCaptureConfirmed, entry.BeginClose);
        UnityEventTools.AddVoidPersistentListener(ctrl.onCaptureCancelled, entry.Open);
        UnityEventTools.AddVoidPersistentListener(ctrl.onTimedOut, entry.Open);
        UnityEventTools.AddBoolPersistentListener(ctrl.onTimedOut, refs.exitS3Door.SetPadPressed, true);
        UnityEventTools.AddVoidPersistentListener(ctrl.onAborted, entry.Open);
        UnityEventTools.AddBoolPersistentListener(ctrl.onAborted, refs.exitS3Door.SetPadPressed, true);
        UnityEventTools.AddVoidPersistentListener(ctrl.onRestarted, entry.Open);
        UnityEventTools.AddBoolPersistentListener(ctrl.onRestarted, refs.exitS3Door.SetPadPressed, false);
        UnityEventTools.AddBoolPersistentListener(ctrl.onRestarted, refs.exitS4Door.SetPadPressed, false);
        UnityEventTools.AddVoidPersistentListener(ctrl.onSuccess, entry.Open);

        GameObject captureGo = Child(group, "S2_Capture", new Vector3(-13.15f, 2f, 42f));
        BoxCollider captureBox = captureGo.AddComponent<BoxCollider>();
        captureBox.isTrigger = true;
        captureBox.size = new Vector3(2f, 4f, 23f);
        GameObject inward = Child(captureGo.transform, "InsideDirection", Vector3.zero);
        inward.transform.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        IsolationCaptureTrigger capture = captureGo.AddComponent<IsolationCaptureTrigger>();
        capture.controller = ctrl;
        capture.insideDirection = inward.transform;
        capture.minDepth = 0.1f;

        IsolationRescueHud hud = Child(group, "S2_Hud", Vector3.zero).AddComponent<IsolationRescueHud>();
        hud.controller = ctrl;
        hud.area = new Rect(12f, 12f, 440f, 170f);

        IsolationReadyButton readyIn = Zone<IsolationReadyButton>(group, "S2_ReadyInside", new Vector3(-23f, 0f, 35f), 3f, "Floor");
        readyIn.controller = ctrl; readyIn.inside = true; readyIn.hud = hud;
        IsolationReadyButton readyOut = Zone<IsolationReadyButton>(group, "S2_ReadyOutside", new Vector3(-8f, 0f, 35f), 3f, "Floor");
        readyOut.controller = ctrl; readyOut.inside = false; readyOut.hud = hud;

        for (int i = 0; i < 2; i++)
        {
            ClueBoard clue = Zone<ClueBoard>(group, $"S2_Clue_{i + 1}", new Vector3(-23f, 0f, 42f), 5f, "Door");
            clue.controller = ctrl; clue.stageIndex = i;
            clue.symbolNames = (string[])IsolationSymbols.DefaultNames.Clone();
        }
        CorrespondenceMap map = Zone<CorrespondenceMap>(group, "S2_Correspondence", new Vector3(-6f, 0f, 46f), 4f, "Door");
        map.controller = ctrl;
        map.symbolNames = (string[])IsolationSymbols.DefaultNames.Clone();

        for (int i = 0; i < refs.levers.Count; i++)
        {
            LeverHead head = refs.levers[i];
            SequenceLever sequence = head.leverPivot.gameObject.AddComponent<SequenceLever>();
            sequence.controller = ctrl;
            sequence.lever = head;
            sequence.leverNumber = i + 1;
            head.returnDelay = 0.5f;
            head.returnSpeed = 3f;
        }

        PowerHoldSwitch power = Zone<PowerHoldSwitch>(group, "S2_PowerHold", new Vector3(-6f, 0f, 52f), 3f, "Floor");
        power.controller = ctrl;
        ReleaseButton release = Zone<ReleaseButton>(group, "S2_Release", new Vector3(-23f, 0f, 49f), 3f, "Floor");
        release.controller = ctrl; release.hud = hud;

        SectionSafePoint insideSafe = Child(group, "S2_InsideSafe", new Vector3(-22f, 1f, 42f)).AddComponent<SectionSafePoint>();
        insideSafe.sectionId = "S2_InsideSafe";
        SectionSafePoint outsideSafe = Child(group, "S2_OutsideSafe", new Vector3(-6f, 1f, 42f)).AddComponent<SectionSafePoint>();
        outsideSafe.sectionId = "S2_OutsideSafe";
        GameObject fall = Child(group, "S2_FallReturn", new Vector3(0f, -8f, 42f));
        BoxCollider fallBox = fall.AddComponent<BoxCollider>();
        fallBox.isTrigger = true; fallBox.size = new Vector3(76f, 2f, 84f);
        IsolationRoleReturn roleReturn = fall.AddComponent<IsolationRoleReturn>();
        roleReturn.controller = ctrl; roleReturn.insideSafePoint = insideSafe; roleReturn.outsideSafePoint = outsideSafe;

        GameObject passage = Child(group, "S2_S4Passage", new Vector3(30.8f, 1.8f, 77f));
        BoxCollider passageBox = passage.AddComponent<BoxCollider>();
        passageBox.isTrigger = true; passageBox.size = new Vector3(1.5f, 3.6f, 3.5f);
        Lab_IsolationFlow flow = passage.AddComponent<Lab_IsolationFlow>();
        flow.controller = ctrl; flow.roleReturn = roleReturn; flow.hud = hud;
        flow.moveWholeTeam = true;
        EditorUtility.SetDirty(refs.entryDoor);
        EditorUtility.SetDirty(ctrl);
    }

    private static GameObject Child(Transform parent, string name, Vector3 local)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        return go;
    }

    private static T Zone<T>(Transform parent, string name, Vector3 local, float width, string palette) where T : Component
    {
        GameObject root = Child(parent, name, local);
        BoxCollider trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 0.8f, 0f);
        trigger.size = new Vector3(width, 1.6f, width);
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "VIS_" + name;
        plate.transform.SetParent(root.transform, false);
        plate.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        plate.transform.localScale = new Vector3(width, 0.12f, width);
        Object.DestroyImmediate(plate.GetComponent<Collider>());
        plate.GetComponent<Renderer>().sharedMaterial = Map4Build.GetMaterial(palette);
        return root.AddComponent<T>();
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
        if (!ctrl.ValidateContent(out string contentError) || ctrl.StageCount != 2) d.Add($"2단계 콘텐츠 오류: {contentError}");
        if (ctrl.timer == null) d.Add("timer 참조 없음");
        IsolationTimer[] timers = ctrl.GetComponents<IsolationTimer>();
        if (timers.Length != 1) d.Add($"같은 GO IsolationTimer {timers.Length}개 ≠ 1");
        else if (!Near(timers[0].duration, TimerDurationTeam)) d.Add($"duration {timers[0].duration} ≠ 팀 기본 {TimerDurationTeam}");
        if (ctrl.GetComponents<IsolationRescueController>().Length != 1) d.Add("같은 GO IsolationRescueController ≠ 1");

        // 영구 배선: 성공 시 S4 문과 입구문, 실패 시 입구문을 열어 교착을 막는다.
        int s = ctrl.onSuccess != null ? ctrl.onSuccess.GetPersistentEventCount() : 0;
        int expectS = door != null ? 1 : 0;
        if (s != expectS + 1) d.Add($"onSuccess 영구 배선 {s}개 ≠ {expectS + 1}");
        if (ctrl.onTimedOut.GetPersistentEventCount() != 2) d.Add("onTimedOut 입구문·S3문 열기 배선 ≠ 2");
        if (ctrl.onCaptureConfirmed.GetPersistentEventCount() != 1) d.Add("onCaptureConfirmed 문닫기 배선 ≠ 1");
        if (ctrl.onCaptureCancelled.GetPersistentEventCount() != 1) d.Add("onCaptureCancelled 문열기 배선 ≠ 1");
        CountZero(d, "onRoundStarted", ctrl.onRoundStarted);
        CountZero(d, "onPuzzleChanged", ctrl.onPuzzleChanged);
        CountZero(d, "onFinalReleaseAwaiting", ctrl.onFinalReleaseAwaiting);
        if (ctrl.onAborted.GetPersistentEventCount() != 2) d.Add("onAborted 입구문·S3문 배선 ≠ 2");
        if (ctrl.onRestarted.GetPersistentEventCount() != 3) d.Add("onRestarted 입구문·S3문·S4문 배선 ≠ 3");
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
        if (all.Length != 3 || (door != null && System.Array.IndexOf(all, door) < 0))
        {
            bad++;
            List<string> names = new List<string>();
            foreach (doorPhysics p in all) names.Add(p.name);
            Debug.LogError($"{Tag} generated 아래 doorPhysics {all.Length}개({string.Join(", ", names)}) — 격리 진입문·S3·S4 출구문 각 1개여야 한다.", generated);
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

    private static int VerifyExitS3Door(Transform generated, doorPhysics door)
    {
        if (door == null) { Debug.LogError($"{Tag} S3 비상문 참조 없음.", generated); return 1; }
        List<string> d = new List<string>();
        if (door.name != ExitS3DoorName) d.Add($"이름 '{door.name}' ≠ {ExitS3DoorName}");
        if (door.leverHead != null) d.Add("레버 자동 연결이 남음");
        if (!Near(door.doorTargetYOffset, 3.7f)) d.Add("열림 높이 ≠ 3.7");
        BoxCollider solid = null;
        foreach (BoxCollider c in door.GetComponents<BoxCollider>()) if (!c.isTrigger) { solid = c; break; }
        if (solid == null) d.Add("솔리드 BoxCollider 없음");
        else CheckAabb(d, "콜라이더", LocalBoxAabb(generated, solid), ExitS3Min, ExitS3Max);
        if (d.Count > 0) { Debug.LogError($"{Tag} S3 비상문 불일치: {string.Join(", ", d)}", door); return 1; }
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

    /// <summary>새 CH2 정식 배선의 필수 부품 수와 주요 참조를 확인한다.</summary>
    private static int VerifyPuzzle(Transform generated, Transform group, S2_Refs refs)
    {
        List<string> problems = new List<string>();
        CheckCount<IsolationCaptureTrigger>(generated, 1, problems);
        CheckCount<IsolationEntryDoor>(generated, 1, problems);
        CheckCount<IsolationRescueController>(generated, 1, problems);
        CheckCount<IsolationTimer>(generated, 1, problems);
        CheckCount<IsolationReadyButton>(generated, 2, problems);
        CheckCount<ClueBoard>(generated, 2, problems);
        CheckCount<CorrespondenceMap>(generated, 1, problems);
        CheckCount<SequenceLever>(generated, 3, problems);
        CheckCount<PowerHoldSwitch>(generated, 1, problems);
        CheckCount<ReleaseButton>(generated, 1, problems);
        CheckCount<IsolationRescueHud>(generated, 1, problems);
        CheckCount<IsolationRoleReturn>(generated, 1, problems);
        CheckCount<SectionSafePoint>(generated, 2, problems);
        CheckCount<Lab_IsolationFlow>(generated, 1, problems);
        if (refs.entryDoor == null || refs.entryDoor.GetComponent<IsolationEntryDoor>() == null)
            problems.Add("격리 진입문/안전검사 연결 없음");
        if (refs.exitS3Door == null) problems.Add("S3 실패 출구문 없음");
        if (generated.Find("TEMP_S2_ExitS3_Open") != null) problems.Add("구형 임시 개방 표지가 남음");
        if (group.Find("S2_Capture") == null || group.Find("S2_S4Passage") == null)
            problems.Add("포획 또는 S4 문 뒤 감지 구역 없음");
        if (group.GetComponentInChildren<Lab_IsolationFlow>(true) is Lab_IsolationFlow flow && !flow.moveWholeTeam)
            problems.Add("성공 이동이 세 도형 함께 이동이 아님");
        if (problems.Count > 0) Debug.LogError($"{Tag} CH2 부품 불일치: {string.Join(", ", problems)}", generated);
        return problems.Count;
    }

    private static void CheckCount<T>(Transform generated, int expected, List<string> problems) where T : Component
    {
        int count = generated.GetComponentsInChildren<T>(true).Length;
        if (count != expected) problems.Add($"{typeof(T).Name} {count}개 ≠ {expected}");
    }

    /// <summary>계약 C1-0·C3: 이 Wire가 만든 새 그룹은 S2_Isolation 1개, 그 아래 S2_IsoController 1개(컨트롤러 1 + 타이머 1).</summary>
    private static void VerifyGroup(Transform generated, Transform group)
    {
        int groups = 0;
        for (int i = 0; i < generated.childCount; i++) if (generated.GetChild(i).name == GroupName) groups++;
        if (groups != 1) Debug.LogError($"{Tag} generated 아래 '{GroupName}' {groups}개 — 1개여야 한다.", generated);
        if (group.Find(ControllerName) == null || group.childCount != 14)
            Debug.LogError($"{Tag} '{GroupName}' 자식 {group.childCount}개 — 컨트롤러 포함 CH2 정식 루트 14개여야 한다.", group);
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
