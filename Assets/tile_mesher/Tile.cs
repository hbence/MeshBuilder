﻿
namespace MeshBuilder
{
    public sealed class Tile
    {
        // 2D
        // forward/backward - Z axis
        // left/right - X axis
        public const int LeftForward    = 1 << 3;
        public const int RightForward   = 1 << 2;
        public const int LeftBackward   = 1 << 1;
        public const int RightBackward  = 1 << 0;

        // 3D
        // top/bottom - Y axis
        public const int TopLeftForward     = 1 << 7;
        public const int TopRightForward    = 1 << 6;
        public const int TopLeftBackward    = 1 << 5;
        public const int TopRightBackward   = 1 << 4;

        public const int BottomLeftForward   = 1 << 3;
        public const int BottomRightForward  = 1 << 2;
        public const int BottomLeftBackward  = 1 << 1;
        public const int BottomRightBackward = 1 << 0;

        public enum Piece : byte
        {
            Piece_0000 = 0,

            Piece_1000 = LeftForward,
            Piece_0100 = RightForward,
            Piece_0010 = LeftBackward,
            Piece_0001 = RightBackward,

            Piece_1100 = LeftForward | RightForward,
            Piece_1010 = LeftForward | LeftBackward,
            Piece_0101 = RightForward | RightBackward,
            Piece_0011 = LeftBackward | RightBackward,

            Piece_0111 = RightForward | LeftBackward | RightBackward,
            Piece_1011 = LeftForward | LeftBackward | RightBackward,
            Piece_1101 = LeftForward | RightForward | RightBackward,
            Piece_1110 = LeftForward | RightForward | LeftBackward,

            Piece_1001 = LeftForward | RightBackward,
            Piece_0110 = RightForward | LeftBackward,

            Piece_1111 = LeftForward | RightForward | LeftBackward | RightBackward
        }

        static public Piece ToPiece(bool leftForward, bool rightForward, bool leftBackward, bool rightBackward)
        {
            return (Piece)ToConfig(leftForward, rightForward, leftBackward, rightBackward);
        }

        static public int ToConfig(bool leftForward, bool rightForward, bool leftBackward, bool rightBackward)
        {
            int res = leftForward ? LeftForward : 0;
            res = rightForward ? RightForward : 0;
            res = leftBackward ? LeftBackward : 0;
            res = rightBackward ? RightBackward : 0;
            return res;
        }

        static public int ToConfig(Piece piece)
        {
            return (int)piece;
        }

        static public int ToConfig(Piece top, Piece bottom)
        {
            return (((int)top) << 4) | (int)bottom;
        }

        /// <summary>
        /// The data used to identify the piece. The index means the index of a theme in 
        /// a palette of themes, while the variant means the variant of a certain piece.
        /// </summary>
        public struct Data
        {
            public byte themeIndex;
            public byte variant;
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
    }
}