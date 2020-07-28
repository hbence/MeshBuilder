using System;

using Unity.Collections;
using UnityEngine;

using static MeshBuilder.Utils;

namespace MeshBuilder
{
    public class Volume<T> : IDisposable where T : struct
    {
        private const int MaxSize = 255;

        // y is up
        public Extents Extents { get; private set; }
        public int XLength { get => Extents.X; }
        public int YLength { get => Extents.Y; }
        public int ZLength { get => Extents.Z; }
        private NativeArray<T> data;

        public Volume(Extents extents)
            : this(extents.X, extents.Y, extents.Z)
        {

        }
        
        public Volume(int xSize, int ySize, int zSize)
        {
            int x = Mathf.Clamp(xSize, 1, MaxSize);
            int y = Mathf.Clamp(ySize, 1, MaxSize);
            int z = Mathf.Clamp(zSize, 1, MaxSize);

            if (xSize != x) { Debug.LogError("xsize has been clamped (" + xSize + " -> " + x + " )"); }
            if (ySize != y) { Debug.LogError("ysize has been clamped (" + ySize + " -> " + y + " )"); }
            if (zSize != z) { Debug.LogError("zsize has been clamped (" + zSize + " -> " + z + " )"); }

            Extents = new Extents(x, y, z);

            int count = x * y * z;
            data = new NativeArray<T>(count, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        public Volume(int xSize, int ySize, int zSize, T[] arrayData)
        {
            if (xSize * ySize * zSize != arrayData.Length)
            {
                Debug.LogError("volume size doesn't fit arrayData length!");
            }

            Extents = new Extents(xSize, ySize, zSize);
            data = new NativeArray<T>(arrayData, Allocator.Persistent);
        }

        public void Dispose()
        {
            SafeDispose(ref data);
            Extents.Set(0, 0, 0);
        }

        public T this[int i]
        {
            get { return data[i]; }
            set { data[i] = value; }
        }

        public T this[int x, int y, int z]
        {
            get { return data[IndexAt(x, y, z)]; }
            set { data[IndexAt(x, y, z)] = value; }
        }

        public T this[Vector3Int c]
        {
            get { return data[IndexAt(c.x, c.y, c.z)]; }
            set { data[IndexAt(c.x, c.y, c.z)] = value; }
        }

        private int IndexAt(int x, int y, int z)
        {
            // generally the meshers use the Data directly, so this check doesn't really matter but helps in testing,
            // an out of bounds coordinate can still be inside the data length interval, which would be a silent error,
            // still checking for boundaries for every lookup doesn't feel right
            if (!Extents.IsInBounds(x, y, z))
            {
                Debug.LogErrorFormat("index coordinates out of bounds ({0}, {1}, {2})", x, y, z);
            }

            return Extents.ToIndexAt(x, y, z);
        }

        public bool DoExtentsMatch(Extents e)
        {
            return XLength == e.X && YLength == e.Y && ZLength == e.Z;
        }

        public NativeArray<T> Data { get { return data; } }
        public bool IsDisposed { get { return !data.IsCreated; } }
        public int Length { get { return data.Length; } }
    }
}
