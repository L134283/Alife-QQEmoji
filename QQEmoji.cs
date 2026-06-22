using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
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
        ["？"] = 5, ["！"] = 5,
    };
}

[Module("QQ表情包管家",
    "对AI说「存为 名字.后缀」即可保存QQ图片到本地，AI也会按策略自动发送表情包。",
    defaultCategory: "Alife 官方/实用工具",
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

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        XmlHandler handler = new(this);
        functionService.RegisterHandler(handler);

        var cfg = Configuration ?? new QQEmojiConfig();
        Prompt($$"""
            你有本地表情包库，存放在：{{cfg.EmojiPath}}

            【存图】当用户说「存为xxx」时，用以下格式保存图片：
            <SaveImage name="文件名.后缀">图片URL</SaveImage>
            例如：<SaveImage name="可爱猫猫.png">https://example.com/cat.png</SaveImage>

            【发图】系统有 {{cfg.AutoProbability}}% 的基础概率允许你发一个表情包。
            步骤：
            1. 如果系统允许，用 <ListEmojis /> 查看可用表情
            2. 自己选一个合适的
            3. 用 <qimage type="Private/Group" targetid="QQ号/群号" image="{{cfg.EmojiPath}}/文件名.后缀" /> 发送

            注意：系统决定「能否发」，你来决定「发哪个」
            """);
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);

        ChatBot.ChatOver -= OnChatOver;
        ChatBot.ChatOver += OnChatOver;
    }

    public override Task DestroyAsync()
    {
        ChatBot.ChatOver -= OnChatOver;
        return base.DestroyAsync();
    }

    // 在 AI 回复完毕后触发，直接修改 ChatHistory 中 Assistant 消息的 Content
    // 把 <qimage> 标签附加到 bot 回复内容末尾，而不是另发一条系统通知
    void OnChatOver()
    {
        var cfg = Configuration;
        if (cfg == null) return;

        // ChatOver 触发时，ChatHistory 最后一条一定是 AI 刚刚回复的 Assistant 消息
        if (ChatHistory.Count == 0) return;
        var lastMsg = ChatHistory[^1];
        if (lastMsg.Content == null) return;

        string text = lastMsg.Content;
        if (text.Contains("<qimage")) return;

        // 冷却复位
        lock (_stateLock)
        {
            if (cfg.EnableCooldown &&
                (DateTime.UtcNow - _lastSendTime).TotalSeconds >= cfg.CooldownSeconds)
            {
                _burstCount = 0;
            }
        }

        var decision = Decide(text, cfg);
        if (!decision.ShouldSend) return;

        var dir = cfg.EmojiPath;
        if (!Directory.Exists(dir)) return;

        var files = Directory.GetFiles(dir)
            .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") ||
                        f.EndsWith(".jpeg") || f.EndsWith(".gif") || f.EndsWith(".webp"))
            .ToList();
        if (files.Count == 0) return;

        var pick = files[RandomNumberGenerator.GetInt32(files.Count)];

        lock (_stateLock)
        {
            _lastSendTime = DateTime.UtcNow;
            _burstCount++;
        }

        // 直接把 qimage 标签拼接到 AI 回复内容末尾
        // ChatHistoryUI.razor 从 ChatHistory.AsEnumerable() 读取渲染，
        // ChatOver 事件中 ChatMessageService 触发 OnMessageChanged 会排队 UI 重绘，
        // 此时 ChatHistory 已被我们修改，渲染时 emoji 标签会显示在 bot 回复中
        lastMsg.Content = text + $"<qimage image=\"{pick}\" />";
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
            var dir = Configuration!.EmojiPath;
            Directory.CreateDirectory(dir);
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

            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://qq.com");
            var bytes = await _http.GetByteArrayAsync(source);
            await File.WriteAllBytesAsync(dest, bytes);

            Poke($"✅ 已保存: {Path.GetFileName(dest)}");
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
        var dir = Configuration!.EmojiPath;
        if (!Directory.Exists(dir))
        {
            Poke("📭 表情包库为空。");
            return;
        }

        var exts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
        var files = Directory.GetFiles(dir)
            .Where(f => exts.Contains(Path.GetExtension(f).ToLower()))
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
}
