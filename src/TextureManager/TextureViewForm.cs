using DDSLib;
using MHUpkManager.TexturePreview;
using MHUpkManager.TextureManager;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Tables;

namespace MHUpkManager
{
    public partial class TextureViewForm : Form
    {
        private DdsFile ddsFile;

        private TextureEntry textureEntry;
        private UTexture2D textureObject;

        private string title;
        private int minIndex;
        private int currentMipIndex;

        internal Action<TexturePreviewTexture> SendToTexturePreviewRequested { get; set; }

        public TextureViewForm()
        {
            ddsFile = new();
            InitializeComponent();
        }

        public class MipMapInfo
        {
            public int Size;
            public int Index;

            public static MipMapInfo AddMipMap(FTexture2DMipMap mipMap, int index)
            {
                var info = new MipMapInfo
                {
                    Size = mipMap.Data.Length,
                    Index = index
                };
                return info;
            }

            public override string ToString()
            {
                return Index.ToString();
            }
        }

        public void SetTitle(string name)
        {
            title = name;
            Text = $"Texture Viewer - [{title}]";
        }

        public void SetTextureObject(FObject textObject, UTexture2D data)
        {
            textureObject = data;

            textureEntry = TextureManifest.Instance.GetTextureEntryFromObject(textObject);
            if (textureEntry != null)
            {
                var textureCache = TextureFileCache.Instance;
                textureCache.SetEntry(textureEntry, textureObject);
                textureCache.LoadTextureCache();
            }

            ReloadTextureView();
        }

        private void mipMapBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (mipMapBox.SelectedItem is MipMapInfo mipMap)
            {
                sizeLabel.Text = mipMap.Size.ToString();
                sourceLabel.Text = mipMap.Index < textureObject.FirstResourceMemMip ? "TFC" : "UPK";
                LoadTexture(mipMap.Index);
            }
        }

        public void ReloadTextureView()
        {
            textureNameLabel.Text = title;
            textureGuidLabel.Text = textureObject.TextureFileCacheGuid.ToString();
            mipMapsLabel.Text = textureObject.Mips.Count.ToString();
            textureFileLabel.Text = textureObject.TextureFileCacheName?.Name;

            UpdateMipMapBox();
            LoadTexture(minIndex);
        }

        private void UpdateMipMapBox()
        {
            mipMapBox.Items.Clear();
            int index = 0;
            minIndex = -1;

            if (textureEntry != null)
            {
                index = (int)textureEntry.Data.Maps[0].Index;
                var mips = TextureFileCache.Instance.Texture2D.Mips;
                foreach (var mipMap in mips)
                {
                    if (mipMap.Data != null)
                    {
                        if (minIndex == -1) minIndex = index;
                        mipMapBox.Items.Add(MipMapInfo.AddMipMap(mipMap, index));
                    }
                    index++;
                }
            }

            index = 0;
            foreach (var mipMap in textureObject.Mips) 
            {
                if (mipMap.Data != null)
                {
                    if (minIndex == -1) minIndex = index;
                    mipMapBox.Items.Add(MipMapInfo.AddMipMap(mipMap, index));
                }
                index++;
            }
        }

        private void LoadTexture(int index = 0)
        {
            if (textureObject.Mips.Count > 0)
            {
                UpdateTextureInfo(index);

                Stream stream;
                if (index < textureObject.FirstResourceMemMip)
                    stream = TextureFileCache.Instance.Texture2D.GetObjectStream(index - minIndex);
                else
                    stream = textureObject.GetObjectStream(index);

                ddsFile.Load(stream);
                textureView.Image = BitmapSourceToBitmap(ddsFile.BitmapSource);
                CenterTexture();

                importDDSToolStripMenuItem.Enabled = textureEntry != null;
                exportDDSToolStripMenuItem.Enabled = true;
                sendToTexturePreviewToolStripMenuItem.Enabled = true;
                currentMipIndex = index;
            }
        }

        private static Bitmap BitmapSourceToBitmap(BitmapSource bitmapSource)
        {
            Bitmap bitmap;

            using (MemoryStream outStream = new())
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                encoder.Save(outStream);
                bitmap = new Bitmap(outStream);
            }

