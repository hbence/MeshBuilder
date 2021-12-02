using System;
using Unity.Collections;
using UnityEngine;

namespace MeshBuilder
{
    public class MarchingSquaresComponent : MonoBehaviour
    {
        public enum InitializationPolicy { InAwake, InStart, Dont }

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

        // the default is to initialize in Start() because Awake() is called after the component is added
        // so if the user wants to avoid that, they have a chance to turn it off
        [SerializeField] private InitializationPolicy initializationPolicy = InitializationPolicy.InStart;
        [SerializeField] private InitializationInfo initialization;
        public InitializationInfo InitInfo { get => initialization; set => initialization = value.Clone(); }
        public float CellSize => initialization.cellSize;

        [SerializeField] private DataManagementPolicy dataCreationPolicy = DataManagementPolicy.CreateOwn;
        public DataManagementPolicy DataCreation => dataCreationPolicy;
        [SerializeField] private DataCreationInfo dataCreationInfo;
        [SerializeField] private MarchingSquaresDataComponent dataComponent = null;
        public MarchingSquaresDataComponent DataComponent
        {
            get => dataComponent;
            set
            {
                if (dataComponent != null)
                {
                    dataComponent.OnDataChange -= OnDataComponentChanged;
                }

                dataComponent = value;

                if (dataComponent != null && isActiveAndEnabled)
                {
                    dataComponent.OnDataChange += OnDataComponentChanged;
                }
            }
        }

        public MarchingSquaresMesher Mesher { get; private set; }
        public bool IsGenerating => Mesher != null && Mesher.IsGenerating;

        public bool AutoCompletion { get; set; } = true;

        public Mesh Mesh { get; private set; }

        private MesherDataHandler mesherData;
        public MarchingSquaresMesher.Data Data => mesherData != null ? mesherData.Data : null;
        public int ColNum => Data != null ? Data.ColNum : 0;
        public int RowNum => Data != null ? Data.RowNum : 0;

        public delegate void OnMeshChangedFn(Mesh mesh);
        public OnMeshChangedFn OnMeshChanged;

        private void Awake()
        {
            if (initializationPolicy == InitializationPolicy.InAwake)
            {
                if (Mesher == null)
                {
                    Init();
                }
            }
        }

        private void Start()
        {
            if (initializationPolicy == InitializationPolicy.InStart)
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

        public void Init(InitializationInfo initInfo = null)
            => Init(initInfo, dataCreationPolicy, dataCreationInfo);

        public void ChangeDataInfo(DataManagementPolicy dataPolicy, DataCreationInfo dataInfo)
        {
            bool hasInfoChanged = !dataCreationInfo.DoesEqual(dataInfo);
            if (hasInfoChanged)
            {
                dataCreationInfo = dataInfo.Clone();
            }

            bool hasDataPolicyChanged = dataCreationPolicy != dataPolicy;
            dataCreationPolicy = dataPolicy;

            if (mesherData != null)
            {
                if (dataCreationPolicy == DataManagementPolicy.CreateOwn)
                {
                    if (!mesherData.HasData || hasInfoChanged || hasDataPolicyChanged)
                    {
                        mesherData.CreateOwned(dataCreationInfo);
                    }
                }
                else if (dataCreationPolicy == DataManagementPolicy.SetFromOutside)
                {
                    if (hasDataPolicyChanged)
                    {
                        // if the policy was switched from owning to borrowing, it probably has it's own data which should be released
                        mesherData.Dispose();
                    }
                }
                else if (dataCreationPolicy == DataManagementPolicy.FromDataComponent)
                {
                    if (dataComponent != null)
                    {
                        mesherData.SetBorrowed(dataComponent.Data);
                    }
                }
            }
        }

        public void Init(InitializationInfo initInfo, DataManagementPolicy dataPolicy, DataCreationInfo dataInfo)
        {
            CreateMesher();

            if (initInfo != null)
            {
                initialization = initInfo.Clone();
            }

            ChangeDataInfo(dataPolicy, dataInfo);

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
                initialization = updatedInitInfo.Clone();
            }

            if (dataCreationPolicy == DataManagementPolicy.CreateOwn)
            {
                mesherData.CreateOwned(dataCreationInfo);
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

        private void OnEnable()
        {
            if (dataCreationPolicy == DataManagementPolicy.FromDataComponent && dataComponent != null)
            {
                dataComponent.OnDataChange += OnDataComponentChanged;
            }
        }

        private void OnDisable()
        {   
            AutoComplete();

            if (dataComponent != null)
            {
                dataComponent.OnDataChange -= OnDataComponentChanged;
            }
        }

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

        private void OnDataComponentChanged(MarchingSquaresMesher.Data data)
        {
            if (Mesher == null)
            {
                Init();
            }

            CompleteGeneration();

            mesherData.SetBorrowed(data);
            Regenerate();
        }

        private class MesherDataHandler : IDisposable
        {
            public const bool ClearData = true;
            public const bool DontClearData = false;

            private bool owned = false;
            public MarchingSquaresMesher.Data Data { get; private set; }
            public bool HasData => Data != null;

            public void CreateOwned(DataCreationInfo dataInfo, bool clear = ClearData)
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

            private static bool DoAttritbutesMatch(DataCreationInfo info, MarchingSquaresMesher.Data data)
            {
                return info.ColNum == data.ColNum && info.RowNum == data.RowNum && info.HasHeightData == data.HasHeights && info.HasCullingData == data.HasCullingData;
            }
        }

        public enum DataManagementPolicy
        {
            CreateOwn,
            SetFromOutside,
            FromDataComponent
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

            public DataCreationInfo(int col, int row, bool hasHeight = false, bool hasCulling = false)
            {
                colNum = col;
                rowNum = row;
                hasHeightData = hasHeight;
                hasCullingData = hasCulling;
            }

            public DataCreationInfo Clone()
                => new DataCreationInfo(colNum, rowNum, hasHeightData, HasCullingData);

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

            public bool DoesEqual(DataCreationInfo other)
                => colNum == other.colNum && rowNum == other.rowNum && hasHeightData == other.hasHeightData && hasCullingData == other.hasCullingData;
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

            public Type type = Type.FullCell;
            public float cellSize = 1f;
            public float height = 0f;
            public float lerpToExactEdge = 1f;
            public float taperedTopOffset = 0.0f;
            public float taperedBottomOffset = 0.5f;
            public MarchingSquaresMesher.SideOffset[] segmentedOffsets;

            public InitializationInfo Clone()
            {
                var info = new InitializationInfo();
                info.type = type;
                info.cellSize = cellSize;
                info.height = height;
                info.lerpToExactEdge = lerpToExactEdge;
                info.taperedTopOffset = taperedTopOffset;
                info.taperedBottomOffset = taperedBottomOffset;
                if (segmentedOffsets != null)
                {
                    info.segmentedOffsets = new MarchingSquaresMesher.SideOffset[segmentedOffsets.Length];
                    Array.Copy(segmentedOffsets, info.segmentedOffsets, segmentedOffsets.Length);
                }
                return info;
            }

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
        }
    }
}