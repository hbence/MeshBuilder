using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using MeshBuilder;
using DataVolume = MeshBuilder.Volume<byte>;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class TesterTileMesher3D : MonoBehaviour
{
    private const int Filled = 1;

    public TileTheme3DComponent theme;

    private MeshFilter filter;
    private TileMesher3D mesher;

    private DataVolume testData;

    public TileMesher3D.Settings setting;

	void Awake()
    {
        filter = GetComponent<MeshFilter>();

        mesher = new TileMesher3D();

        testData = CreateTestData();
    }

    private void Start()
    {
        mesher.Init(testData, Filled, theme.theme);
        mesher.StartGeneration();
    }

    void LateUpdate()
    {
		if (mesher.IsGenerating)
        {
            mesher.EndGeneration();
            filter.sharedMesh = mesher.Mesh;
        }
	}

    private void OnDestroy()
    {
        mesher.Dispose();
        testData.Dispose();
        TileMesherConfigurations.Dispose();
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
