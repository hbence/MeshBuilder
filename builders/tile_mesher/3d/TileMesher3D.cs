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
using TileVolume = MeshBuilder.Volume<MeshBuilder.TileMesher3D.TileMeshData>; // configuration indices
using ConfigTransformGroup = MeshBuilder.TileTheme.ConfigTransformGroup;
using PieceTransform = MeshBuilder.Tile.PieceTransform;
using Direction = MeshBuilder.Tile.Direction;


namespace MeshBuilder
{
    public class TileMesher3D : TileMesherBase<TileMesher3D.TileMeshData>
    {
        static private readonly Settings DefaultSettings = new Settings { };

        // INITIAL DATA
        private TileTheme theme;
        private DataVolume data;
        public Settings MesherSettings { get; private set; }

        private float3 cellSize;

        private AdjacentVolumes adjacents;

        // GENERATED DATA
        private Volume<MeshTile> tileMeshes;

        // TEMP DATA
        private NativeList<MeshInstance> tempInstanceList;

        public TileMesher3D()
        {
            adjacents = new AdjacentVolumes();
        }

        public void Init(DataVolume dataVolume, int themeIndex, TileThemePalette themePalette, float3 cellSize = default)
        {
            var theme = themePalette.Get(themeIndex);
            int fillValue = themePalette.GetFillValue(themeIndex);
            Init(dataVolume, fillValue, theme, themePalette, cellSize, DefaultSettings);
        }

        public void Init(DataVolume dataVolume, int fillValue, TileTheme theme, float3 cellSize = default, Settings settings = null)
        {
            Init(dataVolume, fillValue, theme, null, cellSize, settings);
        }

        public void Init(DataVolume dataVolume, int fillValue, TileTheme theme, TileThemePalette themePalette, float3 cellSize = default, Settings settings = null)
        {
            Dispose();

            theme.Init();
            if (theme.Configs.Length < TileTheme.Type3DConfigCount)
            {
                Debug.LogError("The theme has less than the required number of configurations! " + theme.Configs.Length);
                return;
            }

            this.data = dataVolume;
            this.ThemePalette = themePalette;
            this.theme = theme;
            this.FillValue = fillValue;
            this.MesherSettings = settings == null ? DefaultSettings : settings;
            
            this.cellSize = cellSize;
            if (this.cellSize.x == 0 || this.cellSize.y == 0 || this.cellSize.z == 0)
            {
                this.cellSize = new float3(1, 1, 1);
                Debug.LogError("cell size can't have zero value! (cell reset to (1,1,1))");
            }

            int x = data.XLength;
            int y = data.YLength;
            int z = data.ZLength;
            dataExtents = new Extents(x, y, z);
            tileExtents = new Extents(x + 1, y + 1, z + 1);

            generationType = GenerationType.FromDataUncached;

            Inited();
        }

        public void SetAdjacent(DataVolume data, byte direction)
        {
            adjacents.SetAdjacent(data, direction);
        }

        override protected JobHandle StartGeneration(JobHandle dependOn)
        { 
            if (generationType == GenerationType.FromDataUncached)
            {
                if (HasTilesData)
                {
                    if (!tiles.DoExtentsMatch(tileExtents)) { SafeDispose(ref tiles); }
                }

                if (!HasTilesData) { tiles = new TileVolume(tileExtents); }

                if (HasTileMeshes)
                {
                    if (!tileMeshes.DoExtentsMatch(tileExtents)) { SafeDispose(ref tileMeshes); }
                }

                if (!HasTileMeshes) { tileMeshes = new Volume<MeshTile>(tileExtents); }

                dependOn = ScheduleTileGeneration(tiles, data, 64, dependOn);
                
                if (ThemePalette != null)
                {
                    int openFillFlags = ThemePalette.CollectOpenThemeFillValueFlags(theme);
                    openFillFlags |= (1 << FillValue);
                    dependOn = ScheduleOpenFlagsUpdate(tiles, data, theme.Configs, openFillFlags, 128, dependOn);
                }
                
                dependOn = adjacents.ScheduleJobs(FillValue, data, tiles, theme.Configs, MesherSettings.skipDirections, MesherSettings.skipDirectionsAndBorders, dependOn);
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
                if (!HasTilesData) Debug.LogError("TileMesher has no tiles data!");
                if (!HasTileMeshes) Debug.LogError("TileMesher has no mesh tiles data!");
            }

            return dependOn;
        }

