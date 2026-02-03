using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace YoutubeCrawlerWPF
{
    public partial class MainWindow : Window
    {
        private CrawlerSystem? _crawler;
        private bool _isCrawling = false;
        private DispatcherTimer? _taskStatusTimer;

        public MainWindow()
        {
            InitializeComponent();

            // 创建WPF专用的工作目录
            var wpfDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoutubeCrawlerWPF");
            Directory.CreateDirectory(wpfDataDir);

            var dbPath = Path.Combine(wpfDataDir, "youtube_data.db");
            var exportPath = Path.Combine(wpfDataDir, "exports");
            Directory.CreateDirectory(exportPath);

            InitializeCrawler(dbPath, exportPath);
            UpdateTaskCount();

            // 启动定时任务状态检查
            StartTaskStatusMonitoring();
        }

        private void InitializeCrawler(string dbPath, string exportPath)
        {
            try
            {
                _crawler = new CrawlerSystem(dbPath, exportPath);
                AppendLog($"✅ 数据分析系统初始化成功");
                AppendLog($"📁 数据目录: {Path.GetDirectoryName(dbPath)}");
                AppendLog($"📤 导出目录: {exportPath}");
                AppendLog($"📢 自媒体全家桶用户群：1076150045");
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 初始化失败: {ex.Message}");
                MessageBox.Show($"初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                txtLog.AppendText($"[{timestamp}] {message}\n");
                txtLog.ScrollToEnd();
                scrollViewer.ScrollToEnd();

                // 限制日志行数，避免内存占用过大
                var lines = txtLog.Text.Split('\n');
                if (lines.Length > 1000)
                {
                    txtLog.Text = string.Join('\n', lines.Skip(lines.Length - 1000));
                }
            });
        }

        private void StartTaskStatusMonitoring()
        {
            _taskStatusTimer = new DispatcherTimer();
            _taskStatusTimer.Interval = TimeSpan.FromSeconds(5); // 每5秒更新一次
            _taskStatusTimer.Tick += async (sender, e) => await UpdateTaskStatusLog();
            _taskStatusTimer.Start();
        }

        private async Task UpdateTaskStatusLog()
        {
            if (_crawler == null) return;

            try
            {
                var tasks = await _crawler.GetTaskStatus();

                foreach (var task in tasks)
                {
                    if (task.Status == "running")
                    {
                        // 只记录运行中的任务，避免日志过多
                        AppendLog($"🔄 任务运行中: {task.TaskType} - 进度: {task.ProcessedItems}/{task.TotalItems} ({task.ProgressPercent:F1}%)");
                    }
                }

                // 更新任务计数
                Dispatcher.Invoke(() =>
                {
                    txtTaskCount.Text = tasks.Count.ToString();

                    var runningTasks = tasks.Count(task => task.Status == "running");
                    if (runningTasks > 0)
                    {
                        statusText.Text = $"正在运行 {runningTasks} 个任务...";
                    }
                    else
                    {
                        statusText.Text = "就绪";
                    }
                });
            }
            catch
            {
                // 静默处理错误，避免日志过多
            }
        }

        private void UpdateTaskCount()
        {
            if (_crawler == null) return;

            Task.Run(async () =>
            {
                try
                {
                    var tasks = await _crawler.GetTaskStatus();
                    Dispatcher.Invoke(() =>
                    {
                        txtTaskCount.Text = tasks.Count.ToString();
                    });
                }
                catch (Exception ex)
                {
                    AppendLog($"❌ 更新任务计数失败: {ex.Message}");
                }
            });
        }

        private async void StartCrawl_Click(object sender, RoutedEventArgs e)
        {
            if (_isCrawling)
            {
                MessageBox.Show("已有任务正在运行，请等待完成", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_crawler == null)
            {
                MessageBox.Show("数据分析系统未初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var keyword = txtKeyword.Text.Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                MessageBox.Show("请输入关键词、频道ID或播放列表ID", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtMaxItems.Text, out var maxItems) || maxItems <= 0)
            {
                MessageBox.Show("最大数量必须是正整数", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isCrawling = true;
            statusText.Text = "正在采集...";

            try
            {
                AppendLog($"🚀 开始采集: {keyword}");
                var taskId = await _crawler.StartCrawlTask("keyword", keyword, maxItems);
                AppendLog($"✅ 任务已启动，ID: {taskId}");
                UpdateTaskCount();

                // 等待一段时间后更新状态
                await Task.Delay(3000);
                await ShowTaskStatus();
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 采集失败: {ex.Message}");
                MessageBox.Show($"采集失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isCrawling = false;
                statusText.Text = "就绪";
            }
        }

        private void KeywordCrawl_Click(object sender, RoutedEventArgs e)
        {
            txtKeyword.Text = "";
            txtKeyword.Focus();
            AppendLog("📝 已选择关键词搜索模式");
        }

        private void ChannelCrawl_Click(object sender, RoutedEventArgs e)
        {
            txtKeyword.Text = "";
            txtKeyword.Focus();
            AppendLog("📝 已选择频道采集模式");
        }

        private void PlaylistCrawl_Click(object sender, RoutedEventArgs e)
        {
            txtKeyword.Text = "";
            txtKeyword.Focus();
            AppendLog("📝 已选择播放列表采集模式");
        }

        private async void UrlListCrawl_Click(object sender, RoutedEventArgs e)
        {
            if (_isCrawling)
            {
                MessageBox.Show("已有任务正在运行", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_crawler == null)
            {
                MessageBox.Show("数据分析系统未初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var urlFile = txtUrlFile.Text.Trim();
            if (string.IsNullOrEmpty(urlFile) || urlFile == "未选择文件")
            {
                MessageBox.Show("请先选择URL文件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(urlFile))
            {
                MessageBox.Show($"URL文件不存在: {urlFile}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var urls = await ReadUrlsFromFile(urlFile);
            if (urls.Count == 0)
            {
                MessageBox.Show("URL文件中没有有效的URL", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isCrawling = true;
            statusText.Text = "正在采集URL列表...";

            try
            {
                AppendLog($"🚀 开始采集 {urls.Count} 个URL");
                var taskId = await _crawler.StartUrlListCrawl(urls, 5);
                AppendLog($"✅ URL列表任务已启动，ID: {taskId}");
                UpdateTaskCount();

                await Task.Delay(3000);
                await ShowTaskStatus();
            }
            catch (Exception ex)
            {
                AppendLog($"❌ URL列表采集失败: {ex.Message}");
                MessageBox.Show($"采集失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isCrawling = false;
                statusText.Text = "就绪";
            }
        }

        private void DiscoveryCrawl_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("智能发现功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void ExportToExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_crawler == null) return;

            try
            {
                AppendLog("📤 正在导出到Excel...");
                var filePath = await _crawler.ExportToExcel("all");
                AppendLog($"✅ 数据导出成功");
                AppendLog($"📁 文件路径: {filePath}");

                var result = MessageBox.Show(
                    $"数据已成功导出到:\n{filePath}\n\n是否打开文件所在文件夹?",
                    "导出成功",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    // 打开文件所在文件夹
                    var folderPath = System.IO.Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", folderPath);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 导出失败: {ex.Message}");
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ShowTaskStatus_Click(object sender, RoutedEventArgs e)
        {
            await ShowTaskStatus();
        }

        private async Task ShowTaskStatus()
        {
            if (_crawler == null) return;

            try
            {
                var tasks = await _crawler.GetTaskStatus();
                AppendLog($"\n📊 任务状态查询 - 共 {tasks.Count} 个任务:\n");

                foreach (var task in tasks)
                {
                    AppendLog($"任务ID: {task.TaskId}");
                    AppendLog($"  类型: {task.TaskType}");
                    AppendLog($"  目标: {(task.TargetId.Length > 50 ? task.TargetId.Substring(0, 50) + "..." : task.TargetId)}");
                    AppendLog($"  状态: {task.Status}");
                    AppendLog($"  进度: {task.ProcessedItems}/{task.TotalItems} ({task.ProgressPercent:F1}%)");
                    AppendLog($"  耗时: {task.Duration}");
                    AppendLog($"  开始时间: {task.StartTime:yyyy-MM-dd HH:mm:ss}");
                    if (task.EndTime.HasValue)
                    {
                        AppendLog($"  结束时间: {task.EndTime.Value:yyyy-MM-dd HH:mm:ss}");
                    }
                    if (!string.IsNullOrEmpty(task.ErrorMessage))
                    {
                        AppendLog($"  错误: {task.ErrorMessage}");
                    }
                    AppendLog(new string('-', 60));
                }
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 查询任务状态失败: {ex.Message}");
            }
        }

        private void ClearDatabase_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定要清空所有数据吗？此操作不可恢复！", "警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete("youtube_data.db");
                    AppendLog("🗑️ 数据库已清空");
                    MessageBox.Show("数据库已清空，请重启应用程序", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    AppendLog($"❌ 清空数据库失败: {ex.Message}");
                    MessageBox.Show($"清空数据库失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task<List<string>> ReadUrlsFromFile(string filePath)
        {
            var urls = new List<string>();
            try
            {
                var lines = await File.ReadAllLinesAsync(filePath);
                urls = lines
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("http") || line.StartsWith("www"))
                    .ToList();
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 读取URL文件失败: {ex.Message}");
            }
            return urls;
        }

        private void SelectUrlFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                Title = "选择URL文件"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                txtUrlFile.Text = openFileDialog.FileName;
                AppendLog($"📁 已选择URL文件: {Path.GetFileName(openFileDialog.FileName)}");
            }
        }

        private void ViewDatabaseData_Click(object sender, RoutedEventArgs e)
        {
            if (_crawler == null)
            {
                MessageBox.Show("数据分析系统未初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var viewerWindow = new DataViewerWindow(_crawler);
                viewerWindow.Show();
                AppendLog("📊 已打开数据查看窗口");
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 打开数据查看窗口失败: {ex.Message}");
                MessageBox.Show($"打开失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // TextBox 占位符处理
        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.Text == textBox.Tag?.ToString())
            {
                textBox.Text = "";
                textBox.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && string.IsNullOrWhiteSpace(textBox.Text))
            {
                textBox.Text = textBox.Tag?.ToString() ?? "";
                textBox.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }
    }
}
