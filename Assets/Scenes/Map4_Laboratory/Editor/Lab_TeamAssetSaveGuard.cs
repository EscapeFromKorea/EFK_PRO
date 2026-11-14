#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 팀 파일 보존 장치(HANDOFF §5-3 "LaboratoryMap4 밖은 한 바이트도 바꾸지 않는다").
///
/// [컨트롤타워 판정 2026-09-28, 사용자 허락 21:43 — R1 검문 기반·S1 치명 지적] S1_Builder가 팀 메뉴
/// SlingCatapultMenuItem.BuildSlingCatapult로 투석기를 만들 때, 팀 코드
/// CatapultMenuItem.LoadOrCreateMaterial(694-711행)이 기존 팀 .mat에 SetDirty를 걸고
/// SlingCatapultMenuItem.cs:108의 AssetDatabase.SaveAssets()가 그 .mat 6개를 다시 저장한다
/// (내용 동일, 줄바꿈만 CRLF→LF — R1~R3 hash_post FAIL 6건). 팀 코드는 고칠 수 없으므로(§5-0·§5-3),
/// 편집기가 LaboratoryMap4 밖 Assets 파일을 디스크에 쓰려는 저장 요청을 여기서 걸러 낸다.
/// 메모리 안 에셋 상태는 그대로라 팀 기믹 생성·동작에는 영향이 없다.
///
/// 허용: Assets/Scenes/Map4_Laboratory/ 아래 전부, Assets 밖 경로(ProjectSettings/EditorBuildSettings.asset —
/// 00_기반_설계 결정8 허용 예외 등). 이 장치는 기믹이 아니라 저장 경로 필터다. 걸러 낼 때마다 경고 로그를 남긴다.
/// </summary>
public class Lab_TeamAssetSaveGuard : AssetModificationProcessor
{
    private const string OurRoot = "Assets/Scenes/Map4_Laboratory/";

    private static string[] OnWillSaveAssets(string[] paths)
    {
        List<string> allowed = new List<string>(paths.Length);
        foreach (string p in paths)
        {
            string n = p.Replace('\\', '/');
            bool teamAsset = n.StartsWith("Assets/") && !n.StartsWith(OurRoot);
            if (teamAsset)
            {
                Debug.LogWarning($"[Lab_TeamAssetSaveGuard] 팀 파일 저장 차단: {n} " +
                                 "(팀 파일 불변 규칙 — 메모리 상태는 유지, 디스크 쓰기만 막음)");
                continue;
            }
            allowed.Add(p);
        }
        return allowed.ToArray();
    }
}
#endif
