using UdonSharp;
using UnityEngine;

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
    /// 차량 관리자 초기화 시 한 번만 호출합니다.
    /// 매 프레임 호출할 필요는 없습니다.
    /// </summary>
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
    /// 해당 s를 포함하는 첫 번째 샘플의 전역 배열 인덱스를 반환합니다.
    /// 이전 프레임의 반환값을 sampleHint로 다시 전달합니다.
    /// </summary>
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
