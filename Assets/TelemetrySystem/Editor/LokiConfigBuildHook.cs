using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace TelemetrySystem.Editor
{
    class LokiConfigBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => LokiConfigSync.Sync();
    }
}
