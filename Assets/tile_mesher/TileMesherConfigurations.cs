using Unity.Collections;
/*
using Elem3D = MeshBuilder.TileTheme3D.Elem;
using TileData3D = MeshBuilder.TileMesher3D.TileData;
using TileConfiguration3D = MeshBuilder.TileMesher3D.TileConfiguration;

using Elem2D = MeshBuilder.TileTheme2D.Elem;
using TileData2D = MeshBuilder.TileMesher2D.TileData;
using TileConfiguration2D = MeshBuilder.TileMesher2D.TileConfiguration;
*/
namespace MeshBuilder
{
    /*
    public class TileMesherConfigurations
    {
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

        static private TileMesherConfigurations instance = null;
        static private TileMesherConfigurations Instance
        {
            get
            {
                if (instance == null) { instance = new TileMesherConfigurations(); }
                return instance;
            }
        }

        private NativeArray<TileConfiguration3D> fullSetConfigurations3D;
        private NativeArray<TileConfiguration2D> fullSetConfigurations2D;

        private TileMesherConfigurations()
        {
            fullSetConfigurations3D = new NativeArray<TileConfiguration3D>(FullSetConfigurations3DArray.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            fullSetConfigurations3D.CopyFrom(FullSetConfigurations3DArray);

            fullSetConfigurations2D = new NativeArray<TileConfiguration2D>(FullSetConfigurations2DArray.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            fullSetConfigurations2D.CopyFrom(FullSetConfigurations2DArray);
        }

        private void DisposeData()
        {
            if (fullSetConfigurations3D.IsCreated)
            {
                fullSetConfigurations3D.Dispose();
            }
            if (fullSetConfigurations2D.IsCreated)
            {
                fullSetConfigurations2D.Dispose();
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

        static public NativeArray<TileConfiguration3D> FullSetConfigurations3D { get { return Instance.fullSetConfigurations3D; } }
        static public NativeArray<TileConfiguration2D> FullSetConfigurations2D { get { return Instance.fullSetConfigurations2D; } }

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

        static public byte ToIndex(bool backLeft, bool backRight, bool frontLeft, bool frontRight)
        {
            byte result = 0;

            if (backLeft)   result |= BackLeft;
            if (backRight)  result |= BackRight;
            if (frontLeft)  result |= FrontLeft;
            if (frontRight) result |= FrontRight;

            return result;
        }

        public const byte BackLeft   = 1 << 0;
        public const byte BackRight  = 1 << 1;
        public const byte FrontLeft  = 1 << 2;
        public const byte FrontRight = 1 << 3;

        #endregion

        #region Declaration Helpers
        static private TileData3D Tile(Elem3D elem, Rotation rot, Direction mirror)
        {
            return new TileData3D { elem = elem, rot = rot, mirror = mirror };
        }

        static private TileConfiguration3D Conf(params TileData3D[] tiles)
        {
            return new TileConfiguration3D(tiles);
        }

        static private TileConfiguration3D NullConf3D()
        {
            return new TileConfiguration3D(null);
        }

        static private TileData2D Tile(Elem2D elem, Rotation rot, Direction mirror)
        {
            return new TileData2D { elem = elem, rot = rot, mirror = mirror };
        }

        static private TileConfiguration2D Conf(params TileData2D[] tiles)
        {
            return new TileConfiguration2D(tiles);
        }

        static private TileConfiguration2D NullConf2D()
        {
            return new TileConfiguration2D(null);
        }

        private const Rotation Rot0 = Rotation.CW0;
        private const Rotation Rot90 = Rotation.CW90;
        private const Rotation Rot180 = Rotation.CW180;
        private const Rotation Rot270 = Rotation.CW270;

        private const Direction MirrorX = Direction.XAxis;
        private const Direction NoMirror = Direction.None;
        #endregion

        #region 3D Full Set Configurations
        // configuration for every possible cases, the bottom and top parts are different
        // (ground and ceiling), so elements can't be mirrored on the Y axis, they can be rotated or
        // mirrored along the X axis
        private static readonly TileConfiguration3D[] FullSetConfigurations3DArray = new TileConfiguration3D[]
        {
            NullConf3D(),
            //1
            Conf(Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case1Bottom, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Bottom, Rot0, NoMirror)),
            //9
            Conf(Tile(Elem3D.Case1Bottom, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot180, NoMirror)),
            //17
            Conf(Tile(Elem3D.Case1Full, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot180, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom1Top, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot180, NoMirror), Tile(Elem3D.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom1Top, Rot90, MirrorX)),
            Conf(Tile(Elem3D.Case1Top, Rot180, NoMirror), Tile(Elem3D.Case1Bottom, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopCenter, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot180, NoMirror), Tile(Elem3D.Case1Bottom, Rot0, NoMirror)),
            //25
            Conf(Tile(Elem3D.Case1Full, Rot180, NoMirror), Tile(Elem3D.Case1Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot180, NoMirror), Tile(Elem3D.Case2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopSide, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot180, NoMirror), Tile(Elem3D.Case2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopSide, Rot0, MirrorX)),
            Conf(Tile(Elem3D.Case1Top, Rot180, NoMirror), Tile(Elem3D.Case3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom1Top, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot270, NoMirror)),
            //33
            Conf(Tile(Elem3D.Case1Top, Rot270, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Full, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom1Top, Rot180, MirrorX)),
            Conf(Tile(Elem3D.Case1Top, Rot270, NoMirror), Tile(Elem3D.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot270, NoMirror), Tile(Elem3D.Case2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case1Full, Rot270, NoMirror), Tile(Elem3D.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopSide, Rot90, MirrorX)),
            Conf(Tile(Elem3D.Case1Top, Rot270, NoMirror), Tile(Elem3D.Case1Bottom, Rot0, NoMirror)),
            //41
            Conf(Tile(Elem3D.Case1Top, Rot270, NoMirror), Tile(Elem3D.Case1Bottom, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom1Top, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopCenter, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot270, NoMirror), Tile(Elem3D.Case2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot270, NoMirror), Tile(Elem3D.Case3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopSide, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom1Top, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case2Top, Rot180, NoMirror)),
            //49
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot180, MirrorX)),
            Conf(Tile(Elem3D.Case2Full, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Top, Rot180, NoMirror), Tile(Elem3D.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3_2Top2Bottom, Rot90, MirrorX)),
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot180, MirrorX), Tile(Elem3D.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom2TopSide, Rot90, MirrorX)),
            Conf(Tile(Elem3D.Case2Top, Rot180, NoMirror), Tile(Elem3D.Case1Bottom, Rot0, NoMirror)),
            //57
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot180, NoMirror), Tile(Elem3D.Case1Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case3_2Top2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom2TopSide, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case2Top, Rot180, NoMirror), Tile(Elem3D.Case2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom2Top, Rot270, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom2Top, Rot270, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom2Top, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror)),
            //65
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Full, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom1Top, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case1Full, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopSide, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot0, NoMirror)),
            //73
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom1Top, Rot0, MirrorX)),
            Conf(Tile(Elem3D.Case3Bottom1TopCenter, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopSide, Rot270, MirrorX)),
            Conf(Tile(Elem3D.Case4Bottom1Top, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case2Top, Rot90, NoMirror)),
            //81
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot90, MirrorX)),
            Conf(Tile(Elem3D.Case2Top, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3_2Top2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case2Full, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom2TopSide, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Top, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot0, NoMirror)),
            //89
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot90, MirrorX), Tile(Elem3D.Case1Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case2Top, Rot90, NoMirror), Tile(Elem3D.Case2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom2Top, Rot180, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3_2Top2Bottom, Rot0, MirrorX)),
            Conf(Tile(Elem3D.Case3Bottom2TopSide, Rot0, MirrorX)),
            Conf(Tile(Elem3D.Case4Bottom2Top, Rot180, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom2Top, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case1Top, Rot270, NoMirror)),
            //97
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case1Top, Rot270, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case1Full, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case2Bottom1Top, Rot180, MirrorX)),
            Conf(Tile(Elem3D.Case1Full, Rot90, NoMirror), Tile(Elem3D.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom1Top, Rot90, NoMirror), Tile(Elem3D.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case1Full, Rot90, NoMirror), Tile(Elem3D.Case1Full, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopSide, Rot180, NoMirror), Tile(Elem3D.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case1Top, Rot270, NoMirror), Tile(Elem3D.Case1Bottom, Rot0, NoMirror)),
            //105
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case1Top, Rot270, NoMirror), Tile(Elem3D.Case1Bottom, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case2Bottom1Top, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot90, NoMirror), Tile(Elem3D.Case3Bottom1TopCenter, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom1Top, Rot0, MirrorX), Tile(Elem3D.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopCenter, Rot90, NoMirror), Tile(Elem3D.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopSide, Rot270, MirrorX), Tile(Elem3D.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom1Top, Rot90, NoMirror), Tile(Elem3D.Case1Top, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Top, Rot180, NoMirror)),
            //113
            Conf(Tile(Elem3D.Case3Top1BottomCenter, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Top1BottomSide, Rot90, MirrorX)),
            Conf(Tile(Elem3D.Case3Top2BottomSide, Rot90, MirrorX)),
            Conf(Tile(Elem3D.Case3Top1BottomSide, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Top2BottomSide, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Full, Rot180, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case3Full, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Top, Rot180, NoMirror), Tile(Elem3D.Case1Bottom, Rot0, NoMirror)),
            //121
            Conf(Tile(Elem3D.Case3Top1BottomCenter, Rot180, NoMirror), Tile(Elem3D.Case1Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot180, NoMirror), Tile(Elem3D.Case4Top2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot180, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot180, NoMirror), Tile(Elem3D.Case4Top2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot180, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot180, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror)),
            //129
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Full, Rot0, NoMirror)),
            //137
            Conf(Tile(Elem3D.Case1Full, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom1Top, Rot270, MirrorX)),
            Conf(Tile(Elem3D.Case3Bottom1TopSide, Rot180, MirrorX)),
            Conf(Tile(Elem3D.Case2Bottom1Top, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopSide, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopCenter, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom1Top, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case1Top, Rot180, NoMirror)),
            //145
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case1Full, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case1Top, Rot180, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case2Bottom1Top, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case1Top, Rot180, NoMirror), Tile(Elem3D.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case2Bottom1Top, Rot90, MirrorX)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case1Top, Rot180, NoMirror), Tile(Elem3D.Case1Bottom, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case1Top, Rot0, NoMirror), Tile(Elem3D.Case3Bottom1TopCenter, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case1Full, Rot0, NoMirror), Tile(Elem3D.Case1Top, Rot180, NoMirror)),
            //153
            Conf(Tile(Elem3D.Case1Full, Rot0, NoMirror), Tile(Elem3D.Case1Full, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom1Top, Rot270, MirrorX), Tile(Elem3D.Case1Top, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopSide, Rot180, MirrorX), Tile(Elem3D.Case1Top, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Bottom1Top, Rot0, NoMirror), Tile(Elem3D.Case1Top, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopSide, Rot90, NoMirror), Tile(Elem3D.Case1Top, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom1TopCenter, Rot0, NoMirror), Tile(Elem3D.Case1Top, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom1Top, Rot0, NoMirror), Tile(Elem3D.Case1Top, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Top, Rot270, NoMirror)),
            //161
            Conf(Tile(Elem3D.Case2Top, Rot270, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3_2Top2Bottom, Rot180, MirrorX)),
            Conf(Tile(Elem3D.Case2Top, Rot270, NoMirror), Tile(Elem3D.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case2Top, Rot270, NoMirror), Tile(Elem3D.Case2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot270, NoMirror), Tile(Elem3D.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom2Top, Rot0, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot270, MirrorX)),
            //169
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot270, MirrorX), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Full, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom2TopSide, Rot180, MirrorX)),
            Conf(Tile(Elem3D.Case3_2Top2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom2Top, Rot0, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom2TopSide, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom2Top, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case3Top, Rot270, NoMirror)),
            //177
            Conf(Tile(Elem3D.Case3Top1BottomSide, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Top1BottomCenter, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Top2BottomSide, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Top, Rot270, NoMirror), Tile(Elem3D.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot270, NoMirror), Tile(Elem3D.Case4Top2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Top1BottomCenter, Rot270, NoMirror), Tile(Elem3D.Case1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot270, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Top1BottomSide, Rot180, MirrorX)),
            //185
            Conf(Tile(Elem3D.Case3Full, Rot270, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Top2BottomSide, Rot180, MirrorX)),
            Conf(Tile(Elem3D.Case3Full, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot270, NoMirror), Tile(Elem3D.Case4Top2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot270, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot270, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case2Top, Rot0, NoMirror)),
            //193
            Conf(Tile(Elem3D.Case2Top, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Top, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case2Top, Rot0, NoMirror), Tile(Elem3D.Case2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot0, MirrorX)),
            Conf(Tile(Elem3D.Case3_2Top2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot0, MirrorX), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom2Top, Rot90, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot0, NoMirror)),
            //201
            Conf(Tile(Elem3D.Case2Top1Bottom, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3_2Top2Bottom, Rot270, MirrorX)),
            Conf(Tile(Elem3D.Case4Bottom2Top, Rot90, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case2Full, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom2TopSide, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Bottom2TopSide, Rot270, MirrorX)),
            Conf(Tile(Elem3D.Case4Bottom2Top, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Top, Rot90, NoMirror)),
            //209
            Conf(Tile(Elem3D.Case3Top1BottomSide, Rot0, MirrorX)),
            Conf(Tile(Elem3D.Case3Top, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot90, NoMirror), Tile(Elem3D.Case4Top2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Top1BottomCenter, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Top2BottomSide, Rot0, MirrorX)),
            Conf(Tile(Elem3D.Case3Top1BottomCenter, Rot90, NoMirror), Tile(Elem3D.Case1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot90, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Top1BottomSide, Rot90, NoMirror)),
            //217
            Conf(Tile(Elem3D.Case3Full, Rot90, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot90, NoMirror), Tile(Elem3D.Case4Top2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot90, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Top2BottomSide, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Full, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot90, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Top, Rot0, NoMirror)),
            //225
            Conf(Tile(Elem3D.Case3Top, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Top1BottomSide, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot0, NoMirror), Tile(Elem3D.Case4Top2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Top1BottomSide, Rot270, MirrorX)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot0, NoMirror), Tile(Elem3D.Case4Top2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Full, Rot0, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot0, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Top1BottomCenter, Rot0, NoMirror)),
            //233
            Conf(Tile(Elem3D.Case3Top1BottomCenter, Rot0, NoMirror), Tile(Elem3D.Case1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case3Top2BottomSide, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot0, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case3Top2BottomSide, Rot270, MirrorX)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot0, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case3Full, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Bottom3Top, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Top, Rot0, NoMirror)),
            //241
            Conf(Tile(Elem3D.Case4Top1Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case4Top1Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case4Top2Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case4Top1Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case4Top2Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case4Top3Bottom, Rot180, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Top3Bottom, Rot180, NoMirror)),
            Conf(Tile(Elem3D.Case4Top1Bottom, Rot0, NoMirror)),
            //249
            Conf(Tile(Elem3D.Case4Top3Bottom, Rot270, NoMirror), Tile(Elem3D.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case4Top2Bottom, Rot0, NoMirror)),
            Conf(Tile(Elem3D.Case4Top3Bottom, Rot270, NoMirror)),
            Conf(Tile(Elem3D.Case4Top2Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case4Top3Bottom, Rot90, NoMirror)),
            Conf(Tile(Elem3D.Case4Top3Bottom, Rot0, NoMirror)),
            
            NullConf3D()
        };
        #endregion

        #region 2D Full Set Configurations
        private static readonly TileConfiguration2D[] FullSetConfigurations2DArray = new TileConfiguration2D[]
        {
            NullConf2D(),

            //2
            Conf(Tile(Elem2D.Case1, Rot270, NoMirror)),
            Conf(Tile(Elem2D.Case1, Rot180, NoMirror)),
            Conf(Tile(Elem2D.Case2, Rot270, NoMirror)),
            Conf(Tile(Elem2D.Case1, Rot0, NoMirror)),
            //6
            Conf(Tile(Elem2D.Case2, Rot270, NoMirror)),
            Conf(Tile(Elem2D.Case1, Rot0, NoMirror), Tile(Elem2D.Case1, Rot180, NoMirror)),
            Conf(Tile(Elem2D.Case3, Rot180, NoMirror)),
            Conf(Tile(Elem2D.Case1, Rot90, NoMirror)),
            //10
            Conf(Tile(Elem2D.Case1, Rot90, NoMirror), Tile(Elem2D.Case1, Rot270, NoMirror)),
            Conf(Tile(Elem2D.Case2, Rot0, NoMirror)),
            Conf(Tile(Elem2D.Case3, Rot270, NoMirror)),
            Conf(Tile(Elem2D.Case2, Rot90, NoMirror)),
            //14
            Conf(Tile(Elem2D.Case3, Rot0, NoMirror)),
            Conf(Tile(Elem2D.Case3, Rot180, NoMirror)),

            Conf(Tile(Elem2D.Case4, Rot0, NoMirror))
        };
        #endregion
    }
    */
}
