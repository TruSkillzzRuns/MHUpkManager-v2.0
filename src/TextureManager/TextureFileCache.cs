using DDSLib;

using UpkManager.Helpers;
using UpkManager.Models.UpkFile.Engine.Texture;

namespace MHUpkManager.TextureManager
{
    public enum ImportType
    {
        New = 0,
        Add = 1,
        Replace = 2,
    }

    public enum WriteResult
    {
        Success,
        MipMapError,
        SizeReplaceError,
    }

    public class TextureFileCache
    {
        public static TextureFileCache Instance { get; private set; }
        public UTexture2D Texture2D { get; } = new();

        public TextureEntry Entry { get; private set; }
        public bool Loaded { get; private set; }

        private TextureFileCache() { }

        public static void Initialize()
        {
            Instance ??= new();
        }

        public void Reset()
        {
            Entry = null;
        }

        public void LoadTextureCache()
        {
            string tfcPath = Path.Combine(TextureManifest.Instance.ManifestPath, Entry.Data.TextureFileName + ".tfc");
            if (Entry.Data.Maps.Count == 0) return;

            if (LoadFromFile(tfcPath, Entry) && Texture2D.Mips.Count > 0) return;

            MessageBox.Show($"Can't Load TFC: {Entry.Head.TextureName}\nFile: {tfcPath}",
                                 "Error load", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public bool LoadFromFile(string filePath, TextureEntry entry, bool onlyFirst = false)
        {
            if (Loaded && Entry == entry) return true;

            if (!File.Exists(filePath)) return false;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            if (Entry != entry)
            {
                Texture2D.ResetMipMaps(entry.Data.Maps.Count);
                Loaded = false;
                Entry = entry;
            }

            foreach (var mipMap in entry.Data.Maps)
            {
                if (mipMap.Offset + mipMap.Size > fs.Length)
                    return false;

                fs.Seek(mipMap.Offset, SeekOrigin.Begin);
                byte[] textureData = reader.ReadBytes((int)mipMap.Size);
                var upkReader = ByteArrayReader.CreateNew(textureData, 0);
                var overrideMipMap = entry.Data.OverrideMipMap;

                Task.Run(() =>
                    Texture2D.ReadMipMapCache(upkReader, mipMap.Index, overrideMipMap)
                ).Wait();
                if (onlyFirst) break;
            }

            Loaded = entry.Data.Maps.Count == Texture2D.Mips.Count;

            return true;
        }

        public WriteResult WriteTexture(string texturePath, string textureCacheName, ImportType importType, DdsFile ddsHeader)
        {
            string tfcPath = Path.Combine(texturePath, textureCacheName + ".tfc");

            using FileStream fs = importType switch
            {
                ImportType.New => new FileStream(tfcPath, FileMode.Create, FileAccess.Write),
                ImportType.Add => new FileStream(tfcPath, FileMode.Append, FileAccess.Write),
                ImportType.Replace => new FileStream(tfcPath, FileMode.Open, FileAccess.ReadWrite),
                _ => throw new ArgumentException("Invalid import type", nameof(importType))
            };

            int index = 0;

            if (Texture2D.Mips.Count <= index || ddsHeader.MipMaps.Count <= index)
                return WriteResult.MipMapError;

            Texture2D.ResetCompressedChunks();
            byte[][] payloads = new byte[Entry.Data.Maps.Count][];
            bool requiresRelocation = importType == ImportType.Add;

            foreach (var mipMap in Entry.Data.Maps)
            {
                Texture2D.Mips[index].Data = ddsHeader.MipMaps[index].MipMap;

                var data = Texture2D.WriteMipMapChache(index);

                if (data.Length == 0) return WriteResult.MipMapError;

                payloads[index] = data;
                if (importType == ImportType.Replace && data.Length > mipMap.Size)
                    requiresRelocation = true;

                index++;
            }

            if (importType == ImportType.Replace && !requiresRelocation)
            {
                for (int i = 0; i < Entry.Data.Maps.Count; i++)
                {
                    var mipMap = Entry.Data.Maps[i];
                    fs.Seek(mipMap.Offset, SeekOrigin.Begin);
                    mipMap.Offset = (uint)fs.Position;
                    fs.Write(payloads[i]);
                    mipMap.Size = (uint)payloads[i].Length;
                }
            }
            else
            {
                fs.Seek(0, SeekOrigin.End);
                for (int i = 0; i < Entry.Data.Maps.Count; i++)
                {
                    var mipMap = Entry.Data.Maps[i];
                    mipMap.Offset = (uint)fs.Position;
                    fs.Write(payloads[i]);
                    mipMap.Size = (uint)payloads[i].Length;
                }
            }

            Entry.Data.TextureFileName = textureCacheName;

            return WriteResult.Success;
        }

        public void SetEntry(TextureEntry entry, UTexture2D textureObject)
        {
            if (Loaded && Entry == entry) return;

            Entry = entry;
            Entry.Data.OverrideMipMap.SizeX = textureObject.SizeX;
            Entry.Data.OverrideMipMap.SizeY = textureObject.SizeY;
            Entry.Data.OverrideMipMap.OverrideFormat = UTexture2D.ParseFileFormat(textureObject.Format);
            Loaded = false;
            Texture2D.ResetMipMaps(Entry.Data.Maps.Count);
        }
    }
}
