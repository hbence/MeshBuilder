using UnityEngine;

namespace MeshBuilder
{
    /// <summary>
    /// component for drawing a tile data volume with a single theme
    /// </summary>
    public class TileThemeDrawerComponent : MeshBuilderDrawerComponent, System.IDisposable
    {
        [SerializeField]
        protected TileDataAsset tileDataAsset;
        public TileDataAsset TileDataAsset { get { return tileDataAsset; } set { tileDataAsset = value; NeedsToRebuild(); } }
        private Volume<Tile.Data> cachedTileData;

        [SerializeField]
        protected TileTheme theme = null;
        public TileTheme Theme { get { return theme; } set { theme = value; NeedsToRebuild(); } }

        [SerializeField]
        protected int themeIndex = 1;
        public int ThemeIndex { get { return themeIndex; } set { themeIndex = value; NeedsToRebuild(); } }

        [SerializeField]
        private Vector3 cellSize = Vector3.one;
        public Vector3 CellSize { get { return cellSize; } set { cellSize = value; NeedsToRebuild(); } }

        [SerializeField]
        private int yLayer = 0;
        public int YLayer { get { return yLayer; } set { yLayer = value; NeedsToRebuild(); } }

        [SerializeField]
        private MeshBuilderDrawer.RenderInfo renderInfo = null;
        private MeshBuilderDrawer drawer;

        protected Volume<Tile.Data> tileData;
        public Volume<Tile.Data> TileData { get { return tileData; } set { tileData = value; NeedsToRebuild(); } }

        protected IMeshBuilder mesher;

        // this is serialized so it can be triggered from the editor
        [SerializeField]
        private bool needsToRebuild = true;

        private void Awake()
        {
            drawer = new MeshBuilderDrawer(renderInfo);
            AddDrawer(drawer);
        }

        private void Update()
        {
            if (needsToRebuild)
            {
                needsToRebuild = false;

                InitTileData();
                InitMesher();
                if (mesher != null)
                {
                    mesher.StartGeneration();
                }
            }
        }

        private void LateUpdate()
        {
            if (mesher != null && mesher.IsGenerating)
            {
                mesher.EndGeneration();
            }
        }

        private void InitTileData()
        {
            if (tileData == null)
            {
                if (cachedTileData == null && tileDataAsset != null)
                {
                    cachedTileData = tileDataAsset.CreateTileDataVolume();
                }

                tileData = cachedTileData;
            }
        }

        private void InitMesher()
        {
            if (theme == null)
            {
                Debug.LogError("theme is null! drawer not initialized!");
                return;
            }

            if (theme.Is3DTheme)
            {
                if (mesher == null)
                {
                    mesher = new TileMesher3D(theme.ThemeName + "_mesher");
                }

                TileMesher3D mesher3D = mesher as TileMesher3D;
                if (mesher3D != null)
                {
                    mesher3D.Init(tileData, themeIndex, theme, cellSize);
                }
            }
            else
            {
                if (mesher == null)
                {
                    mesher = new TileMesher2D(theme.ThemeName + "_mesher");
                }

                TileMesher2D mesher2D = mesher as TileMesher2D;
                if (mesher2D != null)
                {
                    mesher2D.Init(tileData, yLayer, themeIndex, theme);
                }
            }

            drawer.Mesher = mesher;
        }

        public void NeedsToRebuild()
        {
            needsToRebuild = true;
        }

        public void Dispose()
        {
            if (mesher != null)
            {
                mesher.Dispose();
                mesher = null;
            }

            if (cachedTileData != null)
            {
                cachedTileData.Dispose();
                cachedTileData = null;
            }

            tileData = null;
        }

        private void OnDestroy()
        {
            Dispose();
        }

        public bool IsGenerating { get { return mesher != null && mesher.IsGenerating; } }
        public Mesh Mesh { get { return mesher != null ? mesher.Mesh : null; } }
    }
}