using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    public class MeshBuilderDrawerComponent : MonoBehaviour
    {
        private List<MeshBuilderDrawer> drawers;

        private void OnEnable()
        {
            Camera.onPreCull -= DrawWithCamera;
            Camera.onPreCull += DrawWithCamera;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= DrawWithCamera;
        }

        public void AddDrawer(MeshBuilderDrawer drawer)
        {
            if (drawers == null)
            {
                drawers = new List<MeshBuilderDrawer>();
                drawers.Add(drawer);
            }
            else
            {
                if (!DoesContain(drawer))
                {
                    drawers.Add(drawer);
                }
            }
        }

        public void RemoveDrawer(MeshBuilderDrawer drawer)
        {
            if (drawers != null)
            {
                drawers.Remove(drawer);
            }
        }

        public bool DoesContain(MeshBuilderDrawer drawer)
        {
            if (drawers != null)
            {
                return drawers.Contains(drawer);
            }
            return false;
        }

        private void DrawWithCamera(Camera camera)
        {
            if (camera && drawers != null)
            {
                foreach(var drawer in drawers)
                {
                    drawer.Render(camera, transform, gameObject.layer);
                }
            }
        }

    }
}
