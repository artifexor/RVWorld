
namespace RomVaultCore.Sharing
{
    public class FileShareUiComm
    {
        public int command;
        public byte[] username;
        public byte[] session;
        public string filename;

        public ulong size;
        public byte[] crc;
        public byte[] sha1;
        public byte[] md5;

        public string message;

        public string zippedFilename;
        public ulong compressedSize;
        public ulong offset;
        public ulong blockLength;
    }
}
