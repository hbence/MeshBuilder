using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using System.Collections.Generic;
using System;

namespace MeshBuilder
{
    using TopCellInfo = MarchingSquaresMesher.TopCellMesher.CellInfo;
    using CellVerts = MarchingSquaresMesher.TopCellMesher.CellVertices;
    using IndexSpan = MarchingSquaresMesher.TopCellMesher.IndexSpan;

    public partial class MarchingSquaresMesher : Builder
    {
        /// <summary>
        /// Generates an XZ aligned flat mesh. The filled center cells are merged according to different triangulation methods.
        /// </summary>
        /// TODO: This needed a prepass before the corner info generation and also a bit different triangle generation
        /// so there is a lot of code duplication in StartGeneration because it can't be handled like the other ICellMeshers
        /// I should refactor them after I added chunk support
        public struct OptimizedTopCellMesher : ICellMesher<OptimizedTopCellMesher.OptimizationCornerInfo>
        {
            public struct OptimizationCornerInfo
            {
                public TopCellMesher.TopCellInfo info;

                public byte triangleCount;
                public int triA0Cell, triA1Cell, triA2Cell;
                public int triB0Cell, triB1Cell, triB2Cell;
            }

            public float lerpToExactEdge;
            public float heightOffset;
            public OptimizationMode optimizationMode;
            
            public struct OptimizationCellInfo
            {
                public bool wasChecked;
                public bool isFull;
                public bool needsVertex;
                
                public byte triangleCount;
                // these are not vertex indices, but the indicies of the 
                // corner info which holds the vertex index
                public int triA0Cell, triA1Cell, triA2Cell;
                public int triB0Cell, triB1Cell, triB2Cell;
            }

            public OptimizationCornerInfo GenerateInfo(float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                            ref int nextVertices, ref int nextTriIndex, bool hasCellTriangles)
            {
                Debug.LogError("Not implemented!");
                return default;
            }

            public OptimizationCornerInfo GenerateInfo(OptimizationCellInfo cell, float cornerDistance, float rightDistance, float topRightDistance, float topDistance,
                                            ref int nextVertex, ref int nextTriIndex, bool hasCellTriangles)
            {
                byte config = CalcConfiguration(cornerDistance, rightDistance, topRightDistance, topDistance);

                var info = new TopCellMesher.TopCellInfo
                {
                    info = new TopCellInfo(config, cornerDistance, rightDistance, topDistance),
                    verts = new CellVerts(-1, -1, -1),
                    tris = new IndexSpan(nextTriIndex, 0)
                };

                if (config == MaskFull)
                {
                    if (hasCellTriangles)
                    {
                        nextTriIndex += cell.triangleCount * 3;
                    }

                    if (cell.needsVertex)
                    {
                        info.verts.corner = nextVertex;
                        ++nextVertex;
                    }
                }
                else
                {
                    info = TopCellMesher.GenerateInfoSimple(cornerDistance, rightDistance, topRightDistance, topDistance, ref nextVertex, ref nextTriIndex, hasCellTriangles);
                }

                return new OptimizationCornerInfo
                {
                    info = info,
                    triangleCount = cell.triangleCount,
                    
                    triA0Cell = cell.triA0Cell, 
                    triA1Cell = cell.triA1Cell, 
                    triA2Cell = cell.triA2Cell,
                    
                    triB0Cell = cell.triB0Cell, 
                    triB1Cell = cell.triB1Cell, 
                    triB2Cell = cell.triB2Cell,
                };
            }

            public void CalculateVertices(int x, int y, float cellSize, OptimizationCornerInfo corner, float height, NativeArray<float3> vertices)
                => TopCellMesher.CalculateVerticesFull(x, y, cellSize, corner.info.verts, corner.info.info, height, vertices, heightOffset, lerpToExactEdge);

            public void CalculateIndices(OptimizationCornerInfo bl, OptimizationCornerInfo br, OptimizationCornerInfo tr, OptimizationCornerInfo tl, NativeArray<int> triangles)
                => Debug.LogError("Not impemented!");

