#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把文档/注释里"`{}` 是空字典"的旧说法改掉，并在该说清楚的地方补上 `{->}`。"""
import io
import sys

EDITS = [
    ("docs/tutorial/02-types.md",
     "注意：大括号两种都用 —— `{1 2 3}` 是集合，`{\"a\"->1}` 是字典（`->` 左边是键，右边是值）。",
     "注意：大括号两种都用 —— `{1 2 3}` 是集合，`{\"a\"->1}` 是字典（`->` 左边是键，右边是值）。"
     "空的两样也分开写：`{}` 是空集合，`{->}` 是空字典（判据就是那个箭头，没有条目也得写出来）。"),
    ("docs/tutorial/05-functions.md", "d := {}", "d := {->}"),
    ("docs/tutorial/07-collections.md",
     "rows := db.All \"select * from t\" {}", "rows := db.All \"select * from t\" {->}"),
    ("docs/tutorial/07-collections.md", "没有参数就写 `{}`", "没有参数就写 `{->}`"),
    ("docs/tutorial/07-collections.md", "(Html.El \"h2\" {} [title])", "(Html.El \"h2\" {->} [title])"),
    ("docs/tutorial/07-collections.md", "(Html.El \"p\" {} [body])", "(Html.El \"p\" {->} [body])"),
    ("lib/numbers.rav",
     "# 都是个空块(`{ () }`:`{}` 单行是空字典,多行空块本来就禁止)。",
     "# 都是个空块(`{ () }`;空块写不出来 —— `{}` 是空集合、`{->}` 是空字典,多行空块也禁止)。"),
    ("lib/sqlite.rav", "没有参数就写 `{}`", "没有参数就写 `{->}`"),
    ("lib/html.rav", "`{\"class\": \"card\"}`", "`{\"class\"-> \"card\"}`"),
    ("lib/html.rav", "#             (Html.El \"h2\" {} [title])", "#             (Html.El \"h2\" {->} [title])"),
    ("lib/html.rav", "#             (Html.El \"p\" {} [body])", "#             (Html.El \"p\" {->} [body])"),
    ("lib/html.rav", "`{\"kind\": \"element\" \"name\": … \"attrs\": {} \"kids\": []}`",
     "`{\"kind\"-> \"element\" \"name\"-> … \"attrs\"-> {->} \"kids\"-> []}`"),
    ("lib/html.rav", "`[(El \"h2\" {} [\"x\"]) (El \"p\" {} [\"y\"])]`",
     "`[(El \"h2\" {->} [\"x\"]) (El \"p\" {->} [\"y\"])]`"),
    ("tests/lang/99_collections.rav",
     "# 单行 `{}` 是空字典。以前它报「不允许空的 '{ }'」,于是空集合的三种写法不一致:\n"
     "# 空列表能写 [],空字典只能写 `dict default`。\n"
     "# 空块本来就禁止(见 tests/25),所以把 `{}` 给空字典没有歧义;\n"
     "# 跨行的 `{\\n}` 仍走块那条路,照旧报错。",
     "# 空的两样各有写法:`{}` 是空集合,`{->}` 是空字典 —— 字典的判据是那个箭头,\n"
     "# 没有条目也得把它写出来。空块本来就禁止(见 tests/25),三样不打架。\n"
     "# (历史上 `{}` 曾是空字典,所以这份用例原来叫 161_empty_dict。)"),
]


def main():
    bad = 0
    for path, old, new in EDITS:
        with io.open(path, encoding="utf-8", newline="") as f:
            text = f.read()
        n = text.count(old)
        if n == 0:
            print("!! 没找到: %s << %s" % (path, old[:40]))
            bad += 1
            continue
        with io.open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text.replace(old, new))
        print("%s: %d 处" % (path, n))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
