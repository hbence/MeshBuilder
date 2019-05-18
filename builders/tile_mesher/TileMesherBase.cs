﻿using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static MeshBuilder.Utils;

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
        
        override protected void EndGeneration(Mesh mesh)
        {
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
                                var combineInstance = data.instance;
                                combineInstance.mesh = variantMesh;
                                combineInstance.subMeshIndex = subIndex;
                                combineList.Add(combineInstance);
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
        
        protected bool HasTilesData { get { return tiles != null && !tiles.IsDisposed; } }

        /// <summary>
        /// Contains data for rendering a mesh piece. The matrix and indices are usually 
        /// set earlier from a job, then later the indices are used to set the mesh.
        /// (Since the mesh objects can't be handled inside the jobs.)
        /// These MeshInstance struct will be combined into the final mesh.
        /// </summary>
        protected struct MeshInstance
        {
            public CombineInstance instance;
            public int basePieceIndex;
            public byte variantIndex;
        }
    }
}
