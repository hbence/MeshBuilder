using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using System.Runtime.InteropServices;

using static MeshBuilder.Extents;

using DataVolume = MeshBuilder.Volume<byte>; // type values
using TileVolume = MeshBuilder.Volume<MeshBuilder.TileMesher3D.TileVariant>; // configuration indices
using TileElem = MeshBuilder.TileTheme3D.Elem;
using Config = MeshBuilder.TileMesherConfigurations;
using Rotation = MeshBuilder.TileMesherConfigurations.Rotation;
using Direction = MeshBuilder.TileMesherConfigurations.Direction;

namespace MeshBuilder
{
    public class TileMesher3D : TileMesherBase<TileMesher3D.TileVariant>
    {
        private const string DefaultName = "tile_mesh_3d";
        static private readonly Settings DefaultSettings = new Settings { };

        // INITIAL DATA
        private TileTheme3D theme;
        private DataVolume data;
        private Settings settings;

        // in the data volume we're generating the mesh
        // for this value
        private byte fillValue;

        private Extents dataExtents;
        private Extents tileExtents;

        // TEMP DATA
        private NativeList<PlacedTileData> tempTileList;
        private NativeArray<MeshTile> tempMeshTileInstances;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="name">name of the mesher, mostly for logging and debug purposes</param>
        public TileMesher3D(string name = DefaultName)
        {
            this.name = name;

            Mesh = new Mesh();
            Mesh.name = name;
        }
        
        public void Init(DataVolume dataVolume, byte fillValue, TileTheme3D theme)
        {
            Init(dataVolume, fillValue, theme, DefaultSettings);
        }

        public void Init(DataVolume dataVolume, byte fillValue, TileTheme3D theme, Settings settings)
        {
            Dispose();

            SettingsSanityCheck(settings);

            this.data = dataVolume;
            this.theme = theme;
            this.fillValue = fillValue;
            this.settings = settings;

            this.theme.Init();

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
                tiles = new TileVolume(tileExtents.X, tileExtents.Y, tileExtents.Z);
                lastHandle = ScheduleTileGeneration(tiles, data, 128, lastHandle);
            }
            else if (generationType == GenerationType.FromDataCachedTiles)
            {
               if (!HasTilesData)
                {
                    tiles = new TileVolume(tileExtents.X, tileExtents.Y, tileExtents.Z);
                    lastHandle = ScheduleTileGeneration(tiles, data, 128, lastHandle);
                }
            }

            if (HasTilesData)
            {
                // TODO:
                // unfortunately I have to call complete() immediately after collecting the tile pieces which need meshes, because I need to know the 
                // list size
                // I could avoid this if I would store the CombineInstance in the tiles volume (three per cell), and then set them in a single parallel job,
                // but then I would waste more memory and I would have to go through the whole volume to collect the CombineInstances into an array later.
                // not sure if it would be worth it but perhaps I could test it later?

                tempTileList = new NativeList<PlacedTileData>((int)(tiles.Data.Length * 0.25f), Allocator.Temp);
                lastHandle = SchedulePlacedTileListGeneration(tempTileList, tiles, lastHandle);
                
                lastHandle.Complete();

                tempMeshTileInstances = new NativeArray<MeshTile>(tempTileList.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                lastHandle = ScheduleCombineInstanceGeneration(tempMeshTileInstances, tempTileList, 16, lastHandle);
            }
            else
            {
                Error("there is no tile data!");
            }

            JobHandle.ScheduleBatchedJobs();
        }

        override protected void AfterGenerationJobsComplete()
        {
            CombineMeshes(Mesh, tempMeshTileInstances, theme);
        }

        static private void CombineMeshes(Mesh mesh, NativeArray<MeshTile> instanceData, TileTheme3D theme)
        {
            var instanceArray = new CombineInstance[instanceData.Length];
            for (int i = 0; i < instanceData.Length; ++i)
            {
                var data = instanceData[i];
                data.instance.mesh = theme.GetMesh(data.elem, data.variation);
                instanceArray[i] = data.instance;
            }

            mesh.CombineMeshes(instanceArray, true, true);
        }

