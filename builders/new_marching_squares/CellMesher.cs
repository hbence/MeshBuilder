using System;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

using static MeshBuilder.Utils;
using MeshBuffer = MeshBuilder.MeshData.Buffer;
using Data = MeshBuilder.MarchingSquaresMesher.Data;

namespace MeshBuilder.New
{
    public abstract class CellMesher : Builder
    {
        protected const int CalculateVertexBatchNum = 128;
        protected const int MeshTriangleBatchNum = 128;

        private const uint DefMeshDataBufferFlags = (uint)MeshBuffer.Vertex | (uint)MeshBuffer.Triangle | (uint)MeshBuffer.Normal;

        // meshdata
        protected NativeList<float3> vertices;
        protected NativeList<int> triangles;
        protected NativeList<float2> uvs;
        protected NativeList<float3> normals;

        public NativeList<float3> Vertices => vertices;
        public bool HasVertices => HasContainer(vertices);
        public NativeList<int> Triangles => triangles;
        public bool HasTriangles => HasContainer(triangles);
        public NativeList<float2> UVs => uvs;
        public bool HasUVs => HasContainer(uvs);
        public NativeList<float3> Normals => normals;
        public bool HasNormals => HasContainer(normals);

        private bool HasContainer<T>(NativeList<T> list) where T : struct => list.IsCreated && list.Length > 0;

        public uint MeshDataBufferFlags { get; set; } = DefMeshDataBufferFlags;

        abstract override protected JobHandle StartGeneration(JobHandle lastHandle);

        protected override void EndGeneration(Mesh mesh)
        {
            uint flags = MeshDataBufferFlags;
            if (uvs.IsCreated && uvs.Length > 0) { flags |= (uint)MeshBuffer.UV; }

            using (MeshData data = new MeshData(vertices.Length, triangles.Length, Allocator.Temp, flags))
            {
                NativeArray<float3>.Copy(vertices, data.Vertices);
                NativeArray<int>.Copy(triangles, data.Triangles);
                if (normals.IsCreated && normals.Length > 0) { NativeArray<float3>.Copy(normals, data.Normals); }
                if (uvs.IsCreated && uvs.Length > 0) { NativeArray<float2>.Copy(uvs, data.UVs); }

                data.UpdateMesh(mesh, MeshData.UpdateMode.Clear);
            }

            Dispose();
        }

        protected void CreateMeshData(bool hasNormals, bool hasUVs)
        {
            vertices = new NativeList<float3>(Allocator.TempJob);
            triangles = new NativeList<int>(Allocator.TempJob);

            if (hasNormals)
            {
                normals = new NativeList<float3>(Allocator.TempJob);
            }
            if (hasUVs)
            {
                uvs = new NativeList<float2>(Allocator.TempJob);
            }
        }

        override public void Dispose()
        {
            SafeDispose(ref vertices);
            SafeDispose(ref triangles);
            SafeDispose(ref uvs);
            SafeDispose(ref normals);
        }

        static protected void InitializeMeshData(int vertexCount, int triangleCount, ref NativeList<float3> vertices, ref NativeList<int> indices, bool generateNormals, ref NativeList<float3> normals, bool generateUVs, ref NativeList<float2> uvs)
        {
            vertices.ResizeUninitialized(vertexCount);
            indices.ResizeUninitialized(triangleCount);
            if (generateNormals)
            {
                normals.ResizeUninitialized(vertexCount);
            }
            if (generateUVs)
            {
                uvs.ResizeUninitialized(vertexCount);
            }
        }

        static protected void CheckData(Data data, Info info)
        {
            if (data == null)
            {
                Debug.LogError("No data!");
            }

            if (data.ColNum < 2 || data.RowNum < 2)
            {
                Debug.LogError("data size is ");
            }
        }

        protected struct CellInfo
        {
            public byte config;
            public float cornerDist;
            public float rightDist;
            public float topDist;
        }

        protected struct CellVertices
        {
            public int corner;
            public int bottomEdge;
            public int leftEdge;

            public void Set(int corner, int bottom, int left)
            {
                this.corner = corner;
                bottomEdge = bottom;
                leftEdge = left;
            }
        }

        protected interface IVertexCalculator
        {
            void CalculateVertices(int x, int y, float height, CellInfo info, CellVertices verts, NativeArray<float3> vertices);
        }

