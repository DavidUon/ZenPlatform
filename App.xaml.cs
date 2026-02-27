using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using TaifexHisDbManager;
using ZenPlatform.Debug;

namespace ZenPlatform
{
	/// <summary>
	/// App.xaml 的互動邏輯
	/// </summary>
	public partial class App : Application
	{
		protected override void OnStartup(StartupEventArgs e)
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			try
			{
				MagistockStoragePaths.EnsureInitialized();
			}
			catch
			{
				// 固定路徑初始化採 best-effort，失敗時由後續流程自行處理。
			}
			base.OnStartup(e);
			DebugBus.Initialize();
			_ = DebugBus.ConnectAsync();
		}
	}
}
