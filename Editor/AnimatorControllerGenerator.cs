using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Anatawa12.AnimatorControllerAsACode.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Anatawa12.AnimatorControllerAsACode.Editor
{
    [CreateAssetMenu]
    public sealed class AnimatorControllerGenerator : ScriptableObject
    {
        // target information
        public GUID targetGuid;

        /// <summary>
        /// The path to target asset. Relative to this asset if not starting with '/' and
        /// Relative to project root if starting with '/'.
        /// If empty, default value $"{name}.generated.controller" should be used.
        /// </summary>
        [SerializeField]
        private string targetPath;

        private string ThisAssetFolder
        {
            get
            {
                var path = AssetDatabase.GetAssetPath(this);
                if (string.IsNullOrEmpty(path))
                    throw new InvalidOperationException($"AnimatorControllerGenerator must be saved on disk to generate animator");
                return path.Substring(0, path.LastIndexOf('/') + 1);
            }
        }

        /// <summary>
        /// The path to target asset. Always relative to project root
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public string TargetPath
        {
            get
            {
                if (string.IsNullOrEmpty(targetPath))
                    return ThisAssetFolder + $"{name}.generated.controller";
                if (targetPath.StartsWith("/"))
                    return targetPath.Substring(1);
                return ThisAssetFolder + targetPath;
            }
        }

        // generator information
        public GeneratorLayerBase[] generators;

        private AnimatorController _targetResolved;

        public ImmutableHashSet<Object> WatchingObjects => generators.SelectMany(x => x.WatchingObjects).ToImmutableHashSet();

        private void OnEnable()
        {
            if (targetGuid.Empty())
                targetGuid = GUID.Generate();
            if (generators == null)
                generators = Array.Empty<GeneratorLayerBase>();
        }

        public void DoGenerate()
        {
            if (!TryLoadController())
            {
                CreateControllerAtPath();
            }
            else
            {
                // if we loaded previous controller, clear previous controller
                _targetResolved.layers = Array.Empty<AnimatorControllerLayer>();
                _targetResolved.parameters = Array.Empty<AnimatorControllerParameter>();
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(_targetResolved)))
                {
                    if (asset != _targetResolved)
                        DestroyImmediate(asset, true);
                }
            }

            foreach (var generator in generators)
            {
                generator.Generate(new ACC(generator.GeneratorName, _targetResolved));
            }
            EditorUtility.SetDirty(_targetResolved);
        }

        private bool TryLoadController()
        {
            // try load paths
            if (_targetResolved) return true;
            if (TryLoadViaGuid()) return true;
            return false;
        }

        private bool TryLoadViaGuid()
        {
            var path = AssetDatabase.GUIDToAssetPath(targetGuid.ToString());
            if (string.IsNullOrEmpty(path)) return false;
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (!(asset is AnimatorController controller))
                throw new InvalidOperationException($"{targetGuid} is not a AnimatorController");
            _targetResolved = controller;
            return true;
        }

        private void CreateControllerAtPath()
        {
            if (targetGuid.Empty()) {
                targetGuid = GUID.Generate();
                EditorUtility.SetDirty(this);
            }
            var assetPath = TargetPath;
            var metaPath = $"{assetPath}.meta";
            File.WriteAllText(assetPath, EmptyAnimatorController, Encoding.UTF8);
            File.WriteAllText(metaPath, EmptyAnimatorControllerMeta.Replace("{GUID}", targetGuid.ToString()), Encoding.UTF8);
            AssetDatabase.Refresh(ImportAssetOptions.Default);
            _targetResolved = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (_targetResolved == null)
                throw new InvalidOperationException("created controller cannot be loaded");
        }

        private const string EmptyAnimatorController =
            "%YAML 1.1\n"
            + "%TAG !u! tag:unity3d.com,2011:\n"
            + "--- !u!91 &9100000\n"
            + "AnimatorController:\n"
            + "  m_ObjectHideFlags: 0\n"
            + "  m_CorrespondingSourceObject: {fileID: 0}\n"
            + "  m_PrefabInstance: {fileID: 0}\n"
            + "  m_PrefabAsset: {fileID: 0}\n"
            + "  m_Name: New Animator Controller\n"
            + "  serializedVersion: 5\n"
            + "  m_AnimatorParameters: []\n"
            + "  m_AnimatorLayers: []\n";

        // please replace "GUID" with actual guid without '-'
        private const string EmptyAnimatorControllerMeta =
            "fileFormatVersion: 2\n"
            + "guid: {GUID}\n"
            + "NativeFormatImporter:\n"
            + "  externalObjects: {}\n"
            + "  mainObjectFileID: 9100000\n"
            + "  userData:\n"
            + "  assetBundleName:\n"
            + "  assetBundleVariant:\n";

        internal void UpdateTargetPath(string newTargetPath)
        {
            // nothing to do if newTargetPath is same as TargetPath
            // this happens if 
            if (newTargetPath == TargetPath)
                return;
            if (targetPath?.StartsWith("/") ?? false)
            {
                // if current target path is absolute, update as absolute 
                targetPath = "/" + newTargetPath;
                return;
            }

            var thisAssetFolderComponents = ThisAssetFolder.Split('/');
            var newTargetPathComponents = newTargetPath.Split('/');
            if (thisAssetFolderComponents[0] != newTargetPathComponents[0])
            {
                // the first path component (e.g. Assets, Library, Packages) is not same, use absolute
                targetPath = "/" + newTargetPath;
                return;
            }

            // otherwise, use relative
            var commonComponentsCount = -1;
            for (var i = 0;
                 i < thisAssetFolderComponents.Length &&
                 i < newTargetPathComponents.Length;
                 i++)
            {
                if (thisAssetFolderComponents[i] != newTargetPathComponents[i])
                {
                    commonComponentsCount = i;
                    break;
                }
            }

            if (commonComponentsCount == -1)
                commonComponentsCount = Math.Min(thisAssetFolderComponents.Length, newTargetPath.Length);
            // <scc>
            // A/B/C/E/F/this.asset
            // A/B/C/D/target.asset
            var buildingPath = new StringBuilder();
            for (var i = 0; i < thisAssetFolderComponents.Length - commonComponentsCount; i++)
                buildingPath.Append("../");
            buildingPath.Append(string.Join(",", newTargetPathComponents.Skip(commonComponentsCount)));
            targetPath = buildingPath.ToString();
        }
    }
}
