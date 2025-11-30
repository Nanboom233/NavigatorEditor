// NavigatorEditor/ViewModels/MainWindowViewModel.cs

using System;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NavigatorEditor.GrpcClient; // add

namespace NavigatorEditor.ViewModels
{

    public partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
        private bool _status = false;
        
        public string StatusText => Status ? "已连接" : "未连接";
        
        public string ConnectButtonText => Status ? "断开连接" : "连接";
        
        [ObservableProperty] private string _address = "http://127.0.0.1:50051";
        
        [ObservableProperty] private string _commandInput = "";

        [ObservableProperty] private bool _isBusy = false;
        
        public ObservableCollection<string> Log { get; } = new();
        
        private GrpcClient.GrpcClient? _client;

        [RelayCommand(CanExecute = nameof(Status))]
        public async Task SwitchConnection()
        {
            IsBusy = true;
            if (Status)
            {
                await SafeDisposeClientAsync();
                AppendLog("已断开连接");
                Status = false;
                IsBusy = false;
                return;
            }
            try
            {
                if (!Uri.TryCreate(Address, UriKind.Absolute, out var uri))
                    throw new ArgumentException("地址格式不正确");

                _client = await GrpcClient.GrpcClient.CreateClient(uri);
                
                // hook events
                _client.OnLog += (message, ts) =>
                {
                    var serverTime = DateTimeOffset.FromUnixTimeMilliseconds(ts).ToLocalTime().ToString("HH:mm:ss");
                    AppendLog($"[server time {serverTime}] {message}");
                };
                _client.OnDisconnected += async reason =>
                {
                    AppendLog($"连接丢失：{reason}");
                    await SafeDisposeClientAsync();
                    Status = false;
                    return;
                };
                
                long beginTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long endTime = (await _client.Heartbeat()).Timestamp;
                AppendLog($"已连接到服务: {Address}, 延迟 {endTime - beginTime} ms");
                Status = true;
            }
            catch (Exception ex)
            {
                AppendLog($"连接失败：{ex.Message}");
                await SafeDisposeClientAsync();
            }
            IsBusy = false;
        }

        [RelayCommand(CanExecute = nameof(Status))]
        public async Task SendCommand()
        {
            if (!Status || _client is null) return;
            var cmd = CommandInput?.Trim();
            if (string.IsNullOrEmpty(cmd))
            {
                return;
            }
            AppendLog($"发送命令：{cmd}");
            try
            {
                var resp = await _client.Command(cmd);
                AppendLog($"返回：success={resp.Success}, message='{resp.Message}', ts={resp.Timestamp}");
            }
            catch (Exception ex)
            {
                AppendLog($"发送失败：{ex.Message}");
            }
        }



        private void AppendLog(string line)
        {
            var timeString = DateTime.Now.ToString("HH:mm:ss");
            var logLine = $"[{timeString}] {line}";
            Log.Add(logLine);

            _logBuffer.AppendLine(logLine);
            LogText = _logBuffer.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            await SafeDisposeClientAsync();
        }

        private async Task SafeDisposeClientAsync()
        {
            if (_client is not null)
            {
                try
                {
                    await _client.DisposeAsync();
                }
                catch
                {
                    // ignored
                }

                _client = null;
            }
        }
        
        private readonly System.Text.StringBuilder _logBuffer = new();
        [ObservableProperty] private string _logText = "";
    }
}
