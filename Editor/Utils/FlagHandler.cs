using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;

namespace VSCodeEditor
{
    [Flags]
    public enum ArgumentFlag
    {
        None = 0,
        EditorArgument = 1,
    }

    [Flags]
    public enum ConfigFlag
    {
        None = 0,
        Workspace = 1,
        OmniSharp = 2,
        EditorConfig = 4,
    }

    [Flags]
    public enum ProjectGenerationFlag
    {
        None = 0,
        Embedded = 1,
        Local = 2,
        Registry = 4,
        Git = 8,
        BuiltIn = 16,
        Unknown = 32,
        PlayerAssemblies = 64,
        LocalTarBall = 128,
    }

    public interface IFlagHandler
    {
        ArgumentFlag ArgumentFlag { get; }
        ConfigFlag ConfigFlag { get; }
        ProjectGenerationFlag ProjectGenerationFlag { get; }
        void ToggleArgument(ArgumentFlag preference);
        void ToggleConfig(ConfigFlag preference);
        void ToggleProjectGeneration(ProjectGenerationFlag preference);
    }

    internal class FlagHandler : IFlagHandler
    {
        private ArgumentFlag m_ArgumentFlag = (ArgumentFlag)
            EditorPrefs.GetInt("unity_argument_flag", 0);

        private ConfigFlag m_ConfigFlag = (ConfigFlag)EditorPrefs.GetInt("unity_config_flag", 0);

        private ProjectGenerationFlag m_ProjectGenerationFlag = (ProjectGenerationFlag)
            EditorPrefs.GetInt("unity_project_generation_flag", 0);

        public ArgumentFlag ArgumentFlag
        {
            get => m_ArgumentFlag;
            private set
            {
                EditorPrefs.SetInt("unity_argument_flag", (int)value);
                m_ArgumentFlag = value;
            }
        }

        public ConfigFlag ConfigFlag
        {
            get => m_ConfigFlag;
            private set
            {
                EditorPrefs.SetInt("unity_config_flag", (int)value);
                m_ConfigFlag = value;
            }
        }

        public ProjectGenerationFlag ProjectGenerationFlag
        {
            get => m_ProjectGenerationFlag;
            private set
            {
                EditorPrefs.SetInt("unity_project_generation_flag", (int)value);
                m_ProjectGenerationFlag = value;
            }
        }

        public void ToggleArgument(ArgumentFlag preference)
        {
            if (ArgumentFlag.HasFlag(preference))
            {
                ArgumentFlag ^= preference;
            }
            else
            {
                ArgumentFlag |= preference;
            }
        }

        public void ToggleConfig(ConfigFlag preference)
        {
            if (ConfigFlag.HasFlag(preference))
            {
                ConfigFlag ^= preference;
            }
            else
            {
                ConfigFlag |= preference;
            }
        }

        public void ToggleProjectGeneration(ProjectGenerationFlag preference)
        {
            if (ProjectGenerationFlag.HasFlag(preference))
            {
                ProjectGenerationFlag ^= preference;
            }
            else
            {
                ProjectGenerationFlag |= preference;
            }
        }
    }
}
