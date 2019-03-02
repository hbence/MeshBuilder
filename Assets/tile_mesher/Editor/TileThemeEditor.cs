﻿using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

using Piece = MeshBuilder.Tile.Piece;
using TileTheme = MeshBuilder.TileTheme;

[CustomEditor(typeof(MeshBuilder.TileTheme))]
public class TileThemeEditor : Editor
{
    private const bool ExpandChildren = true;

    private SerializedProperty themeNameProp;
    private SerializedProperty typeProp;
    private SerializedProperty openTowardsThemesProp;
    private SerializedProperty baseVariantsProp;

    private bool showBaseVariants;

    private Object meshAsset;

    private void OnEnable()
    {
        themeNameProp = serializedObject.FindProperty("themeName");
        typeProp = serializedObject.FindProperty("type");
        openTowardsThemesProp = serializedObject.FindProperty("openTowardsThemes");
        baseVariantsProp = serializedObject.FindProperty("baseVariants");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(themeNameProp);
        EditorGUILayout.PropertyField(typeProp);
        EditorGUILayout.PropertyField(openTowardsThemesProp, true);

        EditorGUILayout.Separator();
        EditorGUILayout.LabelField("Base Pieces", EditorStyles.boldLabel);
        EditorGUILayout.Separator();

        bool fillFromFile = false;
        fillFromFile = GUILayout.Button("Fill Variants From File");

        meshAsset = EditorGUILayout.ObjectField("Mesh Asset File", meshAsset, typeof(Object), false);

        if (fillFromFile)
        {
            FillFromFile(meshAsset);
            meshAsset = null;
        }

        EditorGUILayout.Separator();
        showBaseVariants = EditorGUILayout.Foldout(showBaseVariants, "Base Variants Array");
        if (showBaseVariants)
        {
            DisplayBaseVariantsArray();

            if (GUILayout.Button("Add New Piece"))
            {
                ++baseVariantsProp.arraySize;
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DisplayBaseVariantsArray()
    {
        for (int i = 0; i < baseVariantsProp.arraySize; ++i)
        {
            bool remove = false;
            var child = baseVariantsProp.GetArrayElementAtIndex(i);
            DisplayBaseVariant(child, out remove);

            if (remove)
            {
                baseVariantsProp.DeleteArrayElementAtIndex(i);
                break;
            }
        }
    }

    private void DisplayBaseVariant(SerializedProperty prop, out bool remove)
    {
        remove = false;

        bool has = prop.NextVisible(ExpandChildren);
        if (has)
        {
            EditorGUI.indentLevel += 1;

            var topProp = prop.Copy();
            prop.NextVisible(ExpandChildren);
            var bottomProp = prop.Copy();
            prop.NextVisible(ExpandChildren);
            var arrayProp = prop;

            // name and remove button
            EditorGUILayout.BeginHorizontal();
            string labelName = CreatePieceName(topProp, bottomProp, arrayProp);
            GUILayout.Label(labelName,  EditorStyles.boldLabel);

            if (GUILayout.Button("X", GUILayout.Width(20)))
            {
                remove = true;
            }
            
            EditorGUILayout.EndHorizontal();

            // configurations
            EditorGUILayout.BeginHorizontal();

            DisplayPieceProp("top", 40, topProp);
            GUILayout.Space(-15);
            DisplayPieceProp("bottom", 60, bottomProp);            

            EditorGUILayout.EndHorizontal();

            // variant mesh array
            EditorGUI.indentLevel += 1;
            EditorGUILayout.PropertyField(arrayProp, ExpandChildren);
            EditorGUI.indentLevel -= 1;
            EditorGUI.indentLevel -= 1;

            EditorGUILayout.Separator();
        }
    }

    private void DisplayPieceProp(string label, int labelWidth, SerializedProperty prop)
    {
        EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));

        GUILayout.Space(-15);

        var value = EditorGUILayout.EnumPopup((Piece)prop.enumValueIndex);
        prop.enumValueIndex = System.Convert.ToByte(value);
    }

    private string CreatePieceName(SerializedProperty top, SerializedProperty bottom, SerializedProperty array)
    {
        var pieceValues = System.Enum.GetValues(typeof(Piece));

        Piece topPiece = (Piece) pieceValues.GetValue(top.enumValueIndex);
        Piece bottomPiece = (Piece) pieceValues.GetValue(bottom.enumValueIndex);

        return "Piece " + ToString(topPiece) + "_" + ToString(bottomPiece) + " variants:" + array.arraySize;
    }
    
    private string ToString(Piece piece)
    {
        string result = "";
        byte value = (byte)piece;

        int index = 3;

        result += (value & (1 << index)) > 0 ? "1" : "0";
        --index;
        result += (value & (1 << index)) > 0 ? "1" : "0";
        --index;
        result += (value & (1 << index)) > 0 ? "1" : "0";
        --index;
        result += (value & (1 << index)) > 0 ? "1" : "0";

        return result;
    }

    private void FillFromFile(Object obj)
    {
        if (obj == null)
        {
            Debug.LogError("Set a Mesh Asset File first!");
            return;
        }

        List<Mesh> meshes = new List<Mesh>();

        string path = AssetDatabase.GetAssetPath(obj);
        var assets = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
        foreach (var asset in assets)
        {
            if (asset is Mesh)
            {
                meshes.Add(asset as Mesh);
            }
        }

        if (meshes.Count > 0)
        {
            TileTheme theme = (TileTheme)target;
            theme.FillBaseVariantsFromMeshList(meshes);
            EditorUtility.SetDirty(theme);
        }
        else
        {
            Debug.LogError("Couldn't find any Mesh sub-asset in given Mesh Asset file:" + path);
        }
    }
}
