using UnityEngine;

namespace MeshBuilder
{
    /// <summary>
    /// component for drawing a tile data volume with a single theme
    /// </summary>
    public abstract class TileThemeDrawerBase : MonoBehaviour, System.IDisposable
    {
        [SerializeField]
        protected TileDataAsset tileData;
        public TileDataAsset TileData { get { return tileData; } set { tileData = value; NeedsToRebuild(); } }

        [SerializeField]
        protected TileTheme theme;
        public TileTheme Theme { get { return theme; } set { theme = value; NeedsToRebuild(); } }

        [SerializeField]
        protected int themeIndex;
        public int ThemeIndex { get { return themeIndex; } set { themeIndex = value; NeedsToRebuild(); } }

        [SerializeField]
        protected ThemeRenderInfo themeRender;
        public ThemeRenderInfo ThemeRender { get { return themeRender; } }

        protected IMeshBuilder mesher;

        private bool needsToRebuild = true;

        virtual protected void Update()
        {
            if (needsToRebuild)
            {
                InitMesher();
                mesher.StartGeneration();
                needsToRebuild = false;
            }
        }

        abstract protected void InitMesher();

        private void LateUpdate()
        {
            if (mesher != null && mesher.IsGenerating)
            {
                mesher.EndGeneration();
            }
        }

        private void OnEnable()
        {
            Camera.onPreCull -= DrawWithCamera;
            Camera.onPreCull += DrawWithCamera;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= DrawWithCamera;
        }

        private void DrawWithCamera(Camera camera)
        {
            if (camera)
            {
                Render(camera);
            }
        }

        private void Render(Camera cam)
        {
            if (mesher != null && mesher.Mesh != null)
            {
                ThemeRender.Draw(transform, mesher.Mesh, cam, gameObject.layer);
            }
        }

        public void Dispose()
        {
            if (mesher != null)
            {
                mesher.Dispose();
                mesher = null;
            }

            if (tileData != null)
            {
                tileData.Dispose();
            }
        }

        public void NeedsToRebuild()
        {
            needsToRebuild = true;
        }

        public bool IsGenerating { get { return mesher != null && mesher.IsGenerating; } }
        public Mesh Mesh { get { return mesher != null ? mesher.Mesh : null; } }

        [System.Serializable]
        public class ThemeRenderInfo
        {
            [SerializeField]
            private bool hide = false;

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