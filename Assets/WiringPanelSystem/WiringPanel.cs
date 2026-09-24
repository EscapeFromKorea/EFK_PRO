using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 발신자 — 포트 선택 상태, 연결된 선 목록, 완료 여부를 갖는다. 완료 시 신호만 쏘고 그 신호를
/// 문이 받을지 전력 유지 장치가 받을지는 모른다(저장소 공통 발신자-수신자 분리 원칙,
/// docs/PRD/WiringPanel.md §1, §3). 실제 전압 계산·물리 케이블은 다루지 않는다 — "규칙표를 읽고
/// 맞는 쌍을 고른다"는 인지 과제만 구현한다.
///
/// [완료 신호 = UnityEvent&lt;WiringPanel, int, int, string&gt;]
/// {장치, 시도, 배정번호, 회로ID}를 4개 타입 인자로 그대로 얹는다(§3). RoleAssignmentManager의
/// IRolePauseReceiver와 달리 인터페이스를 쓰지 않는 이유: 여기 신호는 "이산 이벤트 1종"이지
/// WindupAxle처럼 여러 메서드를 갖는 다중 수신자 계약이 아니다 — PRD 자체도 "UnityEvent(또는
/// 강타입 콜백)"이라고 이미 UnityEvent 쪽을 예시로 든다. 입구 문이든 전력 유지 장치든 인스펙터에서
/// 이 이벤트에 동적 인자로 건다(FallingRockSpawner→RespawnController 배선과 같은 관례).
///
/// [수신자 멱등 처리는 이 컴포넌트의 책임이 아니다]
/// "같은 신호 재전달 시 무시"는 수신자가 {장치, 시도, 배정번호, 회로ID}로 판단할 몫이다(§4). 이
/// 컴포넌트는 완료마다 시도(attempt) 번호를 증가시켜 신호를 정확히 1회 발신하는 것까지만 보장한다.
///
/// [출력 재연결 — 자동 해제]
/// "이미 연결된 출력을 다른 입력으로 바꾸려면 먼저 기존 선을 해제해야 한다"(§4)를, 이미 연결된
/// 출력을 다시 선택하면 그 연결이 자동으로 풀리고 선택 상태로 돌아가는 것으로 구현한다 — 별도
/// "선 끊기" 버튼을 PRD가 요구하지 않으므로 재선택 자체가 해제 동작을 겸한다.
/// </summary>
public class WiringPanel : MonoBehaviour
{
    [System.Serializable]
    public class CompletionEvent : UnityEvent<WiringPanel, int, int, string> { }

    [Tooltip("이 패널의 출력 포트들(OUT_A/B/C). 인스펙터에서 직접 연결한다.")]
    public WiringPort[] outputPorts;

    [Tooltip("이 패널의 입력 포트들(IN_1/2/3). 인스펙터에서 직접 연결한다.")]
    public WiringPort[] inputPorts;

    [Tooltip("모든 정답 쌍을 맞히면 발신(장치=this, 시도, 배정번호, 회로ID). 문/전력 유지 장치가 " +
             "인스펙터에서 동적 인자로 구독한다.")]
    public CompletionEvent OnCircuitCompleted;

    private WiringCircuitData currentCircuit;
    private int assignmentNumber;
    private int attemptCount;
    private bool locked;
    private WiringPort selectedOutput;

    // outputId -> inputId. 완료 신호 발신 후에도 잠긴 채로 유지된다(§4 "완료 후 잠금").
    private readonly Dictionary<string, string> connections = new Dictionary<string, string>();

    public bool IsLocked => locked;
    public WiringCircuitData CurrentCircuit => currentCircuit;
    public WiringPort SelectedOutput => selectedOutput;
    public IReadOnlyDictionary<string, string> Connections => connections;