        private JobHandle ScheduleTileGeneration(TileVolume resultTiles, DataVolume data, int batchCount, JobHandle dependOn)
        {
            var tileGeneration = new GenerateTileDataJob
            {
                tileExtents = tileExtents,
                dataExtents = dataExtents,
                fillValue = fillValue,
                data = data.Data,
                tiles = resultTiles.Data
            };

            return tileGeneration.Schedule(resultTiles.Data.Length, batchCount, dependOn);
        }

        private JobHandle SchedulePlacedTileListGeneration(NativeList<PlacedTileData> resultTileList, TileVolume tiles, JobHandle dependOn)
        {
            var listGeneration = new GeneratePlacedTileList
            {
                skipDirection = settings.skipDirections,
                skipDirectionWithNoBorders = settings.skipDirectionsAndBorders,
                tileExtents = tileExtents,
                tiles = tiles.Data,
                tileConfigs = Config.FullSetConfigurations,
                placedList = resultTileList
            };

            return listGeneration.Schedule(dependOn);
        }

        private JobHandle ScheduleCombineInstanceGeneration(NativeArray<MeshTile> resultMeshTiles, NativeList<PlacedTileData> tileList, int batchCount, JobHandle dependOn)
        {
            var instanceGeneration = new GenerateCombineInstances
            {
                tiles = tileList.ToDeferredJobArray(),
                meshTiles = resultMeshTiles
            };

            return instanceGeneration.Schedule(tileList.Length, batchCount, dependOn);
        }

        override protected void DisposeTemp()
        {
            if (tempTileList.IsCreated)
            {
                tempTileList.Dispose();
            }

            if (tempMeshTileInstances.IsCreated)
            {
                tempMeshTileInstances.Dispose();
            }
        }

        [BurstCompile]
        private struct GenerateTileDataJob : IJobParallelFor
        {
            public Extents tileExtents;
            public Extents dataExtents;
            public byte fillValue;

            [ReadOnly] public NativeArray<byte> data;
            [WriteOnly] public NativeArray<TileVariant> tiles;

            public void Execute(int index)
            {
                byte configuration = 0;
                int3 dc = CoordFromIndex(index, tileExtents);

                if (IsFilled(dc.x, dc.y, dc.z)) configuration |= Config.TopFrontRight;
                if (IsFilled(dc.x - 1, dc.y, dc.z)) configuration |= Config.TopFrontLeft;
                if (IsFilled(dc.x, dc.y, dc.z - 1)) configuration |= Config.TopBackRight;
                if (IsFilled(dc.x - 1, dc.y, dc.z - 1)) configuration |= Config.TopBackLeft;

                if (IsFilled(dc.x, dc.y - 1, dc.z)) configuration |= Config.BottomFrontRight;
                if (IsFilled(dc.x - 1, dc.y - 1, dc.z)) configuration |= Config.BottomFrontLeft;
                if (IsFilled(dc.x, dc.y - 1, dc.z - 1)) configuration |= Config.BottomBackRight;
                if (IsFilled(dc.x - 1, dc.y - 1, dc.z - 1)) configuration |= Config.BottomBackLeft;

                tiles[index] = new TileVariant { config = configuration, variation = 0 };
            }

            private bool IsFilled(int x, int y, int z)
            {
                if (x < 0 || y < 0 || z < 0 || x >= dataExtents.X || y >= dataExtents.Y || z >= dataExtents.Z)
                {
                    return false;
                }

                return IsFilled(IndexFromCoord(x, y, z, dataExtents.XZ, dataExtents.X));
            }

            private bool IsFilled(int index)
            {
                return data[index] == fillValue;
            }
        }

