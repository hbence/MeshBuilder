using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace MeshBuilder
{
    using static MeshBuilder.MeshDrawer;

    [ExecuteInEditMode]
    public class NineScaleDrawer : MonoBehaviour
    {
        [SerializeField] private NineScale nineScale = null;
        [SerializeField] private RenderInfo renderInfo = null;

        public bool debug = false;

        private void OnEnable()
        {
            Camera.onPreCull -= DrawWithCamera;
            Camera.onPreCull += DrawWithCamera;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= DrawWithCamera;
        }

        private void Update()
        {
            if (debug)
            {
                nineScale.Recalculate(transform.position, transform.rotation, transform.lossyScale);
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

        private static bool DoesAllowCamera(Camera cam)
        {
            return cam.cameraType == CameraType.Game || cam.cameraType == CameraType.SceneView;
        }
    }
}
