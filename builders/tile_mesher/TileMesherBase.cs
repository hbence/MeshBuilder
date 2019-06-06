﻿using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;

using static MeshBuilder.Utils;
using DataInstance = MeshBuilder.MeshCombinationBuilder.DataInstance;
using PieceTransform = MeshBuilder.Tile.PieceTransform;

namespace MeshBuilder
{
    abstract public class TileMesherBase<TileVariant> : Builder where TileVariant : struct
    {
        public enum Type { Mesher2D, Mesher3D }
        
        protected enum GenerationType
        {
            /// <summary>
            /// Generate the data for the tiles and dispose of it after the mesh is ready, 
            /// the current meshers don't generate considerable amount of data in most cases 
            /// but if the mesh is static, there is no need to keep it in memory, 
            /// it may be also useful if there is a lot of chunks
            /// </summary>
            FromDataUncached,

            /// <summary>
            /// Generate the data for the tiles and keep it in memory, it's less work, and if there is
            /// randomness in the tile variants they don't change with every regeneration of the mesh
            /// </summary>
            FromDataCachedTiles,

            /// <summary>
            /// The tiles aren't generated by the mesher, they are loaded from somewhere else
            /// </summary>
            FromTiles
        }

        private TileTheme theme;
        public TileTheme Theme
        {
            get { return theme; }
            protected set
            {
                if (theme != value)
                {
                    theme?.Release();
                    theme = value;
                    theme?.Retain();
                }
            }
        }

        // the preferred mesh builder is the deferred version of the MeshCombinationBuilder class
        // this is also the default
        // the unity version is kept here in case the TileMesher needs a feature the MeshCombinationBuilder doesn't handle (and as a reference)
        protected enum CombinationMode
        {
            UnityBuiltIn, CombinationBuilder, DeferredCombinationBuilder
        }
        protected CombinationMode combinationMode = CombinationMode.DeferredCombinationBuilder;

        // in the data volume we're generating the mesh
        // for this value
        public int FillValue { get; protected set; }

        protected Extents dataExtents;
        protected Extents tileExtents;

        protected GenerationType generationType = GenerationType.FromDataUncached;

        private TileThemePalette themePalette;
        public TileThemePalette ThemePalette
        {
            get { return themePalette; }
            protected set
            {
                themePalette = value;
                themePalette?.Init();
            }
        }

        // GENERATED DATA
        protected Volume<TileVariant> tiles;

        protected MeshCombinationBuilder combinationBuilder;

        // TEMP DATA
        private NativeList<MeshInstance> tempMeshInstanceList;
        private NativeList<DataInstance> tempDataInstanceList;

        override protected void EndGeneration(Mesh mesh)
        {
            // the temp lists are added to Temps and will be disposed 
            // that's why they are only set to default
            if (combinationMode == CombinationMode.UnityBuiltIn)
            {
                if (tempMeshInstanceList.IsCreated)
                {
                    CombineMeshes(mesh, tempMeshInstanceList, Theme);
                    tempMeshInstanceList = default;
                }
                else
                {
                    Debug.LogError("There was no tempMeshInstanceList to combine!");
                }
            }
            else
            {
                if (tempDataInstanceList.IsCreated)
                {
                    combinationBuilder.Complete(mesh);
                    tempDataInstanceList = default;
                }
                else
                {
                    Debug.LogError("There was no tempDataInstanceList created!");
                }
            }

            if (generationType == GenerationType.FromDataUncached)
            {
                SafeDispose(ref tiles);
            }
        }

        override public void Dispose()
        {
            base.Dispose();

            SafeDispose(ref tiles);

            Theme?.Release();
            Theme = null;

            ThemePalette = null;
        }

        // collect the data for combination
        // the MeshInstance contains the data for the unity CombineInstance version
        // the DataInstance contains the data for the custom version
        abstract protected JobHandle ScheduleFillMeshInstanceList(NativeList<MeshInstance> resultList, JobHandle dependOn);
        abstract protected JobHandle ScheduleFillDataInstanceList(NativeList<DataInstance> resultList, JobHandle dependOn);

