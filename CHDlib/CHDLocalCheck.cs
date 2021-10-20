﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CHDlib
{

    internal class CHDLocalCheck
    {
        private Message _progress;
        private string _result;
        private hdErr _resultType;


        public byte[] cache;

        internal hdErr ChdCheck(Message progress, hard_disk_info hdi, out string result)
        {

            try
            {

                _progress = progress;
                _result = "";
                _resultType = hard_disk_verify(hdi, progress);

                result = _result;
                return _resultType;
            }
            catch (Exception e)
            {
                result = e.ToString();
                return hdErr.HDERR_DECOMPRESSION_ERROR;
            }
        }


        private static hdErr read_sector_map(hard_disk_info info)
        {
            info.map = new mapentry[info.totalblocks];

            info.file.Seek(info.length, SeekOrigin.Begin);

            using (BinaryReader br = new BinaryReader(info.file, Encoding.UTF8, true))
            {
                if (info.version <= 2)
                {
                    for (int i = 0; i < info.totalblocks; i++)
                    {
                        UInt64 tmpu = br.ReadUInt64BE();

                        mapentry me = new mapentry()
                        {
                            offset = (tmpu << 20) >> 20,
                            crc = 0,
                            length = (tmpu >> 44),
                            UseCount = 0
                        };
                        me.flags = mapFlags.MAP_ENTRY_FLAG_NO_CRC | ((me.length == info.blocksize)
                                       ? mapFlags.MAP_ENTRY_TYPE_UNCOMPRESSED
                                       : mapFlags.MAP_ENTRY_TYPE_COMPRESSED);
                        info.map[i] = me;
                    }
                    Dictionary<ulong, int> selfhunkMap = new Dictionary<ulong, int>();
                    for (int i = 0; i < info.totalblocks; i++)
                    {
                        if (selfhunkMap.TryGetValue(info.map[i].offset, out int index))
                        {
                            info.map[i].offset = (ulong)index;
                            info.map[i].flags = mapFlags.MAP_ENTRY_FLAG_NO_CRC | mapFlags.MAP_ENTRY_TYPE_SELF_HUNK;
                        }
                        else
                            selfhunkMap.Add(info.map[i].offset, i);
                    }
                }
                else
                {
                    for (int i = 0; i < info.totalblocks; i++)
                    {
                        mapentry me = new mapentry()
                        {
                            offset = br.ReadUInt64BE(),
                            crc = br.ReadUInt32BE(),
                            length = br.ReadUInt16BE(),
                            flags = (mapFlags)br.ReadUInt16BE(),
                            UseCount = 0
                        };
                        info.map[i] = me;
                    }
                }

                for (int i = 0; i < info.totalblocks; i++)
                {
                    if ((info.map[i].flags & mapFlags.MAP_ENTRY_FLAG_TYPE_MASK) == mapFlags.MAP_ENTRY_TYPE_SELF_HUNK)
                    {
                        info.map[info.map[i].offset].UseCount += 1;
                    }
                }
            }

            return hdErr.HDERR_NONE;
        }


        public hdErr hard_disk_verify(hard_disk_info hardDisk, Message progress)
        {
            hdErr err;
            int block = 0;

            /* if this is a writeable disk image, we can't verify */
            if ((hardDisk.flags & HDFLAGS_IS_WRITEABLE) != 0)
                return hdErr.HDERR_CANT_VERIFY;

            if (hardDisk.version >= 5 || hardDisk.compression > 2)
            {
                return hdErr.HDERR_UNSUPPORTED;
            }


            err = read_sector_map(hardDisk);
            if (err != hdErr.HDERR_NONE)
                return hdErr.HDERR_INVALID_FILE;

            /* init the MD5 computation */
            MD5 md5 = (hardDisk.md5 != null) ? md5 = MD5.Create() : null;
            SHA1 sha1 = (hardDisk.sha1 != null) ? sha1 = SHA1.Create() : null;

            /* loop over source blocks until we run out */

            ulong sizetoGo = hardDisk.totalbytes;

            cache = new byte[hardDisk.blocksize];
            while (sizetoGo > 0)
            {
                /* progress */
                if ((block % 1000) == 0)
                    progress?.Invoke($"Verifying, {(100 - sizetoGo * 100 / hardDisk.totalbytes):N1}% complete...\r");

                /* read the block into the cache */
                err = read_block_into_cache(hardDisk, block);
                if (err != hdErr.HDERR_NONE)
                    return err;

                int sizenext = sizetoGo > (ulong)hardDisk.blocksize ? (int)hardDisk.blocksize : (int)sizetoGo;

                md5?.TransformBlock(cache, 0, sizenext, null, 0);
                sha1?.TransformBlock(cache, 0, sizenext, null, 0);

                /* prepare for the next block */
                block++;
                sizetoGo -= (ulong)sizenext;
            }

            /* compute the final MD5 */
            byte[] tmp = new byte[0];
            md5?.TransformFinalBlock(tmp, 0, 0);
            sha1?.TransformFinalBlock(tmp, 0, 0);


            if (hardDisk.md5 != null)
            {
                if (!ByteArrCompare(hardDisk.md5, md5.Hash))
                {
                    return hdErr.HDERR_DECOMPRESSION_ERROR;
                }
            }

            if (hardDisk.sha1 != null)
            {
                if (hardDisk.version == 4)
                {
                    if (!ByteArrCompare(hardDisk.rawsha1, sha1.Hash))
                    {
                        return hdErr.HDERR_DECOMPRESSION_ERROR;
                    }
                }
                else
                {
                    if (!ByteArrCompare(hardDisk.sha1, sha1.Hash))
                    {
                        return hdErr.HDERR_DECOMPRESSION_ERROR;
                    }
                }
            }

            return hdErr.HDERR_NONE;

        }

        private const int HDFLAGS_HAS_PARENT = 0x00000001;
        private const int HDFLAGS_IS_WRITEABLE = 0x00000002;
        private const int HDCOMPRESSION_ZLIB = 1;
        private const int HDCOMPRESSION_ZLIB_PLUS = 2;
        private const int HDCOMPRESSION_MAX = 3;

        private hdErr read_block_into_cache(hard_disk_info info, int block)
        {
            bool checkCrc = true;
            mapentry mapEntry = info.map[block];
            switch (mapEntry.flags & mapFlags.MAP_ENTRY_FLAG_TYPE_MASK)
            {
                case mapFlags.MAP_ENTRY_TYPE_COMPRESSED:
                    {

                        if (mapEntry.BlockCache != null)
                        {
                            Buffer.BlockCopy(mapEntry.BlockCache, 0, cache, 0, (int)info.blocksize);
                            //already checked CRC for this block when the cache was made
                            checkCrc = false;
                            break;
                        }

                        info.file.Seek((long)info.map[block].offset, SeekOrigin.Begin);

                        switch (info.compression)
                        {
                            case HDCOMPRESSION_ZLIB:
                            case HDCOMPRESSION_ZLIB_PLUS:
                                {
                                    using (var st = new System.IO.Compression.DeflateStream(info.file, System.IO.Compression.CompressionMode.Decompress, true))
                                    {
                                        int bytes = st.Read(cache, 0, (int)info.blocksize);
                                        if (bytes != (int)info.blocksize)
                                            return hdErr.HDERR_READ_ERROR;

                                        if (mapEntry.UseCount > 0)
                                        {
                                            mapEntry.BlockCache = new byte[bytes];
                                            Buffer.BlockCopy(cache, 0, mapEntry.BlockCache, 0, bytes);
                                        }

                                    }
                                    break;
                                }
                            default:
                                {
                                    Console.WriteLine("Unknown compression");
                                    return hdErr.HDERR_DECOMPRESSION_ERROR;

                                }
                        }
                        break;
                    }

                case mapFlags.MAP_ENTRY_TYPE_UNCOMPRESSED:
                    {
                        info.file.Seek((long)info.map[block].offset, SeekOrigin.Begin);
                        int bytes = info.file.Read(cache, 0, (int)info.blocksize);

                        if (bytes != (int)info.blocksize)
                            return hdErr.HDERR_READ_ERROR;
                        break;
                    }

                case mapFlags.MAP_ENTRY_TYPE_MINI:
                    {
                        byte[] tmp = BitConverter.GetBytes(info.map[block].offset);
                        for (int i = 0; i < 8; i++)
                        {
                            cache[i] = tmp[7 - i];
                        }

                        for (int i = 8; i < info.blocksize; i++)
                        {
                            cache[i] = cache[i - 8];
                        }

                        break;
                    }

                case mapFlags.MAP_ENTRY_TYPE_SELF_HUNK:
                    {
                        hdErr ret = read_block_into_cache(info, (int)mapEntry.offset);
                        if (ret != hdErr.HDERR_NONE)
                            return ret;
                        // check CRC in the read_block_into_cache call
                        checkCrc = false;
                        break;
                    }
                default:
                    return hdErr.HDERR_DECOMPRESSION_ERROR;

            }

            if (checkCrc && (mapEntry.flags & mapFlags.MAP_ENTRY_FLAG_NO_CRC) == 0)
            {
                if (!CRC.VerifyDigest(mapEntry.crc, cache, 0, info.blocksize))
                    return hdErr.HDERR_DECOMPRESSION_ERROR;
            }
            return hdErr.HDERR_NONE;
        }

        public static bool ByteArrCompare(byte[] b0, byte[] b1)
        {
            if ((b0 == null) || (b1 == null))
            {
                return false;
            }
            if (b0.Length != b1.Length)
            {
                return false;
            }

            for (int i = 0; i < b0.Length; i++)
            {
                if (b0[i] != b1[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
