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

        public bool ShouldGenerateUV = true;
        public float uvScale = 1f;

        public bool ShouldGenerateNormals = true;

        private MesherInfo mesherInfo = new MesherInfo();

        private NativeList<float3> vertices;
        private NativeList<int> triangles;
        private NativeList<float2> uvs;
        private NativeList<float3> normals;

        public void Init(int colNum, int rowNum, float cellSize, float yOffset = 0, float[] distanceData = null)
        {
            CellSize = cellSize;

            DistanceData?.Dispose();
            DistanceData = new Data(colNum, rowNum, distanceData);

            mesherInfo.Set(HasTop, NoSide, NoBottom, NoSeparateSides, yOffset, 0);

            Inited();
        }

        public void InitForFullCell(int colNum, int rowNum, float cellSize, float height, bool hasBottom = false, float[] distanceData = null)
        {
            CellSize = cellSize;

            DistanceData?.Dispose();
            DistanceData = new Data(colNum, rowNum, distanceData);

            mesherInfo.Set(HasTop, HasSide, hasBottom, HasSeparateSides, height, 0);

            Inited();
        }

        public void InitForFullCellNoEdgeVertices(int colNum, int rowNum, float cellSize, float height, float[] distanceData = null)
        {
            CellSize = cellSize;

            DistanceData?.Dispose();
            DistanceData = new Data(colNum, rowNum, distanceData);

            mesherInfo.Set(HasTop, HasSide, HasBottom, NoSeparateSides, height, 0);

            Inited();
        }

        public void InitForFullCellTapered(int colNum, int rowNum, float cellSize, float height, float bottomScaleOffset = 0.5f, bool hasBottom = false, float[] distanceData = null)
        {
            CellSize = cellSize;

            DistanceData?.Dispose();
            DistanceData = new Data(colNum, rowNum, distanceData);

            mesherInfo.Set(HasTop, HasSide, hasBottom, HasSeparateSides, height, bottomScaleOffset);

            Inited();
        }

        override protected JobHandle StartGeneration(JobHandle lastHandle)
        {
            return mesherInfo.StartGeneration(lastHandle, this);
        }
        
        /*
        override protected JobHandle StartGeneration(JobHandle lastHandle)
        {
            var cellMesher = CreateScalableFullCellMesher(0.4f, 0.2f);
            return cellMesher.StartGeneration(lastHandle, this);
        }
        //*/

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

            uvs = new NativeList<float2>(Allocator.TempJob);
            AddTemp(uvs);

            normals = new NativeList<float3>(Allocator.TempJob);
            AddTemp(normals);

            bool generateUVs = ShouldGenerateUV && cellMesher.CanGenerateUvs;
            bool generateNormals = ShouldGenerateNormals && cellMesher.CanGenerateNormals;

            int cellCount = (ColNum - 1) * (RowNum - 1);

            var cornerJob = new GenerateCorners<InfoType, MesherType>
            {
                distanceColNum = ColNum,
                distanceRowNum = RowNum,
                
                cellMesher = cellMesher,

                distances = DistanceData.RawData,
                corners = corners,

                vertices = vertices,
                indices = triangles,

                generateUVs = generateUVs,
                uvs = uvs,
                generateNormals = generateNormals,
                normals = normals
            };
            lastHandle = cornerJob.Schedule(lastHandle);

            if (cellMesher.NeedUpdateInfo)
            {
                var infoJob = new UpdateCorners<InfoType, MesherType>
                {
                    cellColNum = ColNum - 1,
                    cellRowNum = RowNum - 1,
                    cellMesher = cellMesher,
                    cornerInfos = corners
                };
                lastHandle = infoJob.Schedule(lastHandle);
            }

            var vertexJob = new CalculateVertices<InfoType, MesherType>
            {
                cornerColNum = ColNum,
                cellSize = CellSize,
                cellMesher = cellMesher,

                cornerInfos = corners,
                vertices = vertices.AsDeferredJobArray()
            };
            var vertexHandle = vertexJob.Schedule(corners.Length, CalculateVertexBatchNum, lastHandle);

            var uvHandle = vertexHandle;
            if (generateUVs)
            {
                var uvJob = new CalculateUvs<InfoType, MesherType>
                {
                    cornerColNum = ColNum,
                    cornerRowNum = RowNum,
                    cellSize = CellSize,
                    uvScale = uvScale,
                    cellMesher = cellMesher,

                    cornerInfos = corners,
                    vertices = vertices.AsDeferredJobArray(),
                    uvs = uvs.AsDeferredJobArray()
                };
                uvHandle = uvJob.Schedule(corners.Length, CalculateVertexBatchNum, vertexHandle);
            }

            var normalHandle = vertexHandle;
            if (generateNormals)
            {
                var normalJob = new CalculateNormals<InfoType, MesherType>
                {
                    cornerColNum = ColNum,
                    cornerRowNum = RowNum,
                    cellMesher = cellMesher,

                    cornerInfos = corners,
                    vertices = vertices.AsDeferredJobArray(),
                    normals = normals.AsDeferredJobArray()
                };
                normalHandle = normalJob.Schedule(corners.Length, CalculateVertexBatchNum, vertexHandle);
            }

            vertexHandle = JobHandle.CombineDependencies(vertexHandle, uvHandle, normalHandle);

            var trianglesJob = new CalculateTriangles<InfoType, MesherType>
            {
                cornerColNum = ColNum,
                cellMesher = cellMesher,
                cornerInfos = corners,
                triangles = triangles.AsDeferredJobArray()
            };
            var trianglesHandle = trianglesJob.Schedule(cellCount, MeshTriangleBatchNum, lastHandle);

            return JobHandle.CombineDependencies(vertexHandle, trianglesHandle);
        }

        protected override void EndGeneration(Mesh mesh)
        {
            uint flags = MeshDataBufferFlags;

            if (uvs.Length > 0) {  flags |= (uint)MeshBuffer.UV; }
            if (normals.Length > 0) { flags |= (uint)MeshBuffer.Normal; }

            using (MeshData data = new MeshData(vertices.Length, triangles.Length, Allocator.Temp, flags))
            {
                NativeArray<float3>.Copy(vertices, data.Vertices);
                NativeArray<int>.Copy(triangles, data.Triangles);

                if (uvs.Length > 0) { NativeArray<float2>.Copy(uvs, data.UVs); }
                if (normals.Length > 0) { NativeArray<float3>.Copy(normals, data.Normals); }

                data.UpdateMesh(mesh, MeshData.UpdateMode.Clear);

                if (normals.Length == 0) { mesh.RecalculateNormals(); }
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            DistanceData?.Dispose();
            DistanceData = null;
        }

        protected override void DisposeTemps()
        {
            base.DisposeTemps();

            vertices = default;
            triangles = default;
            uvs = default;
            normals = default;
        }

        private const bool HasTop = true;
        private const bool NoTop = false;
        private const bool HasSide = true;
        private const bool NoSide = false;
        private const bool HasBottom = true;
        private const bool NoBottom = false;
        private const bool HasSeparateSides = true;
        private const bool NoSeparateSides = false;

        private class MesherInfo
        {
            public bool hasTop = true;
            public bool hasSide = true;
            public bool hasBottom = true;
            public float height = 1;
            public float bottomScaleOffset = 0;
            public bool hasSeparateSides = true;

            public void Set(bool hasTop, bool hasSide, bool hasBottom, bool hasSeparateSides, float height, float bottomScaleOffset)
            {
                this.hasTop = hasTop;
                this.hasSide = hasSide;
                this.hasBottom = hasBottom;
                this.hasSeparateSides = hasSeparateSides;
                this.height = height;
                this.bottomScaleOffset = bottomScaleOffset;
            }

            public JobHandle StartGeneration(JobHandle dependOn, MarchingSquaresMesher mesher)
            {
                if (hasTop && !hasSide && !hasBottom)
                {
                    var cellMesher = new SimpleTopCellMesher();
                    cellMesher.heightOffset = height;
                    return mesher.StartGeneration<SimpleTopCellMesher.CornerInfo, SimpleTopCellMesher>(dependOn, cellMesher);
                }
                else if (hasTop && hasSide && !hasBottom)
                {
                    if (bottomScaleOffset > 0)
                    {
                        var cellMesher = CreateNoBottomScalableFullCellMesher(height, bottomScaleOffset);
                        return cellMesher.StartGeneration(dependOn, mesher);
                    }
                    else
                    {
                        var cellMesher = CreateNoBottomCellMesher(height);
                        return cellMesher.StartGeneration(dependOn, mesher);
                    }
                }
                else if (hasTop && hasSide && hasBottom)
                {
                    if (hasSeparateSides)
                    {
                        if (bottomScaleOffset > 0)
                        {
                            var cellMesher = CreateScalableFullCellMesher(height, bottomScaleOffset);
                            return cellMesher.StartGeneration(dependOn, mesher);
                        }
                        else
                        {
                            var cellMesher = CreateFullCellMesher(height);
                            return cellMesher.StartGeneration(dependOn, mesher);
                        }
                    }
                    else
                    {
                        var cellMesher = new SimpleFullCellMesher();
                        cellMesher.height = height;
                        return mesher.StartGeneration<SimpleSideMesher.CornerInfo, SimpleFullCellMesher>(dependOn, cellMesher);
                    }
                }

                var defMesher = CreateNoBottomScalableFullCellMesher(height, bottomScaleOffset);
                return defMesher.StartGeneration(dependOn, mesher);
            }
        }

        [BurstCompile]
        private struct GenerateCorners<InfoType, MesherType> : IJob 
            where InfoType : struct 
            where MesherType : struct, ICellMesher<InfoType>
        {
            private const bool HasCellTriangles = true;
            private const bool NoCellTriangles = false;

            public int distanceColNum;
            public int distanceRowNum;
            
            public MesherType cellMesher;

            [ReadOnly] public NativeArray<float> distances;
            
            [WriteOnly] public NativeArray<InfoType> corners;
            
            public NativeList<float3> vertices;
            public NativeList<int> indices;

            public bool generateUVs;
            public NativeList<float2> uvs;
            public bool generateNormals;
            public NativeList<float3> normals;

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
                        corners[index] = cellMesher.GenerateInfo(corner, right, topRight, top, ref nextVertex, ref nextTriangleIndex, HasCellTriangles);
                    }
                }
                // top border
                for (int x = 0, y = distanceRowNum - 1; x < distanceColNum - 1; ++x)
                {
                    int index = y * distanceColNum + x;
                    float corner = distances[index];
                    float right = distances[index + 1];
                    corners[index] = cellMesher.GenerateInfo(corner, right, -1, -1, ref nextVertex, ref nextTriangleIndex, NoCellTriangles);
                }
                // right border
                for (int x = distanceColNum - 1, y = 0; y < distanceRowNum - 1; ++y)
                {
                    int index = y * distanceColNum + x;
                    float corner = distances[index];
                    float top = distances[index + distanceColNum];
                    corners[index] = cellMesher.GenerateInfo(corner, -1, -1, top, ref nextVertex, ref nextTriangleIndex, NoCellTriangles);
                }
                // top right corner
                int last = distanceColNum * distanceRowNum - 1;
                corners[last] = cellMesher.GenerateInfo(distances[last], -1, -1, -1, ref nextVertex, ref nextTriangleIndex, NoCellTriangles);
                
                vertices.ResizeUninitialized(nextVertex);
                indices.ResizeUninitialized(nextTriangleIndex);

                if (generateUVs)
                {
                    uvs.ResizeUninitialized(nextVertex);
                }
                if (generateNormals)
                {
                    normals.ResizeUninitialized(nextVertex);
                }
            }
        }

        [BurstCompile]
        private struct UpdateCorners<InfoType, MesherType> : IJob
            where InfoType : struct
            where MesherType : struct, ICellMesher<InfoType>
        {
            public int cellColNum;
            public int cellRowNum;

            public MesherType cellMesher;

            [NativeDisableParallelForRestriction] public NativeArray<InfoType> cornerInfos;

            public void Execute()
            {
                int cornerColNum = cellColNum + 1;
                for (int y = 0; y < cellRowNum; ++y)
                {
                    for (int x = 0; x < cellColNum; ++x)
                    {
                        int index = y * cornerColNum + x;
                        var corner = cornerInfos[index];
                        var right = cornerInfos[index + 1];
                        var top = cornerInfos[index + cornerColNum];
                        cellMesher.UpdateInfo(x, y, cellColNum, cellRowNum, ref corner, ref top, ref right);

                        cornerInfos[index] = corner;
                        cornerInfos[index + 1] = right;
                        cornerInfos[index + cornerColNum] = top;
                    }
                }
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
        private struct CalculateUvs<InfoType, MesherType> : IJobParallelFor
            where InfoType : struct
            where MesherType : struct, ICellMesher<InfoType>
        {
            public int cornerColNum;
            public int cornerRowNum;
            public float cellSize;

            public float uvScale;

            public MesherType cellMesher;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<InfoType> cornerInfos;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<float3> vertices;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float2> uvs;

            public void Execute(int index)
            {
                InfoType info = cornerInfos[index];
                int x = index % cornerColNum;
                int y = index / cornerRowNum;
                cellMesher.CalculateUvs(x, y, cornerColNum, cornerRowNum, cellSize, info, uvScale, vertices, uvs);
            }
        }

        [BurstCompile]
        private struct CalculateNormals<InfoType, MesherType> : IJobParallelFor
            where InfoType : struct
            where MesherType : struct, ICellMesher<InfoType>
        {
            public int cornerColNum;
            public int cornerRowNum;

            public MesherType cellMesher;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<InfoType> cornerInfos;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<float3> vertices;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float3> normals;

            public void Execute(int index)
            {
                InfoType info = cornerInfos[index];
                int x = index % cornerColNum;
                int y = index / cornerRowNum;

                InfoType right = x == (cornerColNum - 1) ? info : cornerInfos[index + 1];
                InfoType top = y == (cornerRowNum - 1) ? info : cornerInfos[index + cornerColNum];

                cellMesher.CalculateNormals(info, right, top, vertices, normals);
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

        public interface ICellMesher<InfoType> where InfoType : struct
        {
            InfoType GenerateInfo(float cornerDist, float rightDist, float topRightDist, float topDist, ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles);
            
            bool NeedUpdateInfo { get; }
            void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref InfoType cell, ref InfoType top, ref InfoType right);
            
            void CalculateVertices(int x, int y, float cellSize, InfoType info, NativeArray<float3> vertices);
            void CalculateIndices(InfoType bl, InfoType br, InfoType tr, InfoType tl, NativeArray<int> triangles);
            
            bool CanGenerateUvs { get; }
            void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, InfoType corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs); 
            
            bool CanGenerateNormals { get; }
            void CalculateNormals(InfoType corner, InfoType right, InfoType top, NativeArray<float3> vertices, NativeArray<float3> normals);
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
