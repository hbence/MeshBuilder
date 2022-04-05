using System;
using System.IO;

using UnityEngine;
using Unity.Collections;

#if UNITY_EDITOR
using UnityEditor;
#endif

using static MeshBuilder.MarchingSquaresComponent;
using Data = MeshBuilder.MarchingSquaresMesherData;

namespace MeshBuilder
{
    public class MarchingSquaresDataComponent : MonoBehaviour
    {
        public enum InitPolicy { Dont, InAwake, InStart }
        public enum SavePolicy { ManualOnly, SaveOnDestroy }
        public enum SerializationPolicy { BuiltIn, DataAsset, Binary }

        [Header("creation")]
        [SerializeField] private DataCreationInfo creationInfo = null;
        public DataCreationInfo CreationInfo => creationInfo;

        [Header("behaviour")]
        [SerializeField] private InitPolicy initPolicy = InitPolicy.InAwake;
        [SerializeField] private SavePolicy savePolicy = SavePolicy.ManualOnly;
        [SerializeField] private SerializationPolicy serializationPolicy = SerializationPolicy.BuiltIn;

        [SerializeField] private MarchingSquaresDataAsset dataAsset = null;
        public MarchingSquaresDataAsset DataAsset
        {
            get => dataAsset;
            set => dataAsset = value;
        }
        [SerializeField] private string binaryDataPath = null;
        public string BinaryDataPath
        {
            get => binaryDataPath;
            set => binaryDataPath = value;
        }

        [SerializeField, HideInInspector] private SerializableData serializedData = null;

        public Data Data { get; private set; }

        public int ColNum => Data == null ? creationInfo.ColNum : Data.ColNum;
        public int RowNum => Data == null ? creationInfo.RowNum : Data.RowNum;

        public event Action<Data> OnDataChange;

        private void Awake()
        {
            if (initPolicy == InitPolicy.InAwake)
            {
                Init();
            }
        }

        private void Start()
        {
            if (initPolicy == InitPolicy.InStart)
            {
                Init();
            }
        }

        public void OnDestroy()
        {
            if (savePolicy == SavePolicy.SaveOnDestroy)
            {
                Save();
            }

            Data?.Dispose();
            Data = null;
        }

        public void Init()
        {
            Data?.Dispose();
            Data = null;

            if ((serializationPolicy == SerializationPolicy.BuiltIn) || 
                (serializationPolicy == SerializationPolicy.DataAsset && dataAsset != null) ||
                (serializationPolicy == SerializationPolicy.Binary && DoesPathExist(binaryDataPath)))
            {
                Load();
            }

            if (Data == null)
            {
                Create(creationInfo);
            }
        }

        public void Create(DataCreationInfo info)
        {
            creationInfo = info;
            Data?.Dispose();
            Data = info.Create();
            Data.Clear();
        }

        public Data LoadData()
        {
            Data data = null;
            if (serializationPolicy == SerializationPolicy.BuiltIn)
            {
                if (serializedData != null && 
                    serializedData.ColNum > 0 && serializedData.ColNum > 0 && 
                    serializedData.Data != null && serializedData.Data.Length == serializedData.ColNum * serializedData.RowNum)
                {
                    data = new Data(serializedData.ColNum, serializedData.RowNum, serializedData.Data,
                                serializedData.HasHeights, serializedData.HeightData,
                                serializedData.HasCulling, serializedData.CullingData);
                }
            }
            if (serializationPolicy == SerializationPolicy.Binary)
            {
                data = LoadBinary(binaryDataPath);
            }
            else if (serializationPolicy == SerializationPolicy.DataAsset)
            {
                data = LoadDataAsset(dataAsset);
            }

            return data;
        }

        public void Load()
        {
            var oldData = Data;
            
            Data = LoadData();

            if (Data == null)
            {
                Create(creationInfo);
            }

            Changed();

            oldData?.Dispose();
        }

        public void UpdateData(Data newData)
        {
            if (Data == null ||
                Data.ColNum != newData.ColNum || Data.RowNum != newData.RowNum ||
                Data.HasHeights != newData.HasHeights || Data.HasCullingData != newData.HasCullingData)
            {
                Data?.Dispose();
                Data = new Data(newData.ColNum, newData.RowNum, null, newData.HasHeights, null, newData.HasCullingData, null);
            }

            NativeArray<float>.Copy(newData.RawData, Data.RawData);
                
            if (newData.HasHeights)
            {
                NativeArray<float>.Copy(newData.HeightsRawData, Data.HeightsRawData);
            }
            if (newData.HasCullingData)
            {
                NativeArray<bool>.Copy(newData.CullingDataRawData, Data.CullingDataRawData);
            }

            Changed();
        }

