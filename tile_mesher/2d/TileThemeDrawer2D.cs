using UnityEngine;

namespace MeshBuilder
{
    /// <summary>
    /// component for drawing a tile data volume with a single theme
    /// </summary>
    public class TileThemeDrawer2D : TileThemeDrawerBase 
    {
        public enum LayerDrawMode
        {
            SingleLayer,
            MultiLayer
        }

        [SerializeField]
        private LayerDrawMode layerMode;
        public LayerDrawMode LayerMode { get { return layerMode; } set { layerMode = value; NeedsToRebuild(); } }

        [SerializeField]
        private int yLevel;
        public int YLevel { get { return yLevel; } set { yLevel = value; NeedsToRebuild(); } }

        override protected void InitMesher()
        {
            if (theme == null)
            {
                Debug.LogError("theme is null! drawer not initialized!");
                return;
            }

            if (theme.Is3DTheme)
            {
                Debug.LogError("theme is 3d! drawer not initialized!");
                return;
            }

            if (mesher == null)
            {
                mesher = new TileMesher2D(theme.ThemeName + "_mesher");
            }

            if (tileData.CachedTileData == null)
            {
                tileData.InitCache();
            }

            if (mesher is TileMesher2D)
            {
                (mesher as TileMesher2D).Init(tileData.CachedTileData, yLevel, themeIndex, theme);
            }
        }

    }
}