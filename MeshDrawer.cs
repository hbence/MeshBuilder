using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    public class MeshDrawer
    {
        [SerializeField] protected RenderInfo renderer = null;
        public RenderInfo Renderer { get => renderer; set => renderer = value; }

        public Mesh Mesh { get; set; }

        public MeshDrawer() { }

        public MeshDrawer(RenderInfo renderInfo)
        {
            Renderer = renderInfo;
        }

        public void Render(Camera cam, Transform transform, int layer)
        {
            renderer.Draw(transform, Mesh, cam, layer);
        }

        [System.Serializable]
        public class RenderInfo
        {
            [SerializeField]
            private bool hide = false;
            public bool Hide { get { return hide; } set { hide = value; } }

            [SerializeField]
            private SubMeshInfo[] subMeshInfo = null;

            // this one should also be per submesh, but it's not used
            // for anything for now, so change it when it's needed
            private MaterialPropertyBlock prop = default;

            public void Draw(Transform transform, Mesh mesh, Camera cam, int layer)
            {
                Draw(transform.localToWorldMatrix, mesh, cam, layer);
            }

            public void Draw(Matrix4x4 matrix, Mesh mesh, Camera cam, int layer)
            {
                if (!hide && subMeshInfo != null && subMeshInfo.Length > 0)
                {
                    for (int i = 0; i < mesh.subMeshCount; ++i)
                    {
                        int smIndex = Mathf.Min(i, subMeshInfo.Length - 1);
                        var smInfo = subMeshInfo[smIndex];

                        if (smInfo.Material != null)
                        {
                            Graphics.DrawMesh(mesh, matrix, smInfo.Material, layer, cam, i, prop, smInfo.CastShadows, smInfo.RecieveShadows);
                        }
                    }
                }
            }

            public void DrawInstanced(List<Matrix4x4> matrices, Mesh mesh, Camera cam, int layer)
            {
                if (!hide && subMeshInfo != null && subMeshInfo.Length > 0)
                {
                    for (int i = 0; i < mesh.subMeshCount; ++i)
                    {
                        int smIndex = Mathf.Min(i, subMeshInfo.Length - 1);
                        var smInfo = subMeshInfo[smIndex];

                        if (smInfo.Material != null)
                        {
                            Graphics.DrawMeshInstanced(mesh, i, smInfo.Material, matrices, prop, smInfo.CastShadows, smInfo.RecieveShadows, layer, cam);
                        }
                    }
                }
            }

            public void DrawInstanced(Matrix4x4[] matrices, Mesh mesh, Camera cam, int layer)
            {
                if (!hide && subMeshInfo != null && subMeshInfo.Length > 0)
                {
                    for (int i = 0; i < mesh.subMeshCount; ++i)
                    {
                        int smIndex = Mathf.Min(i, subMeshInfo.Length - 1);
                        var smInfo = subMeshInfo[smIndex];

                        if (smInfo.Material != null)
                        {
                            Graphics.DrawMeshInstanced(mesh, i, smInfo.Material, matrices, matrices.Length, prop, smInfo.CastShadows, smInfo.RecieveShadows, layer, cam);
                        }
                    }
                }
            }

            [System.Serializable]
            public class SubMeshInfo
            {
                [SerializeField]
                private Material material = null;
                public Material Material { get { return material; } }

                [SerializeField]
                private bool recieveShadows = false;
                public bool RecieveShadows { get { return recieveShadows; } }

                [SerializeField]
                private UnityEngine.Rendering.ShadowCastingMode castShadows = UnityEngine.Rendering.ShadowCastingMode.Off;
                public UnityEngine.Rendering.ShadowCastingMode CastShadows { get { return castShadows; } }
            }
        }
    }
}
