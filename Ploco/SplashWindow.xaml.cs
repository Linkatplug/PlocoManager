using System;
using System.Reflection;
using System.Windows;

namespace Ploco
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            
            // Récupère dynamiquement la version de l'assemblage
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            TxtVersion.Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}";
        }

        public void UpdateProgress(int percentage, string statusMessage)
        {
            Dispatcher.Invoke(() =>
            {
                PbLoading.Value = percentage;
                TxtStatus.Text = statusMessage;
            });
        }
    }
}
