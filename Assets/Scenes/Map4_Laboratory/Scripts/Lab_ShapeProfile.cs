using UnityEngine;

/// <summary>
/// 도형 1종(구/정육면체/정사면체)의 조정값을 한 곳에 모은 설정 에셋(F1-1 "도형 설정 파일").
/// Lab_ShapeProfileApplier가 Variant 프리팹의 Awake에서 이 값을 팀 컴포넌트 필드에 적용한다 —
/// 팀 원본 스크립트(PlayerMover·PlayerShapeIdentity·PlayerGroundContact 등)는 무수정.
///
/// 인스펙터에서 이 에셋 하나만 열면 도형별 값이 한눈에 보인다(F1-1 마지막 문장).
/// </summary>
[CreateAssetMenu(fileName = "Lab_ShapeProfile", menuName = "Laboratory Map4/Shape Profile")]
public class Lab_ShapeProfile : ScriptableObject
{
    [Header("도형 식별")]
    public PlayerShapeStats.ShapeKind shapeKind = PlayerShapeStats.ShapeKind.Sphere;

    [Header("스탯 (팀 PlayerShapeStats 복제본 — 원본 수정 금지)")]
    [Tooltip("팀 원본 Assets/PlayerSystem/ShapeStats/*.asset을 복제한 우리 전용 사본. " +
             "PlayerShapeIdentity.Start()가 이 사본을 읽어 rb.mass/PlayerMover.moveSpeed/" +
             "PlayerJump.jumpHeight/솔리드 콜라이더 물리 머티리얼(마찰·반발)을 적용한다.")]
    public PlayerShapeStats statsAsset;

    [Header("Rigidbody — 팀 메뉴 PlayerObjectMenuItem.ConfigureContinuousRollPhysics 하드코딩값 이식")]
    [Tooltip("[실좌표 09-28] PlayerObjectMenuItem.cs:53-55 — 구=None(자유 회전), " +
             "정육면체/정사면체=FreezeRotation(모서리 피벗 등반 규격 보존용).")]
    public RigidbodyConstraints constraints = RigidbodyConstraints.None;
    [Tooltip("[실좌표 09-28] PlayerObjectMenuItem.cs:56 — 전 도형 공통 0.5.")]
    public float angularDrag = 0.5f;
    [Tooltip("[실좌표 09-28] PlayerObjectMenuItem.cs:139 — 전 도형 공통 30. 참고: 런타임에는 " +
             "PlayerMover.Awake()가 이 값이 30 미만이면 다시 30으로 못박는다(PlayerMover.cs:280-281) " +
             "— 이 값을 30 미만으로 낮춰도 팀 코드가 되돌린다.")]
    public float maxAngularVelocity = 30f;
    public RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate;
    [Tooltip("[F1 재작업 판정 결정1] F1-1 명세 원문 그대로 — 구도 ContinuousDynamic. 팀 메뉴 " +
             "PlayerObjectMenuItem.cs:146은 구를 Discrete로 남겨 두지만, 이 필드는 프로필 값을 " +
             "그대로 대입하는 양방향 설정이라(Lab_ShapeProfileApplier.ApplyRigidbody) Discrete로 " +
             "되돌리고 싶으면 여기서 바꾸면 된다.")]
    public CollisionDetectionMode collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    [Tooltip("0이면 건드리지 않는다(팀 기본값 유지). [실좌표 09-28] PlayerObjectMenuItem.cs:144 — " +
             "구가 아닌 도형만 12.")]
    public int solverIterations = 0;
    [Tooltip("0이면 건드리지 않는다. [실좌표 09-28] PlayerObjectMenuItem.cs:145 — 구가 아닌 도형만 4.")]
    public int solverVelocityIterations = 0;

    [Header("PlayerMover 조정값 (public 필드만 — 리플렉션 금지, F1-1 명세)")]
    [Tooltip("0 이하면 팀 기본값(PlayerMover.airControlMultiplier=0.6) 유지 — 도달 규격표(가로 거리 " +
             "D=지상속도×0.6×체공, PlayerMover.cs:68-70)가 이 값에 의존하므로 근거 없이 바꾸지 않는다.")]
    public float airControlMultiplierOverride = -1f;
    [Tooltip("0 이하면 팀 기본값(PlayerMover.rollRadius=0.5) 유지.")]
    public float rollRadiusOverride = -1f;

    [Header("PlayerGroundContact 조정값 (public 필드만)")]
    [Tooltip("0 이하면 팀 기본값(0.25) 유지.")]
    public float groundCheckRadiusOverride = -1f;
    [Tooltip("0 이하면 팀 기본값(0.6) 유지.")]
    public float groundCheckDistanceOverride = -1f;

    [Header("F1-3/F1-4 끼임 방지 임계값 (Lab_AntiStuck)")]
    [Tooltip("이 깊이(Unit)를 넘는 관통이 stuckFrameThreshold 프레임 이상 지속되면 밀어낸다.")]
    public float penetrationThreshold = 0.02f;
    [Tooltip("관통이 이 프레임 수 이상 지속되면 분리 벡터로 밀어낸다.")]
    public int stuckFrameThreshold = 5;
    [Tooltip("밀어낸 뒤에도 이 프레임 수 이상 계속 관통 상태면 마지막 안전 위치로 복귀한다.")]
    public int recoveryFrameThreshold = 30;
    [Tooltip("안전 위치 링버퍼 크기(개) — 최근 비관통 위치를 이만큼 보관한다.")]
    public int safePositionBufferSize = 20;
    [Tooltip("안전 위치를 기록하는 주기(초).")]
    public float safePositionSampleInterval = 0.5f;
    [Tooltip("주변 정적 콜라이더를 찾는 브로드페이즈 반경 여유(Unit).")]
    public float overlapCheckRadius = 1.0f;
    [Tooltip("[F1 재작업 판정 2차 반려 N2] 연속 관통 카운터(framesPenetrating)를 0으로 되돌리려면 " +
             "이만큼 '연속' 클린 프레임이 필요하다 — 간헐적으로 한두 프레임만 깨끗해지는 경우(관통 " +
             "대상이 매 프레임 바뀌는 좁은 틈 등) 카운터가 섣불리 지워지지 않게 한다.")]
    public int cleanFramesToResetStuck = 5;
    [Tooltip("[2차 반려 A 항목 — 코요테 시간 배제] 안전 위치로 기록하려면 접지 판정에 더해 수직 " +
             "속도의 절댓값이 이 값 미만이어야 한다 — PlayerGroundContact의 유예 시간(coyote time) " +
             "동안에는 이미 낙하 중인데도 IsGrounded()가 true를 유지하는 것을 속도로 걸러낸다 " +
             "(정확한 해법은 아닌 휴리스틱 — 팀이 유예 시간 자체를 공개 API로 노출하지 않는다).")]
    public float safeSampleMaxVerticalSpeed = 0.5f;
}
