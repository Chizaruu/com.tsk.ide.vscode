using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

namespace VSCodeEditor
{
    [InitializeOnLoad]
    public class VSCodeScriptEditor : IExternalCodeEditor
    {
        const string vscode_argument = "vscode_arguments";
        const string vscode_extension = "vscode_userExtensions";

        string m_EditorArguments;

        bool m_ShowEditorSection = false;
        bool m_ShowExtensionsSection = false;
        bool m_ShowConfigSection = false;
        bool m_ShowProjectSection = true;
        bool m_ShowVSCodeSettingsSection = false;
        bool m_ShowWorkspaceSection = false;
        bool m_ShowEditorConfigSection = false;
        bool m_ShowLaunchConfigSection = false;
        bool m_ShowBuiltInExtensions = true;
        bool m_ShowUnityUserExtensions = true;
        bool m_ShowTSKCustomExtensions = true;

        Vector2 m_VSCodeScrollPosition;
        Vector2 m_WorkspaceScrollPosition;
        Vector2 m_EditorConfigScrollPosition;
        Vector2 m_LaunchConfigScrollPosition;

        readonly IDiscovery m_Discoverability;
        readonly IGenerator m_ProjectGeneration;
        readonly IConfigGenerator m_ConfigGeneration;

        static readonly string[] k_SupportedFileNames =
        {
            "code.exe",
            "visualstudiocode.app",
            "visualstudiocode-insiders.app",
            "vscode.app",
            "code.app",
            "code.cmd",
            "code-insiders.cmd",
            "code",
            "com.visualstudio.code"
        };

        static bool IsOSX => Application.platform == RuntimePlatform.OSXEditor;

        static string DefaultApp => EditorPrefs.GetString("kScriptsDefaultApp");

        static string ExternalEditorDefaultArgument { get; } =
            "\"$(ProjectPath)\" -g \"$(File)\":$(Line):$(Column)";

        static string ExternalEditorWorkplaceDefaultArgument { get; } =
            "\"$(ProjectPath)/$(ProjectName).code-workspace\" -g \"$(File)\":$(Line):$(Column)";

        string EditorArguments
        {
            get =>
                m_EditorArguments ??= EditorPrefs.GetString(
                    vscode_argument,
                    ExternalEditorDefaultArgument
                );
            set
            {
                m_EditorArguments = value;
                EditorPrefs.SetString(vscode_argument, value);
            }
        }

