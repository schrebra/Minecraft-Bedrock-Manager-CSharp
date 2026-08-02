using System.Windows;

namespace BedrockServerManager;

public partial class RestoreConfirmWindow : Window
{
    public RestoreConfirmWindow(string zipFilePath)
    {
        InitializeComponent();
        
        // Set the zip file path in the text block
        txtZipPath.Text = zipFilePath;

        btnClose.Click += (s, e) => 
        {
            DialogResult = false;
            Close();
        };

        btnCancel.Click += (s, e) => 
        {
            DialogResult = false;
            Close();
        };

        btnConfirm.Click += (s, e) => 
        {
            DialogResult = true;
            Close();
        };
    }
}