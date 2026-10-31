using UnityEngine;

/// <summary>장전 팔의 당김 앵커를 따라가는 시각 전용 고무줄. 물리와 입력에는 관여하지 않는다.</summary>
[ExecuteAlways]
public sealed class KitchenSpoonBands : MonoBehaviour
{
    public Transform movingAnchor;
    public Transform leftTie;
    public Transform rightTie;
    public LineRenderer leftBand;
    public LineRenderer rightBand;

    private void LateUpdate()
    {
        UpdateBand(leftBand, leftTie);
        UpdateBand(rightBand, rightTie);
    }

    private void UpdateBand(LineRenderer band, Transform tie)
    {
        if (band == null || tie == null || movingAnchor == null) return;
        Vector3 start = band.transform.InverseTransformPoint(tie.position);
        Vector3 end = band.transform.InverseTransformPoint(movingAnchor.position);
        band.useWorldSpace = false;
        band.positionCount = 3;
        band.SetPosition(0, start);
        band.SetPosition(1, Vector3.Lerp(start, end, .5f) + Vector3.down * .06f);
        band.SetPosition(2, end);
    }
}
