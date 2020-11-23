using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace MeshBuilder
{
    using static MeshBuilder.MeshDrawer;
    using static NineScale;

    [ExecuteInEditMode]
    public class NineScaleDrawer : MonoBehaviour
    {
        [SerializeField] private NineScale nineScale = null;
        [SerializeField] private RenderInfo renderInfo = null;

        [SerializeField] private bool forceRecalculation = false;

        private bool instancedRendering = false;
        public bool InstancedRendering
        {
            get => instancedRendering;
            set
            {
                if (instancedRendering && !value)
                {
                    instanceCache.ClearGroups();
                }
                else if (!instancedRendering && value)
                {
                    instanceCache.Update(nineScale);
                }

                instancedRendering = value;
                RegisterRenderingFunction();
            }
        }

        private InstancedMeshCache instanceCache;

        private void OnEnable()
        {
            RegisterRenderingFunction();

            nineScale.Recalculate(transform.position, transform.rotation, transform.lossyScale);

            if (instanceCache == null)
            {
                instanceCache = new InstancedMeshCache();
            }
        }

        private void RegisterRenderingFunction()
        {
            Camera.onPreCull -= DrawWithCamera;
            Camera.onPreCull -= DrawWithCameraInstanced;

            if (instancedRendering)
            {
                Camera.onPreCull += DrawWithCameraInstanced;
            }
            else
            {
                Camera.onPreCull += DrawWithCamera;
            }
        }

        private void OnDisable()
        {
            Camera.onPreCull -= DrawWithCamera;
            Camera.onPreCull -= DrawWithCameraInstanced;
        }
         
        private void Update()
        {
            bool shouldRecalculate = nineScale.ShouldRecalculate(transform.position, transform.rotation, transform.lossyScale);
            if (forceRecalculation || shouldRecalculate)
            {
                nineScale.Recalculate(transform.position, transform.rotation, transform.lossyScale);

                if (instancedRendering)
                {
                    instanceCache.Update(nineScale);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            var m = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = m;
        }

        private void DrawWithCamera(Camera camera)
        {
            if (DoesAllowCamera(camera))
            {
                if (nineScale != null && renderInfo != null)
                {
                    nineScale.Render(renderInfo, camera, gameObject.layer);
                }
            }
        }

        private void DrawWithCameraInstanced(Camera camera)
        {
            if (DoesAllowCamera(camera))
            {
                if (nineScale != null && renderInfo != null)
                {
                    instanceCache.Render(renderInfo, camera, gameObject.layer);
                }
            }
        }

        private static bool DoesAllowCamera(Camera cam)
        {
            return cam.cameraType == CameraType.Game || cam.cameraType == CameraType.SceneView;
        }

        private class InstancedMeshCache
        {
            private PartGroup[] Groups;
            public bool HasGroups => Groups != null;

            public void ClearGroups()
            {
                Groups = null;
            }

            public void Update(NineScale nineScale)
            {
                Part[] parts = nineScale.GatherParts();
                if (parts != null)
                {
                    System.Array.Sort(parts, (Part a, Part b) => { return a.Mesh.GetHashCode() - b.Mesh.GetHashCode(); });

                    List<PartGroup> groups = new List<PartGroup>();
                    List<Matrix4x4> matrices = new List<Matrix4x4>();

                    PartGroup currentGroup = new PartGroup();
                    groups.Add(currentGroup);
                    currentGroup.Mesh = parts[0].Mesh;
                    matrices.Add(parts[0].Matrix);

                    for (int i = 1; i < parts.Length; ++i)
                    {
                        var part = parts[i];
                        if (currentGroup.Mesh == part.Mesh)
                        {
                            matrices.Add(part.Matrix);
                        }
                        else
                        {
                            currentGroup.Matrix = matrices.ToArray();
                            matrices.Clear();

                            currentGroup = new PartGroup();
                            groups.Add(currentGroup);
                            currentGroup.Mesh = part.Mesh;
                            matrices.Add(part.Matrix);
                        }
                    }

                    currentGroup.Matrix = matrices.ToArray();

                    Groups = groups.ToArray();
                }
                else
                {
                    Groups = null;
                }
            }

            public void Render(RenderInfo renderInfo, Camera camera, int layer)
            {
                if (Groups != null)
                {
                    foreach (var group in Groups)
                    {
                        group.Render(renderInfo, camera, layer);
                    }
                }
            }
        }
    }
}
