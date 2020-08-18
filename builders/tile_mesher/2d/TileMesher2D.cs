using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

using static MeshBuilder.Extents;
using static MeshBuilder.Utils;

using TileData = MeshBuilder.Tile.Data;
using TileType = MeshBuilder.Tile.Type;
using DataVolume = MeshBuilder.Volume<MeshBuilder.Tile.Data>; // type values
using TileVolume = MeshBuilder.Volume<MeshBuilder.TileMesher2D.TileMeshData>; // configuration indices
using ConfigTransformGroup = MeshBuilder.TileTheme.ConfigTransformGroup;
using PieceTransform = MeshBuilder.Tile.PieceTransform;
using Direction = MeshBuilder.Tile.Direction;
using DataInstance = MeshBuilder.MeshCombinationBuilder.DataInstance;
using MeshDataOffset = MeshBuilder.MeshCombinationBuilder.MeshDataOffsets;

namespace MeshBuilder
{
    /// TODO: center randomization option uses the tile index, so it's not random at all (but still, it looks more varied)
    public class TileMesher2D : TileMesherBase<TileMesher2D.TileMeshData>
    {
        static private readonly Settings DefaultSettings = new Settings { };

        // INITIAL DATA
        public DataVolume Data { get; private set; }
        public Settings MesherSettings { get; private set; }

        public int YLevel { get; private set; }

        public float3 cellSize;

        // GENERATED DATA
        private Volume<MeshTile> tileMeshes;

        public void Init(DataVolume dataVolume, int yLevel, int themeIndex, TileThemePalette themePalette, float3 cellSize = default(float3), Settings settings = null)
        {
            var theme = themePalette.Get(themeIndex);
            int fillValue = themePalette.GetFillValue(themeIndex);
            Init(dataVolume, yLevel, fillValue, theme, themePalette, cellSize, settings);
        }

        public void Init(DataVolume dataVolume, int yLevel, int fillValue, TileTheme theme, float3 cellSize = default(float3), Settings settings = null)
        {
            Init(dataVolume, yLevel, fillValue, theme, null, cellSize, settings);
        }

        public void Init(DataVolume dataVolume, int yLevel, int fillValue, TileTheme theme, TileThemePalette themePalette, float3 cellSize = default(float3), Settings settings = null)
        {
            Dispose();

            this.cellSize = cellSize;

            theme.Init();
            if (theme.Configs.Length < TileTheme.Type2DConfigCount)
            {
                Debug.LogError("TileMesher theme has less than the required number of configurations! " + theme.ThemeName);
                return;
            }

            Data = dataVolume;
            ThemePalette = themePalette;
            FillValue = fillValue;
            MesherSettings = settings ?? DefaultSettings;

            Theme = theme;

            YLevel = Mathf.Clamp(yLevel, 0, dataVolume.YLength - 1);
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

                dependOn = ScheduleMeshCombination(dependOn);
            }
            else
            {
                if (!HasTilesData) Debug.LogError("TileMesher2D has no tiles data!");
                if (!HasTileMeshes) Debug.LogError("TileMesher2D has no mesh tiles data!");
            }

            return dependOn;
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
                centerRandomRotation = MesherSettings.centerRandomRotation,
                emptyBoundaries = MesherSettings.emptyBoundaries,
                skipCenter = MesherSettings.skipCenter,
                skipBorder = MesherSettings.skipBorder,
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
                cellSize = cellSize,
                tiles = tiles.Data,
                meshTiles = resultMeshTiles.Data
            };

