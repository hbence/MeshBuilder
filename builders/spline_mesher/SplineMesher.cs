using UnityEngine;

using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    using static Utils;

    public class SplineMesher : Builder
    {
        override protected JobHandle StartGeneration(JobHandle dependOn)
        {
            return dependOn;
        }

        override protected void EndGeneration(Mesh mesh)
        {

        }

        private struct NativeCrossSectionData
        {
            private NativeArray<float3> positions;
            private NativeArray<float> uCoordinates;

            public NativeCrossSectionData(NativeCrossSectionData data, Allocator allocator)
            {
                positions = new NativeArray<float3>(data.positions, allocator);
                if (data.uCoordinates != null)
                {
                    uCoordinates = new NativeArray<float>(data.uCoordinates, allocator);
                }
                else
                {
                    uCoordinates = default;
                }
            }

            public void Dispose()
            {
                SafeDispose(ref positions);
                SafeDispose(ref uCoordinates);
            }
        }
    }

}