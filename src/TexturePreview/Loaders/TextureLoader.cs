using DDSLib;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace MHUpkManager.TexturePreview;

internal sealed class TextureLoader
{
    public TexturePreviewTexture LoadFromFile(string filePath, TexturePreviewMaterialSlot fallbackSlot)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Texture file not found.", filePath);

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".dds" => LoadDds(filePath, fallbackSlot),
            ".tga" => LoadTga(filePath, fallbackSlot),
            ".png" or ".jpg" or ".jpeg" => LoadBitmap(filePath, fallbackSlot),
            _ => throw new InvalidOperationException($"Unsupported texture type '{extension}'.")
        };
    }

    private static TexturePreviewTexture LoadBitmap(string filePath, TexturePreviewMaterialSlot fallbackSlot)
    {
        using Bitmap source = new(filePath);
        Bitmap bitmap = ConvertToArgb(source);
        byte[] rgba = ExtractRgba(bitmap);

        return new TexturePreviewTexture
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            SourcePath = filePath,
            SourceDescription = "Disk File",
            Bitmap = bitmap,
            RgbaPixels = rgba,
            Width = bitmap.Width,
            Height = bitmap.Height,
            MipCount = 1,
            Format = "RGBA8",
            Compression = "None",
            ContainerType = Path.GetExtension(filePath).Trim('.').ToUpperInvariant(),
            Slot = fallbackSlot
        };
    }

    private static TexturePreviewTexture LoadDds(string filePath, TexturePreviewMaterialSlot fallbackSlot)
    {
        byte[] fileBytes = File.ReadAllBytes(filePath);
        using MemoryStream stream = new(fileBytes);
        DdsFile dds = new();
        dds.Load(stream);
        Bitmap bitmap = BitmapSourceToBitmap(dds.BitmapSource);
        byte[] rgba = ExtractRgba(bitmap);

        int mipCount = Math.Max(1, dds.MipMaps?.Count ?? 1);
        string compression = "DDS";

        return new TexturePreviewTexture
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            SourcePath = filePath,
            SourceDescription = "Disk File",
            Bitmap = bitmap,
            RgbaPixels = rgba,
            Width = bitmap.Width,
            Height = bitmap.Height,
            MipCount = mipCount,
            Format = "DDS",
            Compression = compression,
            ContainerType = "DDS",
            Slot = fallbackSlot,
            ContainerBytes = fileBytes
        };
    }

    private static TexturePreviewTexture LoadTga(string filePath, TexturePreviewMaterialSlot fallbackSlot)
    {
        byte[] fileBytes = File.ReadAllBytes(filePath);
        Bitmap bitmap = LoadTgaBitmap(fileBytes);
        byte[] rgba = ExtractRgba(bitmap);

        return new TexturePreviewTexture
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            SourcePath = filePath,
            SourceDescription = "Disk File",
            Bitmap = bitmap,
            RgbaPixels = rgba,
            Width = bitmap.Width,
            Height = bitmap.Height,
            MipCount = 1,
            Format = "TGA",
            Compression = "None",
            ContainerType = "TGA",
            Slot = fallbackSlot,
            ContainerBytes = fileBytes
        };
    }

    private static Bitmap LoadTgaBitmap(byte[] data)
    {
        using MemoryStream stream = new(data);
        using BinaryReader reader = new(stream);

        byte idLength = reader.ReadByte();
        reader.ReadByte();
        byte imageType = reader.ReadByte();
        reader.ReadBytes(5);
        reader.ReadUInt16();
        reader.ReadUInt16();
        ushort width = reader.ReadUInt16();
        ushort height = reader.ReadUInt16();
        byte pixelDepth = reader.ReadByte();
        byte imageDescriptor = reader.ReadByte();

        if (imageType is not 2 and not 10)
            throw new InvalidOperationException("Only uncompressed and RLE true-color TGA textures are supported.");

        if (pixelDepth is not 24 and not 32)
            throw new InvalidOperationException("Only 24-bit and 32-bit TGA textures are supported.");

        if (idLength > 0)
            reader.ReadBytes(idLength);

        int bytesPerPixel = pixelDepth / 8;
        byte[] rgba = new byte[width * height * 4];
        bool topOrigin = (imageDescriptor & 0x20) != 0;
        int pixelIndex = 0;

        while (pixelIndex < width * height)
        {
            int runLength = 1;
            byte[] color;
            if (imageType == 10)
            {
                byte packetHeader = reader.ReadByte();
                runLength = (packetHeader & 0x7F) + 1;
                color = ReadTgaPixel(reader, bytesPerPixel);
                if ((packetHeader & 0x80) == 0)
                {
                    WritePixel(rgba, width, height, pixelIndex++, color, topOrigin);
                    for (int i = 1; i < runLength; i++)
                    {
                        color = ReadTgaPixel(reader, bytesPerPixel);
                        WritePixel(rgba, width, height, pixelIndex++, color, topOrigin);
                    }

                    continue;
                }
            }
            else
            {
                color = ReadTgaPixel(reader, bytesPerPixel);
            }

            for (int i = 0; i < runLength; i++)
                WritePixel(rgba, width, height, pixelIndex++, color, topOrigin);
        }

        Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(rgba, 0, bitmapData.Scan0, rgba.Length);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        return bitmap;
    }

    private static byte[] ReadTgaPixel(BinaryReader reader, int bytesPerPixel)
    {
        byte blue = reader.ReadByte();
        byte green = reader.ReadByte();
        byte red = reader.ReadByte();
        byte alpha = bytesPerPixel == 4 ? reader.ReadByte() : (byte)255;
        return [blue, green, red, alpha];
    }

    private static void WritePixel(byte[] rgba, int width, int height, int index, byte[] bgra, bool topOrigin)
    {
        int x = index % width;
        int y = index / width;
        if (!topOrigin)
            y = (height - 1) - y;

        int target = ((y * width) + x) * 4;
        rgba[target + 0] = bgra[0];
        rgba[target + 1] = bgra[1];
        rgba[target + 2] = bgra[2];
        rgba[target + 3] = bgra[3];
    }

    private static Bitmap ConvertToArgb(Bitmap source)
    {
        Bitmap bitmap = new(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        return bitmap;
    }

    private static byte[] ExtractRgba(Bitmap bitmap)
    {
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] bgra = new byte[bitmap.Width * bitmap.Height * 4];
            Marshal.Copy(data.Scan0, bgra, 0, bgra.Length);
            for (int i = 0; i < bgra.Length; i += 4)
            {
                (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
            }

            return bgra;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Bitmap BitmapSourceToBitmap(BitmapSource bitmapSource)
    {
        using MemoryStream outStream = new();
        BitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
        encoder.Save(outStream);
        outStream.Position = 0;
        using Bitmap temp = new(outStream);
        return new Bitmap(temp);
    }
}
