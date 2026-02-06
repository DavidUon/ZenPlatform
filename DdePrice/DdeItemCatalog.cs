using System;
using System.Collections.Generic;
using System.Linq;

namespace ZenPlatform.DdePrice
{
    public sealed record DdeProduct(string DisplayName, string ContractName, string DdeSymbol);

    public static class DdeItemCatalog
    {
        public static readonly IReadOnlyList<DdeProduct> Products = new[]
        {
            new DdeProduct("大型台指", "大型台指", "WTX"),
            new DdeProduct("小型台指", "小型台指", "WMT"),
            new DdeProduct("微型台指", "微型台指", "WTM")
        };

        public static readonly IReadOnlyDictionary<int, string> PriceItemNames = new Dictionary<int, string>
        {
            { 101, "買價" },
            { 102, "賣價" },
            { 125, "成交價" },
            { 143, "時間" },
            { 184, "漲跌" },
            { 185, "漲跌幅" },
            { 229, "距到期日" },
            { 404, "成交量" }
        };

        public static readonly IReadOnlyList<int> DefaultPriceItems = new[]
        {
            101, 102, 125, 143, 404, 184, 185, 229
        };

        public static bool TryGetProduct(string displayName, out DdeProduct product)
        {
            var normalized = NormalizeProductName(displayName);
            product = Products.FirstOrDefault(p => string.Equals(p.DisplayName, normalized, StringComparison.OrdinalIgnoreCase))
                ?? new DdeProduct(string.Empty, string.Empty, string.Empty);
            return !string.IsNullOrEmpty(product.DdeSymbol);
        }

        public static string GetMonthCode(int month)
        {
            return month switch
            {
                1 => "F",
                2 => "G",
                3 => "H",
                4 => "J",
                5 => "K",
                6 => "M",
                7 => "N",
                8 => "Q",
                9 => "U",
                10 => "V",
                11 => "X",
                12 => "Z",
                _ => throw new ArgumentOutOfRangeException(nameof(month), "無效的月份")
            };
        }

        public static int GetMonthFromCode(char monthCode)
        {
            return monthCode switch
            {
                'F' => 1,
                'G' => 2,
                'H' => 3,
                'J' => 4,
                'K' => 5,
                'M' => 6,
                'N' => 7,
                'Q' => 8,
                'U' => 9,
                'V' => 10,
                'X' => 11,
                'Z' => 12,
                _ => 0
            };
        }

        public static bool TryGetProductBySymbol(string ddeSymbol, out DdeProduct product)
        {
            product = Products.FirstOrDefault(p => string.Equals(p.DdeSymbol, ddeSymbol, StringComparison.OrdinalIgnoreCase))
                ?? new DdeProduct(string.Empty, string.Empty, string.Empty);
            return !string.IsNullOrEmpty(product.DdeSymbol);
        }

        public static bool TryParseDdeItem(string item, out string productName, out int year, out int month, out string fieldName)
        {
            productName = "";
            fieldName = "";
            year = 0;
            month = 0;

            var dotIndex = item.LastIndexOf('.');
            if (dotIndex <= 0 || dotIndex >= item.Length - 1)
            {
                return false;
            }

            var contractPart = item.Substring(0, dotIndex);
            var priceItemCode = item.Substring(dotIndex + 1);
            if (contractPart.Length < 3 || !int.TryParse(priceItemCode, out var itemCode))
            {
                return false;
            }

            if (!TryGetPriceItemName(itemCode, out var priceName))
            {
                return false;
            }

            var monthCode = contractPart[^2];
            var yearDigit = contractPart[^1];
            var ddeSymbol = contractPart.Substring(0, contractPart.Length - 2);

            if (!TryGetProductBySymbol(ddeSymbol, out var product))
            {
                return false;
            }

            var parsedMonth = GetMonthFromCode(monthCode);
            if (parsedMonth <= 0 || !char.IsDigit(yearDigit))
            {
                return false;
            }

            productName = product.DisplayName;
            fieldName = priceName;
            month = parsedMonth;
            year = 2020 + (yearDigit - '0');
            return true;
        }

        public static bool TryGetPriceItemName(int priceItem, out string name)
        {
            if (PriceItemNames.TryGetValue(priceItem, out var value))
            {
                name = value;
                return true;
            }

            name = string.Empty;
            return false;
        }

        public static string BuildItem(string ddeSymbol, int year, int month, int priceItem)
        {
            return $"{ddeSymbol}{GetMonthCode(month)}{year % 10}.{priceItem}";
        }

        private static string NormalizeProductName(string name)
        {
            return name switch
            {
                "大台" => "大型台指",
                "小台" => "小型台指",
                "微台" => "微型台指",
                "大台指" => "大型台指",
                "小台指" => "小型台指",
                "微台指" => "微型台指",
                _ => name
            };
        }
    }
}
