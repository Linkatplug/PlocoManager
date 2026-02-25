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
            
            TxtVersion.Text = "Version 1.0.7 - Stable";
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
