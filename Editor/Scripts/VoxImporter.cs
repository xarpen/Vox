using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
#if PACKAGE_TRIINSPECTOR
using TriInspector;
#endif

namespace Fluorite.Vox.Editor
{
#if PACKAGE_TRIINSPECTOR
    [DeclareTabGroup("Tab")]
#endif
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
#if PACKAGE_TRIINSPECTOR
        [GroupNext("Tab"), Tab("Model")]
#endif
        public float scaleFactor = 1;

        public StaticEditorFlags staticFlags = (StaticEditorFlags)byte.MaxValue;
        public int baseLayer;
#if PACKAGE_TRIINSPECTOR
        [GroupNext("Tab"), Tab("Collider")]
#endif
        public bool generateColliders = true;
#if PACKAGE_TRIINSPECTOR
        [EnableIf(nameof(generateColliders))]
#endif
        public bool convex;
#if PACKAGE_TRIINSPECTOR
        [GroupNext("Tab"), Tab("Material")]
        [InfoBox("Color => Direct mapping", TriMessageType.None)]
        [InfoBox("Roughness => [Smoothness = (1 - Roughness) ^ 2]", TriMessageType.None)]
        [InfoBox("IOR => Direct mapping (only used in Glass)", TriMessageType.None)]
        [InfoBox("Specular => [SpecularColor = Metallic * Color * Specular]", TriMessageType.None)]
        [InfoBox("Metallic => Direct mapping", TriMessageType.None)]
        [InfoBox("Emit => [Emission = lerp(0, Color ^ Flux, Emission)]", TriMessageType.None)]
        [InfoBox("Glass => [Alpha = 1 - Transparency]", TriMessageType.None)]
#endif
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