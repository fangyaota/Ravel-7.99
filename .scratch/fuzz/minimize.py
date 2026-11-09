#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 fuzz 找到的失败样本缩到最小：token 级 delta-debugging。

用法: python .scratch/fuzz/minimize.py <case.rav> <关键词> [--parse]
  --parse 用解析那一趟判（默认用 --source 往返那一趟）
关键词是要在报错里出现的字样，比如 "->" 或 "Named"。
"""
import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("fz", os.path.join(HERE, "fuzz.py"))
fz = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fz)

TMP = os.path.join(HERE, "min.tmp.rav")


def fails(src, needle, extra):
    with open(TMP, "w", encoding="utf-8", newline="") as f:
        f.write(src)
    kind, msg = fz.run_one(TMP, extra=extra)
    return kind in ("BUG", "CRASH") and (needle in msg or needle == "")


def ddmin(toks, pred):
    n = 2
    while len(toks) >= 2:
        size = max(1, len(toks) // n)
        chunks = [toks[i:i + size] for i in range(0, len(toks), size)]
        reduced = False
        for c in chunks:
            cand = [t for t in toks if t not in c]  # 按值删（token 是字符串，够用）
            if not cand:
                continue
            if pred("".join(cand)):
                toks = cand
                n = max(2, n - 1)
                reduced = True
                break
        if not reduced:
            if n >= len(toks):
                break
            n = min(len(toks), n * 2)
    return toks


if __name__ == "__main__":
    path, needle = sys.argv[1], sys.argv[2]
    extra = () if "--parse" in sys.argv else ("--source",)
    with open(path, encoding="utf-8", errors="replace") as f:
        src = f.read()
    toks = fz.lex_rough(src)
    print("原始 %d 个 token，%d 字符" % (len(toks), len(src)))
    if not fails(src, needle, extra):
        print("!! 这个文件现在不再失败了（判据: %r）" % needle)
        sys.exit(1)
    out = ddmin(toks, lambda s: fails(s, needle, extra))
    text = "".join(out)
    print("缩到 %d 个 token：\n%s\n----" % (len(out), text))
    print("复现:", fails(text, needle, extra))
