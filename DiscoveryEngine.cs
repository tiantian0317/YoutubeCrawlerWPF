using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using YoutubeExplode;
using YoutubeExplode.Channels;
using YoutubeExplode.Common;
using YoutubeExplode.Search;
using YoutubeExplode.Videos;

namespace YoutubeCrawlerWPF
{
    public enum DiscoveryMode
    {
        BreadthFirst,   // 广度优先：先处理当前层级的所有节点
        DepthFirst      // 深度优先：沿着一个路径深入后再返回
    }
    
    public class DiscoveryEngine
    {
        private readonly YoutubeClient _youtubeClient;
        private readonly string _connectionString;
        private readonly DiscoveryMode _mode;
        private readonly int _maxDepth;
        private readonly int _branchingFactor;
        
        public DiscoveryEngine(string databasePath, DiscoveryMode mode = DiscoveryMode.BreadthFirst, 
                             int maxDepth = 3, int branchingFactor = 5)
        {
            _youtubeClient = new YoutubeClient();
            _connectionString = $"Data Source={databasePath}";
            _mode = mode;
            _maxDepth = maxDepth;
            _branchingFactor = branchingFactor;
        }
        
        // 启动智能发现任务
        public async Task<string> StartDiscovery(string seed, DiscoverySource source = DiscoverySource.Keyword, 
                                               int maxTotalItems = 1000)
        {
            var taskId = Guid.NewGuid().ToString();
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var insertSql = @"
                INSERT INTO DiscoveryTasks (TaskId, Seed, Source, Mode, MaxDepth, BranchingFactor, 
                                          MaxTotalItems, Status, StartTime)
                VALUES (@TaskId, @Seed, @Source, @Mode, @MaxDepth, @BranchingFactor, 
                        @MaxTotalItems, 'running', CURRENT_TIMESTAMP)";
                
            using var command = new SqliteCommand(insertSql, connection);
            command.Parameters.AddWithValue("@TaskId", taskId);
            command.Parameters.AddWithValue("@Seed", seed);
            command.Parameters.AddWithValue("@Source", source.ToString());
            command.Parameters.AddWithValue("@Mode", _mode.ToString());
            command.Parameters.AddWithValue("@MaxDepth", _maxDepth);
            command.Parameters.AddWithValue("@BranchingFactor", _branchingFactor);
            command.Parameters.AddWithValue("@MaxTotalItems", maxTotalItems);
            
            await command.ExecuteNonQueryAsync();
            
            _ = Task.Run(async () => await ExecuteDiscovery(taskId, seed, source, maxTotalItems));
            
            return taskId;
        }
        
        private async Task ExecuteDiscovery(string taskId, string seed, DiscoverySource source, int maxTotalItems)
        {
            try
            {
                var discoveredItems = new HashSet<string>();
                var queue = new Queue<DiscoveryNode>();
                
                // 添加初始种子节点
                var seedNode = await CreateSeedNode(seed, source);
                queue.Enqueue(seedNode);
                discoveredItems.Add(seedNode.Id);
                
                int totalProcessed = 0;
                
                while (queue.Count > 0 && totalProcessed < maxTotalItems)
                {
                    DiscoveryNode currentNode;
                    
                    if (_mode == DiscoveryMode.BreadthFirst)
                    {
                        currentNode = queue.Dequeue();
                    }
                    else // DepthFirst
                    {
                        currentNode = queue.Peek();
                        if (currentNode.Depth >= _maxDepth)
                        {
                            queue.Dequeue();
                            continue;
                        }
                    }
                    
                    // 处理当前节点
                    var newNodes = await ProcessNode(currentNode, discoveredItems);
                    totalProcessed++;
                    
                    // 更新任务进度
                    await UpdateDiscoveryProgress(taskId, totalProcessed, queue.Count, currentNode.Id);
                    
                    // 添加新发现的节点到队列
                    foreach (var newNode in newNodes.Take(_branchingFactor))
                    {
                        if (!discoveredItems.Contains(newNode.Id) && newNode.Depth <= _maxDepth)
                        {
                            discoveredItems.Add(newNode.Id);
                            
                            if (_mode == DiscoveryMode.BreadthFirst)
                            {
                                queue.Enqueue(newNode);
                            }
                            else // DepthFirst
                            {
                                // 深度优先时插入到队列前端
                                var tempQueue = new Queue<DiscoveryNode>();
                                tempQueue.Enqueue(newNode);
                                while (queue.Count > 0) tempQueue.Enqueue(queue.Dequeue());
                                queue = tempQueue;
                            }
                        }
                    }
                    
                    // 延迟控制
                    await Task.Delay(500);
                }
                
                await UpdateDiscoveryStatus(taskId, "completed", totalProcessed, "");
            }
            catch (Exception ex)
            {
                await UpdateDiscoveryStatus(taskId, "failed", 0, ex.Message);
            }
        }
        
