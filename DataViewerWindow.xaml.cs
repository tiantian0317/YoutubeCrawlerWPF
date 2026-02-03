using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace YoutubeCrawlerWPF
{
    public partial class DataViewerWindow : Window
    {
        private readonly CrawlerSystem _crawler;

        public DataViewerWindow(CrawlerSystem crawler)
        {
            InitializeComponent();
            _crawler = crawler;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (cbTableSelect.SelectedIndex == -1)
            {
                cbTableSelect.SelectedIndex = 0;
            }
        }

        private async void cbTableSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbTableSelect.SelectedIndex < 0) return;

            statusText.Text = "正在加载数据...";
            await LoadData();
            statusText.Text = "就绪";
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private async Task LoadData()
        {
            try
            {
                var selectedIndex = cbTableSelect.SelectedIndex;
                DataTable dataTable = new DataTable();

                switch (selectedIndex)
                {
                    case 0: // 任务表
                        dataTable = await GetTasksTable();
                        break;
                    case 1: // 频道表
                        dataTable = await GetChannelsTable();
                        break;
                    case 2: // 视频表
                        dataTable = await GetVideosTable();
                        break;
                    case 3: // 播放列表表
                        dataTable = await GetPlaylistsTable();
                        break;
                }

                // 设置数据源
                dgData.ItemsSource = dataTable.DefaultView;
                
                // 自动生成列并设置中文标题
                dgData.AutoGenerateColumns = true;
                dgData.Loaded += (s, e) => 
                {
                    foreach (var column in dgData.Columns)
                    {
                        if (column is DataGridTextColumn textColumn)
                        {
                            var header = textColumn.Header?.ToString() ?? "";
                            textColumn.Header = GetChineseColumnName(header);
                            
                            // 设置列宽
                            if (header.Contains("Id") || header.Contains("ID"))
                            {
                                textColumn.Width = new DataGridLength(150);
                            }
                            else if (header.Contains("Description") || header.Contains("Title"))
                            {
                                textColumn.Width = new DataGridLength(300);
                            }
                            else
                            {
                                textColumn.Width = DataGridLength.Auto;
                            }
                        }
                    }
                };

                txtRecordCount.Text = $"记录数: {dataTable.Rows.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<DataTable> GetTasksTable()
        {
            var dataTable = new DataTable();
            var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + GetDatabasePath());

            await connection.OpenAsync();
            var sql = "SELECT * FROM CrawlTasks ORDER BY CreatedAt DESC LIMIT 500";
            using var command = new Microsoft.Data.Sqlite.SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();

            // 创建列
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                dataTable.Columns.Add(columnName);
            }

            // 添加数据
            while (await reader.ReadAsync())
            {
                var row = dataTable.NewRow();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    if (reader.IsDBNull(i))
                    {
                        row[i] = "";
                    }
                    else if (value is string strValue && strValue.Length > 100)
                    {
                        row[i] = strValue.Substring(0, 100) + "...";
                    }
                    else
                    {
                        row[i] = value;
                    }
                }
                dataTable.Rows.Add(row);
            }

            return dataTable;
        }

        private async Task<DataTable> GetChannelsTable()
        {
            var dataTable = new DataTable();
            var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + GetDatabasePath());

            await connection.OpenAsync();
            var sql = @"
                SELECT 
                    ChannelId as '频道ID',
                    Title as '频道名称',
                    Description as '描述',
                    SubscriberCount as '订阅数',
                    VideoCount as '视频数',
                    ViewCount as '观看数',
                    IsVerified as '已认证',
                    Country as '国家',
                    CustomUrl as '自定义URL',
                    PublishedAt as '发布时间',
                    LastCrawled as '最后采集',
                    CreatedAt as '创建时间'
                FROM Channels 
                ORDER BY LastCrawled DESC 
                LIMIT 500";
            
            using var command = new Microsoft.Data.Sqlite.SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();

            for (int i = 0; i < reader.FieldCount; i++)
            {
                dataTable.Columns.Add(reader.GetName(i));
            }

            while (await reader.ReadAsync())
            {
                var row = dataTable.NewRow();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    row[i] = reader.IsDBNull(i) ? "" : value;
                }
                dataTable.Rows.Add(row);
            }

            return dataTable;
        }

        private async Task<DataTable> GetVideosTable()
        {
            var dataTable = new DataTable();
            var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + GetDatabasePath());

            await connection.OpenAsync();
            var sql = @"
                SELECT 
                    v.VideoId as '视频ID',
                    c.Title as '频道名称',
                    v.Title as '标题',
                    v.Duration as '时长(秒)',
                    v.UploadDate as '上传日期',
                    v.ViewCount as '观看次数',
                    v.LikeCount as '点赞数',
                    v.DislikeCount as '点踩数',
                    v.CommentCount as '评论数',
                    v.Category as '分类',
                    v.PrivacyStatus as '隐私状态',
                    v.IsLiveContent as '直播内容',
                    v.IsAgeRestricted as '年龄限制',
                    v.Keywords as '关键词',
                    v.LastCrawled as '最后采集'
                FROM Videos v
                LEFT JOIN Channels c ON v.ChannelId = c.ChannelId
                ORDER BY v.UploadDate DESC 
                LIMIT 500";
            
            using var command = new Microsoft.Data.Sqlite.SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();

            for (int i = 0; i < reader.FieldCount; i++)
            {
                dataTable.Columns.Add(reader.GetName(i));
            }

            while (await reader.ReadAsync())
            {
                var row = dataTable.NewRow();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    if (reader.IsDBNull(i))
                    {
                        row[i] = "";
                    }
                    else if (value is string strValue && strValue.Length > 200)
                    {
                        row[i] = strValue.Substring(0, 200) + "...";
                    }
                    else
                    {
                        row[i] = value;
                    }
                }
                dataTable.Rows.Add(row);
            }

            return dataTable;
        }

        private async Task<DataTable> GetPlaylistsTable()
        {
            var dataTable = new DataTable();
            var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + GetDatabasePath());

            await connection.OpenAsync();
            var sql = @"
                SELECT 
                    p.PlaylistId as '播放列表ID',
                    c.Title as '频道名称',
                    p.Title as '标题',
                    p.Description as '描述',
                    p.VideoCount as '视频数',
                    p.PrivacyStatus as '隐私状态',
                    p.CreatedAt as '创建时间'
                FROM Playlists p
                LEFT JOIN Channels c ON p.ChannelId = c.ChannelId
                ORDER BY p.CreatedAt DESC 
                LIMIT 500";
            
            using var command = new Microsoft.Data.Sqlite.SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();

            for (int i = 0; i < reader.FieldCount; i++)
            {
                dataTable.Columns.Add(reader.GetName(i));
            }

            while (await reader.ReadAsync())
            {
                var row = dataTable.NewRow();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    row[i] = reader.IsDBNull(i) ? "" : value;
                }
                dataTable.Rows.Add(row);
            }

            return dataTable;
        }

        private string GetChineseColumnName(string englishName)
        {
            var mappings = new Dictionary<string, string>
            {
                ["TaskId"] = "任务ID",
                ["TaskType"] = "任务类型",
                ["TargetId"] = "目标ID",
                ["Status"] = "状态",
                ["TotalItems"] = "总项目数",
                ["ProcessedItems"] = "已处理数",
                ["StartTime"] = "开始时间",
                ["EndTime"] = "结束时间",
                ["LastProcessedId"] = "最后处理ID",
                ["ErrorMessage"] = "错误信息",
                ["CreatedAt"] = "创建时间",
                ["ChannelId"] = "频道ID",
                ["Title"] = "标题",
                ["Description"] = "描述",
                ["ThumbnailUrl"] = "缩略图URL",
                ["SubscriberCount"] = "订阅数",
                ["VideoCount"] = "视频数",
                ["ViewCount"] = "观看数",
                ["IsVerified"] = "已认证",
                ["Country"] = "国家",
                ["CustomUrl"] = "自定义URL",
                ["PublishedAt"] = "发布时间",
                ["LastCrawled"] = "最后采集",
                ["VideoId"] = "视频ID",
                ["ChannelId"] = "频道ID",
                ["Duration"] = "时长(秒)",
                ["UploadDate"] = "上传日期",
                ["LikeCount"] = "点赞数",
                ["DislikeCount"] = "点踩数",
                ["CommentCount"] = "评论数",
                ["Category"] = "分类",
                ["License"] = "许可证",
                ["PrivacyStatus"] = "隐私状态",
                ["Tags"] = "标签",
                ["Keywords"] = "关键词",
                ["IsLiveContent"] = "直播内容",
                ["IsAgeRestricted"] = "年龄限制",
                ["PlaylistId"] = "播放列表ID"
            };

            return mappings.TryGetValue(englishName, out var chineseName) ? chineseName : englishName;
        }

        private string GetDatabasePath()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YoutubeCrawlerWPF",
                "youtube_data.db");
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
