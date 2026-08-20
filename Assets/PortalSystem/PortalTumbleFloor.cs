using UnityEngine;

/// <summary>
/// 굴리기 모드 설계·테스트용 전용 바닥. 기능은 순수 시각 격자 오버레이뿐이다 — 텀블 로직은 이
/// 바닥이 있든 평범한 바닥이든 똑같이 동작한다(격자 원점을 저장하지 않고 매 텀블 접촉 모서리에서
/// 다시 구하기 때문, <see cref="PlayerRollModeReceiver"/> 상단 주석 참고). 격자는 정육면체/정사면체
/// 한 칸 크기를 눈으로 보여줘 굴리기 구간 배치·정렬을 돕는 도구일 뿐, 어떤 도형이 여기서 굴러도
/// 된다 — "전용"은 격자가 어떤 도형 기준으로 그려지느냐를 뜻하지 통행 제한이 아니다.
///
/// <b>격자는 Scene 뷰에서만 보인다.</b> <see cref="OnDrawGizmos"/>는 Game 뷰·빌드에는 그려지지
/// 않으므로, 배치할 때는 보이고 실제 플레이·테스트에서는 안 보이게 하려고 별도 토글을 만들 필요가
/// 없다.
/// </summary>
public class PortalTumbleFloor : MonoBehaviour
{
    public enum GridMode { Cube, Tetrahedron, Both }

    [Tooltip("어떤 도형의 칸 크기로 격자를 그릴지. Both는 두 격자를 색을 달리해 겹쳐 그려 정렬이 " +
             "맞는 지점을 눈으로 비교하게 한다 — 두 격자 피치는 √(3/2) 무리수 비율이라 완전히는 " +
             "안 맞물린다(PRD §5.2).")]
    public GridMode gridMode = GridMode.Cube;

    [Tooltip("격자를 그릴 정사각 범위(월드 유닛, 이 오브젝트 위치가 중심). 바닥 실제 크기와 " +
             "무관하다 — 바닥보다 넓게 그려져도 무해하다.")]
    public float gridSize = 8f;

    private static readonly Color CubeColor = new Color(0.3f, 0.75f, 1f, 0.7f);
    private static readonly Color TetraColor = new Color(1f, 0.55f, 0.15f, 0.7f);

    private void OnDrawGizmos()
    {
        if (gridMode == GridMode.Cube || gridMode == GridMode.Both)
            DrawGrid(PlayerRollModeReceiver.CubeCellSize, CubeColor);
        if (gridMode == GridMode.Tetrahedron || gridMode == GridMode.Both)
            DrawGrid(PlayerRollModeReceiver.TetrahedronCellSize, TetraColor);
    }

    private void DrawGrid(float cellSize, Color color)
    {
        int lines = Mathf.Max(1, Mathf.RoundToInt(gridSize / cellSize));
        float half = lines * cellSize * 0.5f;
        Vector3 origin = transform.position;
        Gizmos.color = color;

        for (int i = 0; i <= lines; i++)
        {
            float offset = -half + i * cellSize;
            Gizmos.DrawLine(origin + new Vector3(offset, 0.01f, -half), origin + new Vector3(offset, 0.01f, half));
            Gizmos.DrawLine(origin + new Vector3(-half, 0.01f, offset), origin + new Vector3(half, 0.01f, offset));
        }
    }
}
