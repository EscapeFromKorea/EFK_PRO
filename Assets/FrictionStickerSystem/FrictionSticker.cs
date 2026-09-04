using UnityEngine;

/// <summary>
/// 표면에 부착된 스티커 하나. <see cref="PlayerStickerCarrier"/>가 런타임에 생성한다(에디터에서 직접
/// 붙이는 컴포넌트가 아니다). 하는 일은 두 가지뿐:
///  1. 대상 <see cref="StickerSurface"/> 콜라이더의 sharedMaterial을 인스턴스 PhysicMaterial로 교체
///     (원본 참조를 저장해 뒀다가 회수/파괴 시 원복 — 공유 에셋/다른 콜라이더 오염 없음).
///  2. 종류 색으로 칠한 납작한 데칼(얇은 판)을 표면에 표시.
///
/// [움직이는 물체 대응] 이 GameObject는 <see cref="StickerSurface"/>의 자식으로 붙는다. 시소/레일카/
/// 회전판이 움직이면 데칼도 같이 움직이고, 마찰 교체는 그 콜라이더 자체의 재질이라 위치와 무관하게
/// 유지된다.
///
/// [원복 보장 — MP-05] Retract()뿐 아니라 OnDestroy()에서도 원본 재질을 되돌린다. 표면 오브젝트가
/// 씬 전환·파괴로 사라지거나, 스티커만 파괴돼도 마찰이 바뀐 채 남지 않는다. 전역 상태(Physics.*)는
/// 건드리지 않는다(MP-01).
/// </summary>
[DisallowMultipleComponent]
public class FrictionSticker : MonoBehaviour
{
    public StickerKind Kind { get; private set; }
    public StickerSurface Surface { get; private set; }

    private Collider affectedCollider;
    private PhysicMaterial originalSharedMaterial;   // 부착 전 콜라이더가 쓰던 재질(원복 기준). null일 수 있음.
    private PhysicMaterial instanceMaterial;         // 이 스티커가 만든 전용 마찰 재질.
    private Material decalMaterial;                  // 데칼 전용 시각 재질(파괴 시 함께 정리).
    private bool restored;

    /// <summary>
    /// 표면에 스티커를 부착하고 인스턴스를 돌려준다. 표면에 이미 스티커가 있으면 먼저 회수한다
    /// (표면당 1개 규칙 — 중첩 규칙 확정 시 이 부분만 교체).
    /// </summary>
    public static FrictionSticker Attach(StickerSurface surface, StickerKind kind, FrictionStickerSettings settings, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (surface == null) return null;

        Collider col = surface.ResolvedCollider;
        if (col == null)
        {
            Debug.LogWarning($"[FrictionSticker] '{surface.name}'에 마찰을 바꿀 Collider가 없어 부착을 취소합니다.");
            return null;
        }

        // 표면당 1개: 기존 스티커를 떼어낸다(그 종류는 호출자가 인벤토리로 환불).
        if (surface.Current != null)
            surface.Current.Retract();

        GameObject go = new GameObject($"FrictionSticker_{kind}");
        go.transform.SetParent(surface.transform, worldPositionStays: true);
        go.transform.position = hitPoint + hitNormal * 0.001f;
        go.transform.rotation = Quaternion.LookRotation(hitNormal); // +Z 축 = 표면 법선

        FrictionSticker sticker = go.AddComponent<FrictionSticker>();
        sticker.Initialize(surface, col, kind, settings);
        return sticker;
    }

    private void Initialize(StickerSurface surface, Collider col, StickerKind kind, FrictionStickerSettings settings)
    {
        Surface = surface;
        affectedCollider = col;
        Kind = kind;

        originalSharedMaterial = col.sharedMaterial;

        // 이 콜라이더 전용 인스턴스 재질. PhysicMaterial에는 복사 생성자가 없어 새로 만들고,
        // 반발 관련 값만 원본에서 물려받은 뒤 마찰·조합을 이 종류로 덮어쓴다(PlayerShapeIdentity와 같은 방식).
        instanceMaterial = new PhysicMaterial($"{surface.name}_{kind}_Sticker");
        if (originalSharedMaterial != null)
        {
            instanceMaterial.bounciness = originalSharedMaterial.bounciness;
            instanceMaterial.bounceCombine = originalSharedMaterial.bounceCombine;
        }

        float f = settings.FrictionFor(kind);
        instanceMaterial.staticFriction = f;
        instanceMaterial.dynamicFriction = f;
        instanceMaterial.frictionCombine = settings.CombineFor(kind);

        col.sharedMaterial = instanceMaterial;
        surface.Current = this;

        BuildDecal(settings);
    }

    /// <summary>표면에서 스티커를 떼고 마찰을 원복한다. 부착돼 있던 종류를 돌려준다(인벤토리 환불용).</summary>
    public StickerKind Retract()
    {
        StickerKind kind = Kind;
        RestoreMaterial();
        if (Surface != null && Surface.Current == this)
            Surface.Current = null;
        Destroy(gameObject);
        return kind;
    }

    private void RestoreMaterial()
    {
        if (restored) return;
        restored = true;

        if (affectedCollider != null)
            affectedCollider.sharedMaterial = originalSharedMaterial;
        if (instanceMaterial != null)
            Destroy(instanceMaterial);
        if (decalMaterial != null)
            Destroy(decalMaterial);
    }

    private void OnDestroy()
    {
        // Retract 경로가 아니어도(표면 파괴/씬 전환) 반드시 원복.
        RestoreMaterial();
    }

    // 종류 색으로 칠한 얇은 판. 그레이박스용 최소 시각 — 콜라이더 없는 순수 데칼이다.
    private void BuildDecal(FrictionStickerSettings settings)
    {
        GameObject decal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        decal.name = "Decal";
        Destroy(decal.GetComponent<Collider>());
        decal.transform.SetParent(transform, false);
        decal.transform.localPosition = Vector3.zero;
        decal.transform.localRotation = Quaternion.identity;
        decal.transform.localScale = new Vector3(
            Mathf.Max(0.01f, settings.decalSize.x),
            Mathf.Max(0.01f, settings.decalSize.y),
            Mathf.Max(0.001f, settings.decalThickness));   // 얇은 축 = 부모 +Z = 표면 법선

        Renderer r = decal.GetComponent<Renderer>();
        Shader shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
        decalMaterial = new Material(shader);   // 이 데칼 전용 인스턴스(공유 에셋 아님)
        SetColor(decalMaterial, settings.ColorFor(Kind));
        r.material = decalMaterial;
    }

    private static void SetColor(Material mat, Color c)
    {
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
    }
}
