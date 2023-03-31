using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using SR = System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Profiling;
using System.Xml.Linq;

namespace VSCodeEditor
{
    public interface IGenerator
    {
        bool SyncIfNeeded(List<string> affectedFiles, string[] reimportedFiles);
        void Sync();
        string SolutionFile();
        string ProjectDirectory { get; }
        IAssemblyNameProvider AssemblyNameProvider { get; }
        void GenerateAll(bool generateAll);
        bool SolutionExists();
    }

    public class ProjectGeneration : IGenerator
    {
        enum ScriptingLanguage
        {
            None,
            CSharp
        }

        const string k_WindowsNewline = "\r\n";

        const string k_SettingsJson =
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
            @"{
	""folders"": [
		{
			""path"": "".""
		}
	]
}";

        /// <summary>
        /// Map source extensions to ScriptingLanguages
        /// </summary>
        static readonly Dictionary<string, ScriptingLanguage> k_BuiltinSupportedExtensions =
            new()
            {
                { "cs", ScriptingLanguage.CSharp },
                { "uxml", ScriptingLanguage.None },
                { "uss", ScriptingLanguage.None },
                { "shader", ScriptingLanguage.None },
                { "compute", ScriptingLanguage.None },
                { "cginc", ScriptingLanguage.None },
                { "hlsl", ScriptingLanguage.None },
                { "glslinc", ScriptingLanguage.None },
                { "template", ScriptingLanguage.None },
                { "raytrace", ScriptingLanguage.None }
            };

        readonly string m_SolutionProjectEntryTemplate = string.Join(
                "\r\n",
                @"Project(""{{{0}}}"") = ""{1}"", ""{2}"", ""{{{3}}}""",
                @"EndProject"
            )
            .Replace("    ", "\t");

        readonly string m_SolutionProjectConfigurationTemplate = string.Join(
                "\r\n",
                @"        {{{0}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
                @"        {{{0}}}.Debug|Any CPU.Build.0 = Debug|Any CPU"
            )
            .Replace("    ", "\t");

        static readonly string[] k_ReimportSyncExtensions = { ".dll", ".asmdef" };

        string[] m_ProjectSupportedExtensions = Array.Empty<string>();
        const string k_TargetLanguageVersion = "latest";

        public string ProjectDirectory { get; }
        IAssemblyNameProvider IGenerator.AssemblyNameProvider => m_AssemblyNameProvider;

        public void GenerateAll(bool generateAll)
        {
            m_AssemblyNameProvider.ToggleProjectGeneration(
                ProjectGenerationFlag.BuiltIn
                    | ProjectGenerationFlag.Embedded
                    | ProjectGenerationFlag.Git
                    | ProjectGenerationFlag.Local
#if UNITY_2019_3_OR_NEWER
                    | ProjectGenerationFlag.LocalTarBall
#endif
                    | ProjectGenerationFlag.PlayerAssemblies
                    | ProjectGenerationFlag.Registry
                    | ProjectGenerationFlag.Unknown
            );
        }

        readonly string m_ProjectName;
        readonly IAssemblyNameProvider m_AssemblyNameProvider;
        readonly IFileIO m_FileIOProvider;
        readonly IGUIDGenerator m_GUIDProvider;

        const string k_ToolsVersion = "4.0";
        const string k_ProductVersion = "10.0.20506";
        const string k_BaseDirectory = ".";
        const string k_TargetFrameworkVersion = "v4.7.1";

        public ProjectGeneration(string tempDirectory)
            : this(
                tempDirectory,
                new AssemblyNameProvider(),
                new FileIOProvider(),
                new GUIDProvider()
            ) { }

        public ProjectGeneration(
            string tempDirectory,
            IAssemblyNameProvider assemblyNameProvider,
            IFileIO fileIO,
            IGUIDGenerator guidGenerator
        )
        {
            ProjectDirectory = tempDirectory.NormalizePath();
            m_ProjectName = Path.GetFileName(ProjectDirectory);
            m_AssemblyNameProvider = assemblyNameProvider;
            m_FileIOProvider = fileIO;
            m_GUIDProvider = guidGenerator;
        }

        /// <summary>
        /// Syncs the scripting solution if any affected files are relevant.
        /// </summary>
        /// <returns>
        /// Whether the solution was synced.
        /// </returns>
        /// <param name='affectedFiles'>
        /// A set of files whose status has changed
        /// </param>
        /// <param name="reimportedFiles">
        /// A set of files that got reimported
        /// </param>
        public bool SyncIfNeeded(List<string> affectedFiles, string[] reimportedFiles)
        {
            Profiler.BeginSample("SolutionSynchronizerSync");
            SetupProjectSupportedExtensions();

            if (!HasFilesBeenModified(affectedFiles, reimportedFiles))
            {
                Profiler.EndSample();
                return false;
            }

            var assemblies = m_AssemblyNameProvider.GetAssemblies(ShouldFileBePartOfSolution);
            var allProjectAssemblies = RelevantAssembliesForMode(assemblies).ToList();
            SyncSolution(allProjectAssemblies);

            var allAssetProjectParts = GenerateAllAssetProjectParts();

            var affectedNames = affectedFiles
                .Select(asset => m_AssemblyNameProvider.GetAssemblyNameFromScriptPath(asset))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(
                    name => name.Split(new[] { ".dll" }, StringSplitOptions.RemoveEmptyEntries)[0]
                );
            var reimportedNames = reimportedFiles
                .Select(asset => m_AssemblyNameProvider.GetAssemblyNameFromScriptPath(asset))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(
                    name => name.Split(new[] { ".dll" }, StringSplitOptions.RemoveEmptyEntries)[0]
                );
            var affectedAndReimported = new HashSet<string>(affectedNames.Concat(reimportedNames));

            foreach (var assembly in allProjectAssemblies)
            {
                if (!affectedAndReimported.Contains(assembly.name))
                    continue;

                SyncProject(assembly, allAssetProjectParts, ParseResponseFileData(assembly));
            }

            Profiler.EndSample();
            return true;
        }