        protected struct BasicVertexCalculator : IVertexCalculator
        {
            public float cellSize;
            public void CalculateVertices(int x, int y, float height, CellInfo info, CellVertices verts, NativeArray<float3> vertices)
            {
                float3 pos = new float3(x * cellSize, height, y * cellSize);

                if (verts.corner >= 0) { vertices[verts.corner] = pos; }
                if (verts.leftEdge >= 0) { vertices[verts.leftEdge] = new float3(pos.x, pos.y, pos.z + cellSize * VcLerpT(info)); }
                if (verts.bottomEdge >= 0) { vertices[verts.bottomEdge] = new float3(pos.x + cellSize * HzLerpT(info), pos.y, pos.z); }
            }
        }
        protected struct LerpedVertexCalculator : IVertexCalculator
        {
            public float cellSize;
            public float lerpToEdge;

            public void CalculateVertices(int x, int y, float height, CellInfo info, CellVertices verts, NativeArray<float3> vertices)
            {
                float3 pos = new float3(x * cellSize, height, y * cellSize);

                if (verts.corner >= 0) { vertices[verts.corner] = pos; }
                if (verts.leftEdge >= 0) { vertices[verts.leftEdge] = new float3(pos.x, 0, pos.z + cellSize * VcLerpT(info, lerpToEdge)); }
                if (verts.bottomEdge >= 0) { vertices[verts.bottomEdge] = new float3(pos.x + cellSize * HzLerpT(info, lerpToEdge), 0, pos.z); }
            }
        }

        static protected float HzLerpT(CellInfo info) => LerpT(info.cornerDist, info.rightDist);
        static protected float HzLerpT(CellInfo info, float lerpToDist) => LerpT(info.cornerDist, info.rightDist, lerpToDist);

        static protected float VcLerpT(CellInfo info) => LerpT(info.cornerDist, info.topDist);
        static protected float VcLerpT(CellInfo info, float lerpToDist) => LerpT(info.cornerDist, info.topDist, lerpToDist);

        static protected float LerpT(float a, float b) => math.abs(a) / (math.abs(a) + math.abs(b));
        static protected float LerpT(float a, float b, float lerpToDist) => math.lerp(0.5f, math.abs(a) / (math.abs(a) + math.abs(b)), lerpToDist);

        private const float DistanceLimit = 0f;

        protected const byte MaskZero = 0;
        protected const byte MaskBL = 1 << 0;
        protected const byte MaskBR = 1 << 1;
        protected const byte MaskTR = 1 << 2;
        protected const byte MaskTL = 1 << 3;
        protected const byte MaskFull = MaskBL | MaskBR | MaskTR | MaskTL;

        protected static bool HasMask(byte config, byte mask) => (config & mask) != 0;

        protected static byte CalcConfiguration(float bl, float br, float tr, float tl)
        {
            byte config = 0;

            config |= (bl >= DistanceLimit) ? MaskBL : MaskZero;
            config |= (br >= DistanceLimit) ? MaskBR : MaskZero;
            config |= (tr >= DistanceLimit) ? MaskTR : MaskZero;
            config |= (tl >= DistanceLimit) ? MaskTL : MaskZero;

            return config;
        }

        protected static NativeArray<byte> CreateIndexCountArray(Allocator allocator)
        {
            NativeArray<byte> configs = new NativeArray<byte>(16, allocator);
            for (byte config = 0; config <= MaskFull; ++config)
            {
                configs[config] = CalcTriIndexCount(config);
            }
            return configs;
        }

        protected static byte CalcTriIndexCount(byte config)
        {
            switch (config)
            {
                // full
                case MaskBL | MaskBR | MaskTR | MaskTL: return 2 * 3;
                // corners
                case MaskBL: return 1 * 3;
                case MaskBR: return 1 * 3;
                case MaskTR: return 1 * 3;
                case MaskTL: return 1 * 3;
                // halves
                case MaskBL | MaskBR: return 2 * 3;
                case MaskTL | MaskTR: return 2 * 3;
                case MaskBL | MaskTL: return 2 * 3;
                case MaskBR | MaskTR: return 2 * 3;
                // diagonals
                case MaskBL | MaskTR: return 4 * 3;
                case MaskTL | MaskBR: return 4 * 3;
                // three quarters
                case MaskBL | MaskTR | MaskBR: return 3 * 3;
                case MaskBL | MaskTL | MaskBR: return 3 * 3;
                case MaskBL | MaskTL | MaskTR: return 3 * 3;
                case MaskTL | MaskTR | MaskBR: return 3 * 3;
            }
            return 0;
        }

        [Serializable]
        public class Info
        {
            public float LerpToExactEdge = 1f;
            public bool UseCullingData = true;
            public bool UseHeightData = true;
            public float OffsetY = 0f;
            public float HeightScale = 1f;
            public bool GenerateUvs = true;
            public float UScale = 1f;
            public float VScale = 1f;
            public bool NormalizeUV = true;
            public bool GenerateNormals = true;
            public bool IsFlipped = false;
        }
    }
}