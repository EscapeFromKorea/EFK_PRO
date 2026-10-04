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

    [Header("공통 — 피격 알림 (SectionHitCounter 경유 구간 복귀)")]
    [Tooltip("위험부에 유효 접촉이 발생하면 발화(인자 = 맞은 플레이어 Root). 구간 카운터의 " +
             "SectionHitCounter.RegisterHitEvent에 배선한다(예: CH5_시작) — 임계 횟수와 복귀 목적지는 " +
             "카운터가 정하므로 이 스크립트는 목적지를 모른다.")]
    public PlayerHitEvent OnHazardHit;

    [Header("공통 — 끼임 안전 정지 (사양 §5 확정)")]
    public string playerTag = "Player";

    [Tooltip("압착 판정 여유 거리(U). 이동부에 겹쳐 있는 플레이어를 기준으로 이동 방향 쪽 이 거리 " +
             "안에 고정 지형이 있으면 '물러날 자리가 없다'로 보고 정지한다. 플레이어 콜라이더 " +
             "반경(기본 스케일 기준 약 0.5U)보다 살짝 크게 잡아라 — ScalingSystem으로 커진 도형을 " +
             "받는 구간이면 이 값도 늘려야 한다.")]
    public float squeezeCheckDistance = 0.7f;

    [Header("공통 — 시작/정지 (docs/PRD/ZoneEntry.md §3)")]
    [Tooltip("씬 시작과 동시에 작동을 시작한다(기본, 기존 배치의 동작 그대로). 끄면 외부(구역 진입 감지 등)가 " +
             "Activate()를 부를 때까지 초기 자세로 정지해 있다.")]
    public bool autoActivateOnStart = true;

    // Idle = 초기 자세로 정지 / Running = 주기 동작 중 / Returning = 정지 요청 후 초기 자세로 되돌아가는 중
    private enum RunState { Idle, Running, Returning }
    private RunState runState = RunState.Idle;
    private bool pendingActivate;

    /// <summary>주기 동작 중인가. 서브클래스의 IsDangerous가 이걸 함께 본다 — 정지·복귀 중에는 피해가 꺼진다.</summary>
    protected bool IsRunning => runState == RunState.Running;

    /// <summary>작동을 시작한다. 이미 작동 중이면 무시한다(중복 입력 무시). 초기 자세로 되돌아가는 중이면
    /// 복귀가 끝난 직후 시작한다 — 항상 초기 자세의 첫 상태(도끼=예고, 망치=상부 대기, 가시=수납)에서
    /// 시작한다는 규칙(PRD H-06 "다음 시작은 예고 단계부터")을 지키기 위해서다.</summary>
    public void Activate()
    {
        if (runState == RunState.Running) return;
        if (runState == RunState.Returning) { pendingActivate = true; return; }
        BeginRun();
    }

    /// <summary>작동을 멈추고 초기 자세로 되돌아간다. 피해는 즉시 꺼지고 복귀 내내 꺼져 있다. 복귀는
    /// 평소 이동 속도로 하며 끼임 안전 정지를 그대로 적용한다. 작동 중이 아니면 보류 중인 Activate()만
    /// 취소한다.</summary>
    public void Deactivate()
    {
        pendingActivate = false;
        if (runState != RunState.Running) return;

        runState = RunState.Returning;
        ResetHitWindow();
        OnRunStopped();
    }

    private void BeginRun()
    {
        runState = RunState.Running;
        ResetHitWindow();
        StartRun();
    }

    private void Start()
    {
        if (autoActivateOnStart) Activate();
    }

    private void FixedUpdate()
    {
        if (runState == RunState.Running)
        {
            StepRun();
        }
        else if (runState == RunState.Returning && StepReturn())
        {
            runState = RunState.Idle;
            if (pendingActivate)
            {
                pendingActivate = false;
                BeginRun();
            }
        }
    }

    /// <summary>작동 시작 시 상태 기계를 첫 상태로 돌린다(자세는 이미 초기 자세다).</summary>
    protected abstract void StartRun();

    /// <summary>작동 중 한 물리 스텝.</summary>
    protected abstract void StepRun();

    /// <summary>초기 자세로 한 스텝 되돌아간다. 도착하면 true. 피해는 꺼진 상태다.</summary>
    protected abstract bool StepReturn();

    /// <summary>정지 요청 시 호출 — 예고 표시처럼 작동 중에만 켜져 있던 것을 끈다.</summary>
    protected virtual void OnRunStopped() { }

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
