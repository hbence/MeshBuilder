using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;

using static MeshBuilder.Utils;

namespace MeshBuilder
{
    // todo: replace the int3 hash with an int hash 
    // (is that large enough? perhaps it could choose variable type size based on bounding box size?)
    public class AlignNormals : Modifier
    {
        private const float MinCellSize = 0.001f;
        private const float CellCandidateRatio = 0.95f;

        private float cellSize = MinCellSize;

        // most of the time this is used for aligning the normals for a mesh generated from tiles
        // in those meshes only the edge normals have to be aligned, so if you set the skip cell info,
        // then the center vertices can be skipped
        // the skipCellStep is usually the original tile cell size, and the skipCellSize is the size 
        // where vertices will be skipped
        private bool useSkipCells = false;
        private float3 skipCellStep;
        private float3 skipCellSize;

        private NativeHashMap<int3, NormalCell> normalCells;

        private Mesh mesh;
        private MeshData tempMeshData;

        public void Init(float alignCellSize)
        {
            Init(alignCellSize, false, new float3(1, 1, 1), new float3(0, 0, 0));
        }

        public void Init(float alignCellSize, float3 skipCellStep, float3 skipCellSize)
        {
            Init(alignCellSize, true, skipCellStep, skipCellSize);
        }

        private void Init(float alignCellSize, bool useSkipCells, float3 skipCellStep, float3 skipCellSize)
        {
            cellSize = Mathf.Max(alignCellSize, MinCellSize);

            this.useSkipCells = useSkipCells;
            this.skipCellStep = skipCellStep;
            this.skipCellSize = skipCellSize;

            Inited();
        }

        public JobHandle Start(Mesh mesh, JobHandle dependOn = default)
        {
            this.mesh = mesh;

            tempMeshData = new MeshData(mesh, Allocator.TempJob, (int)MeshData.Buffer.Vertex | (int)MeshData.Buffer.Normal);
            AddTemp(tempMeshData);

            return Start(tempMeshData, dependOn);
        }

        protected override JobHandle StartGeneration(MeshData meshData, JobHandle dependOn)
        {
            normalCells = new NativeHashMap<int3, NormalCell>(tempMeshData.VerticesLength, Allocator.TempJob);
            AddTemp(normalCells);

            dependOn = ScheduleGenerateNewNormals(meshData, dependOn);
            dependOn = ScheduleUpdateNormals(meshData, dependOn);

            return dependOn;
        }

        protected override void EndGeneration()
        {
            if (mesh)
            {
                tempMeshData.UpdateMesh(mesh, MeshData.UpdateMode.DontClear, (uint)MeshData.Buffer.Normal);
            }
        }

        private JobHandle ScheduleGenerateNewNormals(MeshData meshData, JobHandle dependOn)
        {
            if (useSkipCells)
            {
                var generateNormals = new SkipVersion.GenerateNewNormals
                {
                    cellSize = cellSize,

                    skipCellStep = skipCellStep,
                    halfSkipCellSize = skipCellSize * 0.5f,

                    vertices = meshData.Vertices,
                    normals = meshData.Normals,
                    normalCells = normalCells
                };
                dependOn = generateNormals.Schedule(dependOn);
            }
            else
            {
                var generateNormals = new NoSkipVersion.GenerateNewNormals
                {
                    cellSize = cellSize,
                    vertices = meshData.Vertices,
                    normals = meshData.Normals,
                    normalCells = normalCells
                };
                dependOn = generateNormals.Schedule(dependOn);
            }
            return dependOn;
        }

        private JobHandle ScheduleUpdateNormals(MeshData meshData, JobHandle dependOn)
        {
            if (useSkipCells)
            {
                var updateNormals = new SkipVersion.UpdateNormals
                {
                    cellSize = cellSize,

                    skipCellStep = skipCellStep,
                    halfSkipCellSize = skipCellSize * 0.5f,

                    vertices = meshData.Vertices,
                    normals = meshData.Normals,
                    normalCells = normalCells
                };
                dependOn = updateNormals.Schedule(tempMeshData.VerticesLength, 128, dependOn);
            }
            else
            {
                var updateNormals = new NoSkipVersion.UpdateNormals
                {
                    cellSize = cellSize,
                    vertices = meshData.Vertices,
                    normals = meshData.Normals,
                    normalCells = normalCells
                };
                dependOn = updateNormals.Schedule(tempMeshData.VerticesLength, 128, dependOn);
            }
            return dependOn;
        }

