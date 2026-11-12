using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 참가자×구간 단위로 피격을 집계하는 재사용 컴포넌트 — FallingRockSpawner.RegisterHit이 이미 구현한
/// "카운트 → 임계 도달 시 발화 → 발화 후 0으로 리셋" 패턴을 일반화했다(docs/PRD/SectionRespawn.md §3).
/// 임계값을 1로 두면 즉시 복귀(레이저류), 2 이상이면 "한 번 더 맞으면 되돌아간다" 경고형(낙석류)이
/// 같은 컴포넌트로 표현된다 — 별도 분기를 두지 않는다.
///
/// 위험 장치(낙석·레이저·함정 등) 여러 개가 같은 구간을 공유해도, 전부 같은 SectionHitCounter
/// 인스턴스를 참조하면 피격이 구간 단위로 합산된다(스폰 지점이 여러 개인 낙석 커튼 등, §4 "피격 집계 키").
///
/// FallingRockSystem 폴더의 파일은 한 줄도 건드리지 않는다 — 기존 낙석은 자체 카운터를 계속 쓰고,
/// 신규 위험 장치부터 이 공용 컴포넌트를 참조한다(§5, 불필요한 재작업 회피).
///
/// [발화 시점에 즉시 리셋 — FallingRockSpawner와 동일한 절충]
/// "복귀 완료 후" 리셋이 원칙이지만, 이 저장소의 기존 낙석 카운터도 발화 즉시 리셋한다(되돌리지 않으면
/// 첫 임계 이후 영원히 매 피격마다 발화한다). 안전점이 전부 막혀 복귀가 불발되는 드문 경우엔 카운터가
/// 실제보다 먼저 0이 되는 오차가 생길 수 있음을 알고 쓴다 — 정식 성공/실패 콜백 루프는 만들지 않았다.
/// </summary>
public class SectionHitCounter : MonoBehaviour
{
    [System.Serializable]
    public class PlayerEvent : UnityEvent<GameObject> { }

    [System.Serializable]
    public class SectionRespawnEvent : UnityEvent<GameObject, SectionSafePoint> { }

    [Tooltip("임계 도달 시 되돌릴 안전점. 이 카운터가 들고 있는 이유는 같은 구간의 여러 위험 장치가 " +
             "항상 같은 목적지를 공유해야 하기 때문이다 — 장치마다 따로 들면 하나만 바꿔 끼웠을 때 " +
             "나머지가 어긋난다. 비워두면 경고 이벤트만 쏘고 아무 데도 보내지 않는다.")]
    public SectionSafePoint destination;

    [Tooltip("이 횟수째 피격에 발화한다(1 = 즉시 복귀, 2 = 한 번은 경고만 하고 다음에 복귀).")]
    public int hitsBeforeRespawn = 2;

    [Tooltip("같은 판정 묶음(동시 충돌 등)을 1회로 합치는 무적 시간(초). FallingRockSpawner의 " +
             "hitCooldown과 같은 역할.")]
    public float hitCooldown = 0.5f;

    [Tooltip("마지막 한 번 전 피격에 발화(경고 표시용). 이 저장소에는 아직 UI 시스템이 없어 지금은 " +
             "로그만 남긴다 — 정식 HUD가 생기면 여기에 배선하면 된다.")]
    public PlayerEvent OnWarning;

    [Tooltip("임계 도달 시 발화(인자 = 맞은 플레이어 Root, 목적지). " +
             "RespawnController.RespawnPlayer(GameObject, SectionSafePoint)에 인스펙터로 꽂는다 — " +
             "Tools > Respawn > Wire Section Hits.")]
    public SectionRespawnEvent OnThresholdReached;

    private class HitRecord
    {
        public int count;
        public float nextAllowedTime;
    }

    // 참가자별 집계 — Rigidbody를 키로 쓴다(FallingRockSpawner와 동일 관례). 맞을 때만 채워지므로
    // 플레이어 수가 늘어도 상시 비용이 없다.
    private readonly Dictionary<Rigidbody, HitRecord> hits = new Dictionary<Rigidbody, HitRecord>();

    /// <summary>위험 장치가 피격을 보고한다. 무적 시간 안이면 무시(false)하고 리턴한다.</summary>
    public bool RegisterHit(GameObject playerRoot)
    {
        if (playerRoot == null) return false;
        Rigidbody rb = playerRoot.GetComponentInParent<Rigidbody>();
        if (rb == null) return false;

        if (!hits.TryGetValue(rb, out HitRecord record))
        {
            record = new HitRecord();
            hits[rb] = record;
        }

        if (Time.time < record.nextAllowedTime) return false;
        record.nextAllowedTime = Time.time + hitCooldown;
        record.count++;

        Debug.Log($"[SectionHitCounter] '{rb.name}' → '{name}' 피격 {record.count}/{hitsBeforeRespawn}.");

        if (record.count < hitsBeforeRespawn)
        {
            OnWarning?.Invoke(playerRoot);
            return true;
        }

        // 발화 후 0으로 되돌린다 — 되돌리지 않으면 첫 임계 이후 영원히 매 피격마다 발화한다.
        record.count = 0;
        string destLabel = destination != null ? destination.sectionId : "(목적지 없음)";
        Debug.Log($"[SectionHitCounter] '{rb.name}' 임계 도달 — '{destLabel}'로 복귀 요청.");
        OnThresholdReached?.Invoke(playerRoot, destination);
        return true;
    }

    /// <summary>인스펙터 동적 배선용, RegisterHit의 반환값 없는 래퍼.</summary>
    public void RegisterHitEvent(GameObject playerRoot) => RegisterHit(playerRoot);

    /// <summary>이 구간 카운터를 특정 참가자에 대해 0으로 되돌린다 — 수동(R)이나 다른 경로로 실제
    /// 이 구간 시작점에 복귀했을 때 SectionSafePoint(counter 연결 시)나 외부 트리거가 호출한다
    /// (§4 "수동 복귀와의 상호작용").</summary>
    public void ResetFor(GameObject playerRoot)
    {
        if (playerRoot == null) return;
        Rigidbody rb = playerRoot.GetComponentInParent<Rigidbody>();
        if (rb != null && hits.TryGetValue(rb, out HitRecord record)) record.count = 0;
    }
}
