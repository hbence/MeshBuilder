using System;
using UnityEngine;
using UnityEditor;

using static MeshBuilder.MarchingSquaresComponent;

namespace MeshBuilder
{
    [CustomEditor(typeof(MarchingSquaresComponent))]
    public class MarchingSquaresComponentEditor : Editor
    {
        private enum PropNames
        {
            meshFilter = 0,
            initializationPolicy,
            initialization,
            dataCreationPolicy,
            dataCreationInfo,
            dataComponent,
            AutoCompleteGeneration,
            GenerateOnInit
        }

        private MarchingSquaresComponent marchingSquares;
        private Utils.ComponentProperties<PropNames> props;
        
        private void OnEnable()
        {
            marchingSquares = (MarchingSquaresComponent)target;
            props = new Utils.ComponentProperties<PropNames>(serializedObject);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            props.Draw(PropNames.meshFilter);
            props.Draw(PropNames.initializationPolicy);
            props.Draw(PropNames.GenerateOnInit);
            props.Draw(PropNames.initialization);
            props.Draw(PropNames.dataCreationPolicy);

            DataManagementPolicy dataPolicy = marchingSquares.DataCreation;
            if (dataPolicy == DataManagementPolicy.CreateOwn)
            {
                props.Draw(PropNames.dataCreationInfo);
            }
            else if (dataPolicy == DataManagementPolicy.FromDataComponent)
            {
                props.Draw(PropNames.dataComponent);
            }

            props.Draw(PropNames.AutoCompleteGeneration);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