        private async Task<DiscoveryNode> CreateSeedNode(string seed, DiscoverySource source)
        {
            return source switch
            {
                DiscoverySource.Keyword => new DiscoveryNode
                {
                    Id = $"keyword_{seed}",
                    Type = DiscoveryNodeType.Keyword,
                    Content = seed,
                    Depth = 0
                },
                DiscoverySource.Channel => new DiscoveryNode
                {
                    Id = $"channel_{seed}",
                    Type = DiscoveryNodeType.Channel,
                    Content = seed,
                    Depth = 0
                },
                DiscoverySource.Video => new DiscoveryNode
                {
                    Id = $"video_{seed}",
                    Type = DiscoveryNodeType.Video,
                    Content = seed,
                    Depth = 0
                },
                _ => throw new ArgumentException("Unsupported discovery source")
            };
        }
        
        private async Task<List<DiscoveryNode>> ProcessNode(DiscoveryNode node, HashSet<string> discoveredItems)
        {
            var newNodes = new List<DiscoveryNode>();
            
            try
            {
                switch (node.Type)
                {
                    case DiscoveryNodeType.Keyword:
                        newNodes.AddRange(await DiscoverFromKeyword(node.Content, node.Depth + 1));
                        break;
                    
                    case DiscoveryNodeType.Channel:
                        newNodes.AddRange(await DiscoverFromChannel(node.Content, node.Depth + 1));
                        break;
                    
                    case DiscoveryNodeType.Video:
                        newNodes.AddRange(await DiscoverFromVideo(node.Content, node.Depth + 1));
                        break;
                }
                
                // 记录节点处理结果
                await RecordNodeProcessing(node, newNodes.Count);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing node {node.Id}: {ex.Message}");
            }
            
            return newNodes;
        }
        
        private async Task<List<DiscoveryNode>> DiscoverFromKeyword(string keyword, int depth)
        {
            var nodes = new List<DiscoveryNode>();
            
            // 搜索相关视频
            var videos = await _youtubeClient.Search.GetVideosAsync(keyword);
            foreach (var video in videos.Take(_branchingFactor))
            {
                nodes.Add(new DiscoveryNode
                {
                    Id = $"video_{video.Id}",
                    Type = DiscoveryNodeType.Video,
                    Content = video.Id,
                    Depth = depth
                });
            }
            
            // 搜索相关频道
            var channels = await _youtubeClient.Search.GetChannelsAsync(keyword);
            foreach (var channel in channels.Take(_branchingFactor / 2))
            {
                nodes.Add(new DiscoveryNode
                {
                    Id = $"channel_{channel.Id}",
                    Type = DiscoveryNodeType.Channel,
                    Content = channel.Id,
                    Depth = depth
                });
            }
            
            // 从视频标题提取新关键词
            var newKeywords = ExtractKeywordsFromVideoTitles(videos.Select(v => v.Title));
            foreach (var newKeyword in newKeywords.Take(3))
            {
                nodes.Add(new DiscoveryNode
                {
                    Id = $"keyword_{newKeyword}",
                    Type = DiscoveryNodeType.Keyword,
                    Content = newKeyword,
                    Depth = depth
                });
            }
            
            return nodes;
        }
        
