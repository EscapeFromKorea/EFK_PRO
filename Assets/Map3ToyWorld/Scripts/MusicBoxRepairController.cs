using UnityEngine;

/// <summary>
/// Map3 진행 연결 전용. 레버의 충돌/회전과 문의 이동은 기존 DoorSystem이 담당하고,
/// 이 컴포넌트는 설치 완료 뒤 레버가 충분히 밀렸는지만 진행 상태에 반영한다.
/// </summary>
public sealed class MusicBoxRepairController : MonoBehaviour
{
    public ToyWorldLevelDirector director;
    public ToyWorldInstallSocket[] installSockets;
    public LeverHead activationLever;
    public doorPhysics activationDoor;
    public float activationAngle = 20f;

    private void Update()
    {
        ResolveDirector();
        if (director == null) return;

        if (!director.IsMusicBoxActivated && director.AllItemsInstalled && activationLever != null &&
            Mathf.Abs(activationLever.GetCurrentAngle()) >= activationAngle)
            director.TryActivateMusicBox();

        if (activationDoor != null)
            activationDoor.SetPadPressed(director.IsMusicBoxActivated);
    }

    public void RefreshInstallVisuals()
    {
        if (installSockets == null) return;
        foreach (ToyWorldInstallSocket socket in installSockets)
            if (socket != null) socket.Refresh();
    }

    private void ResolveDirector()
    {
        if (director == null) director = ToyWorldLevelDirector.Instance;
    }
}