        override protected void EndGeneration(Mesh mesh)
        {
            /*
            if (tempInstanceList.IsCreated)
            {
                CombineMeshes(mesh, tempInstanceList, theme);
                tempInstanceList = default;
            }
            //*/
            if (tempInstanceList.IsCreated)
            {
                var job = ScheduleCombineMeshes(tempInstanceList, theme, default);
                combinationBuilder.Mesh = mesh;
                combinationBuilder.Complete();

                mesh = combinationBuilder.Mesh;
            }
            //*/
            base.EndGeneration(mesh);
        }

        public override void Dispose()
        {
            base.Dispose();

            adjacents.Dispose();
            SafeDispose(ref tileMeshes);
        }

        // TODO: tile generation is divided into multiple versions, based on the mesher settings
        // so if a feature is not needed there is less branching and checking, it causes some code duplication and
        // I'm not yet sure if it has a visible performance impact or what the most used features would be.
        // It's possible the leaner versions aren't needed, and GenerateTileDataJobFullOptions will be enough
        private JobHandle ScheduleTileGeneration(TileVolume resultTiles, DataVolume data, int batchCount, JobHandle dependOn)
        {
            if (MesherSettings.skipDirections == Direction.None && 
                MesherSettings.skipDirectionsAndBorders == Direction.None &&
                MesherSettings.filledBoundaries == Direction.None)
            {
                var tileGeneration = new GenerateTileDataJob
                {
                    tileExtents = tileExtents,
                    dataExtents = dataExtents,
                    themeIndex = FillValue,
                    configs = theme.Configs,
                    data = data.Data,
                    tiles = resultTiles.Data
                };
                return tileGeneration.Schedule(resultTiles.Data.Length, batchCount, dependOn);
            }
            else
            {
                if (MesherSettings.filledBoundaries != Direction.None)
                {
                    var tileGeneration = new GenerateTileDataJobFullOptions
                    {
                        tileExtents = tileExtents,
                        dataExtents = dataExtents,
                        themeIndex = FillValue,
                        skipDirections = MesherSettings.skipDirections,
                        skipDirectionsWithBorders = MesherSettings.skipDirectionsAndBorders,
                        filledBoundaries = MesherSettings.filledBoundaries,
                        configs = theme.Configs,
                        data = data.Data,
                        tiles = resultTiles.Data
                    };
                    return tileGeneration.Schedule(resultTiles.Data.Length, batchCount, dependOn);
                }
                else
                {
                    var tileGeneration = new GenerateTileDataJobWithSkipDirections
                    {
                        tileExtents = tileExtents,
                        dataExtents = dataExtents,
                        themeIndex = FillValue,
                        skipDirections = MesherSettings.skipDirections,
                        skipDirectionsWithBorders = MesherSettings.skipDirectionsAndBorders,
                        configs = theme.Configs,
                        data = data.Data,
                        tiles = resultTiles.Data
                    };
                    return tileGeneration.Schedule(resultTiles.Data.Length, batchCount, dependOn);
                }
            }
        }

        private JobHandle ScheduleOpenFlagsUpdate(TileVolume tiles, DataVolume data, NativeArray<ConfigTransformGroup> configs, int openFillFlags, int batchCount, JobHandle dependOn)
        {
            var job = new UpdateOpenTileConfigurations
            {
                tileExtents = tileExtents,
                dataExtents = dataExtents,

                openFillFlags = openFillFlags,

                configs = configs,
                data = data.Data,
                tiles = tiles.Data
            };

            return job.Schedule(tiles.Data.Length, batchCount, dependOn);
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
                int3 dc = CoordFromIndex(index, tileExtents);

                byte configuration = CreateConfiguration(themeIndex, dc, data, dataExtents);

                var transformGroup = configs[configuration];
                var type = TileType.Normal;
                if (configuration == 0 || configuration == configs.Length - 1)
                {
                    type = TileType.Void;
                }
                tiles[index] = new TileMeshData { type = type, configTransformGroup = transformGroup };
            }
        }

        [BurstCompile]
        private struct GenerateTileDataJobWithSkipDirections : IJobParallelFor
        {
            public Extents tileExtents;
            public Extents dataExtents;
            public int themeIndex;
            public Direction skipDirections;
            public Direction skipDirectionsWithBorders;

            [ReadOnly] public NativeArray<ConfigTransformGroup> configs;
            [ReadOnly] public NativeArray<TileData> data;
            [WriteOnly] public NativeArray<TileMeshData> tiles;

