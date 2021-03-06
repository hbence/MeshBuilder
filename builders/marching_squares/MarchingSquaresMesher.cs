﻿using System;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

using MeshBuffer = MeshBuilder.MeshData.Buffer;

namespace MeshBuilder
{
    // TODO: vertices generation with height data can be a bit weird around the edges since the height for the edge vertices is the
    // same as the height of the corner, so it's possible that the edge of a cell is placed next to an adjacent corner vertex, which can
    // create a sudden jump if the height difference is large
    // TODO: there is a lot of initialization and branching I could avoid if I would use the 0 index of the vertex, uv, normal buffer
    // for junk data. would it worth it? maybe I could check it later.

    public partial class MarchingSquaresMesher : Builder
    {
        private const uint DefMeshDataBufferFlags = (uint)MeshBuffer.Vertex | (uint)MeshBuffer.Triangle | (uint)MeshBuffer.Normal;

        private const int CalculateVertexBatchNum = 128;
        private const int MeshTriangleBatchNum = 128;

        public enum OptimizationMode
        {
            GreedyRect,
            NextLargestRect
        }

        [Serializable]
        public struct SideOffset
        {
            public float hz, vc;
            public SideOffset(float hz, float vc) { this.hz = hz; this.vc = vc; }
        }

        public float CellSize { get; private set; }
        public Data DistanceData { get; private set; }
        public int ColNum => DistanceData.ColNum;
        public int RowNum => DistanceData.RowNum;

        public bool UseCullingData { get; set; } = true;
        public bool UseVertexHeightData { get; set; } = true;

        public uint MeshDataBufferFlags { get; set; } = DefMeshDataBufferFlags;

        public bool ShouldGenerateUV = true;
        public float uvScale = 1f;

        public float heightDataScale = 1f;

        private MesherInfo mesherInfo = new MesherInfo();

        private NativeList<float3> vertices;
        private NativeList<int> triangles;
        private NativeList<float2> uvs;
        private NativeList<float3> normals;

        private void Init(Data distanceData, float cellSize)
        {
            DistanceData = distanceData;
            CellSize = cellSize;
            Inited();
        }

        public void Init(Data distanceData, float cellSize, float yOffset = 0, float lerpToExactEdge = 1)
        {
            mesherInfo.Set(MesherInfo.Type.TopOnly, yOffset, 0, lerpToExactEdge);
            Init(distanceData, cellSize);
        }

        public void InitForFullCell(Data distanceData, float cellSize, float height, bool hasBottom = false, float lerpToExactEdge = 1)
        {
            mesherInfo.Set(hasBottom ? MesherInfo.Type.Full : MesherInfo.Type.NoBottom, height, 0, lerpToExactEdge);
            Init(distanceData, cellSize);
        }

        public void InitForFullCellSimpleMesh(Data distanceData, float cellSize, float height, float lerpToExactEdge = 1)
        {
            mesherInfo.Set(MesherInfo.Type.FullSimple, height, 0, lerpToExactEdge);
            Init(distanceData, cellSize);
        }

        public void InitForSideOnly(Data distanceData, float cellSize, float height, float lerpToExactEdge = 1)
        {
            mesherInfo.Set(MesherInfo.Type.SideOnly, height, 0, lerpToExactEdge);
            Init(distanceData, cellSize);
        }

        public void InitForTaperedSideOnly(Data distanceData, float cellSize, float height, float topOffset, float bottomOffset, float lerpToExactEdge = 1)
        {
            mesherInfo.Set(MesherInfo.Type.SideOnly, height, bottomOffset, lerpToExactEdge);
            mesherInfo.bottomOffsetScale = bottomOffset;
            mesherInfo.topOffsetScale = topOffset;
            Init(distanceData, cellSize);
        }

        public void InitForSegmentedSideOnly(Data distanceData, float cellSize, SideOffset[] offsets, float lerpToExactEdge = 1)
        {
            mesherInfo.Set(MesherInfo.Type.SideOnly, offsets[0].vc, 0, lerpToExactEdge);
            mesherInfo.sideOffsets = offsets;

            if (offsets.Length < 2 || offsets.Length > SegmentedSideMesher.MaxLayerCount)
            {
                Debug.LogError("segmented mesher offset count needs to be between 2 and MaxLayerCount(" + SegmentedSideMesher.MaxLayerCount + ")");
            }

            Init(distanceData, cellSize);
        }

