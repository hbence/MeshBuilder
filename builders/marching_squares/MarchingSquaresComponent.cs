using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

using static MeshBuilder.CellMesher;
using static MeshBuilder.OptimizedTopCellMesher;
using static MeshBuilder.SegmentedSideCellMesher;
using static MeshBuilder.SideCellMesher;
using MesherType = MeshBuilder.MarchingSquaresMesher.MesherType;
using Data = MeshBuilder.MarchingSquaresMesherData;

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
        public bool GenerateOnInit = true;
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

        public bool AutoCompleteGeneration = true;

        public Mesh Mesh { get; private set; }

        private MesherDataHandler mesherData;
        public Data Data => mesherData != null ? mesherData.Data : null;
        public int ColNum => Data != null ? Data.ColNum : 0;
        public int RowNum => Data != null ? Data.RowNum : 0;

        public delegate void OnMeshChangedFn(Mesh mesh);
        public OnMeshChangedFn OnMeshChanged;

        private void Awake()
        {
            CreateMesher();   

            if (initializationPolicy == InitializationPolicy.InAwake)
            {
                Init();
            }
        }

        private void Start()
        {
            if (initializationPolicy == InitializationPolicy.InStart)
            {
                Init();
            }
        }

        private void CreateMesher()
        {
            if (Mesher == null)
            {
                Mesher = new MarchingSquaresMesher();
            }

            if (mesherData == null)
            {
                mesherData = new MesherDataHandler();
            }

            if (Mesh == null)
            {
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    Mesh = meshFilter.sharedMesh;
                }
                else
                {
                    Mesh = new Mesh();
                    if (meshFilter != null)
                    {
                        meshFilter.sharedMesh = Mesh;
                    }
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

                if (GenerateOnInit)
                {
                    Regenerate();
                }
            }
        }

        public void InitWithData(Data data, InitializationInfo updatedInitInfo = null)
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

        public void UpdateData(Data data)
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

            if (!isActiveAndEnabled && AutoCompleteGeneration)
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
            if (AutoCompleteGeneration)
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

        private void OnDataComponentChanged(Data data)
        {
            if (Mesher != null && Mesher.IsInitialized)
            {
                CompleteGeneration();

                mesherData.SetBorrowed(data);
                UpdateData(data);

                Regenerate();
            }
        }

        private class MesherDataHandler : IDisposable
        {
            public const bool ClearData = true;
            public const bool DontClearData = false;

            private bool owned = false;
            public Data Data { get; private set; }
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

            public void Copy(Data source)
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

            public void SetBorrowed(Data data)
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

            private static bool DoAttritbutesMatch(DataCreationInfo info, Data data)
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

            public Data Create()
            {
                var data = new Data(colNum, rowNum);
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
            public float cellSize = 1f;
            public bool generateUVs = true;
            public bool generateNormals = true;

            public SubmeshInitInfo[] submeshes = null;

            public InitializationInfo Clone()
            {
                var clone = (InitializationInfo)MemberwiseClone();
                if (submeshes != null)
                {
                    clone.submeshes = new SubmeshInitInfo[submeshes.Length];
                    for (int i = 0; i < submeshes.Length; ++i)
                    {
                        clone.submeshes[i] = submeshes[i].Clone();
                    }
                }
                return clone;
            }

            [Serializable]
            public class SubmeshInitInfo
            {
                public MesherInitInfo[] meshers = null;

                public SubmeshInitInfo Clone()
                {
                    var clone = new SubmeshInitInfo();
                    if (meshers != null)
                    {
                        clone.meshers = new MesherInitInfo[meshers.Length];
                        for (int i = 0; i < meshers.Length; ++i)
                        {
                            clone.meshers[i] = meshers[i].Clone();
                        }
                    }
                    return clone;
                }
            }

            [Serializable]
            public class MesherInitInfo
            {
                public MesherType type = MesherType.TopCell;

                // info
                public float lerpToExactEdge = 1f;
                public bool useCullingData = true;
                public bool useHeightData = true;
                public float offsetY = 0f;
                public float heightScale = 1f;
                public float uScale = 1f;
                public float vScale = 1f;
                public bool normalizeUV = true;
                public bool isFlipped = false;

                // scaled info
                public float scaledOffset = 0f;

                // optimized info
                public OptimizationMode optimizationMode = OptimizationMode.GreedyRect;

                // side info
                public float sideHeight = 1;
                public float bottomHeightScale = 1f;
                public float bottomScaledOffset = 0;

                // segmented info
                public Segment[] segments = DefaultSegments;

                public MesherInitInfo Clone()
                {
                    var clone = (MesherInitInfo)MemberwiseClone();
                    if (segments != null)
                    {
                        clone.segments = new Segment[segments.Length];
                        Array.Copy(segments, clone.segments, segments.Length);
                    }
                    return clone;
                }

                public MarchingSquaresMesher.MesherInfo GenerateInfo(bool generateNormals = true, bool generateUVs = true)
                {
                    Info info = null;
                    switch (type)
                    {
                        case MesherType.TopCell: 
                            {
                                info = new Info(); 
                                break; 
                            }
                        case MesherType.ScaledTopCell:
                            {
                                var sInfo = new ScaledInfo();
                                sInfo.ScaledOffset = scaledOffset;
                                info = sInfo;
                                break;
                            }
                        case MesherType.OptimizedTopCell:
                            {
                                var oInfo = new OptimizedInfo();
                                oInfo.OptimizationMode = optimizationMode;
                                info = oInfo;
                                break;
                            }
                        case MesherType.SideCell:
                        case MesherType.FullCell:
                            {
                                var sInfo = new SideInfo();
                                sInfo.ScaledOffset = scaledOffset;
                                sInfo.SideHeight = sideHeight;
                                sInfo.BottomHeightScale = bottomHeightScale;
                                sInfo.BottomScaledOffset = bottomScaledOffset;
                                info = sInfo;
                                break;
                            }
                        case MesherType.SegmentedSideCell:
                            {
                                var sInfo = new SegmentedSideInfo();
                                sInfo.Segments = segments;
                                info = sInfo;
                                break;
                            }
                        default:
                            Debug.LogError("Not handled mesher type!");
                            break;
                    }

                    info.LerpToExactEdge = lerpToExactEdge;
                    info.UseCullingData = useCullingData;
                    info.UseHeightData = useHeightData;
                    info.OffsetY = offsetY;
                    info.HeightScale = heightScale;
                    info.GenerateUvs = generateUVs;
                    info.UScale = uScale;
                    info.VScale = vScale;
                    info.NormalizeUV = normalizeUV;
                    info.GenerateNormals = generateNormals;
                    info.IsFlipped = isFlipped;

                    return MarchingSquaresMesher.MesherInfo.Create(type, info);
                }
            }

            public void Init(MarchingSquaresMesher mesher, Data data)
            {
                mesher.Init(data, cellSize);

                var submeshMeshers = new List<MarchingSquaresMesher.MesherInfo>();
                foreach (var submesh in submeshes)
                {
                    submeshMeshers.Clear();
                    foreach(var mesherInfo in submesh.meshers)
                    {
                        var info = mesherInfo.GenerateInfo(generateNormals, generateUVs);
                        submeshMeshers.Add(info);
                    }

                    mesher.AddSubmesh(submeshMeshers.ToArray());
                }
            }
        }
    }
}