        static string[] UserAddedExtensions
        {
            get => EditorPrefs.GetString("vscode_userAddedExtensions", "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            set => EditorPrefs.SetString("vscode_userAddedExtensions", string.Join(";", value));
        }

        #region EditorPrefs Bool Sections

        bool ShowEditorSection
        {
            get => m_ShowEditorSection || EditorPrefs.GetBool("vscode_showEditorSection", false);
            set
            {
                m_ShowEditorSection = value;
                EditorPrefs.SetBool("vscode_showEditorSection", value);
            }
        }

        bool ShowExtensionsSection
        {
            get =>
                m_ShowExtensionsSection
                || EditorPrefs.GetBool("vscode_showExtensionsSection", false);
            set
            {
                m_ShowExtensionsSection = value;
                EditorPrefs.SetBool("vscode_showExtensionsSection", value);
            }
        }

        bool ShowConfigSection
        {
            get => m_ShowConfigSection || EditorPrefs.GetBool("vscode_showConfigSection", false);
            set
            {
                m_ShowConfigSection = value;
                EditorPrefs.SetBool("vscode_showConfigSection", value);
            }
        }

        bool ShowProjectSection
        {
            get => m_ShowProjectSection || EditorPrefs.GetBool("vscode_showProjectSection", false);
            set
            {
                m_ShowProjectSection = value;
                EditorPrefs.SetBool("vscode_showProjectSection", value);
            }
        }

        bool ShowBuiltInExtensions
        {
            get =>
                m_ShowBuiltInExtensions
                || EditorPrefs.GetBool("vscode_showBuiltInExtensions", false);
            set
            {
                m_ShowBuiltInExtensions = value;
                EditorPrefs.SetBool("vscode_showBuiltInExtensions", value);
            }
        }

        bool ShowUnityUserExtensions
        {
            get =>
                m_ShowUnityUserExtensions
                || EditorPrefs.GetBool("vscode_showUnityUserExtensions", false);
            set
            {
                m_ShowUnityUserExtensions = value;
                EditorPrefs.SetBool("vscode_showUnityUserExtensions", value);
            }
        }

        bool ShowTSKCustomExtensions
        {
            get =>
                m_ShowTSKCustomExtensions
                || EditorPrefs.GetBool("vscode_showTSKCustomExtensions", false);
            set
            {
                m_ShowTSKCustomExtensions = value;
                EditorPrefs.SetBool("vscode_showTSKCustomExtensions", value);
            }
        }

        bool ShowUserCustomExtensions
        {
            get =>
                EditorPrefs.GetBool("vscode_showUserCustomExtensions", false);
            set
            {
                EditorPrefs.SetBool("vscode_showUserCustomExtensions", value);
            }
        }

        #endregion

        #region Extensions Section

        static readonly string[] customExtensions = { "jslib", "json", "log" };
        private static string[] cachedDefaultExtensions;
        private static HashSet<string> cachedHandledExtensions;

        public static string[] DefaultExtensions => cachedDefaultExtensions ??= GetDefaultExtensions();

        private static string[] GetDefaultExtensions() =>
            EditorSettings.projectGenerationBuiltinExtensions
                .Concat(EditorSettings.projectGenerationUserExtensions)
                .Concat(customExtensions)
                .Concat(UserAddedExtensions)
                .Distinct()
                .ToArray();

        private static HashSet<string> HandledExtensions => cachedHandledExtensions
            ??= new(DefaultExtensions.Select(ext => ext.TrimStart('.', '*')));

        private static bool SupportsExtension(string path)
        {
            var extension = Path.GetExtension(path)?.TrimStart('.');
            return extension != null && HandledExtensions.Contains(extension);
        }

        private string extensionToAdd;

        private void AddUserExtension()
        {
            extensionToAdd = extensionToAdd.TrimStart('.');

            var currentExtensions = UserAddedExtensions.ToList();
            if (!currentExtensions.Contains(extensionToAdd))
            {
                currentExtensions.Add(extensionToAdd);
                UserAddedExtensions = currentExtensions.ToArray();
            }
        }

        #endregion

        #region UI
        public void OnGUI()
        {
            RenderEditorSection();
            RenderExtensionsSection();
            RenderConfigSection();
            RenderProjectSection();
        }

        void RenderEditorSection()
        {
            ShowEditorSection = EditorGUILayout.BeginFoldoutHeaderGroup(
                ShowEditorSection,
                "Configure Editor Script Editor Arguments:"
            );

            EditorGUILayout.EndFoldoutHeaderGroup();

            if (ShowEditorSection)
            {
                EditorGUI.indentLevel++;
                EditorArguments = EditorGUILayout.TextField(
                    "External Script Editor Args",
                    EditorArguments
                );
                FlagButton(
                    ArgumentFlag.EditorArgument,
                    "Use Code-Workspace",
                    "",
                    (handler, flag) => handler.ArgumentFlag.HasFlag(flag),
                    (handler, flag) => handler.ToggleArgument(flag)
                );
                RegenerateButton(
                    m_ConfigGeneration.FlagHandler.ArgumentFlag.HasFlag(ArgumentFlag.EditorArgument)
                        ? "Reset to Workspace default"
                        : "Reset to default",
                    "Regenerate editor arguments"
                );
                EditorGUI.indentLevel--;
            }
        }

        void RenderExtensionsSection()
        {
            ShowExtensionsSection = EditorGUILayout.BeginFoldoutHeaderGroup(
                ShowExtensionsSection,
                "Configure Extensions Handled by VSCode:"
            );

            EditorGUILayout.EndFoldoutHeaderGroup();

            if (ShowExtensionsSection)
            {
                EditorGUI.indentLevel++;

                // Built-in Extensions Foldout
                ShowBuiltInExtensions = EditorGUILayout.Foldout(ShowBuiltInExtensions, "Built-in Extensions", true);
                if (ShowBuiltInExtensions)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    foreach (var ext in EditorSettings.projectGenerationBuiltinExtensions)
                    {
                        EditorGUILayout.TextField(ext);
                    }
                    EditorGUI.EndDisabledGroup();
                }

                // Unity User Extensions Foldout
                ShowUnityUserExtensions = EditorGUILayout.Foldout(ShowUnityUserExtensions, "Unity User Extensions", true);
                if (ShowUnityUserExtensions)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    foreach (var ext in EditorSettings.projectGenerationUserExtensions)
                    {
                        EditorGUILayout.TextField(ext);
                    }
                    EditorGUI.EndDisabledGroup();
                }

                // TSK Custom Extensions Foldout
                ShowTSKCustomExtensions = EditorGUILayout.Foldout(ShowTSKCustomExtensions, "TSK Custom Extensions", true);
                if (ShowTSKCustomExtensions)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    foreach (var ext in customExtensions)
                    {
                        EditorGUILayout.TextField(ext);
                    }
                    EditorGUI.EndDisabledGroup();
                }

