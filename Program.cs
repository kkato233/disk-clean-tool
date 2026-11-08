using System.Globalization;

internal static class Program
{
    private const long MinimumReserveMiB = 256;
    private const long DefaultReserveMiB = 1024;
    private const int BufferSize = 1024 * 1024;

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            PrintUsage();
            return 0;
        }

        try
        {
            if (args is ["--list"])
            {
                ListDrives();
                return 0;
            }

            if (args[0] == "--wipe-free-space")
            {
                return RunWipeCommand(args);
            }

            Console.Error.WriteLine($"不明な引数です: {args[0]}");
            PrintUsage();
            return 2;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"エラー: {exception.Message}");
            return 1;
        }
    }

    private static int RunWipeCommand(string[] args)
    {
        if (args.Length is not (2 or 4) || (args.Length == 4 && args[2] != "--reserve-mb"))
        {
            PrintUsage();
            return 2;
        }

        var reserveMiB = DefaultReserveMiB;
        if (args.Length == 4 &&
            (!long.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out reserveMiB) ||
             reserveMiB < MinimumReserveMiB ||
             reserveMiB > long.MaxValue / (1024 * 1024)))
        {
            Console.Error.WriteLine($"予約する空き容量は {MinimumReserveMiB} MiB 以上の整数で指定してください。");
            return 2;
        }

        if (!Path.IsPathRooted(args[1]))
        {
            Console.Error.WriteLine("対象にはボリュームのルート (例: C:\\ または /Volumes/Disk/) を指定してください。");
            return 2;
        }

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[1]));
        var drive = DriveInfo.GetDrives().FirstOrDefault(candidate =>
            string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate.RootDirectory.FullName)),
                fullPath,
                PathComparison));
        if (drive is null)
        {
            Console.Error.WriteLine($"使用可能なボリュームのルートが見つかりません: {fullPath}");
            return 1;
        }

        if (!drive.IsReady)
        {
            Console.Error.WriteLine($"ドライブを利用できません: {drive.Name}");
            return 1;
        }

        if (drive.DriveType != DriveType.Fixed)
        {
            Console.Error.WriteLine("初期版では固定ドライブのみを対象にできます。");
            return 1;
        }

        var reserveBytes = reserveMiB * 1024 * 1024;
        var availableBytes = drive.AvailableFreeSpace;
        if (availableBytes <= reserveBytes)
        {
            Console.Error.WriteLine(
                $"空き容量が不足しています。現在の空き容量: {FormatBytes(availableBytes)}、予約容量: {FormatBytes(reserveBytes)}");
            return 1;
        }

        var bytesToWrite = availableBytes - reserveBytes;
        Console.WriteLine($"対象ドライブ: {drive.Name} ({drive.VolumeLabel})");
        Console.WriteLine($"書き込み予定: {FormatBytes(bytesToWrite)}");
        Console.WriteLine($"処理後に残す空き容量: {FormatBytes(reserveBytes)}");
        Console.WriteLine("空き領域に一時ファイルを書き込みます。必要なファイルは削除しません。");
        Console.Write($"続行するには「WIPE {drive.Name}」と入力してください: ");

        if (!string.Equals(Console.ReadLine(), $"WIPE {drive.Name}", StringComparison.Ordinal))
        {
            Console.WriteLine("キャンセルしました。");
            return 0;
        }

        return WipeFreeSpace(drive, bytesToWrite);
    }

    private static int WipeFreeSpace(DriveInfo drive, long bytesToWrite)
    {
        var temporaryPath = Path.Combine(drive.RootDirectory.FullName, $".disk-clean-tool-{Guid.NewGuid():N}.tmp");
        using var cancellationSource = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            Console.WriteLine("空き領域をゼロで上書きしています。Ctrl+C で中断できます。");
            var buffer = new byte[BufferSize];
            long written = 0;
            var nextProgress = Math.Max(bytesToWrite / 10, 1);

            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.SequentialScan))
                {
                    while (written < bytesToWrite)
                    {
                        if (cancellationSource.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(cancellationSource.Token);
                        }

                        var count = (int)Math.Min(buffer.Length, bytesToWrite - written);
                        stream.Write(buffer, 0, count);
                        written += count;

                        if (written >= nextProgress || written == bytesToWrite)
                        {
                            Console.WriteLine($"進捗: {written * 100d / bytesToWrite:F0}%");
                            nextProgress = Math.Min(nextProgress + Math.Max(bytesToWrite / 10, 1), bytesToWrite);
                        }
                    }

                    stream.Flush(flushToDisk: true);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            Console.WriteLine("完了しました。一時ファイルを削除しました。");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("中断しました。一時ファイルを削除しましたが、空き領域の上書きは未完了です。");
            return 130;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void ListDrives()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(candidate => candidate.DriveType == DriveType.Fixed))
        {
            if (!drive.IsReady)
            {
                Console.WriteLine($"{drive.Name} (未使用可能)");
                continue;
            }

            Console.WriteLine(
                $"{drive.Name}  {drive.VolumeLabel}  空き: {FormatBytes(drive.AvailableFreeSpace)} / 合計: {FormatBytes(drive.TotalSize)}");
        }
    }

    private static string FormatBytes(long bytes) =>
        $"{bytes / (1024d * 1024 * 1024):N1} GiB";

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            DiskCleanTool - ディスクの空き領域をゼロで上書きするツール

            使い方:
              DiskCleanTool --list
              DiskCleanTool --wipe-free-space <ボリュームのルート> [--reserve-mb <MiB>]

            例:
              DiskCleanTool --wipe-free-space C:\
              DiskCleanTool --wipe-free-space D:\ --reserve-mb 2048
              DiskCleanTool --wipe-free-space /Volumes/Disk/

            --reserve-mb を省略した場合、空き容量を 1024 MiB 残します。
            実行前に対象ドライブとバックアップを確認してください。
            """);
    }
}
