using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            BakeFlatSurfaces
        }

        #region Fields
        [SerializeField]
        Generator generator = new();

        static VoxRenderPipelineAsset pipelineAsset;
        #endregion

        #region Callbacks
        public static Action<VoxImporter, AssetImportContext> OnImportBegin { get; set; }
        public static Action<VoxImporter, AssetImportContext> OnImportEnd { get; set; }
        #endregion

        #region Methods
        public override void OnImportAsset(AssetImportContext context)
        {
            OnImportBegin?.Invoke(this, context);

            pipelineAsset = AssetDatabase.FindAssets($"t: {nameof(VoxRenderPipelineAsset)}").Select(x => AssetDatabase.LoadAssetAtPath<VoxRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(x))).FirstOrDefault() ?? ScriptableObject.CreateInstance<VoxRenderPipelineAsset>();

            string assetPath = context.assetPath;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            Vox vox = new(assetPath);
            Chunk main = vox.Main;

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

            OnImportEnd?.Invoke(this, context);
        }
        #endregion
    }
}