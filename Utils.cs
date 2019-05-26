using System.Runtime.InteropServices;

using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    public class Utils
    {
        public struct Offset
        {
            public int index;
            public int length;

            public int Start { get => index; }
            public int End { get => index + length; }
        }

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

        [StructLayout(LayoutKind.Explicit)]
        public struct MatrixConverter
        {
            [FieldOffset(0)]
            public float4x4 Float4x4;

            [FieldOffset(0)]
            public Matrix4x4 Matrix4X4;
        }

        static public Matrix4x4 ToMatrix4x4(float4x4 m)
        {
            var c = new MatrixConverter { Float4x4 = m };
            return c.Matrix4X4;
        }

        static public float4x4 ToFloat4x4(Matrix4x4 m)
        {
            var c = new MatrixConverter { Matrix4X4 = m };
            return c.Float4x4;
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
