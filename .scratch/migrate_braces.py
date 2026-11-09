#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 .rav 源码里"当空字典用"的 `{}` 改成 `{->}`（`{}` 现在是空集合）。

只动**代码部分**：`#` 之后的行尾注释和引号里的串都不碰（格式串里全是 `{}`，动不得）。
用法: python .scratch/migrate_braces.py lib examples
"""
import os
import sys


def migrate_line(line):
    out = []
    i, n = 0, len(line)
    in_str = False
    raw = False
    hits = 0
    while i < n:
        c = line[i]
        if in_str:
            if c == "\\" and not raw and i + 1 < n:
                out.append(line[i:i + 2])
                i += 2
                continue
            if raw and line.startswith('"""', i):
                out.append('"""')
                i += 3
                in_str = False
                continue
            if not raw and c == '"':
                in_str = False
            out.append(c)
            i += 1
            continue
        if c == "#":
            out.append(line[i:])
            break
        if line.startswith('"""', i):
            out.append('"""')
            i += 3
            in_str = True
            raw = True
            continue
        if c == '"':
            in_str = True
            out.append(c)
            i += 1
            continue
        if c == "{" and i + 1 < n and line[i + 1] == "}":
            out.append("{->}")
            i += 2
            hits += 1
            continue
        out.append(c)
        i += 1
    return "".join(out), hits


def main(roots):
    for root in roots:
        for dirpath, _, files in os.walk(root):
            for fn in sorted(files):
                if not fn.endswith(".rav"):
                    continue
                p = os.path.join(dirpath, fn)
                with open(p, encoding="utf-8", newline="") as f:
                    raw_text = f.read()
                lines = raw_text.split("\n")
                touched = []
                for k, line in enumerate(lines):
                    new, hits = migrate_line(line)
                    if hits:
                        lines[k] = new
                        touched.append(k + 1)
                if touched:
                    with open(p, "w", encoding="utf-8", newline="") as f:
                        f.write("\n".join(lines))
                    print("%s: %d 处 %s" % (p.replace(os.sep, "/"), len(touched), touched))


if __name__ == "__main__":
    main(sys.argv[1:])
