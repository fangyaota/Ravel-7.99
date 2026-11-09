#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""tests/ 迁移:把**源码区**(`# --- expected ---` 之前)里当空字典用的 `{}` 改成 `{->}`。

期望区不动 —— 那里是输出文本,等跑完测试再按实际输出逐条修。
用法: python .scratch/migrate_tests.py
"""
import os
import sys

SEP = "# --- expected ---"


def migrate_line(line):
    out = []
    i, n = 0, len(line)
    in_str = raw = False
    hits = 0
    while i < n:
        c = line[i]
        if in_str:
            if c == "\\" and not raw and i + 1 < n:
                out.append(line[i:i + 2]); i += 2; continue
            if raw and line.startswith('"""', i):
                out.append('"""'); i += 3; in_str = False; continue
            if not raw and c == '"':
                in_str = False
            out.append(c); i += 1; continue
        if c == "#":
            out.append(line[i:]); break
        if line.startswith('"""', i):
            out.append('"""'); i += 3; in_str = raw = True; continue
        if c == '"':
            in_str = True; out.append(c); i += 1; continue
        if c == "{" and i + 1 < n and line[i + 1] == "}":
            out.append("{->}"); i += 2; hits += 1; continue
        out.append(c); i += 1
    return "".join(out), hits


def main():
    total = 0
    for dirpath, _, files in os.walk("tests"):
        for fn in sorted(files):
            if not fn.endswith(".rav"):
                continue
            p = os.path.join(dirpath, fn)
            with open(p, encoding="utf-8", newline="") as f:
                lines = f.read().split("\n")
            in_expected = False
            touched = []
            for k, line in enumerate(lines):
                if line.strip() == SEP:
                    in_expected = True
                    continue
                if in_expected:
                    continue
                new, hits = migrate_line(line)
                if hits:
                    lines[k] = new
                    touched.append((k + 1, hits))
            if touched:
                with open(p, "w", encoding="utf-8", newline="") as f:
                    f.write("\n".join(lines))
                n = sum(h for _, h in touched)
                total += n
                print("%s: %d 处 %s" % (p.replace(os.sep, "/"), n, [l for l, _ in touched]))
    print("共 %d 处" % total)


if __name__ == "__main__":
    main()