        public void InitForFullCellTapered(Data distanceData, float cellSize, float height, float bottomScaleOffset = 0.5f, bool hasBottom = false, float lerpToExactEdge = 1)
        {
            mesherInfo.Set(hasBottom ? MesherInfo.Type.Full : MesherInfo.Type.NoBottom, height, bottomScaleOffset, lerpToExactEdge);
            Init(distanceData, cellSize);
        }

        public void InitForFullCellSegmented(Data distanceData, float cellSize, SideOffset[] offsets, bool hasBottom = false, float lerpToExactEdge = 1)
        {
            mesherInfo.Set(hasBottom ? MesherInfo.Type.Full : MesherInfo.Type.NoBottom, offsets[0].vc, 0, lerpToExactEdge);
            mesherInfo.sideOffsets = offsets;

            if (offsets.Length < 2 || offsets.Length > SegmentedSideMesher.MaxLayerCount)
            {
                Debug.LogError("segmented mesher offset count needs to be between 2 and MaxLayerCount(" + SegmentedSideMesher.MaxLayerCount + ")");
            }

            Init(distanceData, cellSize);
        }

        public void InitForOptimized(Data distanceData, float cellSize, float height, float lerpToExactEdge = 1, OptimizationMode optimizationMode = OptimizationMode.GreedyRect)
        {
            mesherInfo.Set(MesherInfo.Type.TopOnly, height, 0, lerpToExactEdge);
            mesherInfo.MakeOptimized(optimizationMode);

            Init(distanceData, cellSize);
        }

        override protected JobHandle StartGeneration(JobHandle lastHandle)
        {
            mesherInfo.hasHeightData = DistanceData.HasHeights;
            return mesherInfo.StartGeneration(lastHandle, this);
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

            uvs = new NativeList<float2>(Allocator.TempJob);
            AddTemp(uvs);

            normals = new NativeList<float3>(Allocator.TempJob);
            AddTemp(normals);

            bool generateUVs = ShouldGenerateUV && cellMesher.CanGenerateUvs;

            lastHandle = (UseCullingData && DistanceData.HasCullingData) ?
                            GenerateCornersWithCulling<InfoType, MesherType>.Schedule(ColNum, RowNum, cellMesher, DistanceData.RawData, corners, vertices, triangles, normals, generateUVs, uvs, DistanceData.HasCullingData, DistanceData.CullingDataRawData, lastHandle) :
                            GenerateCorners<InfoType, MesherType>.Schedule(ColNum, RowNum, cellMesher, DistanceData.RawData, corners, vertices, triangles, normals, generateUVs, uvs, lastHandle);

            lastHandle = (cellMesher.NeedUpdateInfo) ?
                            UpdateCorners<InfoType, MesherType>.Schedule(ColNum, RowNum, cellMesher, corners, lastHandle) :
                            lastHandle;

            JobHandle vertexHandle = (UseVertexHeightData && DistanceData.HasHeights) ?
                            CalculateVerticesWithHeight<InfoType, MesherType>.Schedule(ColNum, CellSize, cellMesher, heightDataScale, DistanceData.HeightsRawData, corners, vertices, lastHandle) :
                            CalculateVertices<InfoType, MesherType>.Schedule(ColNum, CellSize, cellMesher, corners, vertices, lastHandle);

            var trianglesHandle = CalculateTriangles<InfoType, MesherType>.Schedule(ColNum, RowNum, cellMesher, corners, triangles, lastHandle);
            
            var uvHandle = (generateUVs) ?
                            CalculateUvs<InfoType, MesherType>.Schedule(ColNum, RowNum, CellSize, uvScale, cellMesher, corners, vertices, uvs, vertexHandle) : 
                            vertexHandle;

            var normalsPreReq = JobHandle.CombineDependencies(vertexHandle, trianglesHandle);

            JobHandle normalHandle = (cellMesher.CanGenerateNormals) ?
                            CalculateNormals<InfoType, MesherType>.Schedule(ColNum, RowNum, cellMesher, corners, vertices, normals, vertexHandle) :
                            CalculateNormals.ScheduleDeferred(vertices, triangles, normals, normalsPreReq);

            vertexHandle = JobHandle.CombineDependencies(vertexHandle, uvHandle, normalHandle);
            lastHandle = JobHandle.CombineDependencies(vertexHandle, trianglesHandle);

            return lastHandle;
        }

