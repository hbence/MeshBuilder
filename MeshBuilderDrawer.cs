using UnityEngine;

namespace MeshBuilder
{
    [System.Serializable]
    public class MeshBuilderDrawer
    {
        [SerializeField]
        protected RenderInfo renderer;
        public RenderInfo Renderer { get { return renderer; } set { renderer = value; } }

        public IMeshBuilder Mesher { get; set; }

        public MeshBuilderDrawer(RenderInfo info)
        {
            renderer = info; 
        }

        public void Render(Camera cam, Transform transform, int layer)
        {
            if (Mesher != null && Mesher.Mesh != null)
            {
                renderer.Draw(transform, Mesher.Mesh, cam, layer);
            }
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
                if (!hide && subMeshInfo != null && subMeshInfo.Length > 0)
                {
                    for (int i = 0; i < mesh.subMeshCount; ++i)
                    {
                        int smIndex = Mathf.Min(i, subMeshInfo.Length - 1);
                        var smInfo = subMeshInfo[smIndex];

                        if (smInfo.Material != null)
                        {
                            Graphics.DrawMesh(mesh, transform.localToWorldMatrix, smInfo.Material, layer, cam, i, prop, smInfo.CastShadows, smInfo.RecieveShadows);
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
