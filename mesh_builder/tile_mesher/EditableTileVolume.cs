using UnityEngine;

using TileData = MeshBuilder.Tile.Data;

namespace MeshBuilder
{
    public class EditableTileVolume : MonoBehaviour
    {
        private static readonly TileData NullTile = new TileData { themeIndex = 0 };

        [SerializeField]
        private ResizeInfo dataResize = default;

        [SerializeField]
        private Vector3Int dataOffset = new Vector3Int(0, 0, 0);
        private bool HasOffset { get { return dataOffset.x != 0 || dataOffset.y != 0 || dataOffset.z != 0; } }

        [SerializeField]
        private BrushInfo brush;

        [SerializeField]
        private bool lockYLevel = false;
        public bool LockYLevel { get { return lockYLevel; } }

        [SerializeField]
        private int lockedLevel = 0;
        public int LockedLevel { get { return lockedLevel; } }

        [SerializeField, HideInInspector]
        private TileDataArray data;
        public TileDataArray Data { get { return data; } }
        public int DataXSize { get { return data.Extents.X; } }
        public int DataYSize { get { return data.Extents.Y; } }
        public int DataZSize { get { return data.Extents.Z; } }

        public void Init()
        {
            ApplyDataResize();
        }

        public void ApplyDataResize()
        {
            dataResize.ClampSize();

            if (Data != null)
            {
                if (!dataResize.DoesSizeMatch(data.Extents))
                {
                    var newData = new TileDataArray(dataResize.CreateExtents());
                    CopyData(Data, newData);
                    data = newData;
                }
            }
            else
            {
                var e = dataResize.CreateExtents();
                data = new TileDataArray(e);
            }
        }

        public void ApplyDataOffset()
        {
            if (HasOffset)
            {
                MoveData(dataOffset.x, dataOffset.y, dataOffset.z);
                dataOffset.Set(0, 0, 0);
            }
        }

        private void CopyData(TileDataArray src, TileDataArray dst)
        {
            int minX = Mathf.Min(src.Extents.X, dst.Extents.X);
            int minY = Mathf.Min(src.Extents.Y, dst.Extents.Y);
            int minZ = Mathf.Min(src.Extents.Z, dst.Extents.Z);
            for (int y = 0; y < minY; ++y)
            {
                for (int z = 0; z < minZ; ++z)
                {
                    for (int x = 0; x < minX; ++x)
                    {
                        dst[x, y, z] = src[x, y, z];
                    }
                }
            }
        }

        private void MoveData(int offsetX, int offsetY, int offsetZ)
        {
            int startX = offsetX <= 0 ? 0 : data.Extents.X - 1;
            int startY = offsetY <= 0 ? 0 : data.Extents.Y - 1;
            int startZ = offsetZ <= 0 ? 0 : data.Extents.Z - 1;

            int endX = offsetX > 0 ? -1 : data.Extents.X;
            int endY = offsetY > 0 ? -1 : data.Extents.Y;
            int endZ = offsetZ > 0 ? -1 : data.Extents.Z;

            int stepX = offsetX <= 0 ? 1 : -1;
            int stepY = offsetY <= 0 ? 1 : -1;
            int stepZ = offsetZ <= 0 ? 1 : -1;

            for (int y = startY; y != endY; y += stepY)
            {
                for (int z = startZ; z != endZ; z += stepZ)
                {
                    for (int x = startX; x != endX; x += stepX)
                    {
                        int ox = x - offsetX;
                        int oy = y - offsetY;
                        int oz = z - offsetZ;
                        if (Data.IsInBounds(ox, oy, oz))
                        {
                            Data[x, y, z] = Data[ox, oy, oz];
                        }
                        else
                        {
                            Data[x, y, z] = NullTile;
                        }
                    }
                }
            }
        }

        [System.Serializable]
        public class TileDataArray
        {
            [SerializeField]
            private Extents extents;
            public Extents Extents { get { return extents; } }
            [SerializeField]
            private TileData[] data;
            public TileData[] Data { get { return data; } }

            public TileDataArray(int x, int y, int z)
            {
                extents = new Extents(x, y, z);
                data = new TileData[x * y * z];
            }

            public TileDataArray(Extents e)
            {
                extents = e;
                data = new TileData[e.X * e.Y * e.Z];
            }

            public bool IsInBounds(int x, int y, int z)
            {
                return extents.IsInBounds(x, y, z);
            }

            public TileData this[int x, int y, int z]
            {
                get
                {
                    int i = extents.ToIndexAt(x, y, z);
                    return data[i];
                }
                set
                {
                    int i = extents.ToIndexAt(x, y, z);
                    data[i] = value;
                }
            }

            public bool IsNull { get { return data == null; } }
        }
        
        [System.Serializable]
        private class ResizeInfo
        {
            [SerializeField]
            private Vector3Int size = new Vector3Int(8, 1, 8);
            public Vector3Int Size { get { return size; } }
            public int SizeX { get { return size.x; } }
            public int SizeY { get { return size.y; } }
            public int SizeZ { get { return size.z; } }

            public void ClampSize()
            {
                size.x = Mathf.Clamp(size.x, 1, 32);
                size.y = Mathf.Clamp(size.y, 1, 32);
                size.z = Mathf.Clamp(size.z, 1, 32);
            }

            public void UpdateSize(int x, int y, int z) { size.Set(x, y, z); ClampSize(); }

            public bool DoesSizeMatch(int x, int y, int z) { return size.x == x && size.y == y && size.z == z; }
            public bool DoesSizeMatch(Extents e) { return DoesSizeMatch(e.X, e.Y, e.Z); }
 
            public Extents CreateExtents() { return new Extents(SizeX, SizeY, SizeZ); }
        }

        [System.Serializable]
        public class BrushInfo
        {
            public enum BrushMode
            {
                Add,
                Remove,
                Replace
            }

            [SerializeField]
            private BrushMode mode = BrushMode.Add;
            public BrushMode Mode { get { return mode; } }

            [SerializeField]
            private int brushSize = 0;
            public int BrushSize { get { return brushSize; } }

            [SerializeField]
            private int selectedIndex = 0;
            public int SelectedIndex { get { return selectedIndex; } }

            [SerializeField]
            private bool removeOnlySelected = false;
            public bool RemoveOnlySelected { get { return removeOnlySelected; } }

            [SerializeField]
            private int replaceIndex = 0;
            public int ReplaceIndex { get { return replaceIndex; } }

            [SerializeField]
            private int[] skipIndices = null;
            public int[] SkipIndices { get { return skipIndices; } }
        }
    }
}
