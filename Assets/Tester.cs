using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using MeshBuilder;
using DataVolume = MeshBuilder.Volume<byte>;

public class Tester : MonoBehaviour
{
    private const int Filled = 1;

    private DataVolume data;

    public MeshFilter groundMeshFilter;
    private GridMesher groundMesher;

    public VolumeTheme theme;
    public MeshFilter tileMeshFilter;
    private TileMesher tileMesher;

	private void Awake ()
    {
        data = CreateTestData();

        groundMesher = new GridMesher();
        groundMesher.Init(data, 1f, 1);

        groundMeshFilter.sharedMesh = groundMesher.Mesh;

        tileMesher = new TileMesher();
        tileMesher.Init(data, theme, 1f);

        tileMeshFilter.sharedMesh = tileMesher.Mesh;
	}

    private void Start()
    {
        groundMesher.StartGeneration();
        tileMesher.StartGeneration();
    }

    private void LateUpdate()
    {
        if (groundMesher.IsGenerating)
        {
            groundMesher.EndGeneration();
        }

        if (tileMesher.IsGenerating)
        {
            tileMesher.EndGeneration();
        }
    }

    private void OnDestroy()
    {
        groundMesher.Dispose();
        tileMesher.Dispose();
        data.Dispose();
    }

    private DataVolume CreateTestData()
    {
        int x = 16;
        int y = 16;
        int z = 16;
        DataVolume res = new DataVolume(x, y, z);

        FillTestData(res);

        return res;
    }

    private void FillTestData(DataVolume volume)
    {
        int x = 0;
        int z = 0;

        for (x = 0; x < 16; ++x)
        {
            for (z = 0; z < 16; ++z)
            {
                volume.SetAt(x, 0, z, Filled);
                volume.SetAt(x, 1, z, Filled);

                if (x < 6 || z < 6)
                {
                    if (x == 3 || x == 5 || z == 3 || z == 5) { }
                    else
                    {
                        volume.SetAt(x, 2, z, Filled);
                    }
                }

                if (x >= 5 && z < 6)
                {
                    volume.SetAt(x, 3, z, Filled);
                }
            }
        }

        x = 5;
        z = 5;
        volume.SetAt(x, 3, z, Filled);
        volume.SetAt(x, 4, z, Filled);
        volume.SetAt(x, 5, z, Filled);

        x = 7;
        z = 5;
        volume.SetAt(x, 3, z, Filled);
        volume.SetAt(x, 4, z, Filled);
        volume.SetAt(x, 5, z, Filled);

        x = 6;
        z = 4;
        volume.SetAt(x, 3, z, Filled);
        volume.SetAt(x, 4, z, Filled);
        volume.SetAt(x, 5, z, Filled);
        volume.SetAt(x, 6, z, Filled);
        volume.SetAt(x, 7, z, Filled);
    }
}
