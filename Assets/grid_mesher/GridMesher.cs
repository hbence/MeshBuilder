using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

using DataVolume = Volume<byte>;
using PieceVolume = Volume<GridMesher.PieceData>;
using PieceElem = VolumeTheme.Elem;

// NOTE:
// the piece data volume has to have twice as large layers (x, z axis), pieces are half the size of cells
[RequireComponent(typeof(MeshFilter))]
public class GridMesher : MonoBehaviour
{
    private const float CellSize = 1f;
    private const float HalfCellSize = CellSize * 0.5f;

    private const byte Empty = 0;
    private const byte Filled = 1;

    private const int GroundResolution = 3;

    // DATA
    public VolumeTheme theme;
    private DataVolume volume;

    private Extents volumeExtents;
    private Extents pieceExtents;

    // lookup arrays
    private NativeArray<PieceData> pieceConfigurationArray;
    private NativeArray<float3> forwardArray;

    // GENERATION JOBS
    private PieceVolume pieces;
    private JobHandle lastHandle;
    private JobHandle groundHandle;

    // MESH BUILDING
    private MeshFilter meshFilter;
    private Mesh mesh;

    public MeshFilter groundMeshFilter;
    private Mesh groundMesh;

    private GridMeshData groundMeshData;
    private NativeList<GridCell> groundGridCells;
    private NativeArray<int> groundMeshArrayLengths;
    private NativeArray<int> groundBorderIndices;

