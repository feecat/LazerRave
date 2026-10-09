using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Security.Cryptography;
using System.Text;
using osu.Framework.IO.Stores;

namespace LazerRave.Bridge;

internal sealed class FileMediaStore(string[] roots, string? avatar, string cache) : IResourceStore<byte[]>
{
    private bool Allowed(string path) => Path.IsPathFullyQualified(path) && (roots.Any(root => SongLibrary.ContainsPath(root, path))
        || SongLibrary.ContainsPath(cache, path) || string.Equals(path, avatar, StringComparison.OrdinalIgnoreCase));
    public byte[] Get(string name)
    {
        if (!Allowed(name) || !File.Exists(name) || new FileInfo(name).Length >= 128 * 1024 * 1024) return null!;
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (!SongLibrary.ContainsPath(cache, name) && new[] { ".bmp", ".png", ".jpg", ".jpeg" }.Contains(extension))
        {
            using var image = Image.FromFile(name);
            if (image.Width > 8192 || image.Height > 8192 || (long)image.Width * image.Height > 36_000_000) return null!;
            using var data = new MemoryStream(); image.Save(data, ImageFormat.Png); return data.ToArray();
        }
        return File.ReadAllBytes(name);
    }
    public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));
    public Stream GetStream(string name) => Get(name) is { } bytes ? new MemoryStream(bytes, false) : null!;
    public IEnumerable<string> GetAvailableResources() => [];
    public void Dispose() { }
}
internal static class SystemFontAtlas
{
    public static string Create(SongLibrary library, FrontendSettings settings, string cache)
    {
        var characters = (string.Concat(Enumerable.Range(32, 95).Select(c => (char)c)) + " ›←↑↓→·×—★♪−□⚙桜華月曲库ライブラリ搜索目作者或文件夹検索曲名アーティスト开始游戏プレイ进入フォルダーを開く设置設定没有符合条件的がありません音乐预览プレビュー返回戻る结果結果难度難易度外观外観言語暗色亮ダークライト谱面速度スクロール判定偏移調整窗口ウィンドウ关闭閉じ刷新更新读取中読み込み鍵型キー音符数ノーツ演奏選" + settings.Player
            + string.Concat(library.Songs.Select(song => song.Title + song.Artist)) + string.Concat(library.Folders.Select(folder => folder.Name)))
            .Where(c => !char.IsControl(c) && !char.IsSurrogate(c)).Distinct().Order().Take(3000).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("binary-v2:" + new string(characters))))[..16];
        var directory = Path.Combine(cache, "fonts", hash); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Dream"); if (File.Exists(path + ".fnt")) return path;
        const int cell = 72, columns = 16, pageCharacters = 256;
        var fnt = new StringBuilder("info face=\"Dream\" size=56 bold=0 italic=0 charset=\"\" unicode=1 stretchH=100 smooth=1 aa=1 padding=0,0,0,0 spacing=0,0\n");
        int pages = (characters.Length + pageCharacters - 1) / pageCharacters;
        fnt.AppendLine($"common lineHeight={cell} base=58 scaleW={cell * columns} scaleH={cell * columns} pages={pages} packed=0");
        for (int page = 0; page < pages; page++) fnt.AppendLine($"page id={page} file=\"Dream_{page}.png\"");
        fnt.AppendLine($"chars count={characters.Length}");
        using var font = new Font("Microsoft YaHei UI", 56, FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone(); format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        for (int page = 0; page < pages; page++)
        {
            using var bitmap = new Bitmap(cell * columns, cell * columns, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.Transparent); graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            for (int slot = 0; slot < pageCharacters && page * pageCharacters + slot < characters.Length; slot++)
            {
                char c = characters[page * pageCharacters + slot]; int x = slot % columns * cell, y = slot / columns * cell;
                var width = Math.Clamp((int)Math.Ceiling(graphics.MeasureString(c.ToString(), font, 1000, format).Width) + 2, 1, cell);
                graphics.DrawString(c.ToString(), font, Brushes.White, x, y, format);
                fnt.AppendLine($"char id={(int)c} x={x} y={y} width={width} height={cell} xoffset=0 yoffset=0 xadvance={width} page={page} chnl=15");
            }
            bitmap.Save(path + $"_{page}.png", ImageFormat.Png);
        }
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(fnt.ToString()));
        SharpFNT.BitmapFont.FromStream(source, SharpFNT.FormatHint.Text, false).Save(path + ".fnt", SharpFNT.FormatHint.Binary);
        return path;
    }
}