            public void Execute(int index)
            {
                int3 dc = CoordFromIndex(index, tileExtents);

                byte configuration = CreateConfiguration(themeIndex, dc, data, dataExtents);

                var transformGroup = configs[configuration];
                var type = TileType.Normal;
                if (configuration == 0 || configuration == configs.Length - 1)
                {
                    type = TileType.Void;
                }

                if (skipDirectionsWithBorders != Direction.None && IsCulledBySkipDirectionsWithBorders(configuration, skipDirectionsWithBorders))
                {
                    type |= TileType.Culled;
                }
                else
                if (skipDirections != Direction.None && IsCulledBySkipDirections(configuration, skipDirections))
                {
                    type |= TileType.Culled;
                }

                tiles[index] = new TileMeshData { type = type, configTransformGroup = transformGroup, variant0 = 0, variant1 = 0 };
            }
        }

        [BurstCompile]
        private struct GenerateTileDataJobFullOptions : IJobParallelFor
        {
            public Extents tileExtents;
            public Extents dataExtents;
            public int themeIndex;
            public Direction skipDirections;
            public Direction skipDirectionsWithBorders;
            public Direction filledBoundaries;

            [ReadOnly] public NativeArray<ConfigTransformGroup> configs;
            [ReadOnly] public NativeArray<TileData> data;
            [WriteOnly] public NativeArray<TileMeshData> tiles;

            public void Execute(int index)
            {
                int3 dc = CoordFromIndex(index, tileExtents);

                byte configuration = CreateConfigurationWithFilledBoundaries(themeIndex, dc, data, dataExtents, filledBoundaries);

                var transformGroup = configs[configuration];
                var type = TileType.Normal;
                if (configuration == 0 || configuration == configs.Length - 1)
                {
                    type = TileType.Void;
                }

                if (skipDirectionsWithBorders != Direction.None && IsCulledBySkipDirectionsWithBorders(configuration, skipDirectionsWithBorders))
                {
                    type |= TileType.Culled;
                }
                else
                if (skipDirections != Direction.None && IsCulledBySkipDirections(configuration, skipDirections))
                {
                    type |= TileType.Culled;
                }

                tiles[index] = new TileMeshData { type = type, configTransformGroup = transformGroup, variant0 = 0, variant1 = 0 };
            }
        }

        [BurstCompile]
        private struct UpdateBoundaryJob : IJob
        {
            public Extents tileExtents;
            public Extents dataExtents;
            public int themeIndex;

            public LayerIndexStep indexStep;

            public Direction skipDirections;
            public Direction skipDirectionsWithBorders;

            [ReadOnly] public NativeArray<ConfigTransformGroup> configs;
            [ReadOnly] public NativeArray<TileData> data;
            
            [ReadOnly] public NativeArray<TileData> adjXM;
            [ReadOnly] public NativeArray<TileData> adjXP;
            [ReadOnly] public NativeArray<TileData> adjZM;
            [ReadOnly] public NativeArray<TileData> adjZP;

            [ReadOnly] public NativeArray<TileData> adjXMZM;
            [ReadOnly] public NativeArray<TileData> adjXMZP;
            [ReadOnly] public NativeArray<TileData> adjXPZM;
            [ReadOnly] public NativeArray<TileData> adjXPZP;

            public NativeArray<TileMeshData> tiles;

            public void Execute()
            {
                for (int row = 0; row < indexStep.rowNum; ++row)
                {
                    int index = indexStep.StartRow(row);
                    for (int col = 0; col < indexStep.colNum; ++col)
                    {
                        var tile = tiles[index];
                        var dc = CoordFromIndex(index, tileExtents);
                        byte configuration = CreateConfigurationWithAdjacent(themeIndex, dc, data, dataExtents, adjXM, adjXP, adjZM, adjZP, adjXMZM, adjXMZP, adjXPZM, adjXPZP);
                        if (configuration == 0 || configuration == configs.Length - 1)
                        {
                            tile.type = TileType.Void;
                        }
                        else
                        {
                            tile.type = TileType.Normal;
                        }

                        if (skipDirectionsWithBorders != Direction.None && IsCulledBySkipDirectionsWithBorders(configuration, skipDirectionsWithBorders))
                        {
                            tile.type |= TileType.Culled;
                        }
                        else
                        if (skipDirections != Direction.None && IsCulledBySkipDirections(configuration, skipDirections))
                        {
                            tile.type |= TileType.Culled;
                        }

                        tile.configTransformGroup = configs[configuration];
                        tiles[index] = tile;

                        index = indexStep.NextCol(index);
                    }
                }
            }
        }

