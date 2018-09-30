using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    public class TileTheme3DComponent : MonoBehaviour
    {
        // theme name is only used for debugging
        public string themeName;
        public TileTheme3D theme;

        private void Awake()
        {
            theme.Init();
        }
    }
}
