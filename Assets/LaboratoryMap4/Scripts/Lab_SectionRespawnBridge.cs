using UnityEngine;

/// <summary>
/// 섹터 씬의 SectionHitCounter를 Master 씬의 RespawnController에 **실행 중에** 잇는 다리(공용 어댑터).
///
/// [왜 필요한가] 팀 방식(PathChaserMenuItem.BuildRig·RespawnWiringMenuItem)은 카운터의
/// OnThresholdReached에 RespawnController.RespawnPlayer를 인스펙터 영구 배선으로 건다. 그런데 맵4는
/// RespawnController가 Master 씬에, 카운터가 섹터 씬에 있다 — Unity는 씬 사이 참조를 저장하지 못해
/// 영구 배선이 불가능하다(F1-2 "씬 사이 직접 참조 금지"와도 같은 이유). 그래서 섹터 빌더는 영구 배선을
/// 비워 두고, 이 컴포넌트가 Start에서 같은 메서드를 런타임 리스너로 붙인다. 팀 파일은 건드리지 않는다.
///
/// 이미 영구 배선이 있는 카운터(누가 손으로 걸어 둔 것)는 건너뛴다 — 두 번 발화하지 않게.
/// </summary>
public class Lab_SectionRespawnBridge : MonoBehaviour
{
    public int WiredCount { get; private set; }

    void Start()
    {
        RespawnController respawn = Object.FindObjectOfType<RespawnController>();
        if (respawn == null)
        {
            Debug.LogError($"[Lab_SectionRespawnBridge] RespawnController를 찾지 못했다 — '{gameObject.scene.name}'의 " +
                           "구간 복귀가 동작하지 않는다(Master 씬이 함께 로드됐는지 확인).", this);
            return;
        }

        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            foreach (SectionHitCounter counter in root.GetComponentsInChildren<SectionHitCounter>(true))
            {
                if (counter.OnThresholdReached == null)
                    counter.OnThresholdReached = new SectionHitCounter.SectionRespawnEvent();
                if (counter.OnThresholdReached.GetPersistentEventCount() > 0) continue;

                counter.OnThresholdReached.AddListener(respawn.RespawnPlayer);
                WiredCount++;
            }
        }
        Debug.Log($"[Lab_SectionRespawnBridge] '{gameObject.scene.name}' 구간 카운터 {WiredCount}개를 RespawnController에 연결했다.", this);
    }
}