        bool HasFilesBeenModified(List<string> affectedFiles, string[] reimportedFiles)
        {
            return affectedFiles.Any(ShouldFileBePartOfSolution)
                || reimportedFiles.Any(ShouldSyncOnReimportedAsset);
        }

        static bool ShouldSyncOnReimportedAsset(string asset)
        {
            return k_ReimportSyncExtensions.Contains(new FileInfo(asset).Extension);
        }

        private static IEnumerable<SR.MethodInfo> GetPostProcessorCallbacks(string name)
        {
            return TypeCache
                .GetTypesDerivedFrom<AssetPostprocessor>()
                .Select(
                    t =>
                        t.GetMethod(
                            name,
                            SR.BindingFlags.Public
                                | SR.BindingFlags.NonPublic
                                | SR.BindingFlags.Static
                        )
                )
                .Where(m => m != null);
        }

        static void OnGeneratedCSProjectFiles()
        {
            foreach (var method in GetPostProcessorCallbacks(nameof(OnGeneratedCSProjectFiles)))
            {
                method.Invoke(null, Array.Empty<object>());
            }
        }

        private static string InvokeAssetPostProcessorGenerationCallbacks(
            string name,
            string path,
            string content
        )
        {
            foreach (var method in GetPostProcessorCallbacks(name))
            {
                var args = new[] { path, content };
                var returnValue = method.Invoke(null, args);
                if (method.ReturnType == typeof(string))
                {
                    // We want to chain content update between invocations
                    content = (string)returnValue;
                }
            }

            return content;
        }

        private static string OnGeneratedCSProject(string path, string content)
        {
            return InvokeAssetPostProcessorGenerationCallbacks(
                nameof(OnGeneratedCSProject),
                path,
                content
            );
        }

        private static string OnGeneratedSlnSolution(string path, string content)
        {
            return InvokeAssetPostProcessorGenerationCallbacks(
                nameof(OnGeneratedSlnSolution),
                path,
                content
            );
        }

        public void Sync()
        {
            SetupProjectSupportedExtensions();
            GenerateAndWriteSolutionAndProjects();

            OnGeneratedCSProjectFiles();
        }

        public bool SolutionExists()
        {
            return m_FileIOProvider.Exists(SolutionFile());
        }

        void SetupProjectSupportedExtensions()
        {
            m_ProjectSupportedExtensions = m_AssemblyNameProvider.ProjectSupportedExtensions;
        }

        bool ShouldFileBePartOfSolution(string file)
        {
            // Exclude files coming from packages except if they are internalized.
            if (m_AssemblyNameProvider.IsInternalizedPackagePath(file))
            {
                return false;
            }

            return HasValidExtension(file);
        }

        bool HasValidExtension(string file)
        {
            string extension = Path.GetExtension(file);

            // Dll's are not scripts but still need to be included..
            if (extension == ".dll")
                return true;

            if (file.ToLower().EndsWith(".asmdef"))
                return true;

            return IsSupportedExtension(extension);
        }

        bool IsSupportedExtension(string extension)
        {
            extension = extension.TrimStart('.');
            if (k_BuiltinSupportedExtensions.ContainsKey(extension))
                return true;
            if (m_ProjectSupportedExtensions.Contains(extension))
                return true;
            return false;
        }

        static ScriptingLanguage ScriptingLanguageFor(Assembly assembly)
        {
            return ScriptingLanguageFor(GetExtensionOfSourceFiles(assembly.sourceFiles));
        }

        static string GetExtensionOfSourceFiles(string[] files)
        {
            return files.Length > 0 ? GetExtensionOfSourceFile(files[0]) : "NA";
        }

        static string GetExtensionOfSourceFile(string file)
        {
            var ext = Path.GetExtension(file).ToLower();
            ext = ext[1..]; //strip dot
            return ext;
        }

        static ScriptingLanguage ScriptingLanguageFor(string extension)
        {
            return k_BuiltinSupportedExtensions.TryGetValue(
                extension.TrimStart('.'),
                out var result
            )
                ? result
                : ScriptingLanguage.None;
        }

        public void GenerateAndWriteSolutionAndProjects()
        {
            // Only synchronize assemblies that have associated source files and ones that we actually want in the project.
            // This also filters out DLLs coming from .asmdef files in packages.
            var assemblies = m_AssemblyNameProvider
                .GetAssemblies(ShouldFileBePartOfSolution)
                .ToArray();

            var allAssetProjectParts = GenerateAllAssetProjectParts();

            SyncSolution(assemblies);
            var allProjectAssemblies = RelevantAssembliesForMode(assemblies).ToList();
            foreach (Assembly assembly in allProjectAssemblies)
            {
                var responseFileData = ParseResponseFileData(assembly);
                SyncProject(assembly, allAssetProjectParts, responseFileData);
            }

            WriteVSCodeSettingsFiles();
            WriteWorkspaceFile();
        }

