using System;
using System.Runtime.InteropServices;

namespace ZenPlatform.DdePrice
{
    public class DdeCtrl
    {
        private IntPtr _idInst = IntPtr.Zero;
        private readonly DdeApi.DdeCallbackDelegate _ddeCallback;
        private static readonly object SyncRoot = new();

        public event Action<IntPtr, string, string>? DataUpdate;
        public event Action<IntPtr>? ConnectionConfirmed;
        public event Action<IntPtr>? ConnectionLost;

        public DdeCtrl()
        {
            _ddeCallback = DdeCallback;
        }

        public IntPtr Initialize()
        {
            lock (SyncRoot)
            {
                if (_idInst == IntPtr.Zero)
                {
                    DdeApi.DdeInitialize(out _idInst, _ddeCallback, DdeApi.APPCMD_CLIENTONLY, 0);
                    if (_idInst == IntPtr.Zero)
                    {
                        var lastError = DdeApi.DdeGetLastError(IntPtr.Zero);
                        throw new DdeException(DdeErrCode.初始化失敗, lastError);
                    }
                }
            }

            return _idInst;
        }

        public IntPtr Connect(string service, string topic)
        {
            if (_idInst == IntPtr.Zero)
            {
                throw new DdeException(DdeErrCode.初始化未成功);
            }

            IntPtr hszService = DdeApi.DdeCreateStringHandle(_idInst, service, DdeApi.CP_ACP);
            IntPtr hszTopic = DdeApi.DdeCreateStringHandle(_idInst, topic, DdeApi.CP_ACP);

            IntPtr hConv = IntPtr.Zero;
            try
            {
                hConv = DdeApi.DdeConnect(_idInst, hszService, hszTopic, IntPtr.Zero);
            }
            finally
            {
                DdeApi.DdeFreeStringHandle(_idInst, hszService);
                DdeApi.DdeFreeStringHandle(_idInst, hszTopic);
            }

            if (hConv == IntPtr.Zero)
            {
                var lastError = DdeApi.DdeGetLastError(_idInst);
                throw new DdeException(DdeErrCode.連線失敗, lastError);
            }

            return hConv;
        }

        public void Disconnect(IntPtr hConv)
        {
            if (hConv == IntPtr.Zero)
            {
                throw new DdeException(DdeErrCode.尚未建立連線);
            }

            try
            {
                DdeApi.DdeDisconnect(hConv);
            }
            catch
            {
                var lastError = DdeApi.DdeGetLastError(_idInst);
                throw new DdeException(DdeErrCode.發生未知錯誤, lastError);
            }
        }

        public string Request(IntPtr hConv, string item, uint timeout = DdeApi.TIMEOUT_SYNC)
        {
            if (hConv == IntPtr.Zero)
            {
                throw new DdeException(DdeErrCode.尚未建立連線);
            }

            IntPtr hszItem = DdeApi.DdeCreateStringHandle(_idInst, item, DdeApi.CP_ACP);
            try
            {
                uint result = 0;
                IntPtr hData = DdeApi.DdeClientTransaction(
                    IntPtr.Zero,
                    0,
                    hConv,
                    hszItem,
                    DdeApi.CF_TEXT,
                    DdeApi.XTYP_REQUEST,
                    timeout,
                    out result);

                if (hData == IntPtr.Zero || result != DdeApi.DMLERR_NO_ERROR)
                {
                    var lastError = DdeApi.DdeGetLastError(_idInst);
                    throw new DdeException(DdeErrCode.DDE交易失敗, lastError);
                }

                try
                {
                    IntPtr pData = DdeApi.DdeAccessData(hData, out uint dataSize);
                    var data = Marshal.PtrToStringAnsi(pData, (int)dataSize)?.TrimEnd('\0') ?? "";
                    DdeApi.DdeUnaccessData(hData);
                    return data;
                }
                finally
                {
                    DdeApi.DdeFreeDataHandle(hData);
                }
            }
            finally
            {
                DdeApi.DdeFreeStringHandle(_idInst, hszItem);
            }
        }

