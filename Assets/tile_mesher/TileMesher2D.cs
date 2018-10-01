using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using System.Runtime.InteropServices;

using static MeshBuilder.Extents;

using DataVolume = MeshBuilder.Volume<byte>; // type values
using TileVolume = MeshBuilder.Volume<MeshBuilder.TileMesher3D.TileVariant>; // configuration indices
using TileElem = MeshBuilder.TileTheme2D.Elem;
using Config = MeshBuilder.TileMesherConfigurations;
using Rotation = MeshBuilder.TileMesherConfigurations.Rotation;
using Direction = MeshBuilder.TileMesherConfigurations.Direction;

namespace MeshBuilder
{
    public class TileMesher2D : TileMesherBase<TileMesher2D.TileVariant>
    {
        private const string DefaultName = "tile_mesh_2d";
        static private readonly Settings DefaultSettings = new Settings { };

     // INITIAL DATA
        private TileTheme2D theme;
        private DataVolume data;
        private Settings settings;

        // in the data volume we're generating the mesh
        // for this value
        private byte fillValue;

        private int yLevel;

        private Extents dataExtents;
        private Extents tileExtents;

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

        public void Init(DataVolume dataVolume, int yLevel, byte fillValue, TileTheme2D theme)
        {
            Init(dataVolume, yLevel, fillValue, theme, DefaultSettings);
        }

        public void Init(DataVolume dataVolume, int yLevel, byte fillValue, TileTheme2D theme, Settings settings)
        {
            Dispose();

            this.data = dataVolume;
            this.theme = theme;
            this.yLevel = yLevel;
            this.fillValue = fillValue;
            this.settings = settings;

            this.theme.Init();

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
        }

        override protected void AfterGenerationJobsComplete()
        {

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

            // TODO: NOT IMPLEMENTED
            /// <summary>
            /// When generating the mesh, should the algorithm consider the chunk boundaries empty?
            /// If the chunk represents ground for example, then the bottom and side boundaries should
            /// be NOT empty so those sides don't get generated. If the chunk is a flying island which
            /// needs all of its sides rendered, then the boundaries should be considered empty.
            /// </summary>
            public byte emptyBoundaries = (int)Direction.All;
        }

        public enum TileType : byte
        {
            Normal,
            Custom,
            Overlapped
        }

        public struct TileVariant
        {
            public TileType type;
            public byte config;
            public byte variation;
        }
    }
}
