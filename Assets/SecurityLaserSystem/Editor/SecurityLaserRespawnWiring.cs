using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 보안 레이저 피격 → 리스폰 배선 도구. `ProjectileTrapSystem/Editor/ProjectileTrapRespawnWiring.cs`·
/// `PeriodicTrapSystem/Editor/PeriodicTrapRespawnWiring.cs`와 완전히 같은 패턴(같은 이유로 이 폴더에
/// 둔다 — RespawnController.RespawnPlayer는 이미 공개된 진입점이라 읽기만 하는 배선 도구는
/// RespawnSystem 파일을 한 줄도 고치지 않고 여기서 완전히 동작한다). CH4/CH5 둘 다 LaserBeam 하나로
/// 판정하므로 FindObjectsOfType&lt;LaserBeam&gt;() 한 번으로 전부 잡는다.
/// </summary>
public static class SecurityLaserRespawnWiring
{
    [MenuItem("Tools/SecurityLaserSystem/Wire Hits To Respawn")]
    public static void WireHitsToRespawn()
    {
        RespawnController controller = Object.FindObjectOfType<RespawnController>();
        if (controller == null)
        {
            Debug.LogWarning("[SecurityLaserSystem] 씬에 RespawnController가 없어 배선할 수 없다. " +
                             "Tools > Respawn > Create Respawn Controller를 먼저 실행해라.");
            return;
        }

        LaserBeam[] beams = Object.FindObjectsOfType<LaserBeam>();
        if (beams.Length == 0)
        {
            Debug.LogWarning("[SecurityLaserSystem] 씬에 보안 레이저가 없다. 먼저 Create Character " +
                             "Locked Laser 또는 Create Fixed Periodic Laser로 배치해라.");
            return;
        }

        int wired = 0, already = 0;
        foreach (LaserBeam beam in beams)
        {
            Undo.RecordObject(beam, "Wire Security Laser Hits");

            if (beam.OnHazardHit == null)
                beam.OnHazardHit = new LaserBeam.PlayerHitEvent();

            if (AlreadyWired(beam.OnHazardHit, controller))
            {
                already++;
            }
            else
            {
                UnityEventTools.AddPersistentListener<GameObject>(beam.OnHazardHit, controller.RespawnPlayer);
                wired++;
            }

            EditorUtility.SetDirty(beam);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[SecurityLaserSystem] 배선 완료 — 새로 꽂음 {wired}개 / 이미 꽂혀 있음 {already}개. " +
                  "활성 광선에 맞으면 맞은 도형이 페이드 리스폰된다.", controller);
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
