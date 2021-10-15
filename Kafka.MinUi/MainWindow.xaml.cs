using Confluent.Kafka;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace Kafka.MinUi
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _viewModel = new();
            _viewModel.AlertSent += ViewModel_AlertSent;
        }

        private void ViewModel_AlertSent(object sender, AlertEventArgs e)
        {
            MessageBox.Show(e.Message);
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.StartAsync();
        }


        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.StopAsync();
        }

        private async void CreateTopicButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.CreateTopicAsync();
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.SendAsync();
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            textBox.ScrollToEnd();
        }
    }

    public class AlertEventArgs : EventArgs
    {
        public string Message { get; private set; }

        public AlertEventArgs(string message)
        {
            Message = message;
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<AlertEventArgs> AlertSent;

        private readonly string _rootDir;

        private string _zookeeperOutput;
        private string _kafkaOutput;
        private bool _zookeeperError;
        private bool _isStartButtonEnabled = true;
        private bool _isStopButtonEnabled = true;
        private bool _isCreateTopicButtonEnabled = true;
        private bool _isSendButtonEnabled = true;

        private bool _kafkaError;

        public MainViewModel()
        {
            _rootDir = Directory.GetParent(Path.GetDirectoryName(typeof(MainViewModel).Assembly.Location)).FullName;
        }

        public string TopicName { get; set; }
        public string Content { get; set; }

        public bool IsStartButtonEnabled
        {
            get { return _isStartButtonEnabled; }
            set { _isStartButtonEnabled = value; OnPropertyChanged(); }
        }


        public bool IsStopButtonEnabled
        {
            get { return _isStopButtonEnabled; }
            set { _isStopButtonEnabled = value; OnPropertyChanged(); }
        }

        public bool IsCreateTopicButtonEnabled
        {
            get { return _isCreateTopicButtonEnabled; }
            set { _isCreateTopicButtonEnabled = value; OnPropertyChanged(); }
        }

        public bool IsSendButtonEnabled
        {
            get { return _isSendButtonEnabled; }
            set { _isSendButtonEnabled = value; OnPropertyChanged(); }
        }

        public string ZookeeperOutput
        {
            get { return _zookeeperOutput; }
            set { _zookeeperOutput = value; OnPropertyChanged(); }
        }

        public string KafkaOutput
        {
            get { return _kafkaOutput; }
            set { _kafkaOutput = value; OnPropertyChanged(); }
        }

        public async Task StartAsync()
        {
            IsStartButtonEnabled = false;
            IsStopButtonEnabled = false;
            _ = Task.Run(StartZookeeperAsync);
            await Task.Delay(5000);
            if (_zookeeperError)
            {
                return;
            }
            _ = Task.Run(StartKafkaAsync);
            if (_kafkaError)
            {
                return;
            }
            await Task.Delay(5000);
            IsStopButtonEnabled = true;
        }

        public async Task StopAsync()
        {
            IsStartButtonEnabled = false;
            IsStopButtonEnabled = false;

            _ = Task.Run(StopKafkaAsync);
            await Task.Delay(2000);

            _ = Task.Run(StopZookeeperAsync);
            await Task.Delay(2000);

            IsStartButtonEnabled = true;
            IsStopButtonEnabled = true;
        }

        public async Task StartZookeeperAsync()
        {
            _zookeeperError = false;
            var process = RunCommand("zookeeper-server-start", GetPropertiesPath("zookeeper"));

            while (!process.HasExited)
            {
                ZookeeperOutput += await process.StandardOutput.ReadLineAsync() + Environment.NewLine;
            }

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                ZookeeperOutput += error;
                _zookeeperError = true;
                SendAlert($"Error starting Zookeeper.{Environment.NewLine}{error}");
                IsStartButtonEnabled = true;
                IsStopButtonEnabled = true;
            }
        }

        public async Task StartKafkaAsync()
        {
            _kafkaError = false;
            var process = RunCommand("kafka-server-start", GetPropertiesPath("server"));

            while (!process.HasExited)
            {
                KafkaOutput += await process.StandardOutput.ReadLineAsync() + Environment.NewLine;
            }

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                KafkaOutput += error;
                _kafkaError = true;
                SendAlert($"Error starting Kafka.{Environment.NewLine}{error}");
                IsStartButtonEnabled = true;
                IsStopButtonEnabled = true;
            }
        }


        public async Task StopZookeeperAsync()
        {
            _zookeeperError = false;
            var process = RunCommand("zookeeper-server-stop", GetPropertiesPath("zookeeper"));

            while (!process.HasExited)
            {
                ZookeeperOutput += await process.StandardOutput.ReadLineAsync() + Environment.NewLine;
            }

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                ZookeeperOutput += error;
                _zookeeperError = true;
                SendAlert($"Error stopping Zookeeper.{Environment.NewLine}{error}");
                IsStartButtonEnabled = true;
                IsStopButtonEnabled = true;
            }
        }

        public async Task StopKafkaAsync()
        {
            _kafkaError = false;
            var process = RunCommand("kafka-server-stop", GetPropertiesPath("server"));

            while (!process.HasExited)
            {
                KafkaOutput += await process.StandardOutput.ReadLineAsync() + Environment.NewLine;
            }

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                KafkaOutput += error;
                _kafkaError = true;
                SendAlert($"Error stopping Kafka.{Environment.NewLine}{error}");
                IsStartButtonEnabled = true;
                IsStopButtonEnabled = true;
            }
        }



        public async Task CreateTopicAsync()
        {
            try
            {
                IsCreateTopicButtonEnabled = false;
                if (!ValidateTopicName())
                {
                    SendAlert("A valid topic name must be specified");
                    return;
                }
                var options = $"--create --topic {TopicName} --bootstrap-server localhost:9092 --if-not-exists";

                var process = RunCommand("kafka-topics", options);

                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    SendAlert($"Error creating topic.{Environment.NewLine}{error}");
                    return;
                }
                SendAlert($"Topic created");
            }
            finally
            {
                IsCreateTopicButtonEnabled = true;
            }
        }

        public async Task SendAsync()
        {
            try
            {
                IsSendButtonEnabled = false;

                if (!ValidateTopicName())
                {
                    SendAlert("A valid topic name must be specified");
                    return;
                }
                var config = new ProducerConfig
                {
                    BootstrapServers = "localhost:9092",
                };


                using var producer = new ProducerBuilder<Null, string>(config).Build();
                var cts = new CancellationTokenSource();
                cts.CancelAfter(5000);

                try
                {
                    var result = await producer.ProduceAsync(TopicName ?? "", new Message<Null, string> { Value = Content }, cts.Token);
                    if (result.Status != PersistenceStatus.NotPersisted)
                    {
                        SendAlert("Message sent");
                        return;
                    }
                }
                catch (TaskCanceledException)
                { }
                SendAlert("Error sending message");
            }
            finally
            {
                IsSendButtonEnabled = true;
            }
        }

        private bool ValidateTopicName()
        {
            return Regex.IsMatch(TopicName ?? "", @"^[a-zA-Z0-9\._\-]+$");
        }

        private string GetPropertiesPath(string propertiesFile) =>
            Path.Combine(_rootDir, $@"config\{propertiesFile}.properties");

        private Process RunCommand(string command, string args)
        {
            var scriptPath = Path.Combine(_rootDir, $@"bin\windows\{command}.bat");

            var info = new ProcessStartInfo("cmd.exe", $"/c {scriptPath} {args}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _rootDir
            };

            return Process.Start(info);
        }

        private void SendAlert(string message) =>
            AlertSent?.Invoke(this, new AlertEventArgs(message));

        #region INotifyPropertyChanged
        private void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));


        #endregion
    }
}
