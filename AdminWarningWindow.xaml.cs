using System.Windows;

namespace BedrockServerManager;

public partial class AdminWarningWindow : Window
{
    public bool DontShowAgain { get; private set; }
    public bool RelaunchRequested { get; private set; }

    public AdminWarningWindow()
    {
        InitializeComponent();
        
        btnClose.Click += (s, e) => Close();
        
        btnContinue.Click += (s, e) => 
        {
            DontShowAgain = chkDontShowAgain.IsChecked ?? false;
            RelaunchRequested = false;
            Close();
        };
        
        btnRelaunch.Click += (s, e) => 
        {
            // If they relaunch as admin, we don't need to show this again
            DontShowAgain = true; 
            RelaunchRequested = true;
            Close();
        };
    }
}