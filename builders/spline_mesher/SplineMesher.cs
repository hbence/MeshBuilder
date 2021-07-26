using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MeshBuilder
{
    public class SplineMesher : Builder
    {
        private float3 meshVertexOffset;
        private SplineCache splineCache;

        private int colNum;
        private int rowNum;
        private float cellWidth;
        private float cellHeight;

        private MeshData meshData;

        public void Init(SplineCache splineCache, int cellColCount, float meshCellWidth, float meshCellLength, float3 positionOffset = default)
        {
            this.splineCache = splineCache;

            colNum = cellColCount;
            rowNum = Mathf.CeilToInt(splineCache.Distance / meshCellLength) + 1;
            cellWidth = meshCellWidth;
            cellHeight = meshCellLength;
            meshVertexOffset = positionOffset;

            Inited();
        }

        protected override JobHandle StartGeneration(JobHandle dependOn)
        {
            int vertexCount = (colNum + 1) * (rowNum + 1);
            int indexCount = (colNum * rowNum) * 2 * 3;

            var transforms = new NativeArray<RigidTransform>(rowNum, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(transforms);

            meshData = new MeshData(vertexCount, indexCount, Allocator.TempJob, (uint)(MeshData.Buffer.Vertex | MeshData.Buffer.Triangle));
            AddTemp(meshData);

            dependOn = GenerateGrid.Schedule(colNum, rowNum, cellWidth, cellHeight, meshData, dependOn);

            dependOn = GenerateTransforms.Schedule(splineCache, transforms, cellHeight, 512, dependOn);

            var lerpedTransforms = new NativeArray<RigidTransform>(rowNum, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(lerpedTransforms);

            /*
            dependOn = ApplyTransforms.Schedule(meshData.Vertices, transforms, cellHeight, dependOn);
            */

            ///////////

            var lerps = new LerpValue[] 
            {
                new LerpValue { distance = -2, scale = 0.25f },
                new LerpValue { distance = -1, scale = 0.5f },
                new LerpValue { distance = 1, scale = 0.5f },
                new LerpValue { distance = 2, scale = 0.25f },
            };

            var lerps2 = new LerpValue[]
            {
                new LerpValue { distance = -2.5f, scale = 0.25f },
                new LerpValue { distance = -1f, scale = 0.5f },
                new LerpValue { distance = 1f, scale = 0.5f },
                new LerpValue { distance = 2.5f, scale = 0.25f },
            };

            
            var lerpArray = new NativeArray<LerpValue>(lerps2, Allocator.TempJob);
            AddTemp(lerpArray);

            dependOn = ApplyLerpedTransforms.Schedule(meshData.Vertices, transforms, lerpArray, 1f, cellHeight, 1024, dependOn);
            

            /*
            dependOn = ApplyLerpedTransformsLimited.Schedule(meshData.Vertices, transforms, lerps2, 1f, cellHeight, 1024, dependOn);
            */

            if (!meshVertexOffset.Equals(default))
            {
                dependOn = Utils.VertexOffsetJob.Schedule(meshData.Vertices, meshVertexOffset, 1024, dependOn);
            }
            
            return dependOn;
        }

        protected override void EndGeneration(Mesh mesh)
        {
            meshData.UpdateMesh(mesh);
        }

        private struct GenerateGrid : IJob
        {
            public int colNum;
            public int rowNum;
            public float cellWidth;
            public float cellLength;

            public NativeArray<float3> vertices;
            public NativeArray<int> indices;

            public void Execute()
            {
                int vertexColNum = colNum + 1;

                float3 offset = new float3(colNum * cellWidth * -0.5f, 0, 0);
                for (int row = 0; row <= rowNum; ++row)
                {
                    for (int col = 0; col <= colNum; ++col)
                    {
                        vertices[row * vertexColNum + col] = new float3(col * cellWidth, 0, row * cellLength) + offset;
                    }
                }

                for (int row = 0; row < rowNum; ++row)
                {
                    for (int col = 0; col < colNum; ++col)
                    {
                        SetIndices(col, row, vertexColNum);
                    }
                }
            }

            private void SetIndices(int col, int row, int vertexColNum)
            {
                int vertex = row * vertexColNum + col;
                int start = ((row * colNum) + col) * 6;

                indices[start] = vertex;
                indices[start + 1] = vertex + vertexColNum;
                indices[start + 2] = vertex + 1;

                indices[start + 3] = vertex + vertexColNum;
                indices[start + 4] = vertex + vertexColNum + 1;
                indices[start + 5] = vertex + 1;
            }

            public static JobHandle Schedule(int colNum, int rowNum, float cellWidth, float cellHeight, MeshData meshData, JobHandle dependOn)
            {
                var generateGrid = new GenerateGrid
                {
                    colNum = colNum,
                    rowNum = rowNum,
                    cellWidth = cellWidth,
                    cellLength = cellHeight,
                    vertices = meshData.Vertices,
                    indices = meshData.Triangles
                };
                return generateGrid.Schedule(dependOn);
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

        private struct LerpValue
        {
            public float distance;
            public float scale;
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
                => SplineMesher.GetTransform(distance, transforms, transfromStepDistance);
            
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
                    var other = GetTransform(distance + sv.distance);
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
                => SplineMesher.GetTransform(distance, transforms, transfromStepDistance);

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
                var other = GetTransform(distance + lerpValue.distance);
                float scale = lerpValue.scale * sideRatio;
                rot += other.rot.value * scale;
            }

            private RigidTransform GetTransform(float distance)
                => SplineMesher.GetTransform(distance, transforms, transfromStepDistance);

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
                => index < array.Length ? array[index] : new LerpValue { distance = 0, scale = 0 };
        }

        static private RigidTransform GetTransform(float distance, NativeArray<RigidTransform> transforms, float transformStep)
        {
            int index = Mathf.FloorToInt(distance / transformStep);
            index = math.clamp(index, 0, transforms.Length - 1);
            return transforms[index];
        }
    }
}
