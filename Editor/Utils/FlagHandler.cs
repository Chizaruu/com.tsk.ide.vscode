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
        Argument = 1,
        Setup = 2,
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
        void ToggleArgument(ArgumentFlag preference);
        void ToggleConfig(ConfigFlag preference);
    }

    internal class FlagHandler : IFlagHandler
    {
        private ArgumentFlag m_ArgumentFlag = (ArgumentFlag)
            EditorPrefs.GetInt("unity_argument_flag", 0);

        private ConfigFlag m_ConfigFlag = (ConfigFlag)EditorPrefs.GetInt("unity_config_flag", 0);

        public ArgumentFlag ArgumentFlag
        {
            get => m_ArgumentFlag;
            private set { EditorPrefs.SetInt("unity_argument_flag", (int)value); }
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
    }
}
