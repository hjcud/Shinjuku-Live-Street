using UnityEngine;

/// <summary>
/// 차량 이동과 Editor 경로 표시에서 함께 사용하는 회복 곡선 계산
/// </summary>
public static class TrafficRecoveryMath
{
    public static float SmoothRecoveryForwardExit01(float progress)
    {
        float t = Mathf.Clamp01(progress);
        return t * t * (2f - t);
    }

    public static Vector3 AdvanceRecoveryKinematicPose(Vector3 pose, float signedDistance, float curvature)
    {
        float heading = pose.z;
        if (Mathf.Abs(curvature) <= 0.0001f)
        {
            pose.x += signedDistance * Mathf.Cos(heading);
            pose.y += signedDistance * Mathf.Sin(heading);
            return pose;
        }

        float nextHeading = heading + curvature * signedDistance;
        pose.x += (Mathf.Sin(nextHeading) - Mathf.Sin(heading)) / curvature;
        pose.y += (-Mathf.Cos(nextHeading) + Mathf.Cos(heading)) / curvature;
        pose.z = nextHeading;
        return pose;
    }

    public static float SmoothLaneChange01(float progress)
    {
        float t = Mathf.Clamp01(progress);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }
}
