using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

using static MeshBuilder.Utils;

namespace MeshBuilder
{
    using TileData = Tile.Data;
    using DataVolume = Volume<Tile.Data>;
    using Direction = Tile.Direction;

    /// <summary>
    /// GridMesher
    /// Generates a grid mesh based on 3d volume data (top part for now)
    /// 
    /// TODO:
    /// - it only generates a top part, it should be able to handle any direction
    /// - it would be nice if the heightmap lerp values would be gradual
    /// </summary>
    public class GridMesher : Builder
    {
        public enum UVMode { Normalized, NoScaling }

        private const uint MeshDataBufferFlags = (uint)MeshData.Buffer.Vertex | (uint)MeshData.Buffer.Triangle | (uint)MeshData.Buffer.UV;

        private const int VertexLengthIndex = 0;
        private const int TriangleLengthIndex = 1;
        private const int ArrayLengthsCount = 2;

        private const int MinResolution = 1;
        private const int MaxResolution = 10;

        private DataVolume data;
        private int filledValue;
        private Extents dataExtents;
        private int resolution;
        private float3 cellSize;
        private UVMode uvMode;
        private float3 positionOffset;

        private NativeArray<int> meshArrayLengths;

        private HeightMapData heightMapData;
        private HeightMapData HeightMap
        {
            get { return heightMapData; }
            set
            {
                if (heightMapData != null) { heightMapData.Dispose(); }
                heightMapData = value;
            }
        }

        private MeshData meshData;

        public void Init(DataVolume data, int filledValue, float3 cellSize, int cellResolution, UVMode uvMode = UVMode.NoScaling, float3 posOffset = default(float3))
        {
            this.data = data;
            dataExtents = new Extents(data.XLength, data.YLength, data.ZLength);

            this.filledValue = filledValue;

            resolution = Mathf.Clamp(cellResolution, MinResolution, MaxResolution);
            if (resolution != cellResolution)
            {
                Debug.LogWarningFormat("cell resolution has been clamped ({0} -> {1})", cellResolution, resolution);
            }

            this.cellSize = cellSize;
            if (this.cellSize.x <= 0f || this.cellSize.y <= 0f || this.cellSize.z <= 0f)
            {
                this.cellSize = new float3(1, 1, 1);
                Debug.LogWarning("Cell size must be greater than zero!");
            }

            this.uvMode = uvMode;
            this.positionOffset = posOffset;

            InitMeshArrayLengths();

            HeightMap = null;

            Inited();
        }

        private void InitMeshArrayLengths()
        {
            if (meshArrayLengths.IsCreated)
            {
                for (int i = 0; i < meshArrayLengths.Length; ++i)
                {
                    meshArrayLengths[i] = 0;
                }
            }
            else
            {
                meshArrayLengths = new NativeArray<int>(ArrayLengthsCount, Allocator.Persistent);
            }
        }

        public void InitHeightMap(Texture2D heightMap, float valueScale, float valueOffset = 0)
        {
            HeightMap = HeightMapData.CreateWithManualInfo(heightMap, valueScale, valueOffset);
        }

        public void InitHeightMapScaleFromHeight(Texture2D heightMap, float maxHeight, float valueOffset = 0)
        {
            HeightMap = HeightMapData.CreateWithScaleFromTexInterval(heightMap, maxHeight, valueOffset);
        }

        public void InitHeightMapScaleFromHeightAvgOffset(Texture2D heightMap, float maxHeight)
        {
            HeightMap = HeightMapData.CreateWithScaleFromTexIntervalAvgOffset(heightMap, maxHeight);
        }

        public void InitHeightMapScaleFromHeightMinOffset(Texture2D heightMap, float maxHeight)
        {
            HeightMap = HeightMapData.CreateWithScaleFromTexIntervalMinOffset(heightMap, maxHeight);
        }

        override public void Dispose()
        {
            base.Dispose();

            SafeDispose(ref meshArrayLengths);

            HeightMap = null;
        }

