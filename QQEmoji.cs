using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Alife.Platform;
using Microsoft.SemanticKernel;

namespace Alife.Plugin.QQEmoji;

public enum EmojiPolicyMode
{
    Balanced,
    Conservative,
    Active
}

public record DecisionResult
{
    public bool ShouldSend { get; init; }
    public string Reason { get; init; } = "";
}

public class BqbEntry
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Url { get; set; } = "";
}

public class BqbRoot
{
    public int Status { get; set; }
    public string Info { get; set; } = "";
    public List<BqbEntry> Data { get; set; } = new();
}

public record QQEmojiConfig
{
    public string EmojiPath { get; set; } = Path.Combine(AlifePath.StorageFolderPath, "QQEmojis");
    public int AutoProbability { get; set; } = 10;
    public int CooldownSeconds { get; set; } = 30;
    public int MaxBurst { get; set; } = 1;
    public bool EnableEmotionBoost { get; set; } = true;
    public bool EnableCooldown { get; set; } = true;
    public bool EnableBurstLimit { get; set; } = true;
    public EmojiPolicyMode PolicyMode { get; set; } = EmojiPolicyMode.Balanced;

    public Dictionary<string, int> EmotionKeywords { get; set; } = new()
    {
        ["哈哈"] = 25, ["笑死"] = 30, ["好笑"] = 20, ["好耶"] = 20,
        ["开心"] = 20, ["可爱"] = 15, ["棒"] = 15, ["赞"] = 10,
        ["哭"] = 15, ["难受"] = 15, ["呜呜"] = 20, ["绷不住"] = 25,
    };

    public bool EnableAutoUpdateEmojiList { get; set; } = true;
    public int AutoUpdateThreshold { get; set; } = 5;
    public int MaxEmojiListPreview { get; set; } = 50;
    public bool EnableLogging { get; set; } = false;

    public bool EnableOnlineSearch { get; set; } = false;
    public bool EnableOnlineSearchLog { get; set; } = false;
    public int SearchResultCount { get; set; } = 5;
    public bool BqbUseCdn { get; set; } = true;

    // true=自动下载到表情包目录（永久），false=缓存到临时目录（5小时清理）
    public bool EnableOnlineImageCache { get; set; } = false;

    /// <summary>启用腾讯/搜狗实时表情搜索，AI 只传关键词即可同轮直发（推荐，默认开启）。</summary>
    public bool EnableTencentSearch { get; set; } = true;

    /// <summary>腾讯表情发送成功后，是否顺便下载保存到本地表情包库。</summary>
    public bool EnableTencentAutoSave { get; set; } = false;

    /// <summary>
    /// true=优先从 AI 调用参数取 type/targetid（出站侧，推荐）；
    /// false=优先用入站 QChat 缓存（旧逻辑）。
    /// 两侧都没有目标时软降级：只搜图返回 URL，不报「未找到会话目标」。
    /// </summary>
    public bool TencentSessionFromAi { get; set; } = true;
}

public class TencentEmojiItem
{
    public string IndexUrl { get; set; } = "";
    public string Format { get; set; } = "";
    public string ImageId { get; set; } = "";
    public int RealHeight { get; set; }
    public int RealWidth { get; set; }
    public long FileSize { get; set; }
}

public class TencentEmojiResponse
{
    public int Code { get; set; }
    public string Msg { get; set; } = "";
    public List<TencentEmojiItem>? Data { get; set; }
    public int HasMore { get; set; }
    public bool EndFlag { get; set; }
}

[Module("QQ表情包管家",
    "对AI说「存为 名字.后缀」即可保存QQ图片到本地，AI也会按策略自动发送表情包。",
    defaultCategory: "Doro的妙妙工具",
    EditorUI = typeof(QQEmojiUI))]
