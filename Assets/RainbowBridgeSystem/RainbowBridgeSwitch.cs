using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무지개 다리 스위치. 평소 투명(숨김)한 대상 오브젝트들을, 플레이어가 발판을 밟는 동안
/// 실체화시킨다. 나타남/사라짐은 알파를 시간에 걸쳐 보간하는 페이드로 처리한다.
///
/// 왜 이렇게 만들었는가:
/// - 협동은 Tab 단일조작 전제다. 한 도형을 발판 위에 세워두고 Tab으로 다른 도형이 건너간다.
///   판정은 CompareTag("Player")뿐이라, 추후 네트워크 동시조작(각 플레이어도 Player 태그)으로
///   확장돼도 규칙이 그대로 유지된다 — 네트워크 코드는 지금 넣지 않는다.
/// - 플레이어는 트리거 콜라이더(Player_Mesh)와 솔리드 콜라이더(Player_Collider)를 함께 가져
///   OnTriggerEnter/Exit가 한 플레이어당 여러 번 불릴 수 있다. 그래서 overlapCount 카운터로
///   실제 겹침 수를 세어, 콜라이더 하나가 먼저 빠져도 눌림이 풀리지 않게 한다
///   (DoorSystem/PadTrigger 패턴 이식, 원본 미수정).
///
/// 콜라이더 타이밍(중요): "보이는 동안"에는 항상 밟을 수 있어야 한다. 그래서 페이드-인 시작
/// 시점(표시 목표가 켜지는 순간)에 콜라이더를 즉시 켜고, 페이드-아웃이 완전히 끝나 알파가 0이
/// 됐을 때 콜라이더를 끈다. 반쯤 보이는데 못 밟거나, 안 보이는데 밟히는 상황이 없게 한다.
///
/// 알파는 MaterialPropertyBlock으로 렌더러별로 덮어써서 공유 머티리얼 에셋을 오염시키지 않는다.
/// 단, 알파 블렌딩이 화면에 반영되려면 대상 머티리얼이 Transparent surface여야 한다. Tools 세팅
/// 로더가 만드는 세그먼트 머티리얼은 파이프라인별로 투명 설정을 맞춰준다.
/// ponytail: 로더 밖에서 직접 붙인 대상이 불투명 머티리얼이면 반투명 보간이 안 보이고 팝된다 —
/// 그때는 해당 머티리얼을 Transparent로 바꾸면 된다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RainbowBridgeSwitch : MonoBehaviour
{
    [Header("활성화 대상 (다리 세그먼트)")]
    [Tooltip("발판이 눌릴 때 실체화할 오브젝트들. 각 오브젝트 자신/자식의 Collider와 Renderer를 " +
             "제어한다. 발판 하나로 여러 세그먼트를 동시에 다룰 수 있다.")]
    public GameObject[] targetObjects;

    [Header("모드")]
    [Tooltip("true = 협동: 발판이 눌려 있는 동안만 유지. false = 솔플: 밟으면 activeDurationSec 동안만 유지 후 소멸.")]
    public bool activatorRequiresHold = true;

    [Tooltip("[솔플 전용] 밟은 뒤 유지되는 시간(초). 재차 밟으면 갱신된다.")]
    public float activeDurationSec = 3f;

    [Header("페이드")]
    [Tooltip("투명↔불투명 전환에 걸리는 시간(초). 0이면 즉시 전환.")]
    public float fadeDuration = 0.45f;

    private int overlapCount = 0;
    private float soloTimer = 0f;

    // 페이드 상태: targetAlpha는 목표(0=숨김/1=표시), currentAlpha는 현재 보간값.
    private float targetAlpha = 0f;
    private float currentAlpha = 0f;
    private float lastAppliedAlpha = -1f;   // 중복 적용 방지(-1=미적용 센티넬)

    // 대상 캐시(Start에서 1회 수집)
    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Color> baseColors = new List<Color>();  // 렌더러별 기준 RGB(알파는 런타임 제어)
    private readonly List<Collider> colliders = new List<Collider>();
    private MaterialPropertyBlock mpb;
    private bool collidersOn = false;

    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in Standard
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP/HDRP Lit

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void Start()
    {
        mpb = new MaterialPropertyBlock();
        CacheTargets();

        // 시작은 완전 숨김으로 강제(에디터에서 켜진 채 저장됐을 수 있음).
        currentAlpha = 0f;
        targetAlpha = 0f;
        ApplyVisual();
        lastAppliedAlpha = 0f;
        SetCollidersOn(false);
    }

    private void CacheTargets()
    {
        renderers.Clear();
        baseColors.Clear();
        colliders.Clear();
        if (targetObjects == null) return;

        foreach (GameObject t in targetObjects)
        {
            if (t == null) continue;

            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                renderers.Add(r);
                Color c = r.sharedMaterial != null ? r.sharedMaterial.color : Color.white;
                baseColors.Add(c);
            }
            foreach (Collider col in t.GetComponentsInChildren<Collider>(true))
                colliders.Add(col);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        overlapCount++;
        if (!activatorRequiresHold)
            soloTimer = activeDurationSec;   // 솔플: 밟을 때마다 타이머 갱신

        Show();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        overlapCount = Mathf.Max(0, overlapCount - 1);

        // 협동 모드에서만 발판에서 완전히 벗어나면 페이드-아웃 시작. 솔플은 타이머가 끈다.
        if (activatorRequiresHold && overlapCount == 0)
            Hide();
    }

    private void Update()
    {
        // 솔플 유지 타이머
        if (!activatorRequiresHold && targetAlpha > 0f)
        {
            soloTimer -= Time.deltaTime;
            if (soloTimer <= 0f) Hide();
        }

        // 알파 보간
        if (fadeDuration <= 0f)
            currentAlpha = targetAlpha;
        else
            currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, Time.deltaTime / fadeDuration);

        if (!Mathf.Approximately(currentAlpha, lastAppliedAlpha))
        {
            ApplyVisual();
            lastAppliedAlpha = currentAlpha;
        }

        // 콜라이더: 표시 목표가 켜진 순간(페이드-인 시작) 즉시 ON, 완전히 사라진 뒤(알파 0) OFF.
        bool shouldCollide = targetAlpha > 0f || currentAlpha > 0.001f;
        SetCollidersOn(shouldCollide);
    }

    // 유지 중 다시 밟히면 targetAlpha를 1로 되돌려, 페이드-아웃 중이었어도 곧바로 페이드-인으로 전환된다.
    private void Show()
    {
        targetAlpha = 1f;
        SetCollidersOn(true);   // 나타나기 시작하면 즉시 밟히도록
    }

    private void Hide()
    {
        targetAlpha = 0f;
    }

    private void ApplyVisual()
    {
        bool visible = currentAlpha > 0.001f;
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;

            r.enabled = visible;
            if (!visible) continue;

            Color c = baseColors[i];
            c.a = currentAlpha;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(ColorId, c);
            mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }

    private void SetCollidersOn(bool on)
    {
        if (collidersOn == on) return;
        collidersOn = on;
        for (int i = 0; i < colliders.Count; i++)
            if (colliders[i] != null) colliders[i].enabled = on;
    }

    private void OnDrawGizmos()
    {
        if (targetObjects == null) return;

        Gizmos.color = Color.magenta;
        Vector3 from = transform.position + Vector3.up * 0.1f;
        foreach (GameObject target in targetObjects)
        {
            if (target == null) continue;
            Gizmos.DrawLine(from, target.transform.position);
            Gizmos.DrawWireCube(target.transform.position, Vector3.one * 0.3f);
        }
    }
}