        static public void ClearDistanceData(Data data, float clearingValue = Data.DefaultClearDistance)
        {
            var rawData = data.RawData;
            for(int i = 0; i < rawData.Length; ++i)
            {
                rawData[i] = clearingValue;
            }
        }

        static public void ClearHeightData(Data data, float clearingValue = Data.DefaultClearHeight)
        {
            if (data.HasHeights)
            {
                var rawData = data.HeightsRawData;
                for (int i = 0; i < rawData.Length; ++i)
                {
                    rawData[i] = clearingValue;
                }
            }
        }

        static public void ClearCullingData(Data data, bool clearingValue = Data.DefaultClearCulling)
        {
            if (data.HasHeights)
            {
                var rawData = data.CullingDataRawData;
                for (int i = 0; i < rawData.Length; ++i)
                {
                    rawData[i] = clearingValue;
                }
            }
        }

        public void Save()
        {
            if (Data != null)
            {
                Save(Data);
            }
        }

        public void Save(Data data)
        {
            if (serializationPolicy == SerializationPolicy.BuiltIn)
            {
                serializedData = SerializableData.CreateFromData(data);
            }
            else if (serializationPolicy == SerializationPolicy.Binary)
            {
                SaveBinary(binaryDataPath, data);
            }
            else if (serializationPolicy == SerializationPolicy.DataAsset)
            {
                SaveDataAsset(dataAsset, data);
            }
        }    

        static public Data LoadDataAsset(MarchingSquaresDataAsset asset)
        {
            if (asset.Info != null && asset.Info.ColNum > 0 && asset.Info.RowNum > 0)
            {
                return asset.CreateData();
            }
            return null;
        }

        static public void SaveDataAsset(MarchingSquaresDataAsset asset, Data data)
        {
            asset.UpdateData(data);
#if UNITY_EDITOR
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
#endif
        }

        static public bool DoesPathExist(string path)
            => File.Exists(path);

