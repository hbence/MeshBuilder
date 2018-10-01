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

    public MeshFilter tileMeshFilter;

    public Texture2D heightmap;
    [Range(1, 4)]
    public int resolution = 3;
    public bool normalizeUV = true;

	private void Awake ()
    {
        data = CreateTestData();

        groundMesher = new GridMesher();
        //        groundMesher.Init(data, 1f, 1);
        groundMesher.Init(data, 1f, resolution, heightmap, 1f, 128, normalizeUV);

        groundMeshFilter.sharedMesh = groundMesher.Mesh;

  
	}

    private void Start()
    {
        groundMesher.StartGeneration();
    }

    private void LateUpdate()
    {
        if (groundMesher.IsGenerating)
        {
            groundMesher.EndGeneration();
        }


    }

    private void OnDestroy()
    {
        groundMesher.Dispose();
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
        int x = 5;
        int z = 5;

        for (x = 0; x < 16; ++x)
        {
            for (z = 0; z < 16; ++z)
            {
                volume[x, 0, z] = Filled;
                volume[x, 1, z] = Filled;

                if (x < 6 || z < 6)
                {
                    if (x == 3 || x == 5 || z == 3 || z == 5) { }
                    else
                    {
                        volume[x, 2, z] = Filled;
                    }
                }

                if (x >= 5 && z < 6)
                {
                    volume[x, 3, z] = Filled;
                }
            }
        }

        x = 5;
        z = 5;
        volume[x, 3, z] = Filled;
        volume[x, 4, z] = Filled;
        volume[x, 5, z] = Filled;

        x = 7;
        z = 5;
        volume[x, 3, z] = Filled;
        volume[x, 4, z] = Filled;
        volume[x, 5, z] = Filled;

        x = 6;
        z = 4;
        volume[x, 3, z] = Filled;
        volume[x, 4, z] = Filled;
        volume[x, 5, z] = Filled;
        volume[x, 6, z] = Filled;
        volume[x, 7, z] = Filled;
    }
}
