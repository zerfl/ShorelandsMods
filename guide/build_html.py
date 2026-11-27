"""Turns guide.md into one self-contained HTML page: build_html.py guide.md out.html

Only the Markdown the guide uses: # headings with {#id}, paragraphs, - and 1. lists (one level, - [ ] checkboxes),
> notes, | tables |, ``` fences (words after the language, like sample=x file=y, name the sample and are not shown),
`code`, **bold**, *italic*, [links](#id), and [TOC] for the list of ## sections.
"""
import html
import re
import sys

CSS = """
:root { --bg: #fbfaf7; --fg: #1d1f21; --muted: #5b6168; --line: #dcd8cf; --code: #f0ede6; --note: #eef5fb;
  --noteline: #9cc3e4; --link: #1f5f99; }
@media (prefers-color-scheme: dark) {
  :root { --bg: #1b1d20; --fg: #e6e3dc; --muted: #a3a8ae; --line: #3a3d42; --code: #26292d; --note: #1f2a33;
    --noteline: #3f6c8f; --link: #8cc4f2; }
}
html { background: var(--bg); }
body { color: var(--fg); background: var(--bg); font: 17px/1.6 "Segoe UI", system-ui, sans-serif; max-width: 48em;
  margin: 0 auto; padding: 1.5em 16px 4em; }
h1 { font-size: 2em; margin: 0.2em 0 0.4em; }
h2 { font-size: 1.45em; margin-top: 2.2em; padding-top: 0.6em; border-top: 1px solid var(--line); }
h3 { font-size: 1.15em; margin-top: 1.8em; }
a { color: var(--link); }
code { font: 0.9em Consolas, "Cascadia Mono", monospace; background: var(--code); padding: 0.1em 0.3em;
  border-radius: 3px; }
pre { background: var(--code); padding: 0.8em 1em; border-radius: 6px; overflow-x: auto; line-height: 1.45; }
pre code { background: none; padding: 0; font-size: 0.88em; }
pre .file { display: block; color: var(--muted); font-size: 0.8em; margin-bottom: 0.4em; }
blockquote { margin: 1em 0; padding: 0.6em 1em; background: var(--note); border-left: 4px solid var(--noteline);
  border-radius: 0 6px 6px 0; }
blockquote p { margin: 0.3em 0; }
table { border-collapse: collapse; margin: 1em 0; display: block; overflow-x: auto; }
th, td { border: 1px solid var(--line); padding: 0.35em 0.6em; text-align: left; vertical-align: top; }
th { background: var(--code); }
li { margin: 0.25em 0; }
ul.checks { list-style: none; padding-left: 0.4em; }
nav.toc { background: var(--code); padding: 0.6em 1.2em; border-radius: 6px; }
nav.toc ol { margin: 0.3em 0; }
"""


def inline(text):
    out, parts = [], re.split(r"(`[^`]+`)", text)
    for p in parts:
        if p.startswith("`") and p.endswith("`") and len(p) > 1:
            out.append("<code>" + html.escape(p[1:-1]) + "</code>")
            continue
        p = html.escape(p, quote=False)
        p = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", p)
        p = re.sub(r"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", r"<em>\1</em>", p)
        p = re.sub(r"\[([^\]]+)\]\(([^)\s]+)\)", r'<a href="\2">\1</a>', p)
        out.append(p)
    return "".join(out)


def heading(line):
    m = re.match(r"(#+)\s+(.*?)\s*(?:\{#([\w-]+)\})?$", line)
    level, text, hid = len(m.group(1)), m.group(2), m.group(3)
    if not hid:
        hid = re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")
    return level, text, hid


def fence_attrs(info):
    words = info.split()
    lang = words[0] if words and "=" not in words[0] else ""
    attrs = dict(re.findall(r'(\w+)=("[^"]*"|\S+)', info))
    return lang, {k: v.strip('"') for k, v in attrs.items()}