[Description("QQ表情包存发管理：存图+智能发图。")]
public class QQEmoji(
    XmlFunctionCaller functionService
) : InteractiveModule<QQEmoji>, IConfigurable<QQEmojiConfig>
{
    public QQEmojiConfig? Configuration { get; set; }
    static readonly HttpClient _http = new();

    DateTime _lastSendTime = DateTime.MinValue;
    int _burstCount;
    readonly object _stateLock = new();
    int _saveCountSinceLastListUpdate;

    List<BqbEntry> _bqbIndex = new();
    readonly object _bqbLock = new();
    readonly HashSet<string> _downloadedSet = new(StringComparer.OrdinalIgnoreCase);
    static DateTime _lastCacheClean = DateTime.MinValue;

    // 最近一次从入站 qchat 解析出的会话目标（动态，不写死任何号）
    string? _lastChatType;
    string? _lastTargetId;
    readonly object _chatTargetLock = new();

    const string BQB_RAW_PREFIX = "https://raw.githubusercontent.com/zhaoolee/ChineseBQB/master/";
    const string BQB_CDN_PREFIX = "https://cdn.jsdelivr.net/gh/zhaoolee/ChineseBQB@master/";
    const string TencentSearchApi = "https://h5api.sginput.qq.com/wxbq/search";
    const string PluginId = "Alife.Plugin.QQEmoji";
    const string BqbIndexFileName = "chinesebqb_index.json";
    const string CacheSubDir = ".online_cache";
    const int MaxImageBytes = 10 * 1024 * 1024;
    static readonly TimeSpan CacheTtl = TimeSpan.FromHours(5);
    // 兼容 AI 出站/文档中的 qchat 标签（属性顺序两种）
    static readonly Regex QChatTagRegex = new(
        @"<qchat\b[^>]*\btype\s*=\s*""(?<type>Private|Group)""[^>]*\btargetid\s*=\s*""(?<id>\d+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex QChatTagRegexAlt = new(
        @"<qchat\b[^>]*\btargetid\s*=\s*""(?<id>\d+)""[^>]*\btype\s*=\s*""(?<type>Private|Group)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // QChat 入站实际格式（见 OneBotSegment / QChatService）
    // 群缓冲：> 以下是群 [123456(群名)] 的消息
    static readonly Regex GroupBufferRegex = new(
        @"以下是群\s*\[(?<id>\d+)",
        RegexOptions.Compiled);
    // 群单条来源：[群聊 123456(群名), 发言人 ...]
    static readonly Regex GroupChatSourceRegex = new(
        @"\[群聊\s+(?<id>\d+)",
        RegexOptions.Compiled);
    // 私聊：[私聊][123456(昵称)]: 或 [私聊 123456(昵称)]
    static readonly Regex PrivateSpeakerRegex = new(
        @"\[私聊\]\[(?<id>\d+)",
        RegexOptions.Compiled);
    static readonly Regex PrivateSourceRegex = new(
        @"\[私聊\s+(?<id>\d+)",
        RegexOptions.Compiled);
    // ===== Alife 4.2.x QChat 新格式 =====
    // 群聊：[群聊消息(123456,群名)] 或 [群聊消息(123456)]
    static readonly Regex GroupMsgRegex = new(
        @"\[群聊消息\((?<id>\d+)",
        RegexOptions.Compiled);
    // 私聊：[私聊消息(2686267740,doro)]
    static readonly Regex PrivateMsgRegex = new(
        @"\[私聊消息\((?<id>\d+)",
        RegexOptions.Compiled);

    static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    static readonly string[] _imageExts = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    void LogOnline(string msg)
    {
        var cfg = Configuration;
        if (cfg == null || !cfg.EnableOnlineSearchLog)
            return;
        Console.WriteLine($"[QQEmoji] {msg}");
    }

    void LogGeneral(string msg)
    {
        var cfg = Configuration;
        if (cfg != null && !cfg.EnableLogging)
            return;
        Console.WriteLine($"[QQEmoji] {msg}");
    }

    static QQEmoji()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://qq.com");
    }

    static string GetBuiltinIndexPath() =>
        Path.Combine(AlifePath.StorageFolderPath, "Plugins", PluginId, BqbIndexFileName);

    static string GetTempCachePath(QQEmojiConfig cfg) =>
        Path.Combine(cfg.EmojiPath, CacheSubDir);

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        XmlHandler handler = new(this);
        functionService.RegisterHandler(handler, cancellationToken: DestroyCancellationToken);

        var cfg = Configuration ?? new QQEmojiConfig();
        EnsureEmojiPath(cfg);
        string emojiList = BuildEmojiListString(cfg.EmojiPath, cfg.MaxEmojiListPreview);

        Prompt($$"""
            你有本地表情包库，存放在：{{cfg.EmojiPath}}

            【存图】当用户说「存为xxx」时，用以下格式保存图片：
            <SaveImage name="文件名.后缀">图片URL</SaveImage>
            例如：<SaveImage name="可爱猫猫.png">https://example.com/cat.png</SaveImage>

            【发图】系统有 {{cfg.AutoProbability}}% 的基础概率允许你发一个表情包。
            当前可用表情包，你已知道所有文件名，直接发即可，无需再查询：
            {{emojiList}}

            发图格式：<qimage type="Private/Group" targetid="QQ号/群号" image="{{cfg.EmojiPath}}/文件名.后缀" />

            注意：系统决定「能否发」，你来决定「发哪个」
            """);

        if (cfg.EnableOnlineSearch)
        {
            await LoadBqbIndexAsync(cfg);

            if (cfg.EnableOnlineImageCache)
            {
                Prompt($@"【在线表情包·BQB】SearchBqbOnline 搜内置索引（关键词5字内）。
从结果中选一张：若标注[已在库中]则直接用 <qimage> 发出；否则用 <DownloadToCache url=""URL"" name=""文件名"" /> 下载后再发。");
            }
            else
            {
                Prompt($@"【在线表情包·BQB】SearchBqbOnline 搜内置索引（关键词5字内）。
从结果中选一张：若标注[已缓存]则直接用 <qimage> 发出；否则用 <DownloadToCache url=""URL"" name=""文件名"" /> 下载到缓存后再发。
缓存目录 {GetTempCachePath(cfg)}，每 5 小时和启动时自动清理。");
                CleanTempCache(cfg);
            }
        }

        if (cfg.EnableTencentSearch)
        {
            if (cfg.TencentSessionFromAi)
            {
                Prompt("""
                    【在线表情包·腾讯】需要在线表情时，只调用一次（请带上会话目标，与 qimage 字段一致）：
                    <SendTencentEmoji keyword="关键词" type="Private/Group" targetid="QQ号或群号" />
                    插件会自动搜索并直接发出图片，你无需再写 <qimage>，也不要第二轮补发。
                    若当前不在 QQ 会话、无法确定目标，仍可只传 keyword 搜图；函数会返回图片 URL，你可自行决定是否用 <qimage> 发出。
                    若函数反馈失败，请改用本地表情包库已有文件发 <qimage>。
                    """);
            }
            else
            {
                Prompt("""
                    【在线表情包·腾讯】需要在线表情时，只调用一次：
                    <SendTencentEmoji keyword="关键词" />
                    插件会自动搜索并直接发出图片，你无需再写 <qimage>，也不要第二轮补发。
                    若函数反馈失败，请改用本地表情包库已有文件发 <qimage>。
                    """);
            }
        }

        LogGeneral($"启动完成，表情包目录：{cfg.EmojiPath}，当前共 {GetImageFiles(cfg.EmojiPath).Count} 个文件");
    }

    static void CleanTempCache(QQEmojiConfig cfg)
    {
        var cacheDir = GetTempCachePath(cfg);
        if (!Directory.Exists(cacheDir)) return;

        try
        {
            var threshold = DateTime.UtcNow - CacheTtl;
            foreach (var file in Directory.GetFiles(cacheDir))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < threshold)
                        File.Delete(file);
                }
                catch { }
            }
        }
        catch { }
        _lastCacheClean = DateTime.UtcNow;
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);

        ChatBot.ChatSend -= OnChatSend;
        ChatBot.ChatSend += OnChatSend;
    }

    public override Task DestroyAsync()
    {
        ChatBot.ChatSend -= OnChatSend;
        return base.DestroyAsync();
    }

    string OnChatSend(string msg)
    {
        // 4.2.x 格式：[消息来源(QChatService)]；旧格式：消息来源:[QChatService]
        if (!msg.Contains("[消息来源(QChatService)]") &&
            !msg.Contains("消息来源:[QChatService]")) return msg;

        // 始终尝试解析当次会话目标，供腾讯源同轮直发使用
        TryParseChatTarget(msg);

        var cfg = Configuration;
        if (cfg == null) return msg;

        lock (_stateLock)
        {
            double elapsed = (DateTime.UtcNow - _lastSendTime).TotalSeconds;
            if (elapsed >= cfg.CooldownSeconds)
            {
                if (_burstCount != 0)
                    LogGeneral($"爆率计数器复位，距上次发送 {elapsed:F1} 秒");
                _burstCount = 0;
            }
        }

        var decision = Decide(msg, cfg);
        LogGeneral($"决策结果：{decision.ShouldSend}，原因：{decision.Reason}");
        if (!decision.ShouldSend) return msg;

        lock (_stateLock)
        {
            _lastSendTime = DateTime.UtcNow;
            _burstCount++;
        }

        LogGeneral("已向消息注入表情包提示");

        var tips = new List<string> { "可以选个表情包附末尾" };
        if (cfg.EnableTencentSearch)
        {
            if (cfg.TencentSessionFromAi)
                tips.Add("本地没有合适的可用 <SendTencentEmoji keyword=\"关键词\" type=\"Private/Group\" targetid=\"QQ号或群号\" />，调用后图已直接发出，勿再写 qimage");
            else
                tips.Add("本地没有合适的可用 <SendTencentEmoji keyword=\"关键词\" />，调用后图已直接发出，勿再写 qimage");
        }
        if (cfg.EnableOnlineSearch)
            tips.Add("也可用 SearchBqbOnline 搜内置索引（关键词5字内），再 DownloadToCache 后 qimage 发送");

        tips.Add("若下一轮没有此提示，则说明系统未允许发表情包，不要擅作主张发出表情");
        return msg + "\n\n[系统提示：" + string.Join("。", tips) + "]";
    }

    void TryParseChatTarget(string msg)
    {
        string? type = null;
        string? id = null;

        // 1) 显式 qchat 标签（若存在）
        var m = QChatTagRegex.Match(msg);
        if (!m.Success)
            m = QChatTagRegexAlt.Match(msg);
        if (m.Success)
        {
            type = m.Groups["type"].Value;
            id = m.Groups["id"].Value;
        }

        // 2) QChat 入站群聊缓冲 / 来源标签（优先群，避免误把发言人当目标）
        if (type == null)
        {
            // 4.2.x：[群聊消息(群号,群名)]
            m = GroupMsgRegex.Match(msg);
            if (!m.Success)
                m = GroupBufferRegex.Match(msg);
            if (!m.Success)
                m = GroupChatSourceRegex.Match(msg);
            if (m.Success)
            {
                type = "Group";
                id = m.Groups["id"].Value;
            }
        }

        // 3) QChat 入站私聊
        if (type == null)
        {
            // 4.2.x：[私聊消息(QQ号,昵称)]
            m = PrivateMsgRegex.Match(msg);
            if (!m.Success)
                m = PrivateSpeakerRegex.Match(msg);
            if (!m.Success)
                m = PrivateSourceRegex.Match(msg);
            if (m.Success)
            {
                type = "Private";
                id = m.Groups["id"].Value;
            }
        }

        if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(id))
            return;

        lock (_chatTargetLock)
        {
            _lastChatType = type;
            _lastTargetId = id;
        }
        LogOnline($"已解析会话目标 type={type} targetid={id}");
    }

    bool TryGetChatTarget(out string type, out string targetId)
    {
        lock (_chatTargetLock)
        {
            type = _lastChatType ?? "";
            targetId = _lastTargetId ?? "";
            return !string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(targetId);
        }
    }

    /// <summary>
    /// 规范化 AI 传入的 type/targetid；合法则返回 true。
    /// type 接受 Private/Group（大小写不敏感），targetid 须为纯数字。
    /// </summary>
    static bool TryNormalizeAiTarget(string? type, string? targetId, out string chatType, out string id)
    {
        chatType = "";
        id = "";
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(targetId))
            return false;

        var t = type.Trim();
        var tid = targetId.Trim();
        if (tid.Length == 0 || !tid.All(char.IsDigit))
            return false;

        if (t.Equals("Private", StringComparison.OrdinalIgnoreCase))
            chatType = "Private";
        else if (t.Equals("Group", StringComparison.OrdinalIgnoreCase))
            chatType = "Group";
        else
            return false;

        id = tid;
        return true;
    }

    /// <summary>
    /// 按开关解析会话目标：AI 参数优先或入站缓存优先，另一侧作回退。
    /// </summary>
    bool ResolveChatTarget(string? aiType, string? aiTargetId, out string chatType, out string targetId)
    {
        chatType = "";
        targetId = "";
        var fromAi = Configuration?.TencentSessionFromAi ?? true;
        var aiOk = TryNormalizeAiTarget(aiType, aiTargetId, out var aType, out var aId);
        var cacheOk = TryGetChatTarget(out var cType, out var cId);

        if (fromAi)
        {
            if (aiOk)
            {
                chatType = aType;
                targetId = aId;
                return true;
            }
            if (cacheOk)
            {
                chatType = cType;
                targetId = cId;
                return true;
            }
        }
        else
        {
            if (cacheOk)
            {
                chatType = cType;
                targetId = cId;
                return true;
            }
            if (aiOk)
            {
                chatType = aType;
                targetId = aId;
                return true;
            }
        }

        return false;
    }

    DecisionResult Decide(string inputText, QQEmojiConfig cfg)
    {
        int p = GetBaseProbability(cfg);

        if (cfg.EnableCooldown)
        {
            double seconds;
            lock (_stateLock) { seconds = (DateTime.UtcNow - _lastSendTime).TotalSeconds; }
            if (seconds < cfg.CooldownSeconds)
                return new() { ShouldSend = false, Reason = "冷却中" };
        }

        if (cfg.EnableBurstLimit)
        {
            int burst;
            lock (_stateLock) { burst = _burstCount; }
            if (burst >= cfg.MaxBurst)
                return new() { ShouldSend = false, Reason = "已达连发上限" };
        }

        if (cfg.EnableEmotionBoost && !string.IsNullOrWhiteSpace(inputText))
            p += GetEmotionBoost(cfg, inputText);

        p = Math.Clamp(p, 0, 100);

        bool ok = RandomNumberGenerator.GetInt32(100) < p;
        return new() { ShouldSend = ok, Reason = $"概率={p}%" };
    }

    int GetBaseProbability(QQEmojiConfig cfg) => cfg.PolicyMode switch
    {
        EmojiPolicyMode.Conservative => cfg.AutoProbability / 2,
        EmojiPolicyMode.Active       => Math.Min(100, cfg.AutoProbability + 20),
        _                            => cfg.AutoProbability,
    };

    int GetEmotionBoost(QQEmojiConfig cfg, string text)
    {
        int boost = 0;
        foreach (var kv in cfg.EmotionKeywords)
        {
            if (text.Contains(kv.Key))
                boost += kv.Value;
        }
        return boost;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("保存一张图片到本地表情包文件夹。传入图片URL。")]
    public async Task SaveImage(
        [Description("图片下载地址")] string source,
        [Description("保存的文件名，如 cute.gif")] string name)
    {
        try
        {
            var cfg = Configuration;
            if (cfg == null) return;

            if (string.IsNullOrWhiteSpace(source) ||
                !Uri.TryCreate(source.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Poke("❌ 保存失败: 无效的图片 URL");
                return;
            }

            var dir = cfg.EmojiPath;
            Directory.CreateDirectory(dir);

            name = Path.GetFileName(name);
            if (string.IsNullOrEmpty(name)) return;

            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (!_imageExts.Contains(ext))
            {
                Poke("❌ 保存失败: 不支持的图片后缀");
                return;
            }

            var dest = Path.Combine(dir, name);

            if (File.Exists(dest))
            {
                var n = Path.GetFileNameWithoutExtension(name);
                var e = Path.GetExtension(name);
                for (int i = 2; ; i++)
                {
                    dest = Path.Combine(dir, $"{n}_{i}{e}");
                    if (!File.Exists(dest)) break;
                }
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > MaxImageBytes)
            {
                Poke($"❌ 保存失败: 图片过大（>{MaxImageBytes / 1024 / 1024}MB）");
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            if (bytes.Length == 0)
            {
                Poke("❌ 保存失败: 图片内容为空");
                return;
            }
            if (bytes.Length > MaxImageBytes)
            {
                Poke($"❌ 保存失败: 图片过大（>{MaxImageBytes / 1024 / 1024}MB）");
                return;
            }

            await File.WriteAllBytesAsync(dest, bytes);

            Poke($"✅ 已保存: {Path.GetFileName(dest)}");
            LogGeneral($"图片已保存：{Path.GetFileName(dest)}（{bytes.Length} bytes）");

            if (cfg.EnableAutoUpdateEmojiList)
            {
                _saveCountSinceLastListUpdate++;
                LogGeneral($"存图计数器：{_saveCountSinceLastListUpdate}/{cfg.AutoUpdateThreshold}");
                if (_saveCountSinceLastListUpdate >= cfg.AutoUpdateThreshold)
                {
                    _saveCountSinceLastListUpdate = 0;
                    string newList = BuildEmojiListString(dir, cfg.MaxEmojiListPreview);
                    Prompt($"📢 表情包列表已更新（存了 {cfg.AutoUpdateThreshold} 个新图后自动刷新）：\n{newList}");
                    LogGeneral($"已达阈值 {cfg.AutoUpdateThreshold}，已向 AI 推送最新表情包列表");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Poke("❌ 保存失败: 下载超时（超过 1 分钟），请稍后重试");
        }
        catch (Exception ex)
        {
            Poke($"❌ 保存失败: {ex.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("列出本地表情包库中所有可用的表情包文件名。")]
    public void ListEmojis()
    {
        var cfg = Configuration;
        if (cfg == null) return;

        EnsureEmojiPath(cfg);
        var dir = cfg.EmojiPath;
        if (!Directory.Exists(dir))
        {
            Poke("📭 表情包库为空。");
            return;
        }

        var files = GetImageFiles(dir)
            .Select(Path.GetFileName)
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
        {
            Poke("📭 表情包库为空。");
            return;
        }

        var list = string.Join("\n", files.Select((f, i) => $"  {i + 1}. {f}"));
        Poke($"📷 共有 {files.Count} 个表情包：\n{list}");
    }

    // ===== 在线搜图 =====

    async Task LoadBqbIndexAsync(QQEmojiConfig cfg)
    {
        string indexPath = GetBuiltinIndexPath();
        if (!File.Exists(indexPath))
        {
            lock (_bqbLock) { _bqbIndex = new List<BqbEntry>(); }
            LogOnline($"内置索引不存在：{indexPath}");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(indexPath);
            var root = JsonSerializer.Deserialize<BqbRoot>(json, _jsonOpts);
            var list = root?.Data?
                .Where(e => _imageExts.Contains(Path.GetExtension(e.Name).ToLowerInvariant()))
                .ToList() ?? new List<BqbEntry>();

            lock (_bqbLock) { _bqbIndex = list; }
            LogOnline($"内置表情包索引已加载（{list.Count} 条）");
        }
        catch (Exception ex)
        {
            lock (_bqbLock) { _bqbIndex = new List<BqbEntry>(); }
            LogOnline($"内置索引加载失败: {ex.Message}");
        }
    }

    static string ConvertBqbUrl(string rawUrl, QQEmojiConfig cfg)
    {
        return cfg.BqbUseCdn
            ? rawUrl.Replace(BQB_RAW_PREFIX, BQB_CDN_PREFIX)
            : rawUrl;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("从在线搜索结果中下载指定图片到本地，返回本地路径。下载成功后直接用 <qimage> 发送。")]
    public async Task DownloadToCache(
        [Description("图片下载地址（来自 SearchBqbOnline 返回的 URL）")] string url,
        [Description("保存的文件名（来自 SearchBqbOnline 返回的名称）")] string name)
    {
        var cfg = Configuration;
        if (cfg == null) return;

        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Poke("❌ 下载失败: 无效的图片 URL");
            return;
        }

        name = Path.GetFileName(name);
        if (string.IsNullOrEmpty(name)) return;

        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (!_imageExts.Contains(ext)) return;

        // 开关 ON → 存到表情包目录；开关 OFF → 存到缓存目录
        var targetDir = cfg.EnableOnlineImageCache ? cfg.EmojiPath : GetTempCachePath(cfg);
        Directory.CreateDirectory(targetDir);

        var dest = Path.Combine(targetDir, name);
        if (File.Exists(dest))
        {
            Poke($"✅ 已存在: {dest}");
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > MaxImageBytes)
            {
                Poke($"❌ 下载失败: 图片过大（>{MaxImageBytes / 1024 / 1024}MB）");
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            if (bytes.Length == 0 || bytes.Length > MaxImageBytes)
            {
                Poke("❌ 下载失败: 图片内容为空或过大");
                return;
            }

            await File.WriteAllBytesAsync(dest, bytes);
            Poke($"✅ 已缓存: {dest}");
            LogOnline($"DownloadToCache 成功：{name} → {dest}");
        }
        catch (OperationCanceledException)
        {
            Poke("❌ 下载超时，请稍后重试或换一张图");
        }
        catch (Exception ex)
        {
            Poke($"❌ 下载失败: {ex.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("搜索 ChineseBQB 在线表情包库。返回结果含名称和下载URL。从中选一张后用 DownloadToCache 下载到本地，再用 <qimage> 发出。若标注[已缓存]则无需再次下载。")]
    public void SearchBqbOnline(
        [Description("搜索关键词（精简到5字以内，可用空格分隔多个关键词）")] string keyword)
    {
        var cfg = Configuration;
        if (cfg == null || !cfg.EnableOnlineSearch)
        {
            Poke("在线搜图功能未开启");
            return;
        }

        List<BqbEntry> index;
        lock (_bqbLock) { index = _bqbIndex; }

        if (index.Count == 0)
        {
            string indexPath = GetBuiltinIndexPath();
            if (File.Exists(indexPath))
            {
                try
                {
                    var json = File.ReadAllText(indexPath);
                    var root = JsonSerializer.Deserialize<BqbRoot>(json, _jsonOpts);
                    index = root?.Data?
                        .Where(e => _imageExts.Contains(Path.GetExtension(e.Name).ToLowerInvariant()))
                        .ToList() ?? new List<BqbEntry>();
                    lock (_bqbLock) { _bqbIndex = index; }
                }
                catch { }
            }
        }

        if (index.Count == 0)
        {
            Poke("在线表情包索引未就绪，用本地库的就好");
            return;
        }

        if (string.IsNullOrWhiteSpace(keyword))
        {
            Poke("请提供搜索关键词");
            return;
        }

        var keywords = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results = index
            .Where(e => keywords.All(k =>
                e.Name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                e.Category.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Take(cfg.SearchResultCount)
            .ToList();

        if (results.Count == 0)
        {
            Poke("在线没搜到，用本地库的就好");
            return;
        }

        var lines = new List<string>(results.Count);
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var url = ConvertBqbUrl(r.Url, cfg);
            var line = $"{i + 1}. {r.Name}\n   {url}";

            if (cfg.EnableOnlineImageCache)
            {
                var localPath = Path.Combine(cfg.EmojiPath, r.Name);
                if (File.Exists(localPath))
                    line += $"\n   [已在库中] {localPath}";
            }
            else
            {
                var cachedPath = Path.Combine(GetTempCachePath(cfg), r.Name);
                if (File.Exists(cachedPath))
                    line += $"\n   [已缓存] {cachedPath}";
            }

            lines.Add(line);
        }

        string pokeMsg;
        if (cfg.EnableOnlineImageCache)
        {
            pokeMsg = $"搜到{results.Count}个：\n{string.Join("\n", lines)}\n" +
                $"从上面选一张，若标注[已在库中]则直接用 <qimage> 发出；否则用 <DownloadToCache url=\"选中的URL\" name=\"选中的文件名\" /> 下载后发送。";
        }
        else
        {
            pokeMsg = $"搜到{results.Count}个：\n{string.Join("\n", lines)}\n" +
                $"从上面选一张，若标注[已缓存]则直接用 <qimage> 发出；否则用 <DownloadToCache url=\"选中的URL\" name=\"选中的文件名\" /> 下载后，再用 <qimage> 发送缓存返回的路径。";
        }

        Poke(pokeMsg);
        LogOnline($"在线搜图「{keyword}」→ {results.Count} 个结果");

        // 开关 ON 时后台预下载全部到表情包目录，加速下次使用
        if (cfg.EnableOnlineImageCache)
        {
            #pragma warning disable CS4014
            Task.Run(async () => await DownloadToEmojiPathAsync(results, cfg));
            #pragma warning restore CS4014
        }
        else
        {
            if (DateTime.UtcNow - _lastCacheClean > CacheTtl)
                CleanTempCache(cfg);
        }
    }

    // 自动下载到表情包目录（永久）
    async Task DownloadToEmojiPathAsync(List<BqbEntry> results, QQEmojiConfig cfg)
    {
        try
        {
            var dir = cfg.EmojiPath;
            Directory.CreateDirectory(dir);
            var sem = new System.Threading.SemaphoreSlim(3);

            var tasks = results.Select(async r =>
            {
                var name = Path.GetFileName(r.Name);
                if (string.IsNullOrEmpty(name)) return;
                var ext = Path.GetExtension(name).ToLowerInvariant();
                if (!_imageExts.Contains(ext)) return;

                var dest = Path.Combine(dir, name);
                lock (_downloadedSet) { if (_downloadedSet.Contains(name)) return; }

                await sem.WaitAsync();
                try
                {
                    if (File.Exists(dest)) return;

                    var url = ConvertBqbUrl(r.Url, cfg);
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                        return;

                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                    using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    if (!response.IsSuccessStatusCode) return;

                    var contentLength = response.Content.Headers.ContentLength;
                    if (contentLength is > MaxImageBytes) return;

                    var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                    if (bytes.Length == 0 || bytes.Length > MaxImageBytes) return;

                    await File.WriteAllBytesAsync(dest, bytes);
                    lock (_downloadedSet) { _downloadedSet.Add(name); }
                    LogOnline($"已下载到表情包目录：{name}");
                }
                catch { }
                finally { sem.Release(); }
            });

            await Task.WhenAll(tasks);
        }
        catch { }
    }

    // 下载到临时缓存（5小时清理）
    async Task DownloadToCacheAsync(List<BqbEntry> results, QQEmojiConfig cfg)
    {
        try
        {
            var cacheDir = GetTempCachePath(cfg);
            Directory.CreateDirectory(cacheDir);
            var sem = new System.Threading.SemaphoreSlim(3);

            var tasks = results.Select(async r =>
            {
                var name = Path.GetFileName(r.Name);
                if (string.IsNullOrEmpty(name)) return;
                var ext = Path.GetExtension(name).ToLowerInvariant();
                if (!_imageExts.Contains(ext)) return;

                var dest = Path.Combine(cacheDir, name);
                if (File.Exists(dest)) return;

                await sem.WaitAsync();
                try
                {
                    var url = ConvertBqbUrl(r.Url, cfg);
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                        return;

                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                    using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    if (!response.IsSuccessStatusCode) return;

                    var contentLength = response.Content.Headers.ContentLength;
                    if (contentLength is > MaxImageBytes) return;

                    var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                    if (bytes.Length == 0 || bytes.Length > MaxImageBytes) return;

                    await File.WriteAllBytesAsync(dest, bytes);
                    LogOnline($"已缓存：{name}");
                }
                catch { }
                finally { sem.Release(); }
            });

            await Task.WhenAll(tasks);
        }
        catch { }
    }

    // ===== 腾讯/搜狗实时表情 =====

    [XmlFunction(FunctionMode.OneShot)]
    [Description("按关键词搜索腾讯在线表情并直接发送到当前会话。推荐同时传 type/targetid（与 qimage 一致）。无会话目标时仅返回图片 URL，不报错。失败时请改用本地表情包库。")]
    public async Task SendTencentEmoji(
        [Description("表情关键词，如：开心、奶龙、滑稽")] string keyword,
        [Description("会话类型：Private 或 Group（推荐与 qimage 一致）")] string? type = null,
        [Description("QQ号或群号（推荐与 qimage 一致）")] string? targetid = null)
    {
        var cfg = Configuration;
        if (cfg == null || !cfg.EnableTencentSearch)
        {
            Poke("腾讯在线表情未开启，请用本地库已有文件发 qimage");
            return;
        }

        if (string.IsNullOrWhiteSpace(keyword))
        {
            Poke("请提供搜索关键词，或改用本地库已有文件发 qimage");
            return;
        }

        keyword = keyword.Trim();
        if (keyword.Length > 20)
            keyword = keyword[..20];

        var hasTarget = ResolveChatTarget(type, targetid, out var chatType, out var targetId);
        if (!hasTarget)
            LogOnline("SendTencentEmoji：无会话目标，将软降级为仅搜图返回 URL");

        try
        {
            var items = await SearchTencentApiAsync(keyword, page: 1, num: Math.Max(5, cfg.SearchResultCount));
            if (items.Count == 0)
            {
                Poke($"腾讯表情未搜到「{keyword}」，请改用本地库已有文件发 qimage");
                return;
            }

            // 不做尺寸/体积过滤；轻微打散，避免总发同一张
            var pick = items[RandomNumberGenerator.GetInt32(Math.Min(items.Count, Math.Max(1, cfg.SearchResultCount)))];
            if (string.IsNullOrWhiteSpace(pick.IndexUrl) ||
                !Uri.TryCreate(pick.IndexUrl.Trim(), UriKind.Absolute, out var imageUri) ||
                (imageUri.Scheme != Uri.UriSchemeHttp && imageUri.Scheme != Uri.UriSchemeHttps))
            {
                Poke("腾讯表情返回无效链接，请改用本地库已有文件发 qimage");
                return;
            }

            var imageUrl = pick.IndexUrl.Trim();
            LogOnline($"SendTencentEmoji「{keyword}」→ {imageUrl}");

            // 非 QQ 环境 / 无目标：软降级，只返回 URL，不硬失败
            if (!hasTarget)
            {
                Poke($"已搜到表情（当前无会话目标，未直发）：{imageUrl}。若在 QQ 会话中，请带 type/targetid 再调；或自行用 <qimage type=\"Private/Group\" targetid=\"号\" image=\"{imageUrl}\" /> 发出；也可改用本地库。");
                return;
            }

            // 同轮手动调用 QChat 的 qimage，AI 无需再写标签
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = chatType,
                ["targetid"] = targetId,
                ["image"] = imageUrl,
            };

            await functionService.HandlerTable.Handle("qimage", new XmlContext
            {
                CallMode = CallMode.OneShot,
                Parameters = parameters,
            });

            LogOnline($"已同轮直发 qimage type={chatType} targetid={targetId}");

            if (cfg.EnableTencentAutoSave)
            {
                var saveName = BuildTencentSaveName(pick);
#pragma warning disable CS4014
                Task.Run(async () => await SaveTencentToLibraryAsync(imageUrl, saveName, cfg));
#pragma warning restore CS4014
            }
        }
        catch (Exception ex)
        {
            Poke($"腾讯表情发送失败: {ex.Message}，请改用本地库已有文件发 qimage");
            LogOnline($"SendTencentEmoji 异常: {ex.Message}");
        }
    }

    async Task<List<TencentEmojiItem>> SearchTencentApiAsync(string keyword, int page, int num)
    {
        var url = $"{TencentSearchApi}?key={Uri.EscapeDataString(keyword)}&page={page}&num={num}";

        // 超时重试 1 次
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var response = await _http.GetAsync(url, cts.Token);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                var root = JsonSerializer.Deserialize<TencentEmojiResponse>(json, _jsonOpts);
                if (root == null || root.Code != 0)
                {
                    LogOnline($"腾讯 API 返回 code={root?.Code} msg={root?.Msg}");
                    return new List<TencentEmojiItem>();
                }

                return root.Data?
                    .Where(x => !string.IsNullOrWhiteSpace(x.IndexUrl))
                    .ToList() ?? new List<TencentEmojiItem>();
            }
            catch (OperationCanceledException) when (attempt == 0)
            {
                LogOnline("腾讯 API 超时，重试一次");
            }
            catch (Exception ex) when (attempt == 0)
            {
                LogOnline($"腾讯 API 请求失败，重试一次: {ex.Message}");
            }
        }

        return new List<TencentEmojiItem>();
    }

    static string BuildTencentSaveName(TencentEmojiItem item)
    {
        var format = (item.Format ?? "").Trim().ToLowerInvariant();
        if (format is not ("gif" or "png" or "jpg" or "jpeg" or "webp"))
        {
            // 从 URL 猜后缀
            try
            {
                var path = new Uri(item.IndexUrl).AbsolutePath;
                var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                format = ext is "gif" or "png" or "jpg" or "jpeg" or "webp" ? ext : "gif";
            }
            catch
            {
                format = "gif";
            }
        }

        var id = string.IsNullOrWhiteSpace(item.ImageId)
            ? Guid.NewGuid().ToString("N")[..12]
            : Regex.Replace(item.ImageId, @"[^\w\-]", "");
        if (string.IsNullOrEmpty(id))
            id = Guid.NewGuid().ToString("N")[..12];

        return $"tencent_{id}.{format}";
    }

    async Task SaveTencentToLibraryAsync(string imageUrl, string fileName, QQEmojiConfig cfg)
    {
        try
        {
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return;

            fileName = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(fileName)) return;

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (!_imageExts.Contains(ext)) return;

            var dir = cfg.EmojiPath;
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, fileName);
            if (File.Exists(dest)) return;

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode) return;

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > MaxImageBytes) return;

            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            if (bytes.Length == 0 || bytes.Length > MaxImageBytes) return;

            await File.WriteAllBytesAsync(dest, bytes);
            LogOnline($"腾讯表情已入库：{fileName}");

            if (cfg.EnableAutoUpdateEmojiList)
            {
                bool shouldRefresh = false;
                lock (_stateLock)
                {
                    _saveCountSinceLastListUpdate++;
                    if (_saveCountSinceLastListUpdate >= cfg.AutoUpdateThreshold)
                    {
                        _saveCountSinceLastListUpdate = 0;
                        shouldRefresh = true;
                    }
                }
                if (shouldRefresh)
                {
                    string newList = BuildEmojiListString(dir, cfg.MaxEmojiListPreview);
                    Prompt($"📢 表情包列表已更新（含腾讯入库）：\n{newList}");
                }
            }
        }
        catch (Exception ex)
        {
            LogOnline($"腾讯表情入库失败: {ex.Message}");
        }
    }

    // ===== 工具方法 =====

    /// <summary>表情包目录不存在时自动创建，避免首次启用报错。</summary>
    static void EnsureEmojiPath(QQEmojiConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.EmojiPath))
            cfg.EmojiPath = Path.Combine(AlifePath.StorageFolderPath, "QQEmojis");

        try
        {
            Directory.CreateDirectory(cfg.EmojiPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[QQEmoji] 创建表情包目录失败：{cfg.EmojiPath}，{ex.Message}");
        }
    }

    static List<string> GetImageFiles(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
            return new List<string>();

        try
        {
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch
        {
            return new List<string>();
        }

        if (!Directory.Exists(dir))
            return new List<string>();

        return Directory.GetFiles(dir)
            .Where(f => _imageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
    }

    string BuildEmojiListString(string dir, int maxPreview = 50)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch { }

        if (!Directory.Exists(dir)) return "(空)";

        var files = GetImageFiles(dir)
            .Select(Path.GetFileName)
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0) return "(空)";

        LogGeneral($"扫描目录：共 {files.Count} 个表情包，预览上限 {maxPreview}");

        var lines = maxPreview > 0 ? files.Take(maxPreview).Select(f => $"  - {f}")
                                   : files.Select(f => $"  - {f}");
        var result = string.Join("\n", lines);
        if (maxPreview > 0 && files.Count > maxPreview)
            result += $"\n  ... 共 {files.Count} 个，如需完整列表请调用 ListEmojis";

        return result;
    }
}