            return bitmap;
        }

        private void UpdateTextureInfo(int mipmapIndex)
        {
            var mipMap = textureObject.Mips[mipmapIndex];
            formatLabel.Text = mipMap.OverrideFormat.ToString();
            widthLabel.Text = $"{mipMap.SizeX} x {mipMap.SizeY}";
            mipMapBox.SelectedIndex = mipmapIndex - minIndex;
        }

        private void texturePanel_Resize(object sender, EventArgs e)
        {
            CenterTexture();
        }

        private void CenterTexture()
        {
            if (textureView.Image != null)
            {
                int x = (texturePanel.ClientSize.Width - textureView.Width) / 2;
                int y = (texturePanel.ClientSize.Height - textureView.Height) / 2;

                textureView.Location = new Point(Math.Max(x, 0), Math.Max(y, 0));
            }
        }

        private static MemoryStream BitmapSourceToPng(BitmapSource bitmapSource)
        {
            MemoryStream outStream = new();

            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            encoder.Save(outStream);

            return outStream;
        }

        private void exportDDSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (textureObject.Mips.Count == 0) return;

            using var saveFileDialog = new SaveFileDialog();
            saveFileDialog.FileName = textureNameLabel.Text + ".dds";
            saveFileDialog.Filter = "DDS Files (*.dds)|*.dds|PNG Files (*.png)|*.png";
            saveFileDialog.Title = "Save a Texture File";

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                string filename = saveFileDialog.FileName;

                var texture = textureObject;
                var cacheTexture = TextureFileCache.Instance.Texture2D;
                if (textureEntry != null && cacheTexture.Mips.Count > 0)
                    texture = cacheTexture;

                var stream = texture.GetMipMapsStream();
                if (stream == null) return;

                bool isPNG = filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                if (isPNG)
                {
                    ddsFile.Load(stream);
                    stream = BitmapSourceToPng(ddsFile.BitmapSource);
                }

                using var fileStream = new FileStream(filename, FileMode.Create, FileAccess.Write);
                stream.Seek(0, SeekOrigin.Begin);
                stream.CopyTo(fileStream);
            }
        }

        private void importDDSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (textureEntry == null)
            {
                MessageBox.Show($"Load {TextureManifest.ManifestName} first so the texture cache can be resolved.",
                    "Texture Import", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using var openFileDialog = new OpenFileDialog
            {
                Filter = "DDS Files (*.dds)|*.dds|PNG Files (*.png)|*.png",
                Title = "Select a Texture File"
            };

            if (openFileDialog.ShowDialog() != DialogResult.OK)
                return;

            string filename = openFileDialog.FileName;
            DdsFile importDds;

            bool isPng = filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            if (isPng)
            {
                FTexture2DMipMap targetMip = textureObject.Mips
                    .Where(static mip => mip.Data != null && mip.Data.Length > 0)
                    .OrderByDescending(static mip => Math.Max(mip.SizeX, mip.SizeY))
                    .FirstOrDefault();

                if (targetMip == null)
                {
                    MessageBox.Show("Current texture does not expose writable mip data.",
                        "Texture Import", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                byte[] rgba = LoadBitmapRgba(filename, targetMip.SizeX, targetMip.SizeY);
                importDds = DdsFile.FromRgba(targetMip.SizeX, targetMip.SizeY, rgba, targetMip.OverrideFormat, textureEntry.Data.Maps.Count);
            }
            else
            {
                importDds = new DdsFile(filename, true);
            }

            ImportHeaderDds(importDds);
        }

        private void ImportHeaderDds(DdsFile ddsHeader)
        {
            if (ddsHeader == null)
            {
                MessageBox.Show("Wrong DDS format!", "DDS Format", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            FTexture2DMipMap targetMip = textureObject.Mips
                .Where(static mip => mip.Data != null && mip.Data.Length > 0)
                .OrderByDescending(static mip => Math.Max(mip.SizeX, mip.SizeY))
                .FirstOrDefault();

            if (targetMip == null)
            {
                MessageBox.Show("Current texture does not expose writable mip data.",
                    "Texture Import", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (ddsHeader.Width != targetMip.SizeX || ddsHeader.Height != targetMip.SizeY)
            {
                MessageBox.Show($"DDS should be {targetMip.SizeX} x {targetMip.SizeY}, your size {ddsHeader.Width} x {ddsHeader.Height}",
                    "DDS Format", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (ddsHeader.FileFormat != targetMip.OverrideFormat)
            {
                MessageBox.Show($"DDS format should be {targetMip.OverrideFormat}, your format is {ddsHeader.FileFormat}",
                    "DDS Format", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (ddsHeader.MipMaps.Count < textureEntry.Data.Maps.Count)
                ddsHeader.RegenMipMaps(textureEntry.Data.Maps.Count);

            TextureFileCache cache = TextureFileCache.Instance;
            cache.SetEntry(textureEntry, textureObject);

            string manifestFilePath = TextureManifest.Instance.ManifestFilePath;
            string tfcPath = Path.Combine(TextureManifest.Instance.ManifestPath, textureEntry.Data.TextureFileName + ".tfc");
            EnsureBackupExists(manifestFilePath);
            EnsureBackupExists(tfcPath);

            if (!cache.LoadFromFile(tfcPath, textureEntry))
            {
                MessageBox.Show($"Could not load texture cache file:\n{tfcPath}",
                    "Texture Import", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            WriteResult result = cache.WriteTexture(TextureManifest.Instance.ManifestPath, textureEntry.Data.TextureFileName, ImportType.Replace, ddsHeader);
            switch (result)
            {
                case WriteResult.Success:
                    TextureManifest.Instance.SaveManifest();
                    ReloadTextureView();
                    MessageBox.Show("Texture imported successfully.", "Texture Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                case WriteResult.MipMapError:
                    MessageBox.Show("Error while writing mip map data.",
                        "Texture Import", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                case WriteResult.SizeReplaceError:
                    MessageBox.Show("Compressed data is too large to replace in the existing texture cache.",
                        "Texture Import", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                default:
                    MessageBox.Show($"Texture import failed with result '{result}'.",
                        "Texture Import", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
            }
        }

        private static void EnsureBackupExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            string backupPath = path + ".bak";
            if (!File.Exists(backupPath))
                File.Copy(path, backupPath, overwrite: false);
        }

        private void sendToTexturePreviewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TexturePreviewTexture texture = BuildPreviewTexture();
            if (texture == null)
            {
                MessageBox.Show("Current texture could not be prepared for the preview tab.",
                    "Texture Preview", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SendToTexturePreviewRequested?.Invoke(texture);
        }

        private TexturePreviewTexture BuildPreviewTexture()
        {
            Stream stream = null;
            try
            {
                if (textureObject?.Mips == null || textureObject.Mips.Count == 0)
                    return null;

                int mipIndex = currentMipIndex;
                if (mipIndex < textureObject.FirstResourceMemMip && textureEntry != null)
                    stream = TextureFileCache.Instance.Texture2D.GetObjectStream(mipIndex - minIndex);
                else
                    stream = textureObject.GetObjectStream(mipIndex);

                if (stream == null)
                    return null;

                using MemoryStream copyStream = new();
                stream.CopyTo(copyStream);
                byte[] containerBytes = copyStream.ToArray();

                using MemoryStream ddsStream = new(containerBytes, writable: false);
                DdsFile previewDds = new();
                previewDds.Load(ddsStream);
                Bitmap bitmap = BitmapSourceToBitmap(previewDds.BitmapSource);

                FTexture2DMipMap mip = textureObject.Mips[Math.Max(0, Math.Min(mipIndex, textureObject.Mips.Count - 1))];
                string mipSource = mipIndex < textureObject.FirstResourceMemMip ? "TFC" : "UPK";
                string sourceDescription = string.IsNullOrWhiteSpace(textureEntry?.Head.TextureName)
                    ? "Texture Viewer"
                    : $"Texture Viewer: {textureEntry.Head.TextureName}";

                return new TexturePreviewTexture
                {
                    Name = title ?? textureNameLabel.Text,
                    SourceDescription = sourceDescription,
                    Bitmap = bitmap,
                    RgbaPixels = LoadBitmapRgba(bitmap),
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                    MipCount = textureObject.Mips.Count,
                    SelectedMipIndex = mipIndex,
                    Format = mip.OverrideFormat.ToString(),
                    Compression = mip.OverrideFormat.ToString(),
                    ContainerType = "DDS",
                    MipSource = mipSource,
                    Slot = TexturePreviewMaterialSlot.Diffuse,
                    ContainerBytes = containerBytes,
                    AvailableMipLevels =
                    [
                        new TexturePreviewMipLevel
                        {
                            AbsoluteIndex = mipIndex,
                            RelativeIndex = mipIndex,
                            Width = bitmap.Width,
                            Height = bitmap.Height,
                            DataSize = containerBytes.Length,
                            Source = mipSource,
                            Format = mip.OverrideFormat.ToString()
                        }
                    ]
                };
            }
            finally
            {
                stream?.Dispose();
            }
        }

        private static byte[] LoadBitmapRgba(string path, int expectedWidth, int expectedHeight)
        {
            using Bitmap source = new(path);
            if (source.Width != expectedWidth || source.Height != expectedHeight)
                throw new InvalidOperationException($"PNG should be {expectedWidth} x {expectedHeight}, your size {source.Width} x {source.Height}");

            using Bitmap bitmap = new(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);

            Rectangle rect = new(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] bgra = new byte[bitmap.Width * bitmap.Height * 4];
                Marshal.Copy(data.Scan0, bgra, 0, bgra.Length);
                for (int i = 0; i < bgra.Length; i += 4)
                    (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);

                return bgra;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static byte[] LoadBitmapRgba(Bitmap bitmap)
        {
            Rectangle rect = new(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] bgra = new byte[bitmap.Width * bitmap.Height * 4];
                Marshal.Copy(data.Scan0, bgra, 0, bgra.Length);
                for (int i = 0; i < bgra.Length; i += 4)
                    (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);

                return bgra;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }

}
