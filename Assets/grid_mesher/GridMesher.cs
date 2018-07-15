using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

using DataVolume = MeshBuilder.Volume<byte>;

namespace MeshBuilder
{
    public class GridMesher : IMeshBuilder
    {
        public const bool NoScaling = false;
        public const bool NormalizeUVs = true;

        private const int Filled = 1;

        private const int VertexLengthIndex = 0;
        private const int TriangleLengthIndex = 1;
        private const int BorderLengthIndex = 2;
        private const int ArrayLengthsCount = 3;

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
        private NativeArray<int> borderIndices;

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

        public void Init(DataVolume data, float cellSize, int resolution, Texture2D heightmap, float maxHeight, byte colorLevelOffset = 0, float3 posOffset = default(float3))
        {
            Init(data, cellSize, resolution, true, posOffset);

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
            meshData = new GridMeshData();

            var generateGroundGridCells = new GenerateMeshGridCells
            {
                resolution = resolution,
                volumeExtent = dataExtents,
                data = data.Data,
                cells = gridCells,
                bufferLengths = meshArrayLengths
            };

            lastHandle = generateGroundGridCells.Schedule();

            lastHandle.Complete();

            meshData.CreateMeshBuffers(meshArrayLengths[VertexLengthIndex], meshArrayLengths[TriangleLengthIndex], true);
            borderIndices = new NativeArray<int>(meshArrayLengths[BorderLengthIndex], Allocator.Temp);

            var generateMeshData = new GenerateMeshData
            {
                cellResolution = resolution,
                cellSize = cellSize,
                volumeExtent = dataExtents,
                cells = gridCells,
                borderIndices = borderIndices,
                meshVertices = meshData.Vertices,
                meshUVs = meshData.UVs,
                meshTriangles = meshData.Tris
            };

            lastHandle = generateMeshData.Schedule(lastHandle);

            if (normalizeUvs)
            {
                var scaleJob = new UVScaleJob
                {
                    scale = new float2((float)(1f / data.XLength), (float)(1f / data.ZLength)),
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

        internal struct GenerateMeshGridCells : IJob
        {
            public int resolution;
            public Extents volumeExtent;
            [ReadOnly] public NativeArray<byte> data;

            [WriteOnly] public NativeList<GridCell> cells;
            [WriteOnly] public NativeArray<int> bufferLengths;

            public void Execute()
            {
                int vertexCount = 0;

                int cellCount = 0;

                int[] bottom = new int[volumeExtent.X];

                int dataIndex = 0;
                for (int y = 0; y < volumeExtent.Y; ++y)
                {
                    for (int i = 0; i < bottom.Length; ++i)
                    {
                        bottom[i] = -1;
                    }

                    for (int z = 0; z < volumeExtent.Z; ++z)
                    {
                        int left = -1;
                        for (int x = 0; x < volumeExtent.X; ++x)
                        {
                            if (data[dataIndex] == Filled && (y == volumeExtent.Y - 1 || data[dataIndex + volumeExtent.XZ] != Filled))
                            {
                                var cell = new GridCell { coord = new int3 { x = x, y = y, z = z }, leftCell = left, bottomCell = bottom[x] };
                                cells.Add(cell);

                                vertexCount += CalcCellVertexCount(left, bottom[x]);

                                left = cellCount;
                                bottom[x] = cellCount;

                                ++cellCount;
                            }
                            else
                            {
                                left = -1;
                                bottom[x] = -1;
                            }

                            ++dataIndex;
                        }
                    }
                }

                int triangleCount = cellCount * (resolution * resolution * 2) * 3;
                int borderIndexCount = cellCount * (resolution + 1) * 2;

                bufferLengths[VertexLengthIndex] = vertexCount;
                bufferLengths[TriangleLengthIndex] = triangleCount;
                bufferLengths[BorderLengthIndex] = borderIndexCount;
            }

            private int CalcCellVertexCount(int leftIndex, int bottomIndex)
            {
                int vertexCount = (resolution + 1) * (resolution + 1);

                if (leftIndex >= 0 && bottomIndex >= 0)
                {
                    vertexCount -= (resolution + 1) * 2 - 1;
                }
                else if (leftIndex >= 0 || bottomIndex >= 0)
                {
                    vertexCount -= resolution + 1;
                }

                return vertexCount;
            }
        }

        internal struct GenerateMeshData : IJob
        {
            static private readonly Range NullRange = new Range { start = 0, end = 0 };

            public int cellResolution;
            public float cellSize;

            public Extents volumeExtent;
            public NativeArray<GridCell> cells;

            public NativeArray<int> borderIndices;

            [WriteOnly] public NativeArray<float3> meshVertices;
            [WriteOnly] public NativeArray<int> meshTriangles;
            [WriteOnly] public NativeArray<float2> meshUVs;

            private float stepX;
            private float stepZ;

            private float stepU;
            private float stepV;

            public void Execute()
            {
                stepX = cellSize / cellResolution;
                stepZ = cellSize / cellResolution;

                stepU = 1.0f / cellResolution;
                stepV = 1.0f / cellResolution;

                int boundaryIndex = 0;
                int sideVertexCount = cellResolution + 1;

                int vertexIndex = 0;
                int triangleIndex = 0;
                for (int i = 0; i < cells.Length; ++i)
                {
                    var cell = cells[i];

                    Range left = cell.leftCell >= 0 ? cells[cell.leftCell].rightBorderIndices : NullRange;
                    Range bottom = cell.bottomCell >= 0 ? cells[cell.bottomCell].topBorderIndices : NullRange;

                    var right = new Range { start = boundaryIndex, end = boundaryIndex + sideVertexCount - 1 };
                    boundaryIndex += sideVertexCount;
                    var top = new Range { start = boundaryIndex, end = boundaryIndex + sideVertexCount - 1 };
                    boundaryIndex += sideVertexCount;

                    GenerateGrid(cell.coord, left, bottom, right, top, ref vertexIndex, ref triangleIndex);

                    cell.rightBorderIndices = right;
                    cell.topBorderIndices = top;
                    cells[i] = cell;
                }
            }

            private void GenerateGrid(int3 coord, Range left, Range bottom, Range right, Range top, ref int vertexIndex, ref int triangleIndex)
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
                    if (y == 0 && bottom.IsValid)
                    {
                        borderIndices[right.start] = borderIndices[bottom.end];
                    }
                    else
                    {
                        borderIndices[right.start + y] = vertexIndex;
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
                    if (i == 0 && left.IsValid)
                    {
                        borderIndices[top.start] = borderIndices[left.end];
                    }
                    else
                    {
                        if (i == width - 1)
                        {
                            borderIndices[right.end] = vertexIndex;
                        }

                        borderIndices[top.start + i] = vertexIndex;

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
            [ReadOnly] public NativeArray<Color32> colors;
            [ReadOnly] public NativeArray<float2> uvs;
            public NativeArray<float3> vertices;

            public void Execute(int index)
            {
                int x = (int)(heightmapWidth * uvs[index].x) % heightmapWidth;
                int y = (int)(heightmapHeight * uvs[index].y) % heightmapHeight;

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
        /// leftCell and bottomCell are the two neighbours which are connected to the same vertex mesh (-1 means no connection)
        /// topBorderIndices and rightBorderIndices are two ranges pointing to and index array, these are the bordering vertex indices 
        /// </summary>
        internal struct GridCell
        {
            public int3 coord;

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
