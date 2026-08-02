using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 낙석 피격 → 리스폰 배선 도구.
///
/// FallingRockSystem은 "누적 피격이 임계를 넘으면 OnHitThresholdExceeded(맞은 플레이어 Root)를
/// 쏘고 끝"까지만 구현하고 <b>리스폰 동작을 일부러 비워 뒀다</b>(그쪽 CLAUDE.md: "여기에 리스폰을
/// 구현하지 마라"). 비워 둔 자리를 채우는 것이 이 메뉴이고, 채우는 방식도 그쪽이 지정한 대로
/// <b>인스펙터 이벤트 배선</b>이다 — 리스폰 컨트롤러가 런타임에 스포너를 찾아 자동 구독하는
/// 방식으로 만들지 않았다. 자동 구독은 기획자가 인스펙터에서 끄고 켤 수 없고, 손으로 배선한
/// 씬에서는 이중 발화가 된다.
///
/// [왜 씬 YAML을 직접 고치지 않고 메뉴인가]
/// UnityEvent의 persistent call은 대상 컴포넌트의 fileID·메서드명·인자 모드가 맞물린 구조라
/// 손으로 쓰면 조용히 어긋나기 쉽고, 무엇보다 <b>스포너나 컨트롤러를 다시 만들면 배선이 끊긴다.</b>
/// 메뉴는 몇 번이고 다시 돌릴 수 있고, 이미 꽂혀 있으면 건너뛴다.
///
/// FallingRockSystem 폴더의 파일은 한 줄도 수정하지 않는다 — 읽고 씬 데이터만 쓴다.
/// </summary>
public static class RespawnWiringMenuItem
{
    // 낙석에 몇 번 맞으면 되돌릴 것인가. 스포너 기본값은 0(비활성)이라, 배선만 하고 이 값을
    // 안 올리면 이벤트가 영영 안 쏜다. 이미 기획자가 값을 넣어 뒀으면 덮어쓰지 않는다.
    private const int DefaultHitsBeforeRespawn = 3;

    [MenuItem("Tools/Respawn/Wire Falling Rock Hits")]
    public static void WireFallingRockHits()
    {
        RespawnController controller = Object.FindObjectOfType<RespawnController>();
        if (controller == null)
        {
            Debug.LogWarning("[Respawn] 씬에 RespawnController가 없어 배선할 수 없다. " +
                             "Tools > Respawn > Create Respawn Controller를 먼저 실행해라.");
            return;
        }

        FallingRockSpawner[] spawners = Object.FindObjectsOfType<FallingRockSpawner>();
        if (spawners.Length == 0)
        {
            Debug.LogWarning("[Respawn] 씬에 FallingRockSpawner가 없다. 낙석 커튼을 먼저 만들어라 " +
                             "(Tools > FallingRock > Create Rock Curtain).");
            return;
        }

        int wired = 0, already = 0;
        foreach (FallingRockSpawner spawner in spawners)
        {
            Undo.RecordObject(spawner, "Wire Falling Rock Hits");

            if (spawner.OnHitThresholdExceeded == null)
                spawner.OnHitThresholdExceeded = new FallingRockSpawner.PlayerHitEvent();

            if (AlreadyWired(spawner.OnHitThresholdExceeded, controller))
            {
                already++;
            }
            else
            {
                // 동적 인자 배선 — 이벤트가 넘기는 "맞은 플레이어 Root"가 그대로 인자로 들어간다.
                // (정적 인자로 꽂히면 언제나 같은 오브젝트만 리스폰돼 조용히 틀린다.)
                UnityEventTools.AddPersistentListener<GameObject>(
                    spawner.OnHitThresholdExceeded, controller.RespawnPlayer);
                wired++;
            }

            // 임계가 0이면 이벤트 자체가 안 쏜다 — 배선만 하고 끝내면 "꽂았는데 아무 일도 안 난다"가
            // 된다. 기획자가 이미 값을 넣어 뒀으면 존중한다.
            if (spawner.hitsBeforeRespawn <= 0)
            {
                spawner.hitsBeforeRespawn = DefaultHitsBeforeRespawn;
                Debug.Log($"[Respawn] '{spawner.name}'의 hitsBeforeRespawn이 0(비활성)이라 " +
                          $"{DefaultHitsBeforeRespawn}으로 올렸다 — 인스펙터에서 조정해라.", spawner);
            }

            EditorUtility.SetDirty(spawner);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[Respawn] 낙석 피격 배선 완료 — 새로 꽂음 {wired}개 / 이미 꽂혀 있음 {already}개. " +
                  "누적 피격이 임계를 넘으면 맞은 도형이 페이드 리스폰된다(카운트는 발화 후 0으로 " +
                  "돌아간다 = '리스폰 이후 다시 N번').", controller);
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
