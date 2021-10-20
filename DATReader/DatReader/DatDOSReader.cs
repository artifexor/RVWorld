﻿using System.ComponentModel;
using DATReader.DatStore;
using DATReader.Utils;

namespace DATReader.DatReader
{
    public static class DatDOSReader
    {
        public static bool ReadDat(string strFilename, ReportError errorReport, out DatHeader datHeader)
        {
            using (DatFileLoader dfl = new DatFileLoader())
            {
                datHeader = new DatHeader { BaseDir = new DatDir(DatFileType.UnSet) };
                int errorCode = dfl.LoadDat(strFilename, System.Text.Encoding.UTF8);
                if (errorCode != 0)
                {
                    errorReport?.Invoke(strFilename, new Win32Exception(errorCode).Message);
                    return false;
                }

                dfl.Gn();
                if (dfl.EndOfStream())
                {
                    return false;
                }
                if (dfl.Next.ToLower() == "doscenter")
                {
                    dfl.Gn();
                    if (!LoadHeaderFromDat(dfl, strFilename, datHeader, errorReport))
                    {
                        return false;
                    }
                    dfl.Gn();
                }

                while (!dfl.EndOfStream())
                {
                    switch (dfl.Next.ToLower())
                    {
                        case "game":
                            dfl.Gn();
                            if (!LoadGameFromDat(dfl, datHeader.BaseDir, errorReport))
                            {
                                return false;
                            }
                            dfl.Gn();
                            break;
                        default:
                            errorReport?.Invoke(dfl.Filename, "Error: key word '" + dfl.Next + "' not known, on line " + dfl.LineNumber);
                            dfl.Gn();
                            break;
                    }
                }
            }

            return true;
        }


        private static bool LoadHeaderFromDat(DatFileLoader dfl, string filename, DatHeader datHeader, ReportError errorReport)
        {
            if (dfl.Next != "(")
            {
                errorReport?.Invoke(dfl.Filename, "( not found after DOSCenter, on line " + dfl.LineNumber);
                return false;
            }
            dfl.Gn();

            datHeader.Filename = filename;

            while (dfl.Next != ")")
            {
                string nextstr = dfl.Next.ToLower();
                if ((nextstr.Length > 5) && (nextstr.Substring(0, 5) == "name:"))  // this is needed as there is no space after 'name:'
                {
                    datHeader.Name = (dfl.Next.Substring(5) + " " + dfl.GnRest()).Trim();
                    dfl.Gn();
                }
                else
                {

                    switch (nextstr)
                    {
                        case "name":
                        case "name:":
                            datHeader.Name = dfl.GnRest();
                            dfl.Gn();
                            break;
                        case "description":
                        case "description:":
                            datHeader.Description = dfl.GnRest();
                            dfl.Gn();
                            break;
                        case "version":
                        case "version:":
                            datHeader.Version = dfl.GnRest();
                            dfl.Gn();
                            break;
                        case "date":
                        case "date:":
                            datHeader.Date = dfl.GnRest();
                            dfl.Gn();
                            break;
                        case "author":
                        case "author:":
                            datHeader.Author = dfl.GnRest();
                            dfl.Gn();
                            break;
                        case "homepage":
                        case "homepage:":
                            datHeader.Homepage = dfl.GnRest();
                            dfl.Gn();
                            break;
                        case "comment":
                        case "comment:":
                            datHeader.Comment = dfl.GnRest();
                            dfl.Gn();
                            break;
                        default:
                            errorReport?.Invoke(dfl.Filename, "Error: key word '" + dfl.Next + "' not known in DOSReader, on line " + dfl.LineNumber);
                            dfl.Gn();
                            break;
                    }
                }
            }
            return true;
        }


        private static bool LoadGameFromDat(DatFileLoader dfl, DatDir parentDir, ReportError errorReport)
        {
            if (dfl.Next != "(")
            {
                errorReport?.Invoke(dfl.Filename, "( not found after game, on line " + dfl.LineNumber);
                return false;
            }
            dfl.Gn();

            string sNext = dfl.Next.ToLower();

            if (sNext != "name")
            {
                errorReport?.Invoke(dfl.Filename, "Name not found as first object in ( ), on line " + dfl.LineNumber);
                return false;
            }


            string name = dfl.GnRest();
            int nameLength = name.Length;
            if (nameLength > 4 && name.ToLower().Substring(nameLength - 4, 4) == ".zip")
                name = name.Substring(0, nameLength - 4);

            dfl.Gn();

            DatDir dDir = new DatDir(DatFileType.UnSet) { Name = name, DGame = new DatGame() };

            while (dfl.Next != ")")
            {
                switch (dfl.Next.ToLower())
                {
                    case "file":
                        dfl.Gn();
                        if (!LoadFileFromDat(dfl, dDir, errorReport))
                        {
                            return false;
                        }
                        dfl.Gn();
                        break;
                    case "rom":
                        dfl.Gn();
                        if (!LoadFileFromDat(dfl, dDir, errorReport))
                        {
                            return false;
                        }
                        dfl.Gn();
                        break;
                    default:
                        errorReport?.Invoke(dfl.Filename, "Error: key word '" + dfl.Next + "' not known in game, on line " + dfl.LineNumber);
                        dfl.Gn();
                        break;
                }
            }
            parentDir.ChildAdd(dDir);
            return true;
        }

        private static bool LoadFileFromDat(DatFileLoader dfl, DatDir parentDir, ReportError errorReport)
        {
            if (dfl.Next != "(")
            {
                errorReport?.Invoke(dfl.Filename, "( not found after file, on line " + dfl.LineNumber);
                return false;
            }
            dfl.Gn();

            if (dfl.Next.ToLower() != "name")
            {
                errorReport?.Invoke(dfl.Filename, "Name not found as first object in ( ), on line " + dfl.LineNumber);
                return false;
            }

            DatFile dRom = new DatFile(DatFileType.UnSet)
            {
                Name = dfl.GnNameToSize()
            };
            dfl.Gn();


            while (dfl.Next != ")")
            {
                switch (dfl.Next.ToLower())
                {
                    case "size":
                        dRom.Size = VarFix.ULong(dfl.Gn());
                        dfl.Gn();
                        break;
                    case "crc":
                        dRom.CRC = VarFix.CleanMD5SHA1(dfl.Gn(), 8);
                        dfl.Gn();
                        break;
                    case "date":
                        dRom.DateModified = dfl.Gn() + " " + dfl.Gn();
                        dfl.Gn();
                        break;
                    default:
                        errorReport?.Invoke(dfl.Filename, "Error: key word '" + dfl.Next + "' not known in rom, on line " + dfl.LineNumber);
                        dfl.Gn();
                        break;
                }
            }

            parentDir.ChildAdd(dRom);

            return true;
        }


    }
}