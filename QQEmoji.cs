using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        ["?"] = 5, ["!"] = 5,
    };

    public bool EnableAutoUpdateEmojiList { get; set; } = true;
    public int AutoUpdateThreshold { get; set; } = 5;
    public int MaxEmojiListPreview { get; set; } = 50;
    public bool EnableLogging { get; set; } = false;

    public bool EnableOnlineSearch { get; set; } = false;
    public bool EnableOnlineSearchLog { get; set; } = false;
    public int SearchCacheDays { get; set; } = 7;
    public int SearchResultCount { get; set; } = 5;
    public bool BqbUseCdn { get; set; } = true;
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
    static readonly HttpClient _bqbHttp = new() { Timeout = TimeSpan.FromMinutes(2) };

    DateTime _lastSendTime = DateTime.MinValue;
    int _burstCount;
    readonly object _stateLock = new();
    int _saveCountSinceLastListUpdate;

    List<BqbEntry> _bqbIndex = new();
    DateTime _bqbIndexLoadTime = DateTime.MinValue;

    const string BQB_INDEX_URL_CDN = "https://cdn.jsdelivr.net/gh/zhaoolee/ChineseBQB@master/chinesebqb_github.json";
    const string BQB_INDEX_URL_RAW = "https://raw.githubusercontent.com/zhaoolee/ChineseBQB/master/chinesebqb_github.json";
    const string BQB_RAW_PREFIX = "https://raw.githubusercontent.com/zhaoolee/ChineseBQB/master/";
    const string BQB_CDN_PREFIX = "https://cdn.jsdelivr.net/gh/zhaoolee/ChineseBQB@master/";

    static string GetBqbIndexUrl(QQEmojiConfig cfg) => cfg.BqbUseCdn ? BQB_INDEX_URL_CDN : BQB_INDEX_URL_RAW;

    static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    static readonly string[] _imageExts = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    void Log(string msg)
    {
        Console.WriteLine($"[QQEmoji] {msg}");
    }

    static QQEmoji()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://qq.com");
        _bqbHttp.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        XmlHandler handler = new(this);
        functionService.RegisterHandler(handler);

        var cfg = Configuration ?? new QQEmojiConfig();
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
            Prompt("【搜在线表情包】若本地没有符合当前语境的，可调用 SearchBqbOnline 搜在线图（关键词5字内，可用空格分隔多个词）。搜到结果后从中选一个，用 SaveImage 下载到本地，再用 <qimage> 发出。");
            _ = RefreshBqbCacheAsync(cfg);
        }

        Log($"启动完成，表情包目录：{cfg.EmojiPath}，当前共 {GetImageFiles(cfg.EmojiPath).Count} 个文件");
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

    // AI 回复前预处理：检测 QQ 聊天消息，决策是否触发，在消息末尾注入提示让 AI 自主选表情包
    string OnChatSend(string msg)
    {
        if (!msg.Contains("消息来源:[QChatService]")) return msg;

        var cfg = Configuration;
        if (cfg == null) return msg;

        lock (_stateLock)
        {
            double elapsed = (DateTime.UtcNow - _lastSendTime).TotalSeconds;
            if (elapsed >= cfg.CooldownSeconds)
            {
                if (_burstCount != 0)
                    Log($"爆率计数器复位，距上次发送 {elapsed:F1} 秒");
                _burstCount = 0;
            }
        }

        var decision = Decide(msg, cfg);
        Log($"决策结果：{decision.ShouldSend}，原因：{decision.Reason}");
        if (!decision.ShouldSend) return msg;

        lock (_stateLock)
        {
            _lastSendTime = DateTime.UtcNow;
            _burstCount++;
        }

        Log("已向消息注入表情包提示");
        if (cfg.EnableOnlineSearch)
        {
            return msg + "\n\n[系统提示：可以选个表情包附末尾。若列表没有符合当前语境的表情包，可用 SearchBqbOnline 搜在线表情包（关键词5字内），搜到后用 SaveImage 下载到本地再用 <qimage> 发出。\n若下一轮没有此提示，则说明系统未允许发表情包，不发送表情包]";
        }
        else
        {
            return msg + "\n\n[系统提示：现在可以选一个表情包附在回复末尾。若下一轮没有此提示，表示系统未允许你发表情包，不要擅作主张发出<qimage>标签]";
        }
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

            var dir = cfg.EmojiPath;
            Directory.CreateDirectory(dir);

            // 防路径穿越
            name = Path.GetFileName(name);
            if (string.IsNullOrEmpty(name)) return;

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

            var bytes = await _http.GetByteArrayAsync(source);
            await File.WriteAllBytesAsync(dest, bytes);

            Poke($"✅ 已保存: {Path.GetFileName(dest)}");
            Log($"图片已保存：{Path.GetFileName(dest)}（{bytes.Length} bytes）");

            // 存图后自动更新表情包列表
            if (cfg.EnableAutoUpdateEmojiList)
            {
                _saveCountSinceLastListUpdate++;
                Log($"存图计数器：{_saveCountSinceLastListUpdate}/{cfg.AutoUpdateThreshold}");
                if (_saveCountSinceLastListUpdate >= cfg.AutoUpdateThreshold)
                {
                    _saveCountSinceLastListUpdate = 0;
                    string newList = BuildEmojiListString(dir, cfg.MaxEmojiListPreview);
                    Prompt($"📢 表情包列表已更新（存了 {cfg.AutoUpdateThreshold} 个新图后自动刷新）：\n{newList}");
                    Log($"已达阈值 {cfg.AutoUpdateThreshold}，已向 AI 推送最新表情包列表");
                }
            }
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

    async Task RefreshBqbCacheAsync(QQEmojiConfig cfg)
    {
        try
        {
            string cachePath = Path.Combine(cfg.EmojiPath, "chinesebqb_index.json");
            bool needDownload = true;

            if (File.Exists(cachePath))
            {
                var lastWrite = File.GetLastWriteTimeUtc(cachePath);
                if ((DateTime.UtcNow - lastWrite).TotalDays < cfg.SearchCacheDays)
                    needDownload = false;
            }

            if (needDownload)
            {
                if (cfg.EnableOnlineSearchLog)
                    Log("正在更新在线表情包索引...");
                string json = null!;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        var bytes = await _bqbHttp.GetByteArrayAsync(GetBqbIndexUrl(cfg));
                        json = Encoding.UTF8.GetString(bytes);
                        break;
                    }
                    catch (Exception ex) when (attempt == 0)
                    {
                        if (cfg.EnableOnlineSearchLog)
                            Log($"索引下载第1次失败，重试中... ({ex.Message})");
                        await Task.Delay(2000);
                    }
                }
                if (json != null)
                {
                    Directory.CreateDirectory(cfg.EmojiPath);
                    await File.WriteAllTextAsync(cachePath, json);
                    if (cfg.EnableOnlineSearchLog)
                        Log("在线表情包索引已更新");
                }
                else
                {
                    if (cfg.EnableOnlineSearchLog)
                        Log("在线表情包索引下载失败，尝试使用已有缓存");
                }
            }

            await LoadBqbIndexAsync(cachePath, cfg);
        }
        catch (Exception ex)
        {
            if (cfg.EnableOnlineSearchLog)
                Log($"在线表情包索引更新失败: {ex.Message}");
            string cachePath = Path.Combine(cfg.EmojiPath, "chinesebqb_index.json");
            if (File.Exists(cachePath))
                await LoadBqbIndexAsync(cachePath, cfg);
        }
    }

    async Task LoadBqbIndexAsync(string cachePath, QQEmojiConfig cfg)
    {
        if (!File.Exists(cachePath))
        {
            _bqbIndex = new List<BqbEntry>();
            return;
        }

        var json = await File.ReadAllTextAsync(cachePath);
        var root = JsonSerializer.Deserialize<BqbRoot>(json, _jsonOpts);
        _bqbIndex = root?.Data?
            .Where(e => _imageExts.Contains(Path.GetExtension(e.Name).ToLower()))
            .ToList() ?? new List<BqbEntry>();
        _bqbIndexLoadTime = DateTime.UtcNow;
        if (cfg.EnableOnlineSearchLog)
            Log($"在线表情包索引已加载（{_bqbIndex.Count} 条）");
    }

    static string ConvertBqbUrl(string rawUrl, QQEmojiConfig cfg)
    {
        return cfg.BqbUseCdn
            ? rawUrl.Replace(BQB_RAW_PREFIX, BQB_CDN_PREFIX)
            : rawUrl;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("用关键词搜索 ChineseBQB 在线表情包库，返回结果（含名称和URL）。从结果中选一个后用 SaveImage 下载到本地，再用 <qimage> 发出。")]
    public void SearchBqbOnline(
        [Description("搜索关键词（精简到5字以内，可用空格分隔多个关键词）")] string keyword)
    {
        var cfg = Configuration;
        if (cfg == null || !cfg.EnableOnlineSearch)
        {
            Poke("在线搜图功能未开启");
            return;
        }

        if (_bqbIndex.Count == 0)
        {
            string cachePath = Path.Combine(cfg.EmojiPath, "chinesebqb_index.json");
            if (File.Exists(cachePath))
            {
                try
                {
                    var json = File.ReadAllText(cachePath);
                    var root = JsonSerializer.Deserialize<BqbRoot>(json, _jsonOpts);
                    _bqbIndex = root?.Data?
                        .Where(e => _imageExts.Contains(Path.GetExtension(e.Name).ToLower()))
                        .ToList() ?? new List<BqbEntry>();
                }
                catch { }
            }
        }

        if (_bqbIndex.Count == 0)
        {
            Poke("在线表情包索引未就绪，用本地库的就好");
            return;
        }

        var keywords = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results = _bqbIndex
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

        var lines = results.Select((r, i) =>
            $"{i + 1}. {r.Name}\n   {ConvertBqbUrl(r.Url, cfg)}");
        string pokeMsg = $"搜到{results.Count}个：\n{string.Join("\n", lines)}\n" +
            $"选一个后用 <SaveImage name=\"文件名.后缀\" source=\"上面选的URL\" /> 下载到本地，再用 <qimage> 发出";

        Poke(pokeMsg);

        if (cfg.EnableOnlineSearchLog)
            Log($"在线搜图「{keyword}」→ {results.Count} 个结果");
    }

    // ===== 工具方法 =====

    static List<string> GetImageFiles(string dir)
    {
        return Directory.GetFiles(dir)
            .Where(f => _imageExts.Contains(Path.GetExtension(f).ToLower()))
            .ToList();
    }

    string BuildEmojiListString(string dir, int maxPreview = 50)
    {
        if (!Directory.Exists(dir)) return "(空)";

        var files = GetImageFiles(dir)
            .Select(Path.GetFileName)
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0) return "(空)";

        Log($"扫描目录：共 {files.Count} 个表情包，预览上限 {maxPreview}");

        var lines = maxPreview > 0 ? files.Take(maxPreview).Select(f => $"  - {f}")
                                   : files.Select(f => $"  - {f}");
        var result = string.Join("\n", lines);
        if (maxPreview > 0 && files.Count > maxPreview)
            result += $"\n  ... 共 {files.Count} 个，如需完整列表请调用 ListEmojis";

        return result;
    }
}
