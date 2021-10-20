using RomVaultCore.FindFix;
using RomVaultCore.RvDB;
using RomVaultCore.Utils;
using Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Compress.ZipFile;
using System.Security.Cryptography;
using System.Diagnostics;

namespace RomVaultCore.Sharing
{
    [Flags]
    public enum comBits
    {
        cCRC = 0x01,
        cSHA1 = 0x02,
        cMD5 = 0x04
    }

    public class FileClient
    {
        private ThreadWorker _thWrk;

        public string ip;
        public int port;
        public byte[] name;
        private byte[] session; //guid

        private FileGroup[] fileMissingGroupsSHA1Sorted;
        public void StartClient(ThreadWorker e)
        {
            _thWrk = e;

            session = Guid.NewGuid().ToByteArray();

            _thWrk?.Report("Building FileList");
            List<RvFile> filesGot = new List<RvFile>();
            List<RvFile> filesMissing = new List<RvFile>();
            FindFixes.GetSelectedFiles(DB.DirRoot, true, filesGot, filesMissing);

            _thWrk?.Report("Sorting Missing CRC");
            RvFile[] filesMissingSortedCRC = FindFixesSort.SortCRC(filesMissing);
            FindFixes.MergeGotFiles(filesMissingSortedCRC, out FileGroup[] fileMissingGroupsCRCSorted);
            _thWrk?.Report("Sorting Missing SHA1");
            FindFixesSort.SortFamily(fileMissingGroupsCRCSorted, FindSHA1, FamilySortSHA1, out fileMissingGroupsSHA1Sorted);

            Find();
        }

        public void Cancel()
        {
            //throw new NotImplementedException();
        }

        private static bool FindSHA1(FileGroup fileGroup)
        {
            return fileGroup.SHA1 != null;
        }
        private static int FamilySortSHA1(FileGroup fileGroup1, FileGroup fileGroup2)
        {
            return ArrByte.ICompare(fileGroup1.SHA1, fileGroup2.SHA1);
        }
        private static int CompareSHA1(RvFile file, FileGroup fileGroup)
        {
            return ArrByte.ICompare(file.SHA1, fileGroup.SHA1);
        }

        public class searchFiles
        {
            public string name;
            public ulong Size;
            public byte[] CRC;
            public byte[] SHA1;
            public byte[] MD5;
            public ulong compressedSize;
            public bool found;
        }
            

        public void Find()
        {
            List<searchFiles> sFiles = new List<searchFiles>();
            foreach (var f in fileMissingGroupsSHA1Sorted)
            {
                if (f.Files.Count == 0)
                    continue;

                string filename = "ToSort\\" + BitConverter.ToString(f.SHA1).Replace("-", "") + ".zip";
                Debug.WriteLine(filename);

                if (File.Exists(filename))
                    continue;
                if ((f.Size ?? 0) == 0)// || f.Size > 1024 * 1024 * 128)
                    continue;


                searchFiles sFile = new searchFiles()
                {
                    name = f.Files[0].DatTreeFullName,
                    Size = (ulong)f.Size,
                    CRC= f.CRC,
                    SHA1 = f.SHA1,
                    MD5 = f.MD5
                };
                sFiles.Add(sFile);
            }
            foreach (var f in sFiles)
            {
                FindSHA1(f);
                if (f.found)
                {
                    fetch(f);
                }
                if (_thWrk.CancellationPending)
                    return;
            }
        }


        public bool FindSHA1(searchFiles f)
        {
            f.found = false;

            FileShareUiComm fs = new FileShareUiComm
            {
                username = name,
                session = session,
                filename = f.name,
                size = f.Size,
                crc = f.CRC,
                sha1 = f.SHA1,
                md5 = f.MD5,
                offset = 0,
                blockLength = 0
            };
            fs.command = 2;

            comBits cb = 0;
            if (f.CRC != null) cb |= comBits.cCRC;
            if (f.SHA1 != null) cb |= comBits.cSHA1;
            if (f.MD5 != null) cb |= comBits.cMD5;


            List<byte> list = new List<byte>();
            list.Add(0); list.Add(2);
            list.AddRange(name);
            list.AddRange(session);
            list.AddRange(BitConverter.GetBytes(f.Size));
            list.Add((byte)cb);
            if (f.CRC != null) list.AddRange(f.CRC);
            if (f.SHA1 != null) list.AddRange(f.SHA1);
            if (f.MD5 != null) list.AddRange(f.MD5);



            NetClient.SendToServer(list.ToArray(), ip, port, out byte[] bReply);

            if (bReply == null)
            {
                fs.message = "Server Lost";
                _thWrk?.Report(fs);
                return false;
            }

            if (bReply[0] != 0 || bReply[1] != 2)
            {
                //                textBox2.Text = "Incorrect Command reply";
                return false;
            }

            if (bReply[2] != 1)
            {
                fs.message = "Not Found";
                _thWrk?.Report(fs);
                return false;
            }

            f.compressedSize = BitConverter.ToUInt64(bReply, 3);
            byte[] fCRC = bReply.Copy(11, 4);

            if (!ArrByte.ECompare(f.CRC, fCRC))
                return false;

            if (f.CRC == null) f.CRC = fCRC;

            f.found = true;
            return true;

        }

