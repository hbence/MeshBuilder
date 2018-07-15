using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

using static MeshBuilder.Extents;

using DataVolume = MeshBuilder.Volume<byte>;
using PieceVolume = MeshBuilder.Volume<MeshBuilder.TileMesher.PieceData>;
using PieceElem = MeshBuilder.VolumeTheme.Elem;

namespace MeshBuilder
{
    public class TileMesher : IMeshBuilder
    {
        private const byte Empty = 0;
        private const byte Filled = 1;

        // DATA
        private VolumeTheme theme;
        private DataVolume data;
        private float cellSize = 1f;

        private Extents dataExtents;
        private Extents pieceExtents;

        private bool inited = false;
        private bool isGenerating = false;

        // temps
        private NativeList<PieceDataInfo> pieceList;
        private NativeArray<MeshPieceWrapper> combineArray;

        // lookup arrays
        private NativeArray<PieceData> pieceConfigurationArray;
        private NativeArray<float3> forwardArray;

        // GENERATION JOBS
        private PieceVolume pieces;
        private JobHandle lastHandle;

        // MESH BUILDING
        private Mesh mesh;

        public TileMesher()
        {
            // SIDE MESH
            mesh = new Mesh();
        }

        public void Init(DataVolume data, VolumeTheme theme, float cellSize)
        {
            inited = true;

            this.data = data;
            this.theme = theme;
            this.cellSize = cellSize;

            int x = data.XLength;
            int y = data.YLength;
            int z = data.ZLength;
            dataExtents = new Extents(x, y, z);
            pieceExtents = new Extents(x + 1, y, z + 1);

            if (pieces != null)
            {
                pieces.Dispose();
            }

            pieces = new PieceVolume(x + 1, y, z + 1); // vertices points for the same grid size as the data volume
            CreateLookupArrays();
        }

        private void CreateLookupArrays()
        {
            if (!pieceConfigurationArray.IsCreated)
            {
                pieceConfigurationArray = new NativeArray<PieceData>(PieceConfigurations.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                pieceConfigurationArray.CopyFrom(PieceConfigurations);
            }

            if (!forwardArray.IsCreated)
            {
                forwardArray = new NativeArray<float3>(Directions.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                forwardArray.CopyFrom(Directions);
            }
        }

        public void Dispose()
        {
            lastHandle.Complete();
            DisposeTemp();

            if (pieces != null)
            {
                pieces.Dispose();
                pieces = null;
            }

            if (pieceConfigurationArray.IsCreated) pieceConfigurationArray.Dispose();
            if (forwardArray.IsCreated) forwardArray.Dispose();
        }

        private void DisposeTemp()
        {
            if (pieceList.IsCreated) pieceList.Dispose();
            if (combineArray.IsCreated) combineArray.Dispose();
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

            // calculate the pieces in a volume
            var pieceGenerationJob = new GeneratePieceDataJob
            {
                volumeExtents = dataExtents,
                pieceExtents = pieceExtents,
                volumeData = data.Data,
                pieceConfigurations = pieceConfigurationArray,
                pieces = pieces.Data
            };
            lastHandle = pieceGenerationJob.Schedule(pieces.Data.Length, 256, lastHandle);

            // collect the pieces which needs to be processed
            pieceList = new NativeList<PieceDataInfo>((int)(pieces.Data.Length * 0.2f), Allocator.Temp);
            var pieceListGeneration = new GeneratePieceDataInfoList
            {
                pieceExtents = pieceExtents,
                pieces = pieces.Data,
                result = pieceList
            };
            lastHandle = pieceListGeneration.Schedule(lastHandle);

            lastHandle.Complete();

            // generate the CombineInstance structs based on the list of pieces
            combineArray = new NativeArray<MeshPieceWrapper>(pieceList.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var combineList = new GenerateCombineInstances
            {
                cellSize = cellSize,
                forwards = forwardArray,
                pieces = pieceList.ToDeferredJobArray(),
                result = combineArray
            };
            lastHandle = combineList.Schedule(pieceList.Length, 16, lastHandle);

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

            var array = new CombineInstance[combineArray.Length];
            for (int i = 0; i < combineArray.Length; ++i)
            {
                var c = combineArray[i];
                c.combine.mesh = theme.GetMesh(c.elem, 0);
                array[i] = c.combine;
            }

            mesh.CombineMeshes(array, true, true);

            DisposeTemp();
        }

        public Mesh Mesh { get { return mesh; } }
        public bool IsGenerating { get { return isGenerating; } }

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

                if (c.z < volumeExtents.Z)
                {
                    if (c.x < volumeExtents.X && IsFilled(volumeIndex)) { configuration |= HasTR; }
                    if (c.x > 0 && IsFilled(volumeIndex - 1)) { configuration |= HasTL; }
                }
                if (c.z > 0)
                {
                    // the bottom checks are from the previous row
                    volumeIndex -= volumeExtents.X;
                    if (c.x < volumeExtents.X && IsFilled(volumeIndex)) { configuration |= HasBR; }
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

                        PieceData under = c.y > 0 ? pieces[i - pieceExtents.XZ] : NullPiece;
                        PieceData above = c.y < pieceExtents.Y - 1 ? pieces[i + pieceExtents.XZ] : NullPiece;

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

            public float cellSize;
            [ReadOnly] public NativeArray<float3> forwards;

            [ReadOnly] public NativeArray<PieceDataInfo> pieces;
            [WriteOnly] public NativeArray<MeshPieceWrapper> result;

            public void Execute(int index)
            {
                PieceData piece = pieces[index].data;
                int3 c = pieces[index].coord;

                float3 p = new float3 { x = c.x * cellSize, y = c.y * cellSize, z = c.z * cellSize };
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
    }
}