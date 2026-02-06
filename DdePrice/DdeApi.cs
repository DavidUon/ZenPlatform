using System;
using System.Runtime.InteropServices;

namespace ZenPlatform.DdePrice
{
    public static class DdeApi
    {
        public const uint DMLERR_NO_ERROR = 0;
        public const uint DDE_F_ACK = 0x8000;
        public const uint XTYP_DISCONNECT = 0x00C0;

        public const uint XCLASS_BOOL = 0x1000;
        public const uint XCLASS_DATA = 0x2000;
        public const uint XCLASS_FLAGS = 0x4000;
        public const uint XCLASS_NOTIFICATION = 0x8000;

        public const uint XTYP_REQUEST = 0x00B0 | XCLASS_DATA;
        public const uint XTYP_ADVDATA = 0x0010 | XCLASS_FLAGS;
        public const uint XTYP_ADVSTART = 0x0030 | XCLASS_BOOL;
        public const uint XTYP_ADVSTOP = 0x0040 | XCLASS_NOTIFICATION;

        public const uint XTYPF_ACKREQ = 0x0008;
        public const uint CF_UNICODETEXT = 13;
        public const uint TIMEOUT_ASYNC = 0xFFFFFFFF;
        public const uint TIMEOUT_SYNC = 3000;
        public const uint CF_TEXT = 1;
        public const uint APPCMD_CLIENTONLY = 0x00000010;
        public const uint CP_ACP = 1004;

        public delegate uint DdeCallbackDelegate(
            uint uType,
            uint uFmt,
            IntPtr hConv,
            IntPtr hsz1,
            IntPtr hsz2,
            IntPtr hData,
            IntPtr dwData1,
            IntPtr dwData2);

        [DllImport("user32.dll")]
        public static extern uint DdeInitialize(out IntPtr pidInst, DdeCallbackDelegate callback, uint afCmd, uint ulRes);

        [DllImport("user32.dll")]
        public static extern void DdeUninitialize(IntPtr idInst);

        [DllImport("user32.dll")]
        public static extern IntPtr DdeConnect(IntPtr idInst, IntPtr hszService, IntPtr hszTopic, IntPtr pCC);

        [DllImport("user32.dll")]
        public static extern void DdeDisconnect(IntPtr hConv);

        [DllImport("user32.dll")]
        public static extern IntPtr DdeCreateStringHandle(IntPtr idInst, string psz, uint codePage);

        [DllImport("user32.dll")]
        public static extern void DdeFreeStringHandle(IntPtr idInst, IntPtr hsz);

        [DllImport("user32.dll")]
        public static extern uint DdeQueryString(IntPtr idInst, IntPtr hsz, [Out] char[]? psz, uint cchMax, uint iCodePage);

        [DllImport("user32.dll")]
        public static extern IntPtr DdeClientTransaction(
            IntPtr pData,
            uint cbData,
            IntPtr hConv,
            IntPtr hszItem,
            uint wFmt,
            uint wType,
            uint timeout,
            out uint result);

        [DllImport("user32.dll")]
        public static extern IntPtr DdeAccessData(IntPtr hData, out uint pcbData);

        [DllImport("user32.dll")]
        public static extern uint DdeUnaccessData(IntPtr hData);

        [DllImport("user32.dll")]
        public static extern uint DdeFreeDataHandle(IntPtr hData);

        [DllImport("user32.dll")]
        public static extern uint DdeGetLastError(IntPtr idInst);

        [DllImport("user32.dll")]
        public static extern IntPtr DdeCreateDataHandle(
            IntPtr idInst,
            byte[] pSrc,
            uint cb,
            uint off,
            IntPtr hszItem,
            uint wFmt,
            uint afCmd);
    }
}