    /// <summary>새 회로를 배정한다 — 연결 0/선택 0/배정번호 갱신으로 리셋한다(§4 "완료 후 잠금":
    /// 유지 패널은 다음 배정 전까지 잠금, 다음 배정 시 리셋). 콘텐츠가 유효하지 않으면 거부하고
    /// 기존 상태(이전 회로·잠금 여부)를 그대로 둔다 — 잘못된 콘텐츠로 시작하지 않는다(§4 "콘텐츠 검증").</summary>
    public bool AssignCircuit(WiringCircuitData circuit, int newAssignmentNumber)
    {
        if (circuit == null)
        {
            Debug.LogError("[Wiring] null 회로는 배정할 수 없다.", this);
            return false;
        }
        if (!circuit.Validate(out string error))
        {
            Debug.LogError($"[Wiring] 회로 콘텐츠 검증 실패 — {error} 시작을 거부한다.", this);
            return false;
        }

        currentCircuit = circuit;
        assignmentNumber = newAssignmentNumber;
        connections.Clear();
        selectedOutput = null;
        locked = false;

        Debug.Log($"[Wiring] '{name}' — 회로 '{circuit.circuitId}'(배정 #{newAssignmentNumber}) 배정, 리셋됨.", this);
        return true;
    }

    /// <summary>출력 포트를 선택한다. 이미 연결된 출력이면 그 연결을 해제하고 선택 상태로 돌아간다
    /// (§4 "출력 재연결" — 클래스 요약 참고).</summary>
    public void SelectOutput(WiringPort output)
    {
        if (locked || currentCircuit == null || output == null || output.role != WiringPort.Role.Output) return;

        if (connections.ContainsKey(output.portId))
        {
            connections.Remove(output.portId);
            Debug.Log($"[Wiring] '{name}' — 출력 '{output.portId}' 기존 연결 해제.", this);
        }

        selectedOutput = output;
    }

    /// <summary>선택된 출력에 이 입력을 짝짓는다. 정답이면 연결이 남고 출력 선택이 풀린다. 오답이면
    /// 새 선을 저장하지 않고 출력 선택만 해제한다 — 기존 정답 선은 그대로 유지된다(§4 "오답 처리").</summary>
    public void TrySelectInput(WiringPort input)
    {
        if (locked || currentCircuit == null || input == null || input.role != WiringPort.Role.Input) return;
        if (selectedOutput == null) return; // 출력을 먼저 골라야 한다.

        // 입력 포트 중복 점유 — 한 포트를 두 선이 동시에 점유할 수 없다(§4).
        foreach (KeyValuePair<string, string> kv in connections)
        {
            if (kv.Value == input.portId)
            {
                Debug.Log($"[Wiring] '{name}' — 입력 '{input.portId}'는 이미 사용 중이다.", this);
                return;
            }
        }

        bool correct = currentCircuit.IsCorrectPair(selectedOutput.portId, input.portId);
        if (correct)
        {
            connections[selectedOutput.portId] = input.portId;
            Debug.Log($"[Wiring] '{name}' — '{selectedOutput.portId}' → '{input.portId}' 연결.", this);
            selectedOutput = null;
            CheckCompletion();
        }
        else
        {
            Debug.Log($"[Wiring] '{name}' — 오답. 대응표를 다시 확인하세요.", this);
            selectedOutput = null;
        }
    }

    /// <summary>W_CANCEL이 호출 — 현재 출력 선택만 취소한다. 이미 맺어진 연결에는 영향 없다.</summary>
    public void CancelSelection() => selectedOutput = null;

    private void CheckCompletion()
    {
        if (currentCircuit.pairs == null || connections.Count < currentCircuit.pairs.Length) return;

        locked = true;
        attemptCount++;
        Debug.Log($"[Wiring] '{name}' — 회로 '{currentCircuit.circuitId}' 완료(시도 #{attemptCount}). 신호 발신.", this);
        OnCircuitCompleted?.Invoke(this, attemptCount, assignmentNumber, currentCircuit.circuitId);
    }
}