        // go through the tile volume data and separate the configs into separate 
        // elems with their config and coordinates
        private struct GeneratePlacedTileList : IJob
        {
            private const byte None = (byte)Direction.None; 

            private const byte BottomConfig = Config.BottomBackLeft | Config.BottomBackRight | Config.BottomFrontLeft | Config.BottomFrontRight;
            private const byte TopConfig = Config.TopBackLeft | Config.TopBackRight | Config.TopFrontLeft | Config.TopFrontRight;
            private const byte LeftConfig = Config.TopBackLeft | Config.TopFrontLeft | Config.BottomBackLeft | Config.BottomFrontLeft;
            private const byte RightConfig = Config.TopBackRight | Config.TopFrontRight| Config.BottomBackRight| Config.BottomFrontRight;
            private const byte FrontConfig = Config.BottomFrontLeft | Config.BottomFrontRight | Config.TopFrontLeft | Config.TopFrontRight;
            private const byte BackConfig = Config.TopBackLeft | Config.TopBackRight | Config.BottomBackLeft | Config.BottomBackRight;

            public Extents tileExtents;
            public byte skipDirection;
            public byte skipDirectionWithNoBorders;

            [ReadOnly] public NativeArray<TileVariant> tiles;
            [ReadOnly] public NativeArray<TileConfiguration> tileConfigs;
            [WriteOnly] public NativeList<PlacedTileData> placedList;

            public void Execute()
            {
                // TEST TODO: I separated the cases and rolled out some of the expected values, it made the Job a bit more
                // convoluted, maybe I should test it later if it was worth it
                if (skipDirection == None && skipDirectionWithNoBorders == None)
                {
                    FillListNoSkip();
                }
                else if (skipDirection != None)
                {
                    byte[] skipSides = new byte[6];
                    int skipCount = 0;
                    if (HasFlag(skipDirection, Direction.XPlus))  { skipSides[skipCount] = LeftConfig; ++skipCount; }
                    if (HasFlag(skipDirection, Direction.XMinus)) { skipSides[skipCount] = RightConfig; ++skipCount; }
                    if (HasFlag(skipDirection, Direction.YPlus))  { skipSides[skipCount] = BottomConfig; ++skipCount; }
                    if (HasFlag(skipDirection, Direction.YMinus)) { skipSides[skipCount] = TopConfig; ++skipCount; }
                    if (HasFlag(skipDirection, Direction.ZPlus))  { skipSides[skipCount] = BackConfig; ++skipCount; }
                    if (HasFlag(skipDirection, Direction.ZMinus)) { skipSides[skipCount] = FrontConfig; ++skipCount; }

                    SkipFn skipFn = (byte index) => { return Skip(index, skipSides[0]); };
                    if (skipCount > 1)
                    {
                        if (skipCount > 2)
                        {
                            if (skipCount > 3)
                            {
                                skipFn = (byte index) => { return Skip(index, skipSides, skipCount); };
                            }
                            else
                            {
                                skipFn = (byte index) => { return Skip(index, skipSides[0], skipSides[1], skipSides[2]); };
                            }
                        }
                        else
                        {
                            skipFn = (byte index) => { return Skip(index, skipSides[0], skipSides[1]); };
                        }
                    }

                    if (skipDirectionWithNoBorders != None)
                    {
                        FillListSkipBoth(skipFn);
                    }
                    else
                    {
                        FillListSkip(skipFn);
                    }
                }
                else //(skipDirectionWithNoBorders != Direction.None)
                {
                    FillListSkipWithTransform();
                }
            }

            private delegate bool SkipFn(byte index);

            // TEST TODO: I wanted to roll out the most likely possibilites, with the double indirection I'm not sure it will make any difference, I should test that
            static private bool Skip(byte index, byte skip0) { return index == skip0; }
            static private bool Skip(byte index, byte skip0, byte skip1) { return index == skip0 || index == skip1; }
            static private bool Skip(byte index, byte skip0, byte skip1, byte skip2) { return index == skip0 || index == skip1 || index == skip2; }
            static private bool Skip(byte index, byte[] skip, int count)
            {
                for (int i = 0; i < count; ++i) { if (index == skip[i]){ return true; } }
                return  false;
            }