            public void CalculateIndices(OptimizationCornerInfo bl, OptimizationCornerInfo br, OptimizationCornerInfo tr, OptimizationCornerInfo tl, NativeArray<int> triangles, NativeArray<OptimizationCornerInfo> corners)
            {
                int triangleIndex = bl.info.tris.start;
                byte config = bl.info.info.config;
                if (config == MaskFull)
                {
                    if (bl.triangleCount == 1)
                    {
                        triangles[triangleIndex] = Vertex(bl.triA0Cell, corners); 
                        ++triangleIndex;
                        triangles[triangleIndex] = Vertex(bl.triA1Cell, corners);
                        ++triangleIndex;
                        triangles[triangleIndex] = Vertex(bl.triA2Cell, corners); 
                    }
                    else if (bl.triangleCount == 2)
                    {
                        triangles[triangleIndex] = Vertex(bl.triA0Cell, corners); 
                        ++triangleIndex;
                        triangles[triangleIndex] = Vertex(bl.triA1Cell, corners); 
                        ++triangleIndex;
                        triangles[triangleIndex] = Vertex(bl.triA2Cell, corners); 
                        ++triangleIndex;

                        triangles[triangleIndex] = Vertex(bl.triB0Cell, corners); 
                        ++triangleIndex;
                        triangles[triangleIndex] = Vertex(bl.triB1Cell, corners); 
                        ++triangleIndex;
                        triangles[triangleIndex] = Vertex(bl.triB2Cell, corners);
                    }
                }
                else
                {
                    TopCellMesher.CalculateIndicesSimple(config, bl.info.tris, bl.info.verts, br.info.verts, tr.info.verts, tl.info.verts, triangles);
                }
            }

            public bool NeedUpdateInfo => false;

            public void UpdateInfo(int x, int y, int cellColNum, int cellRowNum, ref OptimizationCornerInfo cell, ref OptimizationCornerInfo top, ref OptimizationCornerInfo right)
            {
                // do nothing
            }

            private static int Vertex(int index, NativeArray<OptimizationCornerInfo> corners) => corners[index].info.verts.corner;

            public bool CanGenerateUvs => true;

            public void CalculateUvs(int x, int y, int cellColNum, int cellRowNum, float cellSize, OptimizationCornerInfo corner, float uvScale, NativeArray<float3> vertices, NativeArray<float2> uvs)
                => TopCellMesher.TopCalculateUvs(x, y, cellColNum, cellRowNum, cellSize, corner.info.verts, uvScale, vertices, uvs);

            public bool CanGenerateNormals => true;

            public void CalculateNormals(OptimizationCornerInfo corner, OptimizationCornerInfo right, OptimizationCornerInfo top, NativeArray<float3> vertices, NativeArray<float3> normals)
                => TopCellMesher.SetNormals(corner.info.verts, normals, new float3(0, 1, 0));
            
            public JobHandle StartGeneration(JobHandle lastHandle, MarchingSquaresMesher mesher)
            {
                int colNum = mesher.ColNum;
                int rowNum = mesher.RowNum;

                NativeArray<OptimizationCellInfo> optimizationCells = new NativeArray<OptimizationCellInfo>(colNum * rowNum, Allocator.TempJob);
                mesher.AddTemp(optimizationCells);

                NativeArray<OptimizationCornerInfo> corners = new NativeArray<OptimizationCornerInfo>(colNum * rowNum, Allocator.TempJob);
                mesher.AddTemp(corners);

                mesher.vertices = new NativeList<float3>(Allocator.TempJob);
                mesher.AddTemp(mesher.vertices);

                mesher.triangles = new NativeList<int>(Allocator.TempJob);
                mesher.AddTemp(mesher.triangles);

                mesher.uvs = new NativeList<float2>(Allocator.TempJob);
                mesher.AddTemp(mesher.uvs);

                mesher.normals = new NativeList<float3>(Allocator.TempJob);
                mesher.AddTemp(mesher.normals);

                bool generateUVs = mesher.ShouldGenerateUV && CanGenerateUvs;

                lastHandle = GenerateOptimizationData.Schedule(colNum, rowNum, optimizationMode, optimizationCells, mesher.DistanceData.RawData);
                lastHandle = GenerateOptimizedCorners.Schedule(colNum, rowNum, this, optimizationCells, mesher.DistanceData.RawData, corners, mesher.vertices, mesher.triangles, generateUVs, mesher.uvs, mesher.normals, lastHandle);
               
                var vertexHandle = CalculateVertices<OptimizationCornerInfo, OptimizedTopCellMesher>.Schedule(colNum, mesher.CellSize, this, corners, mesher.vertices, lastHandle);

                var uvHandle = vertexHandle;
                if (generateUVs)
                {
                    uvHandle = CalculateUvs<OptimizationCornerInfo, OptimizedTopCellMesher>.Schedule(colNum, rowNum, mesher.CellSize, mesher.uvScale, this, corners, mesher.vertices, mesher.uvs, vertexHandle);
                }

                var normalHandle = CalculateNormals<OptimizationCornerInfo, OptimizedTopCellMesher>.Schedule(colNum, rowNum, this, corners, mesher.vertices, mesher.normals, vertexHandle);

                vertexHandle = JobHandle.CombineDependencies(vertexHandle, uvHandle, normalHandle);

                var trianglesHandle = CalculateOptimizedTriangles.Schedule(colNum, rowNum, this, corners, mesher.triangles, lastHandle);

                return JobHandle.CombineDependencies(vertexHandle, trianglesHandle);
            }

