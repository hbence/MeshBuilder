using System;

using Unity.Collections;
using UnityEngine;

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

    public T At(int x, int y, int z) { return data[ y * layerLength + z * XLength + x]; }
    public void SetAt(int x, int y, int z, T value)
    {
        int index = y * layerLength + z * XLength + x;
        data[index] = value;
    }
    public NativeArray<T> Data { get { return data; } }
}
