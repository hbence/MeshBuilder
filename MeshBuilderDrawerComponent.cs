using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    public class MeshBuilderDrawerComponent : MonoBehaviour
    {
        public bool IsVisible { get; set; } = true;
        public List<MeshBuilderDrawer> Drawers { get; private set; } 

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
            if (Drawers == null)
            {
                Drawers = new List<MeshBuilderDrawer>();
                Drawers.Add(drawer);
            }
            else
            {
                if (!DoesContain(drawer))
                {
                    Drawers.Add(drawer);
                }
            }
        }

        public void RemoveDrawer(MeshBuilderDrawer drawer)
        {
            if (Drawers != null)
            {
                Drawers.Remove(drawer);
            }
        }

        public void RemoveAll()
        {
            Drawers.Clear();
        }

        public bool DoesContain(MeshBuilderDrawer drawer)
        {
            if (Drawers != null)
            {
                return Drawers.Contains(drawer);
            }
            return false;
        }

        private void DrawWithCamera(Camera camera)
        {
            if (IsVisible)
            {
                if (camera != null && DoesAllowCamera(camera) && Drawers != null)
                {
                    foreach (var drawer in Drawers)
                    {
                        drawer.Render(camera, transform, gameObject.layer);
                    }
                }
            }
        }

        private static bool DoesAllowCamera(Camera cam)
        {
            return cam.cameraType == CameraType.Game || cam.cameraType == CameraType.SceneView;
        }
    }
}