            return tileGeneration.Schedule(tiles.Data.Length, batchCount, dependOn);
        }

        override protected JobHandle ScheduleFillMeshInstanceList(NativeList<MeshInstance> resultList, JobHandle dependOn)
        {
            var fillList = new FillMeshInstanceListJob
            {
                meshTiles = tileMeshes.Data,
                resultCombineInstances = resultList
            };

            return fillList.Schedule(dependOn);
        }

        override protected JobHandle ScheduleFillDataInstanceList(NativeList<DataInstance> resultList, JobHandle dependOn)
        {
            var fillList = new FillDataInstanceListJob
            {
                baseMeshVariants = Theme.TileThemeCache.BaseMeshVariants,
                dataOffsets = Theme.TileThemeCache.DataOffsets,
                meshTiles = tileMeshes.Data,
                resultCombineInstances = resultList
            };

            return fillList.Schedule(dependOn);
        }

        private bool HasTileMeshes { get { return tileMeshes != null && !tileMeshes.IsDisposed; } }

        /// <summary>
        /// Takes the simple tile data volume (theme index - variant index pairs) and turns them into a volume of
        /// TileMeshData which contains every information to find and transform the correct mesh pieces for rendering
        /// 
        /// TODO: this handles every settings, should I separate some to handle simpler cases?
        /// </summary>
        [BurstCompile]
        private struct GenerateTileDataJob : IJobParallelFor
        {
            public Extents tileExtents;
            public Extents dataExtents;
            public int themeIndex;
            public int yLevel;
            public bool centerRandomRotation;
            public Direction emptyBoundaries;
            public bool skipCenter;
            public bool skipBorder;

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

                if (emptyBoundaries != Direction.All && (hasLF || hasRF || hasLB || hasRB))
                {
                    if (dc.x == 0 && !HasFlag(emptyBoundaries, Direction.XMinus)) { hasLF = true; hasLB = true; }
                    if (dc.x == dataExtents.X && !HasFlag(emptyBoundaries, Direction.XPlus)) { hasRF = true; hasRB = true; }
                    if (dc.z == 0 && !HasFlag(emptyBoundaries, Direction.ZMinus)) { hasLB = true; hasRB = true; }
                    if (dc.z == dataExtents.Z && !HasFlag(emptyBoundaries, Direction.ZPlus)) { hasLF = true; hasRF = true; }
                }

                if (hasLF) configuration |= Tile.LeftForward;
                if (hasRF) configuration |= Tile.RightForward;
                if (hasLB) configuration |= Tile.LeftBackward;
                if (hasRB) configuration |= Tile.RightBackward;

                var transformGroup = configs[configuration];

                var tileType = TileType.Normal;
                if (configuration == 0)
                {
                    tileType = TileType.Void;
                }
                else if (configuration == configs.Length - 1)
                {
                    if (skipCenter)
                    {
                        tileType = TileType.Void;
                    }
                    else
                    if (centerRandomRotation)
                    {
                        var rot = (PieceTransform)(1 << (2 + (index % 4)));
                        if ((byte)rot >= (byte)PieceTransform.Rotate90)
                        {
                            transformGroup = new ConfigTransformGroup(new TileTheme.ConfigTransform(transformGroup[0].BaseMeshIndex, rot));
                        }
                    }
                }
                else
                {
                    if (skipBorder)
                    {
                        tileType = TileType.Void;
                    }
                }

                tiles[index] = new TileMeshData { type = tileType, configTransformGroup = transformGroup, variant0 = 0, variant1 = 0 };
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
            public Extents tileExtents;
            public float3 cellSize;
            [ReadOnly] public NativeArray<TileMeshData> tiles;
            [WriteOnly] public NativeArray<MeshTile> meshTiles;

            public void Execute(int index)
            {
                var tile = tiles[index];
                var group = tile.configTransformGroup;
                if (group.Count > 0 && tile.type == TileType.Normal)
                {
                    float3 pos = CoordFromIndex(index, tileExtents);
                    pos.x *= cellSize.x;
                    pos.y *= cellSize.y;
                    pos.z *= cellSize.z;

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

        [BurstCompile]
        private struct FillMeshInstanceListJob : IJob
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

        [BurstCompile]
        private struct FillDataInstanceListJob : IJob
        {
            [ReadOnly] public NativeArray<Offset> baseMeshVariants;
            [ReadOnly] public NativeArray<MeshDataOffset> dataOffsets;

            [ReadOnly] public NativeArray<MeshTile> meshTiles;
            [WriteOnly] public NativeList<DataInstance> resultCombineInstances;

            public void Execute()
            {
                for (int i = 0; i < meshTiles.Length; ++i)
                {
                    var tile = meshTiles[i];
                    switch (tile.count)
                    {
                        case 1:
                            {
                                resultCombineInstances.Add(ToDataInstance(tile.mesh0));
                                break;
                            }
                        case 2:
                            {
                                resultCombineInstances.Add(ToDataInstance(tile.mesh0));
                                resultCombineInstances.Add(ToDataInstance(tile.mesh1));
                                break;
                            }
                    }
                }
            }

            public DataInstance ToDataInstance(MeshInstance inst)
            {
                return new DataInstance()
                {
                    dataOffsets = TileTheme.MeshCache.GetMeshDataOffset(inst.basePieceIndex, inst.variantIndex, baseMeshVariants, dataOffsets),
                    transform = inst.transform
                };
            }
        }

        // using this instead of Enum.HasFlag to avoid value boxing
        // (the Burst compiler complained about it)
        public static bool HasFlag(Direction value, Direction flag)
        {
            return ((uint)value & (uint)flag) != 0;
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
            /// in any direction, it can increase the variety of the tileset
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
