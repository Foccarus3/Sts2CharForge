using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SharpCompress.Readers;

namespace Sts2CharForge.MarketAdmin;

/// <summary>
/// 把 RAR（以及其它 SharpCompress 能读的格式）解到临时目录，再重新压成标准 ZIP。
/// 市场包最终仍然只能是 ZIP；这里只是上传器的输入转换。
/// </summary>
internal static class RarConverter
{
    public static string ConvertToZip(string archivePath)
    {
        if (!File.Exists(archivePath)) throw new FileNotFoundException("压缩包不存在", archivePath);
        string tempDir = Path.Combine(Path.GetTempPath(), "sts2cf_rar_" + Guid.NewGuid().ToString("N"));
        string output = Path.Combine(Path.GetTempPath(), "sts2cf_converted_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            Directory.CreateDirectory(tempDir);
            string root = Path.GetFullPath(tempDir) + Path.DirectorySeparatorChar;
            long total = 0;

            using (var stream = File.OpenRead(archivePath))
            using (var reader = ReaderFactory.OpenReader(stream))
            {
                while (reader.MoveToNextEntry())
                {
                    var entry = reader.Entry;
                    if (entry.IsDirectory) continue;
                    string rel = (entry.Key ?? "").Replace('\\', '/');
                    if (rel.Length == 0
                        || rel.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
                        || rel.EndsWith("/.DS_Store", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(rel, ".DS_Store", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (rel.StartsWith("/", StringComparison.Ordinal) || rel.Contains("..", StringComparison.Ordinal))
                        throw new InvalidDataException("压缩包包含非法路径：" + rel);

                    total += Math.Max(0, entry.Size);
                    if (total > 100L * 1024 * 1024)
                        throw new InvalidDataException("解压后超过 100 MB，已拒绝。");

                    string full = Path.GetFullPath(Path.Combine(tempDir, rel));
                    if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("压缩包包含路径穿越：" + rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    using var src = reader.OpenEntryStream();
                    using var dst = File.Create(full);
                    src.CopyTo(dst);
                }
            }

            if (!Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories).Any())
                throw new InvalidDataException("压缩包里没有任何文件。");
            if (!Directory.EnumerateFiles(tempDir, "plugin.json", SearchOption.AllDirectories).Any())
                throw new InvalidDataException("压缩包里找不到 plugin.json。");

            File.Delete(output);
            ZipFile.CreateFromDirectory(tempDir, output, CompressionLevel.Optimal, includeBaseDirectory: false);
            return output;
        }
        catch
        {
            try { if (File.Exists(output)) File.Delete(output); } catch { }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }
}
