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

        public void UpdateData(Data newData)
        {
            Init(newData.ColNum, newData.RowNum, newData.HasHeights, newData.HasCullingData);
            
            NativeArray<float>.Copy(newData.RawData, data);

            if (heightData != null && newData.HasHeights)
            {
                NativeArray<float>.Copy(newData.HeightsRawData, heightData);
            }
            if (cullingData != null && newData.HasCullingData)
            {
                NativeArray<bool>.Copy(newData.CullingDataRawData, cullingData);
            }
        }

        public Data CreateData()
        {
            var copyData = info.Create();

            NativeArray<float>.Copy(data, copyData.RawData);

            if (heightData != null && copyData.HasHeights)
            {
                NativeArray<float>.Copy(heightData, copyData.HeightsRawData);
            }
            if (cullingData != null && copyData.HasCullingData)
            {
                NativeArray<bool>.Copy(cullingData, copyData.CullingDataRawData);
            }
            return copyData;
        }
    }
}
