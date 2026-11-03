# Draws docs/images/src/ai-workflow.svg (render to PNG with render.cjs). Edit the boxes here, then re-run.
W = 1200
INK, MUTED = "#16202a", "#5b6570"
STYLE = {
    "human": ("#fdf1ea", "#c2410c"),
    "orch":  ("#e4ecf7", "#1e4e8c"),
    "doc":   ("#f4f3ef", "#8a857a"),
    "agent": ("#fff4e8", "#e08a3c"),
    "build": ("#eef7ee", "#3f9a5b"),
    "test":  ("#f3eefb", "#7c5cc4"),
    "check": ("#e3f1e8", "#1f7a4d"),
    "done":  ("#16202a", "#16202a"),
}
out = []
def box(cx, y, w, h, kind, title, sub="", r=10, size=17):
    fill, stroke = STYLE[kind]
    color = "#ffffff" if kind == "done" else INK
    subc = "#c9d1d9" if kind == "done" else MUTED
    out.append(f'<rect x="{cx-w/2}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="{fill}" stroke="{stroke}" stroke-width="2"/>')
    lines = sub.split("|") if sub else []
    total = 1 + len(lines)
    top = y + h/2 - (total-1)*10
    out.append(f'<text x="{cx}" y="{top+6}" text-anchor="middle" font-size="{size}" font-weight="700" fill="{color}">{title}</text>')
    for i, l in enumerate(lines):
        out.append(f'<text x="{cx}" y="{top+6+(i+1)*21}" text-anchor="middle" font-size="14" fill="{subc}">{l}</text>')
def line(x1, y1, x2, y2, dash=False, color=MUTED, arrow=True):
    d = ' stroke-dasharray="6 5"' if dash else ""
    m = ' marker-end="url(#a)"' if arrow else ""
    out.append(f'<path d="M{x1} {y1} L{x2} {y2}" stroke="{color}" stroke-width="2" fill="none"{d}{m}/>')
def path(d, dash=False, color=MUTED):
    da = ' stroke-dasharray="6 5"' if dash else ""
    out.append(f'<path d="{d}" stroke="{color}" stroke-width="2" fill="none"{da} marker-end="url(#a)"/>')
def label(x, y, t, anchor="middle", color=MUTED, size=14, weight=400):
    out.append(f'<text x="{x}" y="{y}" text-anchor="{anchor}" font-size="{size}" font-weight="{weight}" fill="{color}">{t}</text>')

cx = W/2
label(cx, 50, "Human-led, contract-first AI workflow", size=30, weight=800, color=INK)
label(cx, 80, "How this project was designed and built with AI agents", size=16)

y = 110
box(cx, y, 340, 64, "human", "Me (the owner)", "goal · product decisions · final approval", r=32); y += 64
line(cx, y, cx, y+30); y += 30
box(cx, y, 420, 64, "doc", "1. Brainstorm with Claude (chat)", "options and trade-offs → I choose"); y += 64
line(cx, y, cx, y+30); y += 30
box(cx, y, 420, 64, "doc", "2. Build plan → docs/SPEC.md", "one source of truth, reviewed by me"); y += 64
line(cx, y, cx, y+30); y += 30
orch_y = y
box(cx, y, 420, 64, "orch", "3. Orchestrator (Claude Code)", "splits the work · reviews every result"); y += 64
line(cx, y, cx, y+30); y += 30
box(cx, y, 420, 64, "doc", "4. Feature contract", "endpoints · JSON shapes · statuses · errors"); y += 64

# fan out to three agents
xs = [cx-380, cx, cx+380]
fan = y + 28
line(cx, y, cx, fan, arrow=False)
line(xs[0], fan, xs[2], fan, arrow=False)
for x in xs: line(x, fan, x, fan+24)
y = fan + 24
agents = [("Backend agent", "API + database"), ("Frontend agent", "screens + simulated API"), ("Database agent", "schema · migrations · seed")]
agent_y = y
for x, (t, s) in zip(xs, agents): box(x, y, 300, 60, "agent", t, s)
y += 60
pairs = [(("Build", "C# code"), ("Test", "real SQL Server")),
         (("Build", "React code"), ("Test", "UI + parity tests")),
         (("Build", "EF migrations"), ("Test", "migration tests"))]
fan2 = y + 24
for x in xs:
    line(x, y, x, fan2, arrow=False)
    line(x-78, fan2, x+78, fan2, arrow=False)
    line(x-78, fan2, x-78, fan2+20); line(x+78, fan2, x+78, fan2+20)
y = fan2 + 20
for x, (b, t) in zip(xs, pairs):
    box(x-78, y, 140, 54, "build", b[0], b[1], size=15)
    box(x+78, y, 140, 54, "test", t[0], t[1], size=15)
y += 54
merge = y + 28
for x in xs:
    line(x-78, y, x-78, merge-12, arrow=False); line(x+78, y, x+78, merge-12, arrow=False)
    line(x-78, merge-12, x+78, merge-12, arrow=False); line(x, merge-12, x, merge, arrow=False)
line(xs[0], merge, xs[2], merge, arrow=False)
line(cx, merge, cx, merge+26)
y = merge + 26
check_y = y
box(cx, y, 460, 64, "check", "5. Orchestrator checks", "all tests · real app · clicks through it in a browser"); y += 64
line(cx, y, cx, y+30); y += 30
rev_y = y
box(cx, y, 460, 64, "check", "6. Independent reviewer agent", "did not write the code · ranks problems"); y += 64
line(cx, y, cx, y+30); label(cx+10, y+20, "clean", anchor="start"); y += 30
dec_y = y
box(cx, y, 420, 64, "human", "7. My decision", "approve, or choose between options", r=32); y += 64
line(cx, y, cx, y+30); label(cx+10, y+20, "approved", anchor="start"); y += 30
box(cx, y, 360, 56, "done", "Commit · update docs · sync", ""); y += 56

# loop: reviewer -> orchestrator (right side)
rx = cx + 230
px = W - 40
path(f"M{rx} {rev_y+32} L{px} {rev_y+32} L{px} {orch_y+32} L{cx+210+8} {orch_y+32}", color="#c2410c")
label(px-12, check_y+26, "problems found:", anchor="end", color="#c2410c", weight=600)
label(px-12, check_y+46, "fix round", anchor="end", color="#c2410c", weight=600)
# loop: agents ask the owner (left side, dashed)
lx = 40
path(f"M{xs[0]-150} {agent_y+30} L{lx} {agent_y+30} L{lx} {dec_y+32} L{cx-210-8} {dec_y+32}", dash=True, color="#c2410c")
label(lx+10, dec_y-20, "unclear? agents ask me,", anchor="start", color="#c2410c", weight=600)
label(lx+10, dec_y, "they never guess", anchor="start", color="#c2410c", weight=600)
# decision -> orchestrator (decision feeds back)
H = y + 30
svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" font-family="Inter, 'Segoe UI', Arial, sans-serif">
<defs><marker id="a" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0 0 L10 5 L0 10 z" fill="context-stroke"/></marker></defs>
<rect width="100%" height="100%" fill="#ffffff"/>
{chr(10).join(out)}
</svg>'''
open(__file__.replace("ai_workflow.py", "ai-workflow.svg"), "w").write(svg)
print("height", H)