            // a cell is divided into four quadrants, depending on which side needs to be skipped, 
            // flagA is the side closer to that if you imagine going towards a box from that direction (the front of the box looks away from the camera) 
            // (skip Z+ -> layerA are the back quadrants (forward you hit the back first), 
            // skip X- -> layerA are the right quadrants (going left you hit the right side) etc.)
            // layerB is the side behind layerA
            static private byte TransformSkipSide(byte value, byte flagA0, byte flagA1, byte flagA2, byte flagA3, byte flagB0, byte flagB1, byte flagB2, byte flagB3)
            {
                int layerB0 = (value & flagB0);
                int layerB1 = (value & flagB1);
                int layerB2 = (value & flagB2);
                int layerB3 = (value & flagB3);
                byte result = (byte)(layerB0 | layerB1 | layerB2 | layerB3);
                if ((value & flagA0) > 0 && layerB0 > 0) result |= flagA0;
                if ((value & flagA1) > 0 && layerB1 > 0) result |= flagA1;
                if ((value & flagA2) > 0 && layerB2 > 0) result |= flagA2;
                if ((value & flagA3) > 0 && layerB3 > 0) result |= flagA3;

                return result;
            }
            
            private void FillListNoSkip()
            {
                for (int i = 0; i < tiles.Length; ++i)
                {
                    byte confIndex = tiles[i].config;
                    if (tileConfigs[confIndex].TileCount > 0)
                    {
                        AddConfig(tileConfigs[confIndex], i);   
                    }
                }
            }

            private void FillListSkip(SkipFn skipFn)
            {
                for (int i = 0; i < tiles.Length; ++i)
                {
                    byte confIndex = tiles[i].config;
                    if (tileConfigs[confIndex].TileCount > 0 && !skipFn(confIndex))
                    {
                        AddConfig(tileConfigs[confIndex], i);
                    }
                }
            }

            private void FillListSkipWithTransform()
            {
                for (int i = 0; i < tiles.Length; ++i)
                {
                    byte confIndex = tiles[i].config;
                    confIndex = TransformConfig(confIndex, skipDirectionWithNoBorders);

                    if (tileConfigs[confIndex].TileCount > 0)
                    {
                        AddConfig(tileConfigs[confIndex], i);
                    }
                }
            }

            private void FillListSkipBoth(SkipFn skipFn)
            {
                for (int i = 0; i < tiles.Length; ++i)
                {
                    byte confIndex = tiles[i].config;
                    confIndex = TransformConfig(confIndex, skipDirectionWithNoBorders);

                    if (tileConfigs[confIndex].TileCount > 0 && !skipFn(confIndex))
                    {
                        AddConfig(tileConfigs[confIndex], i);
                    }
                }
            }

