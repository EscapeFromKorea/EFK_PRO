using UnityEngine;

/// <summary>
/// 도형별 플레이어 스탯을 담는 데이터 에셋(요구사항 #2). 이속/점프높이/질량/마찰/반발을 코드가
/// 아닌 에셋으로 분리해, 밸런싱·튜닝에 재컴파일이 필요 없게 한다. PlayerShapeIdentity가 이 값을
/// 읽어 런타임에 Rigidbody/PlayerMover/PlayerJump/콜라이더에 적용한다.
/// 새 도형은 이 에셋을 하나 더 만들어 지정하면 되고(스탯 관점에선 코드 수정 불필요), kind는 기믹이
/// 도형 종류를 조회할 때(#7) 쓰는 식별자다.
/// </summary>
[CreateAssetMenu(fileName = "ShapeStats", menuName = "PlayerSystem/Shape Stats")]
public class PlayerShapeStats : ScriptableObject
{
    public enum ShapeKind { Sphere, Cube, Tetrahedron }

    [Tooltip("도형 종류 식별자. 기믹이 PlayerShapeIdentity.Kind로 조회해 차등 처리한다.")]
    public ShapeKind kind = ShapeKind.Sphere;

    [Header("이동 / 점프 (#2)")]
    [Tooltip("지상 이동 속도(U/s). 공중은 PlayerMover.airControlMultiplier(0.6)가 곱해진다.")]
    public float moveSpeed = 7.0f;

    [Tooltip("목표 점프 높이 H(Unit). PlayerJump가 velocity.y=√(2gH)로 역산 대입한다.")]
    public float jumpHeight = 1.6f;

    [Tooltip("Rigidbody 질량. velocity 대입 이동/점프에는 영향이 없고(도달은 질량 무관) 충돌·밀림에만 쓰인다.")]
    public float mass = 1.5f;

    [Header("물리 머티리얼 (#6)")]
    [Tooltip("마찰(정지=동). 구 0.1(활강) / 네모 0.5(조향 안정) / 세모 0.8(꼭짓점 고정).")]
    public float friction = 0.1f;

    [Tooltip("반발(bounciness). 요구사항상 전 도형 0.")]
    public float bounciness = 0f;
}