        [BurstCompile]
        private struct CullLayerJob : IJob
        {
            public Extents tileExtents;

            public LayerIndexStep indexStep;

            public NativeArray<TileMeshData> tiles;

            public void Execute()
            {
                for (int row = 0; row < indexStep.rowNum; ++row)
                {
                    int index = indexStep.StartRow(row);
                    for (int col = 0; col < indexStep.colNum; ++col)
                    {
                        var tile = tiles[index];
                        tile.type = TileType.Culled;
                        tiles[index] = tile;

                        index = indexStep.NextCol(index);
                    }
                }
            }
        }

        static private byte CreateConfiguration(int themeIndex, int3 dc, NativeArray<TileData> data, Extents dataExtents)
        {
            byte configuration = 0;

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

            bool IsFilled(int x, int y, int z)
            {
                int index = IndexFromCoord(x, y, z, dataExtents);
                return data[index].themeIndex == themeIndex;
            }

            return configuration;
        }

        static private byte CreateConfigurationWithOpenFlags(int3 dc, NativeArray<TileData> data, Extents dataExtents, int openFlags)
        {
            byte configuration = 0;

            if (IsFilled(dc.x - 1, dc.y,     dc.z))     configuration |= Tile.TopLeftForward;
            if (IsFilled(dc.x    , dc.y,     dc.z))     configuration |= Tile.TopRightForward;
            if (IsFilled(dc.x - 1, dc.y,     dc.z - 1)) configuration |= Tile.TopLeftBackward;
            if (IsFilled(dc.x    , dc.y,     dc.z - 1)) configuration |= Tile.TopRightBackward;
            if (IsFilled(dc.x - 1, dc.y - 1, dc.z))     configuration |= Tile.BottomLeftForward;
            if (IsFilled(dc.x    , dc.y - 1, dc.z))     configuration |= Tile.BottomRightForward;
            if (IsFilled(dc.x - 1, dc.y - 1, dc.z - 1)) configuration |= Tile.BottomLeftBackward;
            if (IsFilled(dc.x    , dc.y - 1, dc.z - 1)) configuration |= Tile.BottomRightBackward;

            bool IsFilled(int x, int y, int z)
            {
                if (dataExtents.IsInBounds(x, y, z))
                {
                    int index = IndexFromCoord(x, y, z, dataExtents);
                    int value = data[index].themeIndex;
                    return value != 0 && ((1 << value) & openFlags) != 0;
                }

                return false;
            }

            return configuration;
        }

