#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Master + 8섹터를 연 부모 하네스에서 호출하는 CH7 PlayMode 검사.
/// 도형을 S7으로 순간이동시키고 실제 팀 공개 API를 호출한다. 키 입력·수동 동선 검사는 별도다.
/// 이벤트를 직접 발신하거나 팀 내부 상태를 반사로 고치지 않는다.
/// </summary>
public static class Map4FollowupS7Checks
{
    public static IEnumerator Run(SectorController sector, PlayerMover[] players, Action<string, bool, string> record)
    {
        if (record == null) yield break;
        if (sector == null || sector.sectorId != 7 || players == null || players.Length < 3 ||
            players[0] == null || players[1] == null || players[2] == null ||
            players[0] == players[1] || players[0] == players[2] || players[1] == players[2])
        {
            record("S7-01", false, "S7 SectorController와 서로 다른 PlayerMover 3개가 필요하다.");
            yield break;
        }

        Lab_ServerRoomFlow flow = sector.GetComponentInChildren<Lab_ServerRoomFlow>(true);
        Lab_ActivateWhenPlayersReady activator = sector.GetComponentInChildren<Lab_ActivateWhenPlayersReady>(true);
        if (flow == null || activator == null || activator.zone == null)
        {
            record("S7-01", false, "S7 흐름 컴포넌트 또는 진입 활성화 구역이 없다.");
            yield break;
        }

        bool wasInactive = !flow.gameObject.activeInHierarchy;
        Rigidbody visitorBody = players[0].GetComponent<Rigidbody>();
        if (!wasInactive)
        {
            record("S7-01", false, "검사 전 이미 CH7이 활성화되어 진입으로 켜지는 과정을 검증할 수 없다.");
        }
        else if (visitorBody == null || !visitorBody.useGravity)
        {
            record("S7-01", false, "진입 도형에 중력이 켜진 Rigidbody가 없어 활성화 조건을 검증할 수 없다.");
        }
        else
        {
            Teleport(players[0], activator.zone.bounds.center);
            for (int i = 0; i < 30 && !flow.gameObject.activeInHierarchy; i++)
                yield return new WaitForFixedUpdate();
            record("S7-01", flow.gameObject.activeInHierarchy,
                "도형을 활성화 구역으로 순간이동하고 FixedUpdate 최대 30회 동안 실제 활성화 감시를 기다림.");
        }

        if (!flow.gameObject.activeInHierarchy)
        {
            record("S7-02", false, "CH7 루트가 비활성이어서 이후 흐름 검사를 실행할 수 없다.");
            yield break;
        }

        // 진입점 근처로 이동시켜 문 끼임 검사를 피한다. 역할/포트 상호작용은 공개 API 직접 호출이다.
        if (sector.spawnSlots != null && sector.spawnSlots.Length >= 3)
            for (int i = 0; i < 3; i++)
                if (sector.spawnSlots[i] != null) Teleport(players[i], sector.spawnSlots[i].position);
        yield return new WaitForFixedUpdate();

        WiringPanel entry = flow.entryPanel;
        QuizTerminal quiz = flow.quiz;
        PowerMaintenanceController power = flow.power;
        RoleAssignmentManager roles = flow.roles;
        BookPanel book = power != null && power.bookSlot != null ? power.bookSlot.GetComponent<BookPanel>() : null;
        if (entry == null || flow.entryCircuit == null || quiz == null || power == null || roles == null ||
            book == null || power.bookSlot == null || power.computerSlot == null || power.powerSlot == null ||
            power.wiringPanel == null || flow.entryDoor == null || flow.exitDoor == null)
        {
            record("S7-02", false, "ENTRY·책·문답·전력·역할 3개·문 2개의 직렬화 참조가 빠졌다.");
            yield break;
        }

        float entryY0 = flow.entryDoor.transform.position.y;
        float exitY0 = flow.exitDoor.transform.position.y;
        bool initial = entry.CurrentCircuit == flow.entryCircuit && !entry.IsLocked &&
                       power.entranceWiring == entry && power.Current == PowerMaintenanceController.State.Ready &&
                       !quiz.SubmitGateOpen && flow.entryDoor.IsAtClosedPosition && flow.exitDoor.IsAtClosedPosition;
        record("S7-02", initial, "ENTRY 런타임 배정·입구 시작 조건·Ready 제출 차단·문 2개 닫힘 확인.");
        if (!initial) yield break;

        bool content = book.pages != null && book.pages.Length == 3 &&
                       quiz.questions != null && quiz.questions.Length == 3 &&
                       book.pages[0] != null && book.pages[1] != null && book.pages[2] != null &&
                       quiz.questions[0] != null && quiz.questions[1] != null && quiz.questions[2] != null &&
                       book.pages[0].Contains("코드는 2") && book.pages[1].Contains("코드는 3") &&
                       book.pages[2].Contains("코드는 1") &&
                       quiz.questions[0].correctIndex == 1 && quiz.questions[1].correctIndex == 2 &&
                       quiz.questions[2].correctIndex == 0;
        record("S7-03", content, "가상 표본 A/B/C의 책 단서 2/3/1과 문항 정답 인덱스 1/2/0 확인.");
        if (!content) yield break;

        PlayerMover computerActor = players[2];
        power.computerSlot.HandleInteract(computerActor); // E 상호작용의 공개 로직을 직접 호출
        bool selectedBeforeStart = quiz.Select(quiz.questions[0].correctIndex, computerActor);
        QuizTerminal.SubmitResult preStart = quiz.Submit(computerActor);
        bool blockedBeforeStart = selectedBeforeStart && preStart == QuizTerminal.SubmitResult.Rejected &&
                                  quiz.CurrentIndex == 0 && !quiz.AllCleared;
        record("S7-04", blockedBeforeStart, "컴퓨터 역할 API 사용 후 시작 전 정답을 실제 Select/Submit으로 제출: 진행 0이어야 함.");
        if (!blockedBeforeStart) yield break;

        power.bookSlot.HandleInteract(players[0]);
        power.powerSlot.HandleInteract(players[1]);
        bool occupied = power.bookSlot.CurrentOwner == players[0] && power.powerSlot.CurrentOwner == players[1] &&
                        power.computerSlot.CurrentOwner == computerActor;
        bool refusedBeforeEntry = quiz.RequestAction(computerActor) &&
                                  power.Current == PowerMaintenanceController.State.Ready;
        record("S7-05", occupied && refusedBeforeEntry,
            "서로 다른 3도형을 역할 API로 순차 점유. ENTRY 미완료에서 컴퓨터 패널 시작 액션은 Ready 유지.");
        if (!occupied || !refusedBeforeEntry) yield break;

        string entryDetail;
        bool entryCompleted = CompletePanelThroughPorts(entry, out entryDetail);
        for (int i = 0; i < 180 && flow.entryDoor.transform.position.y < entryY0 + 0.2f; i++)
            yield return new WaitForFixedUpdate();
        bool entryOpened = entryCompleted && entry.IsLocked && flow.entryDoor.transform.position.y >= entryY0 + 0.2f;
        record("S7-06", entryOpened, "실제 ENTRY 포트를 WiringPanel 공개 선택 API로 연결하고 문 상승 확인. " + entryDetail);
        if (!entryOpened) yield break;

        bool actionAccepted = quiz.RequestAction(computerActor);
        bool started = actionAccepted && power.Current == PowerMaintenanceController.State.Running &&
                       quiz.SubmitGateOpen && power.wiringPanel.CurrentCircuit != null;
        record("S7-07", started, "컴퓨터 패널 RequestAction(실제 Space 경로)으로 실험 시작·전력 첫 회로 배정 확인.");
        if (!started) yield break;

        // 실제 시간 대기 대신 팀의 공개 Tick에 81초를 주어 전력 부족 상태를 재현한다.
        power.Tick(81f);
        QuizTerminal.SubmitResult lowSubmit = quiz.Submit(computerActor); // 시작 전 선택이 유지되어 있음
        bool lowLocked = power.SubmitLocked && !quiz.SubmitGateOpen &&
                         lowSubmit == QuizTerminal.SubmitResult.Rejected && quiz.CurrentIndex == 0;
        record("S7-08", lowLocked, "공개 Tick(81초)으로 전력 부족을 재현하고 실제 Submit 거부 확인.");
        if (!lowLocked) yield break;

        string powerDetail;
        bool circuitCompleted = CompletePanelThroughPorts(power.wiringPanel, out powerDetail);
        bool restored = circuitCompleted && power.Power >= power.unlockAt && !power.SubmitLocked && quiz.SubmitGateOpen;
        record("S7-09", restored, "실제 POWER 회로 포트를 연결해 전력 회복·제출 게이트 재개방 확인. " + powerDetail);
        if (!restored) yield break;

        QuizTerminal.SubmitResult q1 = quiz.Submit(computerActor); // 선택을 보존한 Q1
        Func<PlayerMover> oldResolver = roles.ControlledPlayerResolver;
        PlayerMover controlled = computerActor;
        roles.ControlledPlayerResolver = () => controlled;
        roles.Tick();
        controlled = players[0];
        roles.Tick(); // Tab 전환과 같은 공개 Tick 경로
        bool tabKept = q1 == QuizTerminal.SubmitResult.Correct && quiz.CurrentIndex == 1 &&
                       !power.computerSlot.IsPanelOpen && power.computerSlot.CurrentOwner == computerActor;
        bool bookTurned = book.NextPage() && book.PageIndex == 1;
        controlled = computerActor;
        roles.Tick();
        roles.ControlledPlayerResolver = oldResolver;
        power.computerSlot.HandleInteract(computerActor);
        bool reopened = power.computerSlot.IsPanelOpen && power.computerSlot.PanelUser == computerActor;
        record("S7-10", tabKept && bookTurned && reopened,
            "ControlledPlayerResolver+Tick으로 Tab을 모사: 컴퓨터 화면만 닫히고 역할/Q1 진행 유지, 책 2쪽 열람 후 복귀.");
        if (!tabKept || !bookTurned || !reopened) yield break;

        bool selectedQ2 = quiz.Select(quiz.questions[1].correctIndex, computerActor);
        QuizTerminal.SubmitResult q2 = quiz.Submit(computerActor);
        bool selectedQ3 = quiz.Select(quiz.questions[2].correctIndex, computerActor);
        QuizTerminal.SubmitResult q3 = quiz.Submit(computerActor);
        for (int i = 0; i < 180 && flow.exitDoor.transform.position.y < exitY0 + 0.2f; i++)
            yield return new WaitForFixedUpdate();
        bool completed = selectedQ2 && q2 == QuizTerminal.SubmitResult.Correct &&
                         selectedQ3 && q3 == QuizTerminal.SubmitResult.AllCleared && quiz.AllCleared &&
                         power.Current == PowerMaintenanceController.State.Completed &&
                         flow.exitDoor.transform.position.y >= exitY0 + 0.2f;
        record("S7-11", completed, "Q2·Q3 실제 Select/Submit 후 전력 완료와 출구 문 상승 확인.");
        if (!completed) yield break;

        // 문 끼임으로 닫힘 판정이 오염되지 않도록 세 도형을 스폰 자리로 돌린 뒤 전체 재시작한다.
        if (sector.spawnSlots != null && sector.spawnSlots.Length >= 3)
            for (int i = 0; i < 3; i++)
                if (sector.spawnSlots[i] != null) Teleport(players[i], sector.spawnSlots[i].position);
        roles.ResetChapter();
        bool resetNow = power.Current == PowerMaintenanceController.State.Ready &&
                        entry.CurrentCircuit == flow.entryCircuit && !entry.IsLocked && entry.Connections.Count == 0 &&
                        !quiz.SubmitGateOpen && !quiz.AllCleared && quiz.CurrentIndex == 0 &&
                        book.PageIndex == 0 && power.bookSlot.CurrentOwner == null &&
                        power.computerSlot.CurrentOwner == null && power.powerSlot.CurrentOwner == null;
        for (int i = 0; i < 180 && (!flow.entryDoor.IsAtClosedPosition || !flow.exitDoor.IsAtClosedPosition); i++)
            yield return new WaitForFixedUpdate();
        bool resetDoors = flow.entryDoor.IsAtClosedPosition && flow.exitDoor.IsAtClosedPosition;
        record("S7-12", resetNow && resetDoors,
            "RoleAssignmentManager.ResetChapter 실제 이벤트로 ENTRY 재배정·역할/문답/책/전력 초기화 및 두 문 닫힘 확인.");
    }

