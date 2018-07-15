using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

using static MeshBuilder.Extents;

using DataVolume = MeshBuilder.Volume<byte>;
using TileVolume = MeshBuilder.Volume<MeshBuilder.TileMesher.TileData>;
using TileElem = MeshBuilder.VolumeTheme.Elem;

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
        private float3 positionOffset;

        private Extents dataExtents;
        private Extents tileExtents;

        private bool inited = false;
        private bool isGenerating = false;

        // temps
        private NativeList<TileDataInfo> tileList;
        private NativeArray<MeshTileWrapper> combineArray;

        // lookup arrays
        private NativeArray<TileData> tileConfigurationArray;
        private NativeArray<float3> directionArray;

        // GENERATION JOBS
        private TileVolume tiles;
        private JobHandle lastHandle;

        // MESH BUILDING
        private Mesh mesh;

        public TileMesher()
        {
            mesh = new Mesh();
        }

        public void Init(DataVolume data, VolumeTheme theme, float cellSize = 1f, float3 posOffset = default(float3))
        {
            inited = true;

            this.data = data;
            this.theme = theme;
            this.cellSize = cellSize;
            this.positionOffset = posOffset;

            int x = data.XLength;
            int y = data.YLength;
            int z = data.ZLength;
            dataExtents = new Extents(x, y, z);
            tileExtents = new Extents(x + 1, y, z + 1);

            if (tiles != null)
            {
                tiles.Dispose();
            }

            tiles = new TileVolume(x + 1, y, z + 1); // vertices points for the same grid size as the data volume
            CreateLookupArrays();
        }

        private void CreateLookupArrays()
        {
            if (!tileConfigurationArray.IsCreated)
            {
                tileConfigurationArray = new NativeArray<TileData>(TileConfigurations.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                tileConfigurationArray.CopyFrom(TileConfigurations);
            }

            if (!directionArray.IsCreated)
            {
                directionArray = new NativeArray<float3>(Directions.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                directionArray.CopyFrom(Directions);
            }
        }

        public void Dispose()
        {
            lastHandle.Complete();
            DisposeTemp();

            if (tiles != null)
            {
                tiles.Dispose();
                tiles = null;
            }

            if (tileConfigurationArray.IsCreated) tileConfigurationArray.Dispose();
            if (directionArray.IsCreated) directionArray.Dispose();
        }

        private void DisposeTemp()
        {
            if (tileList.IsCreated) tileList.Dispose();
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

            // calculate the tiles in a volume
            var tileGenerationJob = new GenerateTileDataJob
            {
                dataExtents = dataExtents,
                tileExtents = tileExtents,
                volumeData = data.Data,
                tileConfigurations = tileConfigurationArray,
                tiles = tiles.Data
            };
            lastHandle = tileGenerationJob.Schedule(tiles.Data.Length, 256, lastHandle);

            // collect the tiles which needs to be processed
            tileList = new NativeList<TileDataInfo>((int)(tiles.Data.Length * 0.2f), Allocator.Temp);
            var tileListGeneration = new GenerateTileDataInfoList
            {
                tileExtents = tileExtents,
                tiles = tiles.Data,
                result = tileList
            };
            lastHandle = tileListGeneration.Schedule(lastHandle);

            lastHandle.Complete();

            // generate the CombineInstance structs based on the list of tiles
            combineArray = new NativeArray<MeshTileWrapper>(tileList.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var combineList = new GenerateCombineInstances
            {
                cellSize = cellSize,
                directions = directionArray,
                tiles = tileList.ToDeferredJobArray(),
                result = combineArray
            };
            lastHandle = combineList.Schedule(tileList.Length, 16, lastHandle);

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

        internal struct TileData
        {
            public TileElem elem;
            public Rotation rot;
            public byte visibility;
            public byte variation;
        }

        internal struct TileDataInfo
        {
            public int3 coord;
            public TileData data;
        }

        [BurstCompile]
        internal struct GenerateTileDataJob : IJobParallelFor
        {
            // tile are generated on the vertices points of the grid
            // on a single layer, at every vertex 4 cells join
            // the flags mean which cells are filled from the point of view of the vertex
            // the tile will be chosen based on the configuration of the filled cells
            // top Z+ / right X+
            private const byte HasBL = 1;
            private const byte HasTL = 2;
            private const byte HasBR = 4;
            private const byte HasTR = 8;

            public Extents tileExtents;
            public Extents dataExtents;

            [ReadOnly] public NativeArray<TileData> tileConfigurations;
            [ReadOnly] public NativeArray<byte> volumeData;
            [WriteOnly] public NativeArray<TileData> tiles;

            public void Execute(int index)
            {
                byte configuration = 0;

                int3 c = CoordFromIndex(index, tileExtents);
                int dataIndex = IndexFromCoord(c, dataExtents);

                if (c.z < dataExtents.Z)
                {
                    if (c.x < dataExtents.X && IsFilled(dataIndex)) { configuration |= HasTR; }
                    if (c.x > 0 && IsFilled(dataIndex - 1)) { configuration |= HasTL; }
                }
                if (c.z > 0)
                {
                    // the bottom checks are from the previous row
                    dataIndex -= dataExtents.X;
                    if (c.x < dataExtents.X && IsFilled(dataIndex)) { configuration |= HasBR; }
                    if (c.x > 0 && IsFilled(dataIndex - 1)) { configuration |= HasBL; }
                }

                tiles[index] = tileConfigurations[configuration];
            }

            private bool IsFilled(int index)
            {
                return volumeData[index] == Filled;
            }
        }

        internal struct GenerateTileDataInfoList : IJob
        {
            private static TileData NullTile = new TileData { elem = TileElem.Null, visibility = 0 };

            public Extents tileExtents;
            [ReadOnly] public NativeArray<TileData> tiles;
            [WriteOnly] public NativeList<TileDataInfo> result;

            public void Execute()
            {
                for (int i = 0; i < tiles.Length; ++i)
                {
                    if (tiles[i].elem != TileElem.Null)
                    {
                        var c = CoordFromIndex(i, tileExtents);
                        var info = new TileDataInfo { coord = c, data = tiles[i] };

                        var elem = tiles[i].elem;
                        var rot = tiles[i].rot;

                        TileData under = c.y > 0 ? tiles[i - tileExtents.XZ] : NullTile;
                        TileData above = c.y < tileExtents.Y - 1 ? tiles[i + tileExtents.XZ] : NullTile;

                        if (above.elem == TileElem.Null) { info.data.elem = TopVersion(info.data); }
                        else if (elem == TileElem.Side && above.elem == TileElem.CornerConvex) { info.data.elem = TileElem.TopSide; }
                        else if (elem != TileElem.Side && (above.elem != TileElem.Side && (elem != above.elem || rot != above.rot))) { info.data.elem = TopVersion(info.data); }
                        else if (elem != TileElem.Side && ((elem != under.elem || rot != under.rot))) { info.data.elem = BottomVersion(info.data); }
                        else if (under.elem == TileElem.Null) { info.data.elem = BottomVersion(info.data); }

                        result.Add(info);
                    }
                }
            }

            static private TileElem TopVersion(TileData data)
            {
                return (TileElem)((byte)data.elem - 4);
            }
            static private TileElem BottomVersion(TileData data)
            {
                return (TileElem)((byte)data.elem + 4);
            }
        }

        [BurstCompile]
        internal struct GenerateCombineInstances : IJobParallelFor
        {
            private static readonly float3 Up = new float3 { x = 0, y = 1, z = 0 };

            public float cellSize;
            public float3 offset;
            [ReadOnly] public NativeArray<float3> directions;

            [ReadOnly] public NativeArray<TileDataInfo> tiles;
            [WriteOnly] public NativeArray<MeshTileWrapper> result;

            public void Execute(int index)
            {
                TileData tile = tiles[index].data;
                int3 c = tiles[index].coord;

                float3 p = c;
                p *= cellSize;
                p += offset;
                float4x4 m = float4x4.lookAt(p, directions[(byte)tile.rot], Up);

                result[index] = new MeshTileWrapper
                {
                    elem = tile.elem,
                    combine = new CombineInstance { subMeshIndex = 0, transform = ToMatrix4x4(m) }
                };
            }

            private static Matrix4x4 ToMatrix4x4(float4x4 m)
            {
                return new Matrix4x4(m.c0, m.c1, m.c2, m.c3);
            }
        }

        internal struct MeshTileWrapper
        {
            public TileElem elem;
            public int variation;
            public CombineInstance combine;
        }

        static readonly TileData[] TileConfigurations = new TileData[]
        {
            new TileData { elem = TileElem.Null, visibility = 0 },                                                   // 0 - nothing, no mesh
            new TileData { elem = TileElem.CornerConvex, rot = Rotation.CW270, visibility = DirXMinus | DirZMinus }, // 1 - bottom left is filled
            new TileData { elem = TileElem.CornerConvex, rot = Rotation.CW0, visibility = DirXMinus | DirZPlus  },   // 2 - top left
            new TileData { elem = TileElem.Side, rot = Rotation.CW270, visibility = DirXMinus },                     // 3 - left
            new TileData { elem = TileElem.CornerConvex, rot = Rotation.CW180, visibility = DirXPlus | DirZMinus },  // 4 - bottom right
            new TileData { elem = TileElem.Side, rot = Rotation.CW180, visibility = DirZMinus },                     // 5 - bottom
            new TileData { elem = TileElem.DoubleConcave, rot = Rotation.CW90, visibility = DirAll },                 // 6 - diagonal
            new TileData { elem = TileElem.CornerConcave, rot = Rotation.CW270, visibility = DirXMinus | DirZMinus },// 7 - no top right
            new TileData { elem = TileElem.CornerConvex, rot = Rotation.CW90, visibility = DirXPlus | DirZPlus },    // 8 - top right
            new TileData { elem = TileElem.DoubleConcave, rot = Rotation.CW0, visibility = DirAll },                 // 9 - diagonal
            new TileData { elem = TileElem.Side, visibility = DirZPlus },                                            // 10 - top
            new TileData { elem = TileElem.CornerConcave, rot = Rotation.CW0, visibility = DirXMinus | DirZPlus },   // 11 - no bottom right
            new TileData { elem = TileElem.Side, rot = Rotation.CW90, visibility = DirXPlus },                       // 12 - right
            new TileData { elem = TileElem.CornerConcave, rot = Rotation.CW180, visibility = DirXMinus | DirZMinus },// 13 - no top left
            new TileData { elem = TileElem.CornerConcave, rot = Rotation.CW90, visibility = DirXPlus | DirZPlus },   // 14 - no bottom left
            new TileData { elem = TileElem.Null, visibility = 0 },                                                   // 15 - all, no mesh
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