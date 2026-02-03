# YoutubeCrawlerWPF - YouTube运营数据分析工具

一款专为YouTube内容创作者和运营人员打造的数据采集与分析工具，助力深度对标研究、内容策略优化与竞品分析。

[![GUI Screenshot](https://github.com/tiantian0317/YoutubeCrawlerWPF/blob/main/%E6%90%9C%E7%8B%97%E6%88%AA%E5%9B%BE20260203235604.png)](https://github.com/tiantian0317/YoutubeCrawlerWPF/releases)

---

## 📥 下载安装

**最新版本下载**: [YoutubeCrawlerWPF v1.0](https://github.com/tiantian0317/YoutubeCrawlerWPF/releases/download/1.0/YoutubeCrawlerWPF.zip)

```bash
# 下载后解压，双击运行 YoutubeCrawlerWPF.exe
# 无需安装，开箱即用
```

**系统要求**:
- Windows 10/11
- .NET 6.0 Runtime (首次运行会自动提示安装)
- 网络连接

---

## 🎯 核心用途

### YouTube运营数据分析
- **频道成长追踪**: 监控目标频道的视频发布频率、内容方向变化
- **爆款视频分析**: 采集高播放量视频的标题、标签、描述策略
- **内容趋势洞察**: 通过关键词搜索发现热门话题与内容空白

### 对标视频研究
- **竞品深度剖析**: 系统性采集竞品频道的内容结构、更新节奏
- **成功要素提取**: 分析高互动视频的共同特征与最佳实践
- **差异化定位**: 通过数据对比找到自身内容的优势切入点

---

## 🏗️ 系统架构设计

### 分层架构
```
┌─────────────────────────────────────────────────────────────┐
│                     表现层 (WPF UI)                          │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐       │
│  │ 数据采集 │ │ 任务监控 │ │ 数据查看 │ │ Excel导出 │       │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘       │
└─────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────┐
│                    业务逻辑层 (CrawlerSystem)                │
│  ┌────────────┐ ┌────────────┐ ┌────────────┐              │
│  │ YouTube API │ │ 任务调度   │ │ 数据处理器 │              │
│  │  封装层    │ │   引擎     │ │           │              │
│  └────────────┘ └────────────┘ └────────────┘              │
└─────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────┐
│                    数据存储层 (SQLite)                       │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐       │
│  │ Channels │ │  Videos  │ │Playlists │ │  Tasks   │       │
│  │  频道表   │ │  视频表   │ │ 播放列表  │ │  任务表   │       │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘       │
└─────────────────────────────────────────────────────────────┘
```

### 数据流向
```
YouTube API → JSON数据 → 实体模型 → SQLite存储 → DataGrid展示 → Excel导出
```

---

## 🧮 核心算法与逻辑

### 1. 数据采集调度算法

**任务队列管理**
```csharp
// 基于优先级队列的任务调度
TaskQueue.Enqueue(new CrawlTask {
    Priority = CalculatePriority(taskType, targetId),
    RetryCount = 0,
    MaxRetries = 3
});

// 优先级计算公式
Priority = BasePriority + (IsHighValueTarget ? 10 : 0) - (RetryCount * 5);
```

**智能速率控制**
```csharp
// 自适应请求间隔，平衡效率与稳定性
int GetDelayInterval() {
    if (ConsecutiveErrors > 3) return 5000;  // 错误频发，延长间隔
    if (RequestsPerMinute > 30) return 2000; // 接近限制，降速
    return 1000; // 正常间隔
}
```

### 2. 去重与增量更新算法

**视频唯一性判定**
```sql
-- 复合索引加速查询
CREATE UNIQUE INDEX idx_video_unique ON Videos(VideoId);

-- 增量更新逻辑：仅采集新发布内容
SELECT * FROM Videos 
WHERE ChannelId = @channelId 
  AND UploadDate > (SELECT MAX(UploadDate) FROM Videos WHERE ChannelId = @channelId)
ORDER BY UploadDate DESC;
```

**频道信息版本控制**
```csharp
// 保留历史数据轨迹，支持趋势分析
if (ChannelExists(channelId)) {
    UpdateChannelMetrics(channelId, newData);  // 更新指标
    InsertChannelHistorySnapshot(channelId);   // 记录历史
} else {
    InsertNewChannel(channelId, newData);      // 新建记录
}
```

### 3. 数据关联与关系映射

**多表关联查询优化**
```sql
-- 视频-频道关联视图
CREATE VIEW VideoChannelDetail AS
SELECT 
    v.VideoId,
    v.Title AS VideoTitle,
    v.UploadDate,
    v.ViewCount,
    c.ChannelId,
    c.Title AS ChannelTitle,
    c.SubscriberCount
FROM Videos v
INNER JOIN Channels c ON v.ChannelId = c.ChannelId
WHERE v.PrivacyStatus = 'public';
```

---

## 💡 核心功能详解

### 数据采集模块

| 采集类型 | 适用场景 | 数据维度 |
|---------|---------|---------|
| **关键词搜索** | 发现热门话题、竞品分析 | 视频标题、描述、上传时间、观看数据 |
| **频道内容采集** | 对标频道深度研究 | 全量视频、发布节奏、内容演变 |
| **播放列表采集** | 专题内容批量获取 | 系列视频、课程结构、合集策略 |
| **URL列表采集** | 精准目标批量处理 | 指定视频详情、批量导入分析 |

### 数据存储设计

**规范化数据结构**
```
Channels (频道维度)
├── ChannelId (主键)
├── Title, Description
├── SubscriberCount (订阅数趋势)
├── VideoCount (总视频数)
├── ViewCount (总观看数)
└── LastCrawled (最后采集时间)

Videos (视频维度)
├── VideoId (主键)
├── ChannelId (外键关联)
├── Title, Description
├── Duration (时长)
├── UploadDate (上传日期)
├── ViewCount, LikeCount (互动数据)
├── Tags, Keywords (标签分析)
└── PrivacyStatus (隐私状态)

Playlists (合集维度)
├── PlaylistId (主键)
├── ChannelId (外键)
├── Title, Description
├── VideoCount (视频数量)
└── PrivacyStatus (公开/私有)
```

### 任务监控系统

**实时状态追踪**
- ✅ 任务创建 → 🔄 运行中 → ✅ 完成 / ❌ 失败
- 进度百分比实时计算：`Progress = (ProcessedItems / TotalItems) × 100%`
- 耗时统计：自动记录任务开始/结束时间

**错误恢复机制**
```csharp
try {
    await ProcessVideo(videoId);
} catch (RateLimitException) {
    await Delay(60000);      // 触发限制，冷却1分钟
    Retry(task);             // 重试任务
} catch (VideoUnavailable) {
    MarkAsSkipped(videoId);  // 标记为跳过
    ContinueNext();          // 继续下一个
}
```

---

## 📊 使用指南

### 快速开始

1. **启动程序**
   - 解压下载的ZIP文件
   - 双击 `YoutubeCrawlerWPF.exe`
   - 首次运行自动创建本地数据库

2. **选择采集类型**
   - 🔍 **关键词搜索**: 输入关键词如 "科技评测"
   - 📺 **频道内容采集**: 输入频道ID如 "UCxxxxxxxxxxxxxx"
   - 📂 **播放列表采集**: 输入播放列表URL
   - 📄 **URL列表采集**: 从文本文件批量导入

3. **设置采集参数**
   - 设置最大采集数量（建议50-200）
   - 点击"开始采集"按钮

4. **查看与分析数据**
   - 点击"查看数据"打开内置表格浏览器
   - 或导出为Excel进行深度分析

### 数据导出与分析

**Excel导出字段说明**
| 字段 | 分析价值 |
|-----|---------|
| VideoId | 唯一标识，可用于链接构建 |
| Title | 标题策略分析，关键词提取 |
| UploadDate | 发布时间规律，最佳发布时间 |
| ViewCount | 播放量表现，爆款识别 |
| LikeCount | 互动质量评估 |
| Tags | SEO标签策略研究 |
| Duration | 视频时长与完播率关系 |

**推荐分析维度**
1. **时间序列分析**: 观察频道内容方向演变
2. **标签云分析**: 提取高频标签发现内容焦点
3. **互动率计算**: `(LikeCount / ViewCount) × 100%`
4. **发布频率统计**: 计算平均更新间隔

---

## ⚠️ 重要说明

### 数据获取限制
- **流信息已禁用**: 为避免YouTube防护机制，本工具仅采集公开元数据（标题、描述、标签等），不提供视频下载功能
- **统计数据**: 观看数、点赞数等因YouTube API限制，可能显示为0，不影响标题、标签等核心数据分析

### 合规使用建议
```
✅ 合理使用请求间隔（内置1-5秒延迟）
✅ 单次采集数量控制在合理范围（建议<500）
✅ 用于个人学习、竞品研究、内容策划

❌ 避免高频连续请求
❌ 不得用于大规模数据爬取
❌ 遵守YouTube服务条款与相关法规
```

---

## 🛠️ 技术栈

- **.NET 6.0** - 跨平台运行时
- **WPF** - Windows原生UI框架
- **YouTubeExplode** - YouTube数据交互库
- **SQLite** - 嵌入式数据库
- **EPPlus** - Excel文件生成

---

## 📞 社区与支持

**自媒体全家桶用户群**: `1076150045`

加入QQ群获取：
- 💬 使用技巧交流
- 🐛 问题反馈与解答
- 📢 版本更新通知
- 📚 运营资料分享

---

## 📄 许可证

本项目仅供学习研究使用，请遵守YouTube服务条款及相关法律法规。

---

**Stars** ⭐ **如果这个项目对你有帮助，欢迎Star支持！**
