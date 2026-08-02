using System.Windows;

namespace BedrockServerManager;

public partial class AdminWarningWindow : Window
{
    public bool DontShowAgain { get; private set; }
    public bool ShowDontShowAgain { get; set; } = false;

    public AdminWarningWindow()
    {
        InitializeComponent();
        btnClose.Click += (s, e) => Close();
        btnOk.Click += (s, e) => 
        {
            DontShowAgain = chkDontShowAgain.IsChecked ?? false;
            Close();
        };
        
        Loaded += AdminWarningWindow_Loaded;
    }

    private void AdminWarningWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Only show the checkbox if ShowDontShowAgain is true
        chkDontShowAgain.Visibility = ShowDontShowAgain ? Visibility.Visible : Visibility.Collapsed;
    }
}