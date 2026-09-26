using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 측면 발사 함정 피격 → 리스폰 배선 도구. `PeriodicTrapSystem/Editor/PeriodicTrapRespawnWiring.cs`와
/// 동일한 패턴(같은 이유로 이 폴더에 둔다 — RespawnController.RespawnPlayer는 이미 공개된 진입점이라
/// 읽기만 하는 배선 도구는 RespawnSystem 파일을 한 줄도 고치지 않고 여기서 완전히 동작한다).
/// </summary>
public static class ProjectileTrapRespawnWiring
{
    [MenuItem("Tools/ProjectileTrapSystem/Wire Hits To Respawn")]
    public static void WireHitsToRespawn()
    {
        RespawnController controller = Object.FindObjectOfType<RespawnController>();
        if (controller == null)
        {
            Debug.LogWarning("[ProjectileTrapSystem] 씬에 RespawnController가 없어 배선할 수 없다. " +
                             "Tools > Respawn > Create Respawn Controller를 먼저 실행해라.");
            return;
        }

        ProjectileLauncher[] launchers = Object.FindObjectsOfType<ProjectileLauncher>();
        if (launchers.Length == 0)
        {
            Debug.LogWarning("[ProjectileTrapSystem] 씬에 발사구가 없다. 먼저 Create Projectile " +
                             "Launcher로 배치해라.");
            return;
        }

        int wired = 0, already = 0;
        foreach (ProjectileLauncher launcher in launchers)
        {
            Undo.RecordObject(launcher, "Wire Projectile Trap Hits");

            if (launcher.OnHazardHit == null)
                launcher.OnHazardHit = new ProjectileLauncher.PlayerHitEvent();

            if (AlreadyWired(launcher.OnHazardHit, controller))
            {
                already++;
            }
            else
            {
                UnityEventTools.AddPersistentListener<GameObject>(
                    launcher.OnHazardHit, controller.RespawnPlayer);
                wired++;
            }

            EditorUtility.SetDirty(launcher);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[ProjectileTrapSystem] 배선 완료 — 새로 꽂음 {wired}개 / 이미 꽂혀 있음 {already}개. " +
                  "탄에 맞으면 맞은 도형이 페이드 리스폰된다.", controller);
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