                // Custom User Extensions Foldout
                ShowUserCustomExtensions = EditorGUILayout.Foldout(ShowUserCustomExtensions, "Custom User Extensions", true);
                if (ShowUserCustomExtensions)
                {
                    var userExtensions = UserAddedExtensions.ToList();
                    for (int i = userExtensions.Count - 1; i >= 0; i--)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUI.BeginDisabledGroup(true);
                        //Make read-only text field
                        EditorGUILayout.TextField(userExtensions[i]);
                        EditorGUI.EndDisabledGroup();

                        if (GUILayout.Button("Delete", GUILayout.Width(60)))
                        {
                            userExtensions.RemoveAt(i);
                            UserAddedExtensions = userExtensions.ToArray();
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.Space();
                    // Add Custom User Extensions
                    EditorGUILayout.LabelField("Add Custom User Extension:", EditorStyles.boldLabel);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.BeginChangeCheck();
                    var newExtension = EditorGUILayout.TextArea("");

                    if (EditorGUI.EndChangeCheck() && !string.IsNullOrWhiteSpace(newExtension))
                    {
                        extensionToAdd = newExtension;
                    }

                    if (GUILayout.Button("Add", GUILayout.Width(60)))
                    {
                        AddUserExtension();
                    }

                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.Space();

                EditorGUI.indentLevel--;
            }
        }

        void RenderConfigSection()
        {
            ShowConfigSection = EditorGUILayout.BeginFoldoutHeaderGroup(
                ShowConfigSection,
                "Generate config files for:"
            );

            EditorGUILayout.EndFoldoutHeaderGroup();

            if (ShowConfigSection)
            {
                EditorGUI.indentLevel++;
                FlagButton(
                    ConfigFlag.VSCode,
                    "VSCode Settings",
                    "",
                    (handler, flag) => handler.ConfigFlag.HasFlag(flag),
                    (handler, flag) => handler.ToggleConfig(flag)
                );

                if (m_ConfigGeneration.FlagHandler.ConfigFlag.HasFlag(ConfigFlag.VSCode))
                    RenderSettingsSection(
                        ref m_ShowVSCodeSettingsSection,
                        m_ConfigGeneration.VSCodeSettings,
                        "VSCode",
                        ref m_VSCodeScrollPosition
                    );

                FlagButton(
                    ConfigFlag.Workspace,
                    "Workspace",
                    "",
                    (handler, flag) => handler.ConfigFlag.HasFlag(flag),
                    (handler, flag) => handler.ToggleConfig(flag)
                );

                if (m_ConfigGeneration.FlagHandler.ConfigFlag.HasFlag(ConfigFlag.Workspace))
                    RenderSettingsSection(
                        ref m_ShowWorkspaceSection,
                        m_ConfigGeneration.WorkspaceSettings,
                        "Workspace",
                        ref m_WorkspaceScrollPosition
                    );

                FlagButton(
                    ConfigFlag.EditorConfig,
                    "Editor Config",
                    "",
                    (handler, flag) => handler.ConfigFlag.HasFlag(flag),
                    (handler, flag) => handler.ToggleConfig(flag)
                );

                if (m_ConfigGeneration.FlagHandler.ConfigFlag.HasFlag(ConfigFlag.EditorConfig))
                    RenderSettingsSection(
                        ref m_ShowEditorConfigSection,
                        m_ConfigGeneration.EditorConfigSettings,
                        "editorconfig",
                        ref m_EditorConfigScrollPosition
                    );

                FlagButton(
                    ConfigFlag.LaunchConfig,
                    "Launch Config",
                    "",
                    (handler, flag) => handler.ConfigFlag.HasFlag(flag),
                    (handler, flag) => handler.ToggleConfig(flag)
                );

                if (m_ConfigGeneration.FlagHandler.ConfigFlag.HasFlag(ConfigFlag.LaunchConfig))
                    RenderSettingsSection(
                        ref m_ShowLaunchConfigSection,
                        m_ConfigGeneration.LaunchConfigSettings,
                        "Launch",
                        ref m_LaunchConfigScrollPosition
                    );

                RegenerateButton("Regenerate", "Regenerate config files");
                EditorGUI.indentLevel--;
            }
        }

