using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    public class Utils
    {
        static public void SafeDispose<T>(ref Volume<T> volume) where T : struct
        {
            if (volume != null)
            {
                volume.Dispose();
                volume = null;
            }
        }

        static public void SafeDispose<T>(ref NativeArray<T> collection) where T : struct
        {
            if (collection.IsCreated)
            {
                collection.Dispose();
                collection = default;
            }
        }

        static public void SafeDispose<T>(ref NativeList<T> collection) where T : struct
        {
            if (collection.IsCreated)
            {
                collection.Dispose();
                collection = default;
            }
        }

        static public void SafeDispose<TKey, TValue>(ref NativeHashMap<TKey, TValue> collection) 
            where TKey : struct, System.IEquatable<TKey>
            where TValue : struct
        {
            if (collection.IsCreated)
            {
                collection.Dispose();
                collection = default;
            }
        }

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
}
