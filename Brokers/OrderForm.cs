namespace Brokers;

public enum ContractName
{
    大型台指,
    小型台指,
    微型台指
}

public record OrderForm(
    ContractName ContractName,
    int Year,
    int Month,
    bool IsBuy,
    int Quantity,
    bool IsMarketOrder = false,
    bool IsDayTrading = false,
    decimal? LimitPrice = null);
