using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Fluorite.Vox.Editor
{
    [ScriptedImporter(1, "vox")]
    public partial class VoxImporter : ScriptedImporter
    {
        public enum ImportMaterialType
        {
            None,
            Default,
            BakeEdgeToTexture
        }

        #region Fields
        [Header("Model")]
        public float scaleFactor = 1;
        public StaticEditorFlags staticFlags = (StaticEditorFlags)byte.MaxValue;
        public int baseLayer;

        [Header("Collider")]
        public bool generateColliders = true;
        public bool convex;

        // Color => Direct mapping
        // Roughness => [Smoothness = (1 - Roughness) ^ 2]
        // IOR => Direct mapping (only used in Glass)
        // Specular => [SpecularColor = Metallic * Color * Specular]
        // Metallic => Direct mapping
        // Emit => [Emission = lerp(0, Color ^ Flux, Emission)]
        // Glass => [Alpha = 1 - Transparency]
        [Header("Material")]
        public ImportMaterialType importMaterials = ImportMaterialType.Default;
        #endregion

        #region Callbacks
        public static Action<VoxImporter, AssetImportContext> OnPreprocess { get; set; }
        public static Action<VoxImporter, AssetImportContext> OnPostprocess { get; set; }
        #endregion

        #region Methods
        public override void OnImportAsset(AssetImportContext context)
        {
            OnPreprocess?.Invoke(this, context);

            string assetPath = context.assetPath;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            Vox vox = new(assetPath);
            Chunk main = vox.Main;

            Generator generator = new(scaleFactor, staticFlags, baseLayer, generateColliders, convex, importMaterials);
            (List<Shape> shapes, GameObject gameObject) = generator.CreateAssets(main, name);

            List<Texture> textures = new();
            List<Material> materials = new();
            foreach (Shape shape in shapes)
            {
                foreach (Texture texture in shape.Textures)
                {
                    if (textures.Contains(texture)) continue;
                    context.AddObjectToAsset(texture.name, texture);
                    textures.Add(texture);
                }

                foreach (Material material in shape.Materials)
                {
                    if (materials.Contains(material)) continue;
                    context.AddObjectToAsset(material.name, material);
                    materials.Add(material);
                }

                context.AddObjectToAsset(shape.Mesh.name, shape.Mesh);
            }

            context.AddObjectToAsset(name, gameObject);
            context.SetMainObject(gameObject);

            OnPostprocess?.Invoke(this, context);
        }
        #endregion
    }
}