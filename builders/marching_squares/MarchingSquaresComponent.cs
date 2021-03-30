using MeshBuilder;
using NUnit.Framework.Internal;
using System;
using Unity.Collections;
using UnityEngine;

namespace MeshBuilder
{
    public class MarchingSquaresComponent : MonoBehaviour
    {
        [SerializeField] private MeshFilter meshFilter = null;
        public MeshFilter MeshFilter 
        { 
            get => meshFilter;
            set
            {
                meshFilter = value;
                if (meshFilter != null)
                {
                    meshFilter.sharedMesh = Mesh;
                }
            }
        }

        [SerializeField] private bool initializeInAwake = true;
        [SerializeField] private InitializationInfo initialization;
        public InitializationInfo InitInfo => initialization;
        public float CellSize => initialization.cellSize;

        public MarchingSquaresMesher Mesher { get; private set; }
        public bool IsGenerating => Mesher != null && Mesher.IsGenerating;

        public bool AutoCompletion { get; set; } = true;

        public Mesh Mesh { get; private set; }

        private MesherDataHandler mesherData;
        public MarchingSquaresMesher.Data Data => mesherData != null ? mesherData.Data : null;

        public delegate void OnMeshChangedFn(Mesh mesh);
        public OnMeshChangedFn OnMeshChanged;

        private void Awake()
        {
            if (initializeInAwake)
            {
                if (Mesher == null)
                {
                    Init();
                }
            }
        }

        private void CreateMesher()
        {
            if (mesherData == null)
            {
                mesherData = new MesherDataHandler();
            }

            if (Mesher == null)
            {
                Mesher = new MarchingSquaresMesher();
            }

            if (Mesh == null)
            {
                Mesh = new Mesh();
                if (meshFilter != null)
                {
                    meshFilter.sharedMesh = Mesh;
                }
            }
        }

        public void Init(InitializationInfo updatedInitInfo = null)
        {
            CreateMesher();

            if (updatedInitInfo != null)
            {
                initialization = updatedInitInfo;
            }
            
            if (initialization.dataManagement == InitializationInfo.DataManagement.CreateOwn)
            {
                mesherData.CreateOwned(initialization.DataInfo);
            }

            if (mesherData.HasData)
            {
                initialization.Init(Mesher, mesherData.Data);
            }
        }

        public void InitWithData(MarchingSquaresMesher.Data data, InitializationInfo updatedInitInfo = null)
        {
            CreateMesher();

            if (updatedInitInfo != null)
            {
                initialization = updatedInitInfo;
            }

            if (initialization.dataManagement == InitializationInfo.DataManagement.CreateOwn)
            {
                mesherData.CreateOwned(initialization.DataInfo);
                mesherData.Copy(data);
            }
            else
            {
                mesherData.SetBorrowed(data);
            }

            if (mesherData.HasData)
            {
                initialization.Init(Mesher, mesherData.Data);
            }
        }

        public void UpdateData(MarchingSquaresMesher.Data data)
            => InitWithData(data);

        public void Regenerate()
        {
            if (!Mesher.IsInitialized)
            {
                Debug.LogError("MarchingSquaresComponent Mesher needs to be initialized!");
                return;
            }

            if (!Mesher.IsGenerating)
            {
                Mesher.Start();
            }

            if (!isActiveAndEnabled && AutoCompletion)
            {
                CompleteGeneration();
            }
        }

        private void LateUpdate() 
            => AutoComplete();

        private void OnDisable() 
            => AutoComplete();

        private void AutoComplete()
        {
            if (AutoCompletion)
            {
                CompleteGeneration();
            }
        }

        public void CompleteGeneration()
        {
            if (Mesher.IsGenerating)
            {
                Mesher.Complete(Mesh);
                OnMeshChanged?.Invoke(Mesh);
            }
        }

        private void OnDestroy()
        {
            Mesher?.Dispose();
            mesherData?.Dispose();
        }

        private class MesherDataHandler : IDisposable
        {
            public const bool ClearData = true;
            public const bool DontClearData = false;

            private bool owned = false;
            public MarchingSquaresMesher.Data Data { get; private set; }
            public bool HasData => Data != null;

            public void CreateOwned(InitializationInfo.DataCreationInfo dataInfo, bool clear = ClearData)
            {
                if (owned && HasData && DoAttritbutesMatch(dataInfo, Data))
                {
                    if (clear)
                    {
                        Data.Clear();
                    }
                }
                else
                {
                    Dispose();

                    owned = true;
                    Data = dataInfo.Create();
                }
            }

            public void Copy(MarchingSquaresMesher.Data source)
            {
                if (!owned)
                {
                    Debug.LogError("Data is not owned by this component, so copy is not allowed to avoid unintentionally overwriting it!");
                    return;
                }

                int length = Mathf.Min(Data.RawData.Length, source.RawData.Length);
                if (Data.RawData.Length != source.RawData.Length)
                {
                    Debug.LogWarning("Data length mismatch, copy get truncted!");
                }

                NativeArray<float>.Copy(source.RawData, Data.RawData, length);
                if (Data.HasHeights && source.HasHeights)
                {
                    NativeArray<float>.Copy(source.HeightsRawData, Data.HeightsRawData, length);
                }
                if (Data.HasCullingData && source.HasCullingData)
                {
                    NativeArray<bool>.Copy(source.CullingDataRawData, Data.CullingDataRawData, length);
                }
            }

