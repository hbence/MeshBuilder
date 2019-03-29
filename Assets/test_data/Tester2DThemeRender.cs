using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using MeshBuilder;

public class Tester2DThemeRender : MonoBehaviour
{
    private TileDataAsset data;
    public TileThemeDrawer drawer;

	void Awake()
    {
        data = ScriptableObject.CreateInstance<TileDataAsset>();

        var volume = new Volume<Tile.Data>(10, 2, 10);
        FillVolume(volume);

        data.SetData(volume);
        data.InitCache();

        drawer.TileData = data;

        volume.Dispose();
    }

    private void OnDestroy()
    {
        if (data != null) data.Dispose();
        if (drawer != null)
        {
            drawer.Dispose();
        }
    }

    private void FillVolume(Volume<Tile.Data> volume)
    {
        for (int z = 1; z < volume.ZLength - 1; ++z)
        {
            for (int x = 1; x < volume.XLength - 1; ++x)
            {
                Set(volume, x, z, 1);
            }
        }

        Set(volume, 2, 3, 2);
        Set(volume, 2, 4, 2);
        Set(volume, 2, 5, 2);
        Set(volume, 2, 6, 2);
        Set(volume, 3, 5, 2);
        Set(volume, 3, 6, 2);
        Set(volume, 4, 6, 2);
        Set(volume, 5, 6, 2);
        Set(volume, 3, 7, 2);
        Set(volume, 3, 8, 2);

        Set(volume, 5, 2, 3);
        Set(volume, 5, 3, 3);
        Set(volume, 6, 3, 3);
        Set(volume, 6, 4, 3);
        Set(volume, 6, 5, 3);

        Set(volume, 7, 7, 3);

    }

    private void Set(Volume<Tile.Data> volume, int x, int z, byte theme)
    {
        volume[x, 0, z] = new Tile.Data { themeIndex = theme };
    }
}
