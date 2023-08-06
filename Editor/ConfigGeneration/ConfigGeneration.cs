using System.IO;
using UnityEditor;

namespace VSCodeEditor
{
    public interface IConfigGenerator
    {
        string VSCodeSettings { get; set; }
        string WorkspaceSettings { get; set; }
        string EditorConfigSettings { get; set; }
        string ProjectDirectory { get; }
        IFlagHandler FlagHandler { get; }
        void Sync();
    }

    public class ConfigGeneration : IConfigGenerator
    {
        const string k_DefaultSettingsJson =
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

        const string k_DefaultWorkspaceJson =
            /*lang=json,strict*/
            @"{
	""folders"": [
		{
			""path"": "".""
		}
	]
}";

        const string k_DefaultEditorConfig =
            @"# EditorConfig is awesome: http://EditorConfig.org

# top-most EditorConfig file
root = true

# 4 space indentation
[*.cs]
indent_style = space
indent_size = 4
trim_trailing_whitespace = true

#Ignore IDE0051: Remove unused private members
[*.cs]
dotnet_diagnostic.IDE0051.severity = none
";

        public string ProjectDirectory { get; }
        readonly string m_ProjectName;
        IFlagHandler IConfigGenerator.FlagHandler => m_FlagHandler;

        string m_VSCodeSettings;
        string m_WorkspaceSettings;
        string m_EditorConfigSettings;

        public string VSCodeSettings
        {
            get =>
                m_VSCodeSettings ??= EditorPrefs.GetString(
                    "vscode_settings",
                    k_DefaultSettingsJson
                );
            set
            {
                if (value == "")
                    value = k_DefaultSettingsJson;

                m_VSCodeSettings = value;
                EditorPrefs.SetString("vscode_settings", value);
            }
        }

        public string WorkspaceSettings
        {
            get =>
                m_WorkspaceSettings ??= EditorPrefs.GetString(
                    "vscode_workspaceSettings",
                    k_DefaultWorkspaceJson
                );
            set
            {
                if (value == "")
                    value = k_DefaultWorkspaceJson;

                m_WorkspaceSettings = value;
                EditorPrefs.SetString("vscode_workspaceSettings", value);
            }
        }

        public string EditorConfigSettings
        {
            get =>
                m_EditorConfigSettings ??= EditorPrefs.GetString(
                    "vscode_editorConfigSettings",
                    k_DefaultEditorConfig
                );
            set
            {
                if (value == "")
                    value = k_DefaultEditorConfig;

                m_EditorConfigSettings = value;
                EditorPrefs.SetString("vscode_editorConfigSettings", value);
            }
        }

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

        public void Sync()
        {
            WriteVSCodeSettingsFiles();
            WriteWorkspaceFile();
            WriteEditorConfigFile();
        }

        void WriteVSCodeSettingsFiles()
        {
            if (m_FlagHandler.ConfigFlag.HasFlag(ConfigFlag.VSCode))
            {
                var vsCodeDirectory = Path.Combine(ProjectDirectory, ".vscode");

                if (!m_FileIOProvider.Exists(vsCodeDirectory))
                    m_FileIOProvider.CreateDirectory(vsCodeDirectory);

                var vsCodeSettingsJson = Path.Combine(vsCodeDirectory, "settings.json");

                m_FileIOProvider.WriteAllText(vsCodeSettingsJson, VSCodeSettings);
            }
        }

        void WriteWorkspaceFile()
        {
            if (m_FlagHandler.ConfigFlag.HasFlag(ConfigFlag.Workspace))
            {
                var workspaceFile = Path.Combine(
                    ProjectDirectory,
                    $"{m_ProjectName}.code-workspace"
                );

                m_FileIOProvider.WriteAllText(workspaceFile, WorkspaceSettings);
            }
        }

        void WriteEditorConfigFile()
        {
            if (m_FlagHandler.ConfigFlag.HasFlag(ConfigFlag.EditorConfig))
            {
                var editorConfig = Path.Combine(ProjectDirectory, ".editorconfig");

                m_FileIOProvider.WriteAllText(editorConfig, EditorConfigSettings);
            }
        }
    }
}
