using UnityEditor;
using UnityEngine;

namespace Fluorite.Vox.Editor
{
    [CustomEditor(typeof(VoxImporter))]
    public class VoxImporterEditor : UnityEditor.AssetImporters.ScriptedImporterEditor
    {
        static class Styles
        {
            #region Fields
            public static GUIContent Offset = new("Voxel Offset", "This voxel offset.");
            public static GUIContent ScaleFactor = new("Scale Factor", "How much to scale the models compared to what is in the source file.");
            public static GUIContent GenerateColliders = new("Generate Colliders", "Should Unity generate mesh colliders for all meshes.");
            public static GUIContent Convex = new("Convex", "Will mesh be convex.");
            public static GUIContent StaticFlags = new("Static Flags", "What static flags should be set.");
            public static GUIContent ImportLayers = new("Import Layers", "Should Unity import layers from file. Value represents base layer in Unity.");

            public static GUIContent ImportMaterials = new("Import Materials");
            public static GUIContent NoMaterialHelp = new("Do not generate materials. Use Unity's default material instead.");
            #endregion
        }

        #region Fields
        string[] tabNames = { "Model", "Materials" };
        int tab;

        SerializedProperty offset;
        SerializedProperty scaleFactor;
        SerializedProperty generateColliders;
        SerializedProperty convex;
        SerializedProperty staticFlags;
        SerializedProperty layer;
        SerializedProperty importMaterials;
        #endregion

        #region Base Methods
        public override void OnEnable()
        {
            base.OnEnable();

            offset = serializedObject.FindProperty("generator.offset");
            scaleFactor = serializedObject.FindProperty("generator.scaleFactor");
            generateColliders = serializedObject.FindProperty("generator.generateColliders");
            convex = serializedObject.FindProperty("generator.convex");
            staticFlags = serializedObject.FindProperty("generator.staticFlags");
            layer = serializedObject.FindProperty("generator.layer");
            importMaterials = serializedObject.FindProperty("generator.importMaterials");
        }
        public override bool HasPreviewGUI() => base.HasPreviewGUI() && targets.Length < 2;
        public override void OnInspectorGUI()
        {
            DisplayTabs();
            if (tab == 0) DisplayModelTab();
            else if (tab == 1) DisplayMaterialsTab();

            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
        #endregion

        #region Support Methods
        void DisplayTabs()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            tab = GUILayout.Toolbar(tab, tabNames, "LargeButton", GUI.ToolbarButtonSize.FitToContents);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        void DisplayModelTab()
        {
            EditorGUILayout.PropertyField(offset, Styles.Offset);
            EditorGUILayout.PropertyField(scaleFactor, Styles.ScaleFactor);
            EditorGUILayout.PropertyField(generateColliders, Styles.GenerateColliders);
            EditorGUI.BeginDisabledGroup(!generateColliders.boolValue);
            EditorGUILayout.PropertyField(convex, Styles.Convex);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.PropertyField(staticFlags, Styles.StaticFlags);

            if (layer.intValue == -1)
            {
                layer.intValue = EditorGUILayout.Toggle(Styles.ImportLayers, false) ? 8 : -1;
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                layer.intValue = EditorGUILayout.Toggle(Styles.ImportLayers, true) ? layer.intValue : -1;
                layer.intValue = EditorGUILayout.IntField(layer.intValue);
                EditorGUILayout.EndHorizontal();
            }
        }
        void DisplayMaterialsTab()
        {
            EditorGUILayout.PropertyField(importMaterials, Styles.ImportMaterials);
            if (importMaterials.boolValue)
            {
            }
            else
            {
                EditorGUILayout.HelpBox(Styles.NoMaterialHelp.text, MessageType.Info);
            }
        }
        #endregion
    }
}