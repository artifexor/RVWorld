using System;
using System.Data.SQLite;
using FileHeaderReader;
using RVIO;
using RVXCore.Util;

namespace RVXCore.DB
{
    public class RvFile
    {
        private static SQLiteCommand _commandRvFileWrite;
        private static SQLiteCommand _commandCheckForAnyFiles;
        public uint FileId;
        public ulong Size;
        public ulong CompressedSize;
        public byte[] CRC;
        public byte[] SHA1;
        public byte[] MD5;

        public HeaderFileType AltType;
        public ulong? AltSize;
        public byte[] AltCRC;
        public byte[] AltSHA1;
        public byte[] AltMD5;

        public static void CreateTable()
        {
            DBSqlite.db.ExecuteNonQuery(@"
                CREATE TABLE IF NOT EXISTS [FILES] (
                    [FileId] INTEGER PRIMARY KEY NOT NULL,
                    [size] INTEGER NOT NULL,
                    [compressedsize] INTEGER NULL,
                    [crc] VARCHAR(8) NULL,
                    [sha1] VARCHAR(40) NULL,
                    [md5] VARCHAR(32) NULL,
                    [alttype] VARCHAR(8) NULL,
                    [altsize] INTEGER NULL,
                    [altcrc] VARCHAR(8) NULL,
                    [altsha1] VARCHAR(40) NULL,
                    [altmd5] VARCHAR(32) NULL
                );
            ");
        }

        public void DBWrite()
        {
            DBSqlite.db.Begin();
            RvFileWrite();
            RvRomFileMatchup.MatchFiletoRoms(this);
            DBSqlite.db.Commit();
        }


        private void RvFileWrite()
        {
            if (_commandRvFileWrite == null)
            {
                _commandRvFileWrite = new SQLiteCommand(
                    @"INSERT INTO FILES (size,compressedsize,crc,sha1,md5,alttype,altsize,altcrc,altsha1,altmd5)
                        VALUES (@Size,@compressedsize,@CRC,@SHA1,@MD5,@alttype,@altsize,@altcrc,@altsha1,@altmd5);
                    SELECT last_insert_rowid();", DBSqlite.db.Connection);

                _commandRvFileWrite.Parameters.Add(new SQLiteParameter("size"));
                _commandRvFileWrite.Parameters.Add(new SQLiteParameter("compressedsize"));
                _commandRvFileWrite.Parameters.Add(new SQLiteParameter("crc"));
                _commandRvFileWrite.Parameters.Add(new SQLiteParameter("sha1"));
                _commandRvFileWrite.Parameters.Add(new SQLiteParameter("md5"));
                _commandRvFileWrite.Parameters.Add(new SQLiteParameter("alttype"));
                _commandRvFileWrite.Parameters.Add(new SQLiteParameter("altsize"));
                _commandRvFileWrite.Parameters.Add(new SQLiteParameter("altcrc"));
                _commandRvFileWrite.Parameters.Add(new SQLiteParameter("altsha1"));
                _commandRvFileWrite.Parameters.Add(new SQLiteParameter("altmd5"));
            }

            _commandRvFileWrite.Parameters["size"].Value = Size;
            _commandRvFileWrite.Parameters["compressedsize"].Value = CompressedSize;
            _commandRvFileWrite.Parameters["crc"].Value = VarFix.ToDBString(CRC);
            _commandRvFileWrite.Parameters["sha1"].Value = VarFix.ToDBString(SHA1);
            _commandRvFileWrite.Parameters["md5"].Value = VarFix.ToDBString(MD5);
            _commandRvFileWrite.Parameters["alttype"].Value = (int) AltType;
            _commandRvFileWrite.Parameters["altsize"].Value = AltSize;
            _commandRvFileWrite.Parameters["altcrc"].Value = VarFix.ToDBString(AltCRC);
            _commandRvFileWrite.Parameters["altsha1"].Value = VarFix.ToDBString(AltSHA1);
            _commandRvFileWrite.Parameters["altmd5"].Value = VarFix.ToDBString(AltMD5);

            object res = _commandRvFileWrite.ExecuteScalar();
            FileId = Convert.ToUInt32(res);
        }


        public static bool FilesinDBCheck()
        {
            if (_commandCheckForAnyFiles == null)
            {
                _commandCheckForAnyFiles = new SQLiteCommand("SELECT COUNT(1) FROM FILES LIMIT 1", DBSqlite.db.Connection);
            }

            object res = _commandCheckForAnyFiles.ExecuteScalar();
            if ((res == null) || (res == DBNull.Value))
            {
                return true;
            }
            return Convert.ToInt32(res) == 0;
        }


        public static RvFile fromGZip(string filename, byte[] bytes, ulong compressedSize)
        {
            RvFile retFile = new RvFile();
            retFile.CompressedSize = compressedSize;

            retFile.SHA1 = VarFix.CleanMD5SHA1(Path.GetFileNameWithoutExtension(filename), 40);
            retFile.MD5 = new byte[16];
            Array.Copy(bytes, 0, retFile.MD5, 0, 16);
            retFile.CRC = new byte[4];
            Array.Copy(bytes, 16, retFile.CRC, 0, 4);
            retFile.Size = BitConverter.ToUInt64(bytes, 20);

            if (bytes.Length == 28)
                return retFile;

            retFile.AltType = (HeaderFileType)bytes[28];
            retFile.AltMD5 = new byte[16];
            Array.Copy(bytes, 29, retFile.AltMD5, 0, 16);
            retFile.AltSHA1 = new byte[20];
            Array.Copy(bytes, 45, retFile.AltSHA1, 0, 20);
            retFile.AltCRC = new byte[4];
            Array.Copy(bytes, 65, retFile.AltCRC, 0, 4);
            retFile.AltSize = BitConverter.ToUInt64(bytes, 69);

            return retFile;
        }

        public byte[] SetExtraData()
        {
            bool alt = FileHeaderReader.FileHeaderReader.AltHeaderFile(AltType);
            byte[] retData =  alt
                ? new byte[77] 
                : new byte[28];

            Array.Copy(MD5,0,retData,0,16);
            Array.Copy(CRC, 0, retData, 16, 4);
            Array.Copy(BitConverter.GetBytes(Size),0,retData,20,8);

            if (!alt) 
                return retData;

            retData[28] = (byte) AltType;
            Array.Copy(AltMD5, 0, retData, 29, 16);
            Array.Copy(AltSHA1, 0, retData,45 , 20);
            Array.Copy(AltCRC, 0, retData, 65, 4);
            Array.Copy(BitConverter.GetBytes((ulong)AltSize), 0, retData, 69, 8);

            return retData;
        }
    }
}