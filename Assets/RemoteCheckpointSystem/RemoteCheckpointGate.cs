using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 원격 체크포인트 게이트 — CH2 성공 출구 뒤 구역(A)에 붙어, 퍼즐을 제한시간 안에 끝낸 팀이 A에 들어오면
/// 멀리 있는 CH4 시작의 RespawnZone(B)을 팀 체크포인트로 저장한다. 순간이동·연출 코드는 없다 — 복귀는
/// 기존 R 그대로고, 여기서는 "B를 저장하는 스위치" 역할만 한다. 상세: docs/PRD/RemoteCheckpoint.md.
///
/// 진입 판정은 같은 오브젝트의 ZoneEntryDetector(새 진입마다)를 재사용한다 — 도형 하나의 두 콜라이더를
/// 한 번으로 합치는 일을 여기서 다시 하지 않는다. 연결은 Awake에서 코드로 한다(인스펙터 배선 누락 방지).
///
/// 잠금은 퍼즐 결과를 입력으로 받는다: IsolationRescueController의 onSuccess → SetUnlocked(true),
/// onTimedOut/onAborted → SetUnlocked(false), onRestarted → RestartChapter(). 문이 열린 모습으로 성공을
/// 추측하지 않는다.
/// </summary>
[RequireComponent(typeof(ZoneEntryDetector))]
public class RemoteCheckpointGate : MonoBehaviour
{
    [Header("대상")]
    [Tooltip("저장할 RespawnZone(B)의 checkpointId. 씬에 로드된 활성 구역 중 이 ID가 정확히 하나여야 저장된다.")]
    public string targetCheckpointId;

    [Tooltip("켜진 채 시작하면 CH2 성공 없이도 저장된다 — 테스트용. 평소엔 잠금이 기본이다.")]
    public bool startUnlocked = false;

    [Header("챕터 전체 재시작 (RestartChapter — 배선은 맵 담당)")]
    [Tooltip("재시작 때 팀 체크포인트를 되돌릴 구역(예: CH2 시작). 비우면 체크포인트 없음 상태가 된다.")]
    public RespawnZone restartReturnZone;

    [Tooltip("재시작 때 잡음 기록과 깃발을 지울 구역들(B 포함 이번 시도에서 잡은 구역).")]
    public RespawnZone[] restartClearZones;

    [Header("출력")]
    [Tooltip("저장에 성공한 최초 한 번만 발신한다. 문 뒤 'R을 누르면 CH4 시작으로 이동' 안내를 켜는 데 쓴다.")]
    public UnityEvent OnCheckpointStored;

    private bool unlocked;
    private bool stored;

    private void Reset()
    {
        GetComponent<ZoneEntryDetector>().mode = ZoneEntryDetector.EntryMode.EveryNewEntry;
    }

    private void Awake()
    {
        unlocked = startUnlocked;

        ZoneEntryDetector detector = GetComponent<ZoneEntryDetector>();
        // OncePerZone이면 잠긴 동안의 첫 진입이 기회를 태워 버려, 성공 뒤 들어와도 영영 발동하지 않는다.
        if (detector.mode != ZoneEntryDetector.EntryMode.EveryNewEntry)
            Debug.LogWarning($"[RemoteCheckpointGate] '{name}'의 ZoneEntryDetector 모드가 '새 진입마다'가 아니다 — " +
                             "잠긴 동안의 첫 진입이 기회를 소모해 이후 저장되지 않을 수 있다.", this);
        detector.OnValidEntry.AddListener(OnEntry);
    }

    /// <summary>CH2 성공/실패 신호를 인스펙터로 연결하는 진입점.</summary>
    public void SetUnlocked(bool value) => unlocked = value;

    /// <summary>챕터 전체 재시작 — A를 다시 잠그고, 저장 기록을 지우고, 팀 체크포인트를 지정 구역으로 되돌린다.</summary>
    public void RestartChapter()
    {
        unlocked = false;
        stored = false;
        RespawnController.RestartCheckpoint(restartReturnZone, restartClearZones);
    }

    private void OnEntry()
    {
        if (!unlocked || stored) return;

        if (!TryFindTarget(out RespawnZone target)) return;

        // 저장에 실패하면 stored를 세우지 않는다 — B가 나중에 로드되거나 배선이 고쳐진 뒤 재진입하면 다시 시도된다.
        if (!RespawnController.TryStoreCheckpoint(target))
        {
            Debug.LogWarning($"[RemoteCheckpointGate] '{name}': '{targetCheckpointId}' 저장에 실패했다 — 이미 잡은 구역이거나 " +
                             "진행 순서 충돌, 또는 RespawnController 없음이다. 기존 체크포인트를 유지한다.", this);
            return;
        }

        stored = true;
        OnCheckpointStored?.Invoke();
    }

    /// <summary>ID가 같은 활성 RespawnZone이 정확히 하나일 때만 성공한다. "첫 번째 B"를 임의로 고르지 않는다.</summary>
    private bool TryFindTarget(out RespawnZone target)
    {
        target = null;
        if (string.IsNullOrEmpty(targetCheckpointId))
        {
            Debug.LogWarning($"[RemoteCheckpointGate] '{name}': targetCheckpointId가 비어 있어 저장하지 않는다.", this);
            return false;
        }

        List<RespawnZone> matches = new List<RespawnZone>();
        foreach (RespawnZone z in FindObjectsOfType<RespawnZone>())
            if (z.checkpointId == targetCheckpointId) matches.Add(z);

        if (matches.Count != 1)
        {
            Debug.LogWarning($"[RemoteCheckpointGate] '{name}': checkpointId '{targetCheckpointId}'인 활성 RespawnZone이 " +
                             $"{matches.Count}개라 저장하지 않는다(정확히 1개여야 한다 — B 씬이 안 로드됐거나 ID가 없거나 겹침).", this);
            return false;
        }

        target = matches[0];
        return true;
    }
}
