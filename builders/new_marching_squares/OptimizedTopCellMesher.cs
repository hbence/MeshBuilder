using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

using static MeshBuilder.New.TopCellMesher;
using static MeshBuilder.New.ScaledTopCellMesher;
using Data = MeshBuilder.MarchingSquaresMesher.Data;

namespace MeshBuilder.New
{
    public class OptimizedTopCellMesher : CellMesher
    {
        public enum OptimizationMode
        {
            GreedyRect,
            NextLargestRect
        }

        public class OptimizedInfo : ScaledInfo
        {
            public OptimizationMode OptimizationMode = OptimizationMode.GreedyRect;
        }

        private float cellSize;
        private Data data;
        private OptimizedInfo info;

        public void Init(Data data, float cellSize, OptimizedInfo info)
        {
            this.data = data;
            this.cellSize = cellSize;
            this.info = info;

            CheckData(data, info);

            Inited();
        }

        protected override JobHandle StartGeneration(JobHandle lastHandle = default)
        {
            Dispose();
            CheckData(data, info);

            CreateMeshData(info.GenerateNormals, info.GenerateUvs);

            bool useCullingData = info.UseCullingData && data.HasCullingData;
            bool useHeightData = info.UseHeightData && data.HasHeights;
            bool needsEdgeNormalData = info.ScaledOffset > 0;

            var mergeInfoArray = new NativeArray<MergeCellInfo>(data.RawData.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(mergeInfoArray);

            lastHandle = ScheduleGenerateOptimizationData(data, info, useCullingData, useHeightData, mergeInfoArray, lastHandle);

            var infoArray = new NativeArray<TopCellInfo>(data.RawData.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(infoArray);

            lastHandle = ScheduleCalculateInfoJob(data, info, useCullingData, infoArray, mergeInfoArray, vertices, triangles, normals, uvs, lastHandle);

            NativeArray<EdgeNormals> edgeNormalsArray = default;
            if (needsEdgeNormalData)
            {
                edgeNormalsArray = new NativeArray<EdgeNormals>(data.RawData.Length, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                AddTemp(edgeNormalsArray);

                lastHandle = ScheduleEdgeNormalsJob(new TopCellEdgeNormalCalculator(), data.ColNum, data.RowNum, infoArray, edgeNormalsArray, info.LerpToExactEdge, lastHandle);
            }

            JobHandle vertexHandle = ScheduleCalculateVerticesJob(data, info, useHeightData, cellSize, infoArray, vertices, edgeNormalsArray, lastHandle);

            if (info.GenerateUvs)
            {
                vertexHandle = ScheduleCalculateUVJob(data, info, cellSize, infoArray, vertices, uvs, vertexHandle);
            }

            JobHandle triangleHandle = ScheduleCalculateTrianglesJob(data.ColNum, data.RowNum, infoArray, mergeInfoArray, triangles, info.IsFlipped, lastHandle);

            lastHandle = JobHandle.CombineDependencies(vertexHandle, triangleHandle);

            if (info.GenerateNormals && useHeightData)
            {
                lastHandle = CalculateNormals.ScheduleDeferred(vertices, triangles, normals, lastHandle);
            }

            return lastHandle;
        }

        private struct OptimizedTopCellInfoGenerator : InfoGenerator<TopCellInfo>
        {
            [ReadOnly] public NativeArray<MergeCellInfo> mergeInfoArray;

            public TopCellInfo GenerateInfoWithTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => GenerateInfo(mergeInfoArray, index, cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex, true);

            public TopCellInfo GenerateInfoNoTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => GenerateInfo(mergeInfoArray, index, cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex, false);

            static public TopCellInfo GenerateInfo(NativeArray<MergeCellInfo> mergeInfoArray, int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex, bool hasTriangle)
            {
                MergeCellInfo mergeCell = mergeInfoArray[index];
                MergeTriInfo mergeInfo = mergeCell.mergedInfo;
                if (mergeCell.canBeMerged)
                {
                    byte indexLength = (byte)(mergeInfo.triangleCount * 3);

                    byte config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance);
                    var cellInfo = new TopCellInfo()
                    {
                        info = new CellInfo() { config = config, cornerDist = cornerDistance, rightDist = rightDistance, topDist = topDistance },
                        verts = new CellVertices { corner = -1, leftEdge = -1, bottomEdge = -1 },
                        tris = new IndexSpan(nextTriIndex, indexLength)
                    };

                    if (hasTriangle)
                    {
                        nextTriIndex += indexLength;
                    }

                    if (mergeCell.needsVertex)
                    {
                        cellInfo.verts.corner = nextVertex;
                        ++nextVertex;
                    }

                    return cellInfo;
                }
                else
                {
                    return hasTriangle ?
                            TopCellMesher.GenerateInfoWithTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex) :
                            TopCellMesher.GenerateInfoNoTriangles(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex);
                }
            }
        }

        private struct CulledOptimizedTopCellInfoGenerator : InfoGenerator<TopCellInfo>
        {
            [ReadOnly] public NativeArray<bool> culledArray;
            [ReadOnly] public NativeArray<MergeCellInfo> mergeInfoArray;

            public TopCellInfo GenerateInfoWithTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => OptimizedTopCellInfoGenerator.GenerateInfo(mergeInfoArray, index, cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex, !culledArray[index]);

            public TopCellInfo GenerateInfoNoTriangles(int index, float cornerDistance, float rightDistance, float topRightDistance, float topDistance, ref int nextVertex, ref int nextTriIndex)
                => OptimizedTopCellInfoGenerator.GenerateInfo(mergeInfoArray, index, cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex, false);
        }

        static private JobHandle ScheduleCalculateInfoJob(Data data, Info info, bool useCullingData, NativeArray<TopCellInfo> infoArray, NativeArray<MergeCellInfo> mergeInfoArray, NativeList<float3> vertices, NativeList<int> triangles, NativeList<float3> normals, NativeList<float2> uvs, JobHandle lastHandle = default)
        {
            float3 normal = info.IsFlipped ? new float3(0, -1, 0) : new float3(0, 1, 0);
            if (useCullingData)
            {
                var generator = new CulledOptimizedTopCellInfoGenerator();
                generator.mergeInfoArray = mergeInfoArray;
                generator.culledArray = data.CullingDataRawData;
                lastHandle = CalculateInfoJob<CulledOptimizedTopCellInfoGenerator, TopCellInfo>.Schedule(generator, data, vertices, triangles, info.GenerateNormals, normal, normals, info.GenerateUvs, uvs, infoArray, lastHandle);
            }
            else
            {
                var generator = new OptimizedTopCellInfoGenerator();
                generator.mergeInfoArray = mergeInfoArray;
                lastHandle = CalculateInfoJob<OptimizedTopCellInfoGenerator, TopCellInfo>.Schedule(generator, data, vertices, triangles, info.GenerateNormals, normal, normals, info.GenerateUvs, uvs, infoArray, lastHandle);
            }

            return lastHandle;
        }

        [BurstCompile]
        private struct CalculateTrianglesJob<Orderer> : IJobParallelFor
            where Orderer : struct, ITriangleOrderer
        {
            public int cornerColNum;
            public Orderer orderer;

            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<TopCellInfo> infoArray;
            [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<MergeCellInfo> mergeInfoArray;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<int> triangles;

            public void Execute(int index)
            {
                int cellColNum = cornerColNum - 1;
                int x = index % cellColNum;
                int y = index / cellColNum;
                index = y * cornerColNum + x;

                TopCellInfo bl = infoArray[index];
                TopCellInfo br = infoArray[index + 1];
                TopCellInfo tr = infoArray[index + 1 + cornerColNum];
                TopCellInfo tl = infoArray[index + cornerColNum];
                CalculateIndices(orderer, bl.info.config, bl.tris.start, mergeInfoArray[index].mergedInfo, bl.tris, bl.verts, br.verts, tr.verts, tl.verts);
            }

            public void CalculateIndices(Orderer orderer, byte config, int triangleIndex, MergeTriInfo mergeInfo, IndexSpan tris, CellVertices bl, CellVertices br, CellVertices tr, CellVertices tl)
            {
                if (mergeInfo.triangleCount > 0)
                {
                    if (mergeInfo.triangleCount == 1)
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, VertexIndexA0(mergeInfo), VertexIndexA1(mergeInfo), VertexIndexA2(mergeInfo));
                    }
                    else if (mergeInfo.triangleCount == 2)
                    {
                        orderer.AddTriangle(triangles, ref triangleIndex, VertexIndexA0(mergeInfo), VertexIndexA1(mergeInfo), VertexIndexA2(mergeInfo));
                        orderer.AddTriangle(triangles, ref triangleIndex, VertexIndexB0(mergeInfo), VertexIndexB1(mergeInfo), VertexIndexB2(mergeInfo));
                    }
                }
                else
                {
                    TopCellMesher.CalculateIndices(orderer, config, triangleIndex, tris, bl, br, tr, tl, triangles);
                }
            }

            private int VertexIndexA0(MergeTriInfo tri) => infoArray[tri.triA0Cell].verts.corner;
            private int VertexIndexA1(MergeTriInfo tri) => infoArray[tri.triA1Cell].verts.corner;
            private int VertexIndexA2(MergeTriInfo tri) => infoArray[tri.triA2Cell].verts.corner;

            private int VertexIndexB0(MergeTriInfo tri) => infoArray[tri.triB0Cell].verts.corner;
            private int VertexIndexB1(MergeTriInfo tri) => infoArray[tri.triB1Cell].verts.corner;
            private int VertexIndexB2(MergeTriInfo tri) => infoArray[tri.triB2Cell].verts.corner;

            public static JobHandle Schedule(int colNum, int rowNum, NativeArray<TopCellInfo> infoArray, NativeArray<MergeCellInfo> mergeInfo, NativeList<int> triangles, JobHandle dependOn)
            {
                int cellCount = (colNum - 1) * (rowNum - 1);
                var trianglesJob = new CalculateTrianglesJob<Orderer>
                {
                    cornerColNum = colNum,
                    orderer = new Orderer(),
                    infoArray = infoArray,
                    mergeInfoArray = mergeInfo,
                    triangles = triangles.AsDeferredJobArray()
                };
                return trianglesJob.Schedule(cellCount, MeshTriangleBatchNum, dependOn);
            }
        }

