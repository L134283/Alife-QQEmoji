using System;
using System.Collections.Generic;
using System.Linq;
using Alife.Framework;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using AntDesign;

namespace Alife.Plugin.QQEmoji;

public partial class QQEmojiUI : ModuleUIBase<QQEmoji, QQEmojiConfig>
{
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        if (Configuration == null)
        {
            b.AddContent(0, "Configuration NULL");
            return;
        }

        int i = 0;

        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "style", "background:#fafafa;padding:24px;border-radius:12px;border:1px solid #f0f0f0;");

        // 标题
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "style", "font-size:16px;font-weight:bold;margin-bottom:4px;");
        b.AddContent(i++, "😊 QQ表情包管家");
        b.CloseElement();

        // 使用说明
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "style", "font-size:12px;color:#666;background:#e6f7ff;padding:10px 12px;border-radius:8px;margin-bottom:12px;line-height:1.6;");
        b.AddContent(i++, "📌 对AI说「存为 名字.后缀」即可保存QQ图片到本地\n");
        b.AddContent(i++, "📌 AI会按下方策略在聊天中自动发送表情包\n");
        b.AddContent(i++, "📌 修改配置后需重新加载模块");
        b.CloseElement();

        // ===== 基础设置 =====
        SectionTitle(b, ref i, "📁 基础设置");

        AddInput(b, ref i, "表情包目录", Configuration.EmojiPath, v => Configuration.EmojiPath = v);
        AddHint(b, ref i, "存图和AI发图都用的同一个目录");

        AddInput(b, ref i, "基础概率 (0-100%)", Configuration.AutoProbability.ToString(), v =>
        {
            if (int.TryParse(v, out var n))
                Configuration.AutoProbability = Math.Clamp(n, 0, 100);
        });
        AddHint(b, ref i, "每次AI回复时触发发送的概率，0=关闭此功能");

        // ===== 策略设置 =====
        SectionTitle(b, ref i, "🎯 策略设置");

        // 策略模式 — 原生 HTML 下拉
        AddSelect(b, ref i, "策略模式", Configuration.PolicyMode.ToString(), v =>
        {
            if (Enum.TryParse<EmojiPolicyMode>(v, out var mode))
                Configuration.PolicyMode = mode;
        }, new[] {
            ("Balanced", "平衡（推荐）"),
            ("Conservative", "保守（少发）"),
            ("Active", "活跃（多发）"),
        });
        AddHint(b, ref i, "保守=概率减半，活跃=加20%，平衡=不变");

        AddInput(b, ref i, "冷却时间 (秒)", Configuration.CooldownSeconds.ToString(), v =>
        {
            if (int.TryParse(v, out var n))
                Configuration.CooldownSeconds = Math.Max(0, n);
        });
        AddHint(b, ref i, "发完一次表情后等多久才能再发，防刷屏");

        AddInput(b, ref i, "最大连发次数", Configuration.MaxBurst.ToString(), v =>
        {
            if (int.TryParse(v, out var n))
                Configuration.MaxBurst = Math.Max(1, n);
        });
        AddHint(b, ref i, "冷却时间内最多发几次，建议1次");

        // ===== 开关 =====
        SectionTitle(b, ref i, "🔘 开关");

        AddSwitch(b, ref i, "情绪加权", Configuration.EnableEmotionBoost, v => Configuration.EnableEmotionBoost = v);
        AddHint(b, ref i, "检测关键词临时增加概率");

        // 情绪关键词 — 原生 HTML textarea
        var kwText = Configuration.EmotionKeywords != null
            ? string.Join(", ", Configuration.EmotionKeywords.Select(kv => $"{kv.Key}:{kv.Value}"))
            : "";
        AddTextArea(b, ref i, "情绪关键词 (关键词:权重)", kwText, v =>
        {
            var dict = new Dictionary<string, int>();
            foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Trim().Split(':');
                if (kv.Length == 2 && int.TryParse(kv[1], out var w))
                    dict[kv[0].Trim()] = w;
            }
            if (dict.Count > 0)
                Configuration.EmotionKeywords = dict;
        });
        AddHint(b, ref i, "格式：哈哈:25, 笑死:30，逗号分隔，冒号隔开关键词和权重");

        AddSwitch(b, ref i, "冷却机制", Configuration.EnableCooldown, v => Configuration.EnableCooldown = v);
        AddHint(b, ref i, "关闭后每次回复都可能连续触发");

        AddSwitch(b, ref i, "连发限制", Configuration.EnableBurstLimit, v => Configuration.EnableBurstLimit = v);
        AddHint(b, ref i, "关闭后冷却时间内可以无限发");

        b.CloseElement();
    }

    // ===== 工具函数 =====

    void SectionTitle(RenderTreeBuilder b, ref int seq, string text)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "style", "font-size:14px;font-weight:bold;color:#666;margin:12px 0 8px;border-bottom:1px solid #eee;padding-bottom:4px;");
        b.AddContent(seq++, text);
        b.CloseElement();
    }

    void AddHint(RenderTreeBuilder b, ref int seq, string text)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "style", "font-size:11px;color:#999;margin:2px 0 8px 2px;");
        b.AddContent(seq++, text);
        b.CloseElement();
    }

    void AddInput(RenderTreeBuilder b, ref int seq, string label, string value, Action<string> setter)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "style", "margin-top:10px;");
        b.CloseElement();

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "style", "font-weight:bold;margin-bottom:4px;font-size:13px;");
        b.AddContent(seq++, label);
        b.CloseElement();

        b.OpenComponent<Input<string>>(seq++);
        b.AddAttribute(seq++, "Value", value);
        b.AddAttribute(seq++, "ValueChanged",
            EventCallback.Factory.Create<string>(this, setter));
        b.CloseComponent();
    }

    void AddSelect(RenderTreeBuilder b, ref int seq, string label, string value, Action<string> setter, (string val, string text)[] options)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "style", "margin-top:10px;");
        b.CloseElement();

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "style", "font-weight:bold;margin-bottom:4px;font-size:13px;");
        b.AddContent(seq++, label);
        b.CloseElement();

        b.OpenElement(seq++, "select");
        b.AddAttribute(seq++, "style", "width:100%;padding:6px 10px;border:1px solid #d9d9d9;border-radius:6px;font-size:13px;background:#fff;");
        b.AddAttribute(seq++, "value", value);
        b.AddAttribute(seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
            setter(e.Value?.ToString() ?? "")));
        foreach (var opt in options)
        {
            b.OpenElement(seq++, "option");
            b.AddAttribute(seq++, "value", opt.val);
            if (opt.val == value)
                b.AddAttribute(seq++, "selected", true);
            b.AddContent(seq++, opt.text);
            b.CloseElement();
        }
        b.CloseElement();
    }

    void AddTextArea(RenderTreeBuilder b, ref int seq, string label, string value, Action<string> setter)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "style", "margin-top:10px;");
        b.CloseElement();

        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "style", "font-weight:bold;margin-bottom:4px;font-size:13px;");
        b.AddContent(seq++, label);
        b.CloseElement();

        b.OpenElement(seq++, "textarea");
        b.AddAttribute(seq++, "style", "width:100%;padding:6px 10px;border:1px solid #d9d9d9;border-radius:6px;font-size:13px;font-family:monospace;resize:vertical;min-height:60px;");
        b.AddAttribute(seq++, "value", value);
        b.AddAttribute(seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
            setter(e.Value?.ToString() ?? "")));
        b.CloseElement();
    }

    void AddSwitch(RenderTreeBuilder b, ref int seq, string label, bool value, Action<bool> setter)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "style", "margin-top:10px;display:flex;align-items:center;gap:12px;");

        b.OpenElement(seq++, "span");
        b.AddAttribute(seq++, "style", "font-weight:bold;font-size:13px;");
        b.AddContent(seq++, label);
        b.CloseElement();

        b.OpenComponent<Switch>(seq++);
        b.AddAttribute(seq++, "Checked", value);
        b.AddAttribute(seq++, "CheckedChanged",
            EventCallback.Factory.Create<bool>(this, setter));
        b.CloseComponent();

        b.CloseElement();
    }
}
