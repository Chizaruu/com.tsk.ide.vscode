using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Unity.CodeEditor;

namespace VSCodeEditor
{
    [InitializeOnLoad]
    public class VSCodeScriptEditor : IExternalCodeEditor
    {
        const string vscode_argument = "vscode_arguments";
        const string vscode_extension = "vscode_userExtensions";
        static readonly GUIContent k_ResetArguments = EditorGUIUtility.TrTextContent(
            "Reset argument"
        );
        string m_Arguments;
        readonly IDiscovery m_Discoverability;
        readonly IGenerator m_ProjectGeneration;

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

        static string DefaultArgument { get; } =
            "\"$(ProjectPath)/$(ProjectName).code-workspace\" -g \"$(File)\":$(Line):$(Column)";

        string Arguments
        {
            get => m_Arguments ??= EditorPrefs.GetString(vscode_argument, DefaultArgument);
            set
            {
                m_Arguments = value;
                EditorPrefs.SetString(vscode_argument, value);
            }
        }

        static string[] DefaultExtensions
        {
            get
            {
                var customExtensions = new[] { "json", "asmdef", "log" };
                return EditorSettings.projectGenerationBuiltinExtensions
                    .Concat(EditorSettings.projectGenerationUserExtensions)
                    .Concat(customExtensions)
                    .Distinct()
                    .ToArray();
            }
        }

        static string[] HandledExtensions
        {
            get
            {
                return HandledExtensionsString
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.TrimStart('.', '*'))
                    .ToArray();
            }
        }

        static string HandledExtensionsString
        {
            get => EditorPrefs.GetString(vscode_extension, string.Join(";", DefaultExtensions));
            set => EditorPrefs.SetString(vscode_extension, value);
        }

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

        public void OnGUI()
        {
            Arguments = EditorGUILayout.TextField("External Script Editor Args", Arguments);
            if (GUILayout.Button(k_ResetArguments, GUILayout.Width(120)))
            {
                Arguments = DefaultArgument;
            }

            EditorGUILayout.LabelField("Generate .csproj files for:");
            EditorGUI.indentLevel++;
            SettingsButton(ProjectGenerationFlag.Embedded, "Embedded packages", "");
            SettingsButton(ProjectGenerationFlag.Local, "Local packages", "");
            SettingsButton(ProjectGenerationFlag.Registry, "Registry packages", "");
            SettingsButton(ProjectGenerationFlag.Git, "Git packages", "");
            SettingsButton(ProjectGenerationFlag.BuiltIn, "Built-in packages", "");
#if UNITY_2019_3_OR_NEWER
            SettingsButton(ProjectGenerationFlag.LocalTarBall, "Local tarball", "");
#endif
            SettingsButton(ProjectGenerationFlag.Unknown, "Packages from unknown sources", "");
            RegenerateProjectFiles();
            EditorGUI.indentLevel--;

            HandledExtensionsString = EditorGUILayout.TextField(
                new GUIContent("Extensions handled: "),
                HandledExtensionsString
            );
        }

        void RegenerateProjectFiles()
        {
            var rect = EditorGUI.IndentedRect(
                EditorGUILayout.GetControlRect(new GUILayoutOption[] { })
            );
            rect.width = 252;
            if (GUI.Button(rect, "Regenerate project files"))
            {
                m_ProjectGeneration.Sync();
            }
        }

        void SettingsButton(ProjectGenerationFlag preference, string guiMessage, string toolTip)
        {
            var prevValue = m_ProjectGeneration.AssemblyNameProvider.ProjectGenerationFlag.HasFlag(
                preference
            );
            var newValue = EditorGUILayout.Toggle(new GUIContent(guiMessage, toolTip), prevValue);
            if (newValue != prevValue)
            {
                m_ProjectGeneration.AssemblyNameProvider.ToggleProjectGeneration(preference);
            }
        }