        static private byte CreateConfigurationWithAdjacent(int themeIndex, int3 dc, NativeArray<TileData> data, Extents dataExtents,
            NativeArray<TileData> adjXM, NativeArray<TileData> adjXP, NativeArray<TileData> adjZM, NativeArray<TileData> adjZP,
            NativeArray<TileData> adjXMZM, NativeArray<TileData> adjXMZP, NativeArray<TileData> adjXPZM, NativeArray<TileData> adjXPZP)
        {
            byte configuration = 0;

            if (IsFilled(dc.x - 1, dc.y, dc.z    )) configuration |= Tile.TopLeftForward;
            if (IsFilled(dc.x    , dc.y, dc.z    )) configuration |= Tile.TopRightForward;
            if (IsFilled(dc.x - 1, dc.y, dc.z - 1)) configuration |= Tile.TopLeftBackward;
            if (IsFilled(dc.x    , dc.y, dc.z - 1)) configuration |= Tile.TopRightBackward;

            if (IsFilled(dc.x - 1, dc.y - 1, dc.z    )) configuration |= Tile.BottomLeftForward;
            if (IsFilled(dc.x    , dc.y - 1, dc.z    )) configuration |= Tile.BottomRightForward;
            if (IsFilled(dc.x - 1, dc.y - 1, dc.z - 1)) configuration |= Tile.BottomLeftBackward;
            if (IsFilled(dc.x    , dc.y - 1, dc.z - 1)) configuration |= Tile.BottomRightBackward;

            bool IsFilled(int x, int y, int z)
            {
                int index = IndexFromCoord(x, y, z, dataExtents);
                if (dataExtents.IsInBounds(x, y, z))
                {
                    return data[index].themeIndex == themeIndex;
                }

                NativeArray<TileData> selectedData = data;
                byte dir = FindDirection(x, y, z, dataExtents);
                switch (dir)
                {
                    case (byte)Direction.XMinus: selectedData = adjXM; break;
                    case (byte)Direction.XPlus: selectedData = adjXP; break;
                    case (byte)Direction.ZMinus: selectedData = adjZM; break;
                    case (byte)Direction.ZPlus: selectedData = adjZP; break;
                    case (byte)Direction.XMinus | (byte)Direction.ZMinus: selectedData = adjXMZM; break;
                    case (byte)Direction.XMinus | (byte)Direction.ZPlus: selectedData = adjXMZP; break;
                    case (byte)Direction.XPlus | (byte)Direction.ZMinus: selectedData = adjXPZM; break;
                    case (byte)Direction.XPlus | (byte)Direction.ZPlus: selectedData = adjXPZP; break;
                }

                if ((dir & (byte)Direction.XPlus) != 0) { x = 0; }
                else if ((dir & (byte)Direction.XMinus) != 0) { x = dataExtents.X - 1; }

                if ((dir & (byte)Direction.ZPlus) != 0) { z = 0; }
                else if ((dir & (byte)Direction.ZMinus) != 0) { z = dataExtents.Z - 1; }

                index = IndexFromCoord(x, y, z, dataExtents);
                if (dataExtents.IsInBounds(x, y, z) && index < selectedData.Length)
                {
                    return selectedData[index].themeIndex == themeIndex;
                }

                return false;
            }

            byte FindDirection(int testX, int testY, int testZ, Extents e)
            {
                byte result = 0;

                if (testX < 0) result |= (byte)Direction.XMinus;
                if (testX >= e.X) result |= (byte)Direction.XPlus;
                if (testY < 0) result |= (byte)Direction.YMinus;
                if (testY >= e.Y) result |= (byte)Direction.YPlus;
                if (testZ < 0) result |= (byte)Direction.ZMinus;
                if (testZ >= e.Z) result |= (byte)Direction.ZPlus;

                return result;
            }

            return configuration;
        }

        private const byte XPlusMask = 0b01010101;
        private const byte XMinusMask = 0b10101010;
        private const byte YPlusMask = 0b11110000;
        private const byte YMinusMask = 0b00001111;
        private const byte ZPlusMask = 0b11001100;
        private const byte ZMinusMask = 0b00110011;

        static private byte CreateConfigurationWithFilledBoundaries(int themeIndex, int3 dc, NativeArray<TileData> data, Extents dataExtents, Direction filledBoundaries)
        {
            byte configuration = CreateConfiguration(themeIndex, dc, data, dataExtents);

            if (configuration != 0 && configuration != 0b11111111)
            {
                if (dc.x >= dataExtents.X && (filledBoundaries & Direction.XPlus) != 0)
                {
                    if ((configuration & Tile.TopLeftForward) != 0) configuration |= Tile.TopRightForward;
                    if ((configuration & Tile.TopLeftBackward) != 0) configuration |= Tile.TopRightBackward;
                    if ((configuration & Tile.BottomLeftForward) != 0) configuration |= Tile.BottomRightForward;
                    if ((configuration & Tile.BottomLeftBackward) != 0) configuration |= Tile.BottomRightBackward;
                }
                if (dc.x <= 0 && (filledBoundaries & Direction.XMinus) != 0)
                {
                    if ((configuration & Tile.TopRightForward) != 0) configuration |= Tile.TopLeftForward;
                    if ((configuration & Tile.TopRightBackward) != 0) configuration |= Tile.TopLeftBackward;
                    if ((configuration & Tile.BottomRightForward) != 0) configuration |= Tile.BottomLeftForward;
                    if ((configuration & Tile.BottomRightBackward) != 0) configuration |= Tile.BottomLeftBackward;
                }

                if (dc.y >= dataExtents.Y && (filledBoundaries & Direction.YPlus) != 0)
                {
                    if ((configuration & Tile.BottomLeftForward) != 0) configuration |= Tile.TopLeftForward;
                    if ((configuration & Tile.BottomRightForward) != 0) configuration |= Tile.TopRightForward;
                    if ((configuration & Tile.BottomLeftBackward) != 0) configuration |= Tile.TopLeftBackward;
                    if ((configuration & Tile.BottomRightBackward) != 0) configuration |= Tile.TopRightBackward;
                }
                if (dc.y <= 0 && (filledBoundaries & Direction.YMinus) != 0)
                {
                    if ((configuration & Tile.TopLeftForward) != 0) configuration |= Tile.BottomLeftForward;
                    if ((configuration & Tile.TopRightForward) != 0) configuration |= Tile.BottomRightForward;
                    if ((configuration & Tile.TopLeftBackward) != 0) configuration |= Tile.BottomLeftBackward;
                    if ((configuration & Tile.TopRightBackward) != 0) configuration |= Tile.BottomRightBackward;
                }
                if (dc.z >= dataExtents.Z && (filledBoundaries & Direction.ZPlus) != 0)
                {
                    if ((configuration & Tile.TopLeftBackward) != 0) configuration |= Tile.TopLeftForward;
                    if ((configuration & Tile.TopRightBackward) != 0) configuration |= Tile.TopRightForward;
                    if ((configuration & Tile.BottomLeftBackward) != 0) configuration |= Tile.BottomLeftForward;
                    if ((configuration & Tile.BottomRightBackward) != 0) configuration |= Tile.BottomRightForward;
                }
                if (dc.z <= 0 && (filledBoundaries & Direction.ZMinus) != 0)
                {
                    if ((configuration & Tile.TopLeftForward) != 0) configuration |= Tile.TopLeftBackward;
                    if ((configuration & Tile.TopRightForward) != 0) configuration |= Tile.TopRightBackward;
                    if ((configuration & Tile.BottomLeftForward) != 0) configuration |= Tile.BottomLeftBackward;
                    if ((configuration & Tile.BottomRightForward) != 0) configuration |= Tile.BottomRightBackward;
                }
            }
            return configuration;
        }

