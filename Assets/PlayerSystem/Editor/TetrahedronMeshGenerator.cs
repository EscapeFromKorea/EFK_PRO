// 이 파일은 반드시 프로젝트의 "Editor" 폴더 안에 위치해야 합니다.
// UnityEditor 네임스페이스는 쓰지 않지만, 이 메쉬는 Tools 메뉴(에디터 전용) 생성 도구에서만
// 쓰이므로 다른 Editor 스크립트들과 같은 위치에 둡니다.

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unity 기본 Primitive에 없는 정사면체 Mesh를 코드로 생성한다.
/// 면마다 정점을 따로 두어(플랫 셰이딩) 큐브/스피어와 비슷한 각진 느낌을 유지한다.
/// </summary>
public static class TetrahedronMeshGenerator
{
    /// <summary>
    /// 정사면체의 네 꼭짓점(중복 없는 원본 좌표, 무게중심은 원점)을 반환한다. Create()가 만드는
    /// vertices 배열은 면마다 플랫 셰이딩용으로 정점을 중복시킨 것이라, 원본 4개 좌표가 필요한
    /// 곳(CreateChamferedColliderMesh 등)에서는 이 메서드를 쓴다.
    /// </summary>
    public static Vector3[] GetVertices(float scale = 0.5f)
    {
        // 정육면체에 내접하는 정사면체의 네 꼭짓점(무게중심은 원점).
        Vector3[] raw =
        {
            new Vector3(1, 1, 1) * scale,
            new Vector3(1, -1, -1) * scale,
            new Vector3(-1, 1, -1) * scale,
            new Vector3(-1, -1, 1) * scale,
        };

        // 위 좌표 그대로면 (1,1,1) 축이 세로가 되어 위아래가 모두 "모서리"로 놓인다(면이 바닥에
        // 안 닿음). 한 면이 바닥에 평평히 닿고 반대 꼭짓점이 위를 향하도록, (1,1,1) 방향을 월드
        // up으로 정렬한다. 원점 기준 회전이라 무게중심은 그대로 원점에 유지되고, 시각 메쉬와
        // 콜라이더 메쉬 모두 이 메서드를 거치므로 한 번에 정합하게 재정렬된다.
        Quaternion faceDown = Quaternion.FromToRotation(new Vector3(1f, 1f, 1f), Vector3.up);
        for (int i = 0; i < raw.Length; i++)
            raw[i] = faceDown * raw[i];

        return raw;
    }

    /// <summary>
    /// 정사면체 메쉬를 생성한다. scale은 Cube/Sphere 기본 Primitive와 맞춘 반경 기준값(기본 0.5).
    /// </summary>
    public static Mesh Create(float scale = 0.5f)
    {
        Vector3[] v = GetVertices(scale);
        Vector3 a = v[0], b = v[1], c = v[2], d = v[3];

        // 네 면 모두 바깥쪽을 향하도록 검증된 정점 순서
        Vector3[] vertices =
        {
            a, b, c, // Face 1
            a, d, b, // Face 2
            a, c, d, // Face 3
            b, d, c, // Face 4
        };

        int[] triangles = new int[vertices.Length];
        for (int i = 0; i < triangles.Length; i++)
            triangles[i] = i;

        Mesh mesh = new Mesh { name = "Tetrahedron" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        return mesh;
    }

    /// <summary>
    /// 솔리드 콜라이더 전용 "모서리를 살짝 깎은" 정사면체 메쉬를 만든다. 시각 메쉬(Create())는
    /// 뾰족한 원본 그대로 두고, PlayerObjectMenuItem의 솔리드 콜라이더에만 이 메쉬를 쓴다.
    ///
    /// [왜 컴파운드 SphereCollider가 아니라 이 방식인가]
    /// 꼭짓점 4개(+ 변 중점 6개)짜리 SphereCollider 컴파운드로 근사했을 때, 변/면 중간을 덮을
    /// 만큼 반지름을 키우면 오히려 한 강체에 딸린 여러 개의 겹치는 프리미티브가 굴러가는 동안
    /// 동시에 접촉해(변 하나에 꼭짓점 2개+중점 1개가 거의 동시에 닿는 식) PhysX 솔버가 서로 다른
    /// 분리 방향을 요구하는 접촉점들 사이에서 버벅였다. 반지름을 작게 유지하면 이번엔 그 사이
    /// 틈으로 메쉬가 파고들었다 — 틈을 메우는 것과 접촉 개수를 줄이는 것이 상충했다.
    ///
    /// 대신 각 꼭짓점을 이웃 꼭짓점 쪽으로 truncateFactor만큼 살짝 깎아 뾰족한 점 대신 작은
    /// 평평한 면으로 만들고, 그 12개 점을 "단 하나의" Convex MeshCollider로 쿠킹한다. 콜라이더가
    /// 하나뿐이라 PhysX가 표준적인 단일 볼록체 접촉으로 다뤄 컴파운드 특유의 접촉 충돌이 아예
    /// 사라지고, 깎인 면 덕에 완전히 뾰족한 점으로 접지할 때 생기던 접촉 불연속도 줄어들며,
    /// 통짜 볼록 껍질이라 프리미티브 사이 미접촉 틈 자체가 없다.
    ///
    /// 렌더링용이 아니라 콜라이더 전용 메쉬라 삼각형 구성 자체의 정확도는 중요하지 않다 —
    /// MeshCollider(convex=true)가 실제로는 이 정점들로부터 다시 계산한 볼록 껍질을 쓰므로,
    /// 여기서는 모든 정점을 참조하는 유효한 삼각형(부채꼴 삼각분할)만 채운다.
    /// </summary>
    public static Mesh CreateChamferedColliderMesh(float scale = 0.5f, float truncateFactor = 0.15f)
    {
        Vector3[] v = GetVertices(scale);
        var vertices = new List<Vector3>(12);
        for (int i = 0; i < v.Length; i++)
            for (int j = 0; j < v.Length; j++)
                if (i != j)
                    vertices.Add(Vector3.Lerp(v[i], v[j], truncateFactor));

        var triangles = new List<int>();
        for (int i = 1; i < vertices.Count - 1; i++)
        {
            triangles.Add(0);
            triangles.Add(i);
            triangles.Add(i + 1);
        }

        Mesh mesh = new Mesh { name = "Tetrahedron_ChamferedCollider" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}