        void RenderSettingsSection(
            ref bool showSection,
            string settings,
            string sectionName,
            ref Vector2 scrollPosition
        )
        {
            showSection = EditorGUILayout.BeginFoldoutHeaderGroup(
                showSection,
                $"Configure {sectionName} Settings:"
            );

            EditorGUILayout.EndFoldoutHeaderGroup();

            if (showSection)
            {
                scrollPosition = EditorGUILayout.BeginScrollView(
                    scrollPosition,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight * 7)
                );
                EditorGUI.BeginChangeCheck();
                settings = EditorGUILayout.TextArea(settings, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();

                if (EditorGUI.EndChangeCheck())
                {
                    switch (sectionName)
                    {
                        case "VSCode":
                            m_ConfigGeneration.VSCodeSettings = settings;
                            break;
                        case "Workspace":
                            m_ConfigGeneration.WorkspaceSettings = settings;
                            break;
                        case "editorconfig":
                            m_ConfigGeneration.EditorConfigSettings = settings;
                            break;
                        case "Launch":
                            m_ConfigGeneration.LaunchConfigSettings = settings;
                            break;
                    }
                }

                RegenerateButton("Reset to default", $"Reset {sectionName} settings");
                EditorGUILayout.Space();
            }
        }

        void RenderProjectSection()
        {
            ShowProjectSection = EditorGUILayout.BeginFoldoutHeaderGroup(
                ShowProjectSection,
                "Generate .csproj files for:"
            );

            EditorGUILayout.EndFoldoutHeaderGroup();

            if (ShowProjectSection)
            {
                EditorGUI.indentLevel++;
                FlagButton(
                    ProjectGenerationFlag.Embedded,
                    "Embedded packages",
                    "",
                    (handler, flag) => handler.ProjectGenerationFlag.HasFlag(flag),
                    (handler, flag) => handler.ToggleProjectGeneration(flag)
                );
                FlagButton(
                    ProjectGenerationFlag.Local,
                    "Local packages",
                    "",
                    (handler, flag) => handler.ProjectGenerationFlag.HasFlag(flag),
                    (handler, flag) => handler.ToggleProjectGeneration(flag)
                );
                FlagButton(
                    ProjectGenerationFlag.Registry,
                    "Registry packages",
                    "",
                    (handler, flag) => handler.ProjectGenerationFlag.HasFlag(flag),
                    (handler, flag) => handler.ToggleProjectGeneration(flag)
                );
                FlagButton(
                    ProjectGenerationFlag.Git,
                    "Git packages",
                    "",
                    (handler, flag) => handler.ProjectGenerationFlag.HasFlag(flag),
                    (handler, flag) => handler.ToggleProjectGeneration(flag)
                );
                FlagButton(
                    ProjectGenerationFlag.BuiltIn,
                    "Built-in packages",
                    "",
                    (handler, flag) => handler.ProjectGenerationFlag.HasFlag(flag),
                    (handler, flag) => handler.ToggleProjectGeneration(flag)
                );
                FlagButton(
                    ProjectGenerationFlag.LocalTarBall,
                    "Local tarball",
                    "",
                    (handler, flag) => handler.ProjectGenerationFlag.HasFlag(flag),
                    (handler, flag) => handler.ToggleProjectGeneration(flag)
                );
                FlagButton(
                    ProjectGenerationFlag.Unknown,
                    "Packages from unknown sources",
                    "",
                    (handler, flag) => handler.ProjectGenerationFlag.HasFlag(flag),
                    (handler, flag) => handler.ToggleProjectGeneration(flag)
                );

                EditorGUILayout.Space();
                FlagButton(
                    ProjectGenerationFlag.Analyzers,
                    "Add Analyzers to all .csproj files",
                    "",
                    (handler, flag) => handler.ProjectGenerationFlag.HasFlag(flag),
                    (handler, flag) => handler.ToggleProjectGeneration(flag)
                );
                RegenerateButton("Regenerate", "Regenerate project files");
                EditorGUI.indentLevel--;
            }
        }

