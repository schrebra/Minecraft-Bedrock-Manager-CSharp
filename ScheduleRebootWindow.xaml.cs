using System;
using System.Windows;
using System.Windows.Controls;
using BedrockServerManager.Models;
using BedrockServerManager.Services;

namespace BedrockServerManager;

public partial class ScheduleRebootWindow : Window
{
    private readonly SharedState _state;
    private bool _schedSaved = false;

    public ScheduleRebootWindow(SharedState state)
    {
        _state = state;
        InitializeComponent();

        for (int i = 0; i < 24; i++) cbHour.Items.Add($"{i:D2}");
        for (int i = 0; i < 60; i++) cbMinute.Items.Add($"{i:D2}");

        chkEnableRestart.IsChecked = _state.ScheduleRebootEnabled;
        
        switch (_state.ScheduleRebootFreq)
        {
            case "Daily": rbDaily.IsChecked = true; break;
            case "Weekly": rbWeekly.IsChecked = true; break;
            case "Biweekly": rbBiweekly.IsChecked = true; break;
            case "Monthly": rbMonthly.IsChecked = true; break;
        }

        var timeParts = _state.ScheduleRebootTime.Split(':');
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

        UpdateSchedDateUI();
        rbDaily.Checked += (_, _) => UpdateSchedDateUI();
        rbWeekly.Checked += (_, _) => UpdateSchedDateUI();
        rbBiweekly.Checked += (_, _) => UpdateSchedDateUI();
        rbMonthly.Checked += (_, _) => UpdateSchedDateUI();

        btnSave.Click += BtnSave_Click;
    }

    private void UpdateSchedDateUI()
    {
        cbDate.Items.Clear();
        if (rbDaily.IsChecked == true)
        {
            cbDate.IsEnabled = false; lblDate.Content = "Day:";
            cbDate.Items.Add("N/A"); cbDate.SelectedIndex = 0;
        }
        else if (rbWeekly.IsChecked == true || rbBiweekly.IsChecked == true)
        {
            cbDate.IsEnabled = true; lblDate.Content = "Day of Week:";
            foreach (var d in new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }) cbDate.Items.Add(d);
            if (cbDate.Items.Contains(_state.ScheduleRebootDate)) cbDate.SelectedItem = _state.ScheduleRebootDate;
            else cbDate.SelectedIndex = 0;
        }
        else if (rbMonthly.IsChecked == true)
        {
            cbDate.IsEnabled = true; lblDate.Content = "Day of Month:";
            for (int i = 1; i <= 31; i++) cbDate.Items.Add(i.ToString());
            cbDate.Items.Add("Last Day");
            if (cbDate.Items.Contains(_state.ScheduleRebootDate)) cbDate.SelectedItem = _state.ScheduleRebootDate;
            else cbDate.SelectedIndex = 0;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // On the second click, close the dialog and return true
        if (_schedSaved) 
        { 
            DialogResult = true; 
            return; 
        }

        // First click: Calculate the date and update the UI
        _state.ScheduleRebootEnabled = chkEnableRestart.IsChecked ?? false;
        _state.ScheduleRebootFreq = rbDaily.IsChecked == true ? "Daily" :
                                    rbWeekly.IsChecked == true ? "Weekly" :
                                    rbBiweekly.IsChecked == true ? "Biweekly" : "Monthly";
        
        _state.ScheduleRebootDate = cbDate.IsEnabled ? cbDate.SelectedItem.ToString() : "N/A";
        _state.ScheduleRebootTime = $"{cbHour.Text}:{cbMinute.Text}";
        
        var nextDate = ScheduledRebootService.GetNextRebootDate(_state.ScheduleRebootFreq, _state.ScheduleRebootDate, _state.ScheduleRebootTime);
        if (nextDate.HasValue) 
        {
            txtNextReboot.Text = " " + nextDate.Value.ToString("dddd, MMMM dd, yyyy 'at' hh:mm tt");
        }
        else 
        {
            txtNextReboot.Text = " Invalid settings";
        }
        
        _schedSaved = true;
        btnSave.Content = "Apply & Close";
    }
}
