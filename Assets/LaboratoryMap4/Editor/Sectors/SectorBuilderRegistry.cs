#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// 섹터 전용 빌더 등록부(지시서 INF1 — C3). Map4SceneBuilder가 섹터별 구체화 빌더를 직접 이름으로
/// 부르는 대신 이 스위치를 통해 부른다 — 새 섹터 빌더가 생길 때마다 Map4SceneBuilder를 고치지
/// 않아도 된다.
///
/// 계약(각 S{n}_Builder.Build(Transform generated, Map4Layout.SectorDef def)):
/// - 전제(섹터 크기·바닥/출구 높이 등 설계서가 가정한 조건)와 def가 맞지 않으면 Debug.LogError 후
///   false를 돌려준다 — 이때 generated 아래에는 아무것도 남기지 않는다(부분 생성 금지, 호출자가
///   빈 틀 BuildEmptyShell로 되돌아간다).
/// - 전제가 맞아 실제로 지었으면 true. 전용 빌더는 자기 섹터 지형·기믹만 짓고, 연결 통로(다음
///   섹터로 이어지는 평평한 복도)와 마커(Entrance/Checkpoint/Spawn_0~2/Exit)는 공용
///   Map4SceneBuilder.BuildConnectorAndMarkers가 짓는다 — 전용 빌더가 그 구역(z ∈
///   [def.length, def.length + connectorLengthToNext])에 Floor를 만들면 겹침 검사에 걸린다.
/// - floorHeight != exitHeight인 섹터(예: S4)의 전용 빌더는 출구 높이까지 오르는 길(경사로 등)을
///   스스로 책임진다 — 공용 코드가 대신 넣어주는 TEMP_Ramp는 없다(빈 틀 경로에서만 TEMP_Ramp가
///   생긴다).
/// - id가 1..8 밖이면 false를 돌려준다(호출자가 빈 틀로 되돌아간다).
/// </summary>
public static class SectorBuilderRegistry
{
    public static bool TryBuild(int id, Transform generated, Map4Layout.SectorDef def)
    {
        switch (id)
        {
            case 1: return S1_Builder.Build(generated, def);
            case 2: return S2_Builder.Build(generated, def);
            case 3: return S3_Builder.Build(generated, def);
            case 4: return S4_Builder.Build(generated, def);
            case 5: return S5_Builder.Build(generated, def);
            case 6: return S6_Builder.Build(generated, def);
            case 7: return S7_Builder.Build(generated, def);
            case 8: return S8_Builder.Build(generated, def);
            default: return false;
        }
    }
}
#endif
