using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

using DataVolume = Volume<byte>;
using PieceVolume = Volume<GridMesher.PieceData>;
using PieceElem = VolumeTheme.Elem;
using IndexVolume = Volume<int>;

// NOTE:
// the piece data volume has to have twice as large layers (x, z axis), pieces are half the size of cells
[RequireComponent(typeof(MeshFilter))]
public class GridMesher : MonoBehaviour
{
    private const float CellSize = 1f;
    private const float HalfCellSize = CellSize * 0.5f;

    private const byte Empty = 0;
    private const byte Filled = 1;

    // DATA
    public VolumeTheme theme;
    private DataVolume volume;

    private Extents volumeExtents;
    private Extents pieceExtents;

    private NativeList<float3> groundVertices;
    private IndexVolume groundIndices;

    // lookup arrays
    private NativeArray<PieceData> pieceConfigurationArray;
    private NativeArray<float3> forwardArray;

    // GENERATION JOBS
    private PieceVolume pieces;
    private JobHandle lastHandle;

    // MESH BUILDING
    private MeshFilter meshFilter;
    private Mesh mesh;

    public MeshFilter groundMeshFilter;
    private Mesh groundMesh;

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
        int x = 0;
        int z = 0;

        for (x = 0; x < 10; ++x)
        {
            for (z = 0; z < 10; ++z)
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

    internal struct MeshPieceWrapper
    {
        public PieceElem elem;
        public int variation;
        public CombineInstance combine;
    }
    /*
    static readonly PieceData[] PieceConfigurations = new PieceData[]
    {
            new PieceData { piece = PieceElem.Null },                                                                   // 0 - nothing, this is invalid configuration 
            new PieceData { piece = PieceElem.CornerConvex, rot = Rotation.CW180, visibility = DirXMinus | DirZMinus }, // 1 - bottom left is filled
            new PieceData { piece = PieceElem.CornerConvex, rot = Rotation.CW270, visibility = DirXMinus | DirZPlus  }, // 2 - top left
            new PieceData { piece = PieceElem.Side, rot = Rotation.CW270, visibility = DirXMinus },                     // 3 - left
            new PieceData { piece = PieceElem.CornerConvex, rot = Rotation.CW90, visibility = DirXPlus | DirZMinus },   // 4 - bottom right
            new PieceData { piece = PieceElem.Side, rot = Rotation.CW180, visibility = DirZMinus },                     // 5 - bottom
            new PieceData { piece = PieceElem.DoubleConcave, visibility = DirAll },                                     // 6 - diagonal
            new PieceData { piece = PieceElem.CornerConcave, rot = Rotation.CW180, visibility = DirXMinus | DirZMinus },// 7 - no top right
            new PieceData { piece = PieceElem.CornerConvex, visibility = DirXPlus | DirZPlus },                         // 8 - top right
            new PieceData { piece = PieceElem.DoubleConcave, rot = Rotation.CW90, visibility = DirAll },                // 9 - diagonal
            new PieceData { piece = PieceElem.Side, visibility = DirZPlus },                                            // 10 - top
            new PieceData { piece = PieceElem.CornerConcave, rot = Rotation.CW270, visibility = DirXMinus | DirZPlus }, // 11 - no bottom right
            new PieceData { piece = PieceElem.Side, rot = Rotation.CW90, visibility = DirXPlus },                       // 12 - right
            new PieceData { piece = PieceElem.CornerConcave, rot = Rotation.CW90, visibility = DirXMinus | DirZMinus }, // 13 - no top left
            new PieceData { piece = PieceElem.CornerConcave, visibility = DirXPlus | DirZPlus },                        // 14 - no bottom left
            new PieceData { piece = PieceElem.Null },                                                                   // 15 - all, this is invalid configuration 
    };*/

    static readonly PieceData[] PieceConfigurations = new PieceData[]
    {
            new PieceData { piece = PieceElem.Null, visibility = DirAll },                                                                   // 0 - nothing, no mesh
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
            new PieceData { piece = PieceElem.Null, visibility = 0 },                                                   // 15 - all, no mesh
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