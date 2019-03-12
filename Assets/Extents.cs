using Unity.Mathematics;

namespace MeshBuilder
{
    public struct Extents
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Z { get; private set; }
        public int XZ { get; private set; }

        public Extents(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
            XZ = x * z;
        }

        public void Set(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
            XZ = x * z;
        }

        public int3 ToCoordAt(int index)
        {
            return CoordFromIndex(index, XZ, X);
        }

        public int ToIndexAt(int x, int y, int z)
        {
            return y * XZ + z * X + x;
        }

        public int ToIndexAt(int3 c)
        {
            return c.y * XZ + c.z * X + c.x;
        }

        public bool IsInBounds(int x, int y, int z)
        {
            return x >= 0 && y >= 0 && z >= 0 && x < X && y < Y && z < Z;
        }

        public bool IsInBounds(int3 c)
        {
            return IsInBounds(c.x, c.y, c.z);
        }

        // index - array index
        // xzLength - size of xz layer (xSize * zSize)
        // zLength - length along z axis
        static public int3 CoordFromIndex(int index, int xzLength, int xLength)
        {
            int rem = index % xzLength;
            return new int3 { x = rem % xLength, y = index / xzLength, z = rem / xLength };
        }

        static public int3 CoordFromIndex(int index, Extents ext)
        {
            int rem = index % ext.XZ;
            return new int3 { x = rem % ext.X, y = index / ext.XZ, z = rem / ext.X };
        }

        static public int IndexFromCoord(int3 c, int xzLength, int xLength)
        {
            return c.y * xzLength + c.z * xLength + c.x;
        }

        static public int IndexFromCoord(int3 c, Extents ext)
        {
            return c.y * ext.XZ + c.z * ext.X + c.x;
        }

        static public int IndexFromCoord(int x, int y, int z, int xzLength, int xLength)
        {
            return y * xzLength + z * xLength + x;
        }

        static public int IndexFromCoord(int x, int y, int z, Extents ext)
        {
            return y * ext.XZ + z * ext.X + x;
        }
    }
}

