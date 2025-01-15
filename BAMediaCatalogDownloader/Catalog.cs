using MemoryPack;

namespace MediaCatalogDownloader
{
    public enum MediaType
    {
        None = 0,
        Audio = 1,
        Video = 2,
        Texture = 3
    }

    [MemoryPackable]
    public partial class Media
    {
        public string Path { get; set; }
        public string FileName { get; set; }
        public long Bytes { get; set; }
        public long Crc { get; set; }
        public bool IsPrologue { get; set; }
        public bool IsSplitDownload { get; set; }
        public MediaType MediaType { get; set; }
    }

    [MemoryPackable]
    public partial class MediaCatalog
    {
        public Dictionary<string, Media> Table { get; set; }
    }

    [MemoryPackable]
    public partial class TableBundle
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public long Crc { get; set; }
        public bool isInbuild { get; set; }
        public bool isChanged { get; set; }
        public bool IsPrologue { get; set; }
        public bool IsSplitDownload { get; set; }
        public List<string> Includes { get; set; }
    }

    [MemoryPackable]
    public partial class TableCatalog
    {
        public Dictionary<string, TableBundle> Table { get; set; }
    }
}