        protected JobHandle ScheduleMeshCombination(JobHandle dependOn)
        {
            if (combinationMode == CombinationMode.UnityBuiltIn)
            {
                tempMeshInstanceList = new NativeList<MeshInstance>(Allocator.TempJob);
                AddTemp(tempMeshInstanceList);
                dependOn = ScheduleFillMeshInstanceList(tempMeshInstanceList, dependOn);
            }
            else
            {
                tempDataInstanceList = new NativeList<DataInstance>(Allocator.TempJob);
                AddTemp(tempDataInstanceList);
                dependOn = ScheduleFillDataInstanceList(tempDataInstanceList, dependOn);

                if (combinationMode == CombinationMode.DeferredCombinationBuilder)
                {
                    dependOn = ScheduleDeferredCombineMeshes(tempDataInstanceList, Theme, dependOn);
                }
                else
                {
                    dependOn.Complete();
                    dependOn = ScheduleCombineMeshes(tempDataInstanceList, Theme, default);
                }
            }
            return dependOn;
        }

        /// <summary>
        /// Combines the mesh instance array int a single mesh, using the base pieces from the theme.
        /// It merges the submeshes properly (a submesh in the piece will be in the same submesh in the result mesh).
        /// </summary>
        /// <param name="mesh">The result will be generated into this</param>
        /// <param name="instanceData">Input data. NOTE: the MeshInstance structs have to be initialized, except for the mesh field of the CombineInstance struct, this will be set from the theme.</param>
        /// <param name="theme">Theme which provides the base mesh pieces.</param>
        static protected void CombineMeshes(Mesh mesh, NativeArray<MeshInstance> instanceData, TileTheme theme)
        {
            var basePieces = theme.BaseVariants;
            using (var combineList = new NativeList<CombineInstance>(instanceData.Length, Allocator.Temp))
            using (var currentList = new NativeList<CombineInstance>(instanceData.Length, Allocator.Temp))
            {
                int maxSubMeshCount = 0;
                for (int i = 0; i < instanceData.Length; ++i)
                {
                    var data = instanceData[i];

                    if (data.basePieceIndex >= 0)
                    {
                        var variants = basePieces[data.basePieceIndex].Variants;
                        var variantMesh = variants[data.variantIndex];

                        if (variantMesh != null)
                        {
                            maxSubMeshCount = Mathf.Max(maxSubMeshCount, variantMesh.subMeshCount);
                            for (int subIndex = 0; subIndex < variantMesh.subMeshCount; ++subIndex)
                            {
                                combineList.Add(new CombineInstance { transform = data.transform, mesh = variantMesh, subMeshIndex = subIndex });
                            }
                        }
                    }
                }

                CombineInstance[] submeshInstArray = new CombineInstance[maxSubMeshCount];

                int currentSubIndex = 0;
                while (combineList.Length > 0 && currentSubIndex < maxSubMeshCount)
                {
                    currentList.Clear();
                    for (int i = combineList.Length - 1; i >= 0 ; --i)
                    {
                        if (combineList[i].subMeshIndex == currentSubIndex)
                        {
                            currentList.Add(combineList[i]);
                            combineList.RemoveAtSwapBack(i);
                        }
                    }

                    var subMesh = new Mesh();
                    subMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    subMesh.CombineMeshes(currentList.ToArray(), true, true);
                    submeshInstArray[currentSubIndex] = new CombineInstance { mesh = subMesh };

                    ++currentSubIndex;
                }

                mesh.CombineMeshes(submeshInstArray, false, false);
            }
        }

