using System;
using System.Windows;

namespace BASpark
{
    public partial class PrivacyWindow : Window
    {
        public PrivacyWindow() 
        { 
            InitializeComponent(); 
            LoadVersion();
        }

        private void LoadVersion()
        {
            try
            {
                string version = AppVersionInfo.DisplayVersion;
                if (!string.IsNullOrWhiteSpace(version))
                {
                    VersionText.Text = version;
                }
            }
            catch
            {
                VersionText.Text = Localization.Get("Privacy_VersionFailed");
            }
        }

        private void BtnAgree_Click(object sender, RoutedEventArgs e)
        {
            ConfigManager.Save("AgreedToPrivacy", true);
            ConfigManager.Save("EnableTelemetry", CheckTelemetry.IsChecked ?? false);
            
            this.DialogResult = true;
            this.Close();
        }

        private void BtnRefuse_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
