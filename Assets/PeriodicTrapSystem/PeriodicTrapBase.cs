using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 주기형 물리 함정(도끼·망치·가시) 공용 뼈대. 세 유형의 운동/판정 로직은 각자 독립 컴포넌트
/// (<see cref="AxeTrap"/>/<see cref="HammerTrap"/>/<see cref="SpikeTrap"/>)로 남기고, 이 클래스는
/// "위상 지연·유효 접촉 1회 집계·끼임 안전 정지"만 공유한다. 상세: docs/PRD/PeriodicTraps.md.
///
/// [끼임 안전 정지 (§5 확정)] 무피해 이동부(위험하지 않은 상태로 움직이는 콜라이더)가 플레이어를
/// 고정 벽/바닥 사이에 압착하려 하면 그 스텝의 이동을 건너뛴다. 판정은 "이동부와 겹쳐 있는 플레이어
/// 기준, 이동 방향으로 아주 짧은 거리 안에 고정 지형이 있는가"다 — 플레이어가 물러날 자리가 없다는
/// 뜻이므로 그 자리에서 멈춘다. 저장소에 이 기능을 지원하는 기존 컴포넌트가 없어(PRD 상단 메모)
/// 여기서 새로 만들었다. 즉사 처리가 아니라 정지이고, 별도 "정지 상태" 플래그 없이 매 프레임 다시
/// 물어본다 — 대상이 벗어나면 다음 프레임에 자연히 재개된다.
///
/// 위험부 이동(하강/상승/돌출 등 피해가 나는 상태)에는 이 검사를 적용하지 않는다 — 그 상태의 접촉은
/// 압착이 아니라 의도된 피격이고, 맞은 즉시 리스폰으로 자리를 벗어나므로 압착이 성립하지 않는다.
/// 서브클래스가 호출 시점을 스스로 가른다(각 트랩 클래스의 IsDangerous 참고).
/// </summary>
public abstract class PeriodicTrapBase : MonoBehaviour
{
    [Header("공통 — 위상 지연 (사양 §4 startPhase)")]
    [Tooltip("이 장치의 내부 시계 시작 값(초). 여러 장치를 다른 값으로 두면 통로가 동시에 닫히지 " +
             "않는다(사양 §4 조립 순서 (5)). 주기보다 큰 값도 무방하다 — 서브클래스가 모듈로로 접는다.")]
    public float startPhase = 0f;

    [System.Serializable]
    public class PlayerHitEvent : UnityEvent<GameObject> { }

    [Header("공통 — 피격 알림 (SectionRespawn 완성 전 임시 배선)")]
    [Tooltip("위험부에 유효 접촉이 발생하면 발화(인자 = 맞은 플레이어 Root). 지금은 " +
             "RespawnController.RespawnPlayer(GameObject)에 배선한다 — 낙석(FallingRockSpawner)과 " +
             "같은 임시 방식. SectionRespawn(목적지 지정 오버로드) 완성 후 배선만 교체할 예정이며 " +
             "이 스크립트는 그때 수정할 필요가 없다.")]
    public PlayerHitEvent OnHazardHit;

    [Header("공통 — 끼임 안전 정지 (사양 §5 확정)")]
    public string playerTag = "Player";

    [Tooltip("압착 판정 여유 거리(U). 이동부에 겹쳐 있는 플레이어를 기준으로 이동 방향 쪽 이 거리 " +
             "안에 고정 지형이 있으면 '물러날 자리가 없다'로 보고 정지한다. 플레이어 콜라이더 " +
             "반경(기본 스케일 기준 약 0.5U)보다 살짝 크게 잡아라 — ScalingSystem으로 커진 도형을 " +
             "받는 구간이면 이 값도 늘려야 한다.")]
    public float squeezeCheckDistance = 0.7f;

    private readonly HashSet<Rigidbody> hitThisActivation = new HashSet<Rigidbody>();
    private static readonly Collider[] overlapBuffer = new Collider[8];

    /// <summary>위험부에 플레이어가 닿았을 때 서브클래스가 부른다. 장치·활성 구간(주기)·대상당 1회만
    /// true를 반환한다(사양 §4 "접촉 집계 단위"). <see cref="ResetHitWindow"/>를 부르기 전까지는 같은
    /// 활성 구간으로 취급한다.</summary>
    protected bool TryRegisterHit(Rigidbody playerBody)
    {
        if (playerBody == null) return false;
        if (!hitThisActivation.Add(playerBody)) return false;
        OnHazardHit?.Invoke(playerBody.gameObject);
        return true;
    }

    /// <summary>위험 상태가 끝나고 다음 활성 구간으로 넘어갈 때 서브클래스가 부른다 — 같은 플레이어가
    /// 다음 활성 구간에 다시 맞을 수 있게 연다.</summary>
    protected void ResetHitWindow() => hitThisActivation.Clear();

    /// <summary>이번 스텝에 mover를 intendedDelta만큼 옮겨도 되는지. false면 압착 위험이니 호출부는
    /// 이동/위상 진행을 이번 프레임 건너뛰어라(다음 프레임 재판정 — 별도 "정지됨" 상태 불필요).</summary>
    protected bool CanAdvance(Collider mover, Vector3 intendedDelta)
    {
        if (mover == null || intendedDelta.sqrMagnitude < 1e-8f) return true;

        Vector3 dir = intendedDelta.normalized;
        Bounds b = mover.bounds;
        float radius = b.extents.magnitude;
        int count = Physics.OverlapSphereNonAlloc(b.center, radius, overlapBuffer, ~0,
                                                    QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = overlapBuffer[i];
            if (c == null || c == mover || !c.CompareTag(playerTag)) continue;
            if (!b.Intersects(c.bounds)) continue; // 구 판정은 넉넉해서 실제로 안 닿았으면 대상이 아니다

            // 플레이어 기준으로 이동 방향 쪽에 고정 지형이 있는지 — 있으면 물러날 자리가 없다.
            if (Physics.Raycast(c.bounds.center, dir, out RaycastHit hit, squeezeCheckDistance,
                                 ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == c || hit.collider == mover) continue;
                return false;
            }
        }
        return true;
    }
}