            private static byte TransformConfig(byte config, byte skipSides)
            {
                if (HasFlag(skipSides, Direction.XPlus))
                {
                    config = TransformSkipSide(config, Config.BottomBackRight, Config.BottomFrontRight, Config.TopBackRight, Config.TopFrontRight,
                                                             Config.BottomBackLeft, Config.BottomFrontLeft, Config.TopBackLeft, Config.TopFrontLeft);
                }
                if (HasFlag(skipSides, Direction.XMinus))
                {
                    config = TransformSkipSide(config, Config.BottomBackLeft, Config.BottomFrontLeft, Config.TopBackLeft, Config.TopFrontLeft,
                                                             Config.BottomBackRight, Config.BottomFrontRight, Config.TopBackRight, Config.TopFrontRight);
                }
                if (HasFlag(skipSides, Direction.YPlus))
                {
                    config = TransformSkipSide(config, Config.BottomBackLeft, Config.BottomFrontLeft, Config.BottomBackRight, Config.BottomFrontRight,
                                                             Config.TopBackLeft, Config.TopFrontLeft, Config.TopBackRight, Config.TopFrontRight);
                }
                if (HasFlag(skipSides, Direction.YMinus))
                {
                    config = TransformSkipSide(config, Config.TopBackLeft, Config.TopFrontLeft, Config.TopBackRight, Config.TopFrontRight,
                                                             Config.BottomBackLeft, Config.BottomFrontLeft, Config.BottomBackRight, Config.BottomFrontRight);
                }
                if (HasFlag(skipSides, Direction.ZMinus))
                {
                    config = TransformSkipSide(config, Config.TopFrontLeft, Config.TopFrontRight, Config.BottomFrontLeft, Config.BottomFrontRight,
                                                             Config.TopBackLeft, Config.TopBackRight, Config.BottomBackLeft, Config.BottomBackRight);
                }
                if (HasFlag(skipSides, Direction.ZPlus))
                {
                    config = TransformSkipSide(config, Config.TopBackLeft, Config.TopBackRight, Config.BottomBackLeft, Config.BottomBackRight,
                                                             Config.TopFrontLeft, Config.TopFrontRight, Config.BottomFrontLeft, Config.BottomFrontRight);
                }

                return config;
            }

            private void AddConfig(TileConfiguration config, int i)
            {
                var coord = CoordFromIndex(i, tileExtents);

                for (int tileInd = 0; tileInd < config.TileCount; ++tileInd)
                {
                    var tile = new PlacedTileData
                    {
                        coord = coord,
                        data = config.GetTile(tileInd),
                        variation = tiles[i].variation
                    };
                    placedList.Add(tile);
                }
            }

            static private bool HasFlag(byte value, Direction dir) { return (value & (byte)dir) != 0; }
        }

        [BurstCompile]
        private struct GenerateCombineInstances : IJobParallelFor
        {
            private static readonly float3 Up = new float3 { x = 0, y = 1, z = 0 };
            private static readonly float3 Down = new float3 { x = 1, y = 0, z = 0 };
            private static readonly float3 One = new float3 { x = 1, y = 1, z = 1 };

            private static readonly float3 MirrorX = new float3 { x = -1, y = 1, z = 1 };
            private static readonly float3 MirrorY = new float3 { x = 1, y = -1, z = 1 };
            private static readonly float3 MirrorXY = new float3 { x = -1, y = -1, z = 1 };

            private static readonly float3 XPlus  = new float3 { x = 1, y = 0, z = 0 };
            private static readonly float3 XMinus = new float3 { x =-1, y = 0, z = 0 };
            private static readonly float3 ZPlus  = new float3 { x = 0, y = 0, z = 1 };
            private static readonly float3 ZMinus = new float3 { x = 0, y = 0, z =-1 };

            [ReadOnly] public NativeArray<PlacedTileData> tiles;
            [WriteOnly] public NativeArray<MeshTile> meshTiles;

            public void Execute(int index)
            {
                TileData tile = tiles[index].data;
                float3 pos = tiles[index].coord;
                
                MatrixConverter m = new MatrixConverter {  };
                Vector3 forward = ToDirection(tile.rot);
                Vector3 up = Up;
                m.float4x4 = math.mul(float4x4.lookAt(pos, forward, up), float4x4.scale(ToScale(tile.mirror)));

                meshTiles[index] = new MeshTile
                {
                    elem = tile.elem,
                    variation = tiles[index].variation,
                    instance = new CombineInstance { subMeshIndex = 0, transform = m.Matrix4x4 }
                };
            }

            static private float3 ToScale(Direction mirrorDir)
            {
                switch(mirrorDir)
                {
                    case Direction.XAxis: return MirrorX;
                    case Direction.YAxis: return MirrorY;
                    case (Direction.XAxis | Direction.YAxis): return MirrorXY;
                }
                
                return One;
            }

