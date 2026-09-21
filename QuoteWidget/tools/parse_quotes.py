# -*- coding: utf-8 -*-
"""把《经典台词大集合/chat.md》解析为挂件内置词库 quotes.json"""
import json
import os
import re

SRC = r"C:\Users\WDL\Documents\经典台词大集合\chat.md"
DST = r"E:\Workspace_AI\ZCODE\demo_0820\QuoteWidget\Assets\quotes.json"

SECTION_MARKS = {"🎬": "movie", "🎮": "game", "📖": "novel", "🎵": "lyric"}


def main():
    with open(SRC, "r", encoding="utf-8") as f:
        text = f.read()

    quotes = []
    category = None
    counters = {}

    for raw in text.splitlines():
        line = raw.strip()
        if not line:
            continue
        if line.startswith("##"):
            category = None
            for mark, cat in SECTION_MARKS.items():
                if mark in line:
                    category = cat
                    break
            continue
        if line.startswith("---"):
            continue
        m = re.match(r"^(\d+)\.\s*(.+)$", line)
        if not m or category is None:
            continue

        body = m.group(2).strip()
        body, sep, source = body.rpartition("——")
        if not sep:
            text_part, source = body, ""
        else:
            text_part = body

        counters[category] = counters.get(category, 0) + 1
        quotes.append({
            "id": f"{category}-{counters[category]:03d}",
            "text": text_part.strip(),
            "source": source.strip(),
            "category": category,
        })

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    with open(DST, "w", encoding="utf-8") as f:
        json.dump({"version": 1, "quotes": quotes}, f, ensure_ascii=False, indent=1)

    print(f"parsed {len(quotes)} quotes ->", counters)
    for q in quotes[:3] + quotes[-3:]:
        print(" ", q["id"], q["text"][:24], "/", q["source"])


if __name__ == "__main__":
    main()
