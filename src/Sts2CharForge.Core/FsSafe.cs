using System.Text;

namespace Sts2CharForge.Core;

/// <summary>
/// 写文件 / 删文件的「抗只读、抗占用」封装。
///
/// 为什么需要它：目标文件**已经存在**时，Windows 会带出两种坑，用户看到的是一整页英文堆栈，
/// 日志里也看不出「我该干什么」（有人报的 `UnauthorizedAccessException: Access to the path
/// '...\images\relics\z1_relic1.png' is denied.` 就是这一类）：
///   ① 文件带「只读 / 隐藏 / 系统」属性（从压缩包解压、云盘同步、别人拷过来的常见结果）：
///      File.Copy(overwrite: true) / File.WriteAllText / File.Create / File.Delete 全部抛
///      UnauthorizedAccessException「Access to the path ... is denied」；
///   ② 文件正被别的进程打开（游戏、Godot 编辑器、杀毒软件扫描中）：File.Copy 抛
///      IOException「... because it is being used by another process」。
///
/// 这里统一处理：覆盖前先清掉只读 / 隐藏 / 系统属性 →（还不行就）删掉目标当新文件重写一遍
/// → 仍然失败才抛异常，异常消息里写清是哪个文件、为什么、怎么办（中文）。
///
/// 实测（.NET 9 / Win11，用于区分两类错误）：
///   File.Copy 到「只读目标」      = UnauthorizedAccessException: Access to the path '&lt;全路径&gt;' is denied.
///   Directory.Delete(递归) 遇只读 = UnauthorizedAccessException: Access to the path '&lt;只有文件名&gt;' is denied.
///   File.Copy 到「被占用目标」    = IOException: ... because it is being used by another process.
/// </summary>
public static class FsSafe
{
    /// <summary>只读 / 隐藏 / 系统：文件带这三种属性时「覆盖写」和「删除」都会被拒。</summary>
    private const FileAttributes Blocking =
        FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;

    /// <summary>出这类问题时给用户看的「怎么办」（日志里直接打出来）。</summary>
    public const string FixHint =
        "  处理办法（任选其一）：\n" +
        "    1) 关掉正在用这个目录的程序（游戏 / Godot 编辑器 / 文件管理器预览窗格）后重试；\n" +
        "    2) 资源管理器里右键工程的**上级**文件夹 → 属性 → 去掉「只读」并应用到子文件夹；\n" +
        "    3) 命令行执行（把路径换成你的输出目录）：attrib -R -H -S /S /D \"<输出目录>\";\n" +
        "    4) 若确认没有只读属性，多半是目录权限被改过（例如以前用管理员运行过）——\n" +
        "       要么右键「以管理员身份运行」本程序，要么把「输出目录」换到别处（例如「文档」下）重新生成。";

