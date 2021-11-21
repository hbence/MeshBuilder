using UnityEngine;

namespace MeshBuilder
{
    [ExecuteAlways]
    public class SplineControlPointsFromChildren : MonoBehaviour
    {
        [SerializeField] private SplineComponent spline = null;

        private Vector3[] controlPointsBuffer;

        private void OnEnable()
        {
            UpdateControlPointsFromChildren();
        }

        void Update()
        {
            if (DoesBufferNeedsUpdate)
            {
                UpdateControlPointsFromChildren();
            }
        }

        private void OnTransformChildrenChanged()
        {
            UpdateControlPointsFromChildren();
        }

        private void UpdateControlPointsFromChildren()
        {
            if (spline == null)
            {
                return;
            }

            if (transform.childCount < 2)
            {
                return;
            }

            if (controlPointsBuffer == null || controlPointsBuffer.Length != transform.childCount)
            {
                controlPointsBuffer = new Vector3[transform.childCount];
            }

            for (int i = 0; i < transform.childCount; ++i)
            {
                controlPointsBuffer[i] = transform.GetChild(i).position;
            }

            if (DoesSplineNeedsUpdate)
            {
                spline.ControlPoints = controlPointsBuffer;
            }
        }

        private bool DoesSplineNeedsUpdate
        {
            get
            {
                if (controlPointsBuffer == null || controlPointsBuffer.Length < 2)
                {
                    return false;
                }

                if (spline.ControlPoints == null || spline.ControlPoints.Length != controlPointsBuffer.Length)
                {
                    return true;
                }

                for (int i = 0; i < spline.ControlPoints.Length; ++i)
                {
                    if (spline.ControlPoints[i] != controlPointsBuffer[i])
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private bool DoesBufferNeedsUpdate
        {
            get
            {
                if (controlPointsBuffer == null || controlPointsBuffer.Length != transform.childCount)
                {
                    return true;
                }

                for (int i = 0; i < transform.childCount; ++i)
                {
                    if (transform.GetChild(i).position != controlPointsBuffer[i])
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}