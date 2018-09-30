using Unity.Collections;

using TileData = MeshBuilder.TileMesher3D.TileData;
using TileConfiguration = MeshBuilder.TileMesher3D.TileConfiguration;
using Rotation = MeshBuilder.TileMesher3D.Rotation;
using Direction = MeshBuilder.TileMesher3D.Direction;
using Elem = MeshBuilder.TileTheme3D.Elem;

namespace MeshBuilder
{
    public class TileMesherConfigurations
    {
        static private TileMesherConfigurations instance = null;
        static private TileMesherConfigurations Instance
        {
            get
            {
                if (instance == null) { instance = new TileMesherConfigurations(); }
                return instance;
            }
        }

        private NativeArray<TileConfiguration> fullSetConfigurations;

        private TileMesherConfigurations()
        {
            fullSetConfigurations = new NativeArray<TileConfiguration>(FullSetConfigurationsArray.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            fullSetConfigurations.CopyFrom(FullSetConfigurationsArray);
        }

        private void DisposeData()
        {
            if (fullSetConfigurations.IsCreated)
            {
                fullSetConfigurations.Dispose();
            }
        }

        #region Public Interface
        static public void Init()
        {
            var inst = Instance;
        }

        static public void Dispose()
        {
            Instance.DisposeData();
            instance = null;
        }

        static public NativeArray<TileConfiguration> FullSetConfigurations { get { return Instance.fullSetConfigurations; } }

        // NOTE
        // piece order
        // top
        //  8 | 7
        //  6 | 5
        // -------
        // bottom
        //  4 | 3
        //  2 | 1
        // eg. left part of bottom layer 0000 1010, front left corner of both layers 1000 1000
        static public byte ToIndex(bool topFrontLeft, bool topFrontRight, bool topBackLeft, bool topBackRight, 
                                    bool bottomFrontLeft, bool bottomFrontRight, bool bottomBackLeft, bool bottomBackRight)
        {
            byte result = 0;

            if (topFrontLeft)       result |= TopFrontLeft;
            if (topFrontRight)      result |= TopFrontRight;
            if (topBackLeft)        result |= TopBackLeft;
            if (topBackRight)       result |= TopBackRight;
            if (bottomFrontLeft)    result |= BottomFrontLeft;
            if (bottomFrontRight)   result |= BottomFrontRight;
            if (bottomBackLeft)     result |= BottomBackLeft;
            if (bottomBackRight)    result |= BottomBackRight;

            return result;
        }

        public const byte BottomBackRight   = 1 << 0;
        public const byte BottomBackLeft    = 1 << 1;
        public const byte BottomFrontRight  = 1 << 2;
        public const byte BottomFrontLeft   = 1 << 3;
        public const byte TopBackRight      = 1 << 4;
        public const byte TopBackLeft       = 1 << 5;
        public const byte TopFrontRight     = 1 << 6;
        public const byte TopFrontLeft      = 1 << 7;

        #endregion

        #region Declaration Helpers
        static private TileData Tile(Elem elem, Rotation rot, Direction mirror)
        {
            return new TileData { elem = elem, rot = rot, mirror = mirror };
        }

        static private TileConfiguration Conf(params TileData[] tiles)
        {
            return new TileConfiguration(tiles);
        }

        private const Rotation Rot0 = Rotation.CW0;
        private const Rotation Rot90 = Rotation.CW90;
        private const Rotation Rot180 = Rotation.CW180;
        private const Rotation Rot270 = Rotation.CW270;

        private const Direction MirrorX = Direction.XAxis;
        private const Direction NoMirror = Direction.None;
        #endregion

        #region Full Set Configurations
        // configuration for every possible cases, the bottom and top parts are different
        // (ground and ceiling), so elements can't be mirrored on the Y axis, they can be rotated or
        // mirrored along the X axis
        private static readonly TileConfiguration[] FullSetConfigurationsArray = new TileConfiguration[]
        {
            Conf(null),
            //1
            Conf(Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case1Bottom, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Bottom, Rot0, NoMirror)),
            //9
            Conf(Tile(Elem.Case1Bottom, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot180, NoMirror)),
            //17
            Conf(Tile(Elem.Case1Full, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot180, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case2Bottom1Top, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot180, NoMirror), Tile(Elem.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case2Bottom1Top, Rot90, MirrorX)),
            Conf(Tile(Elem.Case1Top, Rot180, NoMirror), Tile(Elem.Case1Bottom, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopCenter, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot180, NoMirror), Tile(Elem.Case1Bottom, Rot0, NoMirror)),
            //25
            Conf(Tile(Elem.Case1Full, Rot180, NoMirror), Tile(Elem.Case1Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot180, NoMirror), Tile(Elem.Case2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopSide, Rot270, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot180, NoMirror), Tile(Elem.Case2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopSide, Rot0, MirrorX)),
            Conf(Tile(Elem.Case1Top, Rot180, NoMirror), Tile(Elem.Case3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom1Top, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot270, NoMirror)),
            //33
            Conf(Tile(Elem.Case1Top, Rot270, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Full, Rot270, NoMirror)),
            Conf(Tile(Elem.Case2Bottom1Top, Rot180, MirrorX)),
            Conf(Tile(Elem.Case1Top, Rot270, NoMirror), Tile(Elem.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot270, NoMirror), Tile(Elem.Case2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case1Full, Rot270, NoMirror), Tile(Elem.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopSide, Rot90, MirrorX)),
            Conf(Tile(Elem.Case1Top, Rot270, NoMirror), Tile(Elem.Case1Bottom, Rot0, NoMirror)),
            //41
            Conf(Tile(Elem.Case1Top, Rot270, NoMirror), Tile(Elem.Case1Bottom, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Bottom1Top, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopCenter, Rot270, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot270, NoMirror), Tile(Elem.Case2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot270, NoMirror), Tile(Elem.Case3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopSide, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom1Top, Rot270, NoMirror)),
            Conf(Tile(Elem.Case2Top, Rot180, NoMirror)),
            //49
            Conf(Tile(Elem.Case2Top1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Top1Bottom, Rot180, MirrorX)),
            Conf(Tile(Elem.Case2Full, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Top, Rot180, NoMirror), Tile(Elem.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3_2Top2Bottom, Rot90, MirrorX)),
            Conf(Tile(Elem.Case2Top1Bottom, Rot180, MirrorX), Tile(Elem.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Bottom2TopSide, Rot90, MirrorX)),
            Conf(Tile(Elem.Case2Top, Rot180, NoMirror), Tile(Elem.Case1Bottom, Rot0, NoMirror)),
            //57
            Conf(Tile(Elem.Case2Top1Bottom, Rot180, NoMirror), Tile(Elem.Case1Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case3_2Top2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Bottom2TopSide, Rot270, NoMirror)),
            Conf(Tile(Elem.Case2Top, Rot180, NoMirror), Tile(Elem.Case2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom2Top, Rot270, NoMirror), Tile(Elem.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case4Bottom2Top, Rot270, NoMirror), Tile(Elem.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom2Top, Rot270, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror)),
            //65
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Full, Rot90, NoMirror)),
            Conf(Tile(Elem.Case2Bottom1Top, Rot90, NoMirror)),
            Conf(Tile(Elem.Case1Full, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopSide, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot0, NoMirror)),
            //73
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case2Bottom1Top, Rot0, MirrorX)),
            Conf(Tile(Elem.Case3Bottom1TopCenter, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopSide, Rot270, MirrorX)),
            Conf(Tile(Elem.Case4Bottom1Top, Rot90, NoMirror)),
            Conf(Tile(Elem.Case2Top, Rot90, NoMirror)),
            //81
            Conf(Tile(Elem.Case2Top1Bottom, Rot90, MirrorX)),
            Conf(Tile(Elem.Case2Top, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3_2Top2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Top1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case2Full, Rot90, NoMirror)),
            Conf(Tile(Elem.Case2Top1Bottom, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Bottom2TopSide, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Top, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot0, NoMirror)),
            //89
            Conf(Tile(Elem.Case2Top1Bottom, Rot90, MirrorX), Tile(Elem.Case1Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case2Top, Rot90, NoMirror), Tile(Elem.Case2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case4Bottom2Top, Rot180, NoMirror), Tile(Elem.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3_2Top2Bottom, Rot0, MirrorX)),
            Conf(Tile(Elem.Case3Bottom2TopSide, Rot0, MirrorX)),
            Conf(Tile(Elem.Case4Bottom2Top, Rot180, NoMirror), Tile(Elem.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom2Top, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case1Top, Rot270, NoMirror)),
            //97
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case1Top, Rot270, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case1Full, Rot270, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case2Bottom1Top, Rot180, MirrorX)),
            Conf(Tile(Elem.Case1Full, Rot90, NoMirror), Tile(Elem.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem.Case2Bottom1Top, Rot90, NoMirror), Tile(Elem.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem.Case1Full, Rot90, NoMirror), Tile(Elem.Case1Full, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopSide, Rot180, NoMirror), Tile(Elem.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case1Top, Rot270, NoMirror), Tile(Elem.Case1Bottom, Rot0, NoMirror)),
            //105
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case1Top, Rot270, NoMirror), Tile(Elem.Case1Bottom, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case2Bottom1Top, Rot270, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot90, NoMirror), Tile(Elem.Case3Bottom1TopCenter, Rot270, NoMirror)),
            Conf(Tile(Elem.Case2Bottom1Top, Rot0, MirrorX), Tile(Elem.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopCenter, Rot90, NoMirror), Tile(Elem.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopSide, Rot270, MirrorX), Tile(Elem.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem.Case4Bottom1Top, Rot90, NoMirror), Tile(Elem.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Top, Rot180, NoMirror)),
            //113
            Conf(Tile(Elem.Case3Top1BottomCenter, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Top1BottomSide, Rot90, MirrorX)),
            Conf(Tile(Elem.Case3Top2BottomSide, Rot90, MirrorX)),
            Conf(Tile(Elem.Case3Top1BottomSide, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Top2BottomSide, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Full, Rot180, NoMirror), Tile(Elem.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case3Full, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Top, Rot180, NoMirror), Tile(Elem.Case1Bottom, Rot0, NoMirror)),
            //121
            Conf(Tile(Elem.Case3Top1BottomCenter, Rot180, NoMirror), Tile(Elem.Case1Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot180, NoMirror), Tile(Elem.Case4Top2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot180, NoMirror), Tile(Elem.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot180, NoMirror), Tile(Elem.Case4Top2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot180, NoMirror), Tile(Elem.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot180, NoMirror), Tile(Elem.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror)),
            //129
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Full, Rot0, NoMirror)),
            //137
            Conf(Tile(Elem.Case1Full, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Bottom1Top, Rot270, MirrorX)),
            Conf(Tile(Elem.Case3Bottom1TopSide, Rot180, MirrorX)),
            Conf(Tile(Elem.Case2Bottom1Top, Rot0, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopSide, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopCenter, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom1Top, Rot0, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case1Top, Rot180, NoMirror)),
            //145
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case1Full, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case1Top, Rot180, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case2Bottom1Top, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case1Top, Rot180, NoMirror), Tile(Elem.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case2Bottom1Top, Rot90, MirrorX)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case1Top, Rot180, NoMirror), Tile(Elem.Case1Bottom, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case1Top, Rot0, NoMirror), Tile(Elem.Case3Bottom1TopCenter, Rot180, NoMirror)),
            Conf(Tile(Elem.Case1Full, Rot0, NoMirror), Tile(Elem.Case1Top, Rot180, NoMirror)),
            //153
            Conf(Tile(Elem.Case1Full, Rot0, NoMirror), Tile(Elem.Case1Full, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Bottom1Top, Rot270, MirrorX), Tile(Elem.Case1Top, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopSide, Rot180, MirrorX), Tile(Elem.Case1Top, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Bottom1Top, Rot0, NoMirror), Tile(Elem.Case1Top, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopSide, Rot90, NoMirror), Tile(Elem.Case1Top, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Bottom1TopCenter, Rot0, NoMirror), Tile(Elem.Case1Top, Rot180, NoMirror)),
            Conf(Tile(Elem.Case4Bottom1Top, Rot0, NoMirror), Tile(Elem.Case1Top, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Top, Rot270, NoMirror)),
            //161
            Conf(Tile(Elem.Case2Top, Rot270, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Top1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3_2Top2Bottom, Rot180, MirrorX)),
            Conf(Tile(Elem.Case2Top, Rot270, NoMirror), Tile(Elem.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case2Top, Rot270, NoMirror), Tile(Elem.Case2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case2Top1Bottom, Rot270, NoMirror), Tile(Elem.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case4Bottom2Top, Rot0, NoMirror), Tile(Elem.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Top1Bottom, Rot270, MirrorX)),
            //169
            Conf(Tile(Elem.Case2Top1Bottom, Rot270, MirrorX), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Full, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Bottom2TopSide, Rot180, MirrorX)),
            Conf(Tile(Elem.Case3_2Top2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom2Top, Rot0, NoMirror), Tile(Elem.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Bottom2TopSide, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom2Top, Rot0, NoMirror)),
            Conf(Tile(Elem.Case3Top, Rot270, NoMirror)),
            //177
            Conf(Tile(Elem.Case3Top1BottomSide, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Top1BottomCenter, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Top2BottomSide, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Top, Rot270, NoMirror), Tile(Elem.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot270, NoMirror), Tile(Elem.Case4Top2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Top1BottomCenter, Rot270, NoMirror), Tile(Elem.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot270, NoMirror), Tile(Elem.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Top1BottomSide, Rot180, MirrorX)),
            //185
            Conf(Tile(Elem.Case3Full, Rot270, NoMirror), Tile(Elem.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Top2BottomSide, Rot180, MirrorX)),
            Conf(Tile(Elem.Case3Full, Rot270, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot270, NoMirror), Tile(Elem.Case4Top2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot270, NoMirror), Tile(Elem.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot270, NoMirror), Tile(Elem.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot270, NoMirror)),
            Conf(Tile(Elem.Case2Top, Rot0, NoMirror)),
            //193
            Conf(Tile(Elem.Case2Top, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Top, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case2Top, Rot0, NoMirror), Tile(Elem.Case2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Top1Bottom, Rot0, MirrorX)),
            Conf(Tile(Elem.Case3_2Top2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case2Top1Bottom, Rot0, MirrorX), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case4Bottom2Top, Rot90, NoMirror), Tile(Elem.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case2Top1Bottom, Rot0, NoMirror)),
            //201
            Conf(Tile(Elem.Case2Top1Bottom, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3_2Top2Bottom, Rot270, MirrorX)),
            Conf(Tile(Elem.Case4Bottom2Top, Rot90, NoMirror), Tile(Elem.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case2Full, Rot0, NoMirror)),
            Conf(Tile(Elem.Case3Bottom2TopSide, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Bottom2TopSide, Rot270, MirrorX)),
            Conf(Tile(Elem.Case4Bottom2Top, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Top, Rot90, NoMirror)),
            //209
            Conf(Tile(Elem.Case3Top1BottomSide, Rot0, MirrorX)),
            Conf(Tile(Elem.Case3Top, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot90, NoMirror), Tile(Elem.Case4Top2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Top1BottomCenter, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Top2BottomSide, Rot0, MirrorX)),
            Conf(Tile(Elem.Case3Top1BottomCenter, Rot90, NoMirror), Tile(Elem.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot90, NoMirror), Tile(Elem.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Top1BottomSide, Rot90, NoMirror)),
            //217
            Conf(Tile(Elem.Case3Full, Rot90, NoMirror), Tile(Elem.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot90, NoMirror), Tile(Elem.Case4Top2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot90, NoMirror), Tile(Elem.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Top2BottomSide, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Full, Rot90, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot90, NoMirror), Tile(Elem.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Top, Rot0, NoMirror)),
            //225
            Conf(Tile(Elem.Case3Top, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Top1BottomSide, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot0, NoMirror), Tile(Elem.Case4Top2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Top1BottomSide, Rot270, MirrorX)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot0, NoMirror), Tile(Elem.Case4Top2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Full, Rot0, NoMirror), Tile(Elem.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot0, NoMirror), Tile(Elem.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Top1BottomCenter, Rot0, NoMirror)),
            //233
            Conf(Tile(Elem.Case3Top1BottomCenter, Rot0, NoMirror), Tile(Elem.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case3Top2BottomSide, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot0, NoMirror), Tile(Elem.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case3Top2BottomSide, Rot270, MirrorX)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot0, NoMirror), Tile(Elem.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case3Full, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Bottom3Top, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Top, Rot0, NoMirror)),
            //241
            Conf(Tile(Elem.Case4Top1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case4Top1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case4Top2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case4Top1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case4Top2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case4Top3Bottom, Rot180, NoMirror), Tile(Elem.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem.Case4Top1Bottom, Rot0, NoMirror)),
            //249
            Conf(Tile(Elem.Case4Top3Bottom, Rot270, NoMirror), Tile(Elem.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case4Top2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem.Case4Top2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem.Case4Top3Bottom, Rot0, NoMirror)),
            
            Conf(null)
        };
        #endregion
    }
}
