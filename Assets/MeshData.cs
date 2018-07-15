using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    class MeshData
    {
        public Vector3[] Vertices { get; private set; }
        public int[] Triangles { get; private set; }
        public Vector2[] UVs { get; private set; }

        public Mesh Mesh { get; private set; }

        public MeshData(int verticesCount, int triangleCount)
        {
            Mesh = new Mesh();

            Vertices = new Vector3[verticesCount];
            Triangles = new int[triangleCount];
            UVs = new Vector2[verticesCount];
        }

        public void UpdateData()
        {
            Mesh.vertices = Vertices;
            Mesh.triangles = Triangles;
            Mesh.uv = UVs;
        }

        public void UpdateVertices(NativeArray<float3> data)
        {
            VectorConverter converter = new VectorConverter { Vector3Array = Vertices };
            data.CopyTo(converter.Float3Array);

            Mesh.vertices = Vertices;
            Mesh.RecalculateNormals();
        }

        static public void UpdateMesh(Mesh mesh, NativeArray<float3> vertices, NativeArray<int> tris, NativeArray<float2> uvs)
        {
            mesh.Clear();

            var verticesData = new VectorConverter { Float3Array = vertices.ToArray() };
            var triArray = tris.ToArray();
            var uvData = new UVConverter { Float2Array = uvs.ToArray() };

            mesh.vertices = verticesData.Vector3Array;
            mesh.triangles = triArray;
            mesh.uv = uvData.Vector2Array;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct VectorConverter
        {
            [FieldOffset(0)]
            public float3[] Float3Array;

            [FieldOffset(0)]
            public Vector3[] Vector3Array;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct UVConverter
        {
            [FieldOffset(0)]
            public float2[] Float2Array;

            [FieldOffset(0)]
            public Vector2[] Vector2Array;
        }
    }
}