        private static JobHandle ScheduleCalculateTrianglesJob(int colNum, int rowNum, NativeArray<TopCellInfo> infoArray, NativeArray<MergeCellInfo> mergeInfo, NativeList<int> triangles, bool isFlipped, JobHandle dependOn)
            => isFlipped ?
                 CalculateTrianglesJob<ReverseTriangleOrderer>.Schedule(colNum, rowNum, infoArray, mergeInfo, triangles, dependOn) :
                 CalculateTrianglesJob<TriangleOrderer>.Schedule(colNum, rowNum, infoArray, mergeInfo, triangles, dependOn);

        private struct MergeCellInfo
        {
            public bool canBeMerged;
            public bool wasChecked;
            public bool needsVertex;

            public MergeTriInfo mergedInfo;
        }

        private interface IMergeChecker
        {
            bool CanBeMerged(int index, byte cellConfig);
        }

        private struct SimpleMergeChecker : IMergeChecker
        {
            public bool CanBeMerged(int index, byte cellConfig) => cellConfig == MaskFull;
        }

        private struct HeightMergeChecker : IMergeChecker
        {
            [ReadOnly] public NativeArray<float> heights;
            public int colNum;
            public bool CanBeMerged(int index, byte cellConfig)
            {
                if (cellConfig == MaskFull)
                {
                    return AreHeightsEqual(index, colNum, heights);
                }
                return false;
            }

