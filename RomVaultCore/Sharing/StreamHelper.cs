using System.Net.Sockets;
using System.Text;

namespace Server
{
    public static class StreamHelper
    {
        #region CoreCom
        public static byte[] GetBytes(NetworkStream stream)
        {
            byte[] bReadLength = ReadBytesWithLength(stream, 4);
            int readLength = ByteToInt(bReadLength);
            return ReadBytesWithLength(stream, readLength);
        }

        private static byte[] ReadBytesWithLength(NetworkStream stream, int readLength)
        {
            var bRead = new byte[readLength];

            int datapos = 0;
            while (datapos != readLength)
            {
                int readStreamLength = stream.Read(bRead, datapos, readLength - datapos);
                datapos += readStreamLength;
            }

            return bRead;
        }

        public static void SendBytes(NetworkStream stream, byte[] bSend)
        {
            byte[] bSendLength = IntToByte(bSend.Length);

            stream.Write(bSendLength, 0, bSendLength.Length);
            stream.Write(bSend, 0, bSend.Length);
        }


        public static byte[] SendGetBytes(NetworkStream stream, byte[] bSend)
        {
            SendBytes(stream, bSend);

            return GetBytes(stream);
        }
        #endregion

        #region StringHelpers

        public static string SendGetString(NetworkStream stream, string str)
        {
            byte[] bSend = Encoding.UTF8.GetBytes(str);

            byte[] bRead = SendGetBytes(stream, bSend);

            return bRead == null
                       ? null
                       : Encoding.UTF8.GetString(bRead);
        }

        public static string GetString(NetworkStream stream)
        {
            byte[] buffer = GetBytes(stream);

            return buffer == null
                       ? null
                       : Encoding.ASCII.GetString(buffer);
        }

        public static void SendString(NetworkStream stream, string str)
        {
            byte[] bSend = Encoding.UTF8.GetBytes(str);

            SendBytes(stream, bSend);
        }
        #endregion

        #region helper
        private static int ByteToInt(byte[] val)
        {
            return val[0] | (val[1] << 8) | (val[2] << 16) | (val[3] << 24);
        }

        private static byte[] IntToByte(int val)
        {
            return new[] { (byte)(val & 0xff), (byte)((val >> 8) & 0xff), (byte)((val >> 16) & 0xff), (byte)((val >> 24) & 0xff) };
        }
        #endregion

    }
}
