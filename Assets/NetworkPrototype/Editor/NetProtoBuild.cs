using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class NetProtoBuild
{
    // 배치모드: -executeMethod NetProtoBuild.BuildWin  (개발용 빌드여야 패킷 지연 시뮬레이터가 켜진다)
    public static void BuildWin()
    {
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/IsolationRescue_Test_local.unity" }, // 월드는 런타임에 갈아끼우므로 아무 씬이나 하나
            locationPathName = "C:/TheAxiom/netproto_build/NetProto.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development,
        };
        var r = BuildPipeline.BuildPlayer(opts);
        Debug.Log("[NetProto] 빌드 결과: " + r.summary.result + " 오류=" + r.summary.totalErrors);
        EditorApplication.Exit(r.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
