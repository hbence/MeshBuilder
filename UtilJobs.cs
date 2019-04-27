using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    [BurstCompile]
    public struct VertexOffsetJob : IJobParallelFor
    {
        public float3 offset;
        public NativeArray<float3> vertices;

        public void Execute(int index)
        {
            vertices[index] = vertices[index] + offset;
        }
    }

    [BurstCompile]
    public struct UVScaleJob : IJobParallelFor
    {
        public float2 scale;
        public NativeArray<float2> uvs;

        public void Execute(int index)
        {
            uvs[index] = uvs[index] * scale;
        }
    }
}
