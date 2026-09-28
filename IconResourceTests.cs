using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

internal static class IconResourceTests
{
    private static int failures;

    private static void Check(bool condition, string description)
    {
        Console.WriteLine((condition ? "통과: " : "실패: ") + description);
        if (!condition) failures++;
    }

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("사용법: IconResourceTests.exe app.ico CodexTaskClockKR.exe");
            return 2;
        }

        var sizes = new HashSet<int>();
        if (File.Exists(args[0]))
        {
            using (var reader = new BinaryReader(File.OpenRead(args[0])))
            {
                var reserved = reader.ReadUInt16();
                var type = reader.ReadUInt16();
                var count = reader.ReadUInt16();
                if (reserved == 0 && type == 1)
                {
                    for (var index = 0; index < count; index++)
                    {
                        var width = reader.ReadByte();
                        reader.ReadByte(); // height
                        reader.ReadByte(); // palette size
                        reader.ReadByte(); // reserved
                        reader.ReadUInt16(); // color planes
                        var depth = reader.ReadUInt16();
                        reader.ReadUInt32(); // frame length
                        reader.ReadUInt32(); // frame offset
                        if (depth == 32) sizes.Add(width == 0 ? 256 : width);
                    }
                }
            }
        }
        Check(sizes.Contains(16) && sizes.Contains(32) && sizes.Contains(48) && sizes.Contains(256),
            "작은 제목 표시줄부터 큰 아이콘까지 네 가지 크기가 들어 있음");

        var hasCustomColors = false;
        if (File.Exists(args[1]))
        {
            using (var icon = Icon.ExtractAssociatedIcon(args[1]))
            using (var bitmap = icon == null ? null : icon.ToBitmap())
            {
                if (bitmap != null)
                {
                    var dark = 0;
                    var blue = 0;
                    for (var y = 0; y < bitmap.Height; y++)
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var color = bitmap.GetPixel(x, y);
                        if (color.A < 160) continue;
                        if (color.R < 65 && color.G < 70 && color.B < 75) dark++;
                        if (color.B > 170 && color.B > color.G + 40 && color.G > color.R + 25) blue++;
                    }
                    var pixels = bitmap.Width * bitmap.Height;
                    hasCustomColors = dark >= pixels / 5 && blue >= pixels / 100;
                }
            }
        }
        Check(hasCustomColors, "실행 파일에 먹색과 파란색의 전용 아이콘이 포함됨");
        return failures == 0 ? 0 : 1;
    }
}
