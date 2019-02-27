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