        protected override void EndGeneration(Mesh mesh)
        {
            uint flags = MeshDataBufferFlags;
            if (uvs.Length > 0) {  flags |= (uint)MeshBuffer.UV; }

            using (MeshData data = new MeshData(vertices.Length, triangles.Length, Allocator.Temp, flags))
            {
                NativeArray<float3>.Copy(vertices, data.Vertices);
                NativeArray<int>.Copy(triangles, data.Triangles);
                NativeArray<float3>.Copy(normals, data.Normals);

                if (uvs.Length > 0) { NativeArray<float2>.Copy(uvs, data.UVs); }

                data.UpdateMesh(mesh, MeshData.UpdateMode.Clear);
            }
        }

        protected override void DisposeTemps()
        {
            base.DisposeTemps();

            vertices = default;
            triangles = default;
            uvs = default;
            normals = default;
        }
        
        private class MesherInfo
        {
            public enum Type
            {
                TopOnly,
                NoBottom,
                Full,
                FullSimple,
                SideOnly
            }

            public Type type = Type.NoBottom;
            public float height = 1;
            public bool hasHeightData = false;
            public float topOffsetScale = 0;
            public float bottomOffsetScale = 0;
            public float lerpToExactEdge = 1f;
            
            public bool optimized = false;
            public OptimizationMode optimization;

            public SideOffset[] sideOffsets;

            public void Set(Type type, float height, float bottomOffsetScale = 0, float lerpToExactEdge = 1)
            {
                this.type = type;
                this.height = height;
                this.bottomOffsetScale = bottomOffsetScale;
                this.lerpToExactEdge = lerpToExactEdge;

                sideOffsets = null;
                optimized = false;
            }

            public void MakeOptimized(OptimizationMode mode)
            {
                optimized = true;
                optimization = mode;
            }