        static public Data LoadBinary(string path)
        {
            try
            {
                using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
                {
                    return ReadData(reader);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            return null;
        }

        static public Data ReadData(BinaryReader reader)
        {
            int colNum = reader.ReadInt32();
            int rowNum = reader.ReadInt32();
            bool hasHeights = reader.ReadBoolean();
            bool hasCulling = reader.ReadBoolean();

            var data = new Data(colNum, rowNum, null, hasHeights, null, hasCulling, null);
            var dist = data.RawData;
            int length = colNum * rowNum;
            for (int i = 0; i < length; ++i)
            {
                dist[i] = reader.ReadSingle();
            }

            if (hasHeights)
            {
                var heights = data.HeightsRawData;
                for (int i = 0; i < length; ++i)
                {
                    heights[i] = reader.ReadSingle();
                }
            }

            if (hasCulling)
            {
                var culling = data.CullingDataRawData;
                for (int i = 0; i < length; ++i)
                {
                    culling[i] = reader.ReadBoolean();
                }
            }

            return data;
        }

        static public void SaveBinary(string path, Data data)
        {
            if (data == null)
            {
                Debug.LogError("Can't save null data!");
                return;
            }

            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.Open(path, FileMode.Create)))
                {
                    WriteData(writer, data);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        static public void WriteData(BinaryWriter writer, Data data)
        {
            writer.Write(data.ColNum);
            writer.Write(data.RowNum);
            writer.Write(data.HasHeights);
            writer.Write(data.HasCullingData);

            int length = data.ColNum * data.RowNum;
            for (int i = 0; i < length; ++i)
            {
                writer.Write(data.RawData[i]);
            }

            if (data.HasHeights)
            {
                for (int i = 0; i < length; ++i)
                {
                    writer.Write(data.HeightsRawData[i]);
                }
            }

            if (data.HasCullingData)
            {
                for (int i = 0; i < length; ++i)
                {
                    writer.Write(data.CullingDataRawData[i]);
                }
            }
        }

        static public void WriteDataLimited(BinaryWriter writer, Data data, float minDist, float maxDist)
        {
            writer.Write(data.ColNum);
            writer.Write(data.RowNum);
            writer.Write(data.HasHeights);
            writer.Write(data.HasCullingData);

            int length = data.ColNum * data.RowNum;
            WriteDataLimitedToByte(writer, data.RawData, minDist, maxDist);

            if (data.HasHeights)
            {
                for (int i = 0; i < length; ++i)
                {
                    writer.Write(data.HeightsRawData[i]);
                }
            }

            if (data.HasCullingData)
            {
                for (int i = 0; i < length; ++i)
                {
                    writer.Write(data.CullingDataRawData[i]);
                }
            }
        }

        static public Data ReadDataLimited(BinaryReader reader, float minDist, float maxDist)
        {
            int colNum = reader.ReadInt32();
            int rowNum = reader.ReadInt32();
            bool hasHeights = reader.ReadBoolean();
            bool hasCulling = reader.ReadBoolean();

            var data = new Data(colNum, rowNum, null, hasHeights, null, hasCulling, null);
            var dist = data.RawData;
            int length = colNum * rowNum;
            ReadDataLimitedFromByte(reader, dist, minDist, maxDist);

            if (hasHeights)
            {
                var heights = data.HeightsRawData;
                for (int i = 0; i < length; ++i)
                {
                    heights[i] = reader.ReadSingle();
                }
            }

            if (hasCulling)
            {
                var culling = data.CullingDataRawData;
                for (int i = 0; i < length; ++i)
                {
                    culling[i] = reader.ReadBoolean();
                }
            }

            return data;
        }

        static public void WriteDataLimitedToByte(BinaryWriter writer, NativeArray<float> data, float min, float max)
        {
            for(int i = 0; i < data.Length; ++i)
            {
                byte val = LimitToByte(data[i], min, max);
                writer.Write(val);
            }
        }

        static public void ReadDataLimitedFromByte(BinaryReader reader, NativeArray<float> data, float min, float max)
        {
            float delta = max - min;
            for (int i = 0; i < data.Length; ++i)
            {
                byte val = reader.ReadByte();
                float scaledValue = (val / 255f) * delta;
                data[i] = min + scaledValue;
            }
        }

        static public byte LimitToByte(float value, float min, float max)
        {
            value = Mathf.Clamp(value, min, max);
            float t = (value - min) / (max - min);
            return (byte)Mathf.FloorToInt(t * 255f);
        }

        static public float FromLimitedToByte(byte value, float min, float max)
        {
            float scaledValue = (value / 255f) * (max - min);
            return min + scaledValue;
        }

        public void Changed()
        {
            OnDataChange?.Invoke(Data);   
        }

        [Serializable]
        public class SerializableData
        {
            [SerializeField] private int colNum = 0;
            public int ColNum => colNum;
            [SerializeField] private int rowNum = 0;
            public int RowNum => rowNum;
            [SerializeField] private float[] data = null;
            public float[] Data => data;
            [SerializeField] private bool hasHeights = false;
            public bool HasHeights => hasHeights;
            [SerializeField] private float[] heightData = null;
            public float[] HeightData => heightData;
            [SerializeField] private bool hasCulling = false;
            public bool HasCulling => hasCulling;
            [SerializeField] private bool[] cullingData = null;
            public bool[] CullingData => cullingData;

            public void FromData(Data data)
            {
                colNum = data.ColNum;
                rowNum = data.RowNum;
                this.data = new float[data.RawData.Length];
                NativeArray<float>.Copy(data.RawData, this.data);

                hasHeights = data.HasHeights;
                if (hasHeights)
                {
                    heightData = new float[data.HeightsRawData.Length];
                    NativeArray<float>.Copy(data.HeightsRawData, heightData);
                }
                else
                {
                    heightData = null;
                }

                hasCulling = data.HasCullingData;
                if (hasCulling)
                {
                    cullingData = new bool[data.CullingDataRawData.Length];
                    NativeArray<bool>.Copy(data.CullingDataRawData, cullingData);
                }
                else
                {
                    cullingData = null;
                }
            }

            public Data CreateData()
                => new Data(ColNum, RowNum, Data, HasHeights, HeightData, HasCulling, CullingData);

            static public SerializableData CreateFromData(Data data)
            {
                var serializableData = new SerializableData();
                serializableData.FromData(data);
                return serializableData;
            }
        }
    }
}