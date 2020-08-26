using UnityEngine;
using Unity.Mathematics;

namespace MeshBuilder
{
    public class CrossSectionFromChildren : MonoBehaviour
    {
        private void OnDrawGizmosSelected()
        {
            if (transform.childCount > 0)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(transform.GetChild(0).position, 0.3f);
                for (int i = 1; i < transform.childCount; ++i)
                {
                    Gizmos.DrawSphere(transform.GetChild(i).position, 0.3f);
                    Gizmos.DrawLine(transform.GetChild(i).position, transform.GetChild(i - 1).position);
                }
            }
        }

        public SplineMeshBuilder.CrossSectionData CrossSection
        {
            get
            {
                if (transform.childCount > 0)
                {
                    float3[] positions = new float3[transform.childCount];
                    for (int i = 0; i < transform.childCount; ++i)
                    {
                        positions[i] = transform.GetChild(i).localPosition;
                    }
                    return new SplineMeshBuilder.CrossSectionData(positions);
                }
                return null;
            }
        }
    }
}
