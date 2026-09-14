using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// `RailPath`를 실제 철도처럼 보이게 그리는 순수 시각 컴포넌트(콜라이더 없음, 물리에 관여하지
/// 않는다). 두 개의 `LineRenderer`로 레일 두 줄을 곡선 그대로 그리고, 그 사이에 침목(sleeper)
/// 큐브를 일정 간격으로 깔아 둔다. `[ExecuteAlways]`라 에디터에서 웨이포인트나 곡선 제어점을
/// 옮기면 즉시 다시 그려진다(2026-09-05, 곡선 저작 시 시각 피드백 요구 반영) — 플레이 모드에서는
/// 경로가 런타임에 안 바뀌므로 최초 1회만 만들고 매 프레임 다시 그리지 않는다.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RailPath))]
public class RailTrackVisual : MonoBehaviour
{
    [Header("레일")]
    [Tooltip("두 레일 사이 간격(궤간).")]
    public float gauge = 0.5f;
    [Tooltip("레일 선 굵기(LineRenderer 폭).")]
    public float railWidth = 0.06f;
    public Color railColor = new Color(0.35f, 0.35f, 0.4f);

    [Header("침목")]
    [Tooltip("샘플 몇 개마다 침목 하나를 놓을지 — 값이 작을수록 촘촘하다.")]
    public int sleeperEverySamples = 3;
    [Tooltip("침목 하나의 크기(가로=선로 가로지르는 방향, 세로=두께, 깊이=진행 방향).")]
    public Vector3 sleeperSize = new Vector3(0.9f, 0.06f, 0.18f);
    public Color sleeperColor = new Color(0.32f, 0.22f, 0.14f);

    [Tooltip("구간 하나를 몇 조각으로 쪼개 곡선을 근사할지.")]
    public int samplesPerSegment = 20;

    private RailPath path;
    private LineRenderer leftRail;
    private LineRenderer rightRail;
    private readonly List<Transform> sleeperPool = new List<Transform>();
    private Material railMaterialInstance;
    private Material sleeperMaterialInstance;
    private bool builtOnce;

    void OnEnable()
    {
        path = GetComponent<RailPath>();
        leftRail = FindOrCreateRail("Rail_Left");
        rightRail = FindOrCreateRail("Rail_Right");
        builtOnce = false;
    }

    void Update()
    {
        // 플레이 중엔 경로가 런타임에 바뀌지 않으므로 최초 1회만 만든다 — 에디터에서 곡선 제어점을
        // 드래그하는 동안에는(플레이 중이 아님) 매 프레임 다시 그려 즉시 피드백을 준다.
        if (Application.isPlaying && builtOnce) return;
        if (path == null) path = GetComponent<RailPath>();
        Rebuild();
        builtOnce = true;
    }

    /// <summary>배치 스크립트 등에서 Update() 틱을 기다리지 않고 즉시 한 번 그리고 싶을 때 쓴다.</summary>
    public void ForceRebuild()
    {
        if (path == null) path = GetComponent<RailPath>();
        if (leftRail == null) leftRail = FindOrCreateRail("Rail_Left");
        if (rightRail == null) rightRail = FindOrCreateRail("Rail_Right");
        Rebuild();
        builtOnce = true;
    }

    private LineRenderer FindOrCreateRail(string railName)
    {
        Transform existing = transform.Find(railName);
        GameObject go = existing != null ? existing.gameObject : new GameObject(railName);
        go.transform.SetParent(transform, false);
        LineRenderer lr = go.GetComponent<LineRenderer>();
        if (lr == null) lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.widthMultiplier = railWidth;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (railMaterialInstance == null)
            railMaterialInstance = new Material(Shader.Find("Standard")) { color = railColor };
        lr.sharedMaterial = railMaterialInstance;
        return lr;
    }

    private void Rebuild()
    {
        if (path == null || path.SegmentCount <= 0 || leftRail == null || rightRail == null) return;

        var centerPoints = new List<Vector3>();
        int samples = Mathf.Max(1, samplesPerSegment);
        for (int seg = 0; seg < path.SegmentCount; seg++)
        {
            int startI = seg == 0 ? 0 : 1; // 구간 경계 중복 방지
            for (int i = startI; i <= samples; i++)
                centerPoints.Add(path.Evaluate(seg, (float)i / samples));
        }
        if (centerPoints.Count < 2) return;

        var leftPoints = new Vector3[centerPoints.Count];
        var rightPoints = new Vector3[centerPoints.Count];
        for (int i = 0; i < centerPoints.Count; i++)
        {
            Vector3 dir = i < centerPoints.Count - 1
                ? centerPoints[i + 1] - centerPoints[i]
                : centerPoints[i] - centerPoints[i - 1];
            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized * (gauge * 0.5f);
            leftPoints[i] = centerPoints[i] - side;
            rightPoints[i] = centerPoints[i] + side;
        }

        leftRail.positionCount = leftPoints.Length;
        leftRail.SetPositions(leftPoints);
        rightRail.positionCount = rightPoints.Length;
        rightRail.SetPositions(rightPoints);

        RebuildSleepers(centerPoints);
    }

    private void RebuildSleepers(List<Vector3> centerPoints)
    {
        int stride = Mathf.Max(1, sleeperEverySamples);
        int needed = 0;
        for (int i = 0; i < centerPoints.Count; i += stride) needed++;

        Transform holder = EnsureSleeperHolder();
        EnsureSleeperPoolSize(holder, needed);

        int used = 0;
        for (int i = 0; i < centerPoints.Count; i += stride)
        {
            Vector3 dir = i < centerPoints.Count - 1
                ? centerPoints[i + 1] - centerPoints[i]
                : centerPoints[i] - centerPoints[Mathf.Max(0, i - 1)];
            if (dir.sqrMagnitude < 1e-8f) dir = Vector3.forward;

            Transform sleeper = sleeperPool[used++];
            sleeper.gameObject.SetActive(true);
            sleeper.position = centerPoints[i];
            sleeper.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            sleeper.localScale = sleeperSize;
        }
        for (int i = used; i < sleeperPool.Count; i++)
            sleeperPool[i].gameObject.SetActive(false);
    }

    private Transform EnsureSleeperHolder()
    {
        Transform holder = transform.Find("Sleepers");
        if (holder == null)
        {
            var holderGo = new GameObject("Sleepers");
            holderGo.transform.SetParent(transform, false);
            holder = holderGo.transform;
        }
        return holder;
    }

    // 매 프레임 인스턴스화/파괴하지 않도록 기존 자식을 풀로 재사용하고, 모자란 만큼만 새로 만든다.
    private void EnsureSleeperPoolSize(Transform holder, int count)
    {
        if (sleeperPool.Count == 0)
            for (int i = 0; i < holder.childCount; i++) sleeperPool.Add(holder.GetChild(i));

        while (sleeperPool.Count < count)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Sleeper";
            DestroyAny(cube.GetComponent<Collider>());
            cube.transform.SetParent(holder, false);
            if (sleeperMaterialInstance == null)
                sleeperMaterialInstance = new Material(Shader.Find("Standard")) { color = sleeperColor };
            cube.GetComponent<Renderer>().sharedMaterial = sleeperMaterialInstance;
            sleeperPool.Add(cube.transform);
        }
    }

    private static void DestroyAny(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }
}
