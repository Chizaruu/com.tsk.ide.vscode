using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Profiling;
using SR = System.Reflection;

namespace VSCodeEditor
{
    public interface IConfigGenerator
    {
        string ProjectDirectory { get; }
        IFlagHandler FlagHandler { get; }
        bool TskFileExists();
        void Sync(bool force = false);
    }

    public class ConfigGeneration : IConfigGenerator
    {
        const string k_SettingsJson =
            /*lang=json,strict*/
            @"{
    ""files.exclude"":
    {
        ""**/.DS_Store"":true,
        ""**/.git"":true,
        ""**/.gitmodules"":true,
        ""**/*.booproj"":true,
        ""**/*.pidb"":true,
        ""**/*.suo"":true,
        ""**/*.user"":true,
        ""**/*.userprefs"":true,
        ""**/*.unityproj"":true,
        ""**/*.dll"":true,
        ""**/*.exe"":true,
        ""**/*.pdf"":true,
        ""**/*.mid"":true,
        ""**/*.midi"":true,
        ""**/*.wav"":true,
        ""**/*.gif"":true,
        ""**/*.ico"":true,
        ""**/*.jpg"":true,
        ""**/*.jpeg"":true,
        ""**/*.png"":true,
        ""**/*.psd"":true,
        ""**/*.tga"":true,
        ""**/*.tif"":true,
        ""**/*.tiff"":true,
        ""**/*.3ds"":true,
        ""**/*.3DS"":true,
        ""**/*.fbx"":true,
        ""**/*.FBX"":true,
        ""**/*.lxo"":true,
        ""**/*.LXO"":true,
        ""**/*.ma"":true,
        ""**/*.MA"":true,
        ""**/*.obj"":true,
        ""**/*.OBJ"":true,
        ""**/*.asset"":true,
        ""**/*.cubemap"":true,
        ""**/*.flare"":true,
        ""**/*.mat"":true,
        ""**/*.meta"":true,
        ""**/*.prefab"":true,
        ""**/*.unity"":true,
        ""build/"":true,
        ""Build/"":true,
        ""Library/"":true,
        ""library/"":true,
        ""obj/"":true,
        ""Obj/"":true,
        ""ProjectSettings/"":true,
        ""temp/"":true,
        ""Temp/"":true
    }
}";

        const string k_WorkspaceJson =
            /*lang=json,strict*/
            @"{
	""folders"": [
		{
			""path"": "".""
		}
	]
}";

        const string k_OmniSharpJson =
            /*lang=json,strict*/
            @"{
    ""RoslynExtensionsOptions"": {
        ""enableRoslynAnalyzers"": true,
        ""enableEditorConfigSupport"": true,
        ""analyzeOpenDocumentsOnly"": true,
        ""sdkIncludePrereleases"": false,
        ""organizeImportsOnFormat"": true,
        ""threadsToUseForAnalyzers"": true,
        ""useModernNet"": true,
        ""documentAnalysisTimeoutMs"": 600000
    }
}";

        const string k_EditorConfig =
            @"# EditorConfig is awesome: http://EditorConfig.org

# top-most EditorConfig file
root = true

# 4 space indentation
[*.cs]
indent_style = space
indent_size = 4
trim_trailing_whitespace = true
";

        public string ProjectDirectory { get; }
        readonly string m_ProjectName;
        IFlagHandler IConfigGenerator.FlagHandler => m_FlagHandler;
        readonly IFlagHandler m_FlagHandler;
        readonly IFileIO m_FileIOProvider;

        public ConfigGeneration(string tempDirectory)
            : this(tempDirectory, new FlagHandler(), new FileIOProvider()) { }

        public ConfigGeneration(
            string tempDirectory,
            IFlagHandler flagHandler,
            IFileIO fileIOProvider
        )
        {
            ProjectDirectory = tempDirectory;
            m_ProjectName = Path.GetFileName(ProjectDirectory);
            m_FlagHandler = new FlagHandler();
            m_FileIOProvider = new FileIOProvider();
        }

        public bool TskFileExists()
        {
            var doNotDelete = Path.Combine(ProjectDirectory, "TSKDoNotDelete.txt");

            if (m_FileIOProvider.Exists(doNotDelete))
                return true;

            m_FileIOProvider.WriteAllText(
                doNotDelete,
                "This file is used by the TSK VSCode Editor package. Deleting it will cause your configuration to be overwritten."
            );
            return false;
        }

        public void Sync(bool canForce = false)
        {
            WriteVSCodeSettingsFiles();
            WriteWorkspaceFile(canForce);
            WriteOmniSharpConfigFile(canForce);
            WriteEditorConfigFile(canForce);
        }

        void WriteVSCodeSettingsFiles()
        {
            var vsCodeDirectory = Path.Combine(ProjectDirectory, ".vscode");

            if (!m_FileIOProvider.Exists(vsCodeDirectory))
                m_FileIOProvider.CreateDirectory(vsCodeDirectory);

            var vsCodeSettingsJson = Path.Combine(vsCodeDirectory, "settings.json");

            m_FileIOProvider.WriteAllText(vsCodeSettingsJson, k_SettingsJson);
        }

        void WriteWorkspaceFile(bool canForce = false)
        {
            if (m_FlagHandler.ConfigFlag.HasFlag(ConfigFlag.Workspace) || canForce)
            {
                var workspaceFile = Path.Combine(
                    ProjectDirectory,
                    $"{m_ProjectName}.code-workspace"
                );

                m_FileIOProvider.WriteAllText(workspaceFile, k_WorkspaceJson);
            }
        }

        void WriteOmniSharpConfigFile(bool canForce = false)
        {
            if (m_FlagHandler.ConfigFlag.HasFlag(ConfigFlag.OmniSharp) || canForce)
            {
                var omniSharpConfig = Path.Combine(ProjectDirectory, "omnisharp.json");

                m_FileIOProvider.WriteAllText(omniSharpConfig, k_OmniSharpJson);
            }
        }

        void WriteEditorConfigFile(bool canForce = false)
        {
            if (m_FlagHandler.ConfigFlag.HasFlag(ConfigFlag.EditorConfig) || canForce)
            {
                var editorConfig = Path.Combine(ProjectDirectory, ".editorconfig");

                m_FileIOProvider.WriteAllText(editorConfig, k_EditorConfig);
            }
        }
    }
}
