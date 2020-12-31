using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

using MeshBuffer = MeshBuilder.MeshData.Buffer;

namespace MeshBuilder
{
    public partial class MarchingSquaresMesher : Builder
    {
        private const uint DefMeshDataBufferFlags = (uint)MeshBuffer.Vertex | (uint)MeshBuffer.Triangle;

        private const int CalculateVertexBatchNum = 128;
        private const int MeshTriangleBatchNum = 128;

        public float CellSize { get; private set; }
        public Data DistanceData { get; private set; }
        public int ColNum => DistanceData.ColNum;
        public int RowNum => DistanceData.RowNum;

        public uint MeshDataBufferFlags { get; set; } = DefMeshDataBufferFlags;

        private NativeList<float3> vertices;
        private NativeList<int> triangles;

        public void Init(int colNum, int rowNum, float cellSize, float[] distanceData = null)
        {
            CellSize = cellSize;

            DistanceData?.Dispose();
            DistanceData = new Data(colNum, rowNum, distanceData);

            Inited();
        }

        override protected JobHandle StartGeneration(JobHandle lastHandle)
        {
            SimpleFullCellMesher cellMesher = new SimpleFullCellMesher();
            cellMesher.height = 0.3f;
            return StartGeneration<SimpleFullCellMesher.FullCornerInfo, SimpleFullCellMesher>(lastHandle, cellMesher);
        }

        private JobHandle StartGeneration<InfoType, MesherType>(JobHandle lastHandle, MesherType cellMesher)
            where InfoType : struct
            where MesherType : struct, ICellMesher<InfoType>
        {
            NativeArray<InfoType> corners = new NativeArray<InfoType>(ColNum * RowNum, Allocator.TempJob);
            AddTemp(corners);

            vertices = new NativeList<float3>(Allocator.TempJob);
            AddTemp(vertices);

            triangles = new NativeList<int>(Allocator.TempJob);
            AddTemp(triangles);

            var cornerJob = new GenerateCorners<InfoType, MesherType>
            {
                distanceColNum = ColNum,
                distanceRowNum = RowNum,
                
                cellMesher = cellMesher,

                distances = DistanceData.RawData,
                corners = corners,

                vertices = vertices,
                indices = triangles
            };
            lastHandle = cornerJob.Schedule(lastHandle);

            var vertexJob = new CalculateVertices<InfoType, MesherType>
            {
                cornerColNum = ColNum,
                cellSize = CellSize,
                cellMesher = cellMesher,

                cornerInfos = corners,
                vertices = vertices.AsDeferredJobArray()
            };
            var vertexHandle = vertexJob.Schedule(corners.Length, CalculateVertexBatchNum, lastHandle);
            
            var trianglesJob = new CalculateTriangles<InfoType, MesherType>
            {
                cornerColNum = ColNum,
                cellMesher = cellMesher,
                cornerInfos = corners,
                triangles = triangles.AsDeferredJobArray()
            };
            int cellCount = (ColNum - 1) * (RowNum - 1);
            var trianglesHandle = trianglesJob.Schedule(cellCount, MeshTriangleBatchNum, lastHandle);

            return JobHandle.CombineDependencies(vertexHandle, trianglesHandle);
        }

