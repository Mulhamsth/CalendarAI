using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using GlobalHotKey;
using MessageBox = System.Windows.MessageBox;

namespace CalendarAI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly HttpClient _httpClient;
    private const string OllamaApiUrl = "http://localhost:11434/api/generate";
    private readonly HotKeyManager _hotKeyManager;

    public MainWindow()
    {
        InitializeComponent();
        _httpClient = new HttpClient();
        _hotKeyManager = new HotKeyManager();
        
        InitializeGlobalHotKey();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CenterWindow();
        // Hide window on startup
        this.WindowState = WindowState.Minimized;
        this.ShowInTaskbar = false;
        this.Hide();
    }

    private void CenterWindow()
    {
        // Calculate center position
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        this.Left = (screenWidth - this.ActualWidth) / 2;
        this.Top = (screenHeight - this.ActualHeight) / 2;
    }

    private void InitializeGlobalHotKey()
    {
        _hotKeyManager.Register(Key.Q, ModifierKeys.Control);
        _hotKeyManager.KeyPressed += (sender, args) => 
        {
            Application.Current.Dispatcher.Invoke(() => 
            {
                ShowWindow();
                ShowInputField();
            });
        };
    }

    private void ShowWindow()
    {
        CenterWindow();
        this.Show();
        this.WindowState = WindowState.Normal;
        this.ShowInTaskbar = true;
        this.Activate();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            this.Hide();
            this.ShowInTaskbar = false;
        }
        base.OnStateChanged(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        this.WindowState = WindowState.Minimized;
    }

    private void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
    {
        _hotKeyManager.Dispose();
        Application.Current.Shutdown();
    }

    private void ShowInputField()
    {
        InputTextBox.Visibility = Visibility.Visible;
        InputTextBox.Focus();
    }

    private async void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var description = InputTextBox.Text;
            if (string.IsNullOrWhiteSpace(description))
            {
                StatusText.Text = "Please enter a task description";
                return;
            }

            try
            {
                StatusText.Text = "Extracting date...";
                
                string GetSmartDateLine(int daysOffset)
                {
                    DateTime targetDate = DateTime.Today.AddDays(daysOffset);
                    DayOfWeek targetDay = targetDate.DayOfWeek;
                    string formattedDate = targetDate.ToString("dd.MM.yyyy");

                    if (daysOffset == 0)
                        return $"Today: {formattedDate}, {targetDay}.";
                    else if (daysOffset == -1)
                        return $"Yesterday: {formattedDate}, {targetDay}.";
                    else if (daysOffset == 1)
                        return $"Tomorrow: {formattedDate}, {targetDay}.";

                    // Week calculations (Monday as start of the week)
                    int todayIndex = ((int)DateTime.Today.DayOfWeek + 6) % 7;
                    DateTime startOfThisWeek = DateTime.Today.AddDays(-todayIndex);

                    DateTime startOfLastWeek = startOfThisWeek.AddDays(-7);
                    DateTime startOf2WeeksAgo = startOfThisWeek.AddDays(-14);
                    DateTime startOf3WeeksAgo = startOfThisWeek.AddDays(-21);

                    DateTime startOfNextWeek = startOfThisWeek.AddDays(7);
                    DateTime startOfWeek2 = startOfThisWeek.AddDays(14);
                    DateTime startOfWeek3 = startOfThisWeek.AddDays(21);
                    DateTime startOfWeek4 = startOfThisWeek.AddDays(28);

                    if (targetDate >= startOfThisWeek && targetDate < startOfNextWeek)
                    {
                        return $"{targetDay}: {formattedDate}, {targetDay}.";
                    }
                    else if (targetDate >= startOfNextWeek && targetDate < startOfWeek2)
                    {
                        return $"Next Week - {targetDay}: {formattedDate}, {targetDay}.";
                    }
                    else if (targetDate >= startOfWeek2 && targetDate < startOfWeek3)
                    {
                        return $"In 2 Weeks - {targetDay}: {formattedDate}, {targetDay}.";
                    }
                    else if (targetDate >= startOfWeek3 && targetDate < startOfWeek4)
                    {
                        return $"In 3 Weeks - {targetDay}: {formattedDate}, {targetDay}.";
                    }
                    else if (targetDate >= startOfLastWeek && targetDate < startOfThisWeek)
                    {
                        return $"Last Week - {targetDay}: {formattedDate}, {targetDay}.";
                    }
                    else if (targetDate >= startOf2WeeksAgo && targetDate < startOfLastWeek)
                    {
                        return $"2 Weeks Ago - {targetDay}: {formattedDate}, {targetDay}.";
                    }
                    else if (targetDate >= startOf3WeeksAgo && targetDate < startOf2WeeksAgo)
                    {
                        return $"3 Weeks Ago - {targetDay}: {formattedDate}, {targetDay}.";
                    }

                    // Fallback label
                    return $"{targetDate:dddd, dd.MM.yyyy}: {targetDay}.";
                }
                
                // First request: Extract date
                var datePrompt = 
                    $$"""
                       # Systemprompt
                       Dates for assistance:
                       ```
                       {{string.Join(Environment.NewLine, Enumerable.Range(-3, 21).Select(GetSmartDateLine))}}
                       ```
                       
                       Convert this task description(query) into a JSON.
                       {
                         "date": "date of the task provided by the user (IGNORE TIME, TAKE DATE)",
                       }     

                       Only provide a RFC8259 compliant JSON response following this format without deviation:
                       {
                         "date": "dd.MM.yyyy",
                       }
                       Nothing else, no formatting, nothing else. no \n no thing just the date in json RFC8259 compliant.
                       
                       Example:
                       Query: task at 

                       try to extract the date from the query. If no date is specified take today's date. 
                       
                       so "make an appointment with max on friday from 19 to 20" => you need to ignore 19 to 20 because it references the time 19:00 till 20:00 just take date information
                       
                       If there is not enough information to set a date, take today's date
                       """;
                
                var dateRequestBody = new
                {
                    system = datePrompt,
                    model = "mistral",
                    prompt = description,
                    stream = false,
                    format = new
                        {
                            type = "object",
                            properties = new
                            {
                                date = new { type = "string" }
                            },
                            required = new[] { "date" }
                        },
                    temperature = 0.0
                };

                var dateResponse = await _httpClient.PostAsync(
                    OllamaApiUrl,
                    new StringContent(JsonSerializer.Serialize(dateRequestBody), Encoding.UTF8, "application/json"));

                if (dateResponse.IsSuccessStatusCode)
                {
                    var dateResponseContent = await dateResponse.Content.ReadAsStringAsync();
                    var dateJsonResponse = JsonSerializer.Deserialize<JsonElement>(dateResponseContent);
                    var extractedDate = dateJsonResponse.GetProperty("response").GetString();
                    
                    //remove everything between <think> ...random text... </think>
                    string pattern = @"<think>.*?</think>";
                    // Replace the matched content with an empty string
                    extractedDate = Regex.Replace(extractedDate, pattern, string.Empty, RegexOptions.Singleline);
                    
                    MessageBox.Show(extractedDate, "Extracted Date");

                    StatusText.Text = "Extracting task details...";

                    // Second request: Extract full task details
                    var taskPrompt = $$$"""
                    # Systemprompt
                     today is {{{DateTime.Today.ToString("dd.MM.yyyy")}}}, {{{DateTime.Today.DayOfWeek.ToString()}}}.
                    
                    Convert this task description(query) into a JSON.
                    Only provide a RFC8259 compliant JSON response following this format without deviation:
                    {
                        "startTime": "the time when the task should starts.", (do not take dates into account)
                        "taskName" : "the name of the task you generate. Generate a Title that briefly names the Task. Do not include information already contained in date or startTime. Dont make anything up, only take infromation thats given",
                        "description": "the description of the task you generate based on the query. Describe the task based on the information given. Do not include information already contained in date or startTime. Dont make anything up, only take infromation thats given.",
                        "duration": "how long the task takes in hours"
                    }

                    format:
                    {
                        "startTime": "hh:mm",
                        "description": "description",
                        "duration": 0.75,
                        "taskName": "name"
                    }

                    ## strictly adhere to these rules:
                    The Default duration of a task is 1.
                    The Default value of startTime is null, if the user doesn't specify a time. (do not take dates into account)
                    The Default value of taskName is something you can generate based on the description
                    If there is not enough information within the prompt to make a meaningful task, just schedule a Task called what the query said today startime null.
                    
                    No extra formatting. No extra text besides the Json.
                    """;

                    var taskRequestBody = new
                    {
                        system = taskPrompt,
                        model = "llama3.2:3b-instruct-q8_0",
                        prompt = description,
                        stream = false,
                        format = new
                            {
                                type = "object",
                                properties = new
                                {
                                    startTime = new { type = "string" },
                                    taskName = new { type = "string" },
                                    description = new { type = "string" },
                                    duration = new { type = "number" } // use number for double/float values
                                },
                                required = new[] { "startTime", "taskName", "description", "duration" }
                            },
                    };

                    var taskResponse = await _httpClient.PostAsync(
                        OllamaApiUrl,
                        new StringContent(JsonSerializer.Serialize(taskRequestBody), Encoding.UTF8, "application/json"));

                    if (taskResponse.IsSuccessStatusCode)
                    {
                        var taskResponseContent = await taskResponse.Content.ReadAsStringAsync();
                        var taskJsonResponse = JsonSerializer.Deserialize<JsonElement>(taskResponseContent);
                        var generatedText = taskJsonResponse.GetProperty("response").GetString();
                        
                        StatusText.Text = "Task processed successfully!";
                        InputTextBox.Text = "";
                        InputTextBox.Visibility = Visibility.Collapsed;
                        this.WindowState = WindowState.Minimized;
                        
                        MessageBox.Show(generatedText, "Generated Task");
                    }
                    else
                    {
                        StatusText.Text = "Error processing task details";
                    }
                }
                else
                {
                    StatusText.Text = "Error extracting date";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }
        else if (e.Key == Key.Escape)
        {
            InputTextBox.Text = "";
            InputTextBox.Visibility = Visibility.Collapsed;
            StatusText.Text = "";
            this.WindowState = WindowState.Minimized;
        }
    }
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        _execute();
    }
}