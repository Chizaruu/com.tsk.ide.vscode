using System;
using System.IO;
using System.Security;
using System.Text;

namespace VSCodeEditor
{
    public interface IFileIO
    {
        bool Exists(string fileName);

        string ReadAllText(string fileName);
        void WriteAllText(string fileName, string content);

        void Copy(string sourceFileName, string destFileName, bool overwrite);

        void CreateDirectory(string pathName);
        string EscapedRelativePathFor(string file, string projectDirectory);
    }

    internal class FileIOProvider : IFileIO
    {
        public bool Exists(string fileName)
        {
            return File.Exists(fileName);
        }

        public string ReadAllText(string fileName)
        {
            return File.ReadAllText(fileName);
        }

        public void WriteAllText(string fileName, string content)
        {
            File.WriteAllText(fileName, content, Encoding.UTF8);
        }

        public void Copy(string sourceFileName, string destFileName, bool overwrite)
        {
            File.Copy(sourceFileName, destFileName, overwrite);
        }

        public void CreateDirectory(string pathName)
        {
            _ = Directory.CreateDirectory(pathName);
        }

        public string EscapedRelativePathFor(string file, string projectDirectory)
        {
            string projectDir = Path.GetFullPath(projectDirectory);

            // We have to normalize the path, because the PackageManagerRemapper assumes
            // dir seperators will be os specific.
            string absolutePath = Path.GetFullPath(file.NormalizePath());
            string path = SkipPathPrefix(absolutePath, projectDir);

            return SecurityElement.Escape(path);
        }

        private static string SkipPathPrefix(string path, string prefix)
        {
            return path.StartsWith(
                $"{prefix}{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal
            )
                ? path[(prefix.Length + 1)..]
                : path;
        }
    }
}
