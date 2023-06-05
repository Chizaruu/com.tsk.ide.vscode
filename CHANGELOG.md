# Code Editor Package for Visual Studio Code

## [1.4.1] - 2023-06-05

- Refactored configuration defaults and removed redundant options from omnisharp.json. - Thanks @ershn
- Added support for renaming methods and classes using Omnisharp.
- Updated settings.json to correctly utilize useModernNet, sdkIncludePrereleases, and organizeImportsOnFormat.

## [1.4.0] - 2023-06-04

- Refactored code to improve platform compatibility

## [1.3.9] - 2023-05-18

- .csproj files are separated into their own folders within a main folder called "CSProjFolders" - Thanks @frarees
- The ProjectReference element now includes a direct path to the file.
  - Project element added
  - Name element added
- Compile element now includes a direct path to the file.
- GenerateAssemblyInfo property set to false
- Start of `dotnet build` support

## [1.3.7] - 2023-05-16

- Added the option to disable adding Analyzers to all packages
  - This means that it will only be added to .csproj files originating from the Assets Folder.

## [1.3.6] - 2023-05-10

- [Added config section to Externals Tools](https://github.com/Chizaruu/com.tsk.ide.vscode/commit/8da03f5885c268eaecdfd904e6a324be9bf9b82a)
- [Added Foldouts to remove visual clutter](https://github.com/Chizaruu/com.tsk.ide.vscode/commit/ba73359980ad49f8eca6d485eda6f54e76a2bbff)
- A "TSKDoNotDelete" text file is generated when the package is first added. This was done to counter the constant auto-generation that occurred when you would delete the config files.
- [Added Configurable Config Settings](https://github.com/Chizaruu/com.tsk.ide.vscode/commit/d7a33951346235a91606e258cb206cc68e65e723)

## [1.3.4] - 2023-04-07

- Minimum Unity version is now 2021.3
- Added support for 2021/2022 NetStandard Editor Assemblies Compatibility Level (Not really, it's just a workaround until users start using 2023)
- Added toggle for preferred default arguments for opening VSCode (You can choose between user or workspace)
- Restructured the repository to be more in line with Unity's architecture (See <https://github.com/Unity-Technologies/com.unity.cinemachine> for more info)

## [1.3.2] - 2023-04-05

- Removed koalafied Microsoft.Unity.Analyzers dll
- Removed LocationPaths option from omnisharp.json
- Added support for Nuget package Microsoft.Unity.Analyzers (See ProjectGeneration.cs for more info)

## [1.3.1] - 2023-04-01

- Moved .omnisharp, .editorconfig, and analyzer generation to ProjectGeneration.cs to be in line with the rest of the project
- Simplified XElement creation in ProjectGeneration.cs to be more readable

## [1.3.0] - 2023-04-01

- Added complete SDK support, this allows you to use Omnisharp's useModernNet in your project (Special thanks to Andrew Spiering @wackoisgod)
- Added more predefined variables for the omnisharp.json file
- Going forward, 2021.3 and below will only be able to use .Net Framework API compatibility level
- Added support for .Net 4.8 API compatibility level for 2022.1 and above
- Fixed failure while loading the analyzer reference 'Unity.SourceGenerators'

## [1.2.7] - 2023-03-19

- Added Unity Roslyn analyzers support (See <https://code.visualstudio.com/docs/other/unity#_enabling-unity-warnings>)
- Added Formatting support (See CreateOmniSharpConfig() in VSCodeScriptEditor.cs)
- Removed Rider Package (Spits)
- Updated Text-Framework to 1.3.3
- Start of Community Support

## [1.2.6] - 2023-03-10

- Added workspace file generation
- End of Unity Support

## [1.2.5] - 2022-02-07

- Introduce OnGeneratedCSProjectFiles, OnGeneratedCSProject and OnGeneratedSlnSolution callbacks.
- Always use forward slash in source paths
- Analyzers use absolute paths
- Ruleset files for roslyn analyzers
- Extra snap search paths on Ubuntu
- Specific c# language version for specific unity versions
- No longer hide .gitignore in VSCode file explorer

## [1.2.3] - 2020-10-23

Remove workaround for VSCode omnisharp (as of <https://github.com/OmniSharp/omnisharp-vscode/issues/4113> we no longer need to disable the referenceoutputassemblies).

## [1.2.2] - 2020-09-04

VSC-14 - synchronize solution file when adding new assembly

## [1.2.1] - 2020-05-15

Source filtering adds support for asmref

## [1.2.0] - 2020-03-04

Do not reference projects that has not been generated (case 1211057)
Only open files that exists (case 1188394)
Add individual toggle buttons for generating csprojects for packages
Add support for Roslyn analyzers in project generation through csc.rsp and compiled assembly references
Remove Release build target from csproj and sln

## [1.1.4] - 2020-01-02

Delta project generation, only recompute the csproj files whose script modified.

## [1.1.3] - 2019-10-22

Exe version of vscode will use Normal ProcessWindowStyle while cmd will use Hidden

## [1.1.2] - 2019-08-30

Fixing OSX open command arguments

## [1.1.1] - 2019-08-19

Support for Player Project. Generates specific csproj files containing files, reference, defines,
etc. that will show how the assembly will be compiled for a target platform.

## [1.1.0] - 2019-08-07

Adds support for choosing extensions to be opened with VSCode. This can be done through the GUI in Preferences.
Avoids opening all extensions after the change in core unity.

## [1.0.7] - 2019-05-15

Fix various OSX specific issues.
Generate project on load if they are not generated.
Fix path recognition.

## [1.0.6] - 2019-04-30

Ensure asset database is refreshed when generating csproj and solution files.

## [1.0.5] - 2019-04-27

Add support for generating all csproj files.

## [1.0.4] - 2019-04-18

Fix relative package paths.
Fix opening editor on mac.
Add %LOCALAPPDATA%/Programs to the path of install paths.

## [1.0.3] - 2019-01-01

### This is the first release of _Unity Package vscode_editor_

Using the newly created api to integrate Visual Studio Code with Unity.