        static private bool IsCulledBySkipDirections(byte configuration, Direction skipDirections)
        {
            if ((skipDirections & Direction.XPlus) != 0 && configuration == XMinusMask) { return true; }
            if ((skipDirections & Direction.XMinus) != 0 && configuration == XPlusMask) { return true; }

            if ((skipDirections & Direction.YPlus) != 0 && configuration == YMinusMask) { return true; }
            if ((skipDirections & Direction.YMinus) != 0 && configuration == YPlusMask) { return true; }

            if ((skipDirections & Direction.ZPlus) != 0 && configuration == ZMinusMask) { return true; }
            if ((skipDirections & Direction.ZMinus) != 0 && configuration == ZPlusMask) { return true; }

            return false;
        }

        static private bool IsCulledBySkipDirectionsWithBorders(byte configuration, Direction skipDirections)
        {
            if ((skipDirections & Direction.XPlus) != 0 && (configuration & XPlusMask) == 0) { return true; }
            if ((skipDirections & Direction.XMinus) != 0 && (configuration & XMinusMask) == 0) { return true; }

            if ((skipDirections & Direction.YPlus) != 0 && (configuration & YPlusMask) == 0) { return true; }
            if ((skipDirections & Direction.YMinus) != 0 && (configuration & YMinusMask) == 0) { return true; }

            if ((skipDirections & Direction.ZPlus) != 0 && (configuration & ZPlusMask) == 0) { return true; }
            if ((skipDirections & Direction.ZMinus) != 0 && (configuration & ZMinusMask) == 0) { return true; }

            return false;
        }
        
        [BurstCompile]
        private struct UpdateOpenTileConfigurations : IJobParallelFor
        {
            public Extents tileExtents;
            public Extents dataExtents;

            public int openFillFlags;

            [ReadOnly] public NativeArray<ConfigTransformGroup> configs;
            [ReadOnly] public NativeArray<TileData> data;
            public NativeArray<TileMeshData> tiles;

            public void Execute(int index)
            {
                int3 dc = CoordFromIndex(index, tileExtents);
                var tile = tiles[index];
                if (tile.type != TileType.Void)
                {
                    byte configuration = CreateConfigurationWithOpenFlags(dc, data, dataExtents, openFillFlags);
                    if (configuration == 0 || configuration == configs.Length - 1)
                    {
                        tile.type = TileType.Void;
                        tile.configTransformGroup = configs[0];
                    }
                    tiles[index] = tile;
                }
            }
        }

