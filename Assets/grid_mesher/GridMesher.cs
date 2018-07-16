using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

using DataVolume = MeshBuilder.Volume<byte>;

namespace MeshBuilder
{
    /// <summary>
    /// GridMesher
    /// Generates a grid mesh based on 3d volume data (top part for now)
    /// 
    /// TODO:
    /// - it only generates a top part it should be able to handle any direction
    /// - it should be able to handle multiple materials
    /// - it would be nice if it could generate heightmap lerp values 
    /// so the sides would be always at 0 and scale towards the heightmap value
    /// 
    /// NOTES:
    /// - normalize uv and offset position is an additional step for now, it's modular but it would be probably fater
    /// to do it in the mesh generation phase
    /// - the mesh generation generates every vertex in each cell, it would be perhaps faster to generate 
    /// one cell (for every scenario: no adjacent, bottom adjacent, left adjacent, both adjacent), then just copy that
    /// with offsets and update the border indices
    /// </summary>
    public class GridMesher : IMeshBuilder
    {
        public const bool NoScaling = false;
        public const bool NormalizeUVs = true;

        public const bool Temporary = true;
        public const bool Persistent = false;

        private const int Filled = 1;

        private const int VertexLengthIndex = 0;
        private const int TriangleLengthIndex = 1;
        private const int ArrayLengthsCount = 2;

        private const int MinResolution = 1;
        private const int MaxResolution = 10;

        private bool inited = false;
        private bool isGenerating = false;

        private DataVolume data;
        private Extents dataExtents;
        private int resolution;
        private float cellSize;
        private bool normalizeUvs;
        private float3 positionOffset;

        private Texture2D heightmap;
        private float maxHeight;
        private byte colorLevelOffset;

        private Mesh mesh;
        private NativeArray<int> meshArrayLengths;

        private NativeList<GridCell> gridCells;
        private GridMeshData meshData;
        private NativeList<int> borderIndices;

        private NativeArray<int> tempRowBuffer;
        private NativeArray<Color32> heightMapColors;

        private JobHandle lastHandle;

        public GridMesher()
        {
            mesh = new Mesh();
        }

        public void Init(DataVolume data, float cellSize, int cellResolution, bool normalizeUvs = NoScaling, float3 posOffset = default(float3))
        {
            this.data = data;
            dataExtents = new Extents(data.XLength, data.YLength, data.ZLength);

            resolution = Mathf.Clamp(cellResolution, MinResolution, MaxResolution);
            if (resolution != cellResolution)
            {
                Debug.LogWarningFormat("cell resolution has been clamped ({0} -> {1})", cellResolution, resolution);
            }

            this.cellSize = cellSize;
            if (this.cellSize <= 0f)
            {
                this.cellSize = 0.1f;
                Debug.LogWarning("Cell size must be greater than zero!");
            }

            this.normalizeUvs = normalizeUvs;
            this.positionOffset = posOffset;

            if (meshArrayLengths.IsCreated)
            {
                ClearMeshArrayLengths();
            }
            else
            {
                meshArrayLengths = new NativeArray<int>(ArrayLengthsCount, Allocator.Persistent);
            }

            this.heightmap = null;
            this.maxHeight = 0;

            inited = true;
        }

        public void Init(DataVolume data, float cellSize, int resolution, Texture2D heightmap, float maxHeight, byte colorLevelOffset = 0, bool normalizeUV = true, float3 posOffset = default(float3))
        {
            Init(data, cellSize, resolution, normalizeUV, posOffset);

            this.heightmap = heightmap;
            this.maxHeight = maxHeight;
            this.colorLevelOffset = colorLevelOffset;
        }

        public void Dispose()
        {
            lastHandle.Complete();

            inited = false;
            isGenerating = false;

            DisposeTemp();
            if (meshArrayLengths.IsCreated) meshArrayLengths.Dispose();
        }

        private void DisposeTemp()
        {
            if (gridCells.IsCreated)
            {
                gridCells.Dispose();
            }
            if (borderIndices.IsCreated)
            {
                borderIndices.Dispose();
            }
            if (heightMapColors.IsCreated)
            {
                heightMapColors.Dispose();
            }
            if (tempRowBuffer.IsCreated)
            {
                tempRowBuffer.Dispose();
            }
            if (meshData != null)
            {
                meshData.Dispose();
                meshData = null;
            }
        }