        override protected JobHandle StartGeneration(JobHandle dependOn)
        {
            var gridCells = new NativeList<GridCell>(Allocator.TempJob);
            AddTemp(gridCells);
            var borderIndices = new NativeList<int>(2 * resolution * dataExtents.XZ, Allocator.TempJob);
            AddTemp(borderIndices);
            var tempRowBuffer = new NativeArray<int>(dataExtents.X, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(tempRowBuffer);

            var generateGroundGridCells = new GenerateMeshGridCells
            {
                resolution = resolution,
                volumeExtent = dataExtents,
                data = data.Data,
                filledValue = filledValue,
                cells = gridCells,
                bottomRowBuffer = tempRowBuffer,
                borderIndices = borderIndices,
                bufferLengths = meshArrayLengths
            };

            dependOn = generateGroundGridCells.Schedule(dependOn);

            dependOn.Complete();

            int vertexLength = meshArrayLengths[VertexLengthIndex];
            int triLength = meshArrayLengths[TriangleLengthIndex];
            meshData = new MeshData(vertexLength, triLength, Allocator.TempJob, MeshDataBufferFlags);
            AddTemp(meshData);

            var generateMeshData = new GenerateMeshData
            {
                // cell properties
                cellResolution = resolution,
                cellSize = cellSize,
                stepX = cellSize.x / resolution,
                stepZ = cellSize.z / resolution,
                stepU = 1f / resolution,
                stepV = 1f / resolution,
                stepTriangle = resolution * resolution * 2 * 3,

                // mods
                uvScale = (uvMode == UVMode.Normalized) ? new float2(1f / data.XLength, 1f / data.ZLength) : new float2(1, 1),
                positionOffset = positionOffset,

                // input data
                volumeExtent = dataExtents,
                cells = gridCells,
                borderIndices = borderIndices,

                // output data
                meshVertices = meshData.Vertices,
                meshUVs = meshData.UVs,
                meshTriangles = meshData.Triangles
            };

            dependOn = generateMeshData.Schedule(gridCells.Length, 64, dependOn);

            if (HeightMap != null)
            {
                bool normalizedUV = uvMode == UVMode.Normalized;
                float uScale = normalizedUV ? 1f : 1f / data.XLength;
                float vScale = normalizedUV ? 1f : 1f / data.ZLength;
                dependOn = HeightMap.StartGeneration(meshData, uScale, vScale, resolution, dataExtents, gridCells, borderIndices, dependOn);
            }

            return dependOn;
        }

        override protected void EndGeneration(Mesh mesh)
        {
            if (HeightMap != null)
            {
                HeightMap.EndGeneration();
            }

            meshData.UpdateMesh(mesh, MeshData.UpdateMode.Clear);
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
            public int filledValue;
            [ReadOnly] public NativeArray<TileData> data;

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
                            if (HasTop(x, y, z, data, filledValue, volumeExtent))
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
                                    connectedDirection = FindAdjacentDirections(x, y, z, data, filledValue, volumeExtent),
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

            static private bool HasTop(int x, int y, int z, NativeArray<TileData> data, int filledValue, Extents extent)
            {
                int i = extent.ToIndexAt(x, y, z);
                return data[i].themeIndex == filledValue && (y == extent.Y - 1 || data[i + extent.XZ].themeIndex != filledValue);
            }

            static private bool HasTop(int index, int y, NativeArray<TileData> data, int filledValue, Extents extent)
            {
                return data[index].themeIndex == filledValue && (y == extent.Y - 1 || data[index + extent.XZ].themeIndex != filledValue);
            }

            static private byte FindAdjacentDirections(int x, int y, int z, NativeArray<TileData> data, int filledValue, Extents extent)
            {
                int i = extent.ToIndexAt(x, y, z);
                byte res = 0;

                if (x > 0 && HasTop(i - 1, y, data, filledValue, extent)) { res |= (byte)Tile.Direction.XMinus; }
                if (x < extent.X - 1 && HasTop(i + 1, y, data, filledValue, extent)) { res |= (byte)Tile.Direction.XPlus; }

                if (z > 0 && HasTop(i - extent.X, y, data, filledValue, extent)) { res |= (byte)Tile.Direction.ZMinus; }
                if (z < extent.Z - 1 && HasTop(i + extent.X, y, data, filledValue, extent)) { res |= (byte)Tile.Direction.ZPlus; }

                return res;
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
            public float3 cellSize;
            public float stepX;
            public float stepZ;
            public float stepU;
            public float stepV;
            public int stepTriangle;

            // mods
            public float3 positionOffset;
            public float2 uvScale;

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
                float startX = coord.x * cellSize.x;
                float startY = (coord.y + 1f) * cellSize.y;
                float startZ = coord.z * cellSize.z;

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
                        else if (y == 0 && bottom.IsValid)
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
                meshVertices[index] = vertex + positionOffset;
                meshUVs[index] = uv * uvScale;
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

            // Tile.Direction flags
            public byte connectedDirection;

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

        static private void SafeDispose<T>(ref NativeArray<T> collection) where T : struct
        {
            if (collection.IsCreated)
            {
                collection.Dispose();
                collection = default;
            }
        }

        static private void SafeDispose<T>(ref NativeList<T> collection) where T : struct
        {
            if (collection.IsCreated)
            {
                collection.Dispose();
                collection = default;
            }
        }

        [System.Serializable]
        internal class HeightMapData
        {
            public const int ValueScaleIndex = 0;
            public const int ValueOffsetIndex = 1;

            internal enum ScaleMode { Manual, FromTexInterval }
            internal enum OffsetMode { Manual, FromTexAverage, FromTexMin }

            public Texture2D HeightMap { get; private set; }
            private float maxHeight;

            private ScaleMode scaleMode;
            private float valueScale;

            private OffsetMode offsetMode;
            private float valueOffset;

            private NativeArray<byte> tempHeightMapMin;
            private NativeArray<byte> tempHeightMapAvg;
            private NativeArray<byte> tempHeightMapMax;

            private NativeArray<Color32> tempHeightMapColors;
            private NativeArray<float> tempHeightLerpValues;

            private NativeArray<float> values;

            private HeightMapData(Texture2D heightMap, float maxHeight, float valueScale, ScaleMode scaleMode, float valueOffset, OffsetMode offsetMode)
            {
                HeightMap = heightMap;
                this.maxHeight = maxHeight;
                this.valueScale = valueScale;
                this.scaleMode = scaleMode;
                this.valueOffset = valueOffset;
                this.offsetMode = offsetMode;

                values = new NativeArray<float>(2, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                values[ValueScaleIndex] = valueScale;
                values[ValueOffsetIndex] = valueOffset;
            }

            public void Dispose()
            {
                DisposeTemps();
                if (values.IsCreated)
                {
                    values.Dispose();
                }
            }

            public JobHandle StartGeneration(MeshData meshData, float uScale, float vScale, int cellResolution, Extents dataExtent, NativeArray<GridCell> gridCells, NativeArray<int> borderIndices, JobHandle lastHandle)
            {
                if (HeightMap != null && (scaleMode != ScaleMode.Manual || offsetMode != OffsetMode.Manual))
                {
                    int imgWidth = HeightMap.width;
                    int imgHeight = HeightMap.height;

                    tempHeightMapColors = new NativeArray<Color32>(HeightMap.GetPixels32(), Allocator.TempJob);

                    tempHeightMapMin = new NativeArray<byte>(imgHeight, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    tempHeightMapAvg = new NativeArray<byte>(imgHeight, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    tempHeightMapMax = new NativeArray<byte>(imgHeight, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    tempHeightLerpValues = new NativeArray<float>(meshData.UVs.Length, Allocator.TempJob, NativeArrayOptions.ClearMemory);

                    var generateHeightLerpJob = new GenerateHeightLerpJob
                    {
                        cellResolution = cellResolution,
                        volumeExtent = dataExtent,
                        cells = gridCells,
                        borderIndices = borderIndices,
                        heightLerpValues = tempHeightLerpValues
                    };
                    var heightLerpHandle = generateHeightLerpJob.Schedule(gridCells.Length, 32, lastHandle);

                    var heightMapJob = new CalcHeightMapInfoJob
                    {
                        heightmapWidth = imgWidth,
                        colors = tempHeightMapColors,
                        minValues = tempHeightMapMin,
                        avgValues = tempHeightMapAvg,
                        maxValues = tempHeightMapMax
                    };

                    lastHandle = heightMapJob.Schedule(imgHeight, 128, lastHandle);

                    var fillHeightMapInfo = new FillHeightMapInfoJob
                    {
                        scaleMode = scaleMode,
                        offsetMode = offsetMode,
                        maxHeight = maxHeight,
                        valueScale = valueScale,
                        valueOffset = valueOffset,
                        minValues = tempHeightMapMin,
                        avgValues = tempHeightMapAvg,
                        maxValues = tempHeightMapMax,
                        outValues = values
                    };

                    lastHandle = fillHeightMapInfo.Schedule(lastHandle);

                    lastHandle = JobHandle.CombineDependencies(lastHandle, heightLerpHandle);

                    var applyHeightmapJob = new ApplyHeightMapJob
                    {
                        heightmapWidth = HeightMap.width,
                        heightmapHeight = HeightMap.height,
                        uScale = uScale,
                        vScale = vScale,
                        colors = tempHeightMapColors,
                        heightValues = values,
                        uvs = meshData.UVs,
                        heightLerpValues = tempHeightLerpValues,
                        vertices = meshData.Vertices
                    };

                    lastHandle = applyHeightmapJob.Schedule(meshData.VerticesLength, 128, lastHandle);
                }

                return lastHandle;
            }

            public void EndGeneration()
            {
                DisposeTemps();
            }

            private void DisposeTemps()
            {
                SafeDispose(ref tempHeightMapMin);
                SafeDispose(ref tempHeightMapAvg);
                SafeDispose(ref tempHeightMapMax);
                SafeDispose(ref tempHeightMapColors);
                SafeDispose(ref tempHeightLerpValues);
            }

            static public HeightMapData CreateWithManualInfo(Texture2D heightMap, float valueScale, float valueOffset)
            {
                return new HeightMapData(heightMap, valueScale * 255f, valueScale, ScaleMode.Manual, valueOffset, OffsetMode.Manual);
            }

            static public HeightMapData CreateWithScaleFromTexInterval(Texture2D heightMap, float maxHeight, float valueOffset)
            {
                return new HeightMapData(heightMap, maxHeight, maxHeight / 255f, ScaleMode.FromTexInterval, valueOffset, OffsetMode.Manual);
            }

            static public HeightMapData CreateWithScaleFromTexIntervalMinOffset(Texture2D heightMap, float maxHeight)
            {
                return new HeightMapData(heightMap, maxHeight, maxHeight / 255f, ScaleMode.FromTexInterval, 0, OffsetMode.FromTexMin);
            }

            static public HeightMapData CreateWithScaleFromTexIntervalAvgOffset(Texture2D heightMap, float maxHeight)
            {
                return new HeightMapData(heightMap, maxHeight, maxHeight / 255f, ScaleMode.FromTexInterval, 0, OffsetMode.FromTexAverage);
            }

            [BurstCompile]
            public struct CalcHeightMapInfoJob : IJobParallelFor
            {
                public int heightmapWidth;

                [ReadOnly] public NativeArray<Color32> colors;
                [WriteOnly] public NativeArray<byte> minValues;
                [WriteOnly] public NativeArray<byte> avgValues;
                [WriteOnly] public NativeArray<byte> maxValues;

                public void Execute(int index)
                {
                    byte min = 255;
                    byte max = 0;
                    int sum = 0;
                    int start = index * heightmapWidth;
                    int end = start + heightmapWidth;
                    for (int i = start; i < end; ++i)
                    {
                        int color = colors[i].r;
                        min = (byte)math.min(min, color);
                        max = (byte)math.max(max, color);
                        sum += color;
                    }
                    minValues[index] = min;
                    maxValues[index] = max;
                    avgValues[index] = (byte)(sum / heightmapWidth);
                }
            }

            [BurstCompile]
            public struct FillHeightMapInfoJob : IJob
            {
                public ScaleMode scaleMode;
                public OffsetMode offsetMode;

                public float maxHeight;
                public float valueScale;
                public float valueOffset;

                [ReadOnly] public NativeArray<byte> minValues;
                [ReadOnly] public NativeArray<byte> avgValues;
                [ReadOnly] public NativeArray<byte> maxValues;

                [WriteOnly] public NativeArray<float> outValues;

                public void Execute()
                {
                    if (scaleMode == ScaleMode.FromTexInterval)
                    {
                        byte min = 255;
                        byte max = 0;
                        for (int i = 0; i < minValues.Length; ++i)
                        {
                            min = (byte)math.min((int)min, minValues[i]);
                            max = (byte)math.max((int)max, maxValues[i]);
                        }
                        float diff = math.max(max - min, 1);
                        valueScale = maxHeight / diff;
                    }

                    if (offsetMode == OffsetMode.FromTexMin)
                    {
                        byte min = 255;
                        for (int i = 0; i < minValues.Length; ++i)
                        {
                            min = (byte)math.min((int)min, minValues[i]);
                        }
                        valueOffset = -min * valueScale;
                    }
                    else if (offsetMode == OffsetMode.FromTexAverage)
                    {
                        int sum = 0;
                        for (int i = 0; i < avgValues.Length; ++i)
                        {
                            sum += avgValues[i];
                        }
                        float avg = sum / avgValues.Length;
                        valueOffset = -avg * valueScale;
                    }

                    outValues[ValueScaleIndex] = valueScale;
                    outValues[ValueOffsetIndex] = valueOffset;
                }
            }

            // a height multiplier is generated for the edges, 0 for now, could be extended to be more gradual
            // if the grid is used with other mesh generators, it can be useful to set the edges to zero height, 
            // so a hill on the grid doesn't overlap with the border tiles for example
            [BurstCompile]
            internal struct GenerateHeightLerpJob : IJobParallelFor
            {
                static private readonly Range NullRange = new Range { start = 0, end = 0 };

                // cell properties
                public int cellResolution;

                // input data
                public Extents volumeExtent;
                [ReadOnly] public NativeArray<GridCell> cells;
                [ReadOnly] public NativeArray<int> borderIndices;

                // output data
                [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<float> heightLerpValues;

                public void Execute(int index)
                {
                    var cell = cells[index];

                    Range left = cell.leftCell >= 0 ? cells[cell.leftCell].rightBorderIndices : NullRange;
                    Range bottom = cell.bottomCell >= 0 ? cells[cell.bottomCell].topBorderIndices : NullRange;

                    GenerateGrid(cell.coord, left, bottom, cell.rightBorderIndices, cell.topBorderIndices, cell.startVertex, cell.connectedDirection);
                }

                private void GenerateGrid(int3 coord, Range left, Range bottom, Range right, Range top, int vertexIndex, byte connectedDirection)
                {
                    int c0, c1, c2, c3;
                    int width = cellResolution + 1;
                    int rowOffset = left.IsValid ? width - 1 : width;
                    int colOffset = left.IsValid ? -1 : 0;
                    for (int y = 0; y < cellResolution; ++y)
                    {
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
                            else if (y == 0 && bottom.IsValid)
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
                                ++vertexIndex;
                            }

                            SetHeightLerpValues(c0, c1, c2, c3, connectedDirection, x, y, cellResolution);
                        }

                        // right side vertex
                        if (y > 0 || !bottom.IsValid)
                        {
                            ++vertexIndex;
                        }
                    }
                }

                private void SetHeightLerpValues(int v0, int v1, int v2, int v3, byte adjacentDirection, int x, int y, int cellResolution)
                {
                    heightLerpValues[v0] = CalcHeightLerpValue(adjacentDirection, x, y, cellResolution);
                    heightLerpValues[v1] = CalcHeightLerpValue(adjacentDirection, x, y + 1, cellResolution);
                    heightLerpValues[v2] = CalcHeightLerpValue(adjacentDirection, x + 1, y, cellResolution);
                    heightLerpValues[v3] = CalcHeightLerpValue(adjacentDirection, x + 1, y + 1, cellResolution);
                }

                private float CalcHeightLerpValue(byte adjacentDirection, int x, int y, int cellResolution)
                {
                    if ((adjacentDirection & (byte)Direction.XMinus) == 0 && x == 0) { return 0f; }
                    if ((adjacentDirection & (byte)Direction.XPlus) == 0 && x == cellResolution) { return 0f; }

                    if ((adjacentDirection & (byte)Direction.YMinus) == 0 && y == 0) { return 0f; }
                    if ((adjacentDirection & (byte)Direction.YPlus) == 0 && y == cellResolution) { return 0f; }

                    return 1f;
                }
            }

            [BurstCompile]
            public struct ApplyHeightMapJob : IJobParallelFor
            {
                public int heightmapWidth;
                public int heightmapHeight;
                public float uScale;
                public float vScale;

                [ReadOnly] public NativeArray<float> heightValues;

                [ReadOnly] public NativeArray<Color32> colors;
                [ReadOnly] public NativeArray<float2> uvs;
                [ReadOnly] public NativeArray<float> heightLerpValues;
                public NativeArray<float3> vertices;

                public void Execute(int index)
                {
                    int x = (int)(heightmapWidth * uvs[index].x * uScale) % heightmapWidth;
                    int y = (int)(heightmapHeight * uvs[index].y * vScale) % heightmapHeight;

                    int colorIndex = y * heightmapWidth + x;
                    Color32 color = colors[colorIndex];
                    float height = heightValues[ValueScaleIndex] * color.r + heightValues[ValueOffsetIndex];
                    height *= heightLerpValues[index];
                    vertices[index] += new float3(0, height, 0);
                }
            }
        }
    }
}
