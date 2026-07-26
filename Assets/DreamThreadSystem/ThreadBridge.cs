using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 꿈의 실타래 Phase 3 — 고리 사이를 실로 이어 만드는 "줄다리". 씬에 여러 개 둘 수 있다.
///
/// [무엇을 하나]
/// 두 고리(ThreadAnchor) 사이에 실을 걸고, 그 실 위를 실제로 걸어 건널 수 있게 한다. 올라탄 도형의
/// "무게"와 "위치"를 입력으로 처짐 곡선을 매 FixedUpdate에 수식으로 계산하고, 그 곡선 위로 세그먼트
/// (키네마틱 콜라이더)들을 옮긴다. 실 자체는 LineRenderer로 같은 곡선을 그린다. 무거운 네모가
/// 올라타면 줄이 크게 처져 "낮은 경로"가 생긴다 — 그게 이 기믹의 통과 방식이다.
///
/// [왜 계산 곡선 + 키네마틱 세그먼트인가 (Rigidbody 조인트 체인 아님)]
/// 진짜 로프를 조인트 체인으로 만들면 Phase 1에서 겪은 로프 물리 리스크(솔버 지터, 튜닝 폭발)를
/// 세그먼트 수만큼 곱해 재현하고, 결과가 비결정적이라 레벨 설계가 성립하지 않는다. 반면 처짐은
/// "정적 하중 → 처짐 깊이"라는 잘 알려진 형상이 있어 수식 한 줄로 충분하다. 대신 "줄이 출렁이는"
/// 느낌은 물리로 안 나오므로 baseSag/sagResponseSpeed 같은 연출 노브로만 보완한다.
///
/// [처짐 수식]
///   sag(t) = baseSag·4t(1-t)                     ← 실 자체 무게(가운데가 가장 깊은 포물선)
///          + Σ_riders  A_i · tent(t; u_i)        ← 올라탄 도형별 점하중
///   A_i    = sagPerWeight · W_i · 4u_i(1-u_i)    ← 하중이 만드는 최대 처짐
///   tent(t;u) = t≤u ? t/u : (1-t)/(1-u)          ← 하중 지점이 뾰족한 삼각형
/// 팽팽한 줄에 점하중을 걸면 실제로 하중 지점만 꺾인 **삼각형**이 되고(양쪽은 직선), 처짐 깊이는
/// u(1-u)에 비례한다 — 끝에 서면 안 처지고 가운데에 서야 크게 처진다. 4는 "가운데에 섰을 때 계수
/// 값이 그대로 최대 처짐(Unit)"이 되게 하는 정규화라 노브를 눈금으로 읽을 수 있다.
///
/// [왜 세그먼트를 키네마틱 Rigidbody + MovePosition으로 옮기나]
/// transform을 직접 옮기면 PhysX가 스윕하지 않아 위에 선 플레이어와 파고들었다가 튕겨 나간다.
/// 키네마틱 Rigidbody의 MovePosition은 움직이는 발판의 표준 처리라 접촉이 정상적으로 밀린다.
/// 세그먼트는 렌더러 없는 순수 콜라이더고, 발판 **윗면**이 곡선에 닿도록 두께의 절반만큼 내려 놓는다
/// (중심을 곡선에 두면 실 위에 떠서 걷는 것처럼 보인다 — 플레이테스트).
///
/// [경간 — 하나가 아니라 여럿을 굴린다]
/// 지정 고리 한 쌍만 잇던 것을, **여러 경간(Span)을 동시에 관리**하도록 바꿨다(2026-07-26).
///   1. 기본 경간 — 둘 다 지정(고정) / 한쪽만 지정(그 고리 + 가장 가까운 핀) / 둘 다 비움(핀 2개).
///   2. 가지 경간 — 기본 경간에 안 쓰인 핀이 기본 경간의 양 끝 중 어느 하나와 maxSpan 안이면,
///      그 끝에서 핀으로 가지를 하나 더 친다. 이미 이어진 다리 옆에 세모가 핀을 박으면 길이 갈라진다.
///      **한 끝점에서 뻗는 가지는 하나뿐이다** — 그 끝점에서 가장 가까운 핀만 잇는다. 한 자리에 핀을
///      두 개 박아 실이 두 갈래로 갈라지면 지저분하고 어느 쪽을 밟는지 헷갈린다(플레이테스트).
/// 경간마다 세그먼트·처짐 배열·LineRenderer가 한 벌씩 필요해 상태를 Span 클래스로 묶었다. 지정/비움
/// 조합이 곧 모드라 모드 선택 필드는 없다. 가지 조건에 새 노브를 두지 않고 maxSpan을 재사용한다 —
/// "실이 닿는 거리"라는 뜻이 그대로 맞고, 노브가 늘면 튜닝할 게 하나 더 생긴다.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class ThreadBridge : MonoBehaviour
{
    [Header("양 끝 고리")]
    [Tooltip("줄다리의 양 끝 고리. 비워 둔 쪽만 세모가 박은 핀으로 채워진다.\n" +
             "· 둘 다 지정 = 고정 줄다리(레벨 배치용)\n" +
             "· 둘 다 비움 = 세모가 박은 핀 2개를 잇는다\n" +
             "· 한쪽만 지정 = 그 고리와 '가장 가까운 핀'을 잇는다\n" +
             "어느 경우든, 남은 핀이 기본 경간 끝에서 maxSpan 안에 있으면 가지 경간이 추가로 생긴다 " +
             "(끝점 하나당 가장 가까운 핀 하나까지).")]
    public ThreadAnchor anchorA;
    public ThreadAnchor anchorB;

    [Tooltip("한 경간의 최대 길이(Unit). 두 고리가 이보다 멀면 실이 모자라 이어지지 않는다. " +
             "가지 경간이 생길지(핀이 '근처'인지)도 같은 거리로 판정한다.")]
    public float maxSpan = 14f;

    [Tooltip("동시에 유지할 경간의 최대 개수. 1이면 가지 없이 기본 경간만 쓴다(예전 동작). " +
             "핀이 최대 2개라 기본 1 + 가지 2 = 3이면 충분하다.")]
    public int maxSpans = 3;

    [Header("발판 세그먼트 (보이지 않는 콜라이더)")]
    [Tooltip("경간 하나를 몇 마디로 쪼갤지. 많을수록 곡선이 매끄럽고 물리 비용이 는다. " +
             "경간이 만들어질 때 한 번 생성하며 런타임 변경은 반영되지 않는다.")]
    public int segmentCount = 12;
    [Tooltip("발판 폭(Unit, 줄에 수직인 가로). 좁을수록 떨어지기 쉬워 난이도가 오른다. " +
             "보이는 실보다 넓으면 '실 옆 허공을 밟는' 느낌이 나므로 과하게 키우지 않는다.")]
    public float segmentWidth = 0.5f;
    [Tooltip("발판 두께(Unit). 발판은 윗면이 곡선에 닿도록 이 값의 절반만큼 아래에 놓이므로, " +
             "밟는 높이는 두께와 무관하게 항상 보이는 실과 같다.")]
    public float segmentThickness = 0.2f;
    [Tooltip("발판 길이를 이 배율만큼 늘려 서로 겹치게 한다. 처져서 호가 길어져도 마디 사이에 " +
             "빠지는 틈이 생기지 않게 하는 여유분(1.0이면 딱 맞춰 틈이 생길 수 있다).")]
    public float segmentOverlap = 1.2f;

    [Header("처짐 곡선")]
    [Tooltip("아무도 올라타지 않았을 때 줄 가운데가 처지는 깊이(Unit). 실 자체 무게 연출.")]
    public float baseSag = 0.4f;
    [Tooltip("무게 1당 늘어나는 처짐 깊이(Unit). 가운데에 선 기준이며 끝으로 갈수록 줄어든다. " +
             "네모(무게 3.0)가 가운데 서면 대략 이 값의 3배만큼 내려간다.")]
    public float sagPerWeight = 0.55f;
    [Tooltip("처짐 깊이의 상한(Unit). 줄다리가 아래 지형을 뚫고 내려가지 않게 막는 안전값.")]
    public float maxSag = 4f;
    [Tooltip("처짐이 목표 깊이를 따라가는 속도(Unit/s). 작으면 묵직하게 가라앉고, 크면 즉각 반응하지만 " +
             "올라탄 도형이 발판에 얻어맞은 듯 튄다.")]
    public float sagResponseSpeed = 5f;

    [Header("올라탄 도형 감지")]
    [Tooltip("줄 곡선에서 이 거리(Unit) 안에 있는 플레이어를 '올라탄 것'으로 보고 무게를 반영한다. " +
             "발판 위에 선 도형의 중심이 곡선보다 조금 위에 있으므로 도형 크기보다 넉넉히 준다.")]
    public float riderCaptureRadius = 1.6f;

    [Header("실 시각")]
    [Tooltip("LineRenderer 실 두께.")]
    public float lineWidth = 0.06f;

    // 올라탄 도형 하나가 만드는 처짐: u = 줄 위 위치(0~1), amp = 그 지점의 최대 처짐 깊이.
    private struct Rider { public float u; public float amp; }

    private struct Pair { public ThreadAnchor a, b; }

    /// <summary>경간 하나가 필요로 하는 전부 — 발판 세그먼트, 처짐 배열, 실 렌더러, 곡선 좌표.
    /// 여러 경간을 굴리려면 이게 경간마다 한 벌씩 있어야 해서 클래스로 묶었다.</summary>
    private class Span
    {
        public ThreadAnchor a, b;
        public Vector3 endA, endB;

        public Transform root;
        public Rigidbody[] bodies;
        public BoxCollider[] boxes;
        public LineRenderer line;

        public float[] sag;      // 샘플 지점별 현재 처짐 깊이. 길이 segmentCount+1(양 끝 포함).
        public Vector3[] points; // 이번 스텝의 곡선 샘플 좌표. LineRenderer가 그대로 쓴다.

        public bool active;
        public bool teleportNext; // 새로 이어진 첫 스텝은 스윕 대신 순간이동으로 자리잡는다.
        public float builtSpan = -1f;
    }

    private readonly List<Span> spans = new List<Span>();
    private readonly List<Rider> riders = new List<Rider>();
    private readonly List<ThreadAnchor> pinBuf = new List<ThreadAnchor>();
    private readonly List<Pair> pairBuf = new List<Pair>();

    private LineRenderer mainLine;   // 경간 0이 쓰는, 이 컴포넌트에 원래 붙어 있는 렌더러.
    private ThreadPinPlacer pinPlacer;
    private string lastFailReason;   // 같은 실패 사유를 매 프레임 반복해 찍지 않기 위한 기억.

    void Awake()
    {
        mainLine = GetComponent<LineRenderer>();
        mainLine.useWorldSpace = true;
        mainLine.widthMultiplier = lineWidth;
        mainLine.enabled = false;
        if (mainLine.sharedMaterial == null)
        {
            Shader s = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (s != null) mainLine.material = new Material(s) { color = new Color(0.7f, 0.9f, 1f, 1f) };
        }
        segmentCount = Mathf.Max(2, segmentCount);
        maxSpans = Mathf.Max(1, maxSpans);
    }

    void Reset()
    {
        LineRenderer lr = GetComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.widthMultiplier = 0.06f;
        lr.numCapVertices = 2;
        lr.enabled = false;
    }

    void OnDisable()
    {
        // 컴포넌트가 꺼지면 발판도 함께 걷는다 — 안 그러면 보이지 않는 콜라이더가 허공에 남는다.
        foreach (Span s in spans) SetSpanActive(s, false);
    }

    void FixedUpdate()
    {
        ResolvePairs(pairBuf);

        for (int i = 0; i < pairBuf.Count; i++)
        {
            Span s = EnsureSpan(i);
            // 다른 고리 짝으로 갈아탔으면 처짐을 물려받지 않고 새로 자리잡는다.
            if (s.a != pairBuf[i].a || s.b != pairBuf[i].b)
            {
                s.a = pairBuf[i].a;
                s.b = pairBuf[i].b;
                ResetSpanState(s);
            }
            SetSpanActive(s, true);
            UpdateSpan(s);
        }

        // 이번 스텝에 쓰이지 않은 경간은 접는다(핀 회수·경간 초과 등).
        for (int i = pairBuf.Count; i < spans.Count; i++)
        {
            spans[i].a = null;
            spans[i].b = null;
            SetSpanActive(spans[i], false);
        }
    }

    void LateUpdate()
    {
        foreach (Span s in spans)
            if (s.active) s.line.SetPositions(s.points);
    }

    // ── 어느 고리들을 이을지 ───────────────────────────────────────────────────────────

    /// <summary>이번 스텝에 존재해야 할 경간들의 양 끝을 정한다. 기본 경간 하나를 정하고, 남은 핀이
    /// 그 경간의 끝에서 maxSpan 안이면 가지를 친다.</summary>
    private void ResolvePairs(List<Pair> into)
    {
        into.Clear();

        if (pinPlacer == null) pinPlacer = Object.FindObjectOfType<ThreadPinPlacer>();
        pinBuf.Clear();
        if (pinPlacer != null) pinPlacer.CollectPins(pinBuf);

        ThreadAnchor a = anchorA;
        ThreadAnchor b = anchorB;

        if (a == null && b == null)
        {
            if (pinPlacer == null)
            {
                Fail("씬에 ThreadPinPlacer가 없습니다 (Tools > DreamThread > Create Pin Placer).");
                return;
            }
            if (pinBuf.Count < 2)
            {
                Fail("세모가 박은 핀이 2개가 아닙니다 — G로 두 군데를 박아야 이어집니다.");
                return;
            }
            a = pinBuf[0];
            b = pinBuf[1];
        }
        else if (a == null || b == null)
        {
            // 혼합: 지정된 고리에서 가장 가까운 핀 하나로 나머지 끝을 채운다. 거리 기준이라 결정론적이고,
            // 더 가까운 자리에 새로 박으면 다리가 그쪽으로 옮겨가 조준을 다시 할 수 있다.
            ThreadAnchor fixedEnd = a != null ? a : b;
            ThreadAnchor pin = NearestPin(fixedEnd.transform.position);
            if (pin == null)
            {
                Fail("세모가 박은 핀이 없습니다 — G로 한 군데를 박으면 지정 고리와 이어집니다.");
                return;
            }
            if (a == null) a = pin; else b = pin;
        }

        if (!Spannable(a, b, out string why))
        {
            Fail(why);
            return;
        }

        lastFailReason = null;
        into.Add(new Pair { a = a, b = b });

        // 가지: 기본 경간에 안 쓰인 핀을 양 끝 중 가까운 쪽에서 뻗되, **한 끝점당 하나만** 뻗는다.
        // 한 끝점에 핀 두 개를 박았을 때 둘 다 이어지면 같은 자리에서 실이 두 갈래로 갈라져 나가
        // 지저분하고, 어느 쪽을 밟는 건지도 헷갈린다(플레이테스트) — 그 끝점에서 가장 가까운 핀
        // 하나만 남긴다. 양 끝에 하나씩 박으면 여전히 양쪽 다 뻗는다.
        // 실패해도 조용히 넘어간다 — 가지는 "되면 좋은 것"이지 없다고 고장이 아니다.
        ThreadAnchor bestForA = null, bestForB = null;
        float bestDistA = float.PositiveInfinity, bestDistB = float.PositiveInfinity;

        foreach (ThreadAnchor pin in pinBuf)
        {
            if (pin == a || pin == b) continue;

            ThreadAnchor root = Closer(a, b, pin.transform.position);
            if (!Spannable(root, pin, out _)) continue;

            float d = Vector3.Distance(root.transform.position, pin.transform.position);
            if (root == a) { if (d < bestDistA) { bestDistA = d; bestForA = pin; } }
            else { if (d < bestDistB) { bestDistB = d; bestForB = pin; } }
        }

        if (bestForA != null && into.Count < maxSpans) into.Add(new Pair { a = a, b = bestForA });
        if (bestForB != null && into.Count < maxSpans) into.Add(new Pair { a = b, b = bestForB });
    }

    /// <summary>from에 가장 가까운 핀(없으면 null).</summary>
    private ThreadAnchor NearestPin(Vector3 from)
    {
        ThreadAnchor best = null;
        float bestSqr = float.PositiveInfinity;
        foreach (ThreadAnchor p in pinBuf)
        {
            float sqr = (p.transform.position - from).sqrMagnitude;
            if (sqr >= bestSqr) continue;
            bestSqr = sqr;
            best = p;
        }
        return best;
    }

    private static ThreadAnchor Closer(ThreadAnchor a, ThreadAnchor b, Vector3 to) =>
        (a.transform.position - to).sqrMagnitude <= (b.transform.position - to).sqrMagnitude ? a : b;

    private bool Spannable(ThreadAnchor a, ThreadAnchor b, out string why)
    {
        why = null;
        if (a == null || b == null || a == b) return false;

        float d = Vector3.Distance(a.transform.position, b.transform.position);
        if (d <= 0.5f) { why = $"두 고리가 너무 가깝습니다 (거리 {d:0.##}, 최소 0.5)."; return false; }
        if (d > maxSpan) { why = $"두 고리가 너무 멉니다 (거리 {d:0.##} > maxSpan {maxSpan})."; return false; }
        return true;
    }

    // 줄다리가 안 생길 때 "아무 일도 안 일어남"이 가장 진단하기 어렵다. 이유가 바뀔 때만 한 번씩
    // 찍어 매 프레임 스팸 없이 원인을 남긴다.
    private void Fail(string reason)
    {
        if (lastFailReason == reason) return;
        Debug.Log($"[ThreadBridge] '{name}' 줄다리를 잇지 못했습니다 — {reason}");
        lastFailReason = reason;
    }

    // ── 경간 하나의 갱신 ──────────────────────────────────────────────────────────────

    private void UpdateSpan(Span s)
    {
        s.endA = s.a.transform.position;
        s.endB = s.b.transform.position;

        float chord = Vector3.Distance(s.endA, s.endB);
        if (Mathf.Abs(chord - s.builtSpan) > 0.05f)
        {
            float len = chord / segmentCount * segmentOverlap;
            for (int i = 0; i < s.boxes.Length; i++)
                s.boxes[i].size = new Vector3(segmentWidth, segmentThickness, len);
            s.builtSpan = chord;
        }

        CollectRiders(s); // 직전 스텝의 곡선 기준(1스텝 지연) — 처짐이 자기 자신을 입력으로 삼는 순환을 끊는다.
        UpdateSag(s);
        PlaceSegments(s);
    }

    private void CollectRiders(Span s)
    {
        riders.Clear();
        Vector3 ab = s.endB - s.endA;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-4f) return;

        // ponytail: 매 FixedUpdate FindObjectsOfType — 플레이어가 3명뿐이라 무시할 비용이고
        // 이 폴더의 기존 컴포넌트들도 같은 방식이다. 플레이어가 많아지면 트리거 기반 집계로 바꾼다.
        foreach (PlayerMover m in Object.FindObjectsOfType<PlayerMover>())
        {
            Rigidbody rb = m.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic) continue; // 벽 부착 중인 세모 등은 줄에 하중을 주지 않는다

            float u = Mathf.Clamp01(Vector3.Dot(rb.position - s.endA, ab) / len2);
            if ((rb.position - PointAt(s, u)).sqrMagnitude > riderCaptureRadius * riderCaptureRadius) continue;

            // 무게 기준은 PlayerSystem의 PlayerWeight 하나뿐이다(질량 × 실효 중력 배율) —
            // 무중력 버블 안의 네모는 줄을 덜 처지게 만든다.
            float w = PlayerWeight.Of(rb);
            riders.Add(new Rider { u = u, amp = sagPerWeight * w * 4f * u * (1f - u) });
        }
    }

    private void UpdateSag(Span s)
    {
        float step = sagResponseSpeed * Time.fixedDeltaTime;
        for (int i = 0; i < s.sag.Length; i++)
        {
            float t = (float)i / segmentCount;
            float target = baseSag * 4f * t * (1f - t);
            for (int r = 0; r < riders.Count; r++)
                target += riders[r].amp * Tent(t, riders[r].u);
            target = Mathf.Min(target, maxSag);
            s.sag[i] = Mathf.MoveTowards(s.sag[i], target, step);
        }
    }

    // 하중 지점(u)에서 1, 양 끝에서 0인 삼각형 프로파일. 팽팽한 줄에 점하중이 걸렸을 때의 실제 형상이다.
    private static float Tent(float t, float u)
    {
        if (u <= 1e-4f) return 1f - t;
        if (u >= 1f - 1e-4f) return t;
        return t <= u ? t / u : (1f - t) / (1f - u);
    }

    /// <summary>현재 처짐을 반영한 줄 위 좌표. t는 A(0)→B(1) 매개변수.</summary>
    private Vector3 PointAt(Span s, float t)
    {
        t = Mathf.Clamp01(t);
        float f = t * segmentCount;
        int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, segmentCount - 1);
        float sagAt = Mathf.Lerp(s.sag[i], s.sag[i + 1], f - i);
        return Vector3.Lerp(s.endA, s.endB, t) + Vector3.down * sagAt;
    }

    private void PlaceSegments(Span s)
    {
        for (int i = 0; i <= segmentCount; i++)
            s.points[i] = PointAt(s, (float)i / segmentCount);

        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 p0 = s.points[i];
            Vector3 p1 = s.points[i + 1];
            // 발판을 두께의 절반만큼 내려 **윗면이 곡선에 닿게** 한다. 박스 중심을 곡선에 두면 밟는
            // 면이 실보다 thickness/2 위에 생겨 "실 위에 떠서 걷는" 것처럼 보인다(플레이테스트).
            Vector3 mid = (p0 + p1) * 0.5f + Vector3.down * (segmentThickness * 0.5f);
            Vector3 dir = p1 - p0;
            // forward를 줄 방향으로 두면 박스의 z가 마디 길이, x가 발판 폭이 된다. up은 월드 위쪽으로
            // 고정해 발판이 옆으로 기울지(roll) 않게 한다 — 처짐으로 앞뒤(pitch)로만 기운다.
            Quaternion rot = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.identity;

            if (s.teleportNext) s.bodies[i].transform.SetPositionAndRotation(mid, rot);
            else
            {
                s.bodies[i].MovePosition(mid);
                s.bodies[i].MoveRotation(rot);
            }
        }
        s.teleportNext = false;
    }

    // ── 경간 풀 ───────────────────────────────────────────────────────────────────────

    /// <summary>index번째 경간을 준다. 없으면 그 자리에서 만든다(필요한 만큼만 만들어, 가지가 안 생기는
    /// 레벨은 경간 하나치 콜라이더만 쓴다). 한 번 만든 경간은 접었다 폈다 하며 재사용한다.</summary>
    private Span EnsureSpan(int index)
    {
        while (spans.Count <= index) spans.Add(BuildSpan(spans.Count));
        return spans[index];
    }

    private Span BuildSpan(int index)
    {
        Span s = new Span
        {
            sag = new float[segmentCount + 1],
            points = new Vector3[segmentCount + 1],
            bodies = new Rigidbody[segmentCount],
            boxes = new BoxCollider[segmentCount]
        };

        GameObject rootObj = new GameObject($"ThreadBridge_Span{index}");
        s.root = rootObj.transform;
        s.root.SetParent(transform, false);

        for (int i = 0; i < segmentCount; i++)
        {
            GameObject seg = new GameObject($"Seg_{i}");
            seg.transform.SetParent(s.root, false);

            BoxCollider box = seg.AddComponent<BoxCollider>();
            box.size = new Vector3(segmentWidth, segmentThickness, 1f);

            Rigidbody rb = seg.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            s.boxes[i] = box;
            s.bodies[i] = rb;
        }

        // 경간 0은 이 컴포넌트에 원래 붙어 있는 LineRenderer를 쓴다 — 인스펙터에서 지정한 머티리얼·
        // 두께가 그대로 살아야 하기 때문. 가지들은 그 설정을 복사한 자식 렌더러를 새로 만든다.
        s.line = index == 0 ? mainLine : CreateBranchLine(rootObj);
        s.line.positionCount = segmentCount + 1;
        s.line.enabled = false;

        s.root.gameObject.SetActive(false);
        return s;
    }

    private LineRenderer CreateBranchLine(GameObject host)
    {
        LineRenderer lr = host.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.widthMultiplier = mainLine.widthMultiplier;
        lr.numCapVertices = mainLine.numCapVertices;
        lr.textureMode = mainLine.textureMode;
        lr.sharedMaterial = mainLine.sharedMaterial;
        lr.startColor = mainLine.startColor;
        lr.endColor = mainLine.endColor;
        return lr;
    }

    private static void ResetSpanState(Span s)
    {
        for (int i = 0; i < s.sag.Length; i++) s.sag[i] = 0f; // 이전 짝의 처짐을 물려받지 않게
        s.teleportNext = true;
        s.builtSpan = -1f;
    }

    private void SetSpanActive(Span s, bool on)
    {
        if (s.active == on) return;
        s.active = on;
        if (s.root != null) s.root.gameObject.SetActive(on);
        if (s.line != null) s.line.enabled = on;
        if (on) ResetSpanState(s);
    }

    // 레벨 배치용: 양 끝 고리를 지정했을 때 아무도 안 탄 상태의 줄 모양(baseSag)을 미리 보여준다.
    // 핀은 런타임 생성이라 편집 중에는 알 수 없어, 혼합·핀 모드는 미리보기가 나오지 않는다.
    void OnDrawGizmos()
    {
        if (anchorA == null || anchorB == null) return;
        Vector3 a = anchorA.transform.position;
        Vector3 b = anchorB.transform.position;
        int n = Mathf.Max(2, segmentCount);

        Gizmos.color = Vector3.Distance(a, b) <= maxSpan
            ? new Color(0.7f, 0.9f, 1f, 0.9f)
            : new Color(1f, 0.4f, 0.3f, 0.9f); // 빨강 = 너무 멀어 이어지지 않음

        Vector3 prev = a;
        for (int i = 1; i <= n; i++)
        {
            float t = (float)i / n;
            Vector3 p = Vector3.Lerp(a, b, t) + Vector3.down * (baseSag * 4f * t * (1f - t));
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
}
