using System.Windows;
using BedrockServerManager.Models;
using Forms = System.Windows.Forms;

namespace BedrockServerManager;

public partial class LocalBackupWindow : Window
{
    private readonly SharedState _state;
    private bool _schedSaved = false;

    public LocalBackupWindow(SharedState state)
    {
        _state = state;
        InitializeComponent();

        for (int i = 0; i < 24; i++) cbHour.Items.Add($"{i:D2}");
        for (int i = 0; i < 60; i++) cbMinute.Items.Add($"{i:D2}");

        txtLocalPath.Text = string.IsNullOrWhiteSpace(_state.LocalBackupPath) ? _state.BackupPath : _state.LocalBackupPath;

        var timeParts = _state.LocalBackupTime.Split(':');
        if (timeParts.Length == 2 && int.TryParse(timeParts[0], out int h) && int.TryParse(timeParts[1], out int m))
        {
            cbHour.SelectedItem = $"{h:D2}";
            cbMinute.SelectedItem = $"{m:D2}";
        }
        else
        {
            cbHour.SelectedIndex = 0;
            cbMinute.SelectedIndex = 0;
        }

        btnBrowse.Click += (s, e) =>
        {
            using var dlg = new Forms.FolderBrowserDialog
            {
                Description = "Select local backup destination",
                SelectedPath = string.IsNullOrWhiteSpace(txtLocalPath.Text) ? _state.RootPath : txtLocalPath.Text
            };
            if (dlg.ShowDialog() == Forms.DialogResult.OK)
            {
                txtLocalPath.Text = dlg.SelectedPath;
            }
        };

        btnSave.Click += BtnSave_Click;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_schedSaved)
        {
            DialogResult = true;
            return;
        }

        _state.LocalBackupPath = txtLocalPath.Text;
        _state.LocalBackupTime = $"{cbHour.Text}:{cbMinute.Text}";
        _state.NextLocalBackupDate = null;

        _schedSaved = true;
        btnSave.Content = "Apply & Close";
        ConfigPanel.IsEnabled = false;
    }
}