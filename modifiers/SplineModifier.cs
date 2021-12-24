using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;

namespace MeshBuilder
{
    using ScaleValue = SplineModifier.ValueAtDistance<float2>;
    using RotationValue = SplineModifier.ValueAtDistance<float>;

    public class SplineModifier : Modifier
    {
        private const int DefGenerateTransformsInnerBatchCount = 512;
        private const int DefApplyTransformsInnerBatchCount = 512;

        public int GenerateTransformsInnerBatchCount { get; set; } = DefGenerateTransformsInnerBatchCount;
        public int ApplyTransformsInnerBatchCount { get; set; } = DefApplyTransformsInnerBatchCount;

        public SplineCache SplineCache { get; set; }
        
        public float TransformStepDistance { get; set; }

        public LerpValue[] LerpValues { get; set; } = null;

        public ScaleValue[] ScaleValues { get; set; } = null;
        public RotationValue[] RotationValues { get; set; } = null;

        public void Init(SplineCache splineCache, float transformStepDistance, LerpValue[] lerpValues = null)
        {
            SplineCache = splineCache;
            TransformStepDistance = transformStepDistance;
            LerpValues = lerpValues;

            Inited();
        }

        protected override JobHandle StartGeneration(MeshData meshData, JobHandle dependOn)
        {
            int rowNum = Mathf.CeilToInt(SplineCache.Distance / TransformStepDistance);

            var transforms = new NativeArray<PointTransform>(rowNum, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(transforms);

            if (ScaleValues != null)
            {
                FillScales(transforms, TransformStepDistance, ScaleValues);
            }
            else
            {
                float2 one = new float2(1, 1);
                for (int i = 0; i < transforms.Length; ++i)
                {
                    transforms[i] = new PointTransform { scale = one };
                }
            }

            if (RotationValues != null)
            {
                var rotations = CalcRotations(rowNum, TransformStepDistance, RotationValues);
                AddTemp(rotations);
                dependOn = GenerateRotatedTransforms.Schedule(SplineCache, transforms, rotations, TransformStepDistance, GenerateTransformsInnerBatchCount, dependOn);
            }
            else
            {
                dependOn = GenerateTransforms.Schedule(SplineCache, transforms, TransformStepDistance, GenerateTransformsInnerBatchCount, dependOn);
            }

            if (LerpValues == null)
            {
                dependOn = ApplyTransforms.Schedule(meshData.Vertices, transforms, TransformStepDistance, ApplyTransformsInnerBatchCount, dependOn);
            }
            else
            {
                if (LerpValues.Length <= ApplyLerpedTransformsLimited.MaxLerpValueCount)
                {
                    dependOn = ApplyLerpedTransformsLimited.Schedule(meshData.Vertices, transforms, LerpValues, TransformStepDistance, ApplyTransformsInnerBatchCount, dependOn);
                }
                else
                {
                    var lerpArray = new NativeArray<LerpValue>(LerpValues, Allocator.TempJob);
                    AddTemp(lerpArray);

                    dependOn = ApplyLerpedTransforms.Schedule(meshData.Vertices, transforms, lerpArray, TransformStepDistance, ApplyTransformsInnerBatchCount, dependOn);
                }
            }
            
            return dependOn;
        }

        protected override void EndGeneration()
        {
            
        }

        private NativeArray<float> CalcRotations(int rowNum, float stepDistance, RotationValue[] rotations)
        {
            NativeArray<float> res = new NativeArray<float>(rowNum, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < res.Length; ++i)
            {
                res[i] = math.radians(GetValueAtDistance(i * stepDistance, rotations));
            }

            return res;
        }

        private void FillScales(NativeArray<PointTransform> transforms, float stepDistance, ScaleValue[] scales)
        {
            for (int i = 0; i < transforms.Length; ++i)
            {
                var transform = transforms[i];
                transform.scale = GetValueAtDistance(i * stepDistance, scales);
                transforms[i] = transform;
            }
        }

        private struct PointTransform
        {
            public float3 pos;
            public float3 up;
            public float3 right;
            public float2 scale;
        }

        public struct LerpValue
        {
            public float sampleDistance;
            public float scale;
            public LerpValue(float sampleDistance, float scale) 
            {
                this.sampleDistance = sampleDistance;
                this.scale = scale;
            }
        }

        [System.Serializable]
        public struct ValueAtDistance<T> where T : struct
        {
            public float Distance;
            public T Value;
            public ValueAtDistance(float distance, T value)
            {
                Distance = distance;
                Value = value;
            }
        }

        public delegate T LerpFn<T>(T a, T b, float t);

        public static T LerpValueAtDistance<T>(float distance, ValueAtDistance<T> a, ValueAtDistance<T> b, LerpFn<T> lerp)
            where T : struct
        {
            float max = b.Distance - a.Distance;
            if (max > 0)
            {
                float t = (distance - a.Distance) / max;
                if (t > 0 && t < 1)
                {
                    return lerp(a.Value, b.Value, t);
                }
                else
                {
                    return t <= 0 ? a.Value : b.Value;
                }

            }
            return a.Value;
        }

        public static float2 LerpValueAtDistance(float distance, ScaleValue a, ScaleValue b)
            => LerpValueAtDistance(distance, a, b, math.lerp);

        public static float LerpValueAtDistance(float distance, RotationValue a, RotationValue b)
            => LerpValueAtDistance(distance, a, b, math.lerp);

        public static T GetValueAtDistance<T>(float distance, ValueAtDistance<T>[] values, LerpFn<T> lerp, T defMin, T defMax)
            where T : struct
        {
            if (distance <= values[0].Distance)
            {
                return defMin;
            }
            else if (distance >= values[values.Length - 1].Distance)
            {
                return defMax;
            }
            else
            {
                for (int i = 1; i < values.Length; ++i)
                {
                    if (values[i].Distance > distance)
                    {
                        return LerpValueAtDistance(distance, values[i - 1], values[i], lerp);
                    }
                }
            }

            return defMax;
        }

        public static float GetValueAtDistance(float distance, RotationValue[] values, float defMin, float defMax)
            => GetValueAtDistance(distance, values, math.lerp, defMin, defMax);

        public static float2 GetValueAtDistance(float distance, ScaleValue[] values, float2 defMin, float2 defMax)
            => GetValueAtDistance(distance, values, math.lerp, defMin, defMax);

        public static T GetValueAtDistance<T>(float distance, ValueAtDistance<T>[] values, LerpFn<T> lerp)
            where T : struct
            => GetValueAtDistance(distance, values, lerp, values[0].Value, values[values.Length - 1].Value);

        public static float GetValueAtDistance(float distance, RotationValue[] values)
            => GetValueAtDistance(distance, values, math.lerp);

        public static float2 GetValueAtDistance(float distance, ScaleValue[] values)
            => GetValueAtDistance(distance, values, math.lerp);

        [BurstCompile]
        private struct GenerateTransforms : IJobParallelFor
        {
            public float cacheStepDistance;
            public float transfromStepDistance;

            [ReadOnly] public NativeArray<float3> splineCachePositions;
            public NativeArray<PointTransform> transforms;

            public void Execute(int i)
            {
                float distance = i * transfromStepDistance;

                int cacheIndex = math.min(Mathf.FloorToInt(distance / cacheStepDistance), splineCachePositions.Length - 2);
                float3 a = splineCachePositions[cacheIndex];
                float3 b = splineCachePositions[cacheIndex + 1];
                float t = CalcCacheT(distance, cacheIndex);

                float3 pos = math.lerp(a, b, t);
                float3 forward = math.normalize(b - a);
                float3 right = math.cross(new float3(0, 1, 0), forward);
                float3 up = math.cross(forward, right);

                var transform = transforms[i];
                transform.pos = pos;
                transform.right = right;
                transform.up = up;
                transforms[i] = transform;
            }

            private float CalcCacheT(float distance, int index)
            {
                float start = index * cacheStepDistance;
                return (distance - start) / cacheStepDistance;
            }

            public static JobHandle Schedule(SplineCache spline, NativeArray<PointTransform> transforms, float transfromStepDistance, int innerBatchCount, JobHandle dependOn)
            {
                var generateTransforms = new GenerateTransforms
                {
                    cacheStepDistance = spline.CacheStepDistance,
                    transfromStepDistance = transfromStepDistance,
                    splineCachePositions = spline.Positions,
                    transforms = transforms
                };
                return generateTransforms.Schedule(transforms.Length, innerBatchCount, dependOn);
            }
        }

        [BurstCompile]
        private struct GenerateRotatedTransforms : IJobParallelFor
        {
            public float cacheStepDistance;
            public float transfromStepDistance;

            [ReadOnly] public NativeArray<float> rotationValues;

            [ReadOnly] public NativeArray<float3> splineCachePositions;
            public NativeArray<PointTransform> transforms;

            public void Execute(int i)
            {
                float distance = i * transfromStepDistance;

                int cacheIndex = math.min(Mathf.FloorToInt(distance / cacheStepDistance), splineCachePositions.Length - 2);
                float3 a = splineCachePositions[cacheIndex];
                float3 b = splineCachePositions[cacheIndex + 1];
                float t = CalcCacheT(distance, cacheIndex);

                float3 pos = math.lerp(a, b, t);
                float3 forward = math.normalize(b - a);
                float3 right = math.cross(new float3(0, 1, 0), forward);

                float angle = rotationValues[i];
                right = math.mul(quaternion.AxisAngle(forward, angle), right);

                var transform = transforms[i];
                transform.pos = pos;
                transform.right = right;
                transform.up = math.cross(forward, right);
                transforms[i] = transform;
            }

            private float CalcCacheT(float distance, int index)
            {
                float start = index * cacheStepDistance;
                return (distance - start) / cacheStepDistance;
            }

            public static JobHandle Schedule(SplineCache spline, NativeArray<PointTransform> transforms, NativeArray<float> rotations, float transfromStepDistance, int innerBatchCount, JobHandle dependOn)
            {
                var generateTransforms = new GenerateRotatedTransforms
                {
                    cacheStepDistance = spline.CacheStepDistance,
                    transfromStepDistance = transfromStepDistance,
                    rotationValues = rotations,
                    splineCachePositions = spline.Positions,
                    transforms = transforms
                };
                return generateTransforms.Schedule(transforms.Length, innerBatchCount, dependOn);
            }
        }

        [BurstCompile]
        private struct ApplyTransforms : IJobParallelFor
        {
            public float transfromStepDistance;

            [ReadOnly] public NativeArray<PointTransform> transforms;
            public NativeArray<float3> vertices;
  
            public void Execute(int i)
            {
                float3 v = vertices[i];

                int index = GetTransformIndex(v.z, transforms, transfromStepDistance);
                // vertices further than the spline distance are get transformed by the last segment (length-2, length-1)
                index = math.min(index, transforms.Length - 2);
                int nextIndex = index + 1;

                var cur = transforms[index];
                var next = transforms[nextIndex];

                float remaining = v.z - (index * transfromStepDistance);
                // t can be more than 1 at the and so vertices that are further than the spline distance
                // won't be placed at the same position
                float t = remaining / transfromStepDistance;

                float scaleX = cur.scale.x;
                float scaleY = cur.scale.y;
                vertices[i] = math.lerp(cur.pos, next.pos, t) + cur.right * (v.x * scaleX) + cur.up * (v.y * scaleY);
            }

            public static JobHandle Schedule(NativeArray<float3> vertices, NativeArray<PointTransform> transforms, float transfromStepDistance, int innerBatchCount, JobHandle dependOn)
            {
                var applyTransforms = new ApplyTransforms
                {
                    transfromStepDistance = transfromStepDistance,
                    transforms = transforms,
                    vertices = vertices
                };
                return applyTransforms.Schedule(vertices.Length, innerBatchCount, dependOn);
            }
        }

        [BurstCompile]
        private struct ApplyLerpedTransforms : IJobParallelFor
        {
            public float transfromStepDistance;

            [ReadOnly] public NativeArray<LerpValue> lerpValues;

            [ReadOnly] public NativeArray<PointTransform> transforms;
            public NativeArray<float3> vertices;

            public void Execute(int i)
            {
                float3 v = vertices[i];

                float distance = v.z;
                int index = GetTransformIndex(distance, transforms, transfromStepDistance);
                // vertices further than the spline distance are get transformed by the last segment (length-2, length-1)
                index = math.min(index, transforms.Length - 2);
                int nextIndex = index + 1;

                var cur = transforms[index];
                var next = transforms[nextIndex];

                float weight = 1f;
                float3 right = cur.right;
                float3 up = cur.up;

                for (int j = 0; j < lerpValues.Length; ++j)
                {
                    var sv = lerpValues[j];
                    var other = GetTransform(distance + sv.sampleDistance);
                    right += other.right * sv.scale;
                    up += other.up * sv.scale;
                    weight += sv.scale;
                }

                right /= weight;
                right = math.normalize(right);

                up /= weight;
                up = math.normalize(up);

                float remaining = v.z - (index * transfromStepDistance);
                float t = remaining / transfromStepDistance;

                float scaleX = cur.scale.x;
                float scaleY = cur.scale.y;
                vertices[i] = math.lerp(cur.pos, next.pos, t) + right * (v.x * scaleX) + up * (v.y * scaleY);
            }
            
            private PointTransform GetTransform(float distance)
            {
                int index = GetTransformIndex(distance, transforms, transfromStepDistance);
                return transforms[index];
            }
            
            public static JobHandle Schedule(NativeArray<float3> vertices, NativeArray<PointTransform> transforms, NativeArray<LerpValue> lerpedValues, float transfromStepDistance, int innerBatchCount, JobHandle dependOn)
            {
                var applyTransforms = new ApplyLerpedTransforms
                {
                    transfromStepDistance = transfromStepDistance,
                    lerpValues = lerpedValues,
                    transforms = transforms,
                    vertices = vertices,
                };
                return applyTransforms.Schedule(vertices.Length, innerBatchCount, dependOn);
            }
        }
        
        // usually there isn't any need for a lot of lerping value, in that case there is a simplified job without a loop
        [BurstCompile]
        private struct ApplyLerpedTransformsLimited : IJobParallelFor
        {
            public const int MaxLerpValueCount = 4;

            public float transfromStepDistance;

            public LerpValue lerpValue0;
            public LerpValue lerpValue1;
            public LerpValue lerpValue2;
            public LerpValue lerpValue3;

            [ReadOnly] public NativeArray<PointTransform> transforms;
            public NativeArray<float3> vertices;

            public void Execute(int i)
            {
                float3 v = vertices[i];

                float distance = v.z;
                int index = GetTransformIndex(distance, transforms, transfromStepDistance);
                // vertices further than the spline distance are get transformed by the last segment (length-2, length-1)
                index = math.min(index, transforms.Length - 2);
                int nextIndex = index + 1;

                var cur = transforms[index];
                var next = transforms[nextIndex];

                float weight = 1f;
                float3 right = cur.right;
                float3 up = cur.up;

                AddLerpValue(distance, lerpValue0, ref weight, ref right, ref up);
                AddLerpValue(distance, lerpValue1, ref weight, ref right, ref up);
                AddLerpValue(distance, lerpValue2, ref weight, ref right, ref up);
                AddLerpValue(distance, lerpValue3, ref weight, ref right, ref up);

                right /= weight;
                right = math.normalize(right);

                up /= weight;
                up = math.normalize(up);

                float remaining = v.z - (index * transfromStepDistance);
                float t = remaining / transfromStepDistance;

                float scaleX = cur.scale.x;
                float scaleY = cur.scale.y;
                vertices[i] = math.lerp(cur.pos, next.pos, t) + right * (v.x * scaleX) + up * (v.y * scaleY);
            }

            private void AddLerpValue(float distance, LerpValue lerpValue, ref float weight, ref float3 right, ref float3 up)
            {
                var other = GetTransform(distance + lerpValue.sampleDistance);
                right += other.right * lerpValue.scale;
                up += other.up * lerpValue.scale;
                weight += lerpValue.scale;
            }

            private PointTransform GetTransform(float distance)
            {
                int index = GetTransformIndex(distance, transforms, transfromStepDistance);
                return transforms[index];
            }

            public static JobHandle Schedule(NativeArray<float3> vertices, NativeArray<PointTransform> transforms, LerpValue[] lerpValues, float transfromStepDistance, int innerBatchCount, JobHandle dependOn)
            {
                if (lerpValues.Length > 4)
                {
                    Debug.LogWarning("ApplyLerpedTransformsLimited can't handle more than four values!");
                }

                var applyTransforms = new ApplyLerpedTransformsLimited
                {
                    transfromStepDistance = transfromStepDistance,
                    lerpValue0 = GetOrDefault(0, lerpValues),
                    lerpValue1 = GetOrDefault(1, lerpValues),
                    lerpValue2 = GetOrDefault(2, lerpValues),
                    lerpValue3 = GetOrDefault(3, lerpValues),
                    transforms = transforms,
                    vertices = vertices,
                };
                return applyTransforms.Schedule(vertices.Length, innerBatchCount, dependOn);
            }

            private static LerpValue GetOrDefault(int index, LerpValue[] array)
                => index < array.Length ? array[index] : new LerpValue { sampleDistance = 0, scale = 0 };
        }
        
        static private int GetTransformIndex<T>(float distance, NativeArray<T> transforms, float transformStep) where T : struct
            => math.clamp((int)math.floor(distance / transformStep), 0, transforms.Length - 1);
    }
}
