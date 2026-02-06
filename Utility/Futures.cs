using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility
{
	/// <summary>
	/// 期貨資訊靜態類別
	/// </summary>
	public static class FutureInfo
	{
		// 期貨合約對應表
		private static readonly Dictionary<string, (string ApiSymbol, string DdeSymbol, decimal TickSize, string TimeZone)> _allContracts = new()
		{
			["大型台指"] = ("TXF", "WTX", 1m, "Taipei Standard Time"),
			["小型台指"] = ("MXF", "WMT", 1m, "Taipei Standard Time"),
			["微型台指"] = ("TMF", "WTM", 1m, "Taipei Standard Time"),
			["大型恆生"] = ("HSI", "WHS", 1m, "Hong Kong Standard Time"),
			["小型恆生"] = ("MHI", "WMH", 1m, "Hong Kong Standard Time")
		};

		// 交易所對應表
		private static readonly Dictionary<string, string> _exchangeMapping = new()
		{
			["TXF"] = "TIMEX",
			["MXF"] = "TIMEX",
			["TMF"] = "TIMEX",
			["HSI"] = "HKF",
			["MHI"] = "HKF"
		};

		/// <summary>
		/// 根據商品名稱取得交易所代號
		/// </summary>
		public static string GetExchange(string productName)
		{
			if (_allContracts.TryGetValue(productName, out var contract))
			{
				return _exchangeMapping.GetValueOrDefault(contract.ApiSymbol, "");
			}
			return "";
		}

		/// <summary>
		/// 根據商品名稱取得API符號
		/// </summary>
		public static string GetApiSymbol(string productName)
		{
			return _allContracts.GetValueOrDefault(productName, ("", "", 0, "")).ApiSymbol;
		}

		/// <summary>
		/// 根據商品名稱取得DDE符號
		/// </summary>
		public static string GetDdeSymbol(string productName)
		{
			return _allContracts.GetValueOrDefault(productName, ("", "", 0, "")).DdeSymbol;
		}

		/// <summary>
		/// 根據商品名稱取得跳動點
		/// </summary>
		public static decimal GetTickSize(string productName)
		{
			return _allContracts.GetValueOrDefault(productName, ("", "", 0, "")).TickSize;
		}

		/// <summary>
		/// 根據商品名稱取得時區
		/// </summary>
		public static string GetTimeZone(string productName)
		{
			return _allContracts.GetValueOrDefault(productName, ("", "", 0, "")).TimeZone;
		}

		/// <summary>
		/// 取得所有支援的商品名稱
		/// </summary>
		public static IEnumerable<string> GetAllProductNames()
		{
			return _allContracts.Keys;
		}

		/// <summary>
		/// 檢查商品名稱是否存在
		/// </summary>
		public static bool IsValidProduct(string productName)
		{
			return _allContracts.ContainsKey(productName);
		}
	}

	public class Contracts
	{
		private string _name = "";
		public int Year;
		public int Month;
		public string ApiSymbol { get; private set; } = "";
		public string DdeSymbol { get; private set; } = "";
		public decimal TickSize { get; private set; }
		public string TimeZone { get; private set; } = "";

		public string Name
		{
			get => _name;
			set
			{
				_name = value;
				UpdateContractInfo();
			}
		}

		private void UpdateContractInfo()
		{
			ApiSymbol = FutureInfo.GetApiSymbol(_name);
			DdeSymbol = FutureInfo.GetDdeSymbol(_name);
			TickSize = FutureInfo.GetTickSize(_name);
			TimeZone = FutureInfo.GetTimeZone(_name);

			// 如果找不到商品，使用預設值
			if (string.IsNullOrEmpty(ApiSymbol))
			{
				ApiSymbol = FutureInfo.GetApiSymbol("大型台指");
				DdeSymbol = FutureInfo.GetDdeSymbol("大型台指");
				TickSize = FutureInfo.GetTickSize("大型台指");
				TimeZone = FutureInfo.GetTimeZone("大型台指");
			}
		}

		public void Init(string ContractName)
		{
			Name = ContractName;  // 這會自動觸發 UpdateContractInfo()

			var time = DateTime.Now.AddMonths(1);
			Year = time.Year;
			Month = time.Month;
		}
	}
}