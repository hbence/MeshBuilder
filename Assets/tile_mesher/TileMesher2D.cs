using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using System.Runtime.InteropServices;

using static MeshBuilder.Extents;

using TileData = MeshBuilder.Tile.Data;
using DataVolume = MeshBuilder.Volume<MeshBuilder.Tile.Data>; // type values
using TileVolume = MeshBuilder.Volume<MeshBuilder.TileMesher2D.TileMeshData>; // configuration indices
using ConfigTransformGroup = MeshBuilder.TileTheme.ConfigTransformGroup;
using ConfigTransform = MeshBuilder.TileTheme.ConfigTransform;
using Direction = MeshBuilder.Tile.Direction;

namespace MeshBuilder
{
    public class TileMesher2D : TileMesherBase<TileMesher2D.TileMeshData>
    {
        private const string DefaultName = "tile_mesh_2d";
        static private readonly Settings DefaultSettings = new Settings { };

        // INITIAL DATA
        private TileTheme theme;
        private DataVolume data;
        private Settings settings;

        // in the data volume we're generating the mesh
        // for this value
        private int themeIndex;

        private int yLevel;

        private Extents dataExtents;
        private Extents tileExtents;

        // GENERATED DATA
        private Volume<MeshTile> tileMeshes;

        // TEMP DATA
        private NativeList<MeshInstance> tempInstanceList;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="name">name of the mesher, mostly for logging and debug purposes</param>
        public TileMesher2D(string name = DefaultName)
        {
            this.name = name;

            Mesh = new Mesh();
            Mesh.name = name;
        }

        public void Init(DataVolume dataVolume, int yLevel, int themeIndex, TileThemePalette themePalette)
        {
            Init(dataVolume, yLevel, themeIndex, themePalette, DefaultSettings);
        }

        public void Init(DataVolume dataVolume, int yLevel, int themeIndex, TileThemePalette themePalette, Settings settings)
        {
            Dispose();

            this.data = dataVolume;
            this.ThemePalette = themePalette;
            this.theme = themePalette.Get(themeIndex);
            this.yLevel = Mathf.Clamp(yLevel, 0, dataVolume.YLength - 1);
            this.themeIndex = themeIndex;
            this.settings = settings;

            if (this.yLevel != yLevel)
            {
                Debug.Log("yLevel is out of bounds:" + yLevel);
            }

            int x = data.XLength;
            int y = data.YLength;
            int z = data.ZLength;
            dataExtents = new Extents(x, y, z);
            tileExtents = new Extents(x + 1, 1, z + 1);

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
                tiles = new TileVolume(tileExtents.X, 1, tileExtents.Z);

                if (HasTileMeshes)
                {
                    tileMeshes.Dispose();
                }

                tileMeshes = new Volume<MeshTile>(tileExtents.X, 1, tileExtents.Z);

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
                yLevel = yLevel,
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
                yLevel = yLevel,
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

        private bool HasTileMeshes { get { return tileMeshes != null && !tileMeshes.IsDisposed; } }

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
            public int yLevel;

            [ReadOnly] public NativeArray<ConfigTransformGroup> configs;
            [ReadOnly] public NativeArray<TileData> data;
            [WriteOnly] public NativeArray<TileMeshData> tiles;

            public void Execute(int index)
            {
                byte configuration = 0;
                int3 dc = CoordFromIndex(index, tileExtents);

                bool hasLF = dc.x > 0 && dc.z < dataExtents.Z && IsFilled(dc.x - 1, yLevel, dc.z);
                bool hasRF = dc.x < dataExtents.X && dc.z < dataExtents.Z && IsFilled(dc.x, yLevel, dc.z);
                bool hasLB = dc.x > 0 && dc.z > 0 && IsFilled(dc.x - 1, yLevel, dc.z - 1);
                bool hasRB = dc.x < dataExtents.X && dc.z > 0 && IsFilled(dc.x, yLevel, dc.z - 1);

                if (hasLF) configuration |= Tile.LeftForward;
                if (hasRF) configuration |= Tile.RightForward;
                if (hasLB) configuration |= Tile.LeftBackward;
                if (hasRB) configuration |= Tile.RightBackward;

                var transformGroup = configs[configuration];

                tiles[index] = new TileMeshData { type = TileType.Normal, configTransformGroup = transformGroup, variant0 = 0, variant1 = 0 };
            }

            private bool IsFilled(int x, int y, int z)
            {
                int index = IndexFromCoord(x, y, z, dataExtents);
                return data[index].themeIndex == themeIndex;
            }
        }

    //    [BurstCompile]
        private struct GenerateMeshDataJob : IJobParallelFor
        {
            public Extents tileExtents;
            public int yLevel;
            [ReadOnly] public NativeArray<TileMeshData> tiles;
            [WriteOnly] public NativeArray<MeshTile> meshTiles;