        protected JobHandle ScheduleCombineMeshes(NativeArray<MeshInstance> instanceData, TileTheme theme, JobHandle dependOn)
        {
            var instanceArray = new NativeArray<DataInstance>(instanceData.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            AddTemp(instanceArray);

            for (int i = 0; i < instanceData.Length; ++i)
            {
                var data = instanceData[i];
                instanceArray[i] = new DataInstance()
                {
                    dataOffsets = theme.TileThemeCache.GetMeshDataOffset(data.basePieceIndex, data.variantIndex),
                    transform = instanceData[i].transform
                };
            }

            return ScheduleCombineMeshes(instanceArray, theme, dependOn);
        }

        protected JobHandle ScheduleCombineMeshes(NativeArray<DataInstance> instanceArray, TileTheme theme, JobHandle dependOn)
        {
            combinationBuilder = new MeshCombinationBuilder();
            AddTemp(combinationBuilder);

            combinationBuilder.Init(instanceArray, theme);
            dependOn = combinationBuilder.Start(dependOn);

            return dependOn;
        }

        protected JobHandle ScheduleDeferredCombineMeshes(NativeList<DataInstance> instanceList, TileTheme theme, JobHandle dependOn)
        {
            combinationBuilder = new MeshCombinationBuilder();
            AddTemp(combinationBuilder);

            combinationBuilder.InitDeferred(instanceList, theme);
            dependOn = combinationBuilder.Start(dependOn);

            return dependOn;
        }

        protected bool HasTilesData { get { return tiles != null && !tiles.IsDisposed; } }

        private const byte RotationMask = (byte)(PieceTransform.Rotate90 | PieceTransform.Rotate180 | PieceTransform.Rotate270);
        private const byte MirrorMask = (byte)PieceTransform.MirrorXYZ;

        static protected void MirrorMatrix(PieceTransform pieceTransform, ref float4x4 m)
        {
            byte mirror = (byte)((byte)pieceTransform & MirrorMask);
            switch (mirror)
            {
                case (byte)PieceTransform.MirrorX: m.c0.x *= -1; m.c1.x *= -1; m.c2.x *= -1; break;
                case (byte)PieceTransform.MirrorY: m.c0.y *= -1; m.c1.y *= -1; m.c2.y *= -1; break;
                case (byte)PieceTransform.MirrorZ: m.c0.z *= -1; m.c1.z *= -1; m.c2.z *= -1; break;
            }
        }

        static protected float4x4 ToRotationMatrix(PieceTransform pieceTransform)
        {
            byte rotation = (byte)((byte)pieceTransform & RotationMask);
            switch (rotation)
            {
                case (byte)PieceTransform.Rotate90: return float4x4.RotateY(math.radians(-90));
                case (byte)PieceTransform.Rotate180: return float4x4.RotateY(math.radians(-180));
                case (byte)PieceTransform.Rotate270: return float4x4.RotateY(math.radians(-270));
            }
            return float4x4.identity;
        }

        static protected bool HasFlag(PieceTransform transform, PieceTransform flag)
        {
            return (byte)(transform & flag) != 0;
        }

        static protected float4x4 CreateTransform(float3 pos, PieceTransform pieceTransform)
        {
            float4x4 transform = ToRotationMatrix(pieceTransform);

            if (HasFlag(pieceTransform, PieceTransform.MirrorX)) { MirrorMatrix(PieceTransform.MirrorX, ref transform); }
            if (HasFlag(pieceTransform, PieceTransform.MirrorY)) { MirrorMatrix(PieceTransform.MirrorY, ref transform); }

            transform.c3.x = pos.x;
            transform.c3.y = pos.y;
            transform.c3.z = pos.z;

            return transform;
        }

        /// <summary>
        /// Contains data for rendering a mesh piece. The matrix and indices are usually 
        /// set earlier from a job, then later the indices are used to set the mesh.
        /// (Since the mesh objects can't be handled inside the jobs.)
        /// These MeshInstance struct will be combined into the final mesh.
        /// </summary>
        protected struct MeshInstance
        {
            public float4x4 transform;
            public int basePieceIndex;
            public byte variantIndex;
        }
    }
}
