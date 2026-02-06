using System;
using System.Collections.Generic;

namespace ZenPlatform.DdePrice
{
    public enum DdeErrCode
    {
        NoError,
        初始化未成功,
        初始化失敗,
        尚未建立連線,
        AlreadyInitialized,
        連線失敗,
        DDE交易失敗,
        HotLinkFailed,
        InvalidState,
        發生未知錯誤,
        RequestFailed
    }

    public class DdeException : Exception
    {
        private static readonly Dictionary<DdeErrCode, string> DdeExpMsg = new()
        {
            { DdeErrCode.初始化失敗, "DDE 系統初始化失敗" },
            { DdeErrCode.初始化未成功, "DDE初始化未成功" },
            { DdeErrCode.尚未建立連線, "尚未建立連線" },
            { DdeErrCode.AlreadyInitialized, "連線已存在，請勿重複連線" },
            { DdeErrCode.連線失敗, "連線失敗" },
            { DdeErrCode.DDE交易失敗, "DDE交易失敗" },
            { DdeErrCode.HotLinkFailed, "HotLink 請求失敗" },
            { DdeErrCode.InvalidState, "DDE 連線狀態無效，操作無法執行" },
            { DdeErrCode.發生未知錯誤, "發生未知錯誤" },
            { DdeErrCode.RequestFailed, "Request 失敗" }
        };

        public DdeException(DdeErrCode errCode, uint lastError = 0)
            : base($"{GetErrMsg(errCode)}。錯誤碼: {lastError}")
        {
        }

        private static string GetErrMsg(DdeErrCode errCode)
        {
            return DdeExpMsg.TryGetValue(errCode, out var msg) ? msg : "未定義的錯誤";
        }
    }
}