    private static void Teleport(PlayerMover player, Vector3 position)
    {
        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
        }
        player.transform.position = position;
        Physics.SyncTransforms();
    }

    private static bool CompletePanelThroughPorts(WiringPanel panel, out string detail)
    {
        detail = "";
        if (panel == null || panel.CurrentCircuit == null || panel.CurrentCircuit.pairs == null ||
            panel.outputPorts == null || panel.inputPorts == null)
        {
            detail = "회로 또는 실제 포트 참조 누락";
            return false;
        }
        foreach (WiringCircuitData.PortPair pair in panel.CurrentCircuit.pairs)
            if (pair == null)
            {
                detail = "회로 정답 쌍에 null이 있다.";
                return false;
            }
        string circuitError;
        if (!panel.CurrentCircuit.Validate(out circuitError))
        {
            detail = "회로 콘텐츠 오류: " + circuitError;
            return false;
        }

        foreach (WiringCircuitData.PortPair pair in panel.CurrentCircuit.pairs)
        {
            WiringPort output = FindPort(panel.outputPorts, pair.outputId, WiringPort.Role.Output);
            WiringPort input = FindPort(panel.inputPorts, pair.inputId, WiringPort.Role.Input);
            if (output == null || input == null || output.panel != panel || input.panel != panel)
            {
                detail = "실제 포트/패널 참조 불일치: " + pair.outputId + "→" + pair.inputId;
                return false;
            }
            panel.SelectOutput(output);
            panel.TrySelectInput(input);
            string connected;
            if (!panel.Connections.TryGetValue(pair.outputId, out connected) || connected != pair.inputId)
            {
                detail = "공개 선택 API 연결 실패: " + pair.outputId + "→" + pair.inputId;
                return false;
            }
        }

        detail = panel.CurrentCircuit.circuitId + " 실제 포트 " + panel.CurrentCircuit.pairs.Length + "쌍 완료";
        return panel.IsLocked;
    }

    private static WiringPort FindPort(WiringPort[] ports, string id, WiringPort.Role role)
    {
        foreach (WiringPort port in ports)
            if (port != null && port.portId == id && port.role == role) return port;
        return null;
    }
}
#endif
