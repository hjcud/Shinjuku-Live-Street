using UdonSharp;
using UnityEngine;

/// <summary>
/// 베이크된 차선 샘플과 차선 변경 규칙을 런타임 교통 시뮬레이션에 제공
/// </summary>
/// <remarks>
/// 배열의 인덱스와 길이는 베이커에서 함께 생성, 기존 데이터는 초기화 시 높이 보정 후 사용
/// 모든 거리는 차선 시작점 기준의 m 단위
/// </remarks>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class TrafficLaneDatabase : UdonSharpBehaviour
{
    public const int FixedLaneCount = 7;
    public const float RoadSurfaceClearance = 0.01f;

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
    // 좌표에 이미 반영한 노면 여유 높이 기록으로 재초기화 시 중복 상승 방지
    [HideInInspector] public float bakedRoadSurfaceClearance;

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
    /// 교통 관리자에서 사용할 베이크 데이터의 배열 구조 검증
    /// </summary>
    /// <returns>필수 배열과 차선별 데이터 길이가 유효하면 <c>true</c> 반환</returns>
    /// <remarks>매 프레임 호출 없이 관리자 초기화 시 한 번만 호출</remarks>
    public bool IsReady()
    {
        if (laneCount != FixedLaneCount ||
            !IsFinite(sampleSpacing) || sampleSpacing <= 0f)
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

        if (changeStartS.Length != ruleCount ||
            changeEndS.Length != ruleCount ||
            changeVehicleMasks.Length != ruleCount) return false;

        // 배열 길이가 같아도 내부 구간이 잘못되면 런타임 조회가 범위를 벗어난다.
        // 초기화/베이크 시 한 번만 검사하고 정상 프레임의 조회 비용은 유지한다.
        for (int lane = 0; lane < laneCount; lane++)
        {
            int first = laneSampleStarts[lane];
            int count = laneSampleCounts[lane];
            if (first < 0 || first > sampleCount || count < 2 ||
                count > sampleCount - first) return false;

            float length = laneLengths[lane];
            if (!IsFinite(length) || length <= 0f ||
                !IsFinite(spawnS[lane]) || !IsFinite(despawnS[lane]) ||
                !IsFinite(stopLineS[lane]) || !IsFinite(speedLimits[lane]) ||
                !IsFinite(spawnWeights[lane])) return false;
            if (despawnS[lane] < 0f || despawnS[lane] > length ||
                (spawnS[lane] != -1f && (spawnS[lane] < 0f || spawnS[lane] > despawnS[lane])) ||
                (stopLineS[lane] != -1f && (stopLineS[lane] < 0f || stopLineS[lane] > length)) ||
                speedLimits[lane] <= 0f || spawnWeights[lane] < 0f) return false;

            float previous = -1f;
            for (int sample = first; sample < first + count; sample++)
            {
                float distance = sampleDistances[sample];
                if (!IsFinite(distance) || distance < 0f ||
                    distance <= previous || distance > length + 0.01f) return false;
                Vector3 position = samplePositions[sample];
                if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z)) return false;
                previous = distance;
            }

            int ruleFirst = laneRuleStarts[lane];
            int rules = laneRuleCounts[lane];
            if (ruleFirst < 0 || ruleFirst > ruleCount || rules < 0 ||
                rules > ruleCount - ruleFirst) return false;
            for (int rule = ruleFirst; rule < ruleFirst + rules; rule++)
            {
                int targetLane = changeToLaneIds[rule];
                float start = changeStartS[rule];
                float end = changeEndS[rule];
                if (targetLane < 0 || targetLane >= laneCount || targetLane == lane ||
                    !IsFinite(start) || !IsFinite(end) ||
                    start < 0f || end < start || end > length) return false;
            }
        }
        return true;
    }

    private bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// 기존 베이크 좌표에 노면 위 1cm 여유를 한 번만 적용하여 그림자와 도로의 겹침 완화
    /// </summary>
    public void ApplyRoadSurfaceClearance()
    {
        if (samplePositions == null || samplePositions.Length == 0) return;

        float adjustment = RoadSurfaceClearance - bakedRoadSurfaceClearance;
        if (adjustment == 0f) return;

        // 월드 Y축으로 동일하게 이동하여 차선 간격, 거리 및 회전 유지
        Vector3 offset = Vector3.up * adjustment;
        for (int i = 0; i < samplePositions.Length; i++)
            samplePositions[i] += offset;

        bakedRoadSurfaceClearance = RoadSurfaceClearance;
    }

    /// <summary>
    /// 차선 거리와 맞닿은 두 샘플 중 앞쪽 샘플의 전역 배열 인덱스 조회
    /// </summary>
    /// <param name="laneId">조회할 차선의 인덱스</param>
    /// <param name="laneS">차선 시작점부터의 거리(m)</param>
    /// <param name="sampleHint">이전 조회에서 반환된 전역 샘플 인덱스</param>
    /// <returns>보간 구간의 첫 샘플 인덱스 반환. 데이터가 유효하지 않으면 -1 반환</returns>
    /// <remarks>
    /// 연속 프레임에서는 이전 반환값을 <paramref name="sampleHint"/>로 전달해 전체 샘플 재탐색 방지
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
    /// 베이크된 인접 샘플 보간을 통한 차선의 월드 위치 계산
    /// </summary>
    /// <param name="laneId">조회할 차선의 인덱스</param>
    /// <param name="laneS">차선 시작점부터의 거리(m)</param>
    /// <param name="sampleHint">이전 프레임에 사용한 전역 샘플 인덱스</param>
    /// <returns>보간된 월드 위치 반환. 데이터가 유효하지 않으면 <see cref="Vector3.zero"/> 반환</returns>
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
    /// 베이크된 인접 샘플 보간을 통한 차선의 월드 회전 계산
    /// </summary>
    /// <param name="laneId">조회할 차선의 인덱스</param>
    /// <param name="laneS">차선 시작점부터의 거리(m)</param>
    /// <param name="sampleHint">이전 프레임에 사용한 전역 샘플 인덱스</param>
    /// <returns>보간된 월드 회전 반환. 데이터가 유효하지 않으면 단위 회전 반환</returns>
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
    /// 지정한 차량 유형의 현재 차선 거리에서 차선 변경 규칙 사용 가능 여부 확인
    /// </summary>
    /// <param name="ruleIndex">베이크된 차선 변경 규칙의 인덱스</param>
    /// <param name="laneS">출발 차선 시작점부터의 거리(m)</param>
    /// <param name="vehicleMask">차량 유형을 나타내는 비트 마스크</param>
    /// <returns>거리 범위와 차량 마스크가 모두 일치하면 <c>true</c> 반환</returns>
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