        private async Task<List<DiscoveryNode>> DiscoverFromChannel(string channelId, int depth)
        {
            var nodes = new List<DiscoveryNode>();
            
            try
            {
                // 获取频道上传视频
                var videos = _youtubeClient.Channels.GetUploadsAsync(channelId);
                int count = 0;
                
                await foreach (var video in videos)
                {
                    if (count >= _branchingFactor) break;
                    
                    nodes.Add(new DiscoveryNode
                    {
                        Id = $"video_{video.Id}",
                        Type = DiscoveryNodeType.Video,
                        Content = video.Id,
                        Depth = depth
                    });
                    count++;
                }
                
                // 从视频中发现相关内容
                var sampleVideos = nodes.Take(2).ToList();
                foreach (var videoNode in sampleVideos)
                {
                    var relatedNodes = await DiscoverFromVideo(videoNode.Content, depth + 1);
                    nodes.AddRange(relatedNodes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error discovering from channel {channelId}: {ex.Message}");
            }
            
            return nodes;
        }
        
        private async Task<List<DiscoveryNode>> DiscoverFromVideo(string videoId, int depth)
        {
            var nodes = new List<DiscoveryNode>();
            
            try
            {
                var video = await _youtubeClient.Videos.GetAsync(videoId);
                
                // 相关视频推荐（通过搜索相似标题）
                var relatedVideos = await _youtubeClient.Search.GetVideosAsync(video.Title);
                foreach (var relatedVideo in relatedVideos.Take(_branchingFactor / 2))
                {
                    if (relatedVideo.Id != videoId)
                    {
                        nodes.Add(new DiscoveryNode
                        {
                            Id = $"video_{relatedVideo.Id}",
                            Type = DiscoveryNodeType.Video,
                            Content = relatedVideo.Id,
                            Depth = depth
                        });
                    }
                }
                
                // 频道发现
                nodes.Add(new DiscoveryNode
                {
                    Id = $"channel_{video.Author.ChannelId}",
                    Type = DiscoveryNodeType.Channel,
                    Content = video.Author.ChannelId,
                    Depth = depth
                });
                
                // 关键词提取
                var keywords = ExtractKeywordsFromVideoInfo(video);
                foreach (var keyword in keywords.Take(2))
                {
                    nodes.Add(new DiscoveryNode
                    {
                        Id = $"keyword_{keyword}",
                        Type = DiscoveryNodeType.Keyword,
                        Content = keyword,
                        Depth = depth
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error discovering from video {videoId}: {ex.Message}");
            }
            
            return nodes;
        }
        
        private IEnumerable<string> ExtractKeywordsFromVideoTitles(IEnumerable<string> titles)
        {
            var keywords = new HashSet<string>();
            
            foreach (var title in titles)
            {
                // 简单关键词提取逻辑
                var words = title.Split(' ', '-', '|', ',', '.', '!', '?')
                    .Where(w => w.Length > 3 && !IsCommonWord(w))
                    .Select(w => w.ToLowerInvariant());
                
                keywords.UnionWith(words);
            }
            
            return keywords.Take(5);
        }
        
        private IEnumerable<string> ExtractKeywordsFromVideoInfo(Video video)
        {
            var keywords = new HashSet<string>();
            
            // 从标题提取
            var titleWords = video.Title.Split(' ', '-', '|')
                .Where(w => w.Length > 3 && !IsCommonWord(w))
                .Select(w => w.ToLowerInvariant());
            keywords.UnionWith(titleWords);
            
            // 从描述提取
            if (!string.IsNullOrEmpty(video.Description))
            {
                var descWords = video.Description.Split(' ', '\n', '\r')
                    .Where(w => w.Length > 5 && !IsCommonWord(w))
                    .Select(w => w.ToLowerInvariant());
                keywords.UnionWith(descWords.Take(3));
            }
            
            // 已有标签
            keywords.UnionWith(video.Keywords ?? Enumerable.Empty<string>());
            
            return keywords.Take(5);
        }
        
        private bool IsCommonWord(string word)
        {
            var commonWords = new HashSet<string> 
            { 
                "the", "and", "you", "that", "was", "for", "are", "with", "his", "they",
                "this", "have", "from", "one", "had", "word", "but", "not", "what", "all"
            };
            
            return commonWords.Contains(word.ToLowerInvariant());
        }
        
        private async Task RecordNodeProcessing(DiscoveryNode node, int discoveredCount)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                INSERT INTO DiscoveryNodes (NodeId, NodeType, Content, Depth, DiscoveredCount, ProcessedAt)
                VALUES (@NodeId, @NodeType, @Content, @Depth, @DiscoveredCount, CURRENT_TIMESTAMP)";
                
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@NodeId", node.Id);
            command.Parameters.AddWithValue("@NodeType", node.Type.ToString());
            command.Parameters.AddWithValue("@Content", node.Content);
            command.Parameters.AddWithValue("@Depth", node.Depth);
            command.Parameters.AddWithValue("@DiscoveredCount", discoveredCount);
            
            await command.ExecuteNonQueryAsync();
        }
        
        private async Task UpdateDiscoveryProgress(string taskId, int processedCount, int queueSize, string currentNodeId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                UPDATE DiscoveryTasks 
                SET ProcessedCount = @ProcessedCount, QueueSize = @QueueSize, 
                    LastProcessedNode = @LastNode, LastUpdate = CURRENT_TIMESTAMP
                WHERE TaskId = @TaskId";
                
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@ProcessedCount", processedCount);
            command.Parameters.AddWithValue("@QueueSize", queueSize);
            command.Parameters.AddWithValue("@LastNode", currentNodeId);
            command.Parameters.AddWithValue("@TaskId", taskId);
            
            await command.ExecuteNonQueryAsync();
        }
        
        private async Task UpdateDiscoveryStatus(string taskId, string status, int totalProcessed, string errorMessage)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                UPDATE DiscoveryTasks 
                SET Status = @Status, TotalProcessed = @TotalProcessed, ErrorMessage = @ErrorMessage,
                    EndTime = CASE WHEN @Status = 'completed' THEN CURRENT_TIMESTAMP ELSE EndTime END
                WHERE TaskId = @TaskId";
                
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@Status", status);
            command.Parameters.AddWithValue("@TotalProcessed", totalProcessed);
            command.Parameters.AddWithValue("@ErrorMessage", errorMessage ?? "");
            command.Parameters.AddWithValue("@TaskId", taskId);
            
            await command.ExecuteNonQueryAsync();
        }
    }
    
    public enum DiscoverySource
    {
        Keyword,
        Channel,
        Video,
        Url
    }
    
    public enum DiscoveryNodeType
    {
        Keyword,
        Channel,
        Video
    }
    
    public class DiscoveryNode
    {
        public string Id { get; set; } = "";
        public DiscoveryNodeType Type { get; set; }
        public string Content { get; set; } = "";
        public int Depth { get; set; }
    }
}