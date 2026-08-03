using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > WindZoneSystem > Create Wind Zone 메뉴로 돌풍 구역을 생성한다.
/// BoxCollider(트리거) + WindZone 스크립트 + 바람 방향을 보여주는 ParticleSystem 자식까지
/// 한 번에 배선해, 씬에 놓자마자 크기/속도만 튜닝하면 되게 한다.
/// </summary>
public static class WindZoneMenuItem
{
    [MenuItem("Tools/WindZoneSystem/Create Wind Zone")]
    private static void CreateWindZone()
    {
        Vector3 spawnPos = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            spawnPos = SceneView.lastActiveSceneView.pivot;

        GameObject zone = new GameObject("WindZone");
        Undo.RegisterCreatedObjectUndo(zone, "Create Wind Zone");
        zone.transform.position = spawnPos;

        Vector3 boxSize = new Vector3(4f, 3f, 4f);
        BoxCollider box = zone.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = boxSize;

        WindZone windZone = zone.AddComponent<WindZone>();

        GameObject visual = new GameObject("WindZone_Visual");
        Undo.RegisterCreatedObjectUndo(visual, "Create Wind Zone");
        visual.transform.SetParent(zone.transform, false);
        ParticleSystem ps = visual.AddComponent<ParticleSystem>();
        ConfigureWindParticles(ps, boxSize);

        // WindZone에 연결해둬야 이후 BoxCollider를 씬에서 리사이즈할 때(특히 한쪽 면 핸들만
        // 드래그하면 center도 같이 움직인다) 파티클 방출 범위가 자동으로 따라간다.
        windZone.windVisual = ps;

        Selection.activeGameObject = zone;
        Debug.Log("[WindZoneSystem] WindZone 생성 완료");
    }

    // 구역 forward(+Z) 방향으로 흐르는 "바람줄기"(길게 늘어지는 속도선)를 박스 전체에 뿌려,
    // 플레이어가 구역에 들어가기 전에도 방향을 눈으로 알 수 있게 한다. WindZone.windSpeed와는
    // 독립된 순수 시각 효과이므로 굳이 실시간으로 windSpeed를 따라갈 필요는 없다.
    private static void ConfigureWindParticles(ParticleSystem ps, Vector3 boxSize)
    {
        var main = ps.main;
        main.loop = true;
        main.startLifetime = Mathf.Max(0.5f, boxSize.z / 4f);
        main.startSpeed = 0f; // 이동은 velocityOverLifetime이 전담
        main.startSize = 0.02f; // 막대기처럼 얇게 — 스트레치는 렌더러가 속도 방향으로 늘려 그린다
        main.startColor = new Color(0.85f, 0.95f, 1f, 0.5f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 150;
        // [핵심] 새 ParticleSystem의 기본 scalingMode는 Local — 이 오브젝트 자신의 로컬 스케일만
        // 반영하고 부모(WindZone)의 스케일은 무시한다. WindZone_Visual 자신은 항상 로컬 스케일
        // (1,1,1)이라, 사용자가 WindZone을 Scale 툴로 늘리면(예: Y/Z만 키움) BoxCollider 바운드는
        // 전체 계층 스케일을 그대로 반영해 커지는데 파티클 방출 범위는 안 커져 트리거 한쪽에만
        // 남는다. 그렇다고 Hierarchy로 바꾸면 방출 "범위"뿐 아니라 파티클 자체의 크기/스트레치
        // 길이까지 부모 스케일만큼 부풀어(구역을 몇 배로 늘리면 파티클도 몇 배로 두꺼워짐) 안에서
        // 플레이어가 안 보일 정도로 두꺼운 덩어리가 됐다(플레이테스트로 확인). Shape 모드는 Shape
        // 모듈(방출 범위)에만 부모 스케일을 반영하고 Start Size/Speed 등 파티클 자체 크기는
        // 그대로 유지해, "범위는 넓어지되 굵기는 안 변하는" 정확히 원하는 동작이 된다.
        main.scalingMode = ParticleSystemScalingMode.Shape;
        // Automatic 컬링은 스트레치로 원래 크기보다 훨씬 길게 그려지는 파티클의 실제 화면 범위를
        // 반영하지 못해, 카메라 프레이밍에 따라 구역 한쪽이 통째로 컬링될 수 있다. 파티클 수가
        // 적어(최대 150) 비용이 작으니 아예 컬링하지 않는다(방어적 조치, 확인된 원인은 아님).
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        var emission = ps.emission;
        emission.rateOverTime = 25f; // 얇고 긴 줄기라 밀도를 낮춰야 개별 선이 겹쳐 뭉개지지 않는다

        // 초기값만 여기서 잡아둔다 — 이후 실제 값은 WindZone.LateUpdate가 BoxCollider의
        // size/center를 그대로 따라가며 매 프레임 덮어쓴다(구역을 리사이즈해도 항상 일치).
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.position = Vector3.zero;
        shape.rotation = Vector3.zero;
        shape.scale = boxSize;

        // [경고 회피] Velocity over Lifetime의 x/y/z는 전부 같은 MinMaxCurve 모드여야 한다("Particle
        // Velocity curves must all be in the same mode" 경고). z만 TwoConstants로 설정하고 x/y를
        // 기본값(Constant)으로 남겨두면 모드가 어긋나 경고가 뜬다 — x/y도 TwoConstants(0,0)으로
        // 명시해 항상 셋이 같은 모드가 되게 한다.
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.z = new ParticleSystem.MinMaxCurve(6f, 9f);

        // Stretched Billboard: 파티클의 실제 이동 속도 방향으로 길게 늘여 그려, 정적인 먼지 입자가
        // 아니라 "빠르게 흐르는 바람줄기"로 읽히게 한다(사용자 선택 스타일).
        // Stretch 렌더링은 카메라 시선이 파티클 속도 방향과 거의 평행해질수록 폭이 0에 가깝게
        // 찌그러져 사실상 안 보이게 될 수 있다 — minParticleSize로 화면 대비 최소 크기를 보장해
        // 각도가 나빠도 완전히 사라지지 않게 한다(방어적 조치, 확인된 원인은 아님 — 실제로 신고된
        // "구역 절반만 보임"의 확인된 원인은 리사이즈 시 파티클 shape가 안 따라가는 것이었다.
        // WindZone.SyncVisualToBox 참고).
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = CreateWindMaterial();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.15f;
        renderer.lengthScale = 2.5f;
        renderer.minParticleSize = 0.02f;
    }

    private static Material CreateWindMaterial()
    {
        Shader shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        return new Material(shader);
    }
}
