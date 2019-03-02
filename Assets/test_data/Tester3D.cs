using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using MeshBuilder;
using DataVolume = MeshBuilder.Volume<MeshBuilder.Tile.Data>;

public class Tester3D : MonoBehaviour
{
    public TileThemePalette palette;

    public MeshFilter meshFilter;

    private DataVolume dataVolume;
    private TileMesher3D mesher;

    void Start ()
    {
        CreateTestData2();

        mesher = new TileMesher3D();
        mesher.Init(dataVolume, 0, palette);
        mesher.StartGeneration();
    }

    private void CreateTestData()
    {
        dataVolume = new DataVolume(10, 3, 10);

        for (int i = 0; i < dataVolume.Length; ++i)
        {
            dataVolume[i] = new Tile.Data { themeIndex = 100 };
        }
        Set(2, 2);
        
        Set(5, 6);
        Set(5, 7);
        Set(5, 8);
        Set(6, 6);
        //Set(6, 7);
        Set(6, 8);
        Set(7, 6);
        Set(7, 7);
        Set(7, 8);

        Set1(7, 6);
       // Set1(7, 7);
        //Set1(7, 8);
        Set1(7, 9);
        Set2(7, 8);
        Set2(7, 7);
        Set2(7, 6);

        Set(8, 9);

        Set(6, 5);
        Set(7, 4);
        Set(7, 3);
        Set(8, 3);

        Set1(3, 3);
        Set1(4, 4);
        Set2(3, 4);
        Set2(4, 3);

        /*
        Set(2, 2);
        Set(3, 2);
        Set(2, 3);
        Set(2, 4);
        Set(2, 5);
        Set(3, 5);
        */
    }

    private void CreateTestData2()
    {
        dataVolume = new DataVolume(16, 16, 16);
        for (int i = 0; i < dataVolume.Length; ++i)
        {
            dataVolume[i] = new Tile.Data { themeIndex = 100 };
        }

        var Filled = new Tile.Data { themeIndex = 0 };

        int x = 5;
        int z = 5;

        for (x = 0; x < 16; ++x)
        {
            for (z = 0; z < 16; ++z)
            {
                dataVolume[x, 0, z] = Filled;
                dataVolume[x, 1, z] = Filled;
                
                if (x < 6 || z < 6)
                {
                    if (x == 3 || x == 5 || z == 3 || z == 5) { }
                    else
                    {
                        dataVolume[x, 2, z] = Filled;
                    }
                }

                if (x >= 5 && z < 6)
                {
                    dataVolume[x, 3, z] = Filled;
                }
            }
        }

        x = 5;
        z = 5;
        dataVolume[x, 3, z] = Filled;
        dataVolume[x, 4, z] = Filled;
        dataVolume[x, 5, z] = Filled;

        x = 7;
        z = 5;
        dataVolume[x, 3, z] = Filled;
        dataVolume[x, 4, z] = Filled;
        dataVolume[x, 5, z] = Filled;

        x = 6;
        z = 4;
        dataVolume[x, 3, z] = Filled;
        dataVolume[x, 4, z] = Filled;
        dataVolume[x, 5, z] = Filled;
        dataVolume[x, 6, z] = Filled;
        dataVolume[x, 7, z] = Filled;
    }

    private void Set(int x, int y)
    {
        dataVolume[x, 0, y] = new Tile.Data { themeIndex = 0 };
    }

    private void Set1(int x, int y)
    {
        dataVolume[x, 1, y] = new Tile.Data { themeIndex = 0 };
    }

    private void Set2(int x, int y)
    {
        dataVolume[x, 2, y] = new Tile.Data { themeIndex = 0 };
    }


    void LateUpdate()
    {
        if (mesher.IsGenerating)
        {
            mesher.EndGeneration();
            meshFilter.sharedMesh = mesher.Mesh;
        }
    }

    private void OnDestroy()
    {
        mesher.Dispose();
        dataVolume.Dispose();
    }
}
