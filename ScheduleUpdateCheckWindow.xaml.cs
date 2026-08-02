using System;
using System.Windows;
using System.Windows.Controls;
using BedrockServerManager.Models;
using BedrockServerManager.Services;

namespace BedrockServerManager;

public partial class ScheduleUpdateCheckWindow : Window
{
    private readonly SharedState _state;
    private bool _schedSaved = false;

    public ScheduleUpdateCheckWindow(SharedState state)
    {
        _state = state;
        InitializeComponent();

        for (int i = 0; i < 24; i++) cbHour.Items.Add($"{i:D2}");
        for (int i = 0; i < 60; i++) cbMinute.Items.Add($"{i:D2}");

        switch (_state.UpdateCheckFreq)
        {
            case "Daily": rbDaily.IsChecked = true; break;
            case "Weekly": rbWeekly.IsChecked = true; break;
            case "Biweekly": rbBiweekly.IsChecked = true; break;
            case "Monthly": rbMonthly.IsChecked = true; break;
        }

        var timeParts = _state.UpdateCheckTime.Split(':');
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
            if (cbDate.Items.Contains(_state.UpdateCheckDate)) cbDate.SelectedItem = _state.UpdateCheckDate;
            else cbDate.SelectedIndex = 0;
        }
        else if (rbMonthly.IsChecked == true)
        {
            cbDate.IsEnabled = true; lblDate.Content = "Day of Month:";
            for (int i = 1; i <= 31; i++) cbDate.Items.Add(i.ToString());
            cbDate.Items.Add("Last Day");
            if (cbDate.Items.Contains(_state.UpdateCheckDate)) cbDate.SelectedItem = _state.UpdateCheckDate;
            else cbDate.SelectedIndex = 0;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_schedSaved) 
        { 
            DialogResult = true; 
            return; 
        }

        _state.UpdateCheckFreq = rbDaily.IsChecked == true ? "Daily" :
                                    rbWeekly.IsChecked == true ? "Weekly" :
                                    rbBiweekly.IsChecked == true ? "Biweekly" : "Monthly";
        
        _state.UpdateCheckDate = cbDate.IsEnabled ? cbDate.SelectedItem.ToString() : "N/A";
        _state.UpdateCheckTime = $"{cbHour.Text}:{cbMinute.Text}";
        
        var nextDate = ScheduledRebootService.GetNextRebootDate(_state.UpdateCheckFreq, _state.UpdateCheckDate, _state.UpdateCheckTime);
        if (nextDate.HasValue) 
        {
            txtNextCheck.Text = " " + nextDate.Value.ToString("dddd, MMMM dd, yyyy 'at' hh:mm tt");
        }
        else 
        {
            txtNextCheck.Text = " Invalid settings";
        }
        
        _schedSaved = true;
        btnSave.Content = "Apply & Close";
        
        // Grey out the controls so they cannot be edited until the window is reopened
        ConfigPanel.IsEnabled = false;
    }
}