            public static bool AreHeightsEqual(int index, int colNum, NativeArray<float> heights)
            {
                int x = index % colNum;
                bool hasRight = x < colNum - 1;
                bool hasTop = index + colNum < heights.Length - 1;

                if (hasRight && !EqualHeight(index, index + 1, heights)) { return false; }
                if (hasTop && !EqualHeight(index, index + colNum, heights)) { return false; }
                if (hasRight && hasTop && !EqualHeight(index, index + colNum + 1, heights)) { return false; }

                return true;
            }

            private static bool EqualHeight(int a, int b, NativeArray<float> heights) => heights[a] == heights[b];
        }

        private struct CulledMergeChecker : IMergeChecker
        {
            [ReadOnly] public NativeArray<bool> culled;

            public bool CanBeMerged(int index, byte cellConfig) => cellConfig == MaskFull && !culled[index];
        }

        private struct CulledHeightMergeChecker : IMergeChecker
        {
            [ReadOnly] public NativeArray<bool> culled;
            [ReadOnly] public NativeArray<float> heights;
            public int colNum;
            public bool CanBeMerged(int index, byte cellConfig)
            {
                if (cellConfig == MaskFull && !culled[index])
                {
                    return HeightMergeChecker.AreHeightsEqual(index, colNum, heights);
                }
                return false;
            }
        }