            private struct GenerateOptimizationData : IJob
            {
                public int distanceColNum;
                public int distanceRowNum;

                public OptimizationMode mode;

                [ReadOnly] public NativeArray<float> distances;

                public NativeArray<OptimizationCellInfo> cells;

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
                            cells[index] = CreateCellInfo(corner, right, topRight, top);
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

                static public JobHandle Schedule(int colNum, int rowNum, OptimizationMode optimizationMode, NativeArray<OptimizationCellInfo> optimizationCells, NativeArray<float> distances, JobHandle dependOn = default)
                {
                    var optimizationJob = new GenerateOptimizationData
                    {
                        distanceColNum = colNum,
                        distanceRowNum = rowNum,
                        mode = optimizationMode,
                        cells = optimizationCells,
                        distances = distances
                    };
                    return optimizationJob.Schedule(dependOn);
                }

                private OptimizationCellInfo CreateCellInfo(float corner, float right, float topRight, float top)
                    => new OptimizationCellInfo
                    {
                        isFull = CalcConfiguration(corner, right, topRight, top) == MaskFull,
                        wasChecked = false,
                        needsVertex = false,
                        triangleCount = 0,
                        triA0Cell = -1, triA1Cell = -1, triA2Cell = -1,
                        triB0Cell = -1, triB1Cell = -1, triB2Cell = -1
                    };

                private bool CanStepRight(int x) => x < distanceColNum - 1;
                private bool CanStepUp(int y) => y < distanceRowNum - 1;

                struct Area
                {
                    public int startX, endX, startY, endY;
                }

