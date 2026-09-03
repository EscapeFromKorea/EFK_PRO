using UnityEngine;

/// <summary>스티커 종류. 미끄럼(마찰 감소) / 벨크로(마찰 증가). 명세서 "종류" 표 그대로.</summary>
public enum StickerKind
{
    Slip,   // 미끄럼 스티커 — 마찰 감소 (경사 가속, 블록 운반, 레일카 가속)
    Velcro  // 벨크로 스티커 — 마찰 증가 (경사 정지, 블록 고정, 레일카 제동)
}

/// <summary>
/// "여기에 스티커를 붙일 수 있다"를 표시하는 순수 마커. 레벨 디자이너가 바닥/블록/시소/레일카/
/// 회전판 등 스티커 부착 대상 오브젝트(콜라이더 보유)에 붙인다.
///
/// 왜 마커만 두고 로직은 안 넣나: 부착 판정·마찰 교체·회수는 전부 <see cref="FrictionSticker"/>와
/// <see cref="PlayerStickerCarrier"/>가 갖는다. 이 컴포넌트는 "이 콜라이더가 부착 가능하다 +
/// 어떤 종류를 허용한다 + 지금 붙어 있는 스티커가 무엇인가"만 안다. ThreadAnchor가 앵커를
/// 순수 위치 마커로 둔 것과 같은 구조다.
///
/// [표면당 스티커 1개] 명세서의 중첩 규칙이 아직 미확정이라, 기본 구현은 "표면당 1개, 새로
/// 붙이면 기존 것 교체". <see cref="Current"/> 슬롯 하나로 강제한다. 규칙이 정해지면
/// FrictionSticker.Attach 쪽 교체 로직만 바꾸면 된다.
///
/// [움직이는 물체 대응] 스티커는 이 오브젝트의 자식으로 붙고 마찰 교체는 이 콜라이더의
/// sharedMaterial에 적용되므로, 시소/레일카/회전판이 움직여도 스티커가 그대로 따라간다.
/// </summary>
[DisallowMultipleComponent]
public class StickerSurface : MonoBehaviour
{
    [Header("마찰을 바꿀 대상 콜라이더")]
    [Tooltip("비우면 이 GameObject의 Collider를 자동으로 쓴다. 콜라이더가 자식에 있거나 여러 개면 " +
             "여기서 명시 지정한다. 이 콜라이더의 sharedMaterial이 부착 중 인스턴스 재질로 교체됐다가 " +
             "회수/파괴 시 원복된다(공유 에셋은 건드리지 않는다).")]
    public Collider targetCollider;

    [Header("허용 종류")]
    [Tooltip("미끄럼 스티커를 붙일 수 있는가.")]
    public bool allowSlip = true;
    [Tooltip("벨크로 스티커를 붙일 수 있는가.")]
    public bool allowVelcro = true;

    [Header("씬 뷰 표시")]
    [Tooltip("부착 가능한 표면임을 씬 뷰에서 표시하는 색(기즈모 전용, 런타임 영향 없음).")]
    public Color attachableGizmoColor = new Color(0.35f, 0.85f, 1f, 1f);

    /// <summary>현재 이 표면에 붙어 있는 스티커. 없으면 null. FrictionSticker가 부착/회수 시 세팅한다.</summary>
    public FrictionSticker Current { get; internal set; }

    /// <summary>마찰 교체가 적용되는 콜라이더. targetCollider 우선, 없으면 자기 Collider.</summary>
    public Collider ResolvedCollider =>
        targetCollider != null ? targetCollider : GetComponent<Collider>();

    private void Reset()
    {
        targetCollider = GetComponent<Collider>();
    }

    /// <summary>이 종류의 스티커를 이 표면이 받을 수 있는가(허용 플래그만 본다. 슬롯 점유 여부는 별개).</summary>
    public bool Accepts(StickerKind kind)
    {
        return kind == StickerKind.Slip ? allowSlip : allowVelcro;
    }

    private void OnDrawGizmos()
    {
        Collider col = ResolvedCollider;
        if (col == null) return;

        Gizmos.color = attachableGizmoColor;
        Bounds b = col.bounds;
        Gizmos.DrawWireCube(b.center, b.size * 1.02f);
    }
}
