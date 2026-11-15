using UnityEngine;
using UnityEngine.SceneManagement;

// Unity 6은 Auto Generate가 없어, 라이팅을 굽지 않은 씬은 스카이박스 환경광(앰비언트 프로브)이 비어 어둡게 나온다.
// 씬 파일을 건드리지 않고(팀 씬 불변) 씬이 로드될 때마다 환경광을 직접 갱신한다.
static class LevelAmbientRefresh
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init() => SceneManager.sceneLoaded += (_, __) => DynamicGI.UpdateEnvironment();
}
