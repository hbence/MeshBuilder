using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;

namespace MeshBuilder
{
    public class NineScaleMeshBuilder : Builder
    {
        private MeshCombinationBuilder combinationBuilder = new MeshCombinationBuilder();

        public void Init(NineScale nineScale, Vector3 targetSize)
        {
            nineScale.Recalculate(Vector3.zero, Quaternion.identity, targetSize);

            var meshParts = nineScale.GatherParts();
            if (meshParts == null || meshParts.Length == 0)
            {
                Debug.LogError("couldn't gather nine scale parts for a mesh!");
                return;
            }

            Mesh[] meshes = new Mesh[meshParts.Length];
            Matrix4x4[] matrices = new Matrix4x4[meshParts.Length];
            for (int i = 0; i < meshParts.Length; ++i)
            {
                meshes[i] = meshParts[i].Mesh;
                matrices[i] = meshParts[i].Matrix;
            }

            combinationBuilder.Init(meshes, matrices);

            Inited();
        }

        protected override JobHandle StartGeneration(JobHandle dependOn)
        {
            combinationBuilder.Start(dependOn);

            return dependOn;
        }

        protected override void EndGeneration(Mesh mesh)
        {
            combinationBuilder.Complete(mesh);
        }

        public override void Dispose()
        {
            combinationBuilder.Dispose();
            base.Dispose();
        }
    }
}
