using System;
using UnityEngine;

/// <summary>
/// 맵4 섹터 8개의 크기·순서·연결 통로 정의(F1-2). 원점(origin)은 저장하지 않고 항상 계산한다 —
/// "섹터가 커지면 뒤 섹터 원점이 자동으로 밀린다"(F1-2 명세).
///
/// 초기 폭/길이/바닥높이 값 출처(F1 재작업 판정 A5, 인용 오류 정정 — 이전 버전은 widths를
/// "TeamLabBuilder.cs:9"로 잘못 인용했었다. TeamLabBuilder.cs:9는 titles[](챕터 제목)이고 widths[]는
/// 존재하지 않는다): 기획작업/맵4_확장실험실_2026-09-22/UnityProject/Assets/LaboratoryMap4/Editor/
/// ExpandedLayout.cs:9(widths) · TeamLabBuilder.cs:24-26(starts/lengths/heights) — [실좌표 09-22].
/// 연결 통로 길이 12·폭 8은 TeamLabBuilder.cs:64 Floor 호출(new Vector3(8,.6f,
/// starts[i+1]-starts[i]-lengths[i]))에서 7개 구간 전부 12로 역산된다(i=0..6 전부 동일) — [실좌표].
///
/// exitHeight(F1 재작업 판정 결정2): 이 섹터 "출구"의 섹터-로컬 윗면 높이. 연결 통로는 항상
/// 평평하게 짓는다 — 통로의 세계 Y = 이 섹터의 floorHeight+exitHeight(섹터 로컬) = 다음 섹터의
/// floorHeight(정확히 일치해야 함, Map4SceneBuilder가 생성 시 검사한다). 높이가 바뀌는 구간
/// (S4: 0→18, S6: 18→34, [실좌표 09-22] TeamLabBuilder.cs:64의 연결 윗면 18/16 근거)은 섹터
/// **안**에서 콘텐츠(반중력·포탈)가 담당한다 — 0단계에서는 그 자리에 TEMP_ 임시 경사로를 넣어
/// 둔다(Map4SceneBuilder.BuildEmptyShell 참고, 섹터 구체화 때 제거 대상).
/// </summary>
[CreateAssetMenu(fileName = "Map4Layout", menuName = "Laboratory Map4/Layout")]
public class Map4Layout : ScriptableObject
{
    [Serializable]
    public class SectorDef
    {
        public int id;                    // 1~8
        public string sectorName;
        [Tooltip("섹터 폭(X, Unit). [실좌표 09-22]")]
        public float width;
        [Tooltip("섹터 길이(Z, Unit). [실좌표 09-22]")]
        public float length;
        [Tooltip("섹터 바닥 높이(Y, Unit) — 수직 단차가 있는 섹터(S5~S8)용. [실좌표 09-22]")]
        public float floorHeight;
        [Tooltip("이 섹터 '출구'의 섹터-로컬 윗면 높이(Unit, floorHeight 기준 상대값 아님 — " +
                 "절대 로컬 좌표). 기본은 floorHeight와 같음(평지 통과). S4=18(=S5.floorHeight)· " +
                 "S6=34(=S7.floorHeight)만 다르다 — 결정2 참고.")]
        public float exitHeight;
        [Tooltip("이 섹터 끝에서 다음 섹터 시작까지의 연결 통로 길이(Z, Unit). 8번째 섹터는 " +
                 "다음이 없어 무시된다. [실좌표 09-22] 역산값 12(모든 구간 동일).")]
        public float connectorLengthToNext = 12f;
        [Tooltip("연결 통로 폭(X, Unit). [실좌표 09-22]")]
        public float connectorWidthToNext = 8f;
    }

    public SectorDef[] sectors = new SectorDef[0];

    /// <summary>섹터 id(1~8)의 원점을 계산한다(Z 누적). X=0 고정(그레이박스가 전 섹터 폭 중앙을
    /// X=0으로 두는 것과 동일), Y=해당 섹터의 floorHeight. id가 목록에 없으면 경고 후 (0,0,0).</summary>
    public Vector3 GetOrigin(int id)
    {
        float z = 0f;
        for (int i = 0; i < sectors.Length; i++)
        {
            SectorDef s = sectors[i];
            if (s.id == id) return new Vector3(0f, s.floorHeight, z);
            z += s.length + s.connectorLengthToNext;
        }
        Debug.LogWarning($"[Map4Layout] 섹터 id {id}를 찾지 못해 원점을 (0,0,0)으로 반환한다.");
        return Vector3.zero;
    }

    public SectorDef GetSector(int id)
    {
        foreach (SectorDef s in sectors)
            if (s.id == id) return s;
        return null;
    }

    /// <summary>09-22 그레이박스 원점(starts[])과 대조하기 위한 기준값 — F1-6 항목3 증거표 생성용.
    /// [실좌표 09-22] TeamLabBuilder.cs:24.</summary>
    public static readonly float[] ReferenceStarts_20260922 = { 0f, 144f, 240f, 436f, 572f, 720f, 830f, 952f };

    /// <summary>[INF1] 09-22 기준 섹터 길이 8개 — Editor/Map4SceneBuilder.cs SectorSource의 원래
    /// 값(= 09-22 lengths, S1이 188로 재설계되기 전, [실좌표 09-22] TeamLabBuilder.cs:24-26)과 같다. WriteOriginComparisonTable이 "이후에도
    /// 의미 있는" 기대 원점을 계산하는 데 쓴다 — 기대 원점[i] = ReferenceStarts_20260922[i] +
    /// Σ_(j&lt;i) (현재 길이[j] − ReferenceLengths_20260922[j]) (연결 통로 길이 변화가 있으면 그것도
    /// 누적에 포함). S1 길이가 132→188(+56)로 바뀌었으면 S2 이후 전부 +56이 정상이다.</summary>
    public static readonly float[] ReferenceLengths_20260922 = { 132f, 84f, 184f, 124f, 136f, 98f, 110f, 132f };
}