def convert(md):
    lines = md.split("\n")
    body, toc, title = [], [], ""
    i = 0

    def para_until(start):
        j = start
        while j < len(lines) and lines[j].strip() and not re.match(r"(#|```|>|\||- |\d+\. )", lines[j]):
            j += 1
        return j

    while i < len(lines):
        line = lines[i]
        if not line.strip():
            i += 1
        elif line.startswith("```"):
            lang, attrs = fence_attrs(line[3:])
            j = i + 1
            while not lines[j].startswith("```"):
                j += 1
            code = "\n".join(lines[i + 1 : j])
            label = '<span class="file">' + html.escape(attrs["file"]) + "</span>" if "file" in attrs else ""
            body.append("<pre>" + label + "<code>" + html.escape(code, quote=False) + "</code></pre>")
            i = j + 1
        elif line.startswith("#"):
            level, text, hid = heading(line)
            if level == 1:
                title = text
            if level == 2:
                toc.append((hid, text))
            body.append("<h%d id=\"%s\">%s</h%d>" % (level, hid, inline(text), level))
            i += 1
        elif line.strip() == "[TOC]":
            body.append("\0TOC\0")
            i += 1
        elif line.startswith(">"):
            j = i
            quoted = []
            while j < len(lines) and lines[j].startswith(">"):
                quoted.append(lines[j][1:].strip())
                j += 1
            body.append("<blockquote><p>" + inline(" ".join(quoted)) + "</p></blockquote>")
            i = j
        elif line.startswith("|"):
            rows = []
            while i < len(lines) and lines[i].startswith("|"):
                rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
                i += 1
            head, rest = rows[0], [r for r in rows[1:] if not all(re.fullmatch(r":?-+:?", c) for c in r)]
            t = "<table><tr>" + "".join("<th>" + inline(c) + "</th>" for c in head) + "</tr>"
            for r in rest:
                t += "<tr>" + "".join("<td>" + inline(c) + "</td>" for c in r) + "</tr>"
            body.append(t + "</table>")
        elif re.match(r"(- |\d+\. )", line):
            ordered = not line.startswith("- ")
            items = []
            while i < len(lines) and re.match(r"(- |\d+\. )", lines[i]):
                text = re.sub(r"^(- |\d+\. )", "", lines[i])
                i += 1
                while i < len(lines) and lines[i].startswith("  ") and lines[i].strip():
                    text += " " + lines[i].strip()
                    i += 1
                items.append(text)
            checks = all(t.startswith("[ ] ") for t in items)
            if checks:
                items = ["&#9744; " + inline(t[4:]) for t in items]
            else:
                items = [inline(t) for t in items]
            tag = "ol" if ordered else "ul"
            cls = ' class="checks"' if checks else ""
            body.append("<%s%s>" % (tag, cls) + "".join("<li>" + t + "</li>" for t in items) + "</%s>" % tag)
        else:
            j = para_until(i)
            if j == i:
                j = i + 1
            body.append("<p>" + inline(" ".join(l.strip() for l in lines[i:j])) + "</p>")
            i = j
    nav = '<nav class="toc"><strong>Contents</strong><ol>' + "".join(
        '<li><a href="#%s">%s</a></li>' % (h, inline(t)) for h, t in toc
    ) + "</ol></nav>"
    page = "\n".join(nav if b == "\0TOC\0" else b for b in body)
    return (
        "<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">"
        '<meta name="viewport" content="width=device-width, initial-scale=1">'
        "<title>" + html.escape(title) + "</title><style>" + CSS + "</style></head>\n<body>\n" + page + "\n</body></html>\n"
    )


if __name__ == "__main__":
    src, dst = sys.argv[1], sys.argv[2]
    with open(src, encoding="utf-8") as f:
        out = convert(f.read())
    with open(dst, "w", encoding="utf-8", newline="\r\n") as f:
        f.write(out)
