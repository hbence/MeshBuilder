using UnityEngine;

namespace MeshBuilder
{
    /// <summary>
    /// component for drawing a tile data volume with a single theme
    /// </summary>
    public class TileThemeDrawer : MonoBehaviour, System.IDisposable
    {
        [SerializeField]
        private TileDataAsset tileData;
        public TileDataAsset TileData { get { return tileData; } set { tileData = value; NeedsToRebuild(); } }
        
        [SerializeField]
        private TileTheme theme;
        public TileTheme Theme { get { return theme; } set { theme = value; NeedsToRebuild(); } }

        [SerializeField]
        private int themeIndex;
        public int ThemeIndex { get { return themeIndex; } set { themeIndex = value; NeedsToRebuild(); } }

        [SerializeField]
        private int yLevel;
        public int YLevel { get { return yLevel; } set { yLevel = value; NeedsToRebuild(); } }

        [SerializeField]
        private ThemeRenderInfo themeRender;
        public ThemeRenderInfo ThemeRender { get { return themeRender; } }
        
        private IMeshBuilder mesher;

        private bool needsToRebuild = true;

        private void Update()
        {
            if (needsToRebuild)
            {
                InitMesher();
                mesher.StartGeneration();
                needsToRebuild = false;
            }
        }

        private void InitMesher()
        {
            if (mesher != null)
            {
                if (mesher is TileMesher2D && theme.Is3DTheme)
                {
                    (mesher as TileMesher2D).Dispose();
                    mesher = null;
                }
                if (mesher is TileMesher3D && theme.Is2DTheme)
                {
                    (mesher as TileMesher3D).Dispose();
                    mesher = null;
                }
            }

            if (mesher == null)
            {
                if (theme.Is2DTheme)
                {
                    mesher = new TileMesher2D(theme.ThemeName + "_mesher");
                }
                else if (theme.Is3DTheme)
                {
                    mesher = new TileMesher3D(theme.ThemeName + "_mesher");
                }
            }

            if (tileData.CachedTileData == null)
            {
                tileData.InitCache();
            }

            if (mesher is TileMesher2D)
            {
                (mesher as TileMesher2D).Init(tileData.CachedTileData, yLevel, themeIndex, theme);
            }
            else if (mesher is TileMesher3D)
            {
                //TODO: implement it!
                Debug.LogError("NOT IMPLEMENTED!");
            }
        }

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
                if (mesher is TileMesher2D) (mesher as TileMesher2D).Dispose();
                if (mesher is TileMesher3D) (mesher as TileMesher3D).Dispose();
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

        [System.Serializable]
        public class ThemeRenderInfo
        {
            [SerializeField]
            private bool hide = false;

            [SerializeField]
            private SubMeshInfo[] subMeshInfo;

            // this one should also be per submesh, but it's not used
            // for anything for now, so change it when it's needed
            private MaterialPropertyBlock prop;

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
                private Material material;
                public Material Material { get { return material; } }

                [SerializeField]
                private bool recieveShadows = false;
                public bool RecieveShadows { get { return recieveShadows; } }

                [SerializeField]
                private UnityEngine.Rendering.ShadowCastingMode castShadows;
                public UnityEngine.Rendering.ShadowCastingMode CastShadows { get { return castShadows; } }
            }
        }
    }
}