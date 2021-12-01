using UnityEngine;
using Unity.Collections;

using static MeshBuilder.MarchingSquaresMesher;
using static MeshBuilder.MarchingSquaresComponent;

namespace MeshBuilder
{
    [CreateAssetMenu(fileName = "marching_data", menuName = "Custom/MarchingSquares", order = 1)]
    [System.Serializable]
    public class MarchingSquaresDataAsset : ScriptableObject
    {
        [SerializeField] private DataCreationInfo info;
        public DataCreationInfo Info => info;

        [SerializeField, HideInInspector] private float[] data;
        public float[] Data => data;
        [SerializeField, HideInInspector] private float[] heightData;
        public float[] HeightData => heightData;
        [SerializeField, HideInInspector] private bool[] cullingData;
        public bool[] CullingData => cullingData;

        public void Init(int colNum, int rowNum, bool hasHeight, bool hasCulling)
        {
            info = new DataCreationInfo(colNum, rowNum, hasHeight, hasCulling);
            data = new float[colNum * rowNum];
            heightData = hasHeight ? new float[colNum * rowNum] : null;
            cullingData = hasCulling ? new bool[colNum * rowNum] : null;
        }

        public void Init(DataCreationInfo info)
         => Init(info.ColNum, info.RowNum, info.HasHeightData, info.HasCullingData);

        public void UpdateData(Data data)
        {
            Init(data.ColNum, data.RowNum, data.HasHeights, data.HasCullingData);
            if (heightData != null && data.HasHeights)
            {
                NativeArray<float>.Copy(data.HeightsRawData, heightData);
            }
            if (cullingData != null && data.HasCullingData)
            {
                NativeArray<bool>.Copy(data.CullingDataRawData, cullingData);
            }
        }

        public Data CreateData()
        {
            var data = info.Create();
            if (heightData != null && data.HasHeights)
            {
                NativeArray<float>.Copy(heightData, data.HeightsRawData);
            }
            if (cullingData != null && data.HasCullingData)
            {
                NativeArray<bool>.Copy(cullingData, data.CullingDataRawData);
            }
            return data;
        }
    }
}
