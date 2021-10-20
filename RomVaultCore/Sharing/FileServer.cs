using RomVaultCore.FindFix;
using RomVaultCore.RvDB;
using RomVaultCore.Utils;
using Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Compress.ZipFile;

namespace RomVaultCore.Sharing
{
    public class FileServer
    {
        private ThreadWorker _thWrk;

        private FileGroup[] fileGroupsSHA1Sorted;

        public int listeningPort;
        private NetClient _listeningClient;

        public void StartSharing(ThreadWorker e)
        {
            _thWrk = e;
            buildShareList();
            if (_thWrk.CancellationPending)
                return;

            _listeningClient = new NetClient();
            _listeningClient.OnReceivedData += ReceivedData;

            _thWrk.Report($"Trying to Listen on port {listeningPort}");
            bool listening = _listeningClient.StartListener(IPAddress.Any, listeningPort, out string error);
            if (listening)
            {
                _thWrk.Report($"Listening on port {listeningPort}");
            }
            else
            {
                return;
            }


        }

        public void Cancel()
        {
            _listeningClient?.StopListener();
        }

        public void buildShareList()
        {
            _thWrk.Report("Building FileList");
            List<RvFile> filesGot = new List<RvFile>();
            List<RvFile> filesMissing = new List<RvFile>();
            FindFixes.GetSelectedFiles(DB.DirRoot, true, filesGot, filesMissing);

            _thWrk.Report("Sorting Got CRC");
            RvFile[] filesGotSortedCRC = FindFixesSort.SortCRC(filesGot);
            FindFixes.MergeGotFiles(filesGotSortedCRC, out FileGroup[] fileGroupsCRCSorted);
            _thWrk.Report("Sorting Got SHA1");
            FindFixesSort.SortFamily(fileGroupsCRCSorted, FindSHA1, FamilySortSHA1, out fileGroupsSHA1Sorted);

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




        private byte[] ReceivedData(string ip, byte[] buffer)
        {
            FileShareUiComm fs;

            if (buffer == null)
            {
                return null;
            }
            _thWrk.Report($"Received {ip} data -> {BitConverter.ToString(buffer)}");

            if (buffer.Length < 2)
            {
                // return an error command reply
                return new byte[] { 0, 0 };
            }

            int bOffset = 0;
            int command = (buffer[bOffset++] << 8) + buffer[bOffset++];
            byte[] name = buffer.Copy(bOffset, 10);  bOffset += 10;
            byte[] session = buffer.Copy(bOffset, 16); bOffset += 16;

            switch (command)
            {
                //  00 -  2 Command 1 (0,1)
                //  02 - 10 Name
                //  12 - 16 Session Guid
                case 1:
                    //  00 -  2 Command 1 (0,1)
                    //  02 -  1 01

                    fs = new FileShareUiComm
                    {
                        command = 1,
                        username = name,
                        session = session,
                        message = "Hello"
                    };
                    _thWrk?.Report(fs);

                    return new byte[] { 0, 1, 1 };



                //  00 -  2 Command 2 (0, 2)
                //  02 - 10 Name
                //  12 - 16 Session Guid
                //  28 -  8 Size

                case 2:
                    //  00 - 2 Command 2 (0, 2)
                    //  02 - 1 File Not Found = 0

                    //  00 - 2 Command 2 (0, 2)
                    //  02 - 1 File Found = 1
                    //   3 - 8 File Compressed Size
                    //  11 - 4 File CRC
                    {
                        RvFile testFile = new RvFile(FileType.ZipFile);
                        testFile.Size = BitConverter.ToUInt64(buffer, bOffset); bOffset += 8;
                        comBits cb = (comBits)buffer[bOffset++];
                        if ((cb & comBits.cCRC) != 0) { testFile.CRC = buffer.Copy(bOffset, 4); bOffset += 4; }
                        if ((cb & comBits.cSHA1) != 0) { testFile.SHA1 = buffer.Copy(bOffset, 20); bOffset += 20; }
                        if ((cb & comBits.cMD5) != 0) { testFile.MD5 = buffer.Copy(bOffset, 16); bOffset += 16; }

                        bool found = FindFixes.FindMatch(fileGroupsSHA1Sorted, testFile, CompareSHA1, FileGroup.FindExactMatch, out List<int> listIndex);

                        if (!found)
                        {
                            fs = new FileShareUiComm
                            {
                                command = 2,
                                username = name,
                                session = session,
                                sha1 = testFile.SHA1,
                                message = "SHA1 not found"
                            };
                            _thWrk?.Report(fs);
                            return new byte[] { 0, 2, 0 };
                        }

                        FileGroup fgMatch = fileGroupsSHA1Sorted[listIndex[0]];
                        if (testFile.Size != fgMatch.Size)
                        {
                            fs = new FileShareUiComm
                            {
                                command = 2,
                                username = name,
                                session = session,
                                sha1 = testFile.SHA1,
                                message = "SHA1,Size not found"
                            };
                            _thWrk?.Report(fs);
                            return new byte[] { 0, 2, 0 };
                        }
                        if (!ArrByte.ECompare(testFile.CRC, fgMatch.CRC))
                        {
                            fs = new FileShareUiComm
                            {
                                command = 2,
                                username = name,
                                session = session,
                                sha1 = testFile.SHA1,
                                message = "SHA1,CRC not found"
                            };
                            _thWrk?.Report(fs);
                            return new byte[] { 0, 2, 0 };
                        }
                        if (!ArrByte.ECompare(testFile.MD5, fgMatch.MD5))
                        {
                            fs = new FileShareUiComm
                            {
                                command = 2,
                                username = name,
                                session = session,
                                sha1 = testFile.SHA1,
                                message = "SHA1,MD5 not found"
                            };
                            _thWrk?.Report(fs);
                            return new byte[] { 0, 2, 0 };
                        }

                        foreach (RvFile fMatch in fgMatch.Files)
                        {
                            Zip zFile = new Zip();
                            zFile.ZipFileOpen(fMatch.Parent.FullNameCase, fMatch.Parent.FileModTimeStamp, false);
                            zFile.ZipFileOpenReadStreamQuick((ulong)fMatch.ZipFileHeaderPosition, true, out Stream stream, out ulong streamSize, out ushort compressionMethod);

                            if (compressionMethod != 8)
                            {
                                zFile.ZipFileCloseReadStream();
                                zFile.ZipFileClose();
                                continue;
                            }

                            List<byte> ret = new List<byte>();
                            ret.AddRange(new byte[] { 0, 2, 1 });
                            ret.AddRange(BitConverter.GetBytes((ulong)streamSize));
                            ret.AddRange(fMatch.CRC);

                            fs = new FileShareUiComm
                            {
                                command = 2,
                                username = name,
                                session = session,
                                sha1 = testFile.SHA1,
                                message = "Found File",
                                filename = fMatch.Parent.FullName,
                                zippedFilename = fMatch.Name,
                                compressedSize = streamSize
                            };
                            _thWrk?.Report(fs);

                            zFile.ZipFileCloseReadStream();
                            zFile.ZipFileClose();

                            return ret.ToArray();
                        }

                        fs = new FileShareUiComm
                        {
                            command = 2,
                            username = name,
                            session = session,
                            sha1 = testFile.SHA1,
                            message = "No Zipped version found."
                        };
                        _thWrk?.Report(fs);

                        return new byte[] { 0, 2, 0 };
                    }

                //  00 -  2 Command 3 (0, 3)
                //  02 - 10 Name
                //  12 - 16 Session Guid
                //  28 - 20 SHA1
                //  48 -  8 Size
                //  56 -  8 long file offset block
                //  64 -  4 block length to read

                case 3:
                    //  00 - 2 Command 2 (0, 3)
                    //  02 - 1 File Not Found = 0

                    //  00 - 2 Command 2 (0, 3)
                    //  02 - 1 File Found = 1
                    //  03 - 4: Block read length
                    //  07 - X: Data
                    {
                        RvFile testFile = new RvFile(FileType.ZipFile);
                        testFile.Size = BitConverter.ToUInt64(buffer, bOffset); bOffset += 8;
                        comBits cb = (comBits)buffer[bOffset++];
                        if ((cb & comBits.cCRC) != 0) { testFile.CRC = buffer.Copy(bOffset, 4); bOffset += 4; }
                        if ((cb & comBits.cSHA1) != 0) { testFile.SHA1 = buffer.Copy(bOffset, 20); bOffset += 20; }
                        if ((cb & comBits.cMD5) != 0) { testFile.MD5 = buffer.Copy(bOffset, 16); bOffset += 16; }

                        ulong offset = BitConverter.ToUInt64(buffer, bOffset); bOffset += 8;
                        uint length = BitConverter.ToUInt32(buffer, bOffset); bOffset += 4;

                        bool found = FindFixes.FindMatch(fileGroupsSHA1Sorted, testFile, CompareSHA1, FileGroup.FindExactMatch, out List<int> listIndex);

                        if (!found)
                        {
                            fs = new FileShareUiComm
                            {
                                command = 3,
                                username = name,
                                session = session,
                                sha1 = testFile.SHA1,
                                message = "SHA1 not found"
                            };
                            _thWrk?.Report(fs);
                            return new byte[] { 0, 3, 0 };
                        }

                        FileGroup fgMatch = fileGroupsSHA1Sorted[listIndex[0]];
                        if (testFile.Size != fgMatch.Size)
                        {
                            fs = new FileShareUiComm
                            {
                                command = 3,
                                username = name,
                                session = session,
                                sha1 = testFile.SHA1,
                                message = "SHA1,Size not found"
                            };
                            _thWrk?.Report(fs);
                            return new byte[] { 0, 3, 0 };
                        }
                        if (!ArrByte.ECompare(testFile.CRC,fgMatch.CRC))
                        {
                            fs = new FileShareUiComm
                            {
                                command = 3,
                                username = name,
                                session = session,
                                sha1 = testFile.SHA1,
                                message = "SHA1,CRC not found"
                            };
                            _thWrk?.Report(fs);
                            return new byte[] { 0, 3, 0 };
                        }
                        if (!ArrByte.ECompare(testFile.MD5, fgMatch.MD5))
                        {
                            fs = new FileShareUiComm
                            {
                                command = 3,
                                username = name,
                                session = session,
                                sha1 = testFile.SHA1,
                                message = "SHA1,MD5 not found"
                            };
                            _thWrk?.Report(fs);
                            return new byte[] { 0, 3, 0 };
                        }

                        foreach (RvFile fMatch in fgMatch.Files)
                        {
                            Zip zFile = new Zip();
                            zFile.ZipFileOpen(fMatch.Parent.FullNameCase, fMatch.Parent.FileModTimeStamp, false);
                            zFile.ZipFileOpenReadStreamQuick((ulong)fMatch.ZipFileHeaderPosition, true, out Stream stream, out ulong streamSize, out ushort compressionMethod);

                            if (compressionMethod != 8)
                            {
                                zFile.ZipFileCloseReadStream();
                                zFile.ZipFileClose();
                                continue;
                            }

                            List<byte> ret = new List<byte>();
                            ret.AddRange(new byte[] { 0, 3, 1 });

                            stream.Seek((long)offset, SeekOrigin.Current);
                            byte[] data = new byte[length];
                            int readLength = stream.Read(data, 0,(int) length);
                            ret.AddRange(BitConverter.GetBytes(readLength));
                            ret.AddRange(data);

                            fs = new FileShareUiComm
                            {
                                command = 2,
                                username = name,
                                session = session,
                                filename = fMatch.Name,
                                sha1 = testFile.SHA1,

                                message = "Sending",

                                zippedFilename = fMatch.Parent.FullName,
                                compressedSize = streamSize,
                                offset = offset,
                                blockLength= length
                            };

                            _thWrk?.Report(fs);
                            zFile.ZipFileCloseReadStream();
                            zFile.ZipFileClose();
                            return ret.ToArray();
                        }

                        return new byte[] { 0, 2, 0 };
                    }

                default:
                    return new byte[] { 0, 0 };
            }

        }


    }

}