        void FlagButton<T>(
            T flag,
            string guiMessage,
            string toolTip,
            Func<IFlagHandler, T, bool> flagGetter,
            Action<IFlagHandler, T> flagToggler
        )
            where T : Enum
        {
            var previousValue = flagGetter(m_ConfigGeneration.FlagHandler, flag);
            var currentValue = EditorGUILayout.Toggle(
                new GUIContent(guiMessage, toolTip),
                previousValue
            );
            if (currentValue != previousValue)
            {
                flagToggler(m_ConfigGeneration.FlagHandler, flag);
            }
        }

        void RegenerateButton(string guiMessage, string command = "")
        {
            var rect = EditorGUI.IndentedRect(
                EditorGUILayout.GetControlRect(new GUILayoutOption[] { })
            );
            rect.width = 252;
            if (GUI.Button(rect, new GUIContent(guiMessage)))
            {
                switch (command)
                {
                    case "Regenerate editor arguments":
                        if (
                            m_ConfigGeneration.FlagHandler.ArgumentFlag.HasFlag(
                                ArgumentFlag.EditorArgument
                            )
                        )
                        {
                            EditorArguments = ExternalEditorWorkplaceDefaultArgument;
                        }
                        else
                        {
                            EditorArguments = ExternalEditorDefaultArgument;
                        }
                        break;
                    case "Regenerate config files":
                        m_ConfigGeneration.Sync();
                        break;
                    case "Regenerate project files":
                        m_ProjectGeneration.Sync();
                        break;
                    case "Reset VSCode settings":
                        m_ConfigGeneration.VSCodeSettings = "";
                        break;
                    case "Reset Workspace settings":
                        m_ConfigGeneration.WorkspaceSettings = "";
                        break;
                    case "Reset editorconfig settings":
                        m_ConfigGeneration.EditorConfigSettings = "";
                        break;
                    case "Reset Launch settings":
                        m_ConfigGeneration.LaunchConfigSettings = "";
                        break;
                    default:
                        UnityEngine.Debug.LogError("Unknown button pressed");
                        break;
                }
            }
        }

        #endregion

        #region ProjectSync

        public void CreateIfDoesntExist()
        {
            if (!m_ProjectGeneration.SolutionExists())
            {
                m_ProjectGeneration.Sync();
            }
        }

        public void SyncIfNeeded(
            string[] addedFiles,
            string[] deletedFiles,
            string[] movedFiles,
            string[] movedFromFiles,
            string[] importedFiles
        )
        {
            (
                m_ProjectGeneration.AssemblyNameProvider as IPackageInfoCache
            )?.ResetPackageInfoCache();
            m_ProjectGeneration.SyncIfNeeded(
                addedFiles.Union(deletedFiles).Union(movedFiles).Union(movedFromFiles).ToList(),
                importedFiles
            );
        }

        public void SyncAll()
        {
            (
                m_ProjectGeneration.AssemblyNameProvider as IPackageInfoCache
            )?.ResetPackageInfoCache();
            AssetDatabase.Refresh();
            m_ProjectGeneration.Sync();
        }

