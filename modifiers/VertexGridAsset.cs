using UnityEngine;

using VertexGrid = MeshBuilder.LatticeGridComponent.VertexGrid;

namespace MeshBuilder
{
    /// <summary>
    /// wrapper class to handle asset import/export
    /// </summary>
    [System.Serializable]
    public class VertexGridAsset : ScriptableObject
    {
        [SerializeField]
        private VertexGrid grid;
        public VertexGrid Grid { get { return grid; } set { grid = value; } }
    }
}
