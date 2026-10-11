using System;
using System.Net;
using System.Windows;

namespace MyTranslator
{
    public static class Program
    {
        [STAThread]
        public static int Main()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            try
            {
                var app = new Application();
                using (var client = new TranslationClient())
                {
                    var main = new MainWindow(new SettingsStore(), client);
                    app.Run(main.View);
                }
                return 0;
            }
            catch (Exception)
            {
                MessageBox.Show("应用无法启动。请确认系统已安装 .NET Framework 4.8，并尝试重新解压或编译软件。", "译知", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
