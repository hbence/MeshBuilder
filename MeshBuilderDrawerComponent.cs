using UnityEngine;

namespace MeshBuilder
{
    public class MeshBuilderDrawerComponent : MonoBehaviour
    {
        private MeshBuilderDrawer[] drawers;

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
            if (!DoesContain(drawer))
            {
                int oldLength = drawers == null ? 0 : drawers.Length;
                var newDrawers = new MeshBuilderDrawer[oldLength + 1];
                if (oldLength > 0)
                {
                    System.Array.Copy(drawers, newDrawers, drawers.Length);
                }
                newDrawers[newDrawers.Length - 1] = drawer;
                drawers = newDrawers;
            }
        }

        public void RemoveDrawer(MeshBuilderDrawer drawer)
        {
            if (drawers != null && drawers.Length > 0)
            {
                if (drawers.Length == 1)
                {
                    if (drawers[0] == drawer)
                    {
                        drawers = null;
                        return;
                    }
                }
                else
                {
                    var newDrawers = new MeshBuilderDrawer[drawers.Length - 1];
                    int sourceIndex = 0;
                    for (int i = 0; i < newDrawers.Length; ++i)
                    {
                        if (drawers[sourceIndex] == drawer)
                        {
                            ++sourceIndex;
                        }

                        newDrawers[i] = drawers[sourceIndex];
                        ++sourceIndex;
                    }
                }
            }
        }

        public bool DoesContain(MeshBuilderDrawer drawer)
        {
            if (drawers != null && drawers.Length > 0)
            {
                foreach(var elem in drawers)
                {
                    if (elem == drawer)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void DrawWithCamera(Camera camera)
        {
            if (camera && drawers != null && drawers.Length > 0)
            {
                foreach(var drawer in drawers)
                {
                    drawer.Render(camera, transform, gameObject.layer);
                }
            }
        }

    }
}