        private struct MergeTriInfo
        {
            public byte triangleCount;
            // these are not vertex indices, but the indicies of the 
            // corner info which holds the vertex index
            public int triA0Cell, triA1Cell, triA2Cell;
            public int triB0Cell, triB1Cell, triB2Cell;

            public MergeTriInfo(byte triCount, int a0, int a1, int a2, int b0, int b1, int b2)
            {
                triangleCount = triCount;
                triA0Cell = a0;
                triA1Cell = a1;
                triA2Cell = a2;
                triB0Cell = b0;
                triB1Cell = b1;
                triB2Cell = b2;
            }
        }

        static private JobHandle ScheduleGenerateOptimizationData(Data data, OptimizedInfo info, bool useCullingData, bool useHeightData, NativeArray<MergeCellInfo> mergeInfoArray, JobHandle lastHandle)
        {
            if (useCullingData && useHeightData)
            {
                var checker = new CulledHeightMergeChecker() { heights = data.HeightsRawData, colNum = data.ColNum, culled = data.CullingDataRawData };
                return ScheduleGenerateOptimizationData(data, info, checker, mergeInfoArray, lastHandle);
            }
            else if (useCullingData)
            {
                var checker = new CulledMergeChecker() { culled = data.CullingDataRawData };
                return ScheduleGenerateOptimizationData(data, info, checker, mergeInfoArray, lastHandle);

            }
            else if (useHeightData)
            {
                var checker = new HeightMergeChecker() { heights = data.HeightsRawData, colNum = data.ColNum };
                return ScheduleGenerateOptimizationData(data, info, checker, mergeInfoArray, lastHandle);
            }
            else
            {
                return ScheduleGenerateOptimizationData(data, info, new SimpleMergeChecker(), mergeInfoArray, lastHandle);
            }
        }

        static private JobHandle ScheduleGenerateOptimizationData<MergeChecker>(Data data, OptimizedInfo info, MergeChecker checker, NativeArray<MergeCellInfo> mergeInfoArray, JobHandle lastHandle)
            where MergeChecker : struct, IMergeChecker
            => GenerateOptimizationData<MergeChecker>.Schedule(data, info, checker, mergeInfoArray, lastHandle);

