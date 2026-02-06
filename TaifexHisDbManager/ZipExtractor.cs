using System;
using System.IO;
using System.IO.Compression;

namespace TaifexHisDbManager
{
    internal static class ZipExtractor
    {
        public static void ExtractAll(string zipPath, string outputDirectory, bool overwriteFiles = true)
        {
            if (string.IsNullOrWhiteSpace(zipPath))
                throw new ArgumentException("zipPath is required.", nameof(zipPath));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("outputDirectory is required.", nameof(outputDirectory));
            if (!File.Exists(zipPath))
                throw new FileNotFoundException($"ZIP 檔不存在: {zipPath}", zipPath);

            Directory.CreateDirectory(outputDirectory);
            ZipFile.ExtractToDirectory(zipPath, outputDirectory, overwriteFiles);
        }
    }
}
