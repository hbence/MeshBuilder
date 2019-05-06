using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;

namespace MeshBuilder
{
    public class AlignNormals : IMeshBuilder
    {
        private const float MinCellSize = 0.001f;

        public Mesh Mesh { get; private set; }
        public bool IsGenerating { get; private set; }

        private float cellSize = MinCellSize;

        private NativeHashMap<int3, NormalCell> normalCells;
        private NativeArray<Vector3> vertices;
        private NativeArray<Vector3> normals;

        private JobHandle lastHandle;

        public void Init(Mesh mesh, float alignCellSize)
        {
            IsGenerating = false;
            Mesh = mesh;

            cellSize = Mathf.Max(alignCellSize, MinCellSize);
        }

        public void StartGeneration()
        {
            IsGenerating = true;

            vertices = new NativeArray<Vector3>(Mesh.vertices, Allocator.TempJob);
            normals = new NativeArray<Vector3>(Mesh.normals, Allocator.TempJob);
            normalCells = new NativeHashMap<int3, NormalCell>(Mesh.vertexCount, Allocator.TempJob);
            
            var generateNormals = new GenerateNewNormals
            {
                cellSize = cellSize,
                vertices = vertices,
                normals = normals,
                normalCells = normalCells
            };
            lastHandle = generateNormals.Schedule();

            var updateNormals = new UpdateNormals
            {
                cellSize = cellSize,
                vertices = vertices,
                normals = normals,
                normalCells = normalCells
            };
            lastHandle = updateNormals.Schedule(vertices.Length, 128, lastHandle);
        }

        public void EndGeneration()
        {
            IsGenerating = false;

            lastHandle.Complete();

            Mesh.normals = normals.ToArray();

            Dispose();
        }

        public void Dispose()
        {
            if (vertices.IsCreated) vertices.Dispose();
            if (normals.IsCreated) normals.Dispose();
            if (normalCells.IsCreated) normalCells.Dispose();
        }

        private struct GenerateNewNormals : IJob
        {
            public float cellSize;

            [ReadOnly] public NativeArray<Vector3> vertices;
            [ReadOnly] public NativeArray<Vector3> normals;

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

        private struct UpdateNormals : IJobParallelFor
        {
            public float cellSize;
            [ReadOnly] public NativeArray<Vector3> vertices;
            [WriteOnly] public NativeArray<Vector3> normals;

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

        static private int3 Hash(float3 v, float cellSize)
        {
            return new int3 { x = Mathf.FloorToInt(v.x / cellSize), y = Mathf.FloorToInt(v.y / cellSize), z = Mathf.FloorToInt(v.z / cellSize) };
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
