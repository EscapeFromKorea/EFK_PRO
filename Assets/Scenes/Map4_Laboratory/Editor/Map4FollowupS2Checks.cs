#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

/// <summary>맵4 후속 CH2 통합 검사. 팀 기믹의 공개 입력과 실제 문 물리를 사용한다.</summary>
public static class Map4FollowupS2Checks
{
    public static IEnumerator Run(SectorController sector, PlayerMover[] players, Action<string, bool, string> record)
    {
        if (sector == null || players == null || players.Length != 3 || record == null)
        {
            record?.Invoke("S2/setup", false, "섹터와 도형 3개가 필요합니다.");
            yield break;
        }

        IsolationRescueController ctrl = sector.GetComponentInChildren<IsolationRescueController>(true);
        IsolationCaptureTrigger capture = sector.GetComponentInChildren<IsolationCaptureTrigger>(true);
        IsolationEntryDoor entry = sector.GetComponentInChildren<IsolationEntryDoor>(true);
        IsolationReadyButton[] ready = sector.GetComponentsInChildren<IsolationReadyButton>(true);
        SequenceLever[] levers = sector.GetComponentsInChildren<SequenceLever>(true);
        ClueBoard[] clues = sector.GetComponentsInChildren<ClueBoard>(true);
        CorrespondenceMap map = sector.GetComponentInChildren<CorrespondenceMap>(true);
        PowerHoldSwitch power = sector.GetComponentInChildren<PowerHoldSwitch>(true);
        ReleaseButton release = sector.GetComponentInChildren<ReleaseButton>(true);
        Lab_IsolationFlow flow = sector.GetComponentInChildren<Lab_IsolationFlow>(true);
        doorPhysics s3 = sector.GetComponentsInChildren<doorPhysics>(true).FirstOrDefault(d => d.name == "DOOR_S2_ExitS3");
        doorPhysics s4 = sector.GetComponentsInChildren<doorPhysics>(true).FirstOrDefault(d => d.name == "DOOR_S2_ExitS4");
        bool complete = ctrl != null && capture != null && entry != null && ready.Length == 2 && levers.Length == 3 &&
                        clues.Length == 2 && map != null && power != null && release != null && flow != null && s3 != null && s4 != null;
        record("S2/정식부품", complete, complete ? "입구문·S3/S4문·2단계 퍼즐·S4 통과 감지 구역 확인" : "필수 부품 누락");
        if (!complete) yield break;

        record("S2/콘텐츠", ctrl.ValidateContent(out string contentError) && ctrl.StageCount == 2 &&
            Mathf.Approximately(ctrl.Timer.duration, 120f) && map.symbolNames.Length == 3,
            $"단계={ctrl.StageCount}, 기호={map.symbolNames.Length}, 제한={ctrl.Timer.duration}, 오류={contentError}");

        PlayerMover inside = players[0], outside = players[1], spare = players[2];
        Collider insideCol = PlayerCollider(inside), outsideCol = PlayerCollider(outside);
        if (insideCol == null || outsideCol == null)
        {
            record("S2/플레이어태그", false, "Player 태그 콜라이더 부족");
            yield break;
        }
        IsolationReadyButton readyIn = ready.FirstOrDefault(x => x.inside);
        IsolationReadyButton readyOut = ready.FirstOrDefault(x => !x.inside);
        if (readyIn == null || readyOut == null) { record("S2/준비버튼", false, "안팎 버튼 구분 실패"); yield break; }
        Array.Sort(levers, (a, b) => a.leverNumber.CompareTo(b.leverNumber));
        Array.Sort(clues, (a, b) => a.stageIndex.CompareTo(b.stageIndex));

        // 첫 시도는 마감 직후 열린 S3 경로를 확인한다. 이벤트 Invoke 없이 팀 공개 입력을 밟는다.
        yield return StartRound(sector, ctrl, capture, entry, readyIn, readyOut, inside, outside, spare, insideCol, record, "시간초과");
        if (ctrl.Current != IsolationRescueController.State.InProgress) yield break;
        // 선택되지 않은 도형이 문닫힘 직후 실내에 들어와도 기존 역할 복귀 경로로 바깥에 돌아오는지 본다.
        Move(spare, sector.transform.TransformPoint(new Vector3(-23f, 1f, 48f)));
        Physics.SyncTransforms();
        for (int i = 0; i < 350; i++)
        {
            Vector3 local = sector.transform.InverseTransformPoint(spare.transform.position);
            if (local.x > -14.1f) break;
            yield return new WaitForFixedUpdate();
        }
        Vector3 spareLocal = sector.transform.InverseTransformPoint(spare.transform.position);
        record("S2/비선정도형복귀", spareLocal.x > -14.1f,
            $"비선정 위치 x={spareLocal.x:0.##}, 역할 복귀={flow.roleReturn != null}");
        ctrl.Timer.Clock = () => ctrl.Timer.Deadline + 0.01f;
        ctrl.CheckDeadline();
        ctrl.Timer.Clock = () => Time.time;
        yield return new WaitForSeconds(2.2f);
        bool timeoutSafe = ctrl.Current == IsolationRescueController.State.TimedOut &&
                           entry.Current == IsolationEntryDoor.Phase.Open && !s3.IsAtClosedPosition &&
                           !flow.TryTransfer(inside.gameObject);
        record("S2/시간초과복구", timeoutSafe,
            $"상태={ctrl.Current}, 입구={entry.Current}, S3닫힘={s3.IsAtClosedPosition}, 이동={flow.HasTransferred}");

        ctrl.FullRestart();
        yield return new WaitForSeconds(2.2f);
        record("S2/재시작", ctrl.Current == IsolationRescueController.State.Idle && s3.IsAtClosedPosition,
            $"상태={ctrl.Current}, S3닫힘={s3.IsAtClosedPosition}");

        yield return StartRound(sector, ctrl, capture, entry, readyIn, readyOut, inside, outside, spare, insideCol, record, "성공");
        if (ctrl.Current != IsolationRescueController.State.InProgress) yield break;

        // 공개 조회 API로 역할별 정보 분리도 확인한다. 실제 조작은 Tab으로 같은 두 도형을 순차 선택한다.
        Move(inside, clues[0].transform.position + Vector3.up);
        Move(outside, map.transform.position + Vector3.up);
        Physics.SyncTransforms();
        clues[0].HandleEnter(insideCol);
        map.HandleEnter(outsideCol);
        bool info = clues[0].ShouldShowTo(inside) && !clues[0].ShouldShowTo(outside) &&
                    map.ShouldShowTo(outside) && !map.ShouldShowTo(inside);
        record("S2/역할정보", info, $"안쪽={clues[0].BuildText()}, 바깥={map.BuildText().Replace('\n', ' ')}");

        bool ordered = true;
        for (int stage = 0; stage < 2; stage++)
        {
            int[] answer = ctrl.CurrentOrder.ToArray();
            for (int i = 0; i < answer.Length; i++)
            {
                SequenceLever lever = levers[answer[i] - 1];
                Move(outside, lever.transform.position + new Vector3(0f, 0.5f, 0f));
                lever.Evaluate(0f);
                ordered &= lever.Evaluate(1f);
                lever.Evaluate(0f);
            }
            ordered &= stage == 0 ? ctrl.StageIndex == 1 : ctrl.Current == IsolationRescueController.State.AwaitFinalRelease;
        }
        record("S2/2단계레버", ordered, $"상태={ctrl.Current}, 단계={ctrl.StageIndex + 1}, 레버=3종");
        if (ctrl.Current != IsolationRescueController.State.AwaitFinalRelease) yield break;

        Move(outside, power.transform.position + Vector3.up);
        power.HandleEnter(outsideCol);
        outside.SetControlled(false);
        inside.SetControlled(true);
        bool heldAcrossSwitch = power.IsOn && ctrl.PowerOn && !outside.IsControlled && inside.IsControlled;
        record("S2/Tab전원유지", heldAcrossSwitch, $"패드={power.IsOn}, 컨트롤러={ctrl.PowerOn}, 안쪽조작={inside.IsControlled}");
        release.Press();
        bool success = ctrl.Current == IsolationRescueController.State.Success && !flow.HasTransferred;
        record("S2/최종해제", success, $"상태={ctrl.Current}, 통과전이동={flow.HasTransferred}");
        if (!success) yield break;

        yield return new WaitForSeconds(2.2f);
        Move(inside, flow.transform.position);
        Physics.SyncTransforms();
        bool transferred = flow.HasTransferred || flow.TryTransfer(inside.gameObject);
        Map4Director director = UnityEngine.Object.FindObjectOfType<Map4Director>();
        SectorController ch4 = null;
        if (director != null) director.TryGetSector(4, out ch4);
        bool atDistinctSlots = AtDistinctSpawnSlots(players, ch4, 0.5f);
        record("S2/S4첫통과3인", transferred && flow.HasTransferred && atDistinctSlots && !s4.IsAtClosedPosition,
            $"이동={flow.HasTransferred}, CH4서로다른슬롯0.5U={atDistinctSlots}, S4닫힘={s4.IsAtClosedPosition}");
        record("S2/중복통과차단", !flow.TryTransfer(inside.gameObject), $"한번만={flow.HasTransferred}");

        // 새 체크포인트를 로그나 private 상태가 아닌 실제 공용 복귀 행동으로 검증한다.
        RespawnController respawn = UnityEngine.Object.FindObjectOfType<RespawnController>();
        RespawnZone cp4 = ch4 != null ? ch4.GetComponentsInChildren<RespawnZone>(true)
            .FirstOrDefault(z => z.name == "CP_S4_Start") : null;
        if (respawn == null || cp4 == null)
            record("S2/CH4체크포인트복귀", false, "공용 RespawnController 또는 CP_S4_Start 없음");
        else
        {
            Vector3 ground = cp4.FindGroundPoint();
            Move(inside, sector.transform.TransformPoint(new Vector3(-6f, 1f, 42f)));
            Physics.SyncTransforms();
            respawn.RespawnPlayer(inside.gameObject);
            bool returned = false;
            float horizontal = float.PositiveInfinity, footError = float.PositiveInfinity;
            for (int i = 0; i < 350; i++)
            {
                yield return new WaitForFixedUpdate();
                Physics.SyncTransforms();
                Vector3 p = inside.transform.position;
                horizontal = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(ground.x, ground.z));
                float foot = float.PositiveInfinity;
                foreach (Collider col in inside.GetComponentsInChildren<Collider>())
                    if (col.enabled && !col.isTrigger) foot = Mathf.Min(foot, col.bounds.min.y);
                footError = foot - ground.y;
                if (horizontal <= 0.5f && footError >= -0.1f && footError <= 0.6f)
                {
                    returned = true;
                    break;
                }
            }
            record("S2/CH4체크포인트복귀", returned,
                $"CP4 수평거리={horizontal:0.###}U, 발바닥-지면={footError:0.###}U");
        }
        ctrl.FullRestart();
        record("S2/재시작2회째", ctrl.Attempt >= 2 && !flow.HasTransferred,
            $"시도={ctrl.Attempt}, 이동플래그={flow.HasTransferred}");
    }

    private static IEnumerator StartRound(SectorController sector, IsolationRescueController ctrl,
        IsolationCaptureTrigger capture, IsolationEntryDoor entry, IsolationReadyButton readyIn,
        IsolationReadyButton readyOut, PlayerMover inside, PlayerMover outside, PlayerMover spare,
        Collider insideCol, Action<string, bool, string> record, string attempt)
    {
        Move(inside, sector.transform.TransformPoint(new Vector3(-11f, 1f, 42f)));
        Move(outside, sector.transform.TransformPoint(new Vector3(-6f, 1f, 52f)));
        Move(spare, sector.transform.TransformPoint(new Vector3(5f, 1f, 35f)));
        Physics.SyncTransforms();
        yield return new WaitForSeconds(2.2f); // 진입문 기본 열림 위치 도달
        capture.HandleEnter(insideCol);
        Move(inside, sector.transform.TransformPoint(new Vector3(-23f, 1f, 42f)));
        Physics.SyncTransforms();
        capture.HandleExit(insideCol);
        PlayerMover selected = capture.ResolvePending();
        record($"S2/{attempt}진입포획", selected == inside && ctrl.InsidePlayer == inside.gameObject &&
            ctrl.Current == IsolationRescueController.State.Preparing,
            $"선정={(selected != null ? selected.name : "없음")}, 상태={ctrl.Current}");
        for (int i = 0; i < 350 && entry.Current != IsolationEntryDoor.Phase.Closed; i++)
            yield return new WaitForFixedUpdate();
        record($"S2/{attempt}안전닫힘", entry.Current == IsolationEntryDoor.Phase.Closed &&
            ctrl.Current == IsolationRescueController.State.Preparing,
            $"문={entry.Current}, 상태={ctrl.Current}");
        if (entry.Current != IsolationEntryDoor.Phase.Closed) yield break;
        inside.SetControlled(true); outside.SetControlled(false); spare.SetControlled(false);
        readyIn.Press();
        inside.SetControlled(false); outside.SetControlled(true);
        readyOut.Press();
        record($"S2/{attempt}양쪽준비", ctrl.Current == IsolationRescueController.State.InProgress,
            $"상태={ctrl.Current}, 안팎 순차 조작");
    }

    private static Collider PlayerCollider(PlayerMover mover) =>
        mover != null ? mover.GetComponentsInChildren<Collider>().FirstOrDefault(c => c.CompareTag("Player")) : null;

    private static bool AtDistinctSpawnSlots(PlayerMover[] players, SectorController sector, float tolerance)
    {
        if (players == null || players.Length != 3 || sector == null || sector.spawnSlots == null || sector.spawnSlots.Length < 3)
            return false;
        bool[] taken = new bool[3];
        foreach (PlayerMover player in players)
        {
            if (player == null) return false;
            int match = -1;
            for (int i = 0; i < 3; i++)
            {
                Transform slot = sector.spawnSlots[i];
                if (taken[i] || slot == null) continue;
                Vector3 p = player.transform.position, s = slot.position;
                float horizontal = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(s.x, s.z));
                if (horizontal <= tolerance) { match = i; break; }
            }
            if (match < 0) return false;
            taken[match] = true;
        }
        return taken[0] && taken[1] && taken[2];
    }

    private static void Move(PlayerMover mover, Vector3 position)
    {
        Rigidbody rb = mover != null ? mover.GetComponent<Rigidbody>() : null;
        if (rb == null) return;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = position;
        rb.transform.position = position;
    }
}
#endif