        protected override void EndGeneration(Mesh mesh)
        {
            using (MeshData data = new MeshData(vertices.Length, triangles.Length, Allocator.Temp, MeshDataBufferFlags))
            {
                NativeArray<float3>.Copy(vertices, data.Vertices);
                NativeArray<int>.Copy(triangles, data.Triangles);
                data.UpdateMesh(mesh, MeshData.UpdateMode.Clear);
                mesh.RecalculateNormals();
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            DistanceData?.Dispose();
            DistanceData = null;
        }

        [BurstCompile]
        private struct GenerateCorners<InfoType, MesherType> : IJob 
            where InfoType : struct 
            where MesherType : struct, ICellMesher<InfoType>
        {
            private const bool Inner = false;
            private const bool OnBorder = true;

            public int distanceColNum;
            public int distanceRowNum;
            
            public MesherType cellMesher;

            [ReadOnly] public NativeArray<float> distances;
            
            [WriteOnly] public NativeArray<InfoType> corners;
            
            public NativeList<float3> vertices;
            public NativeList<int> indices;

            public void Execute()
            {
                int nextVertex = 0;
                int nextTriangleIndex = 0;
                // the border cases are separated to avoid boundary checking
                // not sure if it's worth it...
                // inner
                for (int y = 0; y < distanceRowNum - 1; ++y)
                {
                    for (int x = 0; x < distanceColNum - 1; ++x)
                    {
                        int index = y * distanceColNum + x;
                        float corner = distances[index];
                        float right = distances[index + 1];
                        float topRight = distances[index + 1 + distanceColNum];
                        float top = distances[index + distanceColNum];
                        corners[index] = cellMesher.GenerateInfo(corner, right, topRight, top, ref nextVertex, ref nextTriangleIndex, Inner);
                    }
                }
                // top border
                for (int x = 0, y = distanceRowNum - 1; x < distanceColNum - 1; ++x)
                {
                    int index = y * distanceColNum + x;
                    float corner = distances[index];
                    float right = distances[index + 1];
                    corners[index] = cellMesher.GenerateInfo(corner, right, -1, -1, ref nextVertex, ref nextTriangleIndex, OnBorder);
                }
                // right border
                for (int x = distanceColNum - 1, y = 0; y < distanceRowNum - 1; ++y)
                {
                    int index = y * distanceColNum + x;
                    float corner = distances[index];
                    float top = distances[index + distanceColNum];
                    corners[index] = cellMesher.GenerateInfo(corner, -1, -1, top, ref nextVertex, ref nextTriangleIndex, OnBorder);
                }
                // top right corner
                int last = distanceColNum * distanceRowNum - 1;
                corners[last] = cellMesher.GenerateInfo(distances[last], -1, -1, -1, ref nextVertex, ref nextTriangleIndex, OnBorder);
                
                vertices.ResizeUninitialized(nextVertex);
                indices.ResizeUninitialized(nextTriangleIndex);
            }
        }

        [BurstCompile]
        private struct CalculateVertices<InfoType, MesherType> : IJobParallelFor
            where InfoType : struct
            where MesherType : struct, ICellMesher<InfoType>
        {
            public int cornerColNum;
            public float cellSize;

            public MesherType cellMesher;

            [ReadOnly] public NativeArray<InfoType> cornerInfos;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float3> vertices;

            public void Execute(int index)
            {
                InfoType info = cornerInfos[index];
                int x = index % cornerColNum;
                int y = index / cornerColNum;
                cellMesher.CalculateVertices(x, y, cellSize, info, vertices);
            }
        }

        [BurstCompile]
        private struct CalculateTriangles<InfoType, MesherType> : IJobParallelFor
            where InfoType : struct
            where MesherType : struct, ICellMesher<InfoType>
        {
            public int cornerColNum;
            
            public MesherType cellMesher;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<InfoType> cornerInfos;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<int> triangles;

            public void Execute(int index)
            {
                int cellColNum = cornerColNum - 1;
                int x = index % cellColNum;
                int y = index / cellColNum;
                index = y * cornerColNum + x;

                InfoType bl = cornerInfos[index];
                InfoType br = cornerInfos[index + 1];
                InfoType tr = cornerInfos[index + 1 + cornerColNum];
                InfoType tl = cornerInfos[index + cornerColNum];
                cellMesher.CalculateIndices(bl, br, tr, tl, triangles);
            }
        }

        private interface ICellMesher<InfoType> where InfoType : struct
        {
            //bool CanGenerateNormals { get; }
            //bool CanGenerateUvs { get; }
            InfoType GenerateInfo(float cornerDist, float rightDist, float topRightDist, float topDist, ref int nextVertices, ref int nextTriIndex, bool onBorder);
            void CalculateVertices(int x, int y, float cellSize, InfoType info, NativeArray<float3> vertices);
            void CalculateIndices(InfoType bl, InfoType br, InfoType tr, InfoType tl, NativeArray<int> triangles);
            //void CalculateNormals(InfoType blCornerInfo, NativeArray<float3> normals); 
            //void CalculateUvs(InfoType blCornerInfo, NativeArray<float2> uvs); 
        }

        private const float DistanceLimit = 0f;

        private const byte MaskZero = 0;
        private const byte MaskBL = 1 << 0;
        private const byte MaskBR = 1 << 1;
        private const byte MaskTR = 1 << 2;
        private const byte MaskTL = 1 << 3;

        private static bool HasMask(byte config, byte mask) => (config & mask) != 0;

        private static byte CalcConfiguration(float bl, float br, float tr, float tl)
        {
            byte config = 0;

            config |= (bl >= DistanceLimit) ? MaskBL : MaskZero;
            config |= (br >= DistanceLimit) ? MaskBR : MaskZero;
            config |= (tr >= DistanceLimit) ? MaskTR : MaskZero;
            config |= (tl >= DistanceLimit) ? MaskTL : MaskZero;

            return config;
        }
    }
}
