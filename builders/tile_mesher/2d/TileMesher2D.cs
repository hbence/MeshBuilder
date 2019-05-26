using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using System.Runtime.InteropServices;

using static MeshBuilder.Extents;
using static MeshBuilder.Utils;

using TileData = MeshBuilder.Tile.Data;
using TileType = MeshBuilder.Tile.Type;
using DataVolume = MeshBuilder.Volume<MeshBuilder.Tile.Data>; // type values
using TileVolume = MeshBuilder.Volume<MeshBuilder.TileMesher2D.TileMeshData>; // configuration indices
using ConfigTransformGroup = MeshBuilder.TileTheme.ConfigTransformGroup;
using PieceTransform = MeshBuilder.Tile.PieceTransform;
using Direction = MeshBuilder.Tile.Direction;

namespace MeshBuilder
{
    public class TileMesher2D : TileMesherBase<TileMesher2D.TileMeshData>
    {
        static private readonly Settings DefaultSettings = new Settings { };

        // INITIAL DATA
        public DataVolume Data { get; private set; }
        public Settings MesherSettings { get; private set; }

        public int YLevel { get; private set; }

        // GENERATED DATA
        private Volume<MeshTile> tileMeshes;

        // TEMP DATA
        private NativeList<MeshInstance> tempInstanceList;

        public void Init(DataVolume dataVolume, int yLevel, int themeIndex, TileThemePalette themePalette)
        {
            var theme = themePalette.Get(themeIndex);
            Init(dataVolume, yLevel, themeIndex, theme, themePalette, DefaultSettings);
        }

        public void Init(DataVolume dataVolume, int yLevel, int themeIndex, TileTheme theme, Settings settings = null)
        {
            Init(dataVolume, yLevel, themeIndex, theme, null, settings);
        }

        public void Init(DataVolume dataVolume, int yLevel, int themeIndex, TileTheme theme, TileThemePalette themePalette, Settings settings = null)
        {
            Dispose();

            theme.Init();
            if (theme.Configs.Length < TileTheme.Type2DConfigCount)
            {
                Debug.LogError("TileMesher theme has less than the required number of configurations! " + theme.ThemeName);
                return;
            }

            Data = dataVolume;
            ThemePalette = themePalette;
            YLevel = Mathf.Clamp(yLevel, 0, dataVolume.YLength - 1);
            FillValue = themeIndex;
            MesherSettings = settings != null ? settings : new Settings();

            Theme = theme;

            if (YLevel != yLevel)
            {
                Debug.LogError("TileMesher2D yLevel is out of bounds:" + yLevel + " it is clamped!");
            }

            int x = Data.XLength;
            int y = Data.YLength;
            int z = Data.ZLength;
            dataExtents = new Extents(x, y, z);
            tileExtents = new Extents(x + 1, 1, z + 1);

            generationType = GenerationType.FromDataUncached;

            Inited();
        }

        override protected JobHandle StartGeneration(JobHandle dependOn)
        {
            if (generationType == GenerationType.FromDataUncached)
            {
                if (HasTilesData) { tiles.Dispose(); }

                tiles = new TileVolume(tileExtents);

                if (HasTileMeshes) { tileMeshes.Dispose(); }

                tileMeshes = new Volume<MeshTile>(tileExtents);

                dependOn = ScheduleTileGeneration(tiles, Data, 64, dependOn);
            }

            if (HasTilesData && HasTileMeshes)
            {
                dependOn = ScheduleMeshTileGeneration(tileMeshes, tiles, 32, dependOn);

                tempInstanceList = new NativeList<MeshInstance>(Allocator.TempJob);
                AddTemp(tempInstanceList);
                dependOn = ScheduleFillCombineInstanceList(tempInstanceList, tileMeshes, dependOn);
            }
            else
            {
                if (!HasTilesData) Debug.LogError("TileMesher2D has no tiles data!");
                if (!HasTileMeshes) Debug.LogError("TileMesher2D has no mesh tiles data!");
            }

            return dependOn;
        }

        override protected void EndGeneration(Mesh mesh)
        {
            if (tempInstanceList.IsCreated)
            {
                CombineMeshes(mesh, tempInstanceList, Theme);
                tempInstanceList = default;
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            SafeDispose(ref tileMeshes);
        }

        private JobHandle ScheduleTileGeneration(TileVolume resultTiles, DataVolume data, int batchCount, JobHandle dependOn)
        {
            var tileGeneration = new GenerateTileDataJob
            {
                tileExtents = tileExtents,
                dataExtents = dataExtents,
                themeIndex = FillValue,
                yLevel = YLevel,
                configs = Theme.Configs,
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

                    if (group.Count == 2)
                    {
                        meshTiles[index] = new MeshTile
                        {
                            count = 2,
                            mesh0 = CreateMeshInstance(pos, 0, group, tile.variant0),
                            mesh1 = CreateMeshInstance(pos, 1, group, tile.variant1),
                        };
                    }
                    else
                    {
                        meshTiles[index] = new MeshTile
                        {
                            count = 1,
                            mesh0 = CreateMeshInstance(pos, 0, group, tile.variant0)
                        };
                    }
                }
                else
                {
                    meshTiles[index] = new MeshTile { count = 0 };
                }
            }

            private MeshInstance CreateMeshInstance(float3 pos, int index, ConfigTransformGroup group, byte variant)
            {
                return new MeshInstance
                {
                    transform = CreateTransform(pos, group[index].PieceTransform),
                    basePieceIndex = group[index].BaseMeshIndex,
                    variantIndex = variant
                };
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
            // THIS should be somewhere else probably, data generations should be separated
            /// <summary>
            /// themes can have multiple variations for the same tile,
            /// by default only the first one is used when generating a mesh
            /// </summary>
            //public bool hasTileVariation = false;

            // THIS should be somewhere else probably, data generations should be separated
            /// <summary>
            /// Seed for the random variation.
            /// </summary>
            //public int variationSeed = 0;

            // THIS should be somewhere else probably, data generations should be separated
            /// <summary>
            /// try matching custom tiles if they are available in the theme
            /// </summary>
            // public bool useCustomTiles = false;

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

        /// <summary>
        /// variant data in a variant volume, this contains all the possible variants for every mesher
        /// (if four different themes touch corners, four meshers will draw at that position)
        /// </summary>
        public struct VariantData
        {
            byte variant0;
            byte variant1;
            byte variant2;
            byte variant3;
        }
    }
}
