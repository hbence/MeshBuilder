using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;

namespace MeshBuilder
{
    public class SplineModifier : Modifier
    {
        private const int DefGenerateTransformsInnerBatchCount = 512;
        private const int DefApplyTransformsInnerBatchCount = 512;

        public int GenerateTransformsInnerBatchCount { get; set; } = DefGenerateTransformsInnerBatchCount;
        public int ApplyTransformsInnerBatchCount { get; set; } = DefApplyTransformsInnerBatchCount;

        public SplineCache SplineCache { get; set; }
        
        public float TransformStepDistance { get; set; }

        public float MaxHalfWidth { get; set; }

        public LerpValue[] LerpValues { get; set; } = null;

        public void Init(SplineCache splineCache, float transformStepDistance, float maxHalfWidth = 1f, LerpValue[] lerpValues = null)
        {
            SplineCache = splineCache;
            TransformStepDistance = transformStepDistance;
            MaxHalfWidth = maxHalfWidth;
            LerpValues = lerpValues;

            Inited();
        }

        protected override JobHandle StartGeneration(MeshData meshData, JobHandle dependOn)
        {
            int rowNum = Mathf.CeilToInt(SplineCache.Distance / TransformStepDistance) + 1;

            var transforms = new NativeArray<RigidTransform>(rowNum, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(transforms);

            dependOn = GenerateTransforms.Schedule(SplineCache, transforms, TransformStepDistance, GenerateTransformsInnerBatchCount, dependOn);

            if (LerpValues == null)
            {
                dependOn = ApplyTransforms.Schedule(meshData.Vertices, transforms, TransformStepDistance, ApplyTransformsInnerBatchCount, dependOn);
            }
            else
            {
                if (LerpValues.Length <= ApplyLerpedTransformsLimited.MaxLerpValueCount)
                {
                    dependOn = ApplyLerpedTransformsLimited.Schedule(meshData.Vertices, transforms, LerpValues, MaxHalfWidth, TransformStepDistance, ApplyTransformsInnerBatchCount, dependOn);
                }
                else
                {
                    var lerpArray = new NativeArray<LerpValue>(LerpValues, Allocator.TempJob);
                    AddTemp(lerpArray);

                    dependOn = ApplyLerpedTransforms.Schedule(meshData.Vertices, transforms, lerpArray, MaxHalfWidth, TransformStepDistance, ApplyTransformsInnerBatchCount, dependOn);
                }
            }

            return dependOn;
        }

        protected override void EndGeneration()
        {
            
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


        [BurstCompile]
        private struct GenerateTransforms : IJobParallelFor
        {
            public float cacheStepDistance;
            public float transfromStepDistance;

            [ReadOnly] public NativeArray<float3> splineCachePositions;
            [WriteOnly] public NativeArray<RigidTransform> transforms;

            public void Execute(int i)
            {
                float distance = i * transfromStepDistance;

                int cacheIndex = math.min(Mathf.FloorToInt(distance / cacheStepDistance), splineCachePositions.Length - 2);
                float3 a = splineCachePositions[cacheIndex];
                float3 b = splineCachePositions[cacheIndex + 1];
                float t = CalcCacheT(distance, cacheIndex);

                float3 pos = math.lerp(a, b, t);
                float3 forward = math.normalize(b - a);
                float3 right = math.cross(forward, new float3(0, 1, 0));
                float3 up = -math.cross(forward, right);
                quaternion rot = quaternion.LookRotation(forward, up);
                transforms[i] = math.RigidTransform(rot, pos);
            }

            private float CalcCacheT(float distance, int index)
            {
                float start = index * cacheStepDistance;
                return (distance - start) / cacheStepDistance;
            }

            public static JobHandle Schedule(SplineCache spline, NativeArray<RigidTransform> transforms, float transfromStepDistance, int innerBatchCount, JobHandle dependOn)
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
        private struct ApplyTransforms : IJobParallelFor
        {
            public float transfromStepDistance;

            [ReadOnly] public NativeArray<RigidTransform> transforms;
            public NativeArray<float3> vertices;

            public void Execute(int i)
            {
                float3 v = vertices[i];
                var transform = GetTransform(v.z);
                v.z = 0;
                vertices[i] = math.transform(transform, v);
            }

            private RigidTransform GetTransform(float distance)
                => SplineModifier.GetTransform(distance, transforms, transfromStepDistance);

            public static JobHandle Schedule(NativeArray<float3> vertices, NativeArray<RigidTransform> transforms, float transfromStepDistance, int innerBatchCount, JobHandle dependOn)
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
            public float maxHalfWidth;

            [ReadOnly] public NativeArray<LerpValue> lerpValues;

            [ReadOnly] public NativeArray<RigidTransform> transforms;
            public NativeArray<float3> vertices;

            public void Execute(int i)
            {
                float3 v = vertices[i];

                float distance = v.z;
                var transform = GetTransform(distance);

                float sideRatio = math.abs(v.x) / maxHalfWidth;

                float weight = 1f;
                float4 rotValue = transform.rot.value;

                for (int j = 0; j < lerpValues.Length; ++j)
                {
                    var sv = lerpValues[j];
                    var other = GetTransform(distance + sv.sampleDistance);
                    float scale = sv.scale * sideRatio;
                    rotValue += other.rot.value * scale;
                    weight += sv.scale;
                }

                rotValue /= weight;
                transform.rot = math.normalize(math.quaternion(rotValue));

                v.z = 0;
                vertices[i] = math.transform(transform, v);
            }

            private RigidTransform GetTransform(float distance)
                => SplineModifier.GetTransform(distance, transforms, transfromStepDistance);

            public static JobHandle Schedule(NativeArray<float3> vertices, NativeArray<RigidTransform> transforms, NativeArray<LerpValue> lerpedValues, float maxHalfWidth, float transfromStepDistance, int innerBatchCount, JobHandle dependOn)
            {
                var applyTransforms = new ApplyLerpedTransforms
                {
                    transfromStepDistance = transfromStepDistance,
                    maxHalfWidth = maxHalfWidth,
                    lerpValues = lerpedValues,
                    transforms = transforms,
                    vertices = vertices,
                };
                return applyTransforms.Schedule(vertices.Length, innerBatchCount, dependOn);
            }
        }

        [BurstCompile]
        private struct ApplyLerpedTransformsLimited : IJobParallelFor
        {
            public const int MaxLerpValueCount = 4;

            public float transfromStepDistance;
            public float maxHalfWidth;

            public LerpValue lerpValue0;
            public LerpValue lerpValue1;
            public LerpValue lerpValue2;
            public LerpValue lerpValue3;

            [ReadOnly] public NativeArray<RigidTransform> transforms;
            public NativeArray<float3> vertices;

            public void Execute(int i)
            {
                float3 v = vertices[i];

                float distance = v.z;
                var transform = GetTransform(distance);

                float sideRatio = math.abs(v.x) / maxHalfWidth;

                float4 rotValue = transform.rot.value;

                AddLerpValue(distance, sideRatio, lerpValue0, ref rotValue);
                AddLerpValue(distance, sideRatio, lerpValue1, ref rotValue);
                AddLerpValue(distance, sideRatio, lerpValue2, ref rotValue);
                AddLerpValue(distance, sideRatio, lerpValue3, ref rotValue);

                float weight = 1f + lerpValue0.scale + lerpValue1.scale + lerpValue2.scale + lerpValue3.scale;
                rotValue /= weight;
                transform.rot = math.normalize(math.quaternion(rotValue));

                v.z = 0;
                vertices[i] = math.transform(transform, v);
            }

            private void AddLerpValue(float distance, float sideRatio, LerpValue lerpValue, ref float4 rot)
            {
                var other = GetTransform(distance + lerpValue.sampleDistance);
                float scale = lerpValue.scale * sideRatio;
                rot += other.rot.value * scale;
            }

            private RigidTransform GetTransform(float distance)
                => SplineModifier.GetTransform(distance, transforms, transfromStepDistance);

            public static JobHandle Schedule(NativeArray<float3> vertices, NativeArray<RigidTransform> transforms, LerpValue[] lerpValues, float maxHalfWidth, float transfromStepDistance, int innerBatchCount, JobHandle dependOn)
            {
                if (lerpValues.Length > 4)
                {
                    Debug.LogWarning("ApplyLerpedTransformsLimited can't handle more than four values!");
                }

                var applyTransforms = new ApplyLerpedTransformsLimited
                {
                    transfromStepDistance = transfromStepDistance,
                    maxHalfWidth = maxHalfWidth,
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

        static private RigidTransform GetTransform(float distance, NativeArray<RigidTransform> transforms, float transformStep)
        {
            int index = Mathf.FloorToInt(distance / transformStep);
            index = math.clamp(index, 0, transforms.Length - 1);
            return transforms[index];
        }
    }
}
