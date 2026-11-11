using UnityEngine;

/// <summary>
/// 측면 발사 함정 — 발사구당 1개인 예고 시각 표시(사양 §3). 예고 상태에서만 켜지고 무피해다.
/// 색만 <c>MaterialPropertyBlock</c>으로 덮어써 공유 머티리얼을 오염시키지 않는다
/// (`PeriodicTrapSystem`의 예고 표시·`RespawnSystem` 체크포인트 깃발과 같은 방식).
/// </summary>
public class ProjectileWarning : MonoBehaviour
{
    [Tooltip("비우면 자기 Renderer를 자동으로 찾는다.")]
    public Renderer target;

    public Color warnColor = new Color(1f, 0.6f, 0.1f);
    public Color idleColor = Color.white;

    private MaterialPropertyBlock mpb;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Reset()
    {
        if (target == null) target = GetComponent<Renderer>();
    }

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (target == null) target = GetComponent<Renderer>();
    }

    public void SetWarning(bool warning)
    {
        if (target == null) return;
        mpb.SetColor(ColorId, warning ? warnColor : idleColor);
        target.SetPropertyBlock(mpb);
    }
}