        public void fetch(searchFiles f)
        {
            FileShareUiComm fs = new FileShareUiComm
            {
                username = name,
                session = session,
                filename = f.name,
                size = f.Size,
                crc = f.CRC,
                sha1 = f.SHA1,
                md5 = f.MD5,
                compressedSize = f.compressedSize,
                offset = 0,
                blockLength = 0
            };
            fs.command = 3;

            comBits cb = 0;
            if (f.CRC != null) cb |= comBits.cCRC;
            if (f.SHA1 != null) cb |= comBits.cSHA1;
            if (f.MD5 != null) cb |= comBits.cMD5;


            ulong fileSize = f.compressedSize;

            // now try and get the file
            string filenameTmp = "ToSort\\__FCDownload.tmp";
            string filename = "ToSort\\" + BitConverter.ToString(f.SHA1).Replace("-", "") + ".zip";
            string filenameBin = BitConverter.ToString(f.SHA1).Replace("-", "") + ".bin";
            uint blockMaxSize = 4096 * 4096;

            Zip zFile = new Zip();
            zFile.ZipFileCreate(filenameTmp);
            zFile.ZipFileOpenWriteStream(true, false, filenameBin, f.Size, 8, out Stream stream);

            List<byte> list = new List<byte>();
            byte[] bReply = null;

            ulong offset = 0;
            while (fileSize > 0)
            {
                uint partNow = fileSize > blockMaxSize ? blockMaxSize : (uint)fileSize;
                Debug.WriteLine($"Index {offset} : Block {partNow} : Total {f.compressedSize} / {f.Size}");

                fs.command = 3;
                fs.offset = offset;
                fs.blockLength = (ulong)partNow;

                int FetchCount = 0;
                bool res = false;
                while (!res && FetchCount < 4)
                {
                    list.Clear();
                    list.Add(0); list.Add(3);
                    list.AddRange(name);
                    list.AddRange(session);
                    list.AddRange(BitConverter.GetBytes(f.Size));
                    list.Add((byte)cb);
                    if (f.CRC != null) list.AddRange(f.CRC);
                    if (f.SHA1 != null) list.AddRange(f.SHA1);
                    if (f.MD5 != null) list.AddRange(f.MD5);
                    list.AddRange(BitConverter.GetBytes(offset));
                    list.AddRange(BitConverter.GetBytes(partNow));

                    fs.message = "Receiving";
                    _thWrk?.Report(fs);

                    res = NetClient.SendToServer(list.ToArray(), ip, port, out bReply);
                    FetchCount++;
                }
                if (FetchCount >= 4)
                {
                    fs.message = "Server Lost";
                    _thWrk?.Report(fs);
                    zFile.ZipFileCloseFailed();
                    zFile.ZipFileClose();
                    File.Delete(filenameTmp);
                    return;
                }

                if (bReply[0] != 0 || bReply[1] != 3)
                {
                    fs.message = "Error Back command";
                    _thWrk?.Report(fs);
                    zFile.ZipFileCloseFailed();
                    zFile.ZipFileClose();
                    File.Delete(filenameTmp);
                    return;
                }
                if (bReply[2] != 1)
                {
                    fs.message = "Find now not found";
                    _thWrk?.Report(fs);
                    zFile.ZipFileCloseFailed();
                    zFile.ZipFileClose();
                    File.Delete(filenameTmp);
                    return;
                }
                int partSize = BitConverter.ToInt32(bReply, 3);
                if (partSize != partNow)
                {
                    fs.message = "Receive Size Error";

                    _thWrk?.Report(fs);
                    zFile.ZipFileCloseFailed();
                    zFile.ZipFileClose();
                    File.Delete(filenameTmp);
                    return;
                }

                stream.Write(bReply, 7, partSize);

                offset += partNow;
                fileSize -= partNow;

            }
            zFile.ZipFileCloseWriteStream(f.CRC);
            zFile.ZipFileClose();
            File.Move(filenameTmp, filename);
        }

    }
}