                private class GreedyRect
                {
                    public static void Fill(int cornerColNum, int cornerRowNum, NativeArray<OptimizationCellInfo> cells)
                    {
                        for (int y = 0; y < cornerRowNum; ++y)
                        {
                            for (int x = 0; x < cornerColNum; ++x)
                            {
                                int index = y * cornerColNum + x;
                                var cell = cells[index];

                                if (!cell.wasChecked)
                                {
                                    if (cell.isFull)
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

                    static Area FindFullArea(int x, int y, int colNum, int rowNum, NativeArray<OptimizationCellInfo> cells)
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

                        bool IsValid(int xc, int yc) { int i = yc * colNum + xc; return cells[i].isFull && !cells[i].wasChecked; }

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

                    public static void Fill(int cornerColNum, int cornerRowNum, NativeArray<OptimizationCellInfo> cells)
                    {
                        List<int> candidates = new List<int>();

                        CellDist[] cellDist = new CellDist[cells.Length];
                        for (int i = 0; i < cellDist.Length; ++i)
                        {
                            var cell = new CellDist { dist = 0, index = i, x = i % cornerColNum, y = i / cornerColNum };
                            if (cells[i].isFull)
                            {
                                cell.dist = int.MaxValue;

                                if (IsOnEdge(i, cell.x, cell.y))
                                {
                                    cell.dist = 1;
                                    candidates.Add(i);
                                }
                            }
                            cellDist[i] = cell;
                        }

                        while (candidates.Count > 0)
                        {
                            int i = candidates[0];
                            candidates.RemoveAt(0);

                            var cell = cellDist[i];
                            int curVal = cell.dist + 1;

                            if (cell.x > 0) TestAdjacent(i - 1, curVal);
                            if (i % cornerColNum < cornerColNum - 1) TestAdjacent(i + 1, curVal);
                            if (i >= cornerColNum) TestAdjacent(i - cornerColNum, curVal);
                            if (i <= cells.Length - cornerColNum - 1) TestAdjacent(i + cornerColNum, curVal);
                        }

                        Array.Sort(cellDist, (CellDist a, CellDist b) => { return b.dist.CompareTo(a.dist); });
                        
                        for (int i = 0; i < cellDist.Length; ++i)
                        {
                            int index = cellDist[i].index;
                            if (cells[index].isFull && !cells[index].wasChecked)
                            {
                                int x = index % cornerColNum;
                                int y = index / cornerColNum;

                                Area area = FindFullAreaAround(x, y, cornerColNum, cornerRowNum, cells);
                                TriangulateArea(area, cornerColNum, cells);
                                CheckEdgeVertices(area, cornerColNum, cells);
                                MarkChecked(area, cornerColNum, cells);
                            }
                        }

                        bool IsOnEdge(int i, int x, int y)
                        {
                            if (x > 0 && !cells[i - 1].isFull) return true;
                            if (x < cornerColNum - 1 && !cells[i + 1].isFull) return true;
                            if (y > 0 && !cells[i - cornerColNum].isFull) return true;
                            if (y < cornerRowNum - 1 && !cells[i + cornerColNum].isFull) return true;
                            return false;
                        }

                        void TestAdjacent(int adj, int curVal)
                        {
                            if (cells[adj].isFull)
                            {
                                int val = cellDist[adj].dist;
                                if (val > curVal)
                                {
                                    cellDist[adj].dist = curVal;
                                    candidates.Add(adj);
                                }
                            }
                        }
                    }

                    static Area FindFullAreaAround(int x, int y, int colNum, int rowNum, NativeArray<OptimizationCellInfo> cells)
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
                                if (testX >= 0 && testX <= colNum - 1)
                                {
                                    if (IsValidVertical(testX, fromY, toY)) { posX = testX; }
                                    else { canGrow = false; }
                                }
                                else
                                {
                                    canGrow = false;
                                }
                            }
                        }
                        void GrowVertical(ref bool canGrow, ref int posY, int dir, int fromX, int toX)
                        {
                            if (canGrow)
                            {
                                int testY = posY + dir;
                                if (testY >= 0 && testY <= rowNum - 1)
                                if (IsValidHorizontal(testY, fromX, toX)) { posY = testY; }
                                else { canGrow = false; }
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

                        bool IsValid(int xc, int yc) { int i = yc * colNum + xc; return cells[i].isFull && !cells[i].wasChecked; }

                        return new Area { startX = startX, endX = endX, startY = startY, endY = endY };
                    }
                }

                static void TriangulateArea(Area area, int colNum, NativeArray<OptimizationCellInfo> cells)
                {
                    ApplySimpleFullTriangles(area, colNum, cells);
                }

                static void MarkChecked(Area area, int colNum, NativeArray<OptimizationCellInfo> cells)
                {
                    for (int y = area.startY; y <= area.endY; ++y)
                    {
                        for (int x = area.startX; x <= area.endX; ++x)
                        {
                            MarkChecked(x, y, colNum, cells);
                        }
                    }
                }

                static void CheckEdgeVertices(Area area, int colNum, NativeArray<OptimizationCellInfo> cells)
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
                        return !cells[left].isFull || !cells[bottom].isFull || !cells[bottomLeft].isFull;
                    }
                }

                static void MarkChecked(int x, int y, int colNum, NativeArray<OptimizationCellInfo> cells)
                {
                    int index = ToIndex(x, y, colNum);
                    var cell = cells[index];
                    cell.wasChecked = true;
                    cells[index] = cell;
                }

                static private int ToIndex(int x, int y, int colNum) => y * colNum + x;

                static void ApplySimpleFullTriangles(Area area, int colNum, NativeArray<OptimizationCellInfo> cells)
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

                static private void UpdateCell(int index, NativeArray<OptimizationCellInfo> cells, bool needsVertex)
                {
                    var cell = cells[index];
                    cell.needsVertex = needsVertex;
                    cells[index] = cell;
                }

                static private void UpdateCell(int index, NativeArray<OptimizationCellInfo> cells, bool needsVertex,
                    byte triangleCount = 0, int a0 = -1, int a1 = -1, int a2 = -1, int b0 = -1, int b1 = -1, int b2 = -1)
                {
                    var cell = cells[index];
                    cell.needsVertex = needsVertex;
                    cell.triangleCount = triangleCount;
                    cell.triA0Cell = a0;
                    cell.triA1Cell = a1;
                    cell.triA2Cell = a2;
                    cell.triB0Cell = b0;
                    cell.triB1Cell = b1;
                    cell.triB2Cell = b2;
                    cells[index] = cell;
                }
            }

