using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alife.Framework;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using AntDesign;

namespace Alife.Plugin.QQEmoji;

public partial class QQEmojiUI : ModuleUIBase<QQEmoji, QQEmojiConfig>
{
    const string Css = @"
/* ===== 根容器 ===== */
.qem-root {
    --qem-pink: #ff4d9a;
    --qem-pink-hot: #ff1a7a;
    --qem-pink-soft: #ff8fbf;
    --qem-pink-glow: rgba(255, 77, 154, 0.55);
    --qem-violet: #c44dff;
    --qem-glass: rgba(255, 255, 255, 0.55);

    position: relative;
    isolation: isolate;
    overflow: hidden;
    padding: 28px 24px 32px;
    border-radius: 24px;
    color: #5a1f3a;
    background:
        radial-gradient(ellipse 90% 70% at 0% -10%, rgba(255, 77, 154, 0.35) 0%, transparent 55%),
        radial-gradient(ellipse 80% 60% at 100% 0%, rgba(196, 77, 255, 0.22) 0%, transparent 50%),
        radial-gradient(ellipse 70% 50% at 50% 110%, rgba(255, 143, 191, 0.28) 0%, transparent 55%),
        linear-gradient(145deg, #1a0612 0%, #2a0a1c 35%, #3d0f28 70%, #1f0814 100%);
    box-shadow:
        0 0 0 1px rgba(255, 143, 191, 0.25),
        0 20px 60px rgba(255, 26, 122, 0.25),
        0 8px 24px rgba(0, 0, 0, 0.35),
        inset 0 1px 0 rgba(255, 255, 255, 0.12);
    animation: qem-root-in 0.7s cubic-bezier(0.16, 1, 0.3, 1) both;
}
@keyframes qem-root-in {
    from { opacity: 0; transform: translateY(18px) scale(0.985); filter: blur(6px); }
    to { opacity: 1; transform: none; filter: none; }
}

.qem-grid-bg {
    position: absolute; inset: 0; z-index: 0; pointer-events: none;
    background-image:
        linear-gradient(rgba(255, 143, 191, 0.06) 1px, transparent 1px),
        linear-gradient(90deg, rgba(255, 143, 191, 0.06) 1px, transparent 1px);
    background-size: 36px 36px;
    mask-image: radial-gradient(ellipse 80% 70% at 50% 40%, #000 20%, transparent 75%);
    animation: qem-grid-drift 18s linear infinite;
}
@keyframes qem-grid-drift {
    from { background-position: 0 0, 0 0; }
    to { background-position: 36px 36px, 36px 36px; }
}

.qem-orb {
    position: absolute; border-radius: 50%; filter: blur(40px);
    pointer-events: none; z-index: 0; opacity: 0.55; mix-blend-mode: screen;
}
.qem-orb-a {
    width: 220px; height: 220px; top: -60px; left: -40px;
    background: radial-gradient(circle, rgba(255, 77, 154, 0.7), transparent 70%);
    animation: qem-orb-move-a 10s ease-in-out infinite;
}
.qem-orb-b {
    width: 180px; height: 180px; top: 20%; right: -50px;
    background: radial-gradient(circle, rgba(196, 77, 255, 0.55), transparent 70%);
    animation: qem-orb-move-b 12s ease-in-out infinite;
}
.qem-orb-c {
    width: 160px; height: 160px; bottom: -40px; left: 35%;
    background: radial-gradient(circle, rgba(255, 143, 191, 0.5), transparent 70%);
    animation: qem-orb-move-c 9s ease-in-out infinite;
}
@keyframes qem-orb-move-a {
    0%, 100% { transform: translate(0, 0) scale(1); }
    50% { transform: translate(40px, 30px) scale(1.15); }
}
@keyframes qem-orb-move-b {
    0%, 100% { transform: translate(0, 0) scale(1); }
    50% { transform: translate(-30px, 40px) scale(1.2); }
}
@keyframes qem-orb-move-c {
    0%, 100% { transform: translate(0, 0) scale(1); }
    50% { transform: translate(25px, -25px) scale(1.1); }
}

.qem-scan {
    position: absolute; left: 0; right: 0; height: 120px; z-index: 0; pointer-events: none;
    background: linear-gradient(180deg, transparent 0%, rgba(255,77,154,0.04) 40%, rgba(255,143,191,0.08) 50%, rgba(255,77,154,0.04) 60%, transparent 100%);
    animation: qem-scan 7s ease-in-out infinite;
}
@keyframes qem-scan {
    0% { top: -20%; opacity: 0; }
    15% { opacity: 1; }
    85% { opacity: 1; }
    100% { top: 100%; opacity: 0; }
}

.qem-paw {
    position: absolute; z-index: 0; font-size: 26px; opacity: 0.18; pointer-events: none; user-select: none;
    filter: drop-shadow(0 0 8px rgba(255, 77, 154, 0.6));
    animation: qem-paw-float 7s ease-in-out infinite;
}
.qem-paw-1 { top: 16px; right: 24px; animation-delay: 0s; }
.qem-paw-2 { top: 48%; left: 10px; font-size: 20px; opacity: 0.12; animation-delay: 1.8s; }
.qem-paw-3 { bottom: 28px; right: 18%; font-size: 22px; opacity: 0.14; animation-delay: 3.2s; }
.qem-paw-4 { top: 28%; right: 8%; font-size: 16px; opacity: 0.1; animation-delay: 2.1s; }
@keyframes qem-paw-float {
    0%, 100% { transform: translateY(0) rotate(-8deg) scale(1); }
    50% { transform: translateY(-14px) rotate(10deg) scale(1.08); }
}

.qem-root::before {
    content: ''; position: absolute; inset: 0; border-radius: 24px; padding: 1.5px;
    background: linear-gradient(120deg, transparent 20%, rgba(255,143,191,0.9) 40%, rgba(255,26,122,1) 50%, rgba(196,77,255,0.9) 60%, transparent 80%);
    background-size: 300% 300%;
    -webkit-mask: linear-gradient(#fff 0 0) content-box, linear-gradient(#fff 0 0);
    mask: linear-gradient(#fff 0 0) content-box, linear-gradient(#fff 0 0);
    -webkit-mask-composite: xor; mask-composite: exclude;
    animation: qem-border-flow 5s linear infinite;
    pointer-events: none; z-index: 2;
}
@keyframes qem-border-flow {
    0% { background-position: 0% 50%; }
    100% { background-position: 300% 50%; }
}

/* ===== 内容层 ===== */
.qem-content { position: relative; z-index: 1; }

/* ===== 标题 ===== */
.qem-header { display: flex; align-items: center; gap: 16px; margin-bottom: 14px; }
.qem-logo {
    position: relative; width: 56px; height: 56px; border-radius: 18px;
    display: flex; align-items: center; justify-content: center; font-size: 28px;
    background: linear-gradient(145deg, #ff4d9a, #ff1a7a 50%, #c44dff);
    box-shadow: 0 0 0 1px rgba(255,255,255,0.25), 0 0 24px rgba(255,26,122,0.55), 0 8px 20px rgba(0,0,0,0.35);
    animation: qem-logo-pulse 3.2s ease-in-out infinite;
}
.qem-logo::after {
    content: ''; position: absolute; inset: -4px; border-radius: 20px;
    background: conic-gradient(from 0deg, transparent, #ff8fbf, transparent 40%, #c44dff, transparent 70%);
    animation: qem-spin 4s linear infinite; z-index: -1; opacity: 0.85; filter: blur(1px);
}
@keyframes qem-spin { to { transform: rotate(360deg); } }
@keyframes qem-logo-pulse {
    0%, 100% { transform: scale(1); box-shadow: 0 0 0 1px rgba(255,255,255,0.25), 0 0 24px rgba(255,26,122,0.55), 0 8px 20px rgba(0,0,0,0.35); }
    50% { transform: scale(1.04); box-shadow: 0 0 0 1px rgba(255,255,255,0.35), 0 0 36px rgba(255,26,122,0.75), 0 10px 24px rgba(0,0,0,0.4); }
}
.qem-title-wrap { flex: 1; min-width: 0; }
.qem-title {
    font-size: 24px; font-weight: 900; letter-spacing: 0.5px; line-height: 1.2;
    background: linear-gradient(100deg, #fff 0%, #ffb3d4 25%, #ff4d9a 50%, #e0a0ff 75%, #fff 100%);
    background-size: 220% auto;
    -webkit-background-clip: text; background-clip: text; -webkit-text-fill-color: transparent;
    animation: qem-shimmer 3.5s linear infinite;
    filter: drop-shadow(0 0 18px rgba(255, 77, 154, 0.45));
}
@keyframes qem-shimmer {
    0% { background-position: 0% center; }
    100% { background-position: 220% center; }
}

.qem-tip {
    display: flex; gap: 12px; align-items: flex-start; padding: 14px 16px; margin-bottom: 20px;
    border-radius: 16px; border: 1px solid rgba(255, 143, 191, 0.35);
    background: linear-gradient(135deg, rgba(255,255,255,0.1), rgba(255,77,154,0.08));
    backdrop-filter: blur(14px);
    box-shadow: 0 4px 20px rgba(0,0,0,0.2), inset 0 1px 0 rgba(255,255,255,0.12);
    overflow: hidden;
    animation: qem-tip-in 0.65s cubic-bezier(0.16, 1, 0.3, 1) 0.1s both;
}
@keyframes qem-tip-in {
    from { opacity: 0; transform: translateX(-12px); }
    to { opacity: 1; transform: none; }
}
.qem-tip::before {
    content: ''; position: absolute; top: 0; left: -40%; width: 40%; height: 100%;
    background: linear-gradient(90deg, transparent, rgba(255,255,255,0.12), transparent);
    animation: qem-sheen 4.5s ease-in-out infinite;
}
@keyframes qem-sheen {
    0% { left: -40%; }
    55%, 100% { left: 120%; }
}
.qem-tip-icon {
    font-size: 20px; line-height: 1.3; flex-shrink: 0;
    filter: drop-shadow(0 0 8px rgba(255, 77, 154, 0.7));
    animation: qem-icon-bob 2.6s ease-in-out infinite;
}
@keyframes qem-icon-bob {
    0%, 100% { transform: translateY(0) rotate(0deg); }
    50% { transform: translateY(-4px) rotate(-6deg); }
}
.qem-tip-title {
    font-size: 13px; font-weight: 800; color: #ffb3d4; margin-bottom: 4px; letter-spacing: 0.3px;
}
.qem-tip-desc {
    font-size: 12px; color: rgba(255, 220, 235, 0.82); line-height: 1.75; white-space: pre-line;
}

/* ===== 卡片网格 ===== */
.qem-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
    gap: 16px;
    align-items: stretch;
}
.qem-card {
    position: relative; border-radius: 18px; padding: 16px 15px 14px;
    background: linear-gradient(160deg, rgba(255,255,255,0.12), rgba(255,77,154,0.06) 55%, rgba(196,77,255,0.05));
    border: 1px solid rgba(255, 143, 191, 0.28);
    backdrop-filter: blur(16px) saturate(1.2);
    box-shadow: 0 8px 28px rgba(0,0,0,0.28), inset 0 1px 0 rgba(255,255,255,0.14);
    transition: transform 0.35s cubic-bezier(0.16,1,0.3,1), box-shadow 0.35s ease, border-color 0.35s ease, background 0.35s ease;
    animation: qem-card-in 0.7s cubic-bezier(0.16,1,0.3,1) both;
    overflow: hidden;
}
.qem-card:nth-child(1) { animation-delay: 0.05s; }
.qem-card:nth-child(2) { animation-delay: 0.10s; }
.qem-card:nth-child(3) { animation-delay: 0.15s; }
.qem-card:nth-child(4) { animation-delay: 0.20s; }
.qem-card:nth-child(5) { animation-delay: 0.25s; }
.qem-card:nth-child(6) { animation-delay: 0.30s; }
.qem-card:nth-child(7) { animation-delay: 0.35s; }
@keyframes qem-card-in {
    from { opacity: 0; transform: translateY(16px) scale(0.96); filter: blur(4px); }
    to { opacity: 1; transform: none; filter: none; }
}
.qem-card::before {
    content: ''; position: absolute; inset: 0; border-radius: 18px; padding: 1px;
    background: linear-gradient(135deg, rgba(255,255,255,0.35), transparent 35%, transparent 65%, rgba(255,77,154,0.45));
    -webkit-mask: linear-gradient(#fff 0 0) content-box, linear-gradient(#fff 0 0);
    mask: linear-gradient(#fff 0 0) content-box, linear-gradient(#fff 0 0);
    -webkit-mask-composite: xor; mask-composite: exclude;
    pointer-events: none; opacity: 0.7; transition: opacity 0.3s ease;
}
.qem-card::after {
    content: ''; position: absolute; width: 140px; height: 140px; top: -50px; right: -40px;
    border-radius: 50%; background: radial-gradient(circle, rgba(255,77,154,0.25), transparent 70%);
    pointer-events: none; transition: transform 0.45s ease, opacity 0.45s ease; opacity: 0.6;
}
.qem-card:hover {
    transform: translateY(-6px) scale(1.012);
    border-color: rgba(255, 143, 191, 0.65);
    box-shadow: 0 16px 40px rgba(255,26,122,0.28), 0 0 0 1px rgba(255,143,191,0.2), inset 0 1px 0 rgba(255,255,255,0.2);
    background: linear-gradient(160deg, rgba(255,255,255,0.16), rgba(255,77,154,0.1) 55%, rgba(196,77,255,0.08));
}
.qem-card:hover::before { opacity: 1; }
.qem-card:hover::after { transform: scale(1.35) translate(-8px, 8px); opacity: 1; }

.qem-card-wide { grid-column: 1 / -1; }

.qem-card-head {
    display: flex; align-items: center; gap: 8px; margin-bottom: 12px; padding-bottom: 10px;
    border-bottom: 1px solid rgba(255, 143, 191, 0.2); position: relative;
}
.qem-card-head::after {
    content: ''; position: absolute; left: 0; bottom: -1px; width: 42%; height: 2px; border-radius: 2px;
    background: linear-gradient(90deg, #ff4d9a, #c44dff, transparent);
    box-shadow: 0 0 10px rgba(255, 77, 154, 0.6);
    animation: qem-bar-grow 1.2s cubic-bezier(0.16,1,0.3,1) both;
}
@keyframes qem-bar-grow { from { width: 0; opacity: 0; } to { width: 42%; opacity: 1; } }
.qem-card-emoji { font-size: 17px; line-height: 1; filter: drop-shadow(0 0 6px rgba(255,77,154,0.5)); }
.qem-card-title {
    font-size: 14px; font-weight: 800; color: #ffd0e4; letter-spacing: 0.4px;
    text-shadow: 0 0 16px rgba(255, 77, 154, 0.35);
}
.qem-card-paw {
    margin-left: auto; font-size: 13px; opacity: 0.35;
    transition: transform 0.4s cubic-bezier(0.16,1,0.3,1), opacity 0.3s ease, filter 0.3s ease;
}
.qem-card:hover .qem-card-paw {
    opacity: 1; transform: rotate(-18deg) scale(1.25);
    filter: drop-shadow(0 0 8px rgba(255, 77, 154, 0.8));
}

/* ===== 表单 ===== */
.qem-field { margin-top: 11px; }
.qem-label {
    font-size: 12px; font-weight: 750; color: #ffb3d4; margin-bottom: 6px; letter-spacing: 0.2px;
}
.qem-hint {
    font-size: 11px; color: rgba(255, 200, 220, 0.55); margin: 5px 0 2px 1px; line-height: 1.5;
}

/* AntDesign 输入 */
.qem-root .ant-input {
    border-radius: 12px !important;
    border-color: rgba(255, 143, 191, 0.4) !important;
    background: rgba(20, 6, 14, 0.45) !important;
    color: #ffe6f1 !important;
    transition: border-color 0.25s ease, box-shadow 0.25s ease, background 0.25s ease !important;
}
.qem-root .ant-input::placeholder { color: rgba(255, 200, 220, 0.35) !important; }
.qem-root .ant-input:hover {
    border-color: rgba(255, 143, 191, 0.75) !important;
    background: rgba(30, 8, 20, 0.55) !important;
}
.qem-root .ant-input:focus, .qem-root .ant-input-focused {
    border-color: #ff4d9a !important;
    box-shadow: 0 0 0 3px rgba(255, 77, 154, 0.25), 0 0 18px rgba(255, 26, 122, 0.25) !important;
    background: rgba(30, 8, 20, 0.65) !important;
}

.qem-select, .qem-textarea {
    width: 100%; box-sizing: border-box; padding: 8px 12px;
    border: 1px solid rgba(255, 143, 191, 0.4); border-radius: 12px;
    font-size: 13px; font-family: inherit; color: #ffe6f1;
    background: rgba(20, 6, 14, 0.45); outline: none;
    transition: border-color 0.25s ease, box-shadow 0.25s ease, background 0.25s ease;
}
.qem-select option { background: #2a0a1c; color: #ffe6f1; }
.qem-select:hover, .qem-textarea:hover {
    border-color: rgba(255, 143, 191, 0.75); background: rgba(30, 8, 20, 0.55);
}
.qem-select:focus, .qem-textarea:focus {
    border-color: #ff4d9a;
    box-shadow: 0 0 0 3px rgba(255, 77, 154, 0.25), 0 0 18px rgba(255, 26, 122, 0.25);
    background: rgba(30, 8, 20, 0.65);
}
.qem-textarea {
    resize: vertical; min-height: 76px;
    font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; line-height: 1.55;
}

/* 开关行 */
.qem-switch-row {
    display: flex; align-items: center; justify-content: space-between; gap: 12px;
    margin-top: 10px; padding: 10px 12px; border-radius: 14px;
    background: linear-gradient(135deg, rgba(255,77,154,0.1), rgba(255,255,255,0.04));
    border: 1px solid rgba(255, 143, 191, 0.22);
    transition: transform 0.25s ease, border-color 0.25s ease, box-shadow 0.25s ease, background 0.25s ease;
}
.qem-switch-row:hover {
    transform: translateX(2px);
    border-color: rgba(255, 143, 191, 0.55);
    box-shadow: 0 0 20px rgba(255, 77, 154, 0.15);
    background: linear-gradient(135deg, rgba(255,77,154,0.16), rgba(196,77,255,0.08));
}
.qem-switch-label { font-size: 12.5px; font-weight: 750; color: #ffc4dc; }

.qem-root .ant-switch-checked {
    background: linear-gradient(135deg, #ff4d9a, #c44dff) !important;
    box-shadow: 0 0 12px rgba(255, 77, 154, 0.55) !important;
}
.qem-root .ant-switch { background: rgba(255, 255, 255, 0.15) !important; }

/* 分隔线 */
.qem-sep {
    margin: 10px 0; height: 1px;
    background: linear-gradient(90deg, transparent, rgba(255,143,191,0.3), transparent);
}

.qem-bottom-line {
    margin-top: 22px; height: 2px; border-radius: 2px;
    background: linear-gradient(90deg, transparent, #ff4d9a, #c44dff, #ff8fbf, transparent);
    background-size: 200% 100%;
    box-shadow: 0 0 12px rgba(255, 77, 154, 0.5);
    animation: qem-line-flow 3s linear infinite;
}
@keyframes qem-line-flow {
    0% { background-position: 0% 50%; }
    100% { background-position: 200% 50%; }
}

@media (prefers-reduced-motion: reduce) {
    .qem-root, .qem-root * { animation-duration: 0.01ms !important; animation-iteration-count: 1 !important; transition-duration: 0.01ms !important; }
}
";

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        if (Configuration == null)
        {
            b.AddContent(0, "Configuration NULL");
            return;
        }

        int i = 0;

        b.OpenElement(i++, "style");
        b.AddContent(i++, Css);
        b.CloseElement();

        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qem-root");

        // 背景
        Orb(b, ref i, "qem-grid-bg");
        Orb(b, ref i, "qem-orb qem-orb-a");
        Orb(b, ref i, "qem-orb qem-orb-b");
        Orb(b, ref i, "qem-orb qem-orb-c");
        Orb(b, ref i, "qem-scan");
        Paw(b, ref i, "qem-paw qem-paw-1", "🐾");
        Paw(b, ref i, "qem-paw qem-paw-2", "🐾");
        Paw(b, ref i, "qem-paw qem-paw-3", "🐾");
        Paw(b, ref i, "qem-paw qem-paw-4", "🐾");

        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qem-content");

        // 标题
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qem-header");
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qem-logo");
        b.AddContent(i++, "😺");
        b.CloseElement();
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qem-title-wrap");
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qem-title");
        b.AddContent(i++, "QQ表情包管家");
        b.CloseElement();
        b.CloseElement();
        b.CloseElement();

        // 提示
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qem-tip");
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qem-tip-icon");
        b.AddContent(i++, "✨");
        b.CloseElement();
        b.OpenElement(i++, "div");
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qem-tip-title");
        b.AddContent(i++, "使用说明");
        b.CloseElement();
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qem-tip-desc");
        b.AddContent(i++, "对 AI 说「存为 名字.后缀」即可保存 QQ 图片到本地\nAI 会按下方策略在聊天中自动发送表情包\n修改配置后需重新加载模块");
        b.CloseElement();
        b.CloseElement();
        b.CloseElement();

        // ===== 卡片网格 =====
        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qem-grid");

        // ---- 卡1: 基础 & 策略 ----
        OpenCard(b, ref i, "📁", "基础 & 策略");
        AddInput(b, ref i, "表情包目录", Configuration.EmojiPath, v =>
        {
            Configuration.EmojiPath = v;
            try
            {
                if (!string.IsNullOrWhiteSpace(v))
                    Directory.CreateDirectory(v);
            }
            catch { }
        });
        AddHint(b, ref i, "目录不存在时会自动创建，无需手动建文件夹");
        AddHint(b, ref i, "存图和 AI 发图共用同一目录");
        AddInput(b, ref i, "基础概率 (0-100%)", Configuration.AutoProbability.ToString(), v =>
        {
            if (int.TryParse(v, out var n)) Configuration.AutoProbability = Math.Clamp(n, 0, 100);
        });
        AddHint(b, ref i, "每次 AI 回复触发发送的概率，0 = 关闭");
        AddSelect(b, ref i, "策略模式", Configuration.PolicyMode.ToString(), v =>
        {
            if (Enum.TryParse<EmojiPolicyMode>(v, out var m)) Configuration.PolicyMode = m;
        }, new[] {
            ("Balanced", "平衡（推荐）"),
            ("Conservative", "保守（少发）"),
            ("Active", "活跃（多发）"),
        });
        AddHint(b, ref i, "保守 = 概率减半 · 活跃 = 加 20% · 平衡 = 不变");

        // 冷却机制开关 + 时间放一起
        Sep(b, ref i);
        AddSwitch(b, ref i, "冷却机制", Configuration.EnableCooldown, v => Configuration.EnableCooldown = v);
        if (Configuration.EnableCooldown)
        {
            AddInput(b, ref i, "冷却时间 (秒)", Configuration.CooldownSeconds.ToString(), v =>
            {
                if (int.TryParse(v, out var n)) Configuration.CooldownSeconds = Math.Max(0, n);
            });
            AddHint(b, ref i, "发完一次后等多久才能再发，防刷屏");
        }

        // 连发限制开关 + 次数放一起
        Sep(b, ref i);
        AddSwitch(b, ref i, "连发限制", Configuration.EnableBurstLimit, v => Configuration.EnableBurstLimit = v);
        if (Configuration.EnableBurstLimit)
        {
            AddInput(b, ref i, "最大连发次数", Configuration.MaxBurst.ToString(), v =>
            {
                if (int.TryParse(v, out var n)) Configuration.MaxBurst = Math.Max(1, n);
            });
            AddHint(b, ref i, "冷却时间内最多发几次，建议 1 次");
        }
        CloseCard(b);

        // ---- 卡2: 情绪控制（开关 + 关键词配对） ----
        OpenCard(b, ref i, "💖", "情绪控制");
        AddSwitch(b, ref i, "情绪加权", Configuration.EnableEmotionBoost, v => Configuration.EnableEmotionBoost = v);
        AddHint(b, ref i, "检测对话关键词临时增加发送概率");

        if (Configuration.EnableEmotionBoost)
        {
            var kwText = Configuration.EmotionKeywords != null
                ? string.Join(", ", Configuration.EmotionKeywords.Select(kv => $"{kv.Key}:{kv.Value}"))
                : "";
            AddTextArea(b, ref i, "关键词:权重", kwText, v =>
            {
                var dict = new Dictionary<string, int>();
                foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Trim().Split(':');
                    if (kv.Length == 2 && int.TryParse(kv[1], out var w))
                        dict[kv[0].Trim()] = w;
                }
                if (dict.Count > 0) Configuration.EmotionKeywords = dict;
            });
            AddHint(b, ref i, "格式：哈哈:25, 笑死:30 · 逗号分隔，冒号隔开关键词和权重");
        }
        CloseCard(b);

        // ---- 卡3: 列表更新 ----
        OpenCard(b, ref i, "🔄", "列表更新");
        AddSwitch(b, ref i, "存图后自动更新列表", Configuration.EnableAutoUpdateEmojiList, v => Configuration.EnableAutoUpdateEmojiList = v);
        AddHint(b, ref i, "更新会破坏缓存命中，略增推理开销");

        if (Configuration.EnableAutoUpdateEmojiList)
        {
            AddInput(b, ref i, "每存 N 个图后更新", Configuration.AutoUpdateThreshold.ToString(), v =>
            {
                if (int.TryParse(v, out var n)) Configuration.AutoUpdateThreshold = Math.Max(1, n);
            });
            AddHint(b, ref i, "存图达到该数量时刷新 AI 上下文中的列表");
        }

        AddInput(b, ref i, "预览数量上限 (0=不限制)", Configuration.MaxEmojiListPreview.ToString(), v =>
        {
            if (int.TryParse(v, out var n)) Configuration.MaxEmojiListPreview = Math.Max(0, n);
        });
        AddHint(b, ref i, "注入提示的表情条数上限，注意 token");
        CloseCard(b);

        // ---- 卡4: 调试 ----
        OpenCard(b, ref i, "🔧", "调试");
        AddSwitch(b, ref i, "日志输出", Configuration.EnableLogging, v => Configuration.EnableLogging = v);
        AddHint(b, ref i, "在运行窗口显示插件运行日志");
        CloseCard(b);

        // ---- 卡5: 在线搜图（BQB 内置索引） ----
        OpenCard(b, ref i, "🌐", "在线搜图·BQB");
        AddSwitch(b, ref i, "启用 BQB 在线搜图", Configuration.EnableOnlineSearch, v => Configuration.EnableOnlineSearch = v);
        AddHint(b, ref i, "AI 可用 SearchBqbOnline 搜内置 ChineseBQB 索引（需下载后再发）");

        if (Configuration.EnableOnlineSearch)
        {
            AddSelect(b, ref i, "图片下载源", Configuration.BqbUseCdn ? "CDN" : "RAW", v =>
            {
                Configuration.BqbUseCdn = v == "CDN";
            }, new[] {
                ("CDN", "镜像源（jsDelivr，国内推荐）"),
                ("RAW", "GitHub 源（raw.githubusercontent.com）"),
            });
            AddHint(b, ref i, "仅影响图片下载地址；索引为插件内置，不联网更新");

            AddInput(b, ref i, "搜索结果数量", Configuration.SearchResultCount.ToString(), v =>
            {
                if (int.TryParse(v, out var n)) Configuration.SearchResultCount = Math.Max(1, n);
            });
            AddHint(b, ref i, "每次搜索返回的最大条数，建议 3–10（腾讯源也会复用此数量）");

            Sep(b, ref i);
            AddSwitch(b, ref i, "自动下载搜索结果到表情包目录", Configuration.EnableOnlineImageCache, v => Configuration.EnableOnlineImageCache = v);
            if (Configuration.EnableOnlineImageCache)
            {
                AddHint(b, ref i, "✓ 搜到的图自动下载到表情包目录并永久保存，AI 可直接 <qimage> 发送，无需 SaveImage");
            }
            else
            {
                var cachePath = GetCacheHintPath();
                AddHint(b, ref i, $"搜到的图仅临时缓存到 {cachePath}（5 小时 + 启动时自动清理），AI 需走 SaveImage 下载到本地");
            }

            AddSwitch(b, ref i, "搜图日志", Configuration.EnableOnlineSearchLog, v => Configuration.EnableOnlineSearchLog = v);
            AddHint(b, ref i, "显示索引加载、下载/缓存、腾讯搜图与搜索结果日志");
        }
        CloseCard(b);

        // ---- 卡6: 腾讯实时表情（推荐） ----
        OpenCard(b, ref i, "✨", "腾讯实时表情（推荐）");
        AddSwitch(b, ref i, "启用腾讯在线表情（推荐）", Configuration.EnableTencentSearch, v => Configuration.EnableTencentSearch = v);
        AddHint(b, ref i, "推荐开启（默认开）。AI 只需 <SendTencentEmoji keyword=\"开心\" />，插件自动搜图并同轮直发，无需再写 qimage");

        if (Configuration.EnableTencentSearch)
        {
            AddSwitch(b, ref i, "是否自动保存到库", Configuration.EnableTencentAutoSave, v => Configuration.EnableTencentAutoSave = v);
            if (Configuration.EnableTencentAutoSave)
            {
                AddHint(b, ref i, "✓ 发送成功后后台下载到本地表情包目录，方便下次本地直接用");
            }
            else
            {
                AddHint(b, ref i, "仅用 URL 直发，不下载到本地库");
            }

            if (!Configuration.EnableOnlineSearch)
            {
                AddSwitch(b, ref i, "搜图日志", Configuration.EnableOnlineSearchLog, v => Configuration.EnableOnlineSearchLog = v);
                AddHint(b, ref i, "显示腾讯搜图与发送日志");
            }
        }
        CloseCard(b);

        b.CloseElement(); // grid

        b.OpenElement(i++, "div");
        b.AddAttribute(i++, "class", "qem-bottom-line");
        b.CloseElement();

        b.CloseElement(); // content
        b.CloseElement(); // root
    }

    string GetCacheHintPath()
    {
        var dir = Configuration?.EmojiPath ?? "";
        return string.IsNullOrEmpty(dir) ? "EmojiPath/.online_cache" : Path.Combine(dir, ".online_cache");
    }

    // ===== 辅助渲染 =====

    static void Orb(RenderTreeBuilder b, ref int seq, string cls)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", cls);
        b.AddAttribute(seq++, "aria-hidden", "true");
        b.CloseElement();
    }

    static void Paw(RenderTreeBuilder b, ref int seq, string cls, string text)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", cls);
        b.AddAttribute(seq++, "aria-hidden", "true");
        b.AddContent(seq++, text);
        b.CloseElement();
    }

    static void Sep(RenderTreeBuilder b, ref int seq)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "qem-sep");
        b.CloseElement();
    }

    void OpenCard(RenderTreeBuilder b, ref int seq, string emoji, string title, bool wide = false)
    {
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", wide ? "qem-card qem-card-wide" : "qem-card");
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "qem-card-head");
        b.OpenElement(seq++, "span"); b.AddAttribute(seq++, "class", "qem-card-emoji"); b.AddContent(seq++, emoji); b.CloseElement();
        b.OpenElement(seq++, "span"); b.AddAttribute(seq++, "class", "qem-card-title"); b.AddContent(seq++, title); b.CloseElement();
        b.OpenElement(seq++, "span"); b.AddAttribute(seq++, "class", "qem-card-paw"); b.AddContent(seq++, "🐾"); b.CloseElement();
        b.CloseElement();
    }

    static void CloseCard(RenderTreeBuilder b) => b.CloseElement();

    void AddHint(RenderTreeBuilder b, ref int seq, string text)
    {
        b.OpenElement(seq++, "div"); b.AddAttribute(seq++, "class", "qem-hint"); b.AddContent(seq++, text); b.CloseElement();
    }

    void AddInput(RenderTreeBuilder b, ref int seq, string label, string value, Action<string> setter)
    {
        b.OpenElement(seq++, "div"); b.AddAttribute(seq++, "class", "qem-field");
        b.OpenElement(seq++, "div"); b.AddAttribute(seq++, "class", "qem-label"); b.AddContent(seq++, label); b.CloseElement();
        b.OpenComponent<Input<string>>(seq++);
        b.AddAttribute(seq++, "Value", value);
        b.AddAttribute(seq++, "ValueChanged", EventCallback.Factory.Create<string>(this, setter));
        b.CloseComponent();
        b.CloseElement();
    }

    void AddSelect(RenderTreeBuilder b, ref int seq, string label, string value, Action<string> setter, (string val, string text)[] options)
    {
        b.OpenElement(seq++, "div"); b.AddAttribute(seq++, "class", "qem-field");
        b.OpenElement(seq++, "div"); b.AddAttribute(seq++, "class", "qem-label"); b.AddContent(seq++, label); b.CloseElement();
        b.OpenElement(seq++, "select");
        b.AddAttribute(seq++, "class", "qem-select");
        b.AddAttribute(seq++, "value", value);
        b.AddAttribute(seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e => setter(e.Value?.ToString() ?? "")));
        foreach (var opt in options)
        {
            b.OpenElement(seq++, "option"); b.AddAttribute(seq++, "value", opt.val);
            if (opt.val == value) b.AddAttribute(seq++, "selected", true);
            b.AddContent(seq++, opt.text); b.CloseElement();
        }
        b.CloseElement();
        b.CloseElement();
    }

    void AddTextArea(RenderTreeBuilder b, ref int seq, string label, string value, Action<string> setter)
    {
        b.OpenElement(seq++, "div"); b.AddAttribute(seq++, "class", "qem-field");
        b.OpenElement(seq++, "div"); b.AddAttribute(seq++, "class", "qem-label"); b.AddContent(seq++, label); b.CloseElement();
        b.OpenElement(seq++, "textarea"); b.AddAttribute(seq++, "class", "qem-textarea");
        b.AddAttribute(seq++, "value", value);
        b.AddAttribute(seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e => setter(e.Value?.ToString() ?? "")));
        b.CloseElement();
        b.CloseElement();
    }

    void AddSwitch(RenderTreeBuilder b, ref int seq, string label, bool value, Action<bool> setter)
    {
        b.OpenElement(seq++, "div"); b.AddAttribute(seq++, "class", "qem-switch-row");
        b.OpenElement(seq++, "span"); b.AddAttribute(seq++, "class", "qem-switch-label"); b.AddContent(seq++, label); b.CloseElement();
        b.OpenComponent<Switch>(seq++);
        b.AddAttribute(seq++, "Checked", value);
        b.AddAttribute(seq++, "CheckedChanged", EventCallback.Factory.Create<bool>(this, setter));
        b.CloseComponent();
        b.CloseElement();
    }
}