        #endregion

        #region InstallationManagement

        public bool TryGetInstallationForPath(
                    string editorPath,
                    out CodeEditor.Installation installation
                )
        {
            var lowerCasePath = editorPath.ToLower();
            var filename = Path.GetFileName(lowerCasePath).Replace(" ", "");
            var installations = Installations;
            if (!k_SupportedFileNames.Contains(filename))
            {
                installation = default;
                return false;
            }

            if (!installations.Any())
            {
                installation = new CodeEditor.Installation
                {
                    Name = "Visual Studio Code",
                    Path = editorPath
                };
            }
            else
            {
                try
                {
                    installation = installations.First(inst => inst.Path == editorPath);
                }
                catch (InvalidOperationException)
                {
                    installation = new CodeEditor.Installation
                    {
                        Name = "Visual Studio Code",
                        Path = editorPath
                    };
                }
            }

            return true;
        }

        public bool OpenProject(string path, int line, int column)
        {
            if (path != "" && (!SupportsExtension(path) || !File.Exists(path))) // Assets - Open C# Project passes empty path here
            {
                return false;
            }

            if (line == -1)
                line = 1;
            if (column == -1)
                column = 0;

            var workspacePath =
                $"{m_ProjectGeneration.ProjectDirectory}/{Path.GetFileName(m_ProjectGeneration.ProjectDirectory)}.code-workspace";

            string arguments;
            if (
                EditorArguments != ExternalEditorDefaultArgument
                && EditorArguments != ExternalEditorWorkplaceDefaultArgument
            )
            {
                arguments =
                    m_ProjectGeneration.ProjectDirectory != path
                        ? CodeEditor.ParseArgument(EditorArguments, path, line, column)
                        : workspacePath;
            }
            else
            {
                arguments = m_ConfigGeneration.FlagHandler.ArgumentFlag.HasFlag(
                    ArgumentFlag.EditorArgument
                )
                    ? $@"""{workspacePath}"""
                    : $@"""{m_ProjectGeneration.ProjectDirectory}""";
                if (m_ProjectGeneration.ProjectDirectory != path && path.Length != 0)
                {
                    arguments += $@" -g ""{path}"":{line}:{column}";
                }
            }

            if (IsOSX)
            {
                return OpenOSX(arguments);
            }

            var app = DefaultApp;
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = app,
                    Arguments = arguments,
                    WindowStyle = app.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                        ? ProcessWindowStyle.Hidden
                        : ProcessWindowStyle.Normal,
                    CreateNoWindow = true,
                    UseShellExecute = true,
                }
            };

            process.Start();
            return true;
        }

        static bool OpenOSX(string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-n \"{DefaultApp}\" --args {arguments}",
                    UseShellExecute = true,
                }
            };

            process.Start();
            return true;
        }

        public CodeEditor.Installation[] Installations => m_Discoverability.PathCallback();

        #endregion

        public VSCodeScriptEditor(
            IDiscovery discovery,
            IGenerator projectGeneration,
            IConfigGenerator configGeneration
        )
        {
            m_Discoverability = discovery;
            m_ProjectGeneration = projectGeneration;
            m_ConfigGeneration = configGeneration;
        }

        static VSCodeScriptEditor()
        {
            string projectDirectory = Directory.GetParent(Application.dataPath).FullName;

            var editor = new VSCodeScriptEditor(
                new VSCodeDiscovery(),
                new ProjectGeneration(projectDirectory),
                new ConfigGeneration(projectDirectory)
            );

            CodeEditor.Register(editor);

            if (IsVSCodeInstallation(CodeEditor.CurrentEditorInstallation))
            {
                editor.CreateIfDoesntExist();
            }
        }

        static bool IsVSCodeInstallation(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var lowerCasePath = path.ToLower();
            var filename = Path.GetFileName(
                    lowerCasePath
                        .Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar)
                )
                .Replace(" ", "");
            return k_SupportedFileNames.Contains(filename);
        }

        public void Initialize(string editorInstallationPath) { }
    }
}