        private struct GenerateOptimizationData<MergeChecker> : IJob
            where MergeChecker : struct, IMergeChecker
        {
            public int distanceColNum;
            public int distanceRowNum;

            public OptimizationMode mode;
            public MergeChecker mergeChecker;

            [ReadOnly] public NativeArray<float> distances;

            public NativeArray<MergeCellInfo> cells;

            public void Execute()
            {
                for (int y = 0; y < distanceRowNum; ++y)
                {
                    for (int x = 0; x < distanceColNum; ++x)
                    {
                        int index = y * distanceColNum + x;
                        float corner = distances[index];
                        float right = CanStepRight(x) ? distances[index + 1] : -1;
                        float topRight = CanStepRight(x) && CanStepUp(y) ? distances[index + 1 + distanceColNum] : -1;
                        float top = CanStepUp(y) ? distances[index + distanceColNum] : -1;
                        cells[index] = CreateCellInfo(index, corner, right, topRight, top, mergeChecker);
                    }
                }

                if (mode == OptimizationMode.GreedyRect)
                {
                    GreedyRect.Fill(distanceColNum, distanceRowNum, cells);
                }
                else if (mode == OptimizationMode.NextLargestRect)
                {
                    NextLargest.Fill(distanceColNum, distanceRowNum, cells);
                }
                else
                {
                    GreedyRect.Fill(distanceColNum, distanceRowNum, cells);
                    Debug.LogError("Unhandled optimization mode!");
                }
            }

            static public JobHandle Schedule(Data data, OptimizedInfo info, MergeChecker mergeChecker, NativeArray<MergeCellInfo> optimizationCells, JobHandle dependOn = default)
            {
                var optimizationJob = new GenerateOptimizationData<MergeChecker>
                {
                    distanceColNum = data.ColNum,
                    distanceRowNum = data.RowNum,
                    mode = info.OptimizationMode,
                    mergeChecker = mergeChecker,
                    cells = optimizationCells,
                    distances = data.RawData
                };
                return optimizationJob.Schedule(dependOn);
            }

            private MergeCellInfo CreateCellInfo(int index, float corner, float right, float topRight, float top, MergeChecker mergeChecker)
                => new MergeCellInfo
                {
                    canBeMerged = mergeChecker.CanBeMerged(index, CalcConfiguration(corner, right, topRight, top)),
                    wasChecked = false,
                    needsVertex = false,
                    mergedInfo = new MergeTriInfo(0, -1, -1, -1, -1, -1, -1)
                };

            private bool CanStepRight(int x) => x < distanceColNum - 1;
            private bool CanStepUp(int y) => y < distanceRowNum - 1;

            struct Area
            {
                public int startX, endX, startY, endY;
            }

            private class GreedyRect
            {
                public static void Fill(int cornerColNum, int cornerRowNum, NativeArray<MergeCellInfo> cells)
                {
                    for (int y = 0; y < cornerRowNum; ++y)
                    {
                        for (int x = 0; x < cornerColNum; ++x)
                        {
                            int index = y * cornerColNum + x;
                            var cell = cells[index];

                            if (!cell.wasChecked)
                            {
                                if (cell.canBeMerged)
                                {
                                    Area area = FindFullArea(x, y, cornerColNum, cornerRowNum, cells);
                                    TriangulateArea(area, cornerColNum, cells);
                                    CheckEdgeVertices(area, cornerColNum, cells);
                                    MarkChecked(area, cornerColNum, cells);
                                }
                                else
                                {
                                    MarkChecked(x, y, cornerColNum, cells);
                                }
                            }
                        }
                    }
                }

                static Area FindFullArea(int x, int y, int colNum, int rowNum, NativeArray<MergeCellInfo> cells)
                {
                    int width = 0;
                    int height = 0;

                    int maxWidth = colNum - x - 1;
                    int maxHeight = rowNum - y - 1;

                    bool growWidth = true;
                    bool growHeight = true;

                    while (width < maxWidth && height < maxHeight && (growWidth || growHeight))
                    {
                        if (growWidth)
                        {
                            int testWidth = width + 1;
                            bool allow = true;
                            for (int testY = y; testY <= y + height; ++testY)
                            {
                                if (!IsValid(x + testWidth, testY))
                                {
                                    allow = false;
                                    break;
                                }
                            }
                            if (allow)
                            {
                                width = testWidth;
                            }
                            else
                            {
                                growWidth = false;
                            }
                        }
                        if (growHeight)
                        {
                            int testHeight = height + 1;
                            bool allow = true;
                            for (int testX = x; testX <= x + width; ++testX)
                            {
                                if (!IsValid(testX, y + testHeight))
                                {
                                    allow = false;
                                    break;
                                }
                            }
                            if (allow)
                            {
                                height = testHeight;
                            }
                            else
                            {
                                growHeight = false;
                            }
                        }
                    }

                    bool IsValid(int xc, int yc) { int i = yc * colNum + xc; return cells[i].canBeMerged && !cells[i].wasChecked; }

                    return new Area { startX = x, endX = x + width, startY = y, endY = y + height };
                }
            }

            private class NextLargest
            {
                private class CellDist
                {
                    public int index;
                    public int x;
                    public int y;
                    public int dist;
                }

                public static void Fill(int cornerColNum, int cornerRowNum, NativeArray<MergeCellInfo> cells)
                {
                    Queue<int> candidates = new Queue<int>();

                    CellDist[] cellDist = new CellDist[cells.Length];
                    for (int i = 0; i < cellDist.Length; ++i)
                    {
                        var cell = new CellDist { dist = 0, index = i, x = i % cornerColNum, y = i / cornerColNum };
                        if (cells[i].canBeMerged)
                        {
                            cell.dist = Mathf.Min(cell.x, cell.y, cornerColNum - cell.x - 1, cornerRowNum - cell.y - 1);

                            if (IsOnEdge(i, cell.x, cell.y))
                            {
                                cell.dist = 1;
                                candidates.Enqueue(i);
                            }
                        }
                        cellDist[i] = cell;
                    }

                    while (candidates.Count > 0)
                    {
                        int i = candidates.Dequeue();

                        var cell = cellDist[i];
                        int curVal = cell.dist + 1;

                        if (cell.x > 0) ProcessAdjacent(i - 1, curVal);
                        if (cell.x < cornerColNum - 1) ProcessAdjacent(i + 1, curVal);
                        if (cell.y > 0) ProcessAdjacent(i - cornerColNum, curVal);
                        if (cell.y < cornerRowNum - 1) ProcessAdjacent(i + cornerColNum, curVal);
                    }

                    Array.Sort(cellDist, (CellDist a, CellDist b) => { return b.dist.CompareTo(a.dist); });

                    for (int i = 0; i < cellDist.Length; ++i)
                    {
                        CellDist distInfo = cellDist[i];
                        if (cells[distInfo.index].canBeMerged && !cells[distInfo.index].wasChecked)
                        {
                            Area area = FindFullAreaAround(distInfo.x, distInfo.y, cornerColNum, cornerRowNum, cells);
                            TriangulateArea(area, cornerColNum, cells);
                            CheckEdgeVertices(area, cornerColNum, cells);
                            MarkChecked(area, cornerColNum, cells);
                        }
                    }

                    bool IsOnEdge(int i, int x, int y)
                    {
                        return (x > 0 && !cells[i - 1].canBeMerged) || (x < cornerColNum - 1 && !cells[i + 1].canBeMerged) ||
                                 (y > 0 && !cells[i - cornerColNum].canBeMerged) || (y < cornerRowNum - 1 && !cells[i + cornerColNum].canBeMerged);
                    }

                    void ProcessAdjacent(int adj, int curVal)
                    {
                        if (cells[adj].canBeMerged && cellDist[adj].dist > curVal)
                        {
                            cellDist[adj].dist = curVal;
                            candidates.Enqueue(adj);
                        }
                    }
                }

                static Area FindFullAreaAround(int x, int y, int colNum, int rowNum, NativeArray<MergeCellInfo> cells)
                {
                    int startX = x;
                    int startY = y;
                    int endX = x;
                    int endY = y;

                    bool growLeft = true;
                    bool growRight = true;
                    bool growUp = true;
                    bool growDown = true;

                    while (growLeft || growRight || growUp || growDown)
                    {
                        GrowHorizontal(ref growRight, ref endX, 1, startY, endY);
                        GrowVertical(ref growUp, ref endY, 1, startX, endX);
                        GrowHorizontal(ref growLeft, ref startX, -1, startY, endY);
                        GrowVertical(ref growDown, ref startY, -1, startX, endX);
                    }

                    void GrowHorizontal(ref bool canGrow, ref int posX, int dir, int fromY, int toY)
                    {
                        if (canGrow)
                        {
                            int testX = posX + dir;
                            canGrow = testX >= 0 && testX <= colNum - 1 && IsValidVertical(testX, fromY, toY);
                            posX = canGrow ? testX : posX;
                        }
                    }
                    void GrowVertical(ref bool canGrow, ref int posY, int dir, int fromX, int toX)
                    {
                        if (canGrow)
                        {
                            int testY = posY + dir;
                            canGrow = testY >= 0 && testY <= rowNum - 1 && IsValidHorizontal(testY, fromX, toX);
                            posY = canGrow ? testY : posY;
                        }
                    }
                    bool IsValidVertical(int testX, int fromY, int toY)
                    {
                        for (int testY = fromY; testY <= toY; ++testY) { if (!IsValid(testX, testY)) { return false; } }
                        return true;
                    }
                    bool IsValidHorizontal(int testY, int fromX, int toX)
                    {
                        for (int testX = fromX; testX <= toX; ++testX) { if (!IsValid(testX, testY)) { return false; } }
                        return true;
                    }

                    bool IsValid(int xc, int yc) { int i = yc * colNum + xc; return cells[i].canBeMerged && !cells[i].wasChecked; }

                    return new Area { startX = startX, endX = endX, startY = startY, endY = endY };
                }
            }

            static void TriangulateArea(Area area, int colNum, NativeArray<MergeCellInfo> cells)
            {
                ApplySimpleFullTriangles(area, colNum, cells);
            }

            static void MarkChecked(Area area, int colNum, NativeArray<MergeCellInfo> cells)
            {
                for (int y = area.startY; y <= area.endY; ++y)
                {
                    for (int x = area.startX; x <= area.endX; ++x)
                    {
                        MarkChecked(x, y, colNum, cells);
                    }
                }
            }

            static void CheckEdgeVertices(Area area, int colNum, NativeArray<MergeCellInfo> cells)
            {
                if (area.startY > 0)
                {
                    for (int x = Mathf.Max(area.startX, 1); x <= area.endX; ++x)
                    {
                        int index = ToIndex(x, area.startY, colNum);
                        if (NeedVertex(index))
                        {
                            UpdateCell(index, cells, true);
                        }
                    }
                }
                if (area.startX > 0)
                {
                    for (int y = Mathf.Max(area.startY, 1); y <= area.endY; ++y)
                    {
                        int index = ToIndex(area.startX, y, colNum);
                        if (NeedVertex(index))
                        {
                            UpdateCell(index, cells, true);
                        }
                    }
                }

                bool NeedVertex(int index)
                {
                    int left = index - 1;
                    int bottom = index - colNum;
                    int bottomLeft = index - colNum - 1;
                    return !cells[left].canBeMerged || !cells[bottom].canBeMerged || !cells[bottomLeft].canBeMerged;
                }
            }

            static void MarkChecked(int x, int y, int colNum, NativeArray<MergeCellInfo> cells)
            {
                int index = ToIndex(x, y, colNum);
                var cell = cells[index];
                cell.wasChecked = true;
                cells[index] = cell;
            }

            static private int ToIndex(int x, int y, int colNum) => y * colNum + x;

            static void ApplySimpleFullTriangles(Area area, int colNum, NativeArray<MergeCellInfo> cells)
            {
                int blIndex = ToIndex(area.startX, area.startY, colNum);
                int brIndex = ToIndex(area.endX + 1, area.startY, colNum);
                int tlIndex = ToIndex(area.startX, area.endY + 1, colNum);
                int trIndex = ToIndex(area.endX + 1, area.endY + 1, colNum);

                UpdateCell(blIndex, cells, true, 2, blIndex, tlIndex, trIndex, blIndex, trIndex, brIndex);
                UpdateCell(brIndex, cells, true);
                UpdateCell(tlIndex, cells, true);
                UpdateCell(trIndex, cells, true);
            }

            static private void UpdateCell(int index, NativeArray<MergeCellInfo> cells, bool needsVertex)
            {
                var cell = cells[index];
                cell.needsVertex = needsVertex;
                cells[index] = cell;
            }

            static private void UpdateCell(int index, NativeArray<MergeCellInfo> cells, bool needsVertex,
                byte triangleCount, int a0, int a1, int a2, int b0, int b1, int b2)
            {
                var cell = cells[index];
                cell.needsVertex = needsVertex;
                cell.mergedInfo = new MergeTriInfo(triangleCount, a0, a1, a2, b0, b1, b2);
                cells[index] = cell;
            }
        }
    }
}