            public void Execute(int index)
            {
                var tile = tiles[index];
                var group = tile.configTransformGroup;
                if (group.Count > 0 && tile.type == TileType.Normal)
                {
                    float3 pos = CoordFromIndex(index, tileExtents);
                    pos.y += yLevel;

                    if (group.Count == 2)
                    {
                        meshTiles[index] = new MeshTile
                        {
                            count = 2,

                            mesh0 = new MeshInstance
                            {
                                instance = CreateInstance(pos, group[0].MirrorDirection),
                                basePieceIndex = group[0].BaseMeshIndex,
                                variantIndex = tile.variant0
                            },
                            mesh1 = new MeshInstance
                            {
                                instance = CreateInstance(pos, group[1].MirrorDirection),
                                basePieceIndex = group[1].BaseMeshIndex,
                                variantIndex = tile.variant1
                            }
                        };
                    }
                    else
                    {
                        meshTiles[index] = new MeshTile
                        {
                            count = 1,

                            mesh0 = new MeshInstance
                            {
                                instance = CreateInstance(pos, group[0].MirrorDirection),
                                basePieceIndex = group[0].BaseMeshIndex,
                                variantIndex = tile.variant0
                            }
                        };
                    }
                }
                else
                {
                    meshTiles[index] = new MeshTile { count = 0 };
                }
            }

            private CombineInstance CreateInstance(float3 pos, Direction mirrorDirection)
            {
                MatrixConverter m = new MatrixConverter { };
                m.float4x4 = math.mul(float4x4.translate(pos), ToScaleMatrix(mirrorDirection));

                return new CombineInstance { subMeshIndex = 0, transform = m.Matrix4x4 };
            }

            private static readonly float4x4 NoMirror  = float4x4.identity;
            private static readonly float4x4 XMirror   = float4x4.scale(-1, 1, 1);
            private static readonly float4x4 YMirror   = float4x4.scale(1, -1, 1);
            private static readonly float4x4 ZMirror   = float4x4.scale(1, 1, -1);
            private static readonly float4x4 XYMirror  = float4x4.scale(-1, -1, 1);
            private static readonly float4x4 XZMirror  = float4x4.scale(-1, 1, -1);
            private static readonly float4x4 XYZMirror = float4x4.scale(-1, -1, -1);

            static private float4x4 ToScaleMatrix(Direction mirrorDirection)
            {
                switch (mirrorDirection)
                {
                    case Direction.XAxis: return XMirror;
                    case Direction.YAxis: return YMirror;
                    case Direction.ZAxis: return ZMirror;

                    case Direction.XAxis | Direction.YAxis: return XYMirror;
                    case Direction.XAxis | Direction.ZAxis: return XZMirror;

                    case Direction.All: return XYZMirror;
                }
                return NoMirror;
            }

            [StructLayout(LayoutKind.Explicit)]
            public struct MatrixConverter
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
                    switch(tile.count)
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
                    }
                }
            }
        }

        [System.Serializable]
        public class Settings
        {
            //TODO: NOT IMPLEMENTED
            /// <summary>
            /// themes can have multiple variations for the same tile,
            /// by default only the first one is used when generating a mesh
            /// </summary>
            public bool hasTileVariation = false;

            /// <summary>
            /// Seed for the random variation.
            /// </summary>
            public int variationSeed = 0;

            /// <summary>
            /// try matching custom tiles if they are available in the theme
            /// </summary>
            public bool useCustomTiles = false;

            /// <summary>
            /// skip tiles in the center of the mesh
            /// </summary>
            public bool skipCenter = false;

            /// <summary>
            /// only add center tiles
            /// </summary>
            public bool skipBorder = false;

            /// <summary>
            /// If the center tile (a tile wich is surrounded by other tiles) can be oriented 
            /// in any direction, it can increase the diversity of the tileset
            /// </summary>
            public bool centerRandomRotation = false;

            // TODO: NOT IMPLEMENTED
            /// <summary>
            /// When generating the mesh, should the algorithm consider the chunk boundaries empty?
            /// If the chunk represents ground for example, then the bottom and side boundaries should
            /// be NOT empty so those sides don't get generated. If the chunk is a flying island which
            /// needs all of its sides rendered, then the boundaries should be considered empty.
            /// </summary>
            public byte emptyBoundaries = (byte)Direction.All;
        }

        public enum TileType : byte
        {
            /// <summary>
            /// a single tile, which needs to be drawn (depending on the configuration)
            /// </summary>
            Normal,

            /// <summary>
            /// a custom tile, possibly filling multiple cells, needs to be drawn
            /// </summary>
            Custom,

            /// <summary>
            /// cell doesn't need to be drawn, it is overlapped by a custom tile
            /// </summary>
            Overlapped,

            /// <summary>
            /// the cell is culled by something, it won't be drawn
            /// </summary>
            Culled
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
        }

        /// <summary>
        /// a cell may have two meshes
        /// </summary>
        private struct MeshTile
        {
            public byte count;

            public MeshInstance mesh0;
            public MeshInstance mesh1;
        }
    }
}
