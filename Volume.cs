using System;

using Unity.Collections;
using UnityEngine;

namespace MeshBuilder
{
    public class Volume<T> : IDisposable where T : struct
    {
        private const int MaxSize = 255;

        private bool disposed = false;

        // y is up
        public int XLength { get; private set; }
        public int YLength { get; private set; }
        public int ZLength { get; private set; }
        private NativeArray<T> data;

        private int layerLength;

        public Volume(Extents extents)
            : this(extents.X, extents.Y, extents.Z)
        {

        }

        public Volume(int xSize, int ySize, int zSize)
        {
            XLength = Mathf.Clamp(xSize, 1, MaxSize);
            YLength = Mathf.Clamp(ySize, 1, MaxSize);
            ZLength = Mathf.Clamp(zSize, 1, MaxSize);

            if (xSize != XLength) { Debug.LogWarning("xsize has been clamped (" + xSize + " -> " + XLength + " )"); }
            if (ySize != YLength) { Debug.LogWarning("ysize has been clamped (" + ySize + " -> " + YLength + " )"); }
            if (zSize != ZLength) { Debug.LogWarning("zsize has been clamped (" + zSize + " -> " + ZLength + " )"); }

            layerLength = XLength * ZLength;

            int count = layerLength * YLength;
            data = new NativeArray<T>(count, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        public void Dispose()
        {
            if (!disposed)
            {
                data.Dispose();
            }
            disposed = true;
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
            if (x < 0 || x >= XLength || y < 0 || y >= YLength || z < 0 ||z >= ZLength)
            {
                Debug.LogErrorFormat("index coordinates out of bounds ({0}, {1}, {2})", x, y, z);
            }

            return y * layerLength + z * XLength + x;
        }

        public bool DoExtentsMatch(Extents e)
        {
            return XLength == e.X && YLength == e.Y && ZLength == e.Z;
        }

        public NativeArray<T> Data { get { return data; } }
        public bool IsDisposed { get { return disposed; } }
        public int Length { get { return data.Length; } }
    }
}
