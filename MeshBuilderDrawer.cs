using UnityEngine;
using Unity.Jobs;

namespace MeshBuilder
{
    [System.Serializable]
    public class MeshBuilderDrawer : System.IDisposable
    {
        [SerializeField]
        protected RenderInfo renderer;
        public RenderInfo Renderer { get => renderer; set => renderer = value; }

        public Builder MeshBuilder { get; private set; }
        public T Get<T>() where T : Builder { return MeshBuilder as T; }
        public Mesh Mesh { get; private set; }

        public MeshBuilderDrawer(RenderInfo info, Builder builder)
        {
            MeshBuilder = builder;
            renderer = info;
            Mesh = new Mesh();
        }

        public void StartBuilder(JobHandle dependOn = default)
        {
            if (MeshBuilder.IsInitialized)
            {
                MeshBuilder.Start(dependOn);
            }
            else
            {
                Debug.LogError("MeshBuilder was not initialized!");
            }
        }

        public void CompleteBuilder()
        {
            if (MeshBuilder.IsGenerating)
            {
                MeshBuilder.Complete(Mesh);
            }
            else
            {
                Debug.LogWarning("MeshBuilder was not generating!");
            }
        }

        public bool IsBuilderGenerating { get => MeshBuilder.IsGenerating; }

        public void Render(Camera cam, Transform transform, int layer)
        {
            renderer.Draw(transform, Mesh, cam, layer);
        }

        public void Dispose()
        {
            MeshBuilder.Dispose();
        }

        static public MeshBuilderDrawer Create<T>(RenderInfo info) where T : Builder, new()
        {
            return new MeshBuilderDrawer(info, new T());
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
