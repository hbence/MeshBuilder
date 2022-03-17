using System;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

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
            if (HasUVs) { flags |= (uint)MeshBuffer.UV; }

            using (MeshData data = new MeshData(vertices.Length, triangles.Length, Allocator.Temp, flags))
            {
                NativeArray<float3>.Copy(vertices, data.Vertices);
                NativeArray<int>.Copy(triangles, data.Triangles);
                if (HasNormals) { NativeArray<float3>.Copy(normals, data.Normals); }
                if (HasUVs) { NativeArray<float2>.Copy(uvs, data.UVs); }

                data.UpdateMesh(mesh, MeshData.UpdateMode.Clear);
            }

            Dispose();
        }

        protected void CreateMeshData(bool hasNormals, bool hasUVs)
        {
            vertices = new NativeList<float3>(Allocator.TempJob);
            triangles = new NativeList<int>(Allocator.TempJob);
            normals = new NativeList<float3>(Allocator.TempJob);
            uvs = new NativeList<float2>(Allocator.TempJob);
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

        public struct CellInfo
        {
            public byte config;
            public float cornerDist;
            public float rightDist;
            public float topDist;
        }

        public struct IndexSpan
        {
            public int start;
            public byte length;

            public IndexSpan(int s, byte l)
            {
                start = s;
                length = l;
            }

            public int End => start + length;
            public bool Has => length > 0;
        }

        public struct CellVertices
        {
            // from the point of view of the cell
            //
            // | leftEdge
            // |
            // x ____ bottomEdge

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

        public struct EdgeNormals
        {
            public float2 leftEdgeDir;
            public float2 bottomEdgeDir;
        }

        public interface IVertexCalculator
        {
            void CalculateVertices(int index, CellInfo info, CellVertices verts, NativeArray<float3> vertices);
        }

        public interface IScaleAdjustableCalculator
        {
            void UpdateScaleInfo(float sideOffsetScale, float heightOffset, float heightScale);
        }

        protected struct BasicVertexCalculator : IVertexCalculator
        {
            public int colNum;
            public float heightOffset;
            public float cellSize;

            public void CalculateVertices(int index, CellInfo info, CellVertices verts, NativeArray<float3> vertices)
                => CalculateVertices(index, info, verts, vertices, colNum, heightOffset, cellSize);

            static public void CalculateVertices(int index, CellInfo info, CellVertices verts, NativeArray<float3> vertices, int colNum, float heightOffset, float cellSize)
            {
                float3 pos = CalculatePosition(index, colNum, cellSize, heightOffset);

                if (verts.corner >= 0) { vertices[verts.corner] = pos; }
                if (verts.leftEdge >= 0) { vertices[verts.leftEdge] = new float3(pos.x, pos.y, pos.z + cellSize * VcLerpT(info)); }
                if (verts.bottomEdge >= 0) { vertices[verts.bottomEdge] = new float3(pos.x + cellSize * HzLerpT(info), pos.y, pos.z); }
            }

            static public float3 CalculatePosition(int index, int colNum, float cellSize, float height)
                => new float3((index % colNum) * cellSize, height, (index / colNum) * cellSize);
        }

        protected struct LerpedVertexCalculator : IVertexCalculator
        {
            public int colNum;
            public float heightOffset;
            public float cellSize;
            public float lerpToEdge;

            public void CalculateVertices(int index, CellInfo info, CellVertices verts, NativeArray<float3> vertices)
                => CalculateVertices(index, info, verts, vertices, colNum, heightOffset, cellSize, lerpToEdge);

            static public void CalculateVertices(int index, CellInfo info, CellVertices verts, NativeArray<float3> vertices, int colNum, float heightOffset, float cellSize, float lerpToEdge)
            {
                float3 pos = BasicVertexCalculator.CalculatePosition(index, colNum, cellSize, heightOffset);

                if (verts.corner >= 0) { vertices[verts.corner] = pos; }
                if (verts.leftEdge >= 0) { vertices[verts.leftEdge] = new float3(pos.x, pos.y, pos.z + cellSize * VcLerpT(info, lerpToEdge)); }
                if (verts.bottomEdge >= 0) { vertices[verts.bottomEdge] = new float3(pos.x + cellSize * HzLerpT(info, lerpToEdge), pos.y, pos.z); }
            }
        }

        protected struct BasicHeightVertexCalculator : IVertexCalculator
        {
            public int colNum;
            public float cellSize;
            
            public float heightOffset;
            public float heightScale;
            [ReadOnly] public NativeArray<float> heights;

            public void CalculateVertices(int index, CellInfo info, CellVertices verts, NativeArray<float3> vertices)
                => BasicVertexCalculator.CalculateVertices(index, info, verts, vertices, colNum, heightOffset + heightScale * heights[index], cellSize);
        }

        protected struct LerpedHeightVertexCalculator : IVertexCalculator
        {
            public int colNum;
            public float cellSize;
            public float lerpToEdge;
            
            public float heightOffset;
            public float heightScale;
            [ReadOnly] public NativeArray<float> heights;

            public void CalculateVertices(int index, CellInfo info, CellVertices verts, NativeArray<float3> vertices)
                => LerpedVertexCalculator.CalculateVertices(index, info, verts, vertices, colNum, heightOffset + heightScale * heights[index], cellSize, lerpToEdge);
        }

        static protected float HzLerpT(CellInfo info) => LerpT(info.cornerDist, info.rightDist);
        static protected float HzLerpT(CellInfo info, float lerpToDist) => LerpT(info.cornerDist, info.rightDist, lerpToDist);

        static protected float VcLerpT(CellInfo info) => LerpT(info.cornerDist, info.topDist);
        static protected float VcLerpT(CellInfo info, float lerpToDist) => LerpT(info.cornerDist, info.topDist, lerpToDist);

        static protected float LerpT(float a, float b) => math.abs(a) / (math.abs(a) + math.abs(b));
        static protected float LerpT(float a, float b, float lerpToDist) => math.lerp(0.5f, math.abs(a) / (math.abs(a) + math.abs(b)), lerpToDist);

        public interface InfoGenerator<InfoType> where InfoType : struct
        {
            InfoType GenerateInfoWithTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex);
            InfoType GenerateInfoNoTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex);
        }

        [BurstCompile]
        public struct CalculateInfoJob<GeneratorType, InfoType> : IJob
            where InfoType : struct
            where GeneratorType : struct, InfoGenerator<InfoType>
        {
            public int distanceColNum;
            public int distanceRowNum;

            public GeneratorType generator;

            [ReadOnly] public NativeArray<float> distances;

            public NativeList<float3> vertices;
            public NativeList<int> indices;

            public bool generateNormals;
            public float3 normal;
            public NativeList<float3> normals;

            public bool generateUVs;
            public NativeList<float2> uvs;

            [WriteOnly] public NativeArray<InfoType> info;

            public void Execute()
            {
                int nextVertex = 0;
                int nextTriangleIndex = 0;

                // inner part
                for (int y = 0; y < distanceRowNum - 1; ++y)
                {
                    for (int x = 0; x < distanceColNum - 1; ++x)
                    {
                        int index = y * distanceColNum + x;
                        float corner = distances[index];
                        float right = distances[index + 1];
                        float topRight = distances[index + 1 + distanceColNum];
                        float top = distances[index + distanceColNum];

                        info[index] = generator.GenerateInfoWithTriangles(index, corner, right, topRight, top, ref nextVertex, ref nextTriangleIndex);
                    }
                }

                // top border
                for (int x = 0, y = distanceRowNum - 1; x < distanceColNum - 1; ++x)
                {
                    int index = y * distanceColNum + x;
                    float corner = distances[index];
                    float right = distances[index + 1];
                    info[index] = generator.GenerateInfoNoTriangles(index, corner, right, -1, -1, ref nextVertex, ref nextTriangleIndex);
                }
                // right border
                for (int x = distanceColNum - 1, y = 0; y < distanceRowNum - 1; ++y)
                {
                    int index = y * distanceColNum + x;
                    float corner = distances[index];
                    float top = distances[index + distanceColNum];
                    info[index] = generator.GenerateInfoNoTriangles(index, corner, -1, -1, top, ref nextVertex, ref nextTriangleIndex);
                }

                // top right corner
                int last = distanceColNum * distanceRowNum - 1;
                info[last] = generator.GenerateInfoNoTriangles(last, distances[last], -1, -1, -1, ref nextVertex, ref nextTriangleIndex);

                InitializeMeshData(nextVertex, nextTriangleIndex, ref vertices, ref indices, generateNormals, ref normals, generateUVs, ref uvs);
                if (generateNormals)
                {
                    for (int i = 0; i < normals.Length; ++i)
                    {
                        normals[i] = normal;
                    }
                }
            }

            public static JobHandle Schedule(GeneratorType generator, Data data, NativeList<float3> vertices, NativeList<int> triangles, bool generateNormals, float3 normal, NativeList<float3> normals, bool generateUVs, NativeList<float2> uvs, NativeArray<InfoType> info, JobHandle dependOn = default)
            {
                var cornerJob = new CalculateInfoJob<GeneratorType, InfoType>
                {
                    distanceColNum = data.ColNum,
                    distanceRowNum = data.RowNum,

                    generator = generator,

                    distances = data.RawData,

                    vertices = vertices,
                    indices = triangles,

                    generateNormals = generateNormals,
                    normals = normals,
                    normal = normal,
                    generateUVs = generateUVs,
                    uvs = uvs,

                    info = info
                };
                return cornerJob.Schedule(dependOn);
            }
        }

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

        [Serializable]
        public class ScaledInfo : Info
        {
            public float ScaledOffset = 0f;
        }
    }
}