using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using System.Runtime.InteropServices;

using static MeshBuilder.Extents;

using TileData = MeshBuilder.Tile.Data;
using TileType = MeshBuilder.Tile.Type;
using DataVolume = MeshBuilder.Volume<MeshBuilder.Tile.Data>; // type values
using TileVolume = MeshBuilder.Volume<MeshBuilder.TileMesher3D.TileMeshData>; // configuration indices
using ConfigTransformGroup = MeshBuilder.TileTheme.ConfigTransformGroup;
using PieceTransform = MeshBuilder.Tile.PieceTransform;
using Direction = MeshBuilder.Tile.Direction;

namespace MeshBuilder
{
    public class TileMesher3D : TileMesherBase<TileMesher3D.TileMeshData>
    {
        private const string DefaultName = "tile_mesh_3d";
        static private readonly Settings DefaultSettings = new Settings { };

        // INITIAL DATA
        private TileTheme theme;
        private DataVolume data;
        private Settings settings;

        // in the data volume we're generating the mesh
        // for this value
        private int themeIndex;

        private Extents dataExtents;
        private Extents tileExtents;

        // GENERATED DATA
        private Volume<MeshTile> tileMeshes;

        // TEMP DATA
        private NativeList<MeshInstance> tempInstanceList;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="name">name of the mesher, mostly for logging and debugging purposes</param>
        public TileMesher3D(string name = DefaultName)
            : base(name)
        {

        }

        public void Init(DataVolume dataVolume, int themeIndex, TileThemePalette themePalette)
        {
            Init(dataVolume, themeIndex, themePalette, DefaultSettings);
        }

        public void Init(DataVolume dataVolume, int themeIndex, TileThemePalette themePalette, Settings settings)
        {
            Dispose();
            
            this.data = dataVolume;
            this.ThemePalette = themePalette;
            this.theme = themePalette.Get(themeIndex);
            this.themeIndex = themeIndex;
            this.settings = settings;

            if (theme.Configs.Length < TileTheme.Type3DConfigCount)
            {
                Error("The theme has less than the required number of configurations! " + theme.Configs.Length);
                state = State.Uninitialized;
                return;
            }

            int x = data.XLength;
            int y = data.YLength;
            int z = data.ZLength;
            dataExtents = new Extents(x, y, z);
            tileExtents = new Extents(x + 1, y + 1, z + 1);

            generationType = GenerationType.FromDataUncached;

            state = State.Initialized;
        }

        override protected void ScheduleGenerationJobs()
        {
            if (generationType == GenerationType.FromDataUncached)
            {
                if (HasTilesData)
                {
                    tiles.Dispose();
                }
                tiles = new TileVolume(tileExtents);

                if (HasTileMeshes)
                {
                    tileMeshes.Dispose();
                }

                tileMeshes = new Volume<MeshTile>(tileExtents);

                lastHandle = ScheduleTileGeneration(tiles, data, 64, lastHandle);
            }
            
            if (HasTilesData && HasTileMeshes)
            {
                lastHandle = ScheduleMeshTileGeneration(tileMeshes, tiles, 32, lastHandle);

                tempInstanceList = new NativeList<MeshInstance>(Allocator.Temp);
                lastHandle = ScheduleFillCombineInstanceList(tempInstanceList, tileMeshes, lastHandle);
            }
            else
            {
                if (!HasTilesData) Error("no tiles data!");
                if (!HasTileMeshes) Error("no mesh tiles data!");
            }
        }

        override protected void AfterGenerationJobsComplete()
        {
            if (tempInstanceList.IsCreated)
            {
                CombineMeshes(Mesh, tempInstanceList, theme);
            }

            if (state == State.Generating)
            {
                state = State.Initialized;
            }
        }