        public IntPtr AddHotLink(IntPtr hConv, string item, uint fmt = DdeApi.CF_TEXT)
        {
            if (hConv == IntPtr.Zero)
            {
                throw new DdeException(DdeErrCode.尚未建立連線);
            }

            IntPtr hszItem = DdeApi.DdeCreateStringHandle(_idInst, item, DdeApi.CP_ACP);
            try
            {
                uint result = 0;
                IntPtr ret = DdeApi.DdeClientTransaction(
                    IntPtr.Zero,
                    0,
                    hConv,
                    hszItem,
                    fmt,
                    DdeApi.XTYP_ADVSTART | DdeApi.XTYPF_ACKREQ,
                    DdeApi.TIMEOUT_SYNC,
                    out result);

                if (ret == IntPtr.Zero)
                {
                    uint err = DdeApi.DdeGetLastError(_idInst);
                    if (err != DdeApi.DMLERR_NO_ERROR)
                    {
                        throw new DdeException(DdeErrCode.DDE交易失敗, err);
                    }
                }

                return ret;
            }
            finally
            {
                DdeApi.DdeFreeStringHandle(_idInst, hszItem);
            }
        }

        public bool RemoveHotLink(IntPtr hConv, string item, uint fmt = DdeApi.CF_TEXT)
        {
            if (hConv == IntPtr.Zero)
            {
                throw new DdeException(DdeErrCode.尚未建立連線);
            }

            IntPtr hszItem = DdeApi.DdeCreateStringHandle(_idInst, item, DdeApi.CP_ACP);
            try
            {
                uint result = 0;
                IntPtr ret = DdeApi.DdeClientTransaction(
                    IntPtr.Zero,
                    0,
                    hConv,
                    hszItem,
                    fmt,
                    DdeApi.XTYP_ADVSTOP,
                    DdeApi.TIMEOUT_SYNC,
                    out result);

                if (ret == IntPtr.Zero)
                {
                    uint err = DdeApi.DdeGetLastError(_idInst);
                    if (err != DdeApi.DMLERR_NO_ERROR)
                    {
                        throw new DdeException(DdeErrCode.DDE交易失敗, err);
                    }
                }

                return true;
            }
            finally
            {
                DdeApi.DdeFreeStringHandle(_idInst, hszItem);
            }
        }

        private string DdeQueryString(IntPtr idInst, IntPtr hsz)
        {
            if (hsz == IntPtr.Zero)
            {
                return "";
            }

            uint length = DdeApi.DdeQueryString(idInst, hsz, null, 0, DdeApi.CP_ACP);
            if (length == 0)
            {
                return "";
            }

            char[] buffer = new char[length + 1];
            DdeApi.DdeQueryString(idInst, hsz, buffer, length + 1, DdeApi.CP_ACP);
            return new string(buffer, 0, (int)length);
        }

        private uint DdeCallback(uint uType, uint uFmt, IntPtr hConv, IntPtr hsz1, IntPtr hsz2, IntPtr hData, IntPtr dwData1, IntPtr dwData2)
        {
            uint txn = uType & 0x00F0;

            switch (txn)
            {
                case 0x0080:
                    ConnectionConfirmed?.Invoke(hConv);
                    return DdeApi.DDE_F_ACK;

                case 0x00C0:
                    ConnectionLost?.Invoke(hConv);
                    return DdeApi.DDE_F_ACK;

                case 0x0010:
                    {
                        string item = DdeQueryString(_idInst, hsz2);
                        string data = "";

                        if (hData != IntPtr.Zero)
                        {
                            IntPtr p = IntPtr.Zero;
                            try
                            {
                                p = DdeApi.DdeAccessData(hData, out uint cb);
                                if (uFmt == DdeApi.CF_TEXT)
                                {
                                    data = Marshal.PtrToStringAnsi(p, (int)cb)?.TrimEnd('\0') ?? "";
                                }
                                else if (uFmt == DdeApi.CF_UNICODETEXT)
                                {
                                    data = Marshal.PtrToStringUni(p, (int)(cb / 2))?.TrimEnd('\0') ?? "";
                                }
                            }
                            finally
                            {
                                if (p != IntPtr.Zero)
                                {
                                    DdeApi.DdeUnaccessData(hData);
                                }
                            }
                        }

                        DataUpdate?.Invoke(hConv, item, data);
                        return DdeApi.DDE_F_ACK;
                    }
            }

            return DdeApi.DDE_F_ACK;
        }
    }
}
