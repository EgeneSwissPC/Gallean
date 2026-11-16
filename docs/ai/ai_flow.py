"""Gallean world AI: what one AI turn does (UMLet flow diagram, zoom 10 => px).

Usage: python3 ai_flow.py GalleanAI.uxf, then export with UMLet:
java -Djava.awt.headless=true -jar umlet.jar -action=convert -format=pdf -filename=GalleanAI.uxf
"""
import sys
from xml.sax.saxutils import escape

els = []
S = "fontsize=12"
LOOP, ORDER, ECON, STACK, INFO, BAD = "#cfe8cf", "#dbe7f6", "#fff0a8", "#f6dfc4", "#e6e6e6", "#f4b6b6"
W = 380
COL = {"A": 20, "B": 560, "C": 1100, "D": 1640}
box = {}


def el(kind, x, y, w, h, text, extra=""):
    els.append((kind, x, y, w, h, text, extra))


def b(key, col, y, text, bg, w=W):
    lines = text.count("\n") + 1
    h = lines * 15 + 22
    x = COL[col] if isinstance(col, str) else col
    el("UMLClass", x, y, w, h, f"{text}\nhalign=left\nbg={bg}\n{S}")
    box[key] = (x, y, w, h)


def rel(points, text="", lt="->"):
    xs, ys = [p[0] for p in points], [p[1] for p in points]
    rx, ry = min(xs) - 10, min(ys) - 10
    w, h = max(xs) - rx + 10, max(ys) - ry + 10
    attrs = ";".join(f"{px - rx};{py - ry}" for px, py in points)
    els.append(("Relation", rx, ry, w, h, f"lt={lt}\n{text}".rstrip("\n"), attrs))


def L(k, f=0.5): x, y, w, h = box[k]; return (x, y + h * f)
def R(k, f=0.5): x, y, w, h = box[k]; return (x + w, y + h * f)
def T(k, f=0.5): x, y, w, h = box[k]; return (x + w * f, y)
def B(k, f=0.5): x, y, w, h = box[k]; return (x + w * f, y + h)


el("Text", 20, 10, 2000, 40, "fontsize=20\n*Gallean world AI – what happens in one AI turn*")
el("Text", 20, 45, 2000, 30, "fontsize=12\nsrc/Gallean.Engine/World/Ai · one action per call, repeated until the AI ends its turn")

# Column A: the turn loop
b("start", "A", 100, "*Start of the AI's turn*\n--\nthe game calls WorldAiController.DecideAction\nonce per action; the AI's memory carries\norders and per-turn counters between calls", LOOP)
b("orders", "A", 250, "*Step 1  Set orders*\n(WorldAiOrderPlanner)\n--\nevery own army gets an order for this action:\nwhat it is trying to do right now", LOOP)
b("econ", "A", 400, "*Step 2  Spend*\n(playable races only)\n--\nscore every economy action, take the best;\nif it can be done → do it, back to Step 1\nif the best one CAN'T be done → nothing is\nbought this step (no fallback to the 2nd best)\n= how the AI saves up", LOOP)
b("stacks", "A", 625, "*Step 3  Move armies*\n--\nfor each own army (not in a rioting city,\nnot marked done this turn):\nscore every army action under its order,\ntake the best that can be done → do it\nnothing to do → army marked done", LOOP)
b("end", "A", 850, "*End turn*\n--\nall armies done, or 100 actions used", LOOP)

# Column B: orders
b("ordlist", "B", 100, "*Orders the AI gives itself*\n(for armies with the Normal order, first match wins)\n--\n1. Heal: hit points below 80 % (kept until 90 %)\n2. Explore: rod planter or thief leader\n3. Reinforce: party filled below 80 % of the\n    leader's leadership → go to the own city where\n    buying units helps, or stay in the city it is in\n4. Normal: everything else", ORDER)
b("ordfix", "B", 380, "*Orders from the map*\n--\nStand · Guard · Defend city/location/army\nMove to location · Attack army · Berserk ...\nset in the scenario: kept as they are", ORDER)

# Column B lower: move targets
b("move", "B", 540, "*Where an army moves*\n(MoveAction, by order)\n--\nNormal: known enemies, ruins, bags;\n    nothing known → explore\nReinforce: only to its target city\n    (already there → no move)\nHeal: to an own city\nExplore: into unexplored land\nMap orders: follow their own rule", ORDER)

