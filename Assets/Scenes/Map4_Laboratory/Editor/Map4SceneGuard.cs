#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// F1 재작업 판정 R3 — 인터랙티브(비-배치) 실행 중에 열려 있는 씬을 저장 확인 없이 교체하지
/// 않기 위한 공용 가드. Map4SceneBuilder.cs·Lab_PlayerBuilder.cs·Map4PlayTestRunner.cs가 씬을
/// NewScene(Single)/OpenScene(Single)로 갈아치우기 직전에 반드시 호출한다.
///
/// 배치모드에서는 항상 통과한다 — 물어볼 사용자가 없고, 배치모드는 애초에 "제목 없는 새 씬"
/// 상태로 시작해 되돌릴 대상 자체가 없다(Lab_PlayerBuilder.cs의 09-28 실측 주석 참고).
/// </summary>
public static class Map4SceneGuard
{
    /// <summary>지금 열린 씬을 갈아치워도 되면 true. 인터랙티브 모드에서 수정된 씬이 있으면 Unity
    /// 표준 저장 대화상자(SaveCurrentModifiedScenesIfUserWantsTo)를 띄운다 — 저장/버림을 고르면
    /// true, 사용자가 취소하면 false. 호출자는 false를 받으면 그 즉시 작업 전체를 중단해야 한다
    /// (Manual/ 미저장 편집 소실을 막는 것이 이 가드의 유일한 목적).</summary>
    public static bool CanReplaceCurrentScenes()
    {
        if (Application.isBatchMode) return true;
        return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
    }
}
#endif