        List<ResponseFileData> ParseResponseFileData(Assembly assembly)
        {
            var systemReferenceDirectories = CompilationPipeline.GetSystemAssemblyDirectories(
                assembly.compilerOptions.ApiCompatibilityLevel
            );

            Dictionary<string, ResponseFileData> responseFilesData =
                assembly.compilerOptions.ResponseFiles.ToDictionary(
                    x => x,
                    x =>
                        m_AssemblyNameProvider.ParseResponseFile(
                            x,
                            ProjectDirectory,
                            systemReferenceDirectories
                        )
                );

            Dictionary<string, ResponseFileData> responseFilesWithErrors = responseFilesData
                .Where(x => x.Value.Errors.Any())
                .ToDictionary(x => x.Key, x => x.Value);

            if (responseFilesWithErrors.Any())
            {
                foreach (var error in responseFilesWithErrors)
                    foreach (var valueError in error.Value.Errors)
                    {
                        Debug.LogError($"{error.Key} Parse Error : {valueError}");
                    }
            }

            return responseFilesData.Select(x => x.Value).ToList();
        }

#if !UNITY_2022_2_OR_NEWER
        Dictionary<string, string> GenerateAllAssetProjectParts()
        {
            Dictionary<string, StringBuilder> stringBuilders = new();
#else 
        Dictionary<string, List<XElement>> GenerateAllAssetProjectParts()
        {
            Dictionary<string, List<XElement>> stringBuilders =
                new Dictionary<string, List<XElement>>();
#endif
            foreach (string asset in m_AssemblyNameProvider.GetAllAssetPaths())
            {
                // Exclude files coming from packages except if they are internalized.
                // TODO: We need assets from the assembly API
                if (m_AssemblyNameProvider.IsInternalizedPackagePath(asset))
                {
                    continue;
                }

                string extension = Path.GetExtension(asset);
                if (
                    IsSupportedExtension(extension)
                    && ScriptingLanguage.None == ScriptingLanguageFor(extension)
                )
                {
                    // Find assembly the asset belongs to by adding script extension and using compilation pipeline.
                    var assemblyName = m_AssemblyNameProvider.GetAssemblyNameFromScriptPath(asset);

                    if (string.IsNullOrEmpty(assemblyName))
                    {
                        continue;
                    }

                    assemblyName = Path.GetFileNameWithoutExtension(assemblyName);

                    if (!stringBuilders.TryGetValue(assemblyName, out var projectBuilder))
                    {
#if !UNITY_2022_2_OR_NEWER
                        projectBuilder = new StringBuilder();
#else
                        projectBuilder = new List<XElement>();
#endif
                        stringBuilders[assemblyName] = projectBuilder;
                    }

#if !UNITY_2022_2_OR_NEWER
                    projectBuilder
                        .Append("     <None Include=\"")
                        .Append(m_FileIOProvider.EscapedRelativePathFor(asset, ProjectDirectory))
                        .Append("\" />")
                        .Append(k_WindowsNewline);
#else   
                    var noneElement = new XElement("None");
                    noneElement.SetAttributeValue(
                        "Include",
                        m_FileIOProvider.EscapedRelativePathFor(asset, ProjectDirectory)
                    );
                    projectBuilder.Add(noneElement);
#endif
                }
            }

#if !UNITY_2022_2_OR_NEWER
            var result = new Dictionary<string, string>();
 #else
            var result = new Dictionary<string, List<XElement>>();
#endif

            foreach (var entry in stringBuilders)
            {
#if !UNITY_2022_2_OR_NEWER
                result[entry.Key] = entry.Value.ToString();
#else
                result[entry.Key] = entry.Value;
#endif
            }

            return result;
        }

        void SyncProject(
            Assembly assembly,
#if !UNITY_2022_2_OR_NEWER
            Dictionary<string, string> allAssetsProjectParts,
#else
            Dictionary<string, List<XElement>> allAssetsProjectParts,
#endif
            List<ResponseFileData> responseFilesData
        )
        {
            SyncProjectFileIfNotChanged(
                ProjectFile(assembly),
                ProjectText(assembly, allAssetsProjectParts, responseFilesData)
            );
        }

        void SyncProjectFileIfNotChanged(string path, string newContents)
        {
            if (Path.GetExtension(path) == ".csproj")
            {
                newContents = OnGeneratedCSProject(path, newContents);
            }

            SyncFileIfNotChanged(path, newContents);
        }

        void SyncSolutionFileIfNotChanged(string path, string newContents)
        {
            newContents = OnGeneratedSlnSolution(path, newContents);

            SyncFileIfNotChanged(path, newContents);
        }

        void SyncFileIfNotChanged(string filename, string newContents)
        {
            try
            {
                if (
                    m_FileIOProvider.Exists(filename)
                    && newContents == m_FileIOProvider.ReadAllText(filename)
                )
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            m_FileIOProvider.WriteAllText(filename, newContents);
        }

#if !UNITY_2022_2_OR_NEWER
        string ProjectText(
            Assembly assembly,
            Dictionary<string, string> allAssetsProjectParts,
            List<ResponseFileData> responseFilesData
        )
        {
            var projectBuilder = new StringBuilder();
            ProjectHeader(assembly, responseFilesData, projectBuilder);
            var references = new List<string>();

            foreach (string file in assembly.sourceFiles)
            {
                var fullFile = m_FileIOProvider.EscapedRelativePathFor(file, ProjectDirectory);
                projectBuilder
                    .Append("     <Compile Include=\"")
                    .Append(fullFile)
                    .Append("\" />")
                    .Append(k_WindowsNewline);
            }

            // Append additional non-script files that should be included in project generation.
            if (
                allAssetsProjectParts.TryGetValue(assembly.name, out var additionalAssetsForProject)
            )
                projectBuilder.Append(additionalAssetsForProject);

            var responseRefs = responseFilesData.SelectMany(
                x => x.FullPathReferences.Select(r => r)
            );
            var internalAssemblyReferences = assembly.assemblyReferences
                .Where(i => !i.sourceFiles.Any(ShouldFileBePartOfSolution))
                .Select(i => i.outputPath);
            var allReferences = assembly.compiledAssemblyReferences
                .Union(responseRefs)
                .Union(references)
                .Union(internalAssemblyReferences);

            foreach (var reference in allReferences)
            {
                string fullReference = Path.IsPathRooted(reference)
                    ? reference
                    : Path.Combine(ProjectDirectory, reference);
                AppendReference(fullReference, projectBuilder);
            }

            if (0 < assembly.assemblyReferences.Length)
            {
                projectBuilder.Append("  </ItemGroup>").Append(k_WindowsNewline);
                projectBuilder.Append("  <ItemGroup>").Append(k_WindowsNewline);
                foreach (
                    Assembly reference in assembly.assemblyReferences.Where(
                        i => i.sourceFiles.Any(ShouldFileBePartOfSolution)
                    )
                )
                {
                    projectBuilder
                        .Append("    <ProjectReference Include=\"")
                        .Append(reference.name)
                        .Append(GetProjectExtension())
                        .Append("\">")
                        .Append(k_WindowsNewline);
                    projectBuilder
                        .Append("      <Project>{")
                        .Append(ProjectGuid(reference.name))
                        .Append("}</Project>")
                        .Append(k_WindowsNewline);
                    projectBuilder
                        .Append("      <Name>")
                        .Append(reference.name)
                        .Append("</Name>")
                        .Append(k_WindowsNewline);
                    projectBuilder.Append("    </ProjectReference>").Append(k_WindowsNewline);
                }
            }

            projectBuilder.Append(ProjectFooter());
            return projectBuilder.ToString();
        }

        static void AppendReference(string fullReference, StringBuilder projectBuilder)
        {
            var escapedFullPath = SecurityElement.Escape(fullReference);
            escapedFullPath = escapedFullPath.NormalizePath();
            projectBuilder
                .Append("    <Reference Include=\"")
                .Append(Path.GetFileNameWithoutExtension(escapedFullPath))
                .Append("\">")
                .Append(k_WindowsNewline);
            projectBuilder
                .Append("        <HintPath>")
                .Append(escapedFullPath)
                .Append("</HintPath>")
                .Append(k_WindowsNewline);
            projectBuilder.Append("    </Reference>").Append(k_WindowsNewline);
        }

        private void ProjectHeader(
            Assembly assembly,
            List<ResponseFileData> responseFilesData,
            StringBuilder builder
        )
        {
            var otherArguments = GetOtherArgumentsFromResponseFilesData(responseFilesData);
            GetProjectHeaderTemplate(
                builder,
                ProjectGuid(assembly.name),
                assembly.name,
                string.Join(
                    ";",
                    new[] { "DEBUG", "TRACE" }
                        .Concat(assembly.defines)
                        .Concat(responseFilesData.SelectMany(x => x.Defines))
                        .Concat(EditorUserBuildSettings.activeScriptCompilationDefines)
                        .Distinct()
                        .ToArray()
                ),
                GenerateLangVersion(otherArguments["langversion"], assembly),
                assembly.compilerOptions.AllowUnsafeCode | responseFilesData.Any(x => x.Unsafe),
                GenerateAnalyserItemGroup(RetrieveRoslynAnalyzers(assembly, otherArguments)),
                GenerateRoslynAnalyzerRulesetPath(assembly, otherArguments)
            );
        }
#else

        private const string SDKStyleCsProj =
            @"
        <Project Sdk=""Microsoft.NET.Sdk"">
        <PropertyGroup>
            <TargetFramework>netstandard2.1</TargetFramework>
            <DisableImplicitNamespaceImports>true</DisableImplicitNamespaceImports>
        </PropertyGroup>
        <PropertyGroup>
            <DefaultItemExcludes>$(DefaultItemExcludes);Library/;**/*.*</DefaultItemExcludes>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
        </PropertyGroup>
        </Project>
        ";

        string ProjectText(
            Assembly assembly,
            Dictionary<string, List<XElement>> allAssetsProjectParts,
            List<ResponseFileData> responseFilesData
        )
        {
            // We parse the sdk style project into an XML Document we can then add to :D
            var document = XDocument.Parse(SDKStyleCsProj);
            var project = document.Element("Project");
            var targetFrameWork = project.Elements().First().Element("TargetFramework");

            var group = BuildPipeline.GetBuildTargetGroup(
                EditorUserBuildSettings.activeBuildTarget
            );
            var netSettings = PlayerSettings.GetApiCompatibilityLevel(group);

            targetFrameWork.Value = GetTargetFrameworkVersion(netSettings);

            AddCommonProperties(assembly, responseFilesData, project);

            // we have source files
            if (assembly.sourceFiles.Length != 0)
            {
                var itemGroup = new XElement("ItemGroup");

                foreach (var file in assembly.sourceFiles)
                {
                    var fullFile = m_FileIOProvider.EscapedRelativePathFor(file, ProjectDirectory);
                    itemGroup.Add(
                        new XElement("Compile", new XAttribute("Include", $"{fullFile}"))
                    );
                }

                project.Add(itemGroup);
            }

            // // Append additional non-script files that should be included in project generation.
            if (
                allAssetsProjectParts.TryGetValue(assembly.name, out var additionalAssetsForProject)
            )
            {
                var itemGroup = new XElement("ItemGroup");
                itemGroup.Add(additionalAssetsForProject);
                project.Add(itemGroup);
            }

            var responseRefs = responseFilesData.SelectMany(
                x => x.FullPathReferences.Select(r => r)
            );
            var internalAssemblyReferences = assembly.assemblyReferences
                .Where(i => !i.sourceFiles.Any(ShouldFileBePartOfSolution))
                .Select(i => i.outputPath);
            var allReferences = assembly.compiledAssemblyReferences
                .Union(responseRefs)
                .Union(internalAssemblyReferences);

            if (allReferences.Any())
            {
                var refItemGroup = new XElement("ItemGroup");
                foreach (var reference in allReferences)
                {
                    string fullReference = Path.IsPathRooted(reference)
                        ? reference
                        : Path.Combine(ProjectDirectory, reference);
                    AppendReference(fullReference, refItemGroup);
                }

                project.Add(refItemGroup);
            }

            if (assembly.assemblyReferences.Any())
            {
                var assemblyRefItemGroup = new XElement("ItemGroup");
                foreach (
                    Assembly reference in assembly.assemblyReferences.Where(
                        i => i.sourceFiles.Any(ShouldFileBePartOfSolution)
                    )
                )
                {
                    var packRefElement = new XElement(
                        "ProjectReference",
                        new XAttribute("Include", $"{reference.name}{GetProjectExtension()}")
                    );

                    assemblyRefItemGroup.Add(packRefElement);
                }

                project.Add(assemblyRefItemGroup);
            }

            return document.ToString();
        }

        static void AppendReference(string fullReference, XElement projectBuilder)
        {
            var escapedFullPath = SecurityElement.Escape(fullReference);
            escapedFullPath = escapedFullPath.NormalizePath();

            var reference = new XElement(
                "Reference",
                new XAttribute("Include", Path.GetFileNameWithoutExtension(escapedFullPath))
            );

            var hintPath = new XElement("HintPath");
            hintPath.Value = escapedFullPath;
            reference.Add(hintPath);
            projectBuilder.Add(reference);
        }

        private void AddCommonProperties(
            Assembly assembly,
            List<ResponseFileData> responseFilesData,
            XElement builder
        )
        {
            var otherArguments = GetOtherArgumentsFromResponseFilesData(responseFilesData);

            // Language version
            var langVersion = GenerateLangVersion(otherArguments["langversion"], assembly);

            var commonPropertyGroup = new XElement("PropertyGroup");
            var langElement = new XElement("LangVersion");
            langElement.Value = langVersion;
            commonPropertyGroup.Add(langElement);

            // Allow unsafe code
            bool allowUnsafeCode =
                assembly.compilerOptions.AllowUnsafeCode | responseFilesData.Any(x => x.Unsafe);

            var unsafeElement = new XElement("AllowUnsafeBlocks");
            unsafeElement.Value = allowUnsafeCode.ToString();
            commonPropertyGroup.Add(unsafeElement);

            var warningLevel = new XElement("WarningLevel", "4");
            commonPropertyGroup.Add(warningLevel);

            var noStdLib = new XElement("NoStdLib", "true");
            commonPropertyGroup.Add(noStdLib);

            var assemblyNameElement = new XElement("AssemblyName", assembly.name);
            commonPropertyGroup.Add(assemblyNameElement);

            // we need to grab all the defines and add them to a property group
            var defines = string.Join(
                ";",
                new[] { "DEBUG", "TRACE" }
                    .Concat(assembly.defines)
                    .Concat(responseFilesData.SelectMany(x => x.Defines))
                    .Concat(EditorUserBuildSettings.activeScriptCompilationDefines)
                    .Distinct()
                    .ToArray()
            );
            var definePropertyGroup = new XElement("PropertyGroup");
            var definesElement = new XElement("DefineConstants");
            definesElement.Value = defines;
            definePropertyGroup.Add(definesElement);
            builder.Add(definePropertyGroup);

            var ruleSets = GenerateRoslynAnalyzerRulesetPath(assembly, otherArguments);
            if (ruleSets.Length != 0)
            {


                if (ruleSets.Length != 0)
                {
                    foreach (var item in ruleSets)
                    {
                        var ruleElement = new XElement("CodeAnalysisRuleSet");
                        ruleElement.Value = item;
                        commonPropertyGroup.Add(ruleElement);
                    }
                }
            }

            builder.Add(commonPropertyGroup);
        }
#endif

        public string ProjectFile(Assembly assembly)
        {
            var fileBuilder = new StringBuilder(assembly.name);
            fileBuilder.Append(".csproj");
            return Path.Combine(ProjectDirectory, fileBuilder.ToString());
        }

        public string SolutionFile()
        {
            return Path.Combine(ProjectDirectory, $"{m_ProjectName}.sln");
        }

        private static string GenerateLangVersion(
            IEnumerable<string> langVersionList,
            Assembly assembly
        )
        {
            var langVersion = langVersionList.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(langVersion))
                return langVersion;
#if UNITY_2020_2_OR_NEWER
            return assembly.compilerOptions.LanguageVersion;
#else
            return k_TargetLanguageVersion;
#endif
        }

#if !UNITY_2022_2_OR_NEWER
        private static string GenerateRoslynAnalyzerRulesetPath(
            Assembly assembly,
            ILookup<string, string> otherResponseFilesData
        )
        {
#if UNITY_2020_2_OR_NEWER
            return GenerateAnalyserRuleSet(
                otherResponseFilesData["ruleset"]
                    .Append(assembly.compilerOptions.RoslynAnalyzerRulesetPath)
                    .Where(a => !string.IsNullOrEmpty(a))
                    .Distinct()
                    .Select(x => MakeAbsolutePath(x).NormalizePath())
                    .ToArray()
            );
#else
            return GenerateAnalyserRuleSet(
                otherResponseFilesData["ruleset"]
                    .Distinct()
                    .Select(x => MakeAbsolutePath(x).NormalizePath())
                    .ToArray()
            );
#endif
        }

#else
        private static string[] GenerateRoslynAnalyzerRulesetPath(
            Assembly assembly,
            ILookup<string, string> otherResponseFilesData
        )
        {
#if UNITY_2020_2_OR_NEWER
            return otherResponseFilesData["ruleset"].Append(assembly.compilerOptions.RoslynAnalyzerRulesetPath).Where(a => !string.IsNullOrEmpty(a)).Distinct().Select(x => MakeAbsolutePath(x).NormalizePath()).ToArray();
#else
            return otherResponseFilesData["ruleset"]
                .Distinct()
                .Select(x => MakeAbsolutePath(x).NormalizePath())
                .ToArray();
#endif
        }
#endif

        private static string GenerateAnalyserRuleSet(string[] paths)
        {
            return paths.Length == 0
                ? string.Empty
                : $"{Environment.NewLine}{string.Join(Environment.NewLine, paths.Select(a => $"    <CodeAnalysisRuleSet>{a}</CodeAnalysisRuleSet>"))}";
        }

        private static string MakeAbsolutePath(string path)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
        }

        private static ILookup<string, string> GetOtherArgumentsFromResponseFilesData(
            List<ResponseFileData> responseFilesData
        )
        {
            var paths = responseFilesData
                .SelectMany(x =>
                {
                    return x.OtherArguments
                        .Where(a => a.StartsWith("/") || a.StartsWith("-"))
                        .Select(b =>
                        {
                            var index = b.IndexOf(":", StringComparison.Ordinal);
                            if (index > 0 && b.Length > index)
                            {
                                var key = b[1..index];
                                return new KeyValuePair<string, string>(key, b[(index + 1)..]);
                            }

                            const string warnaserror = "warnaserror";
                            return b[1..].StartsWith(warnaserror)
                                ? new KeyValuePair<string, string>(
                                    warnaserror,
                                    b[(warnaserror.Length + 1)..]
                                )
                                : default;
                        });
                })
                .Distinct()
                .ToLookup(o => o.Key, pair => pair.Value);
            return paths;
        }

        string[] RetrieveRoslynAnalyzers(Assembly assembly, ILookup<string, string> otherArguments)
        {
#if UNITY_2020_2_OR_NEWER
            return otherArguments["analyzer"]
                .Concat(otherArguments["a"])
                .SelectMany(x => x.Split(';'))
#if !ROSLYN_ANALYZER_FIX
                .Concat(m_AssemblyNameProvider.GetRoslynAnalyzerPaths())
#else
        .Concat(assembly.compilerOptions.RoslynAnalyzerDllPaths)
#endif
                .Select(MakeAbsolutePath)
                .Distinct()
                .ToArray();
#else
            return otherArguments["analyzer"]
                .Concat(otherArguments["a"])
                .SelectMany(x => x.Split(';'))
                .Distinct()
                .Select(MakeAbsolutePath)
                .ToArray();
#endif
        }

        static string GenerateAnalyserItemGroup(string[] paths)
        {
            if (paths.Length == 0)
            {
                return string.Empty;
            }

            var analyserBuilder = new StringBuilder();
            analyserBuilder.Append("  <ItemGroup>").Append(k_WindowsNewline);
            foreach (var path in paths)
            {
                analyserBuilder
                    .Append($"    <Analyzer Include=\"{path.NormalizePath()}\" />")
                    .Append(k_WindowsNewline);
            }

            analyserBuilder.Append("  </ItemGroup>").Append(k_WindowsNewline);
            return analyserBuilder.ToString();
        }

        static string GetSolutionText()
        {
            return string.Join(
                    "\r\n",
                    @"",
                    @"Microsoft Visual Studio Solution File, Format Version {0}",
                    @"# Visual Studio {1}",
                    @"{2}",
                    @"Global",
                    @"    GlobalSection(SolutionConfigurationPlatforms) = preSolution",
                    @"        Debug|Any CPU = Debug|Any CPU",
                    @"    EndGlobalSection",
                    @"    GlobalSection(ProjectConfigurationPlatforms) = postSolution",
                    @"{3}",
                    @"    EndGlobalSection",
                    @"    GlobalSection(SolutionProperties) = preSolution",
                    @"        HideSolutionNode = FALSE",
                    @"    EndGlobalSection",
                    @"EndGlobal",
                    @""
                )
                .Replace("    ", "\t");
        }

        static string GetProjectFooterTemplate()
        {
            return string.Join(
                "\r\n",
                @"  </ItemGroup>",
                @"  <Import Project=""$(MSBuildToolsPath)\Microsoft.CSharp.targets"" />",
                @"  <!-- To modify your build process, add your task inside one of the targets below and uncomment it.",
                @"       Other similar extension points exist, see Microsoft.Common.targets.",
                @"  <Target Name=""BeforeBuild"">",
                @"  </Target>",
                @"  <Target Name=""AfterBuild"">",
                @"  </Target>",
                @"  -->",
                @"</Project>",
                @""
            );
        }

#if !UNITY_2022_2_OR_NEWER
        static void GetProjectHeaderTemplate(
            StringBuilder builder,
            string assemblyGUID,
            string assemblyName,
            string defines,
            string langVersion,
            bool allowUnsafe,
            string analyzerBlock,
            string rulesetBlock
        )
        {
            builder.Append(@"<?xml version=""1.0"" encoding=""utf-8""?>").Append(k_WindowsNewline);
            builder
                .Append(@"<Project ToolsVersion=""")
                .Append(k_ToolsVersion)
                .Append(@""" DefaultTargets=""Build"" xmlns=""")
                .Append("http://schemas.microsoft.com/developer/msbuild/2003")
                .Append(@""">")
                .Append(k_WindowsNewline);
            builder.Append(@"  <PropertyGroup>").Append(k_WindowsNewline);
            builder
                .Append(@"    <LangVersion>")
                .Append(langVersion)
                .Append("</LangVersion>")
                .Append(k_WindowsNewline);
            builder.Append(@"  </PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"  <PropertyGroup>").Append(k_WindowsNewline);
            builder
                .Append(
                    @"    <Configuration Condition="" '$(Configuration)' == '' "">Debug</Configuration>"
                )
                .Append(k_WindowsNewline);
            builder
                .Append(@"    <Platform Condition="" '$(Platform)' == '' "">AnyCPU</Platform>")
                .Append(k_WindowsNewline);
            builder
                .Append(@"    <ProductVersion>")
                .Append(k_ProductVersion)
                .Append("</ProductVersion>")
                .Append(k_WindowsNewline);
            builder.Append(@"    <SchemaVersion>2.0</SchemaVersion>").Append(k_WindowsNewline);
            builder
                .Append(@"    <RootNamespace>")
                .Append(EditorSettings.projectGenerationRootNamespace)
                .Append("</RootNamespace>")
                .Append(k_WindowsNewline);
            builder
                .Append(@"    <ProjectGuid>{")
                .Append(assemblyGUID)
                .Append("}</ProjectGuid>")
                .Append(k_WindowsNewline);
            builder.Append(@"    <OutputType>Library</OutputType>").Append(k_WindowsNewline);
            builder
                .Append(@"    <AppDesignerFolder>Properties</AppDesignerFolder>")
                .Append(k_WindowsNewline);
            builder
                .Append(@"    <AssemblyName>")
                .Append(assemblyName)
                .Append("</AssemblyName>")
                .Append(k_WindowsNewline);
            builder
                .Append(@"    <TargetFrameworkVersion>")
                .Append(
                    GetTargetFrameworkVersion(
                        PlayerSettings.GetApiCompatibilityLevel(
                            UnityEditor.Build.NamedBuildTarget.Standalone
                        )
                    )
                )
                .Append("</TargetFrameworkVersion>")
                .Append(k_WindowsNewline);
            builder.Append(@"    <FileAlignment>512</FileAlignment>").Append(k_WindowsNewline);
            builder
                .Append(@"    <BaseDirectory>")
                .Append(k_BaseDirectory)
                .Append("</BaseDirectory>")
                .Append(k_WindowsNewline);
            builder.Append(@"  </PropertyGroup>").Append(k_WindowsNewline);
            builder
                .Append(
                    @"  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' "">"
                )
                .Append(k_WindowsNewline);
            builder.Append(@"    <DebugSymbols>true</DebugSymbols>").Append(k_WindowsNewline);
            builder.Append(@"    <DebugType>full</DebugType>").Append(k_WindowsNewline);
            builder.Append(@"    <Optimize>false</Optimize>").Append(k_WindowsNewline);
            builder
                .Append(@"    <OutputPath>Temp\bin\Debug\</OutputPath>")
                .Append(k_WindowsNewline);
            builder
                .Append(@"    <DefineConstants>")
                .Append(defines)
                .Append("</DefineConstants>")
                .Append(k_WindowsNewline);
            builder.Append(@"    <ErrorReport>prompt</ErrorReport>").Append(k_WindowsNewline);
            builder.Append(@"    <WarningLevel>4</WarningLevel>").Append(k_WindowsNewline);
            builder.Append(@"    <NoWarn>0169</NoWarn>").Append(k_WindowsNewline);
            builder
                .Append(@"    <AllowUnsafeBlocks>")
                .Append(allowUnsafe)
                .Append("</AllowUnsafeBlocks>")
                .Append(k_WindowsNewline);
            builder.Append(@"  </PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"  <PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(@"    <NoConfig>true</NoConfig>").Append(k_WindowsNewline);
            builder.Append(@"    <NoStdLib>true</NoStdLib>").Append(k_WindowsNewline);
            builder
                .Append(
                    @"    <AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences>"
                )
                .Append(k_WindowsNewline);
            builder
                .Append(
                    @"    <ImplicitlyExpandNETStandardFacades>false</ImplicitlyExpandNETStandardFacades>"
                )
                .Append(k_WindowsNewline);
            builder
                .Append(
                    @"    <ImplicitlyExpandDesignTimeFacades>false</ImplicitlyExpandDesignTimeFacades>"
                )
                .Append(k_WindowsNewline);
            builder.Append(rulesetBlock);
            builder.Append(@"  </PropertyGroup>").Append(k_WindowsNewline);
            builder.Append(analyzerBlock);
            builder.Append(@"  <ItemGroup>").Append(k_WindowsNewline);
        }
#endif

        private static string GetTargetFrameworkVersion(ApiCompatibilityLevel netSettings)
        {
            switch (netSettings)
            {
                case ApiCompatibilityLevel.NET_2_0:
                case ApiCompatibilityLevel.NET_2_0_Subset:
#if !UNITY_2021_1_OR_NEWER
                case ApiCompatibilityLevel.NET_4_6:
#endif
                case ApiCompatibilityLevel.NET_Web:
                case ApiCompatibilityLevel.NET_Micro:
                    return k_TargetFrameworkVersion;
#if !UNITY_2021_1_OR_NEWER
                case ApiCompatibilityLevel.NET_Standard_2_0:
                    return "netstandard2.0";
#endif
#if UNITY_2021_1_OR_NEWER
                case ApiCompatibilityLevel.NET_Standard:
                    return "netstandard2.1";
                case ApiCompatibilityLevel.NET_Unity_4_8:
                #if !UNITY_2022_2_OR_NEWER
                    return "v4.8";
                #else
                    return "net48";
                #endif
#else
#endif
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        void SyncSolution(IEnumerable<Assembly> assemblies)
        {
            SyncSolutionFileIfNotChanged(SolutionFile(), SolutionText(assemblies));
        }

        string SolutionText(IEnumerable<Assembly> assemblies)
        {
            var fileversion = "11.00";
            var vsversion = "2010";

            var relevantAssemblies = RelevantAssembliesForMode(assemblies);
            string projectEntries = GetProjectEntries(relevantAssemblies);
            string projectConfigurations = string.Join(
                k_WindowsNewline,
                relevantAssemblies
                    .Select(i => GetProjectActiveConfigurations(ProjectGuid(i.name)))
                    .ToArray()
            );
            return string.Format(
                GetSolutionText(),
                fileversion,
                vsversion,
                projectEntries,
                projectConfigurations
            );
        }

        static IEnumerable<Assembly> RelevantAssembliesForMode(IEnumerable<Assembly> assemblies)
        {
            return assemblies.Where(i => ScriptingLanguage.CSharp == ScriptingLanguageFor(i));
        }

        /// <summary>
        /// Get a Project("{guid}") = "MyProject", "MyProject.csproj", "{projectguid}"
        /// entry for each relevant language
        /// </summary>
        string GetProjectEntries(IEnumerable<Assembly> assemblies)
        {
            var projectEntries = assemblies.Select(
                i =>
                    string.Format(
                        m_SolutionProjectEntryTemplate,
                        SolutionGuid(i),
                        i.name,
                        Path.GetFileName(ProjectFile(i)),
                        ProjectGuid(i.name)
                    )
            );

            return string.Join(k_WindowsNewline, projectEntries.ToArray());
        }

        /// <summary>
        /// Generate the active configuration string for a given project guid
        /// </summary>
        string GetProjectActiveConfigurations(string projectGuid)
        {
            return string.Format(m_SolutionProjectConfigurationTemplate, projectGuid);
        }

        static string SkipPathPrefix(string path, string prefix)
        {
            if (path.StartsWith($@"{prefix}{Path.DirectorySeparatorChar}"))
                return path[(prefix.Length + 1)..];
            return path;
        }

        string ProjectGuid(string assembly)
        {
            return m_GUIDProvider.ProjectGuid(m_ProjectName, assembly);
        }

        string SolutionGuid(Assembly assembly)
        {
            return m_GUIDProvider.SolutionGuid(
                m_ProjectName,
                GetExtensionOfSourceFiles(assembly.sourceFiles)
            );
        }

        static string ProjectFooter()
        {
            return GetProjectFooterTemplate();
        }

        static string GetProjectExtension()
        {
            return ".csproj";
        }

        void WriteVSCodeSettingsFiles()
        {
            var vsCodeDirectory = Path.Combine(ProjectDirectory, ".vscode");

            if (!m_FileIOProvider.Exists(vsCodeDirectory))
                m_FileIOProvider.CreateDirectory(vsCodeDirectory);

            var vsCodeSettingsJson = Path.Combine(vsCodeDirectory, "settings.json");

            if (!m_FileIOProvider.Exists(vsCodeSettingsJson))
                m_FileIOProvider.WriteAllText(vsCodeSettingsJson, k_SettingsJson);
        }

        void WriteWorkspaceFile()
        {
            var workspaceFile = Path.Combine(ProjectDirectory, $"{m_ProjectName}.code-workspace");

            if (!m_FileIOProvider.Exists(workspaceFile))
                m_FileIOProvider.WriteAllText(workspaceFile, k_WorkspaceJson);
        }
    }

    public static class SolutionGuidGenerator
    {
        static readonly MD5 mD5 = MD5CryptoServiceProvider.Create();

        public static string GuidForProject(string projectName)
        {
            return ComputeGuidHashFor(projectName + "salt");
        }

        public static string GuidForSolution(string projectName, string sourceFileExtension)
        {
            return "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";
        }

        static string ComputeGuidHashFor(string input)
        {
            var hash = mD5.ComputeHash(Encoding.Default.GetBytes(input));
            return new Guid(hash).ToString();
        }
    }
}
