using System;
using System.IO;

using UnityEngine;
using Unity.Collections;

using static MeshBuilder.MarchingSquaresComponent;
using static MeshBuilder.MarchingSquaresMesher;

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
        }

        public void Load()
        {
            Data?.Dispose();
            Data = null;

            if (serializationPolicy == SerializationPolicy.BuiltIn)
            {
                if (serializedData != null && serializedData.colNum > 0 && serializedData.rowNum > 0 && serializedData.data != null)
                {
                    Data = new Data(serializedData.colNum, serializedData.rowNum, serializedData.data,
                                serializedData.heightData != null, serializedData.heightData,
                                serializedData.cullingData != null, serializedData.cullingData);
                }
            }
            if (serializationPolicy == SerializationPolicy.Binary)
            {
                Data = LoadBinary(binaryDataPath);
            }
            else if (serializationPolicy == SerializationPolicy.DataAsset)
            {
                Data = LoadDataAsset(dataAsset);
            }

            if (Data == null)
            {
                Create(creationInfo);
            }

            Changed();
        }

        public void UpdateData(Data newData)
        {
            if (Data != null)
            {
                if (Data.ColNum != newData.ColNum || Data.RowNum != newData.RowNum ||
                    Data.HasHeights != newData.HasHeights || Data.HasCullingData != newData.HasCullingData)
                {
                    Data.Dispose();
                    Data = new Data(newData.ColNum, newData.RowNum, null, newData.HasHeights, null, newData.HasHeights, null);
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
            }

            Changed();
        }

        public void Save()
        {
            if (serializationPolicy == SerializationPolicy.BuiltIn)
            {
                if (Data != null)
                {
                    serializedData = SerializableData.CreateFromData(Data);
                }
            }
            else if (serializationPolicy == SerializationPolicy.Binary)
            {
                SaveBinary(binaryDataPath, Data);
            }
            else if (serializationPolicy == SerializationPolicy.DataAsset)
            {
                SaveDataAsset(dataAsset, Data);
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
        }

        static public bool DoesPathExist(string path)
            => File.Exists(path);

        static public Data LoadBinary(string path)
        {
            try
            {
                using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
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
                            dist[i] = reader.ReadSingle();
                        }
                    }

                    return data;
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            return null;
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
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        public void Changed()
        {
            OnDataChange?.Invoke(Data);   
        }

        [Serializable]
        private class SerializableData
        {
            [SerializeField] public int colNum = 0;
            [SerializeField] public int rowNum = 0;
            [SerializeField] public float[] data = null;
            [SerializeField] public float[] heightData = null;
            [SerializeField] public bool[] cullingData = null;

            public void FromData(Data data)
            {
                colNum = data.ColNum;
                rowNum = data.RowNum;
                this.data = new float[data.RawData.Length];
                NativeArray<float>.Copy(data.RawData, this.data);
                if (data.HasHeights)
                {
                    heightData = new float[data.HeightsRawData.Length];
                    NativeArray<float>.Copy(data.HeightsRawData, heightData);
                }
                if (data.HasCullingData)
                {
                    cullingData = new bool[data.CullingDataRawData.Length];
                    NativeArray<bool>.Copy(data.CullingDataRawData, cullingData);
                }
            }

            static public SerializableData CreateFromData(Data data)
            {
                var serializableData = new SerializableData();
                serializableData.FromData(data);
                return serializableData;
            }
        }
    }
}