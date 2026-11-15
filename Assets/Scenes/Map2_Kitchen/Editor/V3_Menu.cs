#if UNITY_EDITOR
using UnityEditor;
public static class V3Menu {
 [MenuItem("Tools/KitchenMapV3/1. Clear Blockout")] public static void Clear()=>V3Build.Clear();
 [MenuItem("Tools/KitchenMapV3/2. Build All (팀 기믹)")] public static void BuildAll()=>V3Build.BuildAll();
 [MenuItem("Tools/KitchenMapV3/3. Setup Play (팀 플레이어·카메라)")] public static void SetupPlay()=>V3Play.SetupPlay();
 [MenuItem("Tools/KitchenMapV3/4. Audit (진단)")] public static void Audit()=>V3Audit.Run();
}
#endif