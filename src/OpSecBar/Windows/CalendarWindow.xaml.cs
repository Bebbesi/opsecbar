using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OpSecBar.Core;

namespace OpSecBar.Windows;

/// <summary>Calendario che si apre dall'orologio: ora e data di adesso, mese sfogliabile, accesso alle notifiche di Windows.</summary>
public partial class CalendarWindow : FlyoutWindow
{
    private readonly Button[] _days = new Button[42];
    private readonly DispatcherTimer _timer;
    private DateTime _today, _month;
    private DateTime? _selected;

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    public CalendarWindow(App app) : base(app)
    {
        InitializeComponent();

        DayOfWeek first = Culture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            WeekdayRow.Children.Add(new TextBlock
            {
                Text = Culture.DateTimeFormat.ShortestDayNames[((int)first + i) % 7],
                FontSize = 11.5, TextAlignment = TextAlignment.Center
            }.Dim());
        }
        for (int i = 0; i < _days.Length; i++)
        {
            var day = new Button { Style = (Style)FindResource("DayButton") };
            day.Click += Day_Click;
            _days[i] = day;
            DayGrid.Children.Add(day);
        }

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateNow();
    }

    protected override void OnOpening()
    {
        _today = DateTime.Today;
        _month = FirstOfMonth(_today);
        _selected = null;
        UpdateNow();
        Render();
    }

    protected override void OnOpened() => _timer.Start();

    protected override void OnHidden() => _timer.Stop();

    private static DateTime FirstOfMonth(DateTime date) => new(date.Year, date.Month, 1);

    private static string Capitalized(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], Culture) + text[1..];

    // ---- Contenuto -------------------------------------------------------------------------------

    private void UpdateNow()
    {
        DateTime now = DateTime.Now;
        TimeText.Text = now.ToString(App.Settings.Clock24h ? "HH:mm:ss" : "h:mm:ss tt", Culture);
        DateText.Text = Capitalized(now.ToString("D", Culture));
        if (now.Date == _today) return;

        // È passata la mezzanotte con il calendario aperto.
        bool followed = _month == FirstOfMonth(_today);
        _today = now.Date;
        if (followed) _month = FirstOfMonth(_today);
        Render();
    }

    private void Render()
    {
        MonthText.Text = Capitalized(_month.ToString("Y", Culture));

        int offset = ((int)_month.DayOfWeek - (int)Culture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        DateTime start = _month.AddDays(-offset);
        for (int i = 0; i < _days.Length; i++)
        {
            DateTime date = start.AddDays(i);
            Button day = _days[i];
            day.Tag = date;
            day.Content = date.Day.ToString(Culture);
            day.Opacity = date.Month == _month.Month ? 1 : 0.4;

            bool today = date == _today, selected = date == _selected;
            if (today)
            {
                day.SetResourceReference(BackgroundProperty, "AccentBrush");
                day.SetResourceReference(ForegroundProperty, "AccentFgBrush");
            }
            else
            {
                day.ClearValue(BackgroundProperty);
                day.ClearValue(ForegroundProperty);
            }
            day.FontWeight = today ? FontWeights.SemiBold : FontWeights.Normal;
            if (selected && !today) day.SetResourceReference(BorderBrushProperty, "AccentBrush");
            else day.ClearValue(BorderBrushProperty);
        }

        DetailText.Text = Detail(_selected ?? _today);
    }

    /// <summary>Riga sotto il mese: settimana dell'anno per oggi, distanza da oggi per il giorno scelto.</summary>
    private string Detail(DateTime date)
    {
        string week = "Week " + ISOWeek.GetWeekOfYear(date);
        int days = (date - _today).Days;
        if (days == 0) return week;
        string distance = days switch
        {
            1 => "tomorrow",
            -1 => "yesterday",
            > 0 => $"in {days:N0} days",
            _ => $"{-days:N0} days ago"
        };
        return $"{Capitalized(date.ToString("D", Culture))} · {distance} · {week}";
    }

    // ---- Interazione -----------------------------------------------------------------------------

    private void ShowMonth(DateTime month)
    {
        if (month.Year < 1601 || month.Year > 9990) return;
        _month = FirstOfMonth(month);
        Render();
    }

    private void Day_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not DateTime date) return;
        _selected = date == _selected || date == _today ? null : date;
        if (date.Month != _month.Month || date.Year != _month.Year) ShowMonth(date);
        else Render();
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => ShowMonth(_month.AddMonths(-1));
    private void Next_Click(object sender, RoutedEventArgs e) => ShowMonth(_month.AddMonths(1));

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _selected = null;
        ShowMonth(_today);
    }

    private void DayGrid_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        ShowMonth(_month.AddMonths(e.Delta > 0 ? -1 : 1));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up or Key.Left or Key.PageUp:
                ShowMonth(_month.AddMonths(-1));
                e.Handled = true;
                break;
            case Key.Down or Key.Right or Key.PageDown:
                ShowMonth(_month.AddMonths(1));
                e.Handled = true;
                break;
            case Key.Home:
                Today_Click(this, e);
                e.Handled = true;
                break;
        }
        base.OnPreviewKeyDown(e);
    }

    private void Notifications_Click(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        TaskbarWindow.ShowWindowsNotifications();
    }

    private void DateSettings_Click(object sender, RoutedEventArgs e) => OpenShell("ms-settings:dateandtime");
}
