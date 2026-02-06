using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
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
			base.OnStartup(e);
			DebugBus.Initialize();
			_ = DebugBus.ConnectAsync();
		}
	}
}