            public JobHandle StartGeneration(JobHandle dependOn, MarchingSquaresMesher mesher)
            {
                if (optimized)
                {
                    var cellMesher = new OptimizedTopCellMesher()
                    {
                        heightOffset = height,
                        lerpToExactEdge = lerpToExactEdge,
                        optimizationMode = optimization
                    };
                    return cellMesher.StartGeneration(dependOn, mesher);
                }

                if (type == Type.FullSimple)
                {
                    var cellMesher = new SimpleFullCellMesher() { height = height, edgeLerp = lerpToExactEdge };
                    return mesher.StartGeneration<SimpleSideMesher.SideInfo, SimpleFullCellMesher>(dependOn, cellMesher);
                }

                bool isFlat = !hasHeightData;

                if (sideOffsets != null)
                {
                    var offsets = new SegmentedSideMesher.OffsetInfo();
                    for (int i = 0; i < sideOffsets.Length; ++i)
                    {
                        offsets.Set(i, sideOffsets[i].hz, sideOffsets[i].vc);
                    }
                    int layerCount = sideOffsets.Length;

                    switch (type)
                    {
                        case Type.NoBottom:
                            {
                                var cellMesher = CreateSegmentedNoBottom(layerCount, offsets, isFlat, lerpToExactEdge);
                                return cellMesher.StartGeneration(dependOn, mesher);
                            }
                        case Type.Full:
                            {
                                var cellMesher = CreateSegmentedFull(layerCount, offsets, isFlat, lerpToExactEdge);
                                return cellMesher.StartGeneration(dependOn, mesher);
                            }
                        case Type.SideOnly:
                            {
                                var cellMesher = new SegmentedSideMesher(layerCount, offsets, lerpToExactEdge);
                                return mesher.StartGeneration<SegmentedSideMesher.SegmentedSideInfo, SegmentedSideMesher>(dependOn, cellMesher);
                            }
                    }
                }
                else if (bottomOffsetScale == 0)
                {
                    switch (type)
                    {
                        case Type.TopOnly:
                            {
                                var cellMesher = new TopCellMesher(height, TopCellMesher.NormalMode.UpDontSetNormal, lerpToExactEdge);
                                return mesher.StartGeneration<TopCellMesher.TopCellInfo, TopCellMesher>(dependOn, cellMesher);
                            }
                        case Type.NoBottom:
                            {
                                var cellMesher = CreateNoBottom(height, isFlat, lerpToExactEdge);
                                return cellMesher.StartGeneration(dependOn, mesher);
                            }
                        case Type.Full:
                            {
                                var cellMesher = CreateFull(height, isFlat, lerpToExactEdge);
                                return cellMesher.StartGeneration(dependOn, mesher);
                            }
                        case Type.SideOnly:
                            {
                                var cellMesher = new SimpleSideMesher(height, lerpToExactEdge);
                                return mesher.StartGeneration<SimpleSideMesher.SideInfo, SimpleSideMesher>(dependOn, cellMesher);
                            }
                    }
                }
                else // tapered
                {
                    switch (type)
                    {
                        case Type.NoBottom:
                            {
                                var cellMesher = CreateScalableNoBottom(height, bottomOffsetScale, isFlat, lerpToExactEdge);
                                return cellMesher.StartGeneration(dependOn, mesher);
                            }
                        case Type.Full:
                            {
                                var cellMesher = CreateScalableFull(height, bottomOffsetScale, isFlat, lerpToExactEdge);
                                return cellMesher.StartGeneration(dependOn, mesher);
                            }
                        case Type.SideOnly:
                            {
                                var cellMesher = new ScalableSideMesher(height, bottomOffsetScale, topOffsetScale, lerpToExactEdge);
                                return mesher.StartGeneration<ScalableSideMesher.ScalableSideInfo, ScalableSideMesher>(dependOn, cellMesher);
                            }
                    }
                }

                var defMesher = new TopCellMesher(height, TopCellMesher.NormalMode.UpDontSetNormal, lerpToExactEdge);
                return mesher.StartGeneration<TopCellMesher.TopCellInfo, TopCellMesher>(dependOn, defMesher);
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
            public NativeList<float3> normals;

            public bool generateUVs;
            public NativeList<float2> uvs;

            public void Execute()
            {
                int nextVertex = 0;
                int nextTriangleIndex = 0;
                // the border cases are separated to avoid boundary checking
                // not sure if it's worth it...
                // inner cells
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

                GenerateBorderCells(ref nextVertex, ref nextTriangleIndex, distances, distanceColNum, distanceRowNum, corners, cellMesher);
                
                InitializeMeshData(nextVertex, nextTriangleIndex, ref vertices, ref indices, ref normals, generateUVs, ref uvs);
            }

            static public void InitializeMeshData(int vertexCount, int triangleCount, ref NativeList<float3> vertices, ref NativeList<int> indices, ref NativeList<float3> normals, bool generateUVs, ref NativeList<float2> uvs)
            {
                vertices.ResizeUninitialized(vertexCount);
                indices.ResizeUninitialized(triangleCount);
                normals.ResizeUninitialized(vertexCount);

                if (generateUVs)
                {
                    uvs.ResizeUninitialized(vertexCount);
                }
            }

            static public void GenerateBorderCells(ref int nextVertex, ref int nextTriangleIndex, NativeArray<float> distances, int distanceColNum, int distanceRowNum, NativeArray<InfoType> corners, MesherType cellMesher)
            {
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
            }

            public static JobHandle Schedule(int colNum, int rowNum, MesherType cellMesher, NativeArray<float> distances, NativeArray<InfoType> corners, NativeList<float3> vertices, NativeList<int> triangles, NativeList<float3> normals, bool generateUVs, NativeList<float2> uvs, JobHandle dependOn = default)
            {
                var cornerJob = new GenerateCorners<InfoType, MesherType>
                {
                    distanceColNum = colNum,
                    distanceRowNum = rowNum,

                    cellMesher = cellMesher,

                    distances = distances,
                    corners = corners,

                    vertices = vertices,
                    indices = triangles,
                    normals = normals,

                    generateUVs = generateUVs,
                    uvs = uvs
                };
                return cornerJob.Schedule(dependOn);
            }
        }

