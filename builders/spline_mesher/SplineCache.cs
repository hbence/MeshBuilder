using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;
using System;

namespace MeshBuilder
{
    public struct SplineCache : IDisposable
    {
        public const int DefaultMaxPositionCount = 1000;
        private const int DefaultSegmentLookupCount = 20;

        private const float MinCacheStepDistance = 0.001f;

        public int MaxPositionCount { get; private set; }
        public int SegmentLookupCount { get; private set; }

        private float cacheStepDistance;
        public float CacheStepDistance
        {
            get => cacheStepDistance;
            private set
            {
                cacheStepDistance = math.max(MinCacheStepDistance, value);
                if (cacheStepDistance != value)
                {
                    Debug.LogWarning("cacheStepDistance was clamped:" + cacheStepDistance);
                }
            }
        }

        public Spline Spline { get; private set; }

        public float Distance { get; private set; }
        private NativeList<float3> positions;
        public NativeArray<float3> Positions => positions;

        public int CachedPositionCount => positions.Length;
        public float3 GetCachedPosition(int index) => positions[index];

        public SplineCache(Spline spline, float cacheStepDistance, int maxPositionCount = DefaultMaxPositionCount, int segmentLookupCount = DefaultSegmentLookupCount)
        {
            Spline = spline;
            positions = default;
            Distance = 0;
            MaxPositionCount = maxPositionCount;
            SegmentLookupCount = segmentLookupCount;
            this.cacheStepDistance = cacheStepDistance;
            CacheStepDistance = cacheStepDistance;
            Recalculate();
        }

        public void Recalculate()
        {
            if (!positions.IsCreated)
            {
                positions = new NativeList<float3>(Allocator.Persistent);
            }

            Recalculate(positions, Spline, ref cacheStepDistance, SegmentLookupCount, MaxPositionCount);
            Distance = (positions.Length - 1) * cacheStepDistance;
        }

        public void Recalculate(Spline spline, float cacheStepDistance)
        {
            Spline = spline;
            CacheStepDistance = cacheStepDistance;
            Recalculate();
        }

        public void Recalculate(Spline spline, float cacheStepDistance, int maxPositionCount, int segmentTestCount)
        {
            Spline = spline;
            CacheStepDistance = cacheStepDistance;
            MaxPositionCount = maxPositionCount;
            SegmentLookupCount = segmentTestCount;
            Recalculate();
        }

        public void Dispose()
        {
            Utils.SafeDispose(ref positions);
        }

        static void Recalculate(NativeList<float3> result, Spline spline, ref float stepDistance, int segmentLookupCount, int maxPositionCount)
        {
            // check the spline
            if (spline.ArcCalculator == null)
            {
                result.Clear();
                return;
            }

            if (spline.ControlPoints == null || spline.ControlPointsCount == 0)
            {
                result.Clear();
                return;
            }

            if (spline.ControlPointsCount == 1)
            {
                result.ResizeUninitialized(1);
                result[0] = spline.GetControlPoint(0);
                return;
            }

            // calculate cache positions
            var controlPoints = Utils.ToFloat3Array(spline.ControlPoints);
            var arc = spline.ArcCalculator;

            int segmentCount = controlPoints.Length - 1;
            segmentCount += spline.IsClosed ? 1 : 0;

            var cachePositions = new NativeArray<CachePosition>(segmentCount * segmentLookupCount + 1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            float tStep = 1f / segmentLookupCount;
            var segmentPositions = new NativeArray<float3>(segmentLookupCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            float distance = 0;
            float3 prev = controlPoints[0];
            for (int i = 0; i < segmentCount; ++i)
            {
                arc.Calculate(segmentPositions, controlPoints, i, (i + 1) % controlPoints.Length, 0f, tStep, spline.IsClosed);
                int offset = i * segmentLookupCount;
                for (int j = 0; j < segmentLookupCount; ++j)
                {
                    float3 pos = segmentPositions[j];
                    distance += math.distance(prev, pos);
                    cachePositions[offset + j] = new CachePosition() { distance = distance, position = pos };
                    prev = pos;
                }
            }
            
            float3 last = spline.IsClosed ? 
                arc.Calculate(controlPoints, controlPoints.Length - 1, 0, 1f, true) :
                arc.Calculate(controlPoints, controlPoints.Length - 2, controlPoints.Length - 1, 1f, false);

            distance += math.distance(cachePositions[cachePositions.Length - 2].position, last);
            cachePositions[cachePositions.Length - 1] = new CachePosition() { position = last, distance = distance };
            
            // calculate positions
            if (distance > 0)
            {
                int positionCount = Mathf.FloorToInt(distance / stepDistance) + 1;
                positionCount = Math.Min(positionCount, maxPositionCount);

                stepDistance = distance / (positionCount - 1);

                result.ResizeUninitialized(positionCount);

                float testDistance = 0;
                int cacheIndex = 1;
                for (int i = 0; i < result.Length; ++i)
                {
                    while (cacheIndex < cachePositions.Length - 1 && testDistance > cachePositions[cacheIndex].distance)
                    {
                        ++cacheIndex;
                    }

                    result[i] = Lerp(testDistance, cachePositions[cacheIndex - 1], cachePositions[cacheIndex]);
                    testDistance += stepDistance;
                }
            }
            else
            {
                result.Clear();
            }
            
            cachePositions.Dispose();
            segmentPositions.Dispose();
        }

        static private float3 Lerp(float dist, CachePosition a, CachePosition b)
            => math.lerp(a.position, b.position, (dist - a.distance) / (b.distance - a.distance));

        public float3 CalculatePosition(float dist)
        {
            int aIndex = Mathf.FloorToInt(dist / cacheStepDistance);
            aIndex = math.clamp(aIndex, 0, positions.Length - 1);
            int bIndex = math.min(aIndex + 1, positions.Length - 1);

            float t = (dist - (aIndex * cacheStepDistance)) / cacheStepDistance;
            return math.lerp(positions[aIndex], positions[bIndex], t);
        }

        public float3 CalculateForward(float dist)
        {
            int index = Mathf.FloorToInt(dist / cacheStepDistance);
            index = math.clamp(index, 0, positions.Length - 2);
            return positions[index + 1] - positions[index];
        }

        private struct CachePosition
        {
            public float3 position;
            public float distance;
        }
    }
}