    /// <summary>清掉会让「覆盖写 / 删除」失败的属性（文件不存在、清不掉都不抛）。</summary>
    public static void ClearBlockingAttributes(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return;
            FileAttributes attrs = File.GetAttributes(path);
            if ((attrs & Blocking) != 0) File.SetAttributes(path, attrs & ~Blocking);
        }
        catch
        {
            // 清不掉不算错 —— 真正写的时候如果再失败，会抛出带「怎么办」的异常
        }
    }

    /// <summary>
    /// 递归清掉整棵目录树的只读 / 隐藏 / 系统属性，返回被改过的条目数。
    /// 删旧工程之前必须先做这一步：目录里只要有一个只读文件，<c>Directory.Delete(root, true)</c>
    /// 就会整棵删不掉（异常消息里出现的偏偏是那个只读文件的**文件名**，很难看出原因）。
    /// </summary>
    public static int ClearBlockingAttributesTree(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        int changed = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,          // 默认会跳过「隐藏 / 系统」，这里正是要处理它们
            ReturnSpecialDirectories = false,
        };
        try
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(dir, "*", options))
            {
                try
                {
                    FileAttributes attrs = File.GetAttributes(entry);
                    bool isDir = (attrs & FileAttributes.Directory) != 0;
                    // 目录上的「只读」在 Windows 里没有实际含义（清掉无害）；文件三种都要清
                    FileAttributes blocked = isDir ? FileAttributes.ReadOnly : Blocking;
                    if ((attrs & blocked) != 0)
                    {
                        File.SetAttributes(entry, attrs & ~blocked);
                        changed++;
                    }
                }
                catch { /* 单个条目失败不影响其它条目 */ }
            }
        }
        catch { /* 枚举失败就算了，后面的删除会报真正的原因 */ }
        ClearBlockingAttributes(dir);
        return changed;
    }

    /// <summary>覆盖复制一个文件（签名跟 <c>File.Copy</c> 一致，可以直接替换）。</summary>
    public static void Copy(string source, string dest, bool overwrite = true)
    {
        EnsureParent(dest);
        if (overwrite) ClearBlockingAttributes(dest);
        try
        {
            File.Copy(source, dest, overwrite);
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // 退路：先把源复制成同目录的临时文件（证明源读得出来、新文件也不受旧文件属性影响），
            // 再删掉旧目标、把临时文件改名过去 —— 万一旧目标删不掉，旧文件也不会凭空消失。
            string tmp = dest + ".forge_tmp";
            try
            {
                ClearBlockingAttributes(tmp);
                if (File.Exists(tmp)) File.Delete(tmp);
                File.Copy(source, tmp, overwrite: true);
                ClearBlockingAttributes(dest);
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);          // 同目录内改名：不会再被旧文件的属性 / 句柄拦住
                return;
            }
            catch (Exception ex2)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 临时文件删不掉不影响结果 */ }
                throw Fail("复制文件", dest, ex2);
            }
        }
    }

    /// <summary>写文本文件的安全版（目标存在时先清只读 / 隐藏 / 系统属性）。</summary>
    public static void WriteAllText(string path, string content, Encoding encoding)
    {
        EnsureParent(path);
        ClearBlockingAttributes(path);
        try
        {
            File.WriteAllText(path, content, encoding);
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            try
            {
                ClearBlockingAttributes(path);
                if (File.Exists(path)) File.Delete(path);
                File.WriteAllText(path, content, encoding);
            }
            catch (Exception ex2)
            {
                throw Fail("写文件", path, ex2);
            }
        }
    }

    /// <summary>写二进制文件的安全版。</summary>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        EnsureParent(path);
        ClearBlockingAttributes(path);
        try
        {
            File.WriteAllBytes(path, bytes);
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            try
            {
                ClearBlockingAttributes(path);
                if (File.Exists(path)) File.Delete(path);
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception ex2)
            {
                throw Fail("写文件", path, ex2);
            }
        }
    }

    /// <summary><c>File.Create</c> 的安全版（PngUtil 这类直接拿 Stream 写的用这个）。</summary>
    public static FileStream Create(string path)
    {
        EnsureParent(path);
        ClearBlockingAttributes(path);
        try
        {
            return File.Create(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            try
            {
                ClearBlockingAttributes(path);
                if (File.Exists(path)) File.Delete(path);
                return File.Create(path);
            }
            catch (Exception ex2)
            {
                throw Fail("写文件", path, ex2);
            }
        }
    }

    /// <summary>删文件（不存在＝成功）；带只读 / 隐藏属性时先清属性。</summary>
    public static void DeleteFile(string path)
    {
        if (!File.Exists(path)) return;
        ClearBlockingAttributes(path);
        try
        {
            File.Delete(path);
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            try
            {
                ClearBlockingAttributes(path);
                File.Delete(path);
            }
            catch (Exception ex2)
            {
                throw Fail("删除文件", path, ex2);
            }
        }
    }

    /// <summary>删整棵目录（先递归清属性，再删；失败时抛带「怎么办」的异常）。</summary>
    public static void DeleteTree(string dir)
    {
        if (!Directory.Exists(dir)) return;

        // 第一次：先清属性再删（旧工程里的只读文件就是这一步救回来的）
        ClearBlockingAttributesTree(dir);
        try
        {
            Directory.Delete(dir, recursive: true);
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // 第二次：属性可能在枚举之后又被同步网盘 / 杀毒软件加回来，再清一遍重试
            ClearBlockingAttributesTree(dir);
            try
            {
                Directory.Delete(dir, recursive: true);
                return;
            }
            catch (Exception ex2)
            {
                throw Fail("删除目录（含里面的旧文件）", dir, ex2);
            }
        }
    }

    /// <summary>
    /// 探一下目录能不能写：返回 null＝能写；否则返回一段「为什么 + 怎么办」的中文提示。
    /// 生成前先探一次，省得写了半个工程才在某个 png 上炸掉。
    /// </summary>
    public static string? ProbeWritable(string dir)
    {
        string probe = Path.Combine(dir, "forge_write_probe.tmp");
        try
        {
            Directory.CreateDirectory(dir);
            ClearBlockingAttributes(probe);
            File.WriteAllText(probe, "probe", new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            return $"输出目录看起来写不进去：{dir}\n  原因：{Explain(ex)}\n{FixHint}";
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { /* 探测文件删不掉不影响生成 */ }
        }
        return null;
    }

    /// <summary>这个异常是不是「访问被拒绝 / 被占用」这一类用户环境问题。</summary>
    public static bool IsAccessProblem(Exception ex) =>
        ex is UnauthorizedAccessException ||
        (ex is IOException io && io.Message.Contains("denied", StringComparison.OrdinalIgnoreCase));

    private static void EnsureParent(string path)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (dir.Length > 0) Directory.CreateDirectory(dir);
    }

    private static IOException Fail(string action, string path, Exception inner) =>
        new($"{action}失败：{path}\n  原因：{Explain(inner)}\n{FixHint}", inner);

    private static string Explain(Exception ex) => ex switch
    {
        UnauthorizedAccessException =>
            "系统拒绝访问 —— 目标文件带「只读 / 隐藏 / 系统」属性，或者被杀毒软件 / 同步网盘保护、正被别的程序占用。",
        IOException =>
            "目标文件正被别的程序占用（游戏 / Godot 编辑器 / 杀毒软件正在扫描）。",
        _ => ex.Message,
    };
}