        private class NoSkipVersion
        {
            [BurstCompile]
            internal struct GenerateNewNormals : IJob
            {
                public float cellSize;

                [ReadOnly] public NativeArray<float3> vertices;
                [ReadOnly] public NativeArray<float3> normals;

                public NativeHashMap<int3, NormalCell> normalCells;

                public void Execute()
                {
                    for (int i = 0; i < vertices.Length; ++i)
                    {
                        int3 hash = Hash(vertices[i], cellSize);
                        NormalCell item;
                        if (normalCells.TryGetValue(hash, out item))
                        {
                            item.AddNormal(normals[i]);
                        }
                        else
                        {
                            item = new NormalCell(normals[i]);
                        }

                        normalCells.Remove(hash);
                        normalCells.TryAdd(hash, item);
                    }
                }
            }

            [BurstCompile]
            internal struct UpdateNormals : IJobParallelFor
            {
                public float cellSize;
                [ReadOnly] public NativeArray<float3> vertices;
                [WriteOnly] public NativeArray<float3> normals;

                [ReadOnly] public NativeHashMap<int3, NormalCell> normalCells;

                public void Execute(int index)
                {
                    int3 hash = Hash(vertices[index], cellSize);
                    NormalCell item;
                    if (normalCells.TryGetValue(hash, out item))
                    {
                        if (item.Count > 1)
                        {
                            normals[index] = item.CalcAverage();
                        }
                    }
                }
            }
        }

        class SkipVersion
        {
            [BurstCompile]
            internal struct GenerateNewNormals : IJob
            {
                public float cellSize;

                public float3 skipCellStep;
                public float3 halfSkipCellSize;

                [ReadOnly] public NativeArray<float3> vertices;
                [ReadOnly] public NativeArray<float3> normals;

                public NativeHashMap<int3, NormalCell> normalCells;

                public void Execute()
                {
                    for (int i = 0; i < vertices.Length; ++i)
                    {
                        if (!ShouldSkip(vertices[i], skipCellStep, halfSkipCellSize))
                        {
                            int3 hash = Hash(vertices[i], cellSize);
                            NormalCell item;
                            if (normalCells.TryGetValue(hash, out item))
                            {
                                item.AddNormal(normals[i]);
                            }
                            else
                            {
                                item = new NormalCell(normals[i]);
                            }

                            normalCells.Remove(hash);
                            normalCells.TryAdd(hash, item);
                        }
                    }
                }
            }

            [BurstCompile]
            internal struct UpdateNormals : IJobParallelFor
            {
                public float cellSize;

                public float3 skipCellStep;
                public float3 halfSkipCellSize;

                [ReadOnly] public NativeArray<float3> vertices;
                [WriteOnly] public NativeArray<float3> normals;

                [ReadOnly] public NativeHashMap<int3, NormalCell> normalCells;

                public void Execute(int index)
                {
                    if (!ShouldSkip(vertices[index], skipCellStep, halfSkipCellSize))
                    {
                        int3 hash = Hash(vertices[index], cellSize);
                        NormalCell item;
                        if (normalCells.TryGetValue(hash, out item))
                        {
                            if (item.Count > 1)
                            {
                                normals[index] = item.CalcAverage();
                            }
                        }
                    }
                }
            }
        }

        static private int3 Hash(float3 v, float cellSize)
        {
            return new int3 { x = Mathf.FloorToInt(v.x / cellSize), y = Mathf.FloorToInt(v.y / cellSize), z = Mathf.FloorToInt(v.z / cellSize) };
        }

        static private bool ShouldSkip(float3 v, float3 skipCellStep, float3 halfSkipCellSize)
        {
            return
                math.abs(v.x - math.round(v.x / skipCellStep.x) * skipCellStep.x) < halfSkipCellSize.x &&
                math.abs(v.y - math.round(v.y / skipCellStep.y) * skipCellStep.y) < halfSkipCellSize.y &&
                math.abs(v.z - math.round(v.z / skipCellStep.z) * skipCellStep.z) < halfSkipCellSize.z;
        }

        private struct NormalCell
        {
            float3 normal;
            int count;

            public NormalCell(float3 normalValue)
            {
                normal = normalValue;
                count = 1;
            }

            public void AddNormal(float3 value)
            {
                normal += value;
                ++count;
            }

            public float3 CalcAverage()
            {
                return count <= 0 ? new float3(0, 0, 0) : normal / count;
            }

            public int Count { get { return count; } }
        }
    }
}
