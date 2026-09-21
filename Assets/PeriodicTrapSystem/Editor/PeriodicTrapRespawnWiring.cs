using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 주기형 물리 함정 피격 → 리스폰 배선 도구. FallingRockSystem이 확립한 방식
/// (`RespawnSystem/Editor/RespawnWiringMenuItem.cs`)과 동일한 패턴이다 — UnityEvent의 persistent
/// call을 손으로 쓰지 않고 메뉴로 만든다(대상 fileID·메서드명·인자 모드가 맞물린 구조라 손으로
/// 쓰면 조용히 어긋나고, 트랩을 다시 만들면 배선이 끊기므로 몇 번이고 다시 돌릴 수 있는 메뉴가
/// 필요하다 — 이미 꽂혀 있으면 건너뛴다).
///
/// [배선 도구를 `RespawnSystem/Editor`가 아니라 이 폴더에 두는 이유] 낙석은 배선 메뉴가
/// `RespawnSystem/Editor` 쪽에 있지만, 그건 그 폴더에 파일을 새로 만드는 교차 폴더 작업이라 저장소
/// 하드 룰(사용자 허가 필요)에 걸린다. `RespawnController.RespawnPlayer`는 이미 다른 기믹이 부르도록
/// 공개된 진입점이라(`RespawnSystem/CLAUDE.md`), 읽기만 하는 이 배선 도구는 이 폴더 안에 두는
/// 것만으로 완전히 동작한다 — RespawnSystem 파일은 한 줄도 고치지 않는다.
///
/// `PeriodicTrapBase.OnHazardHit`이 세 서브클래스(AxeTrap/HammerTrap/SpikeTrap) 공통 필드라
/// `FindObjectsOfType&lt;PeriodicTrapBase&gt;()` 한 번으로 세 유형을 전부 잡는다.
/// </summary>
public static class PeriodicTrapRespawnWiring
{
    [MenuItem("Tools/PeriodicTrapSystem/Wire Hits To Respawn")]
    public static void WireHitsToRespawn()
    {
        RespawnController controller = Object.FindObjectOfType<RespawnController>();
        if (controller == null)
        {
            Debug.LogWarning("[PeriodicTrapSystem] 씬에 RespawnController가 없어 배선할 수 없다. " +
                             "Tools > Respawn > Create Respawn Controller를 먼저 실행해라.");
            return;
        }

        PeriodicTrapBase[] traps = Object.FindObjectsOfType<PeriodicTrapBase>();
        if (traps.Length == 0)
        {
            Debug.LogWarning("[PeriodicTrapSystem] 씬에 주기형 함정이 없다. 먼저 Create Axe/Hammer/" +
                             "Spike Trap으로 배치해라.");
            return;
        }

        int wired = 0, already = 0;
        foreach (PeriodicTrapBase trap in traps)
        {
            Undo.RecordObject(trap, "Wire Periodic Trap Hits");

            if (trap.OnHazardHit == null)
                trap.OnHazardHit = new PeriodicTrapBase.PlayerHitEvent();

            if (AlreadyWired(trap.OnHazardHit, controller))
            {
                already++;
            }
            else
            {
                // 동적 인자 배선 — 이벤트가 넘기는 "맞은 플레이어 Root"가 그대로 인자로 들어간다
                // (정적 인자로 꽂히면 언제나 같은 오브젝트만 리스폰돼 조용히 틀린다).
                UnityEventTools.AddPersistentListener<GameObject>(
                    trap.OnHazardHit, controller.RespawnPlayer);
                wired++;
            }

            EditorUtility.SetDirty(trap);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[PeriodicTrapSystem] 배선 완료 — 새로 꽂음 {wired}개 / 이미 꽂혀 있음 {already}개. " +
                  "위험부에 맞으면 맞은 도형이 페이드 리스폰된다.", controller);
    }

    private static bool AlreadyWired(UnityEventBase evt, RespawnController controller)
    {
        for (int i = 0; i < evt.GetPersistentEventCount(); i++)
            if (ReferenceEquals(evt.GetPersistentTarget(i), controller) &&
                evt.GetPersistentMethodName(i) == nameof(RespawnController.RespawnPlayer))
                return true;
        return false;
    }
}
