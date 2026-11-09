#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Ravel 前端 fuzz：随机 token 流 → `ravel ast` → 找"不是语法错误"的那种失败。

两个判据:
  1) 解析这一趟：只有 SyntaxException 是设计如此；StackOverflow / 超时 / .NET 栈 = bug。
  2) 往返这一趟（`ast --source`）：解析得了的，印回源码再解析必须回到同一棵树 ——
     工具自己会报「印出来再解析,树对不上」。那一条也是 bug。

用法:
  python .scratch/fuzz/fuzz.py gen 3000 --seed 1
  python .scratch/fuzz/fuzz.py run          # 第一判据
  python .scratch/fuzz/fuzz.py roundtrip    # 第二判据（只跑 run 里 ok 的）
"""
import argparse
import json
import os
import random
import re
import shutil
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor

REPO = r"D:\Workspace\Ravel 7.99"
DLL = os.path.join(REPO, "out", "ravel.dll")
BASE = os.path.join(REPO, ".scratch", "fuzz")
CASES = os.path.join(BASE, "cases")
BATCH = os.path.join(BASE, "batches")
OKLIST = os.path.join(BASE, "ok.json")

ATOMS = ["a", "b", "x", "f", "g", "q", "m", "n", "self", "true", "false", "unit", "print"]
NUMS = ["0", "1", "2", "10", "2.5", "7n", "3i", "9f", "0.0"]
STRS = ['"s"', '"a b"', "''", "'c'", "'\\n'"]
BRACKETS = ["(", ")", "[", "]", "{", "}"]
BINOPS = ["+", "-", "*", "/", "%", "**", "==", "!=", "<", ">", "<=", ">=", "&&",
          "||", "|", "&", "^", "<<", ">>", "<<<", ">>>", ":", "<:", ":>", ".."]
MISC = ["=", ":=", "::=", "::", "=>", "<|", "|>", ":<", "@", ",", ".", "?.", "~",
        "??", "??=", "+=", "-=", "*=", "**=", "/=", "%=", "++", "--", ";", "\n"]
SOUP = ATOMS + NUMS + STRS + BRACKETS + BINOPS + MISC


# ---------------------------------------------------------------- 生成

def gen_soup(rnd):
    out = []
    for _ in range(rnd.randint(1, 30)):
        out.append(rnd.choice(SOUP))
        out.append(rnd.choice([" ", "", "", " ", "\n"]))
    return "".join(out)


def gen_expr(rnd, d):
    if d <= 0 or rnd.random() < 0.3:
        return rnd.choice(ATOMS + NUMS + STRS)
    c = rnd.randrange(10)
    if c == 0:
        return "f " + gen_expr(rnd, d - 1)
    if c == 1:
        return "(%s %s %s)" % (gen_expr(rnd, d - 1), rnd.choice(BINOPS), gen_expr(rnd, d - 1))
    if c == 2:
        return "%s[%s]" % (gen_expr(rnd, d - 1), gen_expr(rnd, d - 1))
    if c == 3:
        return "%s.m" % gen_expr(rnd, d - 1)
    if c == 4:
        return "[%s, %s]" % (gen_expr(rnd, d - 1), gen_expr(rnd, d - 1))
    if c == 5:
        return '{"k" -> ' + gen_expr(rnd, d - 1) + "}"
    if c == 6:
        return "%s ?. m" % gen_expr(rnd, d - 1)
    if c == 7:
        return "%s ?? %s" % (gen_expr(rnd, d - 1), gen_expr(rnd, d - 1))
    if c == 8:
        return "~" + gen_expr(rnd, d - 1)
    return gen_expr(rnd, d - 1)


def gen_struct(rnd):
    lines = []
    for _ in range(rnd.randint(1, 4)):
        r = rnd.random()
        if r < 0.5:
            lines.append("%s := %s" % (rnd.choice(ATOMS), gen_expr(rnd, rnd.randint(1, 4))))
        elif r < 0.8:
            lines.append(gen_expr(rnd, rnd.randint(1, 4)))
        else:
            lines.append("@l while {%s} {%s}" % (gen_expr(rnd, 2), gen_expr(rnd, 2)))
    return "\n".join(lines) + "\n"


MULTI = sorted([":<", ":>", "<:", ":=", "::=", "::", "->", "=>", "==", "!=", "<<<", ">>>",
                "<<", ">>", "<=", "<|", "|>", ">=", "&&", "||", "++", "--", "+=", "-=",
                "**=", "**", "*=", "/=", "%=", "??=", "??", "?.", ".."], key=len, reverse=True)


def lex_rough(src):
    """够用的切分：token 用 '' 拼回去 == 原文（空白也是 token）。"""
    out, i, n = [], 0, len(src)
    while i < n:
        if src.startswith('"""', i):
            j = src.find('"""', i + 3)
            j = n if j < 0 else j + 3
            out.append(src[i:j]); i = j; continue
        c = src[i]
        if c in "\"'":
            j = i + 1
            while j < n and src[j] != c and src[j] != "\n":
                j += 2 if src[j] == "\\" else 1
            if j < n and src[j] == c:
                j += 1
            out.append(src[i:j]); i = j; continue
        if c == "#":
            j = src.find("\n", i)
            j = n if j < 0 else j
            out.append(src[i:j]); i = j; continue
        if c.isspace():
            j = i + 1
            while j < n and src[j].isspace():
                j += 1
            out.append(src[i:j]); i = j; continue
        op = next((o for o in MULTI if src.startswith(o, i)), None)
        if op:
            out.append(op); i += len(op); continue
        if c.isalnum() or c == "_":
            j = i + 1
            while j < n and (src[j].isalnum() or src[j] == "_"):
                j += 1
            out.append(src[i:j]); i = j; continue
        out.append(c); i += 1
    return out


