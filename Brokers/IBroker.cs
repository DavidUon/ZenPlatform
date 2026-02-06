using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Brokers;

public enum BrokerNotifyType
{
    DomesticLoginSuccess,
    ForeignLoginSuccess,
    DomesticLoginFailed,
    ForeignLoginFailed,
    DomesticGeneralReport,
    ForeignGeneralReport,
    DomesticOrderReport,
    ForeignOrderReport
}

public interface IBroker
{
    bool IsDomesticLoginSuccess { get; }
    bool IsForeignLoginSuccess { get; }
    bool IsDomesticConnected { get; }
    bool IsForeignConnected { get; }
    event Action<BrokerNotifyType, string>? OnBrokerNotify;
    string Login(string loginId, string password, string branchName, string account);
    Task<string> LoginAsync(string loginId, string password, string branchName, string account);
    string Logout();
    string CheckConnect(bool isDomestic, out string message);
    decimal QueryDomesticAvailableMargin(out string resultCode, out string message);
    decimal QueryDomesticTotalEquity(out string resultCode, out string message);
    decimal QueryForeignAvailableMargin(out string resultCode, out string message);
    decimal QueryForeignTotalEquity(out string resultCode, out string message);
    string QueryDomesticCommodities(out string message);
    string QueryForeignCommodities(out string message);
    string GetCertStatus(string loginId, out string startDate, out string expireDate, out string message);
    Task<decimal> SendOrderAsync(OrderForm form, TimeSpan? timeout = null);
    string PlaceOrder(
        string sessionId,
        string product,
        int year,
        int month,
        bool isBuy,
        decimal price,
        bool isMarket,
        bool isDayTrading);
    bool CancelOrder(string sessionId);
}