        [BurstCompile]
        private struct GenerateMeshDataJob : IJobParallelFor
        {
            private const byte RotationMask = (byte)(PieceTransform.Rotate90 | PieceTransform.Rotate180 | PieceTransform.Rotate270);
            private const byte MirrorMask = (byte)PieceTransform.MirrorXYZ;

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
                    meshTiles[index] = CreateMeshTile(pos, group, tile);
                }
                else
                {
                    meshTiles[index] = new MeshTile { count = 0 };
                }
            }

            private MeshTile CreateMeshTile(float3 pos, ConfigTransformGroup group, TileMeshData tile)
            {
                pos.x *= cellSize.x;
                pos.y *= cellSize.y;
                pos.z *= cellSize.z;
                switch (group.Count)
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

                if (HasFlag(pieceTransform, PieceTransform.MirrorX)) MirrorMatrix(PieceTransform.MirrorX, ref transform);
                if (HasFlag(pieceTransform, PieceTransform.MirrorY)) MirrorMatrix(PieceTransform.MirrorY, ref transform);

                transform.c3.x = pos.x;
                transform.c3.y = pos.y;
                transform.c3.z = pos.z;

                return new CombineInstance { subMeshIndex = 0, transform = ToMatrix4x4(transform) };
            }

            static private bool HasFlag(PieceTransform transform, PieceTransform flag)
            {
                return (byte)(transform & flag) != 0;
            }

            static private void MirrorMatrix(PieceTransform pieceTransform, ref float4x4 m)
            {
                byte mirror = (byte)((byte)pieceTransform & MirrorMask);
                switch (mirror)
                {
                    case (byte)PieceTransform.MirrorX: m.c0.x *= -1; m.c1.x *= -1; m.c2.x *= -1; break;
                    case (byte)PieceTransform.MirrorY: m.c0.y *= -1; m.c1.y *= -1; m.c2.y *= -1; break;
                    case (byte)PieceTransform.MirrorZ: m.c0.z *= -1; m.c1.z *= -1; m.c2.z *= -1; break;
                }
            }

            static private float4x4 ToRotationMatrix(PieceTransform pieceTransform)
            {
                byte rotation = (byte)((byte)pieceTransform & RotationMask);
                switch (rotation)
                {
                    case (byte)PieceTransform.Rotate90: return float4x4.RotateY(math.radians(-90));
                    case (byte)PieceTransform.Rotate180: return float4x4.RotateY(math.radians(-180));
                    case (byte)PieceTransform.Rotate270: return float4x4.RotateY(math.radians(-270));
                }
                return float4x4.identity;
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
            /// skip every tile piece which covers a certain direction, this removes more tiles, borders and corners
            /// </summary>
            public Direction skipDirectionsAndBorders = Direction.None;

            /// <summary>
            /// When generating the mesh, should the algorithm generate mesh at the boundaries?
            /// If the chunk represents ground for example, then the bottom and side boundaries should
            /// not be generated (so out of bounds cells are considered filled). 
            /// If the chunk is a flying paltform which needs all of its sides rendered, then the boundaries 
            /// should be generated. (and out of bounds cell are considered empty)
            /// </summary>
            public Direction filledBoundaries = Direction.All;
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

        /// <summary>
        /// variant data in a variant volume, this contains all the possible variants for every mesher
        /// (if 8 different themes touch corners, 8 meshers will draw at that position)
        /// </summary>
        public struct VariantData
        {
            byte variant0;
            byte variant1;
            byte variant2;
            byte variant3;
            byte variant4;
            byte variant5;
            byte variant6;
            byte variant7;
        }

        private class AdjacentVolumes : System.IDisposable
        {
            public DataVolume XM { get; set; }
            public DataVolume XP { get; set; }
            public DataVolume ZM { get; set; }
            public DataVolume ZP { get; set; }

            public DataVolume XMZM { get; set; }
            public DataVolume XMZP { get; set; }
            public DataVolume XPZM { get; set; }
            public DataVolume XPZP { get; set; }

            private NativeArray<TileData> nullArray;

            public AdjacentVolumes()
            {
                nullArray = new NativeArray<TileData>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }

            public void SetAdjacent(DataVolume data, byte direction)
            {
                switch (direction)
                {
                    case (byte)Direction.XMinus: XM = data; break;
                    case (byte)Direction.XPlus: XP = data; break;
                    case (byte)Direction.ZMinus: ZM = data; break;
                    case (byte)Direction.ZPlus: ZP = data; break;
                    case (byte)Direction.XMinus | (byte)Direction.ZMinus: XMZM = data; break;
                    case (byte)Direction.XMinus | (byte)Direction.ZPlus: XMZP = data; break;
                    case (byte)Direction.XPlus | (byte)Direction.ZMinus: XPZM = data; break;
                    case (byte)Direction.XPlus | (byte)Direction.ZPlus: XPZP = data; break;
                    default:
                        {
                            Debug.LogError("direction not handled: " + direction);
                            break;
                        }
                }
            }

            public JobHandle ScheduleJobs(int themeIndex, DataVolume data, TileVolume tiles, NativeArray<ConfigTransformGroup> configs, Direction skipDirections, Direction skipDirectionsWithBorders, JobHandle handle)
            {
                if (!nullArray.IsCreated)
                {
                    nullArray = new NativeArray<TileData>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                }

                if (XM != null)
                {
                    handle = CreateBoundaryJob(Direction.XMinus, themeIndex, data, tiles, configs, skipDirections, skipDirectionsWithBorders, handle);
                }
                if (XP != null)
                {
                    handle = CreateCullLayerJob(Direction.XPlus, tiles, handle);
                }
                if (ZM != null)
                {
                    handle = CreateBoundaryJob(Direction.ZMinus, themeIndex, data, tiles, configs, skipDirections, skipDirectionsWithBorders, handle);
                }
                if (ZP != null)
                {
                    handle = CreateCullLayerJob(Direction.ZPlus, tiles, handle);
                }

                return handle;
            }

            public void Dispose()
            {
                SafeDispose(ref nullArray);
            }

            private JobHandle CreateBoundaryJob(Direction dir, int themeIndex, DataVolume data, TileVolume tiles, NativeArray<ConfigTransformGroup> configs, Direction skipDirections, Direction skipDirectionsWithBorders, JobHandle dependHandle)
            {
                var job = new UpdateBoundaryJob
                {
                    tileExtents = new Extents(tiles.XLength, tiles.YLength, tiles.ZLength),
                    dataExtents = new Extents(data.XLength, data.YLength, data.ZLength),
                    themeIndex = themeIndex,
                    configs = configs,
                    data = data.Data,

                    indexStep = LayerIndexStep.Create(dir, tiles),
                    
                    skipDirections = skipDirections,
                    skipDirectionsWithBorders = skipDirectionsWithBorders,

                    adjXM = (XM == null) ? nullArray : XM.Data,
                    adjXP = (XP == null) ? nullArray : XP.Data,
                    adjZM = (ZM == null) ? nullArray : ZM.Data,
                    adjZP = (ZP == null) ? nullArray : ZP.Data,
                    adjXMZM = (XMZM == null) ? nullArray : XMZM.Data,
                    adjXMZP = (XMZP == null) ? nullArray : XMZP.Data,
                    adjXPZM = (XPZM == null) ? nullArray : XPZM.Data,
                    adjXPZP = (XPZP == null) ? nullArray : XPZP.Data,
                    
                    tiles = tiles.Data
                };

                return job.Schedule(dependHandle);
            }

            private JobHandle CreateCullLayerJob(Direction dir, TileVolume tiles, JobHandle dependHandle)
            {
                var job = new CullLayerJob
                {
                    indexStep = LayerIndexStep.Create(dir, tiles),
                    tileExtents = new Extents(tiles.XLength, tiles.YLength, tiles.ZLength),
                    tiles = tiles.Data
                };

                return job.Schedule(dependHandle);
            }
        }

        private struct LayerIndexStep
        {
            public int start;
            public int colStep;
            public int colNum;
            public int rowStep;
            public int rowNum;

            public int StartRow(int row) { return start + rowStep * row; }
            public int NextCol(int index) { return index + colStep; }

            static public LayerIndexStep Create<T>(Direction dir, Volume<T> volume) where T : struct
            {
                return Create(dir, volume.XLength, volume.YLength, volume.ZLength);
            }

            static public LayerIndexStep Create(Direction dir, int xLength, int yLength, int zLength)
            {
                var res = new LayerIndexStep();
                res.rowStep = xLength * zLength;
                res.rowNum = yLength;
                switch (dir)
                {
                    case Direction.XPlus:
                        {
                            res.start = xLength - 1;
                            res.colStep = xLength;
                            res.colNum = zLength;
                        }
                        break;
                    case Direction.XMinus:
                        {
                            res.start = 0;
                            res.colStep = xLength;
                            res.colNum = zLength;
                        }
                        break;
                    case Direction.ZMinus:
                        {
                            res.start = 0;
                            res.colStep = 1;
                            res.colNum = xLength;
                        }
                        break;
                    case Direction.ZPlus:
                        {
                            res.start = xLength * (zLength - 1);
                            res.colStep = 1;
                            res.colNum = xLength;
                        }
                        break;
                    default:
                        {
                            Debug.LogError("direction not handled!");
                            break;
                        }
                }

                return res;
            }
        }
    }
}