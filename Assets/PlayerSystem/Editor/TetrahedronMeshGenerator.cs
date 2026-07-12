// 이 파일은 반드시 프로젝트의 "Editor" 폴더 안에 위치해야 합니다.
// UnityEditor 네임스페이스는 쓰지 않지만, 이 메쉬는 Tools 메뉴(에디터 전용) 생성 도구에서만
// 쓰이므로 다른 Editor 스크립트들과 같은 위치에 둡니다.

using UnityEngine;

/// <summary>
/// Unity 기본 Primitive에 없는 정사면체 Mesh를 코드로 생성한다.
/// 면마다 정점을 따로 두어(플랫 셰이딩) 큐브/스피어와 비슷한 각진 느낌을 유지한다.
/// </summary>
public static class TetrahedronMeshGenerator
{
    /// <summary>
    /// 정사면체의 네 꼭짓점(중복 없는 원본 좌표, 무게중심은 원점)을 반환한다. Create()가 만드는
    /// vertices 배열은 면마다 플랫 셰이딩용으로 정점을 중복시킨 것이라, 콜라이더를 꼭짓점 위치에
    /// 직접 배치해야 하는 곳(PlayerObjectMenuItem의 정사면체 컴파운드 콜라이더)에서는 이 메서드로
    /// 원본 4개 좌표를 얻어 쓴다.
    /// </summary>
    public static Vector3[] GetVertices(float scale = 0.5f)
    {
        // 정육면체에 내접하는 정사면체의 네 꼭짓점
        return new[]
        {
            new Vector3(1, 1, 1) * scale,
            new Vector3(1, -1, -1) * scale,
            new Vector3(-1, 1, -1) * scale,
            new Vector3(-1, -1, 1) * scale,
        };
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
}
