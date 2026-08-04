using System.Windows;
using BedrockServerManager.Models;
using Forms = System.Windows.Forms;

namespace BedrockServerManager;

public partial class OffsiteBackupWindow : Window
{
    private readonly SharedState _state;
    private bool _schedSaved = false;

    public OffsiteBackupWindow(SharedState state)
    {
        _state = state;
        InitializeComponent();

        for (int i = 0; i < 24; i++) cbHour.Items.Add($"{i:D2}");
        for (int i = 0; i < 60; i++) cbMinute.Items.Add($"{i:D2}");

        txtOffsitePath.Text = _state.OffsiteBackupPath;

        var timeParts = _state.OffsiteBackupTime.Split(':');
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
                Description = "Select offsite backup destination (e.g. \\\\NAS\\Backups)",
                SelectedPath = string.IsNullOrWhiteSpace(txtOffsitePath.Text) ? @"C:\" : txtOffsitePath.Text
            };
            if (dlg.ShowDialog() == Forms.DialogResult.OK)
            {
                txtOffsitePath.Text = dlg.SelectedPath;
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

        _state.OffsiteBackupPath = txtOffsitePath.Text;
        _state.OffsiteBackupTime = $"{cbHour.Text}:{cbMinute.Text}";
        _state.NextOffsiteBackupDate = null;

        _schedSaved = true;
        btnSave.Content = "Apply & Close";
        ConfigPanel.IsEnabled = false;
    }
}