_CORPUS = None


def corpus():
    global _CORPUS
    if _CORPUS is None:
        files = []
        for root in ("lib", "tests", "examples"):
            for dp, _, fns in os.walk(os.path.join(REPO, root)):
                for fn in fns:
                    if fn.endswith(".rav"):
                        files.append(os.path.join(dp, fn))
        _CORPUS = files
    return _CORPUS


def gen_mutate(rnd):
    path = rnd.choice(corpus())
    with open(path, encoding="utf-8", errors="replace") as f:
        toks = lex_rough(f.read())
    if len(toks) < 4:
        return "a\n"
    for _ in range(rnd.randint(1, 4)):
        k = rnd.randrange(5)
        idx = [i for i, t in enumerate(toks) if not t.isspace()] or [0]
        j = rnd.choice(idx)
        if k == 0 and len(toks) > 2:
            del toks[j]
        elif k == 1:
            toks.insert(j, toks[j])
        elif k == 2:
            toks.insert(j, rnd.choice(SOUP))
        elif k == 3:
            toks[j] = rnd.choice(SOUP)
        else:
            toks.insert(j, rnd.choice(["(", "[", "{", ")", "]", "}", '"', "'", "/*", "#"]))
    return "".join(toks)


def gen_lines(rnd):
    """从语料里抽几行拼起来 —— 大多能解析，落进解析器的深处。"""
    out = []
    for _ in range(rnd.randint(1, 8)):
        with open(rnd.choice(corpus()), encoding="utf-8", errors="replace") as f:
            lines = [l for l in f.read().split("\n")
                     if l.strip() and not l.lstrip().startswith("#")]
        if lines:
            out.append(rnd.choice(lines))
    return "\n".join(out) + "\n"


NASTY = [
    '"abc\n', "'a\n", '"""abc\n', '"""\n  a\n b"""\n', '"${a"\n', '"${a}"\n',
    '"${}"\n', '"${${a}}"\n', '"\\${a}"\n', '"$"\n', '"$5"\n', '"$name"\n',
    "1.2.3\n", "2n1\n", "2nf\n", "0x1F\n", "'ab'\n", "''\n", "'\\u{1F600}'\n",
    "?\n", "$\n", "\\\n", "\x00\n", "#\n", "# just a comment", "{\n", "}\n", "a :\n",
    ":\n", "::\n", ":=\n", "a :=\n", "f(\n", "f (1,\n", "[1..]\n", "(1..)\n",
    "a?.\n", "a ??\n", "1..\n", '{"a" -> }\n', "a.b.\n", "(((1\n", "1]]]\n",
    "\ufeffx := 1\n", "\ufeff\n\ufeffx := 1\n", "a\x00b\n", "ｆｕｌｌｗｉｄｔｈ := 1\n",
    "变量 := 1\n", "e\u0301 := 2\n", "x := '\\uD800'\n", '"\\uD800"\n',
    "x := 1 .. 2\n", "x := ..\n", "x := f <|\n", "x := |>\n", "x := :<\n",
    "a.+\n", "+.2\n", ":.int\n", "a.+ := f\n", "loc := 1\n", "x := y?.\n",
]


def gen_all(n, seed=0):
    rnd = random.Random(seed)
    shutil.rmtree(CASES, ignore_errors=True)
    os.makedirs(CASES, exist_ok=True)
    manifest = []
    for i in range(n):
        r = rnd.random()
        if i < len(NASTY):
            mode, src = "nasty", NASTY[i]
        elif r < 0.30:
            mode, src = "mutate", gen_mutate(rnd)
        elif r < 0.60:
            mode, src = "lines", gen_lines(rnd)
        elif r < 0.80:
            mode, src = "struct", gen_struct(rnd)
        else:
            mode, src = "soup", gen_soup(rnd)
        name = "%05d.rav" % i
        with open(os.path.join(CASES, name), "w", encoding="utf-8", newline="") as f:
            f.write(src)
        manifest.append({"file": name, "mode": mode, "src": src[:300]})
    with open(os.path.join(BASE, "manifest.jsonl"), "w", encoding="utf-8") as f:
        for m in manifest:
            f.write(json.dumps(m, ensure_ascii=False) + "\n")
    print("生成 %d 个用例 → %s" % (n, CASES))


# ---------------------------------------------------------------- 跑

