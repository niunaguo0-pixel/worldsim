#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
WorldSim 设计文档 MD -> HTML 生成器
用法:
  python md2html.py <input.md> [output.html]
若省略 output.html，则与输入同目录同名 .html。

特性:
  - 剥离文档顶部 --- 注释块（项目名/版本等非 YAML 头）
  - 渲染 tables / fenced_code / toc 扩展
  - 注入适配 IDE 浅色主题的排版样式（中文友好）
"""
import sys
import os
import re

import markdown

TEMPLATE = """<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>{title}</title>
<style>
  :root {{
    --bg: #ffffff;
    --fg: #1f2328;
    --muted: #57606a;
    --accent: #0969da;
    --border: #d0d7de;
    --code-bg: #f6f8fa;
    --table-stripe: #f6f8fa;
  }}
  * {{ box-sizing: border-box; }}
  body {{
    margin: 0;
    padding: 0;
    background: var(--bg);
    color: var(--fg);
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "PingFang SC",
                 "Hiragino Sans GB", "Microsoft YaHei", "Helvetica Neue", Arial, sans-serif;
    font-size: 15px;
    line-height: 1.75;
  }}
  .page {{ max-width: 920px; margin: 0 auto; padding: 48px 28px 96px; }}
  h1, h2, h3, h4, h5, h6 {{
    color: var(--fg);
    line-height: 1.35;
    margin-top: 1.8em;
    margin-bottom: 0.6em;
    font-weight: 600;
  }}
  h1 {{ font-size: 28px; border-bottom: 2px solid var(--border); padding-bottom: 0.3em; }}
  h2 {{ font-size: 22px; border-bottom: 1px solid var(--border); padding-bottom: 0.25em; }}
  h3 {{ font-size: 18px; }}
  h4 {{ font-size: 16px; color: var(--muted); }}
  p {{ margin: 0.8em 0; }}
  a {{ color: var(--accent); text-decoration: none; }}
  a:hover {{ text-decoration: underline; }}
  code {{
    background: var(--code-bg);
    padding: 0.15em 0.4em;
    border-radius: 4px;
    font-family: "SFMono-Regular", Consolas, "Liberation Mono", Menlo, monospace;
    font-size: 0.88em;
  }}
  pre {{
    background: var(--code-bg);
    padding: 16px;
    border-radius: 8px;
    overflow-x: auto;
    border: 1px solid var(--border);
  }}
  pre code {{ background: none; padding: 0; }}
  table {{
    border-collapse: collapse;
    width: 100%;
    margin: 1em 0;
    font-size: 14px;
  }}
  th, td {{
    border: 1px solid var(--border);
    padding: 8px 12px;
    text-align: left;
    vertical-align: top;
  }}
  th {{ background: var(--table-stripe); font-weight: 600; }}
  tr:nth-child(even) td {{ background: #fbfcfd; }}
  blockquote {{
    margin: 1em 0;
    padding: 0.4em 1em;
    border-left: 4px solid var(--accent);
    background: #f6f8fa;
    color: var(--muted);
  }}
  ul, ol {{ padding-left: 1.6em; }}
  li {{ margin: 0.3em 0; }}
  hr {{ border: none; border-top: 1px solid var(--border); margin: 2em 0; }}
  #toc {{
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 8px;
    padding: 16px 20px;
    margin: 1.5em 0;
  }}
  #toc ul {{ list-style: none; padding-left: 1.1em; }}
  #toc > ul {{ padding-left: 0.4em; }}
  .meta {{ color: var(--muted); font-size: 13px; margin-bottom: 2em; }}
</style>
</head>
<body>
<div class="page">
<div class="meta">{meta}</div>
{toc}
{content}
</div>
</body>
</html>
"""


def strip_leading_comment(md_text: str) -> str:
    """剥离文档最开头的 --- ... --- 注释块（非 YAML front matter）。"""
    lines = md_text.split("\n")
    if not lines:
        return md_text
    # 找到第一个非空行
    idx = 0
    while idx < len(lines) and lines[idx].strip() == "":
        idx += 1
    if idx >= len(lines) or lines[idx].strip() != "---":
        return md_text
    # 找闭合的 ---
    end = idx + 1
    while end < len(lines) and lines[end].strip() != "---":
        end += 1
    if end >= len(lines):
        return md_text
    return "\n".join(lines[end + 1:])


def extract_title(md_text: str) -> str:
    for line in md_text.split("\n"):
        if line.strip().startswith("# "):
            return line.strip()[2:].strip()
    return "WorldSim 设计文档"


def main():
    if len(sys.argv) < 2:
        print("用法: python md2html.py <input.md> [output.html]")
        sys.exit(1)
    in_path = sys.argv[1]
    out_path = sys.argv[2] if len(sys.argv) > 2 else in_path[:-3] + ".html"
    with open(in_path, "r", encoding="utf-8") as f:
        raw = f.read()

    body = strip_leading_comment(raw)
    title = extract_title(body)

    md = markdown.Markdown(extensions=["tables", "fenced_code", "toc"])
    html_content = md.convert(body)
    toc_html = md.toc if hasattr(md, "toc") else ""

    meta = f"生成自 {os.path.basename(in_path)} · WorldSim 设计文档"
    final = TEMPLATE.format(title=title, meta=meta, toc=toc_html, content=html_content)

    with open(out_path, "w", encoding="utf-8") as f:
        f.write(final)
    print(f"OK: {in_path} -> {out_path} ({len(final)} bytes)")


if __name__ == "__main__":
    main()