            static private float3 ToDirection(Rotation rot)
            {
                // TODO: bug in the burst compiler, remove the casting and uncomment the enums when it gets fixed
                switch ((int)rot)
                {
                    case 0/*Rotation.CW0*/: return ZMinus;
                    case 1/*Rotation.CW90*/: return XMinus;
                    case 2/*Rotation.CW180*/: return ZPlus;
                    case 3/*Rotation.CW270*/: return XPlus;
                }
                return ZPlus;
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

        private void SettingsSanityCheck(Settings settings)
        {
            int skipDir = settings.skipDirections;
            int skipDirBor = settings.skipDirectionsAndBorders;
            if (skipDir == (int)Direction.All) Warning("all directions are skipped!");
            if (skipDirBor == (int)Direction.All) Warning("all directions are skipped, nothing will be rendered!");

            // should I check for skipDirections and skipDirectionsAndBorders overlap?
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
            /// skip sides of the mesh, only full side elements get skipped (so borders, edges, corners stay intact),
            /// it doesn't modify the tile selection
            /// - which means smaller tunnels for example may stay unaffected (if they only contain edge pieces)
            /// - if the edge or corner tiles contain visible features (e.g. rocks standing out), this will keep them so
            /// so there is no visible change if the mesh is recalculated with different skipDirections
            /// </summary>
            public byte skipDirections = (byte)Direction.None;

            /// <summary>
            /// skip every tile piece which covers a certain direction, this removes more tiles, but changes the selected tile
            /// pieces, it is recommended if the skip directions never change (not used for dynamic culling) or for certain tile sets
            /// where the change is not visible
            /// </summary>
            public byte skipDirectionsAndBorders = (byte)Direction.None;

            // TODO: NOT IMPLEMENTED
            /// <summary>
            /// When generating the mesh, should the algorithm consider the chunk boundaries empty?
            /// If the chunk represents ground for example, then the bottom and side boundaries should
            /// be NOT empty so those sides don't get generated. If the chunk is a flying island which
            /// needs all of its sides rendered, then the boundaries should be considered empty.
            /// </summary>
            public byte emptyBoundaries = (int)Direction.All;
        }
        
        public struct TileData
        {
            public TileElem elem;
            public Rotation rot;
            public Direction mirror;
        }

        public struct TileVariant
        {
            public byte config;
            public byte variation;
        }

        internal struct PlacedTileData
        {
            public int3 coord;
            public TileData data;
            public byte variation;
        }

        internal struct MeshTile
        {
            public TileElem elem;
            public int variation;
            public CombineInstance instance;
        }

        public struct TileConfiguration
        {
            // This has to be a struct, so instead of an allocated container it has 4 tile members
            // no configuration needs more than that. NativeArray seemed like an overkill.
            private TileData tile0;
            private TileData tile1;
            private TileData tile2;
            private TileData tile3;
            public byte TileCount { get; private set; }

            public TileConfiguration(params TileData[] tiles)
            {
                if (tiles == null || tiles.Length == 0)
                {
                    TileCount = 0;
                    tile0 = default(TileData);
                    tile1 = default(TileData);
                    tile2 = default(TileData);
                    tile3 = default(TileData);
                }
                else
                {
                    TileCount = (byte)tiles.Length;
                    tile0 = tiles[0];
                    tile1 = (tiles.Length > 1) ? tiles[1] : default(TileData);
                    tile2 = (tiles.Length > 2) ? tiles[2] : default(TileData);
                    tile3 = (tiles.Length > 3) ? tiles[3] : default(TileData);
                }
            }

            public TileData GetTile(int index)
            {
                switch(index)
                {
                    case 0: return tile0;
                    case 1: return tile1;
                    case 2: return tile2;
                    case 3: return tile3;
                    default:
                        {
                            Debug.LogError("GetTile() invalid index:" + index);
                            break;
                        }
                }
                return tile0;
            }
        }
    }
}
