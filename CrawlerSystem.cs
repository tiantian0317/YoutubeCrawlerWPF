using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using OfficeOpenXml;
using YoutubeExplode;
using YoutubeExplode.Channels;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeExplode.Search;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace YoutubeCrawlerWPF
{
    public class CrawlerSystem : IDisposable
    {
        private readonly YoutubeClient _youtubeClient;
        private readonly string _connectionString;
        private readonly string _excelExportPath;
        
        public CrawlerSystem(string databasePath, string? excelExportFolder = null)
        {
            _youtubeClient = new YoutubeClient();
            _connectionString = $"Data Source={databasePath}";
            var dbDir = Path.GetDirectoryName(databasePath) ?? Directory.GetCurrentDirectory();
            _excelExportPath = excelExportFolder ?? Path.Combine(dbDir, "exports");
            
            InitializeDatabase();
            Directory.CreateDirectory(_excelExportPath);
        }
        
        private void InitializeDatabase()
        {
            // 查找 DatabaseSchema.sql 文件
            string schemaFilePath = FindDatabaseSchemaFile();

            if (!File.Exists(schemaFilePath))
            {
                throw new FileNotFoundException($"无法找到数据库架构文件: {schemaFilePath}");
            }

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // 执行数据库初始化脚本
            var schemaScript = File.ReadAllText(schemaFilePath);
            using var command = new SqliteCommand(schemaScript, connection);
            command.ExecuteNonQuery();
        }

        private string FindDatabaseSchemaFile()
        {
            // 首先检查当前工作目录
            string currentPath = Path.Combine(Directory.GetCurrentDirectory(), "DatabaseSchema.sql");
            if (File.Exists(currentPath))
                return currentPath;

            // 检查程序所在目录
            string appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DatabaseSchema.sql");
            if (File.Exists(appPath))
                return appPath;

            // 检查上一级目录（用于开发环境）
            string parentPath = Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.FullName ?? "", "DatabaseSchema.sql");
            if (File.Exists(parentPath))
                return parentPath;

            // 检查项目根目录
            string rootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "DatabaseSchema.sql");
            if (File.Exists(rootPath))
                return Path.GetFullPath(rootPath);

            // 如果都找不到，返回当前目录的路径
            return currentPath;
        }
        
        // 数据采集任务管理
        public async Task<string> StartCrawlTask(string taskType, string targetId, int maxItems = 100)
        {
            var taskId = Guid.NewGuid().ToString();
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var insertSql = @"
                INSERT INTO CrawlTasks (TaskId, TaskType, TargetId, Status, TotalItems, StartTime)
                VALUES (@TaskId, @TaskType, @TargetId, 'running', @MaxItems, CURRENT_TIMESTAMP)";
                
            using var command = new SqliteCommand(insertSql, connection);
            command.Parameters.AddWithValue("@TaskId", taskId);
            command.Parameters.AddWithValue("@TaskType", taskType);
            command.Parameters.AddWithValue("@TargetId", targetId);
            command.Parameters.AddWithValue("@MaxItems", maxItems);
            
            await command.ExecuteNonQueryAsync();
            
            // 启动后台任务
            _ = Task.Run(async () => await ExecuteCrawlTask(taskId, taskType, targetId, maxItems));
            
            return taskId;
        }

        // 从URL列表采集
        public async Task<string> StartUrlListCrawl(List<string> urls, int maxItemsPerUrl = 50)
        {
            var taskId = Guid.NewGuid().ToString();
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var insertSql = @"
                INSERT INTO CrawlTasks (TaskId, TaskType, TargetId, Status, TotalItems, StartTime)
                VALUES (@TaskId, 'url_list', @TargetId, 'running', @MaxItems, CURRENT_TIMESTAMP)";
                
            using var command = new SqliteCommand(insertSql, connection);
            command.Parameters.AddWithValue("@TaskId", taskId);
            command.Parameters.AddWithValue("@TargetId", string.Join(",", urls));
            command.Parameters.AddWithValue("@MaxItems", urls.Count * maxItemsPerUrl);
            
            await command.ExecuteNonQueryAsync();
            
            // 启动后台任务
            _ = Task.Run(async () => await ExecuteUrlListCrawl(taskId, urls, maxItemsPerUrl));
            
            return taskId;
        }
        
        private async Task ExecuteCrawlTask(string taskId, string taskType, string targetId, int maxItems)
        {
            try
            {
                switch (taskType.ToLower())
                {
                    case "keyword":
                        await CrawlByKeyword(taskId, targetId, maxItems);
                        break;
                    case "channel":
                        await CrawlChannel(taskId, targetId, maxItems);
                        break;
                    case "playlist":
                        await CrawlPlaylist(taskId, targetId);
                        break;
                    case "video":
                        await CrawlSingleVideo(taskId, targetId);
                        break;
                    case "url_list":
                        // url_list任务类型在单独的方法中处理
                        break;
                }
                
                await UpdateTaskStatus(taskId, "completed", "", maxItems);
            }
            catch (Exception ex)
            {
                await UpdateTaskStatus(taskId, "failed", ex.Message, 0);
            }
        }
        
        // 关键词采集
        private async Task CrawlByKeyword(string taskId, string keyword, int maxItems)
        {
            var videos = await _youtubeClient.Search.GetVideosAsync(keyword);
            var processed = 0;
            
            foreach (var video in videos.Take(maxItems))
            {
                if (await IsVideoExists(video.Id.Value)) // 将VideoId转换为字符串
                {
                    processed++;
                    await UpdateTaskProgress(taskId, processed, video.Id.Value); // 将VideoId转换为字符串
                    continue;
                }
                
                try
                {
                    await ProcessVideo(video.Id.Value); // 将VideoId转换为字符串
                    processed++;
                    await UpdateTaskProgress(taskId, processed, video.Id.Value); // 将VideoId转换为字符串
                    
                    // 延迟避免请求过快
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing video {video.Id}: {ex.Message}");
                }
            }
            
            // 记录关键词搜索
            await RecordKeywordSearchAsync(keyword, processed);
        }
        
        // 频道采集
        private async Task CrawlChannel(string taskId, string channelId, int maxVideos)
        {
            // 先获取频道信息
            var channel = await _youtubeClient.Channels.GetAsync(channelId);
            await SaveChannelInfo(channel);
            
            // 获取频道上传视频
            var videos = _youtubeClient.Channels.GetUploadsAsync(channelId);
            var processed = 0;
            
            await foreach (var video in videos)
            {
                if (processed >= maxVideos) break;
                
                if (await IsVideoExists(video.Id.Value)) // 将VideoId转换为字符串
                {
                    processed++;
                    await UpdateTaskProgress(taskId, processed, video.Id.Value); // 将VideoId转换为字符串
                    continue;
                }
                
                try
                {
                    await ProcessVideo(video.Id.Value); // 将VideoId转换为字符串
                    processed++;
                    await UpdateTaskProgress(taskId, processed, video.Id.Value); // 将VideoId转换为字符串
                    
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing video {video.Id}: {ex.Message}");
                }
            }
        }
        
        // 播放列表采集
        private async Task CrawlPlaylist(string taskId, string playlistId)
        {
            var playlist = await _youtubeClient.Playlists.GetAsync(playlistId);
            await SavePlaylistInfoAsync(playlist);
            
            var videos = await _youtubeClient.Playlists.GetVideosAsync(playlistId);
            var processed = 0;
            
            foreach (var video in videos)
            {
                if (await IsVideoExists(video.Id.Value)) // 将VideoId转换为字符串
                {
                    processed++;
                    await UpdateTaskProgress(taskId, processed, video.Id.Value); // 将VideoId转换为字符串
                    continue;
                }
                
                try
                {
                    await ProcessVideo(video.Id.Value); // 将VideoId转换为字符串
                    await AddVideoToPlaylistAsync(playlistId, video.Id.Value, processed + 1); // 将VideoId转换为字符串
                    processed++;
                    await UpdateTaskProgress(taskId, processed, video.Id.Value); // 将VideoId转换为字符串
                    
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing video {video.Id}: {ex.Message}");
                }
            }
        }
        
        // 从URL采集单个视频
        private async Task CrawlSingleVideo(string taskId, string videoUrl)
        {
            var videoId = ExtractVideoId(videoUrl);
            if (string.IsNullOrEmpty(videoId))
            {
                Console.WriteLine($"无效的视频URL: {videoUrl}");
                return;
            }
            
            if (await IsVideoExists(videoId))
            {
                Console.WriteLine($"视频已存在: {videoId}");
                await UpdateTaskProgress(taskId, 1, videoId);
                return;
            }
            
            try
            {
                await ProcessVideo(videoId);
                await UpdateTaskProgress(taskId, 1, videoId);
                
                // 延迟避免请求过快
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing video {videoId}: {ex.Message}");
            }
        }

        // 单个视频处理
        private async Task ProcessVideo(string videoId)
        {
            var video = await _youtubeClient.Videos.GetAsync(videoId);

            // 先保存频道信息，确保外键约束满足
            var channel = await _youtubeClient.Channels.GetAsync(video.Author.ChannelId.Value);
            await SaveChannelInfo(channel);

            // 再保存视频信息
            await SaveVideoInfo(video);

            // 流信息获取功能已禁用 - 避免YouTube防护机制导致的cipher manifest错误
            // 如需下载视频，可使用其他工具或库
        }
        
        // 数据库操作方法
        private async Task<bool> IsVideoExists(string videoId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = "SELECT 1 FROM Videos WHERE VideoId = @VideoId";
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@VideoId", videoId);
            
            return (await command.ExecuteScalarAsync()) != null;
        }
        
        private async Task SaveChannelInfo(Channel channel)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                INSERT OR REPLACE INTO Channels 
                (ChannelId, Title, Description, ThumbnailUrl, SubscriberCount, VideoCount, ViewCount, PublishedAt, LastCrawled)
                VALUES (@ChannelId, @Title, @Description, @ThumbnailUrl, @SubscriberCount, @VideoCount, @ViewCount, @PublishedAt, CURRENT_TIMESTAMP)";
                
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@ChannelId", channel.Id.Value); // 将ChannelId转换为字符串
            command.Parameters.AddWithValue("@Title", channel.Title ?? "");
            command.Parameters.AddWithValue("@Description", channel.Title ?? ""); // Description属性不存在，使用Title代替
            command.Parameters.AddWithValue("@ThumbnailUrl", GetBestThumbnail(channel.Thumbnails) ?? "");
            command.Parameters.AddWithValue("@SubscriberCount", 0); // YouTube不再公开订阅数
            command.Parameters.AddWithValue("@VideoCount", 0);
            command.Parameters.AddWithValue("@ViewCount", 0);
            command.Parameters.AddWithValue("@PublishedAt", DateTime.MinValue);
            
            await command.ExecuteNonQueryAsync();
        }
        
        private async Task SaveVideoInfo(Video video)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                INSERT OR REPLACE INTO Videos 
                (VideoId, ChannelId, Title, Description, Duration, UploadDate, ViewCount, LikeCount, 
                 ThumbnailUrl, Tags, IsLiveContent, IsAgeRestricted, LastCrawled)
                VALUES (@VideoId, @ChannelId, @Title, @Description, @Duration, @UploadDate, @ViewCount, @LikeCount,
                        @ThumbnailUrl, @Tags, @IsLiveContent, @IsAgeRestricted, CURRENT_TIMESTAMP)";
                
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@VideoId", video.Id.Value); // 将VideoId转换为字符串
            command.Parameters.AddWithValue("@ChannelId", video.Author.ChannelId.Value); // 将ChannelId转换为字符串
            command.Parameters.AddWithValue("@Title", video.Title ?? "");
            command.Parameters.AddWithValue("@Description", video.Description ?? "");
            command.Parameters.AddWithValue("@Duration", (int)(video.Duration?.TotalSeconds ?? 0));
            command.Parameters.AddWithValue("@UploadDate", video.UploadDate);
            command.Parameters.AddWithValue("@ViewCount", 0L); // ViewCount属性在YoutubeExplode 6.x中不可用
            command.Parameters.AddWithValue("@LikeCount", 0L); // LikeCount属性在YoutubeExplode 6.x中不可用
            command.Parameters.AddWithValue("@ThumbnailUrl", GetBestThumbnail(video.Thumbnails) ?? "");
            command.Parameters.AddWithValue("@Tags", System.Text.Json.JsonSerializer.Serialize(video.Keywords ?? new List<string>()));
            command.Parameters.AddWithValue("@IsLiveContent", 0); // IsLive属性在YoutubeExplode 6.x中不可用
            command.Parameters.AddWithValue("@IsAgeRestricted", 0); // 需要额外检查
            
            await command.ExecuteNonQueryAsync();
        }
        
        // 其他数据库操作方法...
        
        // Excel导出功能
        public async Task<string> ExportToExcel(string exportType = "all")
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var package = new ExcelPackage();

            if (exportType == "all" || exportType == "videos")
                await ExportVideosToExcel(package);

            if (exportType == "all" || exportType == "channels")
                await ExportChannelsToExcelAsync(package);

            // 流信息导出已禁用 - 因为流信息获取功能已关闭
            // if (exportType == "all" || exportType == "streams")
            //     await ExportStreamsToExcelAsync(package);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filePath = Path.Combine(_excelExportPath, $"youtube_data_{timestamp}.xlsx");

            // 确保目录存在
            Directory.CreateDirectory(_excelExportPath);

            await package.SaveAsAsync(new FileInfo(filePath));
            Console.WriteLine($"Excel文件已导出到: {filePath}");

            return filePath;
        }
        
        private async Task ExportVideosToExcel(ExcelPackage package)
        {
            var worksheet = package.Workbook.Worksheets.Add("视频数据");
            
            // 设置表头
            worksheet.Cells[1, 1].Value = "视频ID";
            worksheet.Cells[1, 2].Value = "标题";
            worksheet.Cells[1, 3].Value = "频道";
            worksheet.Cells[1, 4].Value = "时长";
            worksheet.Cells[1, 5].Value = "上传时间";
            worksheet.Cells[1, 6].Value = "观看次数";
            worksheet.Cells[1, 7].Value = "点赞数";
            worksheet.Cells[1, 8].Value = "关键词";
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                SELECT v.VideoId, v.Title, c.Title as ChannelName, v.Duration, v.UploadDate, 
                       v.ViewCount, v.LikeCount, v.Keywords
                FROM Videos v
                LEFT JOIN Channels c ON v.ChannelId = c.ChannelId
                ORDER BY v.ViewCount DESC";
                
            using var command = new SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();
            
            int row = 2;
            while (await reader.ReadAsync())
            {
                worksheet.Cells[row, 1].Value = reader["VideoId"].ToString();
                worksheet.Cells[row, 2].Value = reader["Title"].ToString();
                worksheet.Cells[row, 3].Value = reader["ChannelName"].ToString();
                worksheet.Cells[row, 4].Value = FormatDuration((int)Convert.ToInt64(reader["Duration"]));
                worksheet.Cells[row, 5].Value = reader["UploadDate"].ToString();
                worksheet.Cells[row, 6].Value = Convert.ToInt64(reader["ViewCount"]);
                worksheet.Cells[row, 7].Value = Convert.ToInt64(reader["LikeCount"]);
                worksheet.Cells[row, 8].Value = reader["Keywords"].ToString();
                row++;
            }
            
            worksheet.Cells.AutoFitColumns();
        }

        // URL列表采集执行
        private async Task ExecuteUrlListCrawl(string taskId, List<string> urls, int maxItemsPerUrl)
        {
            try
            {
                var processed = 0;
                
                foreach (var url in urls)
                {
                    if (processed >= urls.Count * maxItemsPerUrl) break;
                    
                    var videoId = ExtractVideoId(url);
                    if (string.IsNullOrEmpty(videoId))
                    {
                        Console.WriteLine($"跳过无效URL: {url}");
                        continue;
                    }
                    
                    if (await IsVideoExists(videoId))
                    {
                        processed++;
                        await UpdateTaskProgress(taskId, processed, videoId);
                        continue;
                    }
                    
                    try
                    {
                        await ProcessVideo(videoId);
                        processed++;
                        await UpdateTaskProgress(taskId, processed, videoId);
                        
                        // 延迟避免请求过快
                        await Task.Delay(1000);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing video {videoId}: {ex.Message}");
                    }
                }
                
                await UpdateTaskStatus(taskId, "completed", "", processed);
            }
            catch (Exception ex)
            {
                await UpdateTaskStatus(taskId, "failed", ex.Message, 0);
            }
        }
        
        // 断点续传支持
        public async Task<List<string>> GetIncompleteTasks()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = "SELECT TaskId FROM CrawlTasks WHERE Status IN ('running', 'paused')";
            using var command = new SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();
            
            var tasks = new List<string>();
            while (await reader.ReadAsync())
            {
                tasks.Add(reader["TaskId"].ToString() ?? "");
            }
            
            return tasks;
        }

        // 获取任务状态信息
        public async Task<List<CrawlTaskInfo>> GetTaskStatus()
        {
            var tasks = new List<CrawlTaskInfo>();
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                SELECT TaskId, TaskType, TargetId, Status, ProcessedItems, TotalItems, 
                       StartTime, EndTime, ErrorMessage
                FROM CrawlTasks 
                ORDER BY StartTime DESC";
                
            using var command = new SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
            var startTime = reader["StartTime"] as DateTime?;
            tasks.Add(new CrawlTaskInfo
            {
                TaskId = reader["TaskId"].ToString() ?? "",
                TaskType = reader["TaskType"].ToString() ?? "",
                TargetId = reader["TargetId"].ToString() ?? "",
                Status = reader["Status"].ToString() ?? "",
                ProcessedItems = Convert.ToInt32(reader["ProcessedItems"]),
                TotalItems = Convert.ToInt32(reader["TotalItems"]),
                StartTime = startTime ?? DateTime.Now,
                EndTime = reader["EndTime"] as DateTime?,
                ErrorMessage = reader["ErrorMessage"].ToString() ?? ""
            });
            }
            
            return tasks;
        }
        
        public async Task ResumeTask(string taskId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                SELECT TaskType, TargetId, ProcessedItems, TotalItems, LastProcessedId
                FROM CrawlTasks WHERE TaskId = @TaskId";
                
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@TaskId", taskId);
            
            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var taskType = reader["TaskType"].ToString() ?? "";
                var targetId = reader["TargetId"].ToString() ?? "";
                var processed = (int)reader["ProcessedItems"];
                var total = (int)reader["TotalItems"];
                
                await UpdateTaskStatus(taskId, "running", "", processed);
                _ = Task.Run(async () => await ResumeCrawlTask(taskId, taskType, targetId, processed, total));
            }
        }
        
        // 辅助方法
        private string GetBestThumbnail(IReadOnlyList<Thumbnail> thumbnails)
        {
            return thumbnails?.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url ?? "";
        }
        
        private string FormatDuration(int seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        // 从YouTube URL提取视频ID
        private string? ExtractVideoId(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            
            try
            {
                if (url.Contains("youtube.com/watch?v="))
                {
                    var uri = new Uri(url);
                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    return query["v"];
                }
                else if (url.Contains("youtu.be/"))
                {
                    var uri = new Uri(url);
                    return uri.AbsolutePath.Trim('/');
                }
                else if (url.Length == 11) // 假设是纯视频ID
                {
                    return url;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting video ID from {url}: {ex.Message}");
            }
            
            return null;
        }
        
        private async Task UpdateTaskStatus(string taskId, string status, string errorMessage, int processedItems)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                UPDATE CrawlTasks 
                SET Status = @Status, ErrorMessage = @ErrorMessage, ProcessedItems = @ProcessedItems,
                    EndTime = CASE WHEN @Status = 'completed' THEN CURRENT_TIMESTAMP ELSE EndTime END
                WHERE TaskId = @TaskId";
                
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Status", status);
            command.Parameters.AddWithValue("@ErrorMessage", errorMessage ?? "");
            command.Parameters.AddWithValue("@ProcessedItems", processedItems);
            command.Parameters.AddWithValue("@TaskId", taskId);
            
            await command.ExecuteNonQueryAsync();
        }
        
        private async Task UpdateTaskProgress(string taskId, int processed, string lastProcessedId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = "UPDATE CrawlTasks SET ProcessedItems = @Processed, LastProcessedId = @LastId WHERE TaskId = @TaskId";
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Processed", processed);
            command.Parameters.AddWithValue("@LastId", lastProcessedId);
            command.Parameters.AddWithValue("@TaskId", taskId);
            
            await command.ExecuteNonQueryAsync();
        }
        
        // 添加缺失的方法
        private async Task RecordKeywordSearchAsync(string keyword, int processed)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                INSERT OR REPLACE INTO KeywordSearches (Keyword, SearchCount, LastSearched)
                VALUES (@Keyword, COALESCE((SELECT SearchCount FROM KeywordSearches WHERE Keyword = @Keyword), 0) + 1, CURRENT_TIMESTAMP)";
                
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Keyword", keyword);
            await command.ExecuteNonQueryAsync();
        }
        
        private async Task SavePlaylistInfoAsync(Playlist playlist)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                INSERT OR REPLACE INTO Playlists 
                (PlaylistId, Title, Description, ChannelId, ThumbnailUrl, VideoCount, LastCrawled)
                VALUES (@PlaylistId, @Title, @Description, @ChannelId, @ThumbnailUrl, @VideoCount, CURRENT_TIMESTAMP)";
                
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@PlaylistId", playlist.Id.Value); // 将PlaylistId转换为字符串
            command.Parameters.AddWithValue("@Title", playlist.Title ?? "");
            command.Parameters.AddWithValue("@Description", playlist.Description ?? "");
            command.Parameters.AddWithValue("@ChannelId", playlist.Author?.ChannelId ?? "");
            command.Parameters.AddWithValue("@ThumbnailUrl", GetBestThumbnail(playlist.Thumbnails) ?? "");
            command.Parameters.AddWithValue("@VideoCount", (await _youtubeClient.Playlists.GetVideosAsync(playlist.Id)).Count);
            
            await command.ExecuteNonQueryAsync();
        }
        
        private async Task AddVideoToPlaylistAsync(string playlistId, string videoId, int position)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                INSERT OR REPLACE INTO PlaylistVideos 
                (PlaylistId, VideoId, Position, AddedAt)
                VALUES (@PlaylistId, @VideoId, @Position, CURRENT_TIMESTAMP)";
                
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@PlaylistId", playlistId);
            command.Parameters.AddWithValue("@VideoId", videoId);
            command.Parameters.AddWithValue("@Position", position);
            
            await command.ExecuteNonQueryAsync();
        }
        
        private async Task SaveStreamInfoAsync(string videoId, StreamManifest streamManifest)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                INSERT OR REPLACE INTO VideoStreams 
                (VideoId, StreamType, Container, VideoCodec, AudioCodec, VideoQuality, 
                 AudioBitrate, VideoBitrate, FileSize, LastCrawled)
                VALUES (@VideoId, @StreamType, @Container, @VideoCodec, @AudioCodec, @VideoQuality,
                        @AudioBitrate, @VideoBitrate, @FileSize, CURRENT_TIMESTAMP)";
                
            // 分别处理视频流和音频流
            var videoStreams = streamManifest.GetVideoStreams();
            var audioStreams = streamManifest.GetAudioStreams();
            
            foreach (var stream in videoStreams.Take(3)) // 只保存前3个视频流
            {
                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@VideoId", videoId);
                command.Parameters.AddWithValue("@StreamType", "video");
                command.Parameters.AddWithValue("@Container", stream.Container.Name);
                command.Parameters.AddWithValue("@VideoCodec", stream.VideoCodec);
                command.Parameters.AddWithValue("@AudioCodec", "");
                command.Parameters.AddWithValue("@VideoQuality", stream.VideoQuality.ToString());
                command.Parameters.AddWithValue("@AudioBitrate", 0);
                command.Parameters.AddWithValue("@VideoBitrate", stream.Bitrate.BitsPerSecond);
                command.Parameters.AddWithValue("@FileSize", stream.Size.Bytes);
                
                await command.ExecuteNonQueryAsync();
            }
            
            foreach (var stream in audioStreams.Take(2)) // 只保存前2个音频流
            {
                using var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@VideoId", videoId);
                command.Parameters.AddWithValue("@StreamType", "audio");
                command.Parameters.AddWithValue("@Container", stream.Container.Name);
                command.Parameters.AddWithValue("@VideoCodec", "");
                command.Parameters.AddWithValue("@AudioCodec", stream.AudioCodec);
                command.Parameters.AddWithValue("@VideoQuality", "");
                command.Parameters.AddWithValue("@AudioBitrate", stream.Bitrate.BitsPerSecond);
                command.Parameters.AddWithValue("@VideoBitrate", 0);
                command.Parameters.AddWithValue("@FileSize", stream.Size.Bytes);
                
                await command.ExecuteNonQueryAsync();
            }
        }
        
        private async Task SaveCaptionInfoAsync(string videoId, object captionManifest)
        {
            // 字幕功能暂时禁用，因为YoutubeExplode API版本可能不兼容
            // 在后续版本中实现
            await Task.CompletedTask;
        }
        
        private async Task ExportChannelsToExcelAsync(ExcelPackage package)
        {
            var worksheet = package.Workbook.Worksheets.Add("频道数据");
            
            // 设置表头
            worksheet.Cells[1, 1].Value = "频道ID";
            worksheet.Cells[1, 2].Value = "频道名称";
            worksheet.Cells[1, 3].Value = "描述";
            worksheet.Cells[1, 4].Value = "订阅数";
            worksheet.Cells[1, 5].Value = "视频数";
            worksheet.Cells[1, 6].Value = "观看数";
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                SELECT ChannelId, Title, Description, SubscriberCount, VideoCount, ViewCount
                FROM Channels";
                
            using var command = new SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();
            
            int row = 2;
            while (await reader.ReadAsync())
            {
                worksheet.Cells[row, 1].Value = reader["ChannelId"].ToString();
                worksheet.Cells[row, 2].Value = reader["Title"].ToString();
                worksheet.Cells[row, 3].Value = reader["Description"].ToString();
                worksheet.Cells[row, 4].Value = Convert.ToInt64(reader["SubscriberCount"]);
                worksheet.Cells[row, 5].Value = Convert.ToInt64(reader["VideoCount"]);
                worksheet.Cells[row, 6].Value = Convert.ToInt64(reader["ViewCount"]);
                row++;
            }
            
            worksheet.Cells.AutoFitColumns();
        }
        
        private async Task ExportStreamsToExcelAsync(ExcelPackage package)
        {
            var worksheet = package.Workbook.Worksheets.Add("流信息");
            
            // 设置表头
            worksheet.Cells[1, 1].Value = "视频ID";
            worksheet.Cells[1, 2].Value = "流类型";
            worksheet.Cells[1, 3].Value = "容器格式";
            worksheet.Cells[1, 4].Value = "视频编码";
            worksheet.Cells[1, 5].Value = "音频编码";
            worksheet.Cells[1, 6].Value = "视频质量";
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                SELECT VideoId, StreamType, Container, VideoCodec, AudioCodec, VideoQuality
                FROM VideoStreams";
                
            using var command = new SqliteCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();
            
            int row = 2;
            while (await reader.ReadAsync())
            {
                worksheet.Cells[row, 1].Value = reader["VideoId"].ToString();
                worksheet.Cells[row, 2].Value = reader["StreamType"].ToString();
                worksheet.Cells[row, 3].Value = reader["Container"].ToString();
                worksheet.Cells[row, 4].Value = reader["VideoCodec"].ToString();
                worksheet.Cells[row, 5].Value = reader["AudioCodec"].ToString();
                worksheet.Cells[row, 6].Value = reader["VideoQuality"].ToString();
                row++;
            }
            
            worksheet.Cells.AutoFitColumns();
        }
        
        private async Task ResumeCrawlTask(string taskId, string taskType, string targetId, int processed, int total)
        {
            // 实现断点续传逻辑
            await ExecuteCrawlTask(taskId, taskType, targetId, total);
        }
        
        public void Dispose()
        {
            // YoutubeClient在YoutubeExplode 6.x中不需要手动释放
            // 如果需要释放资源，可以在后续版本中实现
        }
    }
    
    public class CrawlTaskInfo
    {
        public string TaskId { get; set; } = "";
        public string TaskType { get; set; } = "";
        public string TargetId { get; set; } = "";
        public string Status { get; set; } = "";
        public int ProcessedItems { get; set; }
        public int TotalItems { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string ErrorMessage { get; set; } = "";
        
        public double ProgressPercent => TotalItems > 0 ? (ProcessedItems * 100.0) / TotalItems : 0;
        public string Duration
        {
            get
            {
                try
                {
                    if (StartTime == DateTime.MinValue)
                    {
                        return "未知";
                    }

                    TimeSpan duration;
                    if (EndTime.HasValue)
                    {
                        duration = EndTime.Value - StartTime;
                    }
                    else
                    {
                        duration = DateTime.Now - StartTime;
                    }

                    // 格式化时间为小时:分钟:秒
                    var hours = (int)duration.TotalHours;
                    var minutes = duration.Minutes;
                    var seconds = duration.Seconds;
                    return $"{hours:00}:{minutes:00}:{seconds:00}";
                }
                catch
                {
                    return "计算错误";
                }
            }
        }
    }
}