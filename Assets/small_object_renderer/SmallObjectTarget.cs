using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using System;

namespace MeshBuilder.SmallObject
{
    [RequireComponent(typeof(GameObjectEntity))]
    public class SmallObjectTarget : MonoBehaviour
    {
        // this is the mesh which will be used to generate the points for 
        // small object placement
        [SerializeField]
        private Mesh mesh;

        // if the mesh will be generated later, a mesh filter can be used
        // to get the mesh automatically when it is ready
        [SerializeField]
        private bool initMeshFromMeshFilter;

        // the object is dynamic, its transform will need to be updated
        [SerializeField]
        private bool dynamicObject;

       // public ISmallObjectPlacementFilter[] filters;

        // these are the small objects which will be placed for the target
        [SerializeField]
        private SmallObject[] smallObjects;

        private GameObjectEntity gameObjectEntity;
        private MeshFilter meshFilter;
        private bool wasMeshInited;

        void Start()
        {
            if (initMeshFromMeshFilter)
            {
                meshFilter = GetComponent<MeshFilter>();
                if (meshFilter == null)
                {
                    Debug.LogError("GameObject requires meshFilter!");
                }
                else if (meshFilter.sharedMesh != null)
                {
                    mesh = meshFilter.sharedMesh;
                    wasMeshInited = true;
                }
            }

            gameObjectEntity = GetComponent<GameObjectEntity>();

            InitializeEntity();
        }

        private void Update()
        {
            if (!wasMeshInited && initMeshFromMeshFilter)
            {
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    Mesh = meshFilter.sharedMesh;
                    wasMeshInited = true;
                }
            }
        }

        private void InitializeEntity()
        {
            var entityManager = gameObjectEntity.EntityManager;
            var entity = gameObjectEntity.Entity;

            entityManager.AddSharedComponentData(entity, new TargetObject
            {
                mesh = mesh,
                dynamic = dynamicObject,
                filters = null,
                smallObjects = smallObjects
            });

            entityManager.AddComponentData(entity, new TransformMatrix { });
            entityManager.AddComponentData(entity, new Position { });
            entityManager.AddComponentData(entity, new Rotation { });

            if (dynamicObject)
            {
                entityManager.AddComponentData(entity, new CopyTransformFromGameObject { });
            }
            else
            {
                entityManager.AddComponentData(entity, new CopyInitialTransformFromGameObject { });
            }
        }
        
        private void MeshWasUpdated()
        {
            var entityManager = gameObjectEntity.EntityManager;
            var entity = gameObjectEntity.Entity;
            var targetComponent = entityManager.GetSharedComponentData<TargetObject>(entity);
            targetComponent.mesh = mesh;
            entityManager.SetSharedComponentData(entity, targetComponent);
            
            if (!entityManager.HasComponent(entity, typeof(TargetObjectMeshChanged)))
            {
                entityManager.AddComponentData(entity, new TargetObjectMeshChanged { });
            }
        }

        public Mesh Mesh
        {
            get { return mesh; }
            set
            {
                mesh = value;
                MeshWasUpdated();
            }
        }

    }
}