        public void StartGeneration()
        {
            if (!inited)
            {
                Debug.LogError("mesher was not inited!");
                return;
            }

            if (isGenerating)
            {
                Debug.LogError("mesher is already generating!");
                return;
            }

            isGenerating = true;

            DisposeTemp();
            lastHandle.Complete();

            gridCells = new NativeList<GridCell>(Allocator.Temp);
            borderIndices = new NativeList<int>(2*resolution*dataExtents.XZ, Allocator.Temp);
            meshData = new GridMeshData();
            tempRowBuffer = new NativeArray<int>(dataExtents.X, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            var generateGroundGridCells = new GenerateMeshGridCells
            {
                resolution = resolution,
                volumeExtent = dataExtents,
                data = data.Data,
                cells = gridCells,
                bottomRowBuffer = tempRowBuffer,
                borderIndices = borderIndices,
                bufferLengths = meshArrayLengths
            };

            lastHandle = generateGroundGridCells.Schedule();

            lastHandle.Complete();

            meshData.CreateMeshBuffers(meshArrayLengths[VertexLengthIndex], meshArrayLengths[TriangleLengthIndex], Temporary);

            var generateMeshData = new GenerateMeshData
            {
                // cell properties
                cellResolution = resolution,
                cellSize = cellSize,
                stepX = cellSize / resolution,
                stepZ = cellSize / resolution,
                stepU = 1f / resolution,
                stepV = 1f / resolution,
                stepTriangle = resolution * resolution * 2 *3,

                // input data
                volumeExtent = dataExtents,
                cells = gridCells,
                borderIndices = borderIndices,

                // output data
                meshVertices = meshData.Vertices,
                meshUVs = meshData.UVs,
                meshTriangles = meshData.Tris
            };

            lastHandle = generateMeshData.Schedule(gridCells.Length, 32, lastHandle);

            if (normalizeUvs)
            {
                var scaleJob = new UVScaleJob
                {
                    scale = new float2(1f / data.XLength, 1f / data.ZLength),
                    uvs = meshData.UVs
                };

                lastHandle = scaleJob.Schedule(meshData.UVs.Length, 128, lastHandle);
            }

            if (positionOffset.x != 0 || positionOffset.y != 0 || positionOffset.z != 0)
            {
                var offsetJob = new VertexOffsetJob
                {
                    offset = positionOffset,
                    vertices = meshData.Vertices
                };


                lastHandle = offsetJob.Schedule(meshData.Vertices.Length, 128, lastHandle);
            }

            if (heightmap)
            {
                if (heightMapColors.IsCreated)
                {
                    heightMapColors.Dispose();
                }

                heightMapColors = new NativeArray<Color32>(heightmap.GetPixels32(), Allocator.Temp);

                var applyHeightmapJob = new ApplyHeightMapJob
                {
                    heightmapWidth = heightmap.width,
                    heightmapHeight = heightmap.height,
                    colors = heightMapColors,
                    heightStep = maxHeight / 255f,
                    heightOffset = -(maxHeight / 255f) * colorLevelOffset,
                    uScale = normalizeUvs ? 1f : 1f / data.XLength,
                    vScale = normalizeUvs ? 1f : 1f / data.ZLength,
                    uvs = meshData.UVs,
                    vertices = meshData.Vertices
                };

                lastHandle = applyHeightmapJob.Schedule(meshData.Vertices.Length, 128, lastHandle);
            }

            JobHandle.ScheduleBatchedJobs();
        }

        public void EndGeneration()
        {
            if (!isGenerating)
            {
                Debug.LogWarning("mesher wasn't generating!");
                return;
            }

            lastHandle.Complete();
            isGenerating = false;

            meshData.UpdateMesh(mesh);

            DisposeTemp();
        }

        private void ClearMeshArrayLengths()
        {
            for (int i = 0; i < meshArrayLengths.Length; ++i)
            {
                meshArrayLengths[i] = 0;
            }
        }
        
        // first step of the mesh generation
        // loop through the data volume and make a GridCell for every cell which will be generated
        // store every information which will be needed for parallel generation in the next step
        // set the left and bottom neighbours for the cell
        // set the start vertex for the cell
        // set the border vertex indices
        [BurstCompile]
        internal struct GenerateMeshGridCells : IJob
        {
            // input
            public int resolution;
            public Extents volumeExtent;
            [ReadOnly] public NativeArray<byte> data;

            // temp buffer
            public NativeArray<int> bottomRowBuffer;

            // output
            public NativeList<GridCell> cells;
            public NativeList<int> borderIndices;
            [WriteOnly] public NativeArray<int> bufferLengths;

            // cell vertex
            private int cellVertexCount;
            private int cellVertexCount1Adjacent;
            private int cellVertexCount2Adjacent;

            public void Execute()
            {
                cellVertexCount = (resolution + 1) * (resolution + 1);
                cellVertexCount1Adjacent = cellVertexCount - (resolution + 1);
                cellVertexCount2Adjacent = cellVertexCount - (resolution + 1) * 2 + 1;

                int vertexCount = 0;

                int boundaryIndex = 0;
                int dataIndex = 0;
                for (int y = 0; y < volumeExtent.Y; ++y)
                {
                    for (int i = 0; i < bottomRowBuffer.Length; ++i)
                    {
                        bottomRowBuffer[i] = -1;
                    }

                    for (int z = 0; z < volumeExtent.Z; ++z)
                    {
                        int left = -1;
                        for (int x = 0; x < volumeExtent.X; ++x)
                        {
                            if (data[dataIndex] == Filled && (y == volumeExtent.Y - 1 || data[dataIndex + volumeExtent.XZ] != Filled))
                            {
                                var right = new Range { start = boundaryIndex, end = boundaryIndex + resolution };
                                boundaryIndex += resolution + 1;
                                var top = new Range { start = boundaryIndex, end = boundaryIndex + resolution };
                                boundaryIndex += resolution + 1;
                                borderIndices.ResizeUninitialized(boundaryIndex);
                                
                                if (bottomRowBuffer[x] >= 0)
                                {
                                    if (left >= 0)
                                    {
                                        AddBorderIndicesBothAdj(vertexCount, right.start, top.start, bottomRowBuffer[x], left);
                                    }
                                    else
                                    {
                                        AddBorderIndicesBottomAdj(vertexCount, right.start, top.start, bottomRowBuffer[x]);
                                    }
                                }
                                else if (left >= 0)
                                {
                                    AddBorderIndicesLeftAdj(vertexCount, right.start, top.start, left);
                                }
                                else
                                {
                                    AddBorderIndicesNoAdj(vertexCount, right.start, top.start);
                                }

                                var cell = new GridCell
                                {
                                    coord = new int3 { x = x, y = y, z = z },
                                    leftCell = left,
                                    bottomCell = bottomRowBuffer[x],
                                    rightBorderIndices = right,
                                    topBorderIndices = top,
                                    startVertex = vertexCount
                                };

                                cells.Add(cell);

                                vertexCount += CalcCellVertexCount(left, bottomRowBuffer[x]);

                                left = cells.Length - 1;
                                bottomRowBuffer[x] = cells.Length - 1;
                            }
                            else
                            {
                                left = -1;
                                bottomRowBuffer[x] = -1;
                            }

                            ++dataIndex;
                        }
                    }
                }

                int triangleCount = cells.Length * (resolution * resolution * 2) * 3;
                int borderIndexCount = cells.Length * (resolution + 1) * 2;

                bufferLengths[VertexLengthIndex] = vertexCount;
                bufferLengths[TriangleLengthIndex] = triangleCount;
            }

            private int CalcCellVertexCount(int leftIndex, int bottomIndex)
            {
                if (leftIndex >= 0)
                {
                    if (bottomIndex >= 0)
                    {
                        return cellVertexCount2Adjacent;
                    }
                    else
                    {
                        return cellVertexCount1Adjacent;
                    }
                }
                else if (bottomIndex >= 0)
                {
                    return cellVertexCount1Adjacent;
                }

                return cellVertexCount;
            }

            private void AddBorderIndicesNoAdj(int startVertex, int rightBorderStart, int topBorderStart)
            {
                int lastVertex = startVertex + (resolution + 1) * (resolution + 1) - 1;

                for (int i = 0; i <= resolution; ++i)
                {
                    borderIndices[rightBorderStart + i] = startVertex + resolution;
                    startVertex += resolution + 1;
                    borderIndices[topBorderStart + i] = lastVertex - resolution + i;
                }
            }

            private void AddBorderIndicesLeftAdj(int startVertex, int rightBorderStart, int topBorderStart, int leftCell)
            {
                int lastVertex = startVertex + (resolution + 1) * resolution - 1;

                for (int i = 0; i <= resolution; ++i)
                {
                    borderIndices[rightBorderStart + i] = startVertex + resolution - 1;
                    startVertex += resolution;
                    borderIndices[topBorderStart + i] = lastVertex - resolution + i;
                }

                borderIndices[topBorderStart] = GetBorderRightIndex(leftCell, resolution);
            }

            private void AddBorderIndicesBottomAdj(int startVertex, int rightBorderStart, int topBorderStart, int bottomCell)
            {
                int lastVertex = startVertex + (resolution + 1) * resolution - 1;
                
                for (int i = 1; i <= resolution; ++i)
                {
                    borderIndices[rightBorderStart + i] = startVertex + resolution;
                    startVertex += resolution + 1;
                    borderIndices[topBorderStart + i] = lastVertex - resolution + i;
                }

                borderIndices[rightBorderStart] = GetBorderTopIndex(bottomCell, resolution);
                borderIndices[topBorderStart] = lastVertex - resolution;
            }

            private void AddBorderIndicesBothAdj(int startVertex, int rightBorderStart, int topBorderStart, int bottomCell, int leftCell)
            {
                int lastVertex = startVertex + resolution * resolution - 1;

                for (int i = 1; i <= resolution; ++i)
                {
                    borderIndices[rightBorderStart + i] = startVertex + resolution - 1;
                    startVertex += resolution;
                    borderIndices[topBorderStart + i] = lastVertex - resolution + i;
                }

                borderIndices[rightBorderStart] = GetBorderTopIndex(bottomCell, resolution);
                borderIndices[topBorderStart] = GetBorderRightIndex(leftCell, resolution);
            }

            private int GetBorderRightIndex(int cellIndex, int borderIndex) { return cells[cellIndex].rightBorderIndices.At(borderIndex, borderIndices); }
            private int GetBorderTopIndex(int cellIndex, int borderIndex) { return cells[cellIndex].topBorderIndices.At(borderIndex, borderIndices); }
        }

        // second step of the mesh generation
        // every cell contains the required information
        // so this just steps through in parallel and generates the mesh data for every cell
        [BurstCompile]
        internal struct GenerateMeshData : IJobParallelFor
        {
            static private readonly Range NullRange = new Range { start = 0, end = 0 };

            // cell properties
            public int cellResolution;
            public float cellSize;
            public float stepX;
            public float stepZ;
            public float stepU;
            public float stepV;
            public int stepTriangle;

            // input data
            public Extents volumeExtent;
            [ReadOnly] public NativeArray<GridCell> cells;
            [ReadOnly] public NativeArray<int> borderIndices;

            // output data
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float3> meshVertices;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<int> meshTriangles;
            [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float2> meshUVs;

            public void Execute(int index)
            {
                int triangleIndex = index * stepTriangle;

                var cell = cells[index];

                Range left = cell.leftCell >= 0 ? cells[cell.leftCell].rightBorderIndices : NullRange;
                Range bottom = cell.bottomCell >= 0 ? cells[cell.bottomCell].topBorderIndices : NullRange;
                    
                GenerateGrid(cell.coord, left, bottom, cell.rightBorderIndices, cell.topBorderIndices, cell.startVertex, triangleIndex);
            }

            private void GenerateGrid(int3 coord, Range left, Range bottom, Range right, Range top, int vertexIndex, int triangleIndex)
            {
                float startX = coord.x * cellSize;
                float startY = (coord.y + 0.5f) * cellSize;
                float startZ = coord.z * cellSize;

                float startU = coord.x;
                float startV = coord.z;

                float3 v = new float3 { x = startX, y = startY, z = startZ };
                float2 uv = new float2 { x = startU, y = startV };
                int c0, c1, c2, c3;
                int width = cellResolution + 1;
                int rowOffset = left.IsValid ? width - 1 : width;
                int colOffset = left.IsValid ? -1 : 0;
                for (int y = 0; y < cellResolution; ++y)
                {
                    v.x = startX;
                    uv.x = startU;

                    for (int x = 0; x < cellResolution; ++x)
                    {
                        // corner
                        c0 = vertexIndex;
                        // above
                        c1 = c0 + rowOffset;
                        // right
                        c2 = c0 + 1;
                        // diagonal corner
                        c3 = c0 + rowOffset + 1;

                        if (x == 0 && y == 0 && bottom.IsValid && left.IsValid)
                        {
                            c0 = bottom.At(0, borderIndices);
                            c1 = left.At(1, borderIndices);
                            c2 = bottom.At(1, borderIndices);
                            c3 = vertexIndex;
                        }
                        else
                        if (y == 0 && bottom.IsValid)
                        {
                            c0 = bottom.At(x, borderIndices);
                            c1 = vertexIndex + x + colOffset;
                            c2 = bottom.At(x + 1, borderIndices);
                            c3 = vertexIndex + x + 1 + colOffset;
                        }
                        else if (x == 0 && left.IsValid)
                        {
                            c0 = left.At(y, borderIndices);
                            c1 = left.At(y + 1, borderIndices);
                            c2 = vertexIndex;
                            c3 = vertexIndex + cellResolution;
                        }
                        else
                        {
                            Add(v, uv, vertexIndex);
                            ++vertexIndex;
                        }

                        AddTris(c0, c1, c2, c3, triangleIndex);
                        triangleIndex += 6;

                        v.x += stepX;
                        uv.x += stepU;
                    }

                    // right side vertex
                    if (y > 0 || !bottom.IsValid)
                    {
                        Add(v, uv, vertexIndex);
                        ++vertexIndex;
                    }

                    v.z += stepZ;
                    uv.y += stepV;
                }

                // top row vertices
                v.x = startX;
                uv.x = startU;

                for (int i = 0; i < width; ++i)
                {
                    if (i > 0 || !left.IsValid)
                    {
                        Add(v, uv, vertexIndex);
                        ++vertexIndex;
                    }
                    v.x += stepX;
                    uv.x += stepU;
                }
            }

            private void Add(float3 vertex, float2 uv, int index)
            {
                meshVertices[index] = vertex;
                meshUVs[index] = uv;
            }

            private void AddTris(int c0, int c1, int c2, int c3, int index)
            {
                meshTriangles[index] = c0;
                meshTriangles[index + 1] = c1;
                meshTriangles[index + 2] = c2;

                meshTriangles[index + 3] = c3;
                meshTriangles[index + 4] = c2;
                meshTriangles[index + 5] = c1;
            }
        }

        [BurstCompile]
        public struct ApplyHeightMapJob : IJobParallelFor
        {
            public int heightmapWidth;
            public int heightmapHeight;
            public float heightStep;
            public float heightOffset;
            public float uScale;
            public float vScale;

            [ReadOnly] public NativeArray<Color32> colors;
            [ReadOnly] public NativeArray<float2> uvs;
            public NativeArray<float3> vertices;

            public void Execute(int index)
            {
                int x = (int)(heightmapWidth * uvs[index].x * uScale) % heightmapWidth;
                int y = (int)(heightmapHeight * uvs[index].y * vScale) % heightmapHeight;

                int colorIndex = y * heightmapWidth + x;
                Color32 color = colors[colorIndex];
                vertices[index] += new float3(0, (heightStep * color.r) + heightOffset, 0);
            }
        }

        public Mesh Mesh { get { return mesh; } }
        public bool IsGenerating { get { return isGenerating; } }

        internal class GridMeshData
        {
            // mesh data
            public NativeArray<float3> Vertices { get; private set; }
            public NativeArray<int> Tris { get; private set; }
            public NativeArray<float2> UVs { get; private set; }

            public GridMeshData()
            {

            }

            public void CreateMeshBuffers(int vertexCount, int triangleCount, bool temporary)
            {
                Dispose();

                var allocation = temporary ? Allocator.Temp : Allocator.Persistent;

                Vertices = new NativeArray<float3>(vertexCount, allocation, NativeArrayOptions.UninitializedMemory);
                UVs = new NativeArray<float2>(vertexCount, allocation, NativeArrayOptions.UninitializedMemory);
                Tris = new NativeArray<int>(triangleCount, allocation, NativeArrayOptions.UninitializedMemory);
            }

            public void Dispose()
            {
                if (Vertices.IsCreated) Vertices.Dispose();
                if (Tris.IsCreated) Tris.Dispose();
                if (UVs.IsCreated) UVs.Dispose();
            }

            public void UpdateMesh(Mesh mesh)
            {
                MeshData.UpdateMesh(mesh, Vertices, Tris, UVs);
            }
        }

        /// <summary>
        /// this gets generated for every cell which requires vertex grid generation
        /// startVertex - first index for vertex generation
        /// leftCell and bottomCell - are the two neighbours which are connected to the same vertex mesh (-1 means no connection)
        /// topBorderIndices and rightBorderIndices - are two ranges pointing to and index array, these are the bordering vertex indices 
        /// </summary>
        internal struct GridCell
        {
            public int3 coord;

            public int startVertex;

            public int leftCell;
            public int bottomCell;

            public Range topBorderIndices;
            public Range rightBorderIndices;

            public bool HasLeftCell { get { return leftCell > -1; } }
            public bool HasBottomCell { get { return bottomCell > -1; } }
        }

        internal struct Range
        {
            public int start;
            public int end;

            public bool IsValid { get { return start != end; } }
            public int At(int index, NativeArray<int> indices) { return indices[start + index]; }
        }
    }
}