def run_one(path, timeout=60, extra=()):
    try:
        r = subprocess.run(["dotnet", DLL, "ast", *extra, path], cwd=REPO,
                           capture_output=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        return "BUG", "TIMEOUT"
    out = r.stdout.decode("utf-8", "replace")
    err = r.stderr.decode("utf-8", "replace")
    if "Stack overflow" in err or "Stack overflow" in out:
        return "CRASH", "StackOverflow"
    if r.returncode not in (0, 1):
        return "BUG", "exit=%d %s" % (r.returncode, err.strip()[:300])
    m = re.search(r"Unhandled exception\.\s*([^\n]+)", err + out)
    if m:
        return "BUG", m.group(1)
    if "对不上" in err or "解析不了" in err:
        return "BUG", [l for l in err.split("\n") if "对不上" in l or "解析不了" in l][0][:200]
    # 只有解析那一趟才看报错行：`--source` 印出来的是**源码**，字符串里带 "!!" 很正常
    if not extra and any(l.startswith("!! ") for l in out.split("\n")):
        return "syntax", ""
    return "ok", ""


def _batches(files, size, tag):
    shutil.rmtree(BATCH, ignore_errors=True)
    os.makedirs(BATCH, exist_ok=True)
    jobs = []
    for k in range(0, len(files), size):
        chunk = files[k:k + size]
        d = os.path.join(BATCH, "%s%04d" % (tag, k // size))
        os.makedirs(d, exist_ok=True)
        for f in chunk:
            shutil.copy(os.path.join(CASES, f), os.path.join(d, f))
        jobs.append((d, chunk))
    return jobs


def run_batches(files, size=40, extra=(), tag="b"):
    jobs = _batches(files, size, tag)

    def work(job):
        d, chunk = job
        try:
            r = subprocess.run(["dotnet", DLL, "ast", *extra, d], cwd=REPO,
                               capture_output=True, timeout=600)
        except subprocess.TimeoutExpired:
            return {f: ("BUG", "TIMEOUT(batch)") for f in chunk}
        out = r.stdout.decode("utf-8", "replace")
        err = r.stderr.decode("utf-8", "replace")
        res = {}
        if "Stack overflow" in err or "Unhandled exception" in err or "转储 %d 个文件" % len(chunk) not in err:
            for f in chunk:
                res[f] = run_one(os.path.join(CASES, f), extra=extra)
            return res
        seen = set()
        for line in err.split("\n"):
            if "对不上" in line or "解析不了" in line:
                m = re.search(r"([0-9]{5}\.rav)", line)
                if m:
                    res[m.group(1)] = ("BUG", line.strip()[:160])
                    seen.add(m.group(1))
        # 逐个文件的 stdout 段：`──── <路径> ────` 之后，以 `!! ` 开头的是报错。
        # `--source` 那趟印的是**源码**、标题写成注释（`# ──── …`），不按段判。
        bodies = {}
        if not extra:
            for s in out.split("\n──── ")[1:]:
                head, _, body = s.partition(" ────")
                name = head.replace("\\", "/").rsplit("/", 1)[-1]
                bodies[name] = body
        for f in chunk:
            if f in seen:
                continue
            b = bodies.get(f)
            if extra:
                res[f] = ("ok", "")
            elif b is None:
                res[f] = run_one(os.path.join(CASES, f), extra=extra)
            elif b.lstrip("\n").startswith("!! "):
                res[f] = ("syntax", "")
            else:
                res[f] = ("ok", "")
        return res

    results = {}
    with ThreadPoolExecutor(max_workers=4) as ex:
        for part in ex.map(work, jobs):
            results.update(part)
    return results


def report(results, label):
    bad = {k: v for k, v in results.items() if v[0] in ("CRASH", "BUG")}
    print("[%s] 用例 %d：ok=%d syntax=%d CRASH=%d BUG=%d" % (
        label, len(results),
        sum(1 for v in results.values() if v[0] == "ok"),
        sum(1 for v in results.values() if v[0] == "syntax"),
        sum(1 for v in results.values() if v[0] == "CRASH"),
        sum(1 for v in results.values() if v[0] == "BUG")))
    for k, v in sorted(bad.items()):
        print("  %-6s %s %s" % (v[0], k, v[1][:150]))
    return bad


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=["gen", "run", "roundtrip", "all"])
    ap.add_argument("n", nargs="?", type=int, default=1000)
    ap.add_argument("--seed", type=int, default=0)
    a = ap.parse_args()
    if a.cmd in ("gen", "all"):
        gen_all(a.n, a.seed)
    if a.cmd in ("run", "all"):
        files = sorted(os.listdir(CASES))
        res = run_batches(files)
        report(res, "parse")
        ok = [f for f, v in res.items() if v[0] == "ok"]
        with open(OKLIST, "w", encoding="utf-8") as f:
            json.dump(sorted(ok), f)
        print("可解析的 %d 个留下（%s）" % (len(ok), OKLIST))
    if a.cmd == "roundtrip":
        with open(OKLIST, encoding="utf-8") as f:
            ok = json.load(f)
        res = run_batches(ok, size=40, extra=("--source",), tag="rt")
        report(res, "roundtrip")
