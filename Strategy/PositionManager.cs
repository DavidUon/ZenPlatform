using System;

namespace ZenPlatform.Strategy
{
    public sealed class PositionManager
    {
        public int TotalKou { get; private set; }
        public decimal AvgEntryPrice { get; private set; }
        public decimal PingProfit { get; private set; }
        public decimal FloatProfit { get; private set; }

        public decimal AddPosition(bool isBuy, int qty, decimal price)
        {
            if (qty <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(qty));
            }

            int signedQty = isBuy ? qty : -qty;
            int netQty = TotalKou;
            decimal realizedDelta = 0;

            if (netQty == 0)
            {
                TotalKou = signedQty;
                AvgEntryPrice = price;
                UpdateFloatProfit(price, price);
                return 0;
            }

            int netSign = Math.Sign(netQty);
            int newSign = Math.Sign(signedQty);

            if (netSign == newSign)
            {
                decimal absNet = Math.Abs(netQty);
                decimal absAdd = Math.Abs(signedQty);
                AvgEntryPrice = (AvgEntryPrice * absNet + price * absAdd) / (absNet + absAdd);
                TotalKou = netQty + signedQty;
                UpdateFloatProfit(price, price);
                return 0;
            }

            int closeQty = Math.Min(Math.Abs(netQty), Math.Abs(signedQty));
            decimal pnl = netSign > 0 ? (price - AvgEntryPrice) : (AvgEntryPrice - price);
            PingProfit += pnl;
            realizedDelta = pnl;

            TotalKou = netQty + signedQty;
            if (TotalKou == 0)
            {
                AvgEntryPrice = 0;
                FloatProfit = 0;
                return realizedDelta;
            }

            if (Math.Sign(TotalKou) != netSign)
            {
                AvgEntryPrice = price;
            }
            UpdateFloatProfit(price, price);
            return realizedDelta;
        }

        public void OnTick(decimal buyPrice, decimal sellPrice)
        {
            UpdateFloatProfit(buyPrice, sellPrice);
        }

        public PositionSnapshot ToSnapshot()
        {
            return new PositionSnapshot
            {
                TotalKou = TotalKou,
                AvgEntryPrice = AvgEntryPrice,
                PingProfit = PingProfit,
                FloatProfit = FloatProfit
            };
        }

        public void FromSnapshot(PositionSnapshot snapshot)
        {
            TotalKou = snapshot.TotalKou;
            AvgEntryPrice = snapshot.AvgEntryPrice;
            PingProfit = snapshot.PingProfit;
            FloatProfit = snapshot.FloatProfit;
        }

        private void UpdateFloatProfit(decimal buyPrice, decimal sellPrice)
        {
            if (TotalKou == 0)
            {
                FloatProfit = 0;
                return;
            }

            if (TotalKou > 0)
            {
                FloatProfit = buyPrice - AvgEntryPrice;
                return;
            }

            FloatProfit = AvgEntryPrice - sellPrice;
        }
    }

    public sealed class PositionSnapshot
    {
        public int TotalKou { get; set; }
        public decimal AvgEntryPrice { get; set; }
        public decimal PingProfit { get; set; }
        public decimal FloatProfit { get; set; }
    }
}
