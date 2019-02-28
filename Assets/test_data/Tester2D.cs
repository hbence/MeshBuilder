using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using MeshBuilder;
using DataVolume = MeshBuilder.Volume<MeshBuilder.Tile.Data>;


public class Tester2D : MonoBehaviour
{
    public TileThemePalette palette;

    public MeshFilter meshFilter;

    private DataVolume dataVolume;
    private TileMesher2D mesher;

	void Start ()
    {
        CreateTestData();

        mesher = new TileMesher2D();
        mesher.Init(dataVolume, 0, 0, palette);
        mesher.StartGeneration();
	}

    private void CreateTestData()
    {
        dataVolume = new DataVolume(10, 1, 10);

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

        Set(8, 9);

        Set(6, 5);
        Set(7, 4);
        Set(7, 3);
        Set(8, 3);

        /*
        Set(2, 2);
        Set(3, 2);
        Set(2, 3);
        Set(2, 4);
        Set(2, 5);
        Set(3, 5);
        */
    }

    private void Set(int x, int y)
    {
        dataVolume[x, 0, y] = new Tile.Data { themeIndex = 0 };
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