            [BurstCompile]
            private struct GenerateOptimizedCorners : IJob
            {
                private const bool HasCellTriangles = true;
                private const bool NoCellTriangles = false;

                public int distanceColNum;
                public int distanceRowNum;

                public OptimizedTopCellMesher cellMesher;

                [ReadOnly] public NativeArray<OptimizationCellInfo> optimizationCells;
                [ReadOnly] public NativeArray<float> distances;

                [WriteOnly] public NativeArray<OptimizationCornerInfo> corners;

                public NativeList<float3> vertices;
                public NativeList<int> indices;

                public bool generateUVs;
                public NativeList<float2> uvs;
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
                            var cell = optimizationCells[index];
                            corners[index] = cellMesher.GenerateInfo(cell, corner, right, topRight, top, ref nextVertex, ref nextTriangleIndex, HasCellTriangles);
                        }
                    }
                    // top border
                    for (int x = 0, y = distanceRowNum - 1; x < distanceColNum - 1; ++x)
                    {
                        int index = y * distanceColNum + x;
                        float corner = distances[index];
                        float right = distances[index + 1];
                        var cell = optimizationCells[index];
                        corners[index] = cellMesher.GenerateInfo(cell, corner, right, -1, -1, ref nextVertex, ref nextTriangleIndex, NoCellTriangles);
                    }
                    // right border
                    for (int x = distanceColNum - 1, y = 0; y < distanceRowNum - 1; ++y)
                    {
                        int index = y * distanceColNum + x;
                        float corner = distances[index];
                        float top = distances[index + distanceColNum];
                        var cell = optimizationCells[index];
                        corners[index] = cellMesher.GenerateInfo(cell, corner, -1, -1, top, ref nextVertex, ref nextTriangleIndex, NoCellTriangles);
                    }
                    // top right corner
                    int last = distanceColNum * distanceRowNum - 1;
                    corners[last] = cellMesher.GenerateInfo(optimizationCells[last], distances[last], -1, -1, -1, ref nextVertex, ref nextTriangleIndex, NoCellTriangles);

                    vertices.ResizeUninitialized(nextVertex);
                    indices.ResizeUninitialized(nextTriangleIndex);
                    normals.ResizeUninitialized(nextVertex);

                    if (generateUVs)
                    {
                        uvs.ResizeUninitialized(nextVertex);
                    }
                }

                static public JobHandle Schedule(int colNum, int rowNum, OptimizedTopCellMesher cellMesher, NativeArray<OptimizationCellInfo> optimizationCells, NativeArray<float> distances, NativeArray<OptimizationCornerInfo> corners, NativeList<float3> vertices,
                    NativeList<int> triangles, bool generateUVs, NativeList<float2> uvs, NativeList<float3> normals, JobHandle dependOn)
                {
                    var cornerJob = new GenerateOptimizedCorners
                    {
                        distanceColNum = colNum,
                        distanceRowNum = rowNum,

                        cellMesher = cellMesher,

                        optimizationCells = optimizationCells,
                        distances = distances,
                        corners = corners,

                        vertices = vertices,
                        indices = triangles,

                        generateUVs = generateUVs,
                        uvs = uvs,
                        normals = normals
                    };
                    return cornerJob.Schedule(dependOn);
                }
            }

            [BurstCompile]
            private struct CalculateOptimizedTriangles : IJobParallelFor
            {
                public int cornerColNum;

                public OptimizedTopCellMesher cellMesher;

                [NativeDisableParallelForRestriction] [ReadOnly] public NativeArray<OptimizationCornerInfo> cornerInfos;
                [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<int> triangles;

                public void Execute(int index)
                {
                    int cellColNum = cornerColNum - 1;
                    int x = index % cellColNum;
                    int y = index / cellColNum;
                    index = y * cornerColNum + x;

                    var bl = cornerInfos[index];
                    var br = cornerInfos[index + 1];
                    var tr = cornerInfos[index + 1 + cornerColNum];
                    var tl = cornerInfos[index + cornerColNum];
                    cellMesher.CalculateIndices(bl, br, tr, tl, triangles, cornerInfos);
                }

                public static JobHandle Schedule(int colNum, int rowNum, OptimizedTopCellMesher cellMesher, NativeArray<OptimizationCornerInfo> corners, NativeList<int> triangles, JobHandle dependOn)
                {
                    var trianglesJob = new CalculateOptimizedTriangles
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
        }
    }
}
