#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GaussianSplatting.Editor;
using GaussianSplatting.Editor.Utils;
using GaussianSplatting.Runtime;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility that imports a PLY file into a GaussianSplatAsset
/// by leveraging the existing GaussianSplatAssetCreator via reflection.
/// </summary>
public static class TrainingAssetImporter
{
    private const string DefaultOutputFolder = "Assets/GaussianAssets";
    private static readonly string[] kAssetExtensions = { ".asset", "_chk.bytes", "_pos.bytes", "_oth.bytes", "_col.bytes", "_shs.bytes" };

    /// <summary>
    /// Compute the base name for an asset using the same logic as
    /// GaussianSplatAssetCreator (which uses FilePickerControl.PathToDisplayString
    /// followed by Path.GetFileNameWithoutExtension).
    /// </summary>
    static string ComputeAssetBaseName(string plyFilePath)
    {
        string path = plyFilePath.Replace('\\', '/');
        string[] parts = path.Split('/');
        string fileName = Path.GetFileNameWithoutExtension(parts[^1]).ToLowerInvariant();

        if (fileName != "point_cloud" && fileName != "splat" && fileName != "input")
            return Path.GetFileNameWithoutExtension(parts[^1]);

        if (parts.Length >= 4)
            path = string.Join('/', parts.TakeLast(4));

        path = path.Replace('/', '-');

        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>
    /// Delete any existing asset files with the given base name to avoid stale data
    /// and prevent infinite import loops from orphaned files.
    /// </summary>
    static void DeleteExistingAssets(string outputFolder, string baseName)
    {
        foreach (var ext in kAssetExtensions)
        {
            string assetRelPath = $"{outputFolder}/{baseName}{ext}";
            string absPath = Path.Combine(Application.dataPath, "..", assetRelPath);
            if (File.Exists(absPath))
            {
                AssetDatabase.DeleteAsset(assetRelPath);
                Debug.Log($"[TrainingImporter] Deleted stale file: {assetRelPath}");
            }
        }
    }

    /// <summary>
    /// Import a PLY file and create a GaussianSplatAsset.
    /// Returns the created asset, or null on failure.
    /// </summary>
    public static GaussianSplatAsset ImportPly(string plyFilePath, string outputFolder = null)
    {
        if (string.IsNullOrEmpty(outputFolder))
            outputFolder = DefaultOutputFolder;

        if (!File.Exists(plyFilePath))
        {
            Debug.LogError($"[TrainingImporter] PLY file not found: {plyFilePath}");
            return null;
        }

        Directory.CreateDirectory(outputFolder);

        try
        {
            string baseName = ComputeAssetBaseName(plyFilePath);
            string expectedAssetPath = $"{outputFolder}/{baseName}.asset";

            DeleteExistingAssets(outputFolder, baseName);

            var creator = ScriptableObject.CreateInstance<GaussianSplatAssetCreator>();
            var type = typeof(GaussianSplatAssetCreator);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            type.GetField("m_InputFile", flags).SetValue(creator, plyFilePath);
            type.GetField("m_OutputFolder", flags).SetValue(creator, outputFolder);
            type.GetField("m_ImportCameras", flags).SetValue(creator, true);

            type.GetField("m_FormatPos", flags).SetValue(creator, GaussianSplatAsset.VectorFormat.Norm11);
            type.GetField("m_FormatScale", flags).SetValue(creator, GaussianSplatAsset.VectorFormat.Norm11);
            type.GetField("m_FormatColor", flags).SetValue(creator, GaussianSplatAsset.ColorFormat.Norm8x4);
            type.GetField("m_FormatSH", flags).SetValue(creator, GaussianSplatAsset.SHFormat.Norm6);

            int vertexCount = GaussianFileReader.ReadFileHeader(plyFilePath);
            type.GetField("m_PrevVertexCount", flags).SetValue(creator, vertexCount);
            type.GetField("m_PrevFilePath", flags).SetValue(creator, plyFilePath);

            Debug.Log($"[TrainingImporter] Importing PLY: {plyFilePath} ({vertexCount:N0} splats)");
            Debug.Log($"[TrainingImporter] Expected asset: {expectedAssetPath}");

            var createMethod = type.GetMethod("CreateAsset", flags);
            createMethod.Invoke(creator, null);

            string error = (string)type.GetField("m_ErrorMessage", flags).GetValue(creator);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"[TrainingImporter] Import failed: {error}");
                UnityEngine.Object.DestroyImmediate(creator);
                return null;
            }

            UnityEngine.Object.DestroyImmediate(creator);

            AssetDatabase.ImportAsset(expectedAssetPath, ImportAssetOptions.ForceSynchronousImport);

            var asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(expectedAssetPath);
            if (asset != null)
                Debug.Log($"[TrainingImporter] Successfully created asset: {expectedAssetPath}");
            else
                Debug.LogError($"[TrainingImporter] Asset not found at: {expectedAssetPath}");

            return asset;
        }
        catch (Exception e)
        {
            Debug.LogError($"[TrainingImporter] Exception during import: {e.Message}\n{e.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Import a PLY and assign the result to a GaussianSplatRenderer.
    /// The asset assignment is deferred to the next editor frame to ensure
    /// AssetDatabase has fully settled after the import refresh.
    /// </summary>
    public static void ImportAndAssign(string plyFilePath, GaussianSplatRenderer renderer, string outputFolder = null)
    {
        var asset = ImportPly(plyFilePath, outputFolder);
        if (asset == null || renderer == null)
            return;

        var capturedAsset = asset;
        var capturedRenderer = renderer;
        EditorApplication.delayCall += () =>
        {
            if (capturedRenderer == null)
            {
                Debug.LogWarning("[TrainingImporter] Renderer was destroyed before asset could be assigned");
                return;
            }

            if (capturedRenderer.m_ShaderSplats == null)
                Debug.LogError("[TrainingImporter] m_ShaderSplats is null - check Graphics API is D3D12/Vulkan/Metal");
            if (capturedRenderer.m_ShaderComposite == null)
                Debug.LogError("[TrainingImporter] m_ShaderComposite is null");
            if (capturedRenderer.m_CSSplatUtilities == null)
                Debug.LogError("[TrainingImporter] m_CSSplatUtilities is null - compute shader missing");

            capturedRenderer.m_Asset = capturedAsset;
            Debug.Log($"[TrainingImporter] Asset assigned to renderer: {capturedRenderer.name}");
        };
    }
}
#endif
