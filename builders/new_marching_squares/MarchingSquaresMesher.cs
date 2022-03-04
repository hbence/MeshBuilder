using System;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;


using static MeshBuilder.Utils;
using static MeshBuilder.MarchingSquaresMesher;
using MeshBuffer = MeshBuilder.MeshData.Buffer;

namespace MeshBuilder.New
{
    public class MarchingSquaresMesher : Builder
    {
        private const uint DefMeshDataBufferFlags = (uint)MeshBuffer.Vertex | (uint)MeshBuffer.Triangle | (uint)MeshBuffer.Normal;

        public float CellSize { get; private set; }

        // mraching squares data
        public Data DistanceData { get; private set; }
        public int ColNum => DistanceData.ColNum;
        public int RowNum => DistanceData.RowNum;

        // mesh settings
        public bool UseCullingData = true;
        public bool UseHeightData = true;
        public bool GenerateUVs = true;
        public bool GenerateNormals = true;

        public float HeightScale = 1f;
        public float UScale = 1f;
        public float VScale = 1f;
        public bool NormalizeUV = true;

        public uint MeshDataBufferFlags { get; set; } = DefMeshDataBufferFlags;


        override protected JobHandle StartGeneration(JobHandle lastHandle)
        {
            return lastHandle;
        }

        protected override void EndGeneration(Mesh mesh)
        {

        }

    }
}