    void Awake()
    {
        // LOOKUPS
        pieceConfigurationArray = new NativeArray<PieceData>(PieceConfigurations.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        pieceConfigurationArray.CopyFrom(PieceConfigurations);

        forwardArray = new NativeArray<float3>(Directions.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        forwardArray.CopyFrom(Directions);

        // DATA
        int x = 16;
        int y = 16;
        int z = 16;

        volumeExtents = new Extents { x = x, y = y, z = z, xz = x * z };
        pieceExtents = new Extents { x = x + 1, y = y, z = z + 1, xz = (x + 1) * (z + 1) };

        volume = new DataVolume(x, y, z);
        pieces = new PieceVolume(x + 1, y, z + 1); // vertices points for the same grid size as the data volume

        // GROUND MESH
        if (groundMeshFilter != null)
        {
            groundMesh = new Mesh();
            groundMesh.MarkDynamic();

            groundMeshFilter.mesh = groundMesh;

            groundMeshData = new GridMeshData();
            groundGridCells = new NativeList<GridCell>();
        }

        // SIDE MESH
        mesh = new Mesh();
        mesh.MarkDynamic();

        meshFilter = GetComponent<MeshFilter>();
        meshFilter.mesh = mesh;

        // TEST DATA
        FillTestVolumeData();

        // GENERATE
        StartGeneration();
    }

    void Dispose()
    {
        volume.Dispose();
        pieces.Dispose();
        pieceConfigurationArray.Dispose();
        forwardArray.Dispose();

    }

    private void OnDestroy()
    {
        lastHandle.Complete();

        Dispose();
        DisposeTemp();
    }

    private void FillTestVolumeData()
    {
        /*
        volume.SetAt(1, 5, 1, Filled);
       
        volume.SetAt(1, 5, 5, Filled);
        volume.SetAt(2, 5, 5, Filled);
        volume.SetAt(2, 5, 6, Filled);
        volume.SetAt(3, 5, 5, Filled);
        volume.SetAt(2, 5, 4, Filled);
        
        volume.SetAt(1, 8, 5, Filled);
        volume.SetAt(1, 8, 6, Filled);
        volume.SetAt(1, 8, 7, Filled);

        volume.SetAt(1, 11, 10, Filled);
        volume.SetAt(1, 11, 11, Filled);
        volume.SetAt(1, 11, 12, Filled);
        volume.SetAt(1, 11, 13, Filled);
        */
        /*
        volume.SetAt(5, 5, 8, Filled);
        volume.SetAt(5, 5, 9, Filled);
        volume.SetAt(6, 5, 7, Filled);
        volume.SetAt(6, 5, 8, Filled);

        volume.SetAt(10, 5, 8, Filled);
        volume.SetAt(11, 5, 8, Filled);
        volume.SetAt(11, 5, 9, Filled);
        volume.SetAt(12, 5, 9, Filled);

        volume.SetAt(5, 5, 10, Filled);
        volume.SetAt(5, 5, 11, Filled);
        volume.SetAt(5, 5, 12, Filled);
        volume.SetAt(4, 5, 11, Filled);
        volume.SetAt(6, 5, 11, Filled);

        */
        //  volume.SetAt(7, 5, 5, Filled);


        
        int x = 0;
        int z = 0;

        for (x = 0; x < 16; ++x)
        {
            for (z = 0; z < 16; ++z)
            {
                volume.SetAt(x, 0, z, Filled);
                volume.SetAt(x, 1, z, Filled);

                if (x < 6 || z < 6)
                {
                    if (x == 3 || x == 5 || z == 3 || z == 5) { }
                    else
                    {
                        volume.SetAt(x, 2, z, Filled);
                    }
                }

                if (x >= 5 && z < 6)
                {
                    volume.SetAt(x, 3, z, Filled);
                }
            }
        }
        
        x = 5;
        z = 5;
        volume.SetAt(x, 3, z, Filled);
        volume.SetAt(x, 4, z, Filled);
        volume.SetAt(x, 5, z, Filled);

        x = 7;
        z = 5;
        volume.SetAt(x, 3, z, Filled);
        volume.SetAt(x, 4, z, Filled);
        volume.SetAt(x, 5, z, Filled);

        x = 6;
        z = 4;
        volume.SetAt(x, 3, z, Filled);
        volume.SetAt(x, 4, z, Filled);
        volume.SetAt(x, 5, z, Filled);
        volume.SetAt(x, 6, z, Filled);
        volume.SetAt(x, 7, z, Filled);
        /*
        x = 6;
        z = 6;
        volume.SetAt(x, 3, z, Filled);
        volume.SetAt(x, 4, z, Filled);
        volume.SetAt(x, 5, z, Filled);
        */
        /*
        x = 6;
        volume.SetAt(x, 2, z, Empty);
        
        for (x = 0; x < 10; ++x)
        {
            for (z = 0; z < 10; ++z)
            {
                volume.SetAt(x, 0, z, Filled);
                volume.SetAt(x, 1, z, Filled);
                volume.SetAt(x, 2, z, Filled);
            }
        }*/
    }

    private void LateUpdate()
    {
        if (generating)
        {
            EndGeneration();
            generating = false;
        }
    }

    private bool generating = false;
    private NativeList<PieceDataInfo> tempPieceList;
    private NativeArray<MeshPieceWrapper> tempCombineArray;

    private void DisposeTemp()
    {
        if (tempPieceList.IsCreated) tempPieceList.Dispose();
        if (tempCombineArray.IsCreated) tempCombineArray.Dispose();

        if (groundGridCells.IsCreated) groundGridCells.Dispose();
        if (groundMeshArrayLengths.IsCreated) groundMeshArrayLengths.Dispose();
        if (groundBorderIndices.IsCreated) groundBorderIndices.Dispose();
    }

    void StartGeneration()
    {
        lastHandle.Complete();
        DisposeTemp();
        generating = true;

        // calculate the pieces in a volume
        var pieceGenerationJob = new GeneratePieceDataJob
        {
            volumeExtents = volumeExtents,
            pieceExtents = pieceExtents,
            volumeData = volume.Data,
            pieceConfigurations = pieceConfigurationArray,
            pieces = pieces.Data
        };
        lastHandle = pieceGenerationJob.Schedule(pieces.Data.Length, 256, lastHandle);

        // collect the pieces which needs to be processed
        tempPieceList = new NativeList<PieceDataInfo>((int)(pieces.Data.Length * 0.2f), Allocator.Temp);
        var pieceListGeneration = new GeneratePieceDataInfoList
        {
            pieceExtents = pieceExtents,
            pieces = pieces.Data,
            result = tempPieceList
        };
        lastHandle = pieceListGeneration.Schedule(lastHandle);

        // generate the CombineInstance structs based on the list of pieces
        lastHandle.Complete();
        tempCombineArray = new NativeArray<MeshPieceWrapper>(tempPieceList.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        var combineList = new GenerateCombineInstances
        {
            forwards = forwardArray,
            pieces = tempPieceList,
            result = tempCombineArray
        };
        lastHandle = combineList.Schedule(tempPieceList.Length, 16, lastHandle);

        // ground generation
        groundGridCells = new NativeList<GridCell>(Allocator.Temp);
        groundMeshArrayLengths = new NativeArray<int>(GenerateMeshGridCells.BufferLengthCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        var generateGroundGridCells = new GenerateMeshGridCells
        {
            resolution = GroundResolution,
            volumeExtent = volumeExtents,
            data = volume.Data,
            cells = groundGridCells,
            bufferLengths = groundMeshArrayLengths
        };

        groundHandle = generateGroundGridCells.Schedule();

        groundHandle.Complete();

        groundMeshData.CreateMeshBuffers(groundMeshArrayLengths[GenerateMeshGridCells.VertexCountIndex], groundMeshArrayLengths[GenerateMeshGridCells.TriangleCountIndex], true);
        groundBorderIndices = new NativeArray<int>(groundMeshArrayLengths[GenerateMeshGridCells.BorderIndexCountIndex], Allocator.Temp);

        var generateMeshData = new GenerateMeshData
        {
            cellResolution = GroundResolution,
            volumeExtent = volumeExtents,
            cells = groundGridCells,
            borderIndices = groundBorderIndices,
            meshVertices = groundMeshData.Vertices,
            meshUVs = groundMeshData.UVs,
            meshTriangles = groundMeshData.Tris
        };

        groundHandle = generateMeshData.Schedule(groundHandle);

        JobHandle.ScheduleBatchedJobs();
    }

    void EndGeneration()
    {
        generating = false;

        lastHandle.Complete();

        var array = new CombineInstance[tempCombineArray.Length];
        for (int i = 0; i < tempCombineArray.Length; ++i)
        {
            var c = tempCombineArray[i];
            c.combine.mesh = theme.GetMesh(c.elem, 0);
            array[i] = c.combine;
        }

        mesh.CombineMeshes(array, true, true);
        meshFilter.mesh = mesh;

        groundHandle.Complete();

        groundMeshData.UpdateMesh(groundMesh);

        groundGridCells.Dispose();
        groundBorderIndices.Dispose();
        groundMeshData.Dispose();

        DisposeTemp();
    }

    internal enum Rotation : byte
    {
        CW0 = 0,
        CW90,
        CW180,
        CW270
    }

    private const byte DirXPlus = 1;
    private const byte DirXMinus = 2;
    private const byte DirZPlus = 4;
    private const byte DirZMinus = 8;
    private const byte DirAll = DirXPlus | DirXMinus | DirZPlus | DirZMinus;

    private const byte FilledPiece = 0;
    private const byte EmptyPiece = 0;
    
    internal struct Extents
    {
        public int x;
        public int y;
        public int z;
        public int xz;
    }

    internal struct PieceData
    {
        public PieceElem piece;
        public Rotation rot;
        public byte visibility;
        public byte variation;
    }

    internal struct PieceDataInfo
    {
        public int3 coord;
        public PieceData data;
    }

    [BurstCompile]
    internal struct GeneratePieceDataJob : IJobParallelFor
    {
        // pieces are generated on the vertices points of the grid
        // on a single layer, at every vertex 4 cells join
        // the flags mean which cells are filled from the point of view of the vertex
        // the piece will be chosen based on the configuration of the filled cells
        // top Z+ / right X+
        private const byte HasBL = 1;
        private const byte HasTL = 2;
        private const byte HasBR = 4;
        private const byte HasTR = 8;

        public Extents pieceExtents;
        public Extents volumeExtents;

        [ReadOnly] public NativeArray<PieceData> pieceConfigurations;
        [ReadOnly] public NativeArray<byte> volumeData;
        [WriteOnly] public NativeArray<PieceData> pieces;

        public void Execute(int index)
        {
            byte configuration = 0;

            int3 c = CoordFromIndex(index, pieceExtents);
            int volumeIndex = IndexFromCoord(c, volumeExtents);

            if (c.z < volumeExtents.z)
            {
                if (c.x < volumeExtents.x && IsFilled(volumeIndex)) { configuration |= HasTR; }
                if (c.x > 0 && IsFilled(volumeIndex - 1)) { configuration |= HasTL; }
            }
            if (c.z > 0)
            {
                // the bottom checks are from the previous row
                volumeIndex -= volumeExtents.x;
                if (c.x < volumeExtents.x && IsFilled(volumeIndex)) { configuration |= HasBR; }
                if (c.x > 0 && IsFilled(volumeIndex - 1)) { configuration |= HasBL; }
            }

            pieces[index] = pieceConfigurations[configuration];
        }

        private bool IsFilled(int index)
        {
            return volumeData[index] == Filled;
        }
    }

    internal struct GeneratePieceDataInfoList : IJob
    {
        private static PieceData NullPiece = new PieceData { piece = PieceElem.Null, visibility = 0 };

        public Extents pieceExtents;
        [ReadOnly] public NativeArray<PieceData> pieces;
        [WriteOnly] public NativeList<PieceDataInfo> result;

        public void Execute()
        {
            for (int i = 0; i < pieces.Length; ++i)
            {
                if (pieces[i].piece != PieceElem.Null)
                {
                    var c = CoordFromIndex(i, pieceExtents);
                    var info = new PieceDataInfo { coord = c, data = pieces[i] };

                    var piece = pieces[i].piece;
                    var rot = pieces[i].rot;

                    PieceData under = c.y > 0 ? pieces[i - pieceExtents.xz] : NullPiece;
                    PieceData above = c.y < pieceExtents.y - 1 ? pieces[i + pieceExtents.xz] : NullPiece;

                    if (above.piece == PieceElem.Null) { info.data.piece = TopVersion(info.data); }
                    else if (piece == PieceElem.Side && above.piece == PieceElem.CornerConvex) { info.data.piece = PieceElem.TopSide; }
                    else if (piece != PieceElem.Side && (above.piece != PieceElem.Side && (piece != above.piece || rot != above.rot))) { info.data.piece = TopVersion(info.data); }
                    else if (piece != PieceElem.Side && ((piece != under.piece || rot != under.rot))) { info.data.piece = BottomVersion(info.data); }
                    else if (under.piece == PieceElem.Null) { info.data.piece = BottomVersion(info.data); }

                    result.Add(info);
                }
            }
        }

        static private PieceElem TopVersion(PieceData data)
        {
            return (PieceElem)((byte)data.piece - 4);
        }
        static private PieceElem BottomVersion(PieceData data)
        {
            return (PieceElem)((byte)data.piece + 4);
        }
    }

    [BurstCompile]
    internal struct GenerateCombineInstances : IJobParallelFor
    {
        private static readonly float3 Up = new float3 { x = 0, y = 1, z = 0 };

        [ReadOnly] public NativeArray<float3> forwards;

        [ReadOnly] public NativeArray<PieceDataInfo> pieces;
        [WriteOnly] public NativeArray<MeshPieceWrapper> result;

        public void Execute(int index)
        {
            PieceData piece = pieces[index].data;
            int3 c = pieces[index].coord;

            float3 p = new float3 { x = c.x * CellSize, y = c.y * CellSize, z = c.z * CellSize };
            float4x4 m = float4x4.lookAt(p, forwards[(byte)piece.rot], Up);

            result[index] = new MeshPieceWrapper
            {
                elem = piece.piece,
                combine = new CombineInstance { subMeshIndex = 0, transform = ToMatrix4x4(m) }
            };
        }
        
        private static Matrix4x4 ToMatrix4x4(float4x4 m)
        {
            return new Matrix4x4(m.c0, m.c1, m.c2, m.c3);
        }
    }

    internal struct GenerateMeshGridCells : IJob
    {
        public const int VertexCountIndex = 0;
        public const int TriangleCountIndex = 1;
        public const int BorderIndexCountIndex = 2;
        public const int BufferLengthCount = 3;

        public int resolution;
        public Extents volumeExtent;
        [ReadOnly] public NativeArray<byte> data;

        [WriteOnly] public NativeList<GridCell> cells;
        [WriteOnly] public NativeArray<int> bufferLengths;

        public void Execute()
        {
            int vertexCount = 0;

            int cellCount = 0;

            int[] bottom = new int[volumeExtent.x];
            
            int dataIndex = 0;
            for (int y = 0; y < volumeExtent.y; ++y)
            {
                for (int i = 0; i < bottom.Length; ++i)
                {
                    bottom[i] = -1;
                }

                for (int z = 0; z < volumeExtent.z; ++z)
                {
                    int left = -1;
                    for (int x = 0; x < volumeExtent.x; ++x)
                    {
                        if (data[dataIndex] == Filled && (y == volumeExtent.y - 1 || data[dataIndex + volumeExtent.xz] != Filled))
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

            bufferLengths[VertexCountIndex] = vertexCount;
            bufferLengths[TriangleCountIndex] = triangleCount;
            bufferLengths[BorderIndexCountIndex] = borderIndexCount;
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

        public Extents volumeExtent;
        public NativeArray<GridCell> cells;

        public NativeArray<int> borderIndices;

        [WriteOnly] public NativeArray<float3> meshVertices;
        [WriteOnly] public NativeArray<int> meshTriangles;
        [WriteOnly] public NativeArray<float2> meshUVs;

        private float stepX;
        private float stepZ;

        public void Execute()
        {
            stepX = CellSize / cellResolution;
            stepZ = CellSize / cellResolution;

            int boundaryIndex = 0;
            int sideVertexCount = cellResolution + 1;

            int vertexIndex = 0;
            int triangleIndex = 0;
            for (int i = 0; i < cells.Length; ++i)
            {
                var cell = cells[i];

                float x = cell.coord.x * CellSize;
                float y = (cell.coord.y + 0.5f) * CellSize;
                float z = cell.coord.z * CellSize;

                Range left = cell.leftCell >= 0 ? cells[cell.leftCell].rightBorderIndices : NullRange;
                Range bottom = cell.bottomCell >= 0 ? cells[cell.bottomCell].topBorderIndices : NullRange;

                var right = new Range { start = boundaryIndex, end = boundaryIndex + sideVertexCount - 1 };
                boundaryIndex += sideVertexCount;
                var top = new Range { start = boundaryIndex, end = boundaryIndex + sideVertexCount - 1 };
                boundaryIndex += sideVertexCount;

                GenerateGrid(x, y, z, left, bottom, right, top, ref vertexIndex, ref triangleIndex);

                cell.rightBorderIndices = right;
                cell.topBorderIndices = top;
                cells[i] = cell;
            }
        }

        private void GenerateGrid(float startX, float startY, float startZ, Range left, Range bottom, Range right, Range top, ref int vertexIndex, ref int triangleIndex)
        {
            float3 v = new float3 { x = startX, y = startY, z = startZ };
            float2 uv = new float2 { x = 0, y = 0 };
            int c0, c1, c2, c3;
            int width = cellResolution + 1;
            int rowOffset = left.IsValid ? width - 1 : width;
            int colOffset = left.IsValid ? -1 : 0;
            for (int y = 0; y < cellResolution; ++y)
            {
                v.x = startX;
                uv.x = 0;

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
                    uv.x += stepX;
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
                uv.y += stepZ;
            }

            // top row vertices
            v.x = startX;
            uv.x = 0;

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
                uv.x += stepX;
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

    internal struct MeshPieceWrapper
    {
        public PieceElem elem;
        public int variation;
        public CombineInstance combine;
    }

    internal struct Range
    {
        public int start;
        public int end;

        public bool IsValid { get { return start != end; } }
        public int At(int index, NativeArray<int> indices) { return indices[start + index]; }
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

    static readonly PieceData[] PieceConfigurations = new PieceData[]
    {
            new PieceData { piece = PieceElem.Null, visibility = EmptyPiece },                                                                   // 0 - nothing, no mesh
            new PieceData { piece = PieceElem.CornerConvex, rot = Rotation.CW270, visibility = DirXMinus | DirZMinus }, // 1 - bottom left is filled
            new PieceData { piece = PieceElem.CornerConvex, rot = Rotation.CW0, visibility = DirXMinus | DirZPlus  },   // 2 - top left
            new PieceData { piece = PieceElem.Side, rot = Rotation.CW270, visibility = DirXMinus },                     // 3 - left
            new PieceData { piece = PieceElem.CornerConvex, rot = Rotation.CW180, visibility = DirXPlus | DirZMinus },  // 4 - bottom right
            new PieceData { piece = PieceElem.Side, rot = Rotation.CW180, visibility = DirZMinus },                     // 5 - bottom
            new PieceData { piece = PieceElem.DoubleConcave, rot = Rotation.CW90, visibility = DirAll },                 // 6 - diagonal
            new PieceData { piece = PieceElem.CornerConcave, rot = Rotation.CW270, visibility = DirXMinus | DirZMinus },// 7 - no top right
            new PieceData { piece = PieceElem.CornerConvex, rot = Rotation.CW90, visibility = DirXPlus | DirZPlus },    // 8 - top right
            new PieceData { piece = PieceElem.DoubleConcave, rot = Rotation.CW0, visibility = DirAll },                 // 9 - diagonal
            new PieceData { piece = PieceElem.Side, visibility = DirZPlus },                                            // 10 - top
            new PieceData { piece = PieceElem.CornerConcave, rot = Rotation.CW0, visibility = DirXMinus | DirZPlus },   // 11 - no bottom right
            new PieceData { piece = PieceElem.Side, rot = Rotation.CW90, visibility = DirXPlus },                       // 12 - right
            new PieceData { piece = PieceElem.CornerConcave, rot = Rotation.CW180, visibility = DirXMinus | DirZMinus },// 13 - no top left
            new PieceData { piece = PieceElem.CornerConcave, rot = Rotation.CW90, visibility = DirXPlus | DirZPlus },   // 14 - no bottom left
            new PieceData { piece = PieceElem.Null, visibility = FilledPiece },                                                   // 15 - all, no mesh
    };

    private static readonly float3[] Directions = new float3[]
    {
        new float3 { x = 0, y = 0, z = 1 },
        new float3 { x = 1, y = 0, z = 0 },
        new float3 { x = 0, y = 0, z = -1 },
        new float3 { x = -1, y = 0, z = 0 }
    };

    // index - array index
    // xzLength - size of xz layer (xSize * zSize)
    // zLength - length along z axis
    static int3 CoordFromIndex(int index, int xzLength, int xLength)
    {
        int rem = index % xzLength;
        return new int3 { x = rem % xLength, y = index / xzLength, z = rem / xLength };
    }

    static int3 CoordFromIndex(int index, Extents ext)
    {
        int rem = index % ext.xz;
        return new int3 { x = rem % ext.x, y = index / ext.xz, z = rem / ext.x };
    }

    static int IndexFromCoord(int3 c, int xzLength, int xLength)
    {
        return c.y * xzLength + c.z * xLength + c.x;
    }

    static int IndexFromCoord(int3 c, Extents ext)
    {
        return c.y * ext.xz + c.z * ext.x + c.x;
    }

    static int IndexFromCoord(int x, int y, int z, int xzLength, int xLength)
    {
        return y * xzLength + z * xLength + x;
    }
}