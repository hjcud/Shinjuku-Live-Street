using UdonSharp;
using UnityEngine;

/// <summary>
/// 베이크된 차선 샘플과 차선 변경 규칙을 런타임 교통 시뮬레이션에 제공한다.
/// </summary>
/// <remarks>
/// 배열의 인덱스와 길이는 베이커가 함께 생성하는 데이터 계약이다. 런타임에서는
/// 데이터를 변경하지 않으며, 모든 거리는 차선 시작점을 기준으로 한 m 단위이다.
/// </remarks>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class TrafficLaneDatabase : UdonSharpBehaviour
{
    public const int FixedLaneCount = 7;

    public const int LaneL1 = 0;
    public const int LaneL2 = 1;
    public const int LaneL3 = 2;
    public const int LaneR1 = 3;
    public const int LaneR2 = 4;
    public const int LaneR3 = 5;
    public const int LaneR4Branch = 6;

    public const int VehicleCar = 1;
    public const int VehicleTruck = 2;

    [HideInInspector] public int laneCount = FixedLaneCount;
    [HideInInspector] public float sampleSpacing = 2f;

    [HideInInspector] public int[] laneSampleStarts = new int[0];
    [HideInInspector] public int[] laneSampleCounts = new int[0];

    [HideInInspector] public float[] sampleDistances = new float[0];
    [HideInInspector] public Vector3[] samplePositions = new Vector3[0];
    [HideInInspector] public Quaternion[] sampleRotations =
        new Quaternion[0];

    [HideInInspector] public float[] laneLengths = new float[0];
    [HideInInspector] public int[] laneVehicleMasks = new int[0];

    [HideInInspector] public float[] spawnS = new float[0];
    [HideInInspector] public float[] despawnS = new float[0];
    [HideInInspector] public float[] spawnWeights = new float[0];
    [HideInInspector] public float[] speedLimits = new float[0];

    [HideInInspector] public float[] stopLineS = new float[0];
    [HideInInspector] public int[] signalGroupIds = new int[0];

    [HideInInspector] public int[] laneRuleStarts = new int[0];
    [HideInInspector] public int[] laneRuleCounts = new int[0];

    [HideInInspector] public int[] changeToLaneIds = new int[0];
    [HideInInspector] public float[] changeStartS = new float[0];
    [HideInInspector] public float[] changeEndS = new float[0];
    [HideInInspector] public int[] changeVehicleMasks = new int[0];

    /// <summary>
    /// 교통 관리자에서 사용할 수 있도록 베이크 데이터의 배열 구조를 검증한다.
    /// </summary>
    /// <returns>필수 배열과 차선별 데이터 길이가 유효하면 <c>true</c>이다.</returns>
    /// <remarks>관리자 초기화 시 한 번만 호출하며 매 프레임 호출하지 않는다.</remarks>
    public bool IsReady()
    {
        if (laneCount != FixedLaneCount ||
            sampleSpacing <= 0f)
        {
            return false;
        }

        if (laneSampleStarts == null ||
            laneSampleCounts == null ||
            laneLengths == null ||
            laneVehicleMasks == null ||
            spawnS == null ||
            despawnS == null ||
            spawnWeights == null ||
            speedLimits == null ||
            stopLineS == null ||
            signalGroupIds == null ||
            laneRuleStarts == null ||
            laneRuleCounts == null)
        {
            return false;
        }

        if (laneSampleStarts.Length != laneCount ||
            laneSampleCounts.Length != laneCount ||
            laneLengths.Length != laneCount ||
            laneVehicleMasks.Length != laneCount ||
            spawnS.Length != laneCount ||
            despawnS.Length != laneCount ||
            spawnWeights.Length != laneCount ||
            speedLimits.Length != laneCount ||
            stopLineS.Length != laneCount ||
            signalGroupIds.Length != laneCount ||
            laneRuleStarts.Length != laneCount ||
            laneRuleCounts.Length != laneCount)
        {
            return false;
        }

        if (sampleDistances == null ||
            samplePositions == null ||
            sampleRotations == null)
        {
            return false;
        }

        int sampleCount = sampleDistances.Length;

        if (sampleCount < 2 ||
            samplePositions.Length != sampleCount ||
            sampleRotations.Length != sampleCount)
        {
            return false;
        }

        if (changeToLaneIds == null ||
            changeStartS == null ||
            changeEndS == null ||
            changeVehicleMasks == null)
        {
            return false;
        }

        int ruleCount = changeToLaneIds.Length;

        return changeStartS.Length == ruleCount &&
               changeEndS.Length == ruleCount &&
               changeVehicleMasks.Length == ruleCount;
    }

    /// <summary>
    /// 차선 거리와 맞닿은 두 샘플 중 앞쪽 샘플의 전역 배열 인덱스를 찾는다.
    /// </summary>
    /// <param name="laneId">조회할 차선의 인덱스이다.</param>
    /// <param name="laneS">차선 시작점부터의 거리이다. 단위는 m이다.</param>
    /// <param name="sampleHint">이전 조회에서 반환된 전역 샘플 인덱스이다.</param>
    /// <returns>보간 구간의 첫 샘플 인덱스이며, 데이터가 유효하지 않으면 -1이다.</returns>
    /// <remarks>
    /// 연속 프레임에서는 이전 반환값을 <paramref name="sampleHint"/>로 전달하여
    /// 전체 샘플을 다시 탐색하지 않는다.
    /// </remarks>
    public int FindSampleIndex(
        int laneId,
        float laneS,
        int sampleHint)
    {
        if (!IsLaneIndexValid(laneId) ||
            laneSampleStarts == null ||
            laneSampleCounts == null ||
            laneLengths == null ||
            sampleDistances == null)
        {
            return -1;
        }

        int first = laneSampleStarts[laneId];
        int count = laneSampleCounts[laneId];
        int last = first + count - 1;

        if (count < 2 ||
            first < 0 ||
            last >= sampleDistances.Length)
        {
            return -1;
        }

        float clampedS = Mathf.Clamp(
            laneS,
            0f,
            laneLengths[laneId]
        );

        int index = sampleHint;

        if (index < first || index >= last)
        {
            int estimatedOffset = Mathf.FloorToInt(
                clampedS / sampleSpacing
            );

            index = Mathf.Clamp(
                first + estimatedOffset,
                first,
                last - 1
            );
        }

        while (index > first &&
               sampleDistances[index] > clampedS)
        {
            index--;
        }

        while (index < last - 1 &&
               sampleDistances[index + 1] < clampedS)
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// 베이크된 인접 샘플을 보간하여 차선의 월드 위치를 구한다.
    /// </summary>
    /// <param name="laneId">조회할 차선의 인덱스이다.</param>
    /// <param name="laneS">차선 시작점부터의 거리이다. 단위는 m이다.</param>
    /// <param name="sampleHint">이전 프레임에 사용한 전역 샘플 인덱스이다.</param>
    /// <returns>보간된 월드 위치이며, 데이터가 유효하지 않으면 <see cref="Vector3.zero"/>이다.</returns>
    public Vector3 GetLanePosition(
        int laneId,
        float laneS,
        int sampleHint)
    {
        int sampleIndex = FindSampleIndex(
            laneId,
            laneS,
            sampleHint
        );

        if (sampleIndex < 0)
        {
            return Vector3.zero;
        }

        int last = laneSampleStarts[laneId] +
                   laneSampleCounts[laneId] - 1;

        int nextIndex = Mathf.Min(sampleIndex + 1, last);
        float t = GetSegmentInterpolation(
            laneId,
            laneS,
            sampleIndex,
            nextIndex
        );

        return Vector3.Lerp(
            samplePositions[sampleIndex],
            samplePositions[nextIndex],
            t
        );
    }

    /// <summary>
    /// 베이크된 인접 샘플을 보간하여 차선의 월드 회전을 구한다.
    /// </summary>
    /// <param name="laneId">조회할 차선의 인덱스이다.</param>
    /// <param name="laneS">차선 시작점부터의 거리이다. 단위는 m이다.</param>
    /// <param name="sampleHint">이전 프레임에 사용한 전역 샘플 인덱스이다.</param>
    /// <returns>보간된 월드 회전이며, 데이터가 유효하지 않으면 단위 회전이다.</returns>
    public Quaternion GetLaneRotation(
        int laneId,
        float laneS,
        int sampleHint)
    {
        int sampleIndex = FindSampleIndex(
            laneId,
            laneS,
            sampleHint
        );

        if (sampleIndex < 0)
        {
            return Quaternion.identity;
        }

        int last = laneSampleStarts[laneId] +
                   laneSampleCounts[laneId] - 1;

        int nextIndex = Mathf.Min(sampleIndex + 1, last);
        float t = GetSegmentInterpolation(
            laneId,
            laneS,
            sampleIndex,
            nextIndex
        );

        return Quaternion.Slerp(
            sampleRotations[sampleIndex],
            sampleRotations[nextIndex],
            t
        );
    }

    /// <summary>
    /// 지정한 차량 유형이 현재 차선 거리에서 차선 변경 규칙을 사용할 수 있는지 확인한다.
    /// </summary>
    /// <param name="ruleIndex">베이크된 차선 변경 규칙의 인덱스이다.</param>
    /// <param name="laneS">출발 차선 시작점부터의 거리이다. 단위는 m이다.</param>
    /// <param name="vehicleMask">차량 유형을 나타내는 비트 마스크이다.</param>
    /// <returns>거리 범위와 차량 마스크가 모두 일치하면 <c>true</c>이다.</returns>
    public bool IsChangeAllowed(
        int ruleIndex,
        float laneS,
        int vehicleMask)
    {
        if (changeToLaneIds == null ||
            changeStartS == null ||
            changeEndS == null ||
            changeVehicleMasks == null ||
            ruleIndex < 0 ||
            ruleIndex >= changeToLaneIds.Length)
        {
            return false;
        }

        if ((changeVehicleMasks[ruleIndex] & vehicleMask) == 0)
        {
            return false;
        }

        return laneS >= changeStartS[ruleIndex] &&
               laneS <= changeEndS[ruleIndex];
    }

    private float GetSegmentInterpolation(
        int laneId,
        float laneS,
        int sampleIndex,
        int nextIndex)
    {
        float startS = sampleDistances[sampleIndex];
        float endS = sampleDistances[nextIndex];
        float segmentLength = endS - startS;

        if (segmentLength <= 0.0001f)
        {
            return 0f;
        }

        float clampedS = Mathf.Clamp(
            laneS,
            0f,
            laneLengths[laneId]
        );

        return Mathf.Clamp01(
            (clampedS - startS) / segmentLength
        );
    }

    private bool IsLaneIndexValid(int laneId)
    {
        return laneId >= 0 && laneId < laneCount;
    }
}
