using UnityEngine;

/// <summary>
/// 스티커 종류별 마찰값·조합 방식·시각(색/크기)을 한데 묶은 튜닝 뭉치. <see cref="FrictionStickerController"/>가
/// 인스펙터에 노출하고, 그 값을 <see cref="PlayerStickerCarrier"/>와 <see cref="FrictionSticker"/>에
/// 전달한다(하드코딩 금지 규약).
///
/// [왜 절대값 + PhysicMaterialCombine인가]
/// - "마찰을 절반으로" 같은 배수는 원래 마찰이 0인 표면에선 아무 일도 안 일어난다. 그래서 종류별로
///   "이 표면의 마찰을 이 절대값으로 만든다"로 간다(PlayerShapeIdentity가 도형 마찰을 절대값으로
///   적용하는 것과 같은 방식).
/// - 접촉 시 실제 마찰은 두 콜라이더의 PhysicMaterial 중 우선순위가 높은 frictionCombine으로 정해진다
///   (Maximum &gt; Multiply &gt; Minimum &gt; Average). 플레이어 재질은 Average이므로:
///   · 미끄럼 = Multiply → 플레이어마찰 × slipFriction 이 되어 확실히 미끄럽다(플레이어 Average를 이긴다).
///   · 벨크로 = Maximum → max(플레이어마찰, velcroFriction) 이 되어 확실히 잡힌다.
///   덕분에 "같은 경사면에서 일반/미끄럼/벨크로 결과가 확실히 다르다"(완료 조건)가 물리적으로 보장된다.
/// </summary>
[System.Serializable]
public class FrictionStickerSettings
{
    [Header("미끄럼 스티커")]
    [Tooltip("미끄럼 스티커가 표면에 설정하는 마찰 절대값(정지=동일).")]
    [Range(0f, 2f)] public float slipFriction = 0.05f;
    [Tooltip("미끄럼 스티커 표면의 마찰 조합 방식. 기본 Multiply — 상대 콜라이더 마찰과 곱해져 " +
             "플레이어 Average보다 우선하므로 확실히 미끄럽다.")]
    public PhysicMaterialCombine slipCombine = PhysicMaterialCombine.Multiply;
    [Tooltip("미끄럼 스티커 데칼 색(부착 시 시각 구분용).")]
    public Color slipColor = new Color(0.25f, 0.7f, 1f, 0.9f);

    [Header("벨크로 스티커")]
    [Tooltip("벨크로 스티커가 표면에 설정하는 마찰 절대값(정지=동일).")]
    [Range(0f, 4f)] public float velcroFriction = 1.2f;
    [Tooltip("벨크로 스티커 표면의 마찰 조합 방식. 기본 Maximum — 상대 콜라이더 마찰보다 커서 " +
             "확실히 잡힌다.")]
    public PhysicMaterialCombine velcroCombine = PhysicMaterialCombine.Maximum;
    [Tooltip("벨크로 스티커 데칼 색(부착 시 시각 구분용).")]
    public Color velcroColor = new Color(1f, 0.55f, 0.2f, 0.9f);

    [Header("스티커 데칼 형상")]
    [Tooltip("부착 시 표면에 생기는 데칼(납작한 판)의 가로/세로 크기(Unit).")]
    public Vector2 decalSize = new Vector2(0.7f, 0.7f);
    [Tooltip("데칼 두께(Unit). 표면에서 살짝 떠 보이게 하는 정도.")]
    public float decalThickness = 0.04f;

    /// <summary>종류별 마찰 절대값.</summary>
    public float FrictionFor(StickerKind kind) =>
        kind == StickerKind.Slip ? slipFriction : velcroFriction;

    /// <summary>종류별 마찰 조합 방식.</summary>
    public PhysicMaterialCombine CombineFor(StickerKind kind) =>
        kind == StickerKind.Slip ? slipCombine : velcroCombine;

    /// <summary>종류별 데칼/프리뷰 색.</summary>
    public Color ColorFor(StickerKind kind) =>
        kind == StickerKind.Slip ? slipColor : velcroColor;
}
