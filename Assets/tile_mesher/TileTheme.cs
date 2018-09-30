using System;
using UnityEngine;

namespace MeshBuilder
{
    /// <summary>
    /// Abstract base class to handle getting the correct mesh pieces. It has a 'meshes' array which contains the arrays of piece variations,
    /// the order of the pieces should match the order in the enum 'ElemType'.
    /// </summary>
    /// <typeparam name="ElemType">This should be an enum, which has members that match the mesh elems.</typeparam>
    [System.Serializable]
    public abstract class TileTheme<ElemType> where ElemType : struct, System.IConvertible
    {
        [System.NonSerialized]
        protected Mesh[][] meshes;

        public void Init() { if (meshes == null) { meshes = CreateMeshesArray(); } }

        abstract protected Mesh[][] CreateMeshesArray();

        public Mesh GetMesh(ElemType elem, int index)
        {
            var array = GetArray(elem);
            return array[index % array.Length];
        }

        public Mesh[] GetArray(ElemType elem)
        {
            return meshes[Convert.ToInt32(elem) % meshes.Length];
        }

        public bool HasElem(ElemType elem)
        {
            var array = GetArray(elem);
            return array != null && array.Length > 0;
        }
    }
}