        protected override void DisposeTemp()
        {
            base.DisposeTemp();

            if (tempInstanceList.IsCreated)
            {
                tempInstanceList.Dispose();
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            if (tileMeshes != null)
            {
                tileMeshes.Dispose();
                tileMeshes = null;
            }
        }
        
        private JobHandle ScheduleTileGeneration(TileVolume resultTiles, DataVolume data, int batchCount, JobHandle dependOn)
        {
            var tileGeneration = new GenerateTileDataJob
            {
                tileExtents = tileExtents,
                dataExtents = dataExtents,
                themeIndex = themeIndex,
                configs = theme.Configs,
                data = data.Data,
                tiles = resultTiles.Data
            };

            return tileGeneration.Schedule(resultTiles.Data.Length, batchCount, dependOn);
        }

        private JobHandle ScheduleMeshTileGeneration(Volume<MeshTile> resultMeshTiles, TileVolume tiles, int batchCount, JobHandle dependOn)
        {
            var tileGeneration = new GenerateMeshDataJob
            {
                tileExtents = tileExtents,
                tiles = tiles.Data,
                meshTiles = resultMeshTiles.Data
            };

            return tileGeneration.Schedule(tiles.Data.Length, batchCount, dependOn);
        }

        private JobHandle ScheduleFillCombineInstanceList(NativeList<MeshInstance> resultList, Volume<MeshTile> meshTiles, JobHandle dependOn)
        {
            var fillList = new FillCombineInstanceListJob
            {
                meshTiles = meshTiles.Data,
                resultCombineInstances = resultList
            };

            return fillList.Schedule(dependOn);
        }

        /// <summary>
        /// Takes the simple tile data volume (theme index - variant index pairs) and turns them into a volume of
        /// TileMeshData which contains every information to find and transform the correct mesh pieces for rendering
        /// </summary>
        [BurstCompile]
        private struct GenerateTileDataJob : IJobParallelFor
        {
            public Extents tileExtents;
            public Extents dataExtents;
            public int themeIndex;

            [ReadOnly] public NativeArray<ConfigTransformGroup> configs;
            [ReadOnly] public NativeArray<TileData> data;
            [WriteOnly] public NativeArray<TileMeshData> tiles;

            public void Execute(int index)
            {
                byte configuration = 0;
                int3 dc = CoordFromIndex(index, tileExtents);

                bool hasLF, hasRF, hasLB, hasRB;
                if (dc.y < dataExtents.Y)
                {
                    hasLF = dc.x > 0 && dc.z < dataExtents.Z && IsFilled(dc.x - 1, dc.y, dc.z);
                    hasRF = dc.x < dataExtents.X && dc.z < dataExtents.Z && IsFilled(dc.x, dc.y, dc.z);
                    hasLB = dc.x > 0 && dc.z > 0 && IsFilled(dc.x - 1, dc.y, dc.z - 1);
                    hasRB = dc.x < dataExtents.X && dc.z > 0 && IsFilled(dc.x, dc.y, dc.z - 1);

                    if (hasLF) configuration |= Tile.TopLeftForward;
                    if (hasRF) configuration |= Tile.TopRightForward;
                    if (hasLB) configuration |= Tile.TopLeftBackward;
                    if (hasRB) configuration |= Tile.TopRightBackward;
                }
                if (dc.y > 0)
                {
                    hasLF = dc.x > 0 && dc.z < dataExtents.Z && IsFilled(dc.x - 1, dc.y - 1, dc.z);
                    hasRF = dc.x < dataExtents.X && dc.z < dataExtents.Z && IsFilled(dc.x, dc.y - 1, dc.z);
                    hasLB = dc.x > 0 && dc.z > 0 && IsFilled(dc.x - 1, dc.y - 1, dc.z - 1);
                    hasRB = dc.x < dataExtents.X && dc.z > 0 && IsFilled(dc.x, dc.y - 1, dc.z - 1);

                    if (hasLF) configuration |= Tile.BottomLeftForward;
                    if (hasRF) configuration |= Tile.BottomRightForward;
                    if (hasLB) configuration |= Tile.BottomLeftBackward;
                    if (hasRB) configuration |= Tile.BottomRightBackward;
                }

                var transformGroup = configs[configuration];
                var type = TileType.Normal;
                if (configuration == 0 || configuration == configs.Length - 1)
                {
                    type = TileType.Void;
                }
                tiles[index] = new TileMeshData { type = type, configTransformGroup = transformGroup, variant0 = 0, variant1 = 0 };
            }

            private bool IsFilled(int x, int y, int z)
            {
                int index = IndexFromCoord(x, y, z, dataExtents);
                return data[index].themeIndex == themeIndex;
            }
        }

        [BurstCompile]
        private struct GenerateMeshDataJob : IJobParallelFor
        {
            private const byte RotationMask = (byte)(PieceTransform.Rotate90 | PieceTransform.Rotate180 | PieceTransform.Rotate270);
            private const byte MirrorMask = (byte)PieceTransform.MirrorXYZ;

            public Extents tileExtents;
            [ReadOnly] public NativeArray<TileMeshData> tiles;
            [WriteOnly] public NativeArray<MeshTile> meshTiles;

            public void Execute(int index)
            {
                var tile = tiles[index];
                var group = tile.configTransformGroup;
                if (group.Count > 0 && tile.type == TileType.Normal)
                {
                    float3 pos = CoordFromIndex(index, tileExtents);
                    meshTiles[index] = CreateMeshTile(pos, group, tile);
                }
                else
                {
                    meshTiles[index] = new MeshTile { count = 0 };
                }
            }

            private MeshTile CreateMeshTile(float3 pos, ConfigTransformGroup group, TileMeshData tile)
            {
                switch(group.Count)
                {
                    case 1:
                        return new MeshTile
                        {
                            count = 1,
                            mesh0 = CreateMeshInstance(pos, 0, group, tile.variant0)
                        };
                    case 2:
                        return new MeshTile
                        {
                            count = 2,
                            mesh0 = CreateMeshInstance(pos, 0, group, tile.variant0),
                            mesh1 = CreateMeshInstance(pos, 1, group, tile.variant1)
                        };
                    case 3:
                        return new MeshTile
                        {
                            count = 3,
                            mesh0 = CreateMeshInstance(pos, 0, group, tile.variant0),
                            mesh1 = CreateMeshInstance(pos, 1, group, tile.variant1),
                            mesh2 = CreateMeshInstance(pos, 2, group, tile.variant2)
                        };
                    case 4:
                        return new MeshTile
                        {
                            count = 4,
                            mesh0 = CreateMeshInstance(pos, 0, group, tile.variant0),
                            mesh1 = CreateMeshInstance(pos, 1, group, tile.variant1),
                            mesh2 = CreateMeshInstance(pos, 2, group, tile.variant2),
                            mesh3 = CreateMeshInstance(pos, 3, group, tile.variant3)
                        };
                }

                return new MeshTile { count = 0 };
            }

            private MeshInstance CreateMeshInstance(float3 pos, int index, ConfigTransformGroup group, byte variant)
            {
                return new MeshInstance
                {
                    instance = CreateCombineInstance(pos, group[index].PieceTransform),
                    basePieceIndex = group[index].BaseMeshIndex,
                    variantIndex = variant
                };
            }

            private CombineInstance CreateCombineInstance(float3 pos, PieceTransform pieceTransform)
            {
                float4x4 transform = ToRotationMatrix(pieceTransform);
                
                if (HasFlag(pieceTransform, PieceTransform.MirrorX))
                {
                    transform = math.mul(ToScaleMatrix(PieceTransform.MirrorX), transform);
                }

                if (HasFlag(pieceTransform, PieceTransform.MirrorY))
                {
                    transform = math.mul(ToScaleMatrix(PieceTransform.MirrorY), transform);
                }

                MatrixConverter m = new MatrixConverter { };
                m.float4x4 = math.mul(float4x4.translate(pos), transform);

                return new CombineInstance { subMeshIndex = 0, transform = m.Matrix4x4 };
            }

            static private bool HasFlag(PieceTransform transform, PieceTransform flag)
            {
                return (byte)(transform & flag) != 0;
            }

            // NOTE: I wanted to use static readonly matrices instead of constructing new ones
            // but that didn't work with the Burst compiler
            static private float4x4 ToScaleMatrix(PieceTransform pieceTransform)
            {
                byte mirror = (byte)((byte)pieceTransform & MirrorMask);
                switch (mirror)
                {
                    case (byte)PieceTransform.MirrorX: return float4x4.scale(-1, 1, 1);
                    case (byte)PieceTransform.MirrorY: return float4x4.scale(1, -1, 1);
                    case (byte)PieceTransform.MirrorZ: return float4x4.scale(1, 1, -1);
                    case (byte)PieceTransform.MirrorXY: return float4x4.scale(-1, -1, 1);
                    case (byte)PieceTransform.MirrorXZ: return float4x4.scale(-1, 1, -1);
                    case (byte)PieceTransform.MirrorXYZ: return float4x4.scale(-1, -1, -1);
                }
                return float4x4.identity;
            }

            static private float4x4 ToRotationMatrix(PieceTransform pieceTransform)
            {
                byte rotation = (byte)((byte)pieceTransform & RotationMask);
                switch (rotation)
                {
                    case (byte)PieceTransform.Rotate90: return float4x4.rotateY(math.radians(-90));
                    case (byte)PieceTransform.Rotate180: return float4x4.rotateY(math.radians(-180));
                    case (byte)PieceTransform.Rotate270: return float4x4.rotateY(math.radians(-270));
                }
                return float4x4.identity;
            }

            /// <summary>
            /// Utility class for converting the matrxi data between the two different versions.
            /// The jobs use the float4x4 version to perform operations which might be faster, but the CombineInstance
            /// struct takes Matrix4x4.
            /// TODO: Check this later, later versions of Unity might accept float4x4 which would make this obsolete.
            /// NOTE: This is also used elsewhere, but when I moved it out from this struct to reuse it I got weird 
            /// errors and the Unity editor started to glitch
            /// </summary>
            [StructLayout(LayoutKind.Explicit)]
            private struct MatrixConverter
            {
                [FieldOffset(0)]
                public float4x4 float4x4;

                [FieldOffset(0)]
                public Matrix4x4 Matrix4x4;
            }
        }

        private struct FillCombineInstanceListJob : IJob
        {
            [ReadOnly] public NativeArray<MeshTile> meshTiles;
            [WriteOnly] public NativeList<MeshInstance> resultCombineInstances;

            public void Execute()
            {
                for (int i = 0; i < meshTiles.Length; ++i)
                {
                    var tile = meshTiles[i];
                    switch (tile.count)
                    {
                        case 1:
                            {
                                resultCombineInstances.Add(tile.mesh0);
                                break;
                            }
                        case 2:
                            {
                                resultCombineInstances.Add(tile.mesh0);
                                resultCombineInstances.Add(tile.mesh1);
                                break;
                            }
                        case 3:
                            {
                                resultCombineInstances.Add(tile.mesh0);
                                resultCombineInstances.Add(tile.mesh1);
                                resultCombineInstances.Add(tile.mesh2);
                                break;
                            }
                        case 4:
                            {
                                resultCombineInstances.Add(tile.mesh0);
                                resultCombineInstances.Add(tile.mesh1);
                                resultCombineInstances.Add(tile.mesh2);
                                resultCombineInstances.Add(tile.mesh3);
                                break;
                            }
                    }
                }
            }
        }

        private bool HasTileMeshes { get { return tileMeshes != null && !tileMeshes.IsDisposed; } }

        [System.Serializable]
        public class Settings
        {
            /// <summary>
            /// skip sides of the mesh, only full side elements get skipped (so borders, edges, corners stay intact),
            /// it doesn't modify the tile selection
            /// - which means smaller tunnels for example may stay unaffected (if they only contain edge pieces)
            /// - if the edge or corner tiles contain visible features (e.g. rocks standing out), this will keep them so
            /// so there is no visible change if the mesh is recalculated with different skipDirections
            /// </summary>
            public Direction skipDirections = Direction.None;

            /// <summary>
            /// skip every tile piece which covers a certain direction, this removes more tiles, but changes the selected tile
            /// pieces, it is recommended if the skip directions never change (not used for dynamic culling) or for certain tile sets
            /// where the change is not visible
            /// </summary>
            public Direction skipDirectionsAndBorders = Direction.None;

            /// <summary>
            /// When generating the mesh, should the algorithm consider the chunk boundaries empty?
            /// If the chunk represents ground for example, then the bottom and side boundaries should
            /// be NOT empty so those sides don't get generated. If the chunk is a flying island which
            /// needs all of its sides rendered, then the boundaries should be considered empty.
            /// </summary>
            public Direction emptyBoundaries = Direction.All;
        }

        /// <summary>
        /// The tile data required to render a piece.
        /// </summary>
        public struct TileMeshData
        {
            public TileType type;
            public ConfigTransformGroup configTransformGroup;

            public byte variant0;
            public byte variant1;
            public byte variant2;
            public byte variant3;
        }
        
        /// <summary>
        /// a cell may have two meshes
        /// </summary>
        private struct MeshTile
        {
            public byte count;

            public MeshInstance mesh0;
            public MeshInstance mesh1;
            public MeshInstance mesh2;
            public MeshInstance mesh3;
        }
    }
}