            public void SetBorrowed(MarchingSquaresMesher.Data data)
            {
                Dispose();

                owned = false;
                Data = data;
            }

            public void Dispose()
            {
                if (owned)
                {
                    Data?.Dispose();
                }
                Data = null;
            }

            private static bool DoAttritbutesMatch(InitializationInfo.DataCreationInfo info, MarchingSquaresMesher.Data data)
            {
                return info.ColNum == data.ColNum && info.RowNum == data.RowNum && info.HasHeightData == data.HasHeights && info.HasCullingData == data.HasCullingData;
            }
        }

        [Serializable]
        public class InitializationInfo
        {
            private const bool HasBottom = true;
            private const bool NoBottom = false;

            public enum Type
            {
                TopOnly,
                TopOptimizedGreedy,
                TopOptimizedLargestRect,

                SideOnly,
                TaperedSideOnly,
                SegmentedSideOnly,
                
                NoBottomFullCell,
                TaperedNoBottomFullCell,
                SegmentedNoBottomFullCell,
                
                FullCell,
                SimpleFullCell
            }

            public DataManagement dataManagement = DataManagement.CreateOwn;
            public DataCreationInfo dataCreationInfo;
            public DataCreationInfo DataInfo => dataCreationInfo;
            public Type type = Type.FullCell;
            public float cellSize = 1f;
            public float height = 0f;
            public float lerpToExactEdge = 1f;
            public float taperedTopOffset = 0.0f;
            public float taperedBottomOffset = 0.5f;
            public MarchingSquaresMesher.SideOffset[] segmentedOffsets;

            public void Init(MarchingSquaresMesher mesher, MarchingSquaresMesher.Data data)
            {
                if (type == Type.SegmentedSideOnly || type == Type.SegmentedNoBottomFullCell)
                {
                    if (segmentedOffsets == null || segmentedOffsets.Length < 2)
                    {
                        Debug.LogError("You need to set the segmented offsets array for segmented meshers!");
                    }
                }

                switch(type)
                {
                    case Type.TopOnly:                  mesher.Init(data, cellSize, height, lerpToExactEdge); break;
                    case Type.TopOptimizedGreedy:       mesher.InitForOptimized(data, cellSize, height, lerpToExactEdge, MarchingSquaresMesher.OptimizationMode.GreedyRect); break;
                    case Type.TopOptimizedLargestRect:  mesher.InitForOptimized(data, cellSize, height, lerpToExactEdge, MarchingSquaresMesher.OptimizationMode.NextLargestRect); break;
                    
                    case Type.SideOnly:          mesher.InitForSideOnly(data, cellSize, height, lerpToExactEdge); break;
                    case Type.TaperedSideOnly:   mesher.InitForTaperedSideOnly(data, cellSize, height, taperedTopOffset, taperedBottomOffset, lerpToExactEdge); break;
                    case Type.SegmentedSideOnly: mesher.InitForSegmentedSideOnly(data, cellSize, segmentedOffsets, lerpToExactEdge); break;
                    
                    case Type.NoBottomFullCell:          mesher.InitForFullCell(data, cellSize, height, NoBottom, lerpToExactEdge); break;
                    case Type.TaperedNoBottomFullCell:   mesher.InitForFullCellTapered(data, cellSize, height, taperedBottomOffset, NoBottom, lerpToExactEdge); break;
                    case Type.SegmentedNoBottomFullCell: mesher.InitForFullCellSegmented(data, cellSize, segmentedOffsets, NoBottom, lerpToExactEdge); break; 
                    
                    case Type.FullCell:         mesher.InitForFullCell(data, cellSize, height, HasBottom, lerpToExactEdge); break;
                    case Type.SimpleFullCell:   mesher.InitForFullCellSimpleMesh(data, cellSize, height, lerpToExactEdge); break;
                    default:
                        {
                            mesher.Init(data, cellSize, height, lerpToExactEdge);
                            Debug.LogError("Unhandled mesher initialization type: " + type);
                            break;
                        }
                }
            }

            public enum DataManagement
            {
                CreateOwn,
                SetFromOutside
            }

            [Serializable]
            public class DataCreationInfo
            {
                [SerializeField] private int colNum = 10;
                public int ColNum => colNum;
                [SerializeField] private int rowNum = 10;
                public int RowNum => rowNum;
                [SerializeField] private bool hasHeightData = false;
                public bool HasHeightData => hasHeightData;
                [SerializeField] private bool hasCullingData = false;
                public bool HasCullingData => hasCullingData;

                public MarchingSquaresMesher.Data Create()
                {
                    var data = new MarchingSquaresMesher.Data(colNum, rowNum);
                    if (hasHeightData)
                    {
                        data.InitHeights();
                    }
                    if (hasCullingData)
                    {
                        data.InitCullingData();
                    }
                    return data;
                }
            }
        }
    }
}