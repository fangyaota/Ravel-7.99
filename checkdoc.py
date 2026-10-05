# -*- coding: utf-8 -*-
"""校对教程:把**成对**的 ```ravel 段跑一遍,和它后面那段「执行以上程序会输出如下结果」比对。

    python checkdoc.py                      # 全查 docs/tutorial/*.md
    python checkdoc.py docs/tutorial/05-functions.md ...

**只校对成对的块**(实例 + 输出)。没跟输出块的 ravel 块跳过 —— 那些是片段,不要求能跑。

**报错只比第一句**:跑出来的完整那坨里带着库的**绝对路径**(`bin/Debug/…`),那是钉不住的,
所以文档里也只写 `Error: …` 那一行。

要改教程就先跑这个(Debug 那份要现成:`bash build.sh --no-test`)。
"""
import io, os, subprocess, sys, tempfile, glob

ROOT = os.path.dirname(os.path.abspath(__file__))
RAVEL = os.path.join(ROOT, 'bin', 'Debug', 'net10.0', 'ravel.dll')
MARK = '执行以上程序会输出如下结果：'


def run(src):
    with tempfile.TemporaryDirectory() as d:
        p = os.path.join(d, 'x.rav')
        io.open(p, 'w', encoding='utf-8').write(src + '\n')
        r = subprocess.run(['dotnet', RAVEL, p], capture_output=True, cwd=d)
        raw = r.stdout.decode('gbk', errors='replace')
        if '── Output ──' in raw:
            raw = raw.split('── Output ──', 1)[1]
        return '\n'.join(l.rstrip() for l in raw.split('\n') if l.strip() != '').strip()


def check(path):
    lines = io.open(path, encoding='utf-8-sig').read().split('\n')
    i, n, ok, bad, checked = 0, len(lines), 0, [], 0
    while i < n:
        if lines[i].strip() != '```ravel':
            i += 1
            continue
        code, i = [], i + 1
        while i < n and lines[i].strip() != '```':
            code.append(lines[i])
            i += 1
        i += 1                                   # 跳过收尾的 ```
        j = i
        while j < n and lines[j].strip() == '':
            j += 1
        if j >= n or lines[j].strip() != MARK:
            continue                             # 片段:不校对
        j += 1
        while j < n and lines[j].strip() == '':
            j += 1
        if j >= n or lines[j].strip() != '```':
            continue
        want, at, j = [], j, j + 1
        while j < n and lines[j].strip() != '```':
            want.append(lines[j])
            j += 1
        got = run('\n'.join(code))
        want_s = '\n'.join(l.rstrip() for l in want).strip()
        checked += 1
        same = got == want_s
        if not same and want_s.startswith('Error:') and '\n' not in want_s:
            same = got.split('\n')[0].strip() == want_s
        if same:
            ok += 1
        else:
            # 块开头那一行(1 起) —— 报错里那句 `--> file:line` 指到这儿才找得到
            bad.append((at + 1, want_s, got))
    print(f'{path}: 校对 {checked} 段,对 {ok},错 {len(bad)}')
    for line, want, got in bad:
        print(f'  第 {line} 行那个块:')
        print('    want: ' + want.replace('\n', '\\n'))
        print('    got:  ' + got.replace('\n', '\\n'))
    return len(bad)


def main():
    paths = sys.argv[1:] or sorted(glob.glob(os.path.join(ROOT, 'docs', 'tutorial', '*.md')))
    if not os.path.exists(RAVEL):
        print(f'先编一份 Debug:`bash build.sh --no-test`(找不到 {RAVEL})', file=sys.stderr)
        return 2
    return 1 if sum(check(p) for p in paths) else 0


if __name__ == '__main__':
    sys.exit(main())