# Column C: economy
b("ecolist", "C", 100, "*Economy actions*\n(higher priority always wins)\n--\nP4  Cast spell\nP3  Research spell\nP2  Hire leader   wants 1 fighter per own\n    city + 1 explorer, rod planter, thief;\n    short by ≤ 400 gold → 'save up' plan\nP1  Construct building   1 per turn\n    order: healing, guild, units, spells\nP0  Buy unit      1 per turn, into the city\n    garrison (AutoFit then moves it into\n    the army standing in the city)", ECON)
b("ecoscore", "C", 400, "*Unit purchase score*\n--\naffordable · melee preferred · not the\nsame unit again · free room in the city\n· own army waiting in the city", ECON)

# Column D: stack actions
b("stklist", "D", 100, "*Army actions*\n(higher priority always wins)\n--\nP6  Use item\nP5  Resurrect units (in a city)\nP4  Heal units (in a city)\nP3  AutoFit: garrison units into the party\nP2  Attack · Thief · Rod · Enter city ·\n    Take bag\nP1  Move\nP0  Change order", STACK)
b("attack", "D", 330, "*Attack score*\n--\nonly targets it has explored; estimates\nthe battle first (WorldAiEstimator) and\nskips fights its power can't win; weighs\nvictory, survival, units and XP kept or\nlost, distance, target interest/priority", STACK)

# Info boxes
b("score", "C", 540, "*How a plan is scored*\n--\nWorldAiActionScore = (Priority, Value)\npriority compared first; Value 0–1000 is\nthe product of the action's considerations\n(utility curves)", INFO)
b("know", "D", 540, "*What the AI knows*\n--\nonly explored tiles count as targets (fog)\nmap goals ('objectives') come only from\nscenario events that win the game, and only\nfrom turn 60/50/40/30 (easy → very hard)\ncustom maps have no events → no goals", INFO)

el("UMLNote", COL["D"], 800, W, 110, f"Legend\ngreen = turn loop\nblue = orders and movement\nyellow = spending\norange = army actions\ngrey = scoring and knowledge\n{S}")

# arrows: loop
rel([B("start"), T("orders")])
rel([B("orders"), T("econ")])
rel([B("econ"), T("stacks")], "nothing bought")
rel([B("stacks"), T("end")], "all done")
lx = COL["A"] - 10
rel([L("econ", 0.4), (lx, L("econ", 0.4)[1]), (lx, L("orders", 0.5)[1]), L("orders", 0.5)], "")
rel([L("stacks", 0.6), (lx + 5, L("stacks", 0.6)[1]), (lx + 5, L("orders", 0.7)[1]), L("orders", 0.7)], "")
# to detail columns
rel([R("orders", 0.4), L("ordlist", 0.6)], "decides")
ey = 500
rel([(COL["A"] + W, ey), (COL["C"] - 50, ey), (COL["C"] - 50, L("ecolist", 0.5)[1]), L("ecolist", 0.5)], "uses")
rel([R("stacks", 0.4), L("move", 0.6)], "moves")
sy = 772
rel([B("stacks", 0.9), (B("stacks", 0.9)[0], sy), (COL["D"] - 50, sy), (COL["D"] - 50, L("stklist", 0.5)[1]), L("stklist", 0.5)], "uses")
rel([B("ecolist"), T("ecoscore")])
rel([B("stklist"), T("attack")])
rel([B("ordlist", 0.5), T("ordfix", 0.5)], "or")


def render():
    out = ['<?xml version="1.0" encoding="UTF-8" standalone="no"?>',
           '<diagram program="umlet" version="15.1">', '  <zoom_level>10</zoom_level>']
    for kind, x, y, w, h, text, extra in els:
        out += ["  <element>", f"    <id>{kind}</id>", "    <coordinates>",
                f"      <x>{int(x)}</x>", f"      <y>{int(y)}</y>", f"      <w>{int(w)}</w>", f"      <h>{int(h)}</h>",
                "    </coordinates>", f"    <panel_attributes>{escape(text)}</panel_attributes>",
                f"    <additional_attributes>{extra}</additional_attributes>" if extra else "    <additional_attributes/>",
                "  </element>"]
    out.append("</diagram>")
    return "\n".join(out) + "\n"


open(sys.argv[1], "w", encoding="utf-8").write(render())
print("wrote", sys.argv[1], len(els))