        // The NativeArray has to be initialized when the job is scheduled, so when there is no culling data, I would have to use a temporary array to stop the 
        // compiler from complaining.
        // Instead, I separated the two cases into different jobs, so the native array is only used when it contains culling data. Unfortunately this requires a bit
        // of code duplication.
        [BurstCompile]
        private struct GenerateCornersWithCulling<InfoType, MesherType> : IJob
            where InfoType : struct
            where MesherType : struct, ICellMesher<InfoType>
        {
            public int distanceColNum;
            public int distanceRowNum;

            public MesherType cellMesher;

            [ReadOnly] public NativeArray<float> distances;
            [WriteOnly] public NativeArray<InfoType> corners;

            public NativeList<float3> vertices;
            public NativeList<int> indices;
            public NativeList<float3> normals;

            public bool generateUVs;
            public NativeList<float2> uvs;

            public NativeArray<bool> cullingData;

            public void Execute()
            {
                int nextVertex = 0;
                int nextTriangleIndex = 0;

                // inner cells
                for (int y = 0; y < distanceRowNum - 1; ++y)
                {
                    for (int x = 0; x < distanceColNum - 1; ++x)
                    {
                        int index = y * distanceColNum + x;
                        float corner = distances[index];
                        float right = distances[index + 1];
                        float topRight = distances[index + 1 + distanceColNum];
                        float top = distances[index + distanceColNum];

                        bool hasCell = !cullingData[index];
                        corners[index] = cellMesher.GenerateInfo(corner, right, topRight, top, ref nextVertex, ref nextTriangleIndex, hasCell);
                    }
                }

                GenerateCorners<InfoType, MesherType>.GenerateBorderCells(ref nextVertex, ref nextTriangleIndex, distances, distanceColNum, distanceRowNum, corners, cellMesher);
                GenerateCorners<InfoType, MesherType>.InitializeMeshData(nextVertex, nextTriangleIndex, ref vertices, ref indices, ref normals, generateUVs, ref uvs);
            }

            public static JobHandle Schedule(int colNum, int rowNum, MesherType cellMesher, NativeArray<float> distances, NativeArray<InfoType> corners, NativeList<float3> vertices, NativeList<int> triangles, NativeList<float3> normals, bool generateUVs, NativeList<float2> uvs, bool hasCullingdata, NativeArray<bool> cullingData, JobHandle dependOn = default)
            {
                var cornerJob = new GenerateCornersWithCulling<InfoType, MesherType>
                {
                    distanceColNum = colNum,
                    distanceRowNum = rowNum,

                    cellMesher = cellMesher,

                    distances = distances,
                    corners = corners,

                    vertices = vertices,
                    indices = triangles,
                    normals = normals,

                    generateUVs = generateUVs,
                    uvs = uvs,

                    cullingData = cullingData
                };
                return cornerJob.Schedule(dependOn);
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

            public static JobHandle Schedule(int colNum, int rowNum, MesherType cellMesher, NativeArray<InfoType> corners, JobHandle dependOn)
            {
                var infoJob = new UpdateCorners<InfoType, MesherType>
                {
                    cellColNum = colNum - 1,
                    cellRowNum = rowNum - 1,
                    cellMesher = cellMesher,
                    cornerInfos = corners
                };
                return infoJob.Schedule(dependOn);
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
                cellMesher.CalculateVertices(x, y, cellSize, info, 0, vertices);
            }

            public static JobHandle Schedule(int colNum, float cellSize, MesherType cellMesher, NativeArray<InfoType> corners, NativeList<float3> vertices, JobHandle dependOn)
            {
                var vertexJob = new CalculateVertices<InfoType, MesherType>
                {
                    cornerColNum = colNum,
                    cellSize = cellSize,
                    cellMesher = cellMesher,

                    cornerInfos = corners,
                    vertices = vertices.AsDeferredJobArray()
                };
                return vertexJob.Schedule(corners.Length, CalculateVertexBatchNum, dependOn);
            }
        }

        [BurstCompile]
        private struct CalculateVerticesWithHeight<InfoType, MesherType> : IJobParallelFor
            where InfoType : struct
            where MesherType : struct, ICellMesher<InfoType>
        {
            public int cornerColNum;
            public float cellSize;

            public MesherType cellMesher;

            public float heightDataScale;
            [ReadOnly] public NativeArray<float> heights;
            [ReadOnly] public NativeArray<InfoType> cornerInfos;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float3> vertices;

            public void Execute(int index)
            {
                InfoType info = cornerInfos[index];
                int x = index % cornerColNum;
                int y = index / cornerColNum;
                cellMesher.CalculateVertices(x, y, cellSize, info, heights[index] * heightDataScale, vertices);
            }

            public static JobHandle Schedule(int colNum, float cellSize, MesherType cellMesher, float heightScale, NativeArray<float> heights, NativeArray<InfoType>corners, NativeList<float3> vertices, JobHandle dependOn)
            {
                var vertexJob = new CalculateVerticesWithHeight<InfoType, MesherType>
                {
                    cornerColNum = colNum,
                    cellSize = cellSize,
                    cellMesher = cellMesher,

                    heightDataScale = heightScale,
                    heights = heights,
                    cornerInfos = corners,
                    vertices = vertices.AsDeferredJobArray()
                };
                return vertexJob.Schedule(corners.Length, CalculateVertexBatchNum, dependOn);
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

            public static JobHandle Schedule(int colNum, int rowNum, float cellSize, float uvScale, MesherType cellMesher, NativeArray<InfoType> corners, NativeList<float3> vertices, NativeList<float2> uvs, JobHandle dependOn)
            {
                var uvJob = new CalculateUvs<InfoType, MesherType>
                {
                    cornerColNum = colNum,
                    cornerRowNum = rowNum,
                    cellSize = cellSize,
                    uvScale = uvScale,
                    cellMesher = cellMesher,

                    cornerInfos = corners,
                    vertices = vertices.AsDeferredJobArray(),
                    uvs = uvs.AsDeferredJobArray()
                };
                return uvJob.Schedule(corners.Length, CalculateVertexBatchNum, dependOn);
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
                int y = index / cornerColNum;

                InfoType right = x == (cornerColNum - 1) ? info : cornerInfos[index + 1];
                InfoType top = y == (cornerRowNum - 1) ? info : cornerInfos[index + cornerColNum];

                cellMesher.CalculateNormals(info, right, top, vertices, normals);
            }

            public static JobHandle Schedule(int colNum, int rowNum, MesherType cellMesher, NativeArray<InfoType> corners, NativeList<float3> vertices, NativeList<float3> normals, JobHandle dependOn)
            {
                var normalJob = new CalculateNormals<InfoType, MesherType>
                {
                    cornerColNum = colNum,
                    cornerRowNum = rowNum,
                    cellMesher = cellMesher,

                    cornerInfos = corners,
                    vertices = vertices.AsDeferredJobArray(),
                    normals = normals.AsDeferredJobArray()
                };
                return normalJob.Schedule(corners.Length, CalculateVertexBatchNum, dependOn);
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

            public static JobHandle Schedule(int colNum, int rowNum, MesherType cellMesher, NativeArray<InfoType> corners, NativeList<int> triangles, JobHandle dependOn)
            {
                var trianglesJob = new CalculateTriangles<InfoType, MesherType>
                {
                    cornerColNum = colNum,
                    cellMesher = cellMesher,
                    cornerInfos = corners,
                    triangles = triangles.AsDeferredJobArray()
                };
                int cellCount = (colNum - 1) * (rowNum - 1);
                return trianglesJob.Schedule(cellCount, MeshTriangleBatchNum, dependOn);
            }
        }

        public interface ICellMesher<InfoType> where InfoType : struct
        {
            InfoType GenerateInfo(float cornerDist, float rightDist, float topRightDist, float topDist, ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles);
            
            bool NeedUpdateInfo { get; }
            void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref InfoType cell, ref InfoType top, ref InfoType right);
            
            void CalculateVertices(int x, int y, float cellSize, InfoType info, float height, NativeArray<float3> vertices);
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
        private const byte MaskFull = MaskBL | MaskBR | MaskTR | MaskTL;

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
