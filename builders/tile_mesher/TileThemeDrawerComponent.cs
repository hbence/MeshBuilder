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

        private TileMesher3D.Settings settings3D;
        public TileMesher3D.Settings Settings3D { get { return settings3D; } set { settings3D = value; NeedsToRebuild(); } }

        protected Volume<Tile.Data> tileData;
        public Volume<Tile.Data> TileData { get { return tileData; } set { tileData = value; NeedsToRebuild(); } }

        protected Builder Mesher { get; set; }

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
                if (Mesher != null)
                {
                    Mesher.Start();
                }
            }
        }

        private void LateUpdate()
        {
            if (Mesher != null && Mesher.IsGenerating)
            {
                Mesher.Complete();
            }
        }

        public void InitTileData()
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

        public void InitMesher()
        {
            if (theme == null)
            {
                Debug.LogError("theme is null! drawer not initialized!");
                return;
            }

            if (theme.Is3DTheme)
            {
                if (Mesher == null) { Mesher = new TileMesher3D(); }

                TileMesher3D mesher3D = Mesher as TileMesher3D;
                if (mesher3D != null)
                {
                    mesher3D.Init(tileData, themeIndex, theme, cellSize, Settings3D);
                }
                else
                {
                    Debug.LogError("Mesher is not 3D!");
                }
            }
            else
            {
                if (Mesher == null) { Mesher = new TileMesher2D(); }

                TileMesher2D mesher2D = Mesher as TileMesher2D;
                if (mesher2D != null)
                {
                    mesher2D.Init(tileData, yLayer, themeIndex, theme);
                }
                else
                {
                    Debug.LogError("Mesher is not 2D!");
                }
            }

            drawer.Mesher = Mesher;
        }

        public void StartGeneration()
        {
            if (Mesher != null)
            {
                Mesher.Start();
            }
        }

        public void NeedsToRebuild()
        {
            needsToRebuild = true;
        }

        public void Dispose()
        {
            if (Mesher != null)
            {
                Mesher.Dispose();
                Mesher = null;
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

        public bool IsGenerating { get { return Mesher != null && Mesher.IsGenerating; } }
        public Mesh Mesh { get { return Mesher != null ? Mesher.Mesh : null; } }
    }
}