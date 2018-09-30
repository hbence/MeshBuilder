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
using System;

namespace MeshBuilder
{
    public class TileMesher3D : IMeshBuilder
    {
        private const string DefaultName = "tile_mesh_3d";
        private const float MinCellSize = float.Epsilon;
        static private readonly Settings DefaultSettings = new Settings();
        private enum State { Uninitialized, Initialized, Generating }
        private enum GenerationType { FromDataUncached, FromDataCachedTiles, FromTiles }
        
        private string name;
        private State state = State.Uninitialized;
        private GenerationType generationType = GenerationType.FromDataUncached;

    // INITIAL DATA
        private TileTheme3D theme;
        private DataVolume data;
        private Settings settings;
        private float3 positionOffset;

        // in the data volume we're generating the mesh
        // for this value
        private byte fillValue;

        private Extents dataExtents;
        private Extents tileExtents;

        // GENERATED DATA
        private TileVolume tiles;

        private JobHandle lastHandle;

        // TEMP DATA
        private NativeList<PlacedTileData> tempTileList;
        private NativeArray<MeshTile> tempMeshTileInstances;

        public Mesh Mesh { get; private set; }

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
        
        public void Init(DataVolume dataVolume, byte fillValue, TileTheme3D theme, float3 posOffset = default(float3))
        {
            Init(dataVolume, fillValue, theme, DefaultSettings, posOffset);
        }

        public void Init(DataVolume dataVolume, byte fillValue, TileTheme3D theme, Settings settings, float3 posOffset = default(float3))
        {
            Dispose();

            this.data = dataVolume;
            this.theme = theme;
            this.fillValue = fillValue;
            this.settings = settings;
            this.positionOffset = posOffset;

            this.theme.Init();

            int x = data.XLength;
            int y = data.YLength;
            int z = data.ZLength;
            dataExtents = new Extents(x, y, z);
            tileExtents = new Extents(x + 1, y + 1, z + 1);

            generationType = GenerationType.FromDataUncached;

            state = State.Initialized;
        }

        public void Dispose()
        {
            state = State.Uninitialized;

            lastHandle.Complete();
            DisposeTemp();

            if (tiles != null)
            {
                tiles.Dispose();
                tiles = null;
            }
        }

        public void StartGeneration()
        {
            if (!IsInitialized)
            {
                Error("not initialized!");
                return;
            }

            if (IsGenerating)
            {
                Error("is already generating!");
                return;
            }

            state = State.Generating;

            lastHandle.Complete();
            DisposeTemp();

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

        public void EndGeneration()
        {
            if (!IsGenerating)
            {
                Warning("is not generating! nothing to stop");
                return;
            }

            lastHandle.Complete();
            state = State.Initialized;

            CombineMeshes(Mesh, tempMeshTileInstances, theme);

            if (generationType == GenerationType.FromDataUncached)
            {
                if (HasTilesData)
                {
                    tiles.Dispose();
                    tiles = null;
                }
            }

            DisposeTemp();
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
                skipFloor = settings.skipFloor,
                skipCeiling = settings.skipCeiling,
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

        private void DisposeTemp()
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

        private bool HasTilesData { get { return tiles != null && !tiles.IsDisposed; } }

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
            private static readonly byte FloorConfig = Config.ToIndex(false, false, false, false, true, true, true, true);
            private static readonly byte CeilingConfig = Config.ToIndex(true, true, true, true, false, false, false, false);

            public Extents tileExtents;
            public bool skipFloor;
            public bool skipCeiling;

            [ReadOnly] public NativeArray<TileVariant> tiles;
            [ReadOnly] public NativeArray<TileConfiguration> tileConfigs;
            [WriteOnly] public NativeList<PlacedTileData> placedList;

            public void Execute()
            {
                if (!skipFloor && ! skipCeiling)
                {
                    FillListNoSkip();
                }
                else
                {
                    if (skipFloor && !skipCeiling)
                    {
                        FillListSkipFn(SkipFloor);
                    }
                    else if (!skipFloor && skipCeiling)
                    {
                        FillListSkipFn(SkipCeiling);
                    }
                    else
                    {
                        FillListSkipFn(SkipFloorAndCeiling);
                    }
                }
            }

            private delegate bool AllowConfig(byte index);

            private bool SkipFloor(byte index) { return tileConfigs[index].TileCount > 0 && index != FloorConfig; }
            private bool SkipCeiling(byte index) { return tileConfigs[index].TileCount > 0 && index != CeilingConfig; }
            private bool SkipFloorAndCeiling(byte index) { return tileConfigs[index].TileCount > 0 && index != FloorConfig && index != CeilingConfig; }
            
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

            private void FillListSkipFn(AllowConfig allowConfig)
            {
                for (int i = 0; i < tiles.Length; ++i)
                {
                    byte confIndex = tiles[i].config;
                    if (allowConfig(confIndex))
                    {
                        AddConfig(tileConfigs[confIndex], i);
                    }
                }
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

        private float ValidateCellSize(float value)
        {
            if (value <= 0)
            {
                value = MinCellSize;
                Warning("Minimum cell size has to be greater than 0!");
            }

            return value;
        }

        private void Warning(string msg, params object[] args)
        {
            Debug.LogWarningFormat(name + " - " + msg, args);
        }

        private void Error(string msg, params object[] args)
        {
            Debug.LogErrorFormat(name + " - " + msg, args);
        }

        public bool IsInitialized { get { return state != State.Uninitialized; } }
        public bool IsGenerating { get { return state == State.Generating; } }

        [System.Serializable]
        public class Settings
        {
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
            /// Skip floor tiles (useful if floor surface is generated by something else).
            /// </summary>
            public bool skipFloor = true;

            /// <summary>
            /// Skip ceiling tiles.
            /// </summary>
            public bool skipCeiling = true;

            /// <summary>
            /// Skip tiles which cover a certain direction, the difference between this and
            /// skip floor / ceiling is this one also skips borders and corners. Mostly useful for
            /// chunks which are only visible from certain camera directions, other directions can be
            /// skipped.
            /// </summary>
            public byte skipDirections = (int)Direction.None;

            /// <summary>
            /// When generating the mesh, should the algorithm consider the chunk boundaries empty?
            /// If the chunk represents ground for example, then the bottom and side boundaries should
            /// be NOT empty so those sides don't get generated. If the chunk is a flying island which
            /// needs all of its sides rendered, then the boundaries should be considered empty.
            /// </summary>
            public byte emptyBoundaries = (int)Direction.All;
        }

        public enum Rotation : byte
        {
            CW0 = 0,
            CW90,
            CW180,
            CW270
        }
        
        public enum Direction : byte
        {
            None = 0,

            XPlus = 1,
            XMinus = 2,
            YPlus = 4,
            YMinus = 8,
            ZPlus = 16,
            ZMinus = 32,

            XAxis = XPlus | XMinus,
            YAxis = YPlus | YMinus,
            ZAxis = ZPlus | ZMinus,
            All = XAxis | YAxis | ZAxis
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

        public struct PlacedTileData
        {
            public int3 coord;
            public TileData data;
            public byte variation;
        }

        public struct MeshTile
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