        public void CreateIfDoesntExist()
        {
            if (!m_ProjectGeneration.SolutionExists())
            {
                m_ProjectGeneration.Sync();
            }

            if (!HasNuGetFolder())
            {
                CreateNuGetFolder();
            }

            if (!HasRoslynDLL())
            {
                CreateRoslynDLL();
            }

            if (!HasOmniSharpConfig())
            {
                CreateOmniSharpConfig();
            }

            if (!HasEditorConfig())
            {
                CreateEditorConfig();
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
            if (Arguments != DefaultArgument)
            {
                arguments =
                    m_ProjectGeneration.ProjectDirectory != path
                        ? CodeEditor.ParseArgument(Arguments, path, line, column)
                        : workspacePath;
            }
            else
            {
                arguments = $@"""{workspacePath}""";
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

        private void CreateNuGetFolder()
        {
            var nugetFolder = Path.Combine(m_ProjectGeneration.ProjectDirectory, "NuGet");

            Directory.CreateDirectory(nugetFolder);
        }

        private bool HasNuGetFolder()
        {
            var nugetFolder = Path.Combine(m_ProjectGeneration.ProjectDirectory, "NuGet");
            return Directory.Exists(nugetFolder);
        }

        private void CreateRoslynDLL()
        {
            string unityRoslynDLL = Path.Combine(
                m_ProjectGeneration.ProjectDirectory,
                "Packages",
                "com.tsk.ide.vscode",
                "Editor",
                "NuGet",
                "Microsoft.Unity.Analyzers.dll.koala"
            );

            string nugetFolder = Path.Combine(m_ProjectGeneration.ProjectDirectory, "NuGet");
            string nugetRoslynDLL = Path.Combine(nugetFolder, "Microsoft.Unity.Analyzers.dll");

            File.Copy(unityRoslynDLL, nugetRoslynDLL);
        }

        private bool HasRoslynDLL()
        {
            string nugetFolder = Path.Combine(m_ProjectGeneration.ProjectDirectory, "NuGet");
            string nugetRoslynDLL = Path.Combine(nugetFolder, "Microsoft.Unity.Analyzers.dll");

            return File.Exists(nugetRoslynDLL);
        }

        private void CreateOmniSharpConfig()
        {
            string configFilePath = Path.Combine(
                m_ProjectGeneration.ProjectDirectory,
                "omnisharp.json"
            );

            const string Contents =
                @"{
        ""FormattingOptions"": {
            ""newLine"": ""\n"",
            ""useTabs"": false,
            ""tabSize"": 2,
            ""indentationSize"": 2,

            ""NewLinesForBracesInTypes"": false,
            ""NewLinesForBracesInMethods"": false,
            ""NewLinesForBracesInProperties"": false,
            ""NewLinesForBracesInAccessors"": false,
            ""NewLinesForBracesInAnonymousMethods"": false,
            ""NewLinesForBracesInControlBlocks"": false,
            ""NewLinesForBracesInAnonymousTypes"": false,
            ""NewLinesForBracesInObjectCollectionArrayInitializers"": false,
            ""NewLinesForBracesInLambdaExpressionBody"": false,

            ""NewLineForElse"": false,
            ""NewLineForCatch"": false,
            ""NewLineForFinally"": false,
            ""NewLineForMembersInObjectInit"": false,
            ""NewLineForMembersInAnonymousTypes"": false,
            ""NewLineForClausesInQuery"": false
            },";

            File.WriteAllText(
                configFilePath,
                /*lang=json,strict*/
                Contents
            );

            string roslynExtensionsOptions =
                @"
    ""RoslynExtensionsOptions"": {
        ""enableRoslynAnalyzers"": true,
        ""enableEditorConfigSupport"": true,
        ""sdkIncludePrereleases"": false,
        ""organizeImportsOnFormat"": true,
        ""threadsToUseForAnalyzers"": true,
        ""useModernNet"": true,
        ""documentAnalysisTimeoutMs"": 600000,
        ""LocationPaths"": [""./NuGet""]
    }
}";
            File.AppendAllText(configFilePath, roslynExtensionsOptions);
        }

        private bool HasOmniSharpConfig()
        {
            string configFilePath = Path.Combine(
                m_ProjectGeneration.ProjectDirectory,
                "omnisharp.json"
            );

            return File.Exists(configFilePath);
        }

        private void CreateEditorConfig()
        {
            string configFilePath = Path.Combine(
                m_ProjectGeneration.ProjectDirectory,
                ".editorconfig"
            );

            File.WriteAllText(
                configFilePath,
                @"root=true

[*.cs]
dotnet_diagnostic.IDE0051.severity = none"
            );
        }

        private bool HasEditorConfig()
        {
            string configFilePath = Path.Combine(
                m_ProjectGeneration.ProjectDirectory,
                ".editorconfig"
            );

            return File.Exists(configFilePath);
        }

        static bool SupportsExtension(string path)
        {
            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
                return false;
            return HandledExtensions.Contains(extension.TrimStart('.'));
        }

        public CodeEditor.Installation[] Installations => m_Discoverability.PathCallback();

        public VSCodeScriptEditor(IDiscovery discovery, IGenerator projectGeneration)
        {
            m_Discoverability = discovery;
            m_ProjectGeneration = projectGeneration;
        }

        static VSCodeScriptEditor()
        {
            var editor = new VSCodeScriptEditor(
                new VSCodeDiscovery(),
                new ProjectGeneration(Directory.GetParent(Application.dataPath).FullName)
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
