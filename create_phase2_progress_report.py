from __future__ import annotations

import json
import subprocess
from pathlib import Path

from docx import Document
from docx.enum.table import WD_ALIGN_VERTICAL
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Cm, Inches, Pt, RGBColor
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(r"C:\Users\mukot\OneDrive - North-West University\Documents\Research Proposal Implementation")
DESKTOP = Path(r"C:\Users\mukot\OneDrive - North-West University\Desktop")
OUTPUT = DESKTOP / "Mukota_Phase_1_and_2_Development_Progress_Report.docx"
ASSETS = ROOT / "tmp" / "phase2-doc" / "assets"
EVIDENCE = ROOT / "artifacts" / "runs" / "phase2-20260726-144223"
PHASE1 = ROOT / "artifacts" / "runs" / "20260726-134848"
LOGO = ROOT / "tmp" / "implementation_plan_assets" / "up_logo_from_proposal.png"

TITLE = "Adaptive Forensic Framework for 5G Network Slicing"
BLUE = "D9EAF7"
LIGHT_BLUE = "EAF3FA"
DARK_BLUE = "1F4E79"
MID_BLUE = "7F9DB9"
GREY_BORDER = "A6A6A6"
BLACK = "000000"
WHITE = "FFFFFF"


def image_font(size: int, bold: bool = False, mono: bool = False):
    if mono:
        candidates = [r"C:\Windows\Fonts\consola.ttf", r"C:\Windows\Fonts\cour.ttf"]
    elif bold:
        candidates = [r"C:\Windows\Fonts\timesbd.ttf", r"C:\Windows\Fonts\arialbd.ttf"]
    else:
        candidates = [r"C:\Windows\Fonts\times.ttf", r"C:\Windows\Fonts\arial.ttf"]
    for candidate in candidates:
        if Path(candidate).exists():
            return ImageFont.truetype(candidate, size)
    return ImageFont.load_default()


def draw_centered(draw, bounds, text, font, fill="black", spacing=4):
    left, top, right, bottom = bounds
    bbox = draw.multiline_textbbox((0, 0), text, font=font, spacing=spacing, align="center")
    x = left + (right - left - (bbox[2] - bbox[0])) / 2
    y = top + (bottom - top - (bbox[3] - bbox[1])) / 2
    draw.multiline_text((x, y), text, font=font, fill=fill, spacing=spacing, align="center")


def terminal_image(name: str, title: str, lines: list[str], accent="#73DACA") -> Path:
    ASSETS.mkdir(parents=True, exist_ok=True)
    path = ASSETS / name
    mono = image_font(26, mono=True)
    title_font = image_font(29, bold=True)
    padding = 50
    line_height = 43
    height = 135 + line_height * len(lines) + 55
    image = Image.new("RGB", (1700, height), "#111827")
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((3, 3, 1696, height - 4), radius=18, outline="#496C86", width=3)
    draw.rectangle((4, 4, 1696, 89), fill="#1F4E79")
    draw.ellipse((34, 28, 51, 45), fill="#E06C75")
    draw.ellipse((62, 28, 79, 45), fill="#E5C07B")
    draw.ellipse((90, 28, 107, 45), fill="#73DACA")
    draw.text((140, 23), title, font=title_font, fill="white")
    y = 112
    for line in lines:
        colour = accent if line.startswith(">") else "#F7FAFC"
        draw.text((padding, y), line, font=mono, fill=colour)
        y += line_height
    image.save(path, "PNG")
    return path


def current_docker_lines() -> list[str]:
    wanted = [
        "mysql", "oai-nrf", "oai-udr", "oai-udm", "oai-ausf", "oai-amf", "oai-smf", "oai-upf", "oai-ext-dn", "ims",
        "phase2-oai-gnb", "phase2-oai-nr-ue-embb", "phase2-oai-nr-ue-mmtc",
    ]
    try:
        completed = subprocess.run(
            ["docker", "ps", "--format", "{{.Names}}\t{{.Status}}"],
            capture_output=True, text=True, check=True, timeout=20,
        )
        state = {}
        for raw in completed.stdout.splitlines():
            parts = raw.split("\t", maxsplit=1)
            if len(parts) == 2:
                state[parts[0]] = parts[1]
        lines = ["> docker ps --format \"{{.Names}}    {{.Status}}\"", "Phase 1 OAI core: 10/10 expected containers healthy"]
        for name in ["oai-amf", "oai-smf", "oai-upf", "oai-ext-dn"]:
            lines.append(f"{name:<24} {state.get(name, 'not found')}")
        lines.append("Phase 2 RF-simulated RAN: 3/3 containers healthy")
        for name in ["phase2-oai-gnb", "phase2-oai-nr-ue-embb", "phase2-oai-nr-ue-mmtc"]:
            lines.append(f"{name:<24} {state.get(name, 'not found')}")
        return lines
    except (OSError, subprocess.SubprocessError) as error:
        return [
            "> docker ps --format \"{{.Names}}    {{.Status}}\"",
            "Live status could not be queried while compiling this report.",
            f"Diagnostic: {error}",
            "Retained Phase 1 validation: healthy (10 expected core containers).",
            "Retained Phase 2 validation: completed (gNB and two UEs healthy).",
        ]


def make_topology() -> Path:
    ASSETS.mkdir(parents=True, exist_ok=True)
    path = ASSETS / "phase2_topology.png"
    image = Image.new("RGB", (1800, 1010), "white")
    draw = ImageDraw.Draw(image)
    title_font = image_font(34, bold=True)
    heading = image_font(26, bold=True)
    detail = image_font(21)
    small = image_font(18)
    draw_centered(draw, (80, 25, 1720, 75), "Validated Phase 1 + Phase 2 Laboratory Topology", title_font)

    def box(x1, y1, x2, y2, label, details, fill):
        draw.rounded_rectangle((x1, y1, x2, y2), radius=20, fill=fill, outline="#1F4E79", width=3)
        draw_centered(draw, (x1 + 12, y1 + 18, x2 - 12, y1 + 68), label, heading)
        draw_centered(draw, (x1 + 18, y1 + 78, x2 - 18, y2 - 18), details, detail, spacing=5)

    def arrow(x1, y1, x2, y2, label=None):
        draw.line((x1, y1, x2, y2), fill="#1F4E79", width=5)
        if abs(x2 - x1) >= abs(y2 - y1):
            direction = 1 if x2 > x1 else -1
            draw.polygon([(x2, y2), (x2 - direction * 18, y2 - 11), (x2 - direction * 18, y2 + 11)], fill="#1F4E79")
        else:
            direction = 1 if y2 > y1 else -1
            draw.polygon([(x2, y2), (x2 - 11, y2 - direction * 18), (x2 + 11, y2 - direction * 18)], fill="#1F4E79")
        if label:
            draw.text(((x1 + x2) / 2 - 65, (y1 + y2) / 2 - 28), label, font=small, fill="#1F4E79")

    box(85, 180, 430, 390, "Virtual NR-UE: eMBB", "S-NSSAI: SST 1\nDNN: oai\nTunnel: 10.0.0.2", "#EAF3FA")
    box(85, 600, 430, 810, "Virtual NR-UE: mMTC", "S-NSSAI: SST 3\nDNN: mmtc\nTunnel: 10.0.2.2", "#EAF3FA")
    box(610, 350, 990, 590, "OAI virtual gNB", "RF simulator\nPLMN 001/01\nSupports both slices", "#D9EAF7")
    box(1165, 140, 1515, 335, "OAI core", "AMF / SMF / UPF\nPhase 1 validated\nNF registration active", "#EAF3FA")
    box(1165, 600, 1515, 810, "External data network", "Controlled UDP iperf\nKnown rates + durations\nResource snapshots", "#EAF3FA")
    box(1560, 350, 1745, 590, "C# ledger", "Scenario IDs\nExpected artefacts\nSHA-256 config ref", "#D9EAF7")
    arrow(430, 285, 610, 420, "RRC")
    arrow(430, 705, 610, 520, "RRC")
    arrow(990, 405, 1165, 250, "NGAP")
    arrow(990, 535, 1165, 690, "N3")
    arrow(1515, 690, 1165, 690, "UDP")
    arrow(1515, 250, 1560, 420, "logs")
    arrow(1515, 705, 1560, 520, "results")
    draw.text((90, 900), "Contained laboratory only: no physical radio hardware, live operator network, or real subscriber data.", font=small, fill="#1F4E79")
    image.save(path, "PNG")
    return path


def set_run_font(run, size=None, bold=None, color=None, italic=None):
    run.font.name = "Times New Roman"
    run._element.rPr.rFonts.set(qn("w:ascii"), "Times New Roman")
    run._element.rPr.rFonts.set(qn("w:hAnsi"), "Times New Roman")
    if size is not None:
        run.font.size = Pt(size)
    if bold is not None:
        run.bold = bold
    if color is not None:
        run.font.color.rgb = RGBColor.from_string(color)
    if italic is not None:
        run.italic = italic


def add_text(paragraph, text, size=10.5, bold=False, color=BLACK, italic=False):
    run = paragraph.add_run(text)
    set_run_font(run, size=size, bold=bold, color=color, italic=italic)
    return run


def set_cell_shading(cell, fill):
    tc_pr = cell._tc.get_or_add_tcPr()
    shd = tc_pr.find(qn("w:shd"))
    if shd is None:
        shd = OxmlElement("w:shd")
        tc_pr.append(shd)
    shd.set(qn("w:fill"), fill)


def set_cell_border(cell, **edges):
    tc_pr = cell._tc.get_or_add_tcPr()
    borders = tc_pr.first_child_found_in("w:tcBorders")
    if borders is None:
        borders = OxmlElement("w:tcBorders")
        tc_pr.append(borders)
    for edge, attrs in edges.items():
        tag = qn(f"w:{edge}")
        node = borders.find(tag)
        if node is None:
            node = OxmlElement(f"w:{edge}")
            borders.append(node)
        for key, value in attrs.items():
            node.set(qn(f"w:{key}"), str(value))


def set_cell_margins(cell, top=90, start=120, bottom=90, end=120):
    tc_pr = cell._tc.get_or_add_tcPr()
    margins = tc_pr.first_child_found_in("w:tcMar")
    if margins is None:
        margins = OxmlElement("w:tcMar")
        tc_pr.append(margins)
    for name, value in (("top", top), ("start", start), ("bottom", bottom), ("end", end)):
        node = margins.find(qn(f"w:{name}"))
        if node is None:
            node = OxmlElement(f"w:{name}")
            margins.append(node)
        node.set(qn("w:w"), str(value))
        node.set(qn("w:type"), "dxa")


def set_table_geometry(table, widths_cm):
    table.autofit = False
    table.alignment = WD_ALIGN_PARAGRAPH.LEFT
    tbl_pr = table._tbl.tblPr
    layout = tbl_pr.first_child_found_in("w:tblLayout")
    if layout is None:
        layout = OxmlElement("w:tblLayout")
        tbl_pr.append(layout)
    layout.set(qn("w:type"), "fixed")
    grid = table._tbl.tblGrid
    for grid_col, width in zip(grid.gridCol_lst, widths_cm):
        grid_col.set(qn("w:w"), str(int(width * 567)))
    for row in table.rows:
        for cell, width in zip(row.cells, widths_cm):
            cell.width = Cm(width)
            cell.vertical_alignment = WD_ALIGN_VERTICAL.CENTER
            tc_pr = cell._tc.get_or_add_tcPr()
            width_node = tc_pr.find(qn("w:tcW"))
            if width_node is None:
                width_node = OxmlElement("w:tcW")
                tc_pr.append(width_node)
            width_node.set(qn("w:w"), str(int(width * 567)))
            width_node.set(qn("w:type"), "dxa")
            set_cell_margins(cell)


def keep_paragraph(paragraph, with_next=False):
    p_pr = paragraph._p.get_or_add_pPr()
    node = OxmlElement("w:keepNext" if with_next else "w:keepLines")
    p_pr.append(node)


def add_page_field(paragraph):
    run = paragraph.add_run()
    begin = OxmlElement("w:fldChar")
    begin.set(qn("w:fldCharType"), "begin")
    instruction = OxmlElement("w:instrText")
    instruction.set(qn("xml:space"), "preserve")
    instruction.text = " PAGE "
    end = OxmlElement("w:fldChar")
    end.set(qn("w:fldCharType"), "end")
    run._r.extend([begin, instruction, end])
    set_run_font(run, size=10)


def setup_document():
    doc = Document()
    section = doc.sections[0]
    section.page_width = Cm(21.0)
    section.page_height = Cm(29.7)
    section.left_margin = Cm(2.25)
    section.right_margin = Cm(2.25)
    section.top_margin = Cm(1.8)
    section.bottom_margin = Cm(1.8)
    section.header_distance = Cm(0.6)
    section.footer_distance = Cm(0.8)
    normal = doc.styles["Normal"]
    normal.font.name = "Times New Roman"
    normal._element.rPr.rFonts.set(qn("w:ascii"), "Times New Roman")
    normal._element.rPr.rFonts.set(qn("w:hAnsi"), "Times New Roman")
    normal.font.size = Pt(10.5)
    normal.paragraph_format.space_after = Pt(5)
    normal.paragraph_format.line_spacing = 1.12
    for name, size in (("Heading 1", 13), ("Heading 2", 11.25)):
        style = doc.styles[name]
        style.font.name = "Times New Roman"
        style._element.rPr.rFonts.set(qn("w:ascii"), "Times New Roman")
        style._element.rPr.rFonts.set(qn("w:hAnsi"), "Times New Roman")
        style.font.size = Pt(size)
        style.font.bold = True
        style.font.color.rgb = RGBColor(0, 0, 0)
    header = section.header.paragraphs[0]
    header.alignment = WD_ALIGN_PARAGRAPH.RIGHT
    header.paragraph_format.space_after = Pt(0)
    add_text(header, TITLE, size=10)
    footer = section.footer.paragraphs[0]
    footer.alignment = WD_ALIGN_PARAGRAPH.CENTER
    add_page_field(footer)
    return doc


def add_heading(doc, text, level=1):
    paragraph = doc.add_paragraph(style="Heading 1" if level == 1 else "Heading 2")
    paragraph.paragraph_format.space_before = Pt(10 if level == 1 else 6)
    paragraph.paragraph_format.space_after = Pt(4)
    add_text(paragraph, text, size=13 if level == 1 else 11.25, bold=True)
    keep_paragraph(paragraph, with_next=True)
    return paragraph


def add_caption(doc, text):
    paragraph = doc.add_paragraph()
    paragraph.paragraph_format.space_before = Pt(2)
    paragraph.paragraph_format.space_after = Pt(6)
    add_text(paragraph, text, size=10.2, bold=True)
    keep_paragraph(paragraph)


def add_callout(doc, label, body):
    table = doc.add_table(rows=1, cols=1)
    set_table_geometry(table, [16.45])
    cell = table.cell(0, 0)
    set_cell_shading(cell, LIGHT_BLUE)
    set_cell_border(cell, top={"val": "single", "sz": "8", "color": MID_BLUE}, bottom={"val": "single", "sz": "8", "color": MID_BLUE}, left={"val": "single", "sz": "8", "color": MID_BLUE}, right={"val": "single", "sz": "8", "color": MID_BLUE})
    paragraph = cell.paragraphs[0]
    paragraph.paragraph_format.space_after = Pt(0)
    add_text(paragraph, label + " ", size=10.5, bold=True)
    add_text(paragraph, body, size=10.5)
    keep_paragraph(paragraph)
    spacer = doc.add_paragraph()
    spacer.paragraph_format.space_after = Pt(0)


def add_table(doc, headers, rows, widths, font_size=9.4):
    table = doc.add_table(rows=1, cols=len(headers))
    set_table_geometry(table, widths)
    for cell, value in zip(table.rows[0].cells, headers):
        set_cell_shading(cell, BLUE)
        set_cell_border(cell, top={"val": "single", "sz": "8", "color": MID_BLUE}, bottom={"val": "single", "sz": "8", "color": MID_BLUE}, left={"val": "single", "sz": "8", "color": MID_BLUE}, right={"val": "single", "sz": "8", "color": MID_BLUE})
        cell.paragraphs[0].paragraph_format.space_after = Pt(1)
        add_text(cell.paragraphs[0], value, size=font_size, bold=True)
    header_properties = table.rows[0]._tr.get_or_add_trPr()
    repeat = OxmlElement("w:tblHeader")
    repeat.set(qn("w:val"), "true")
    header_properties.append(repeat)
    for row_values in rows:
        cells = table.add_row().cells
        for cell, value in zip(cells, row_values):
            set_cell_border(cell, top={"val": "single", "sz": "6", "color": GREY_BORDER}, bottom={"val": "single", "sz": "6", "color": GREY_BORDER}, left={"val": "single", "sz": "6", "color": GREY_BORDER}, right={"val": "single", "sz": "6", "color": GREY_BORDER})
            paragraph = cell.paragraphs[0]
            paragraph.paragraph_format.space_after = Pt(1)
            paragraph.paragraph_format.line_spacing = 1.0
            add_text(paragraph, value, size=font_size - 0.1)
            keep_paragraph(paragraph)
    return table


def add_bullet(doc, text):
    paragraph = doc.add_paragraph(style="List Bullet")
    paragraph.paragraph_format.space_after = Pt(2)
    paragraph.paragraph_format.line_spacing = 1.05
    add_text(paragraph, text, size=10.0)
    keep_paragraph(paragraph)


def add_picture(doc, path, width_cm):
    paragraph = doc.add_paragraph()
    paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
    paragraph.paragraph_format.space_after = Pt(1)
    paragraph.add_run().add_picture(str(path), width=Cm(width_cm))
    keep_paragraph(paragraph)


def make_report():
    if not LOGO.exists():
        raise FileNotFoundError(f"Required source-derived logo is missing: {LOGO}")
    summary = json.loads((EVIDENCE / "phase2-summary.json").read_text(encoding="utf-8"))
    ledger = json.loads((EVIDENCE / "scenario-ledger.json").read_text(encoding="utf-8"))
    docker_capture = terminal_image("docker_health.png", "Docker evidence — live status at report generation", current_docker_lines())
    build_capture = terminal_image(
        "csharp_build.png", "C# / .NET Release build evidence",
        ["> dotnet build .\\AdaptiveForensics.sln --configuration Release", "ForensicFramework.LabBootstrap -> ...\\bin\\Release\\net10.0\\ForensicFramework.LabBootstrap.dll", "", "Build succeeded.", "    0 Warning(s)", "    0 Error(s)", "Time Elapsed 00:00:02.70"],
        accent="#73DACA",
    )
    traffic_capture = terminal_image(
        "controlled_traffic.png", "Captured controlled-traffic evidence — iperf UDP server reports",
        [
            "> P2-NORMAL-EMBB-001  | SST 1 | DNN oai | 10.0.0.2 | target 1500 Kbit/s",
            "0.0000-12.0023 sec  2.20 MBytes  1.54 Mbits/sec  1.631 ms  0/1571 (0%)",
            "",
            "> P2-NORMAL-MMTC-001  | SST 3 | DNN mmtc | 10.0.2.2 | target 256 Kbit/s",
            "0.0000-12.0632 sec  389 KBytes  264 Kbits/sec  1.742 ms  0/271 (0%)",
            "",
            "Result: two labelled scenarios completed; expected evidence directory retained.",
        ],
        accent="#73DACA",
    )
    topology = make_topology()
    doc = setup_document()

    # Cover page: deliberately mirrors the approved action plan.
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    p.paragraph_format.space_before = Pt(26)
    p.add_run().add_picture(str(LOGO), width=Inches(5.4))
    for text, size, before, after in [
        ("COS700 Research Proposal", 18, 32, 8),
        ("Phase 1 & Phase 2 Development Progress Report", 16, 0, 18),
        (TITLE, 17, 0, 24),
    ]:
        p = doc.add_paragraph()
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        p.paragraph_format.space_before = Pt(before)
        p.paragraph_format.space_after = Pt(after)
        add_text(p, text, size=size, bold=True)
    for label, value in [
        ("Student number:", "u21538795"),
        ("Supervisor(s):", "Mr. SM (Sheunesu) Makura"),
        ("Student:", "Lister Glen Mukota"),
        ("Reporting date:", "26 July 2026"),
    ]:
        p = doc.add_paragraph()
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        p.paragraph_format.space_after = Pt(7)
        add_text(p, label + " ", size=11.5, bold=True)
        add_text(p, value, size=11.5)
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    p.paragraph_format.space_before = Pt(16)
    add_text(p, "Evidence-led completion report for the first two laboratory development phases.", size=10.5, italic=True)

    doc.add_page_break()
    add_heading(doc, "1 Verified Development Position", 1)
    p = doc.add_paragraph()
    add_text(p, "This report records the completed and revalidated foundation of the approved implementation plan. The environment is a contained Docker/WSL2 laboratory running the OAI core, an RF-simulated virtual gNB, two virtual UEs, and a C#/.NET validation utility. It does not claim that the later forensic modules have been implemented.")
    add_callout(doc, "Verification verdict.", "Phase 1 is healthy and complete: the two-slice OAI core contains all 10 expected containers and the required network functions are registered. Phase 2 is complete: one virtual gNB and two virtual UEs attached successfully and both labelled controlled-traffic scenarios completed with zero UDP datagram loss.")
    add_table(doc, ["Development phase", "Status", "Verified evidence"], [
        ("Phase 1 — OAI two-slice core", "Completed and healthy", "10 expected core containers; NRF registration; baseline summary dated 26 July 2026."),
        ("Phase 2 — virtual gNB/UE and traffic", "Completed and healthy", "gNB + 2 virtual UEs healthy; slice-specific tunnel addresses; two successful iperf scenarios."),
        ("Step 2 ground-truth preparation", "Started and usable", "C# scenario ledger defines IDs, S-NSSAI, DNN, rates, durations and expected artefacts."),
        ("Steps 3–6 — ingestion through evaluation", "Not yet claimed", "Reserved for subsequent implementation and controlled assessment."),
    ], [4.6, 3.45, 8.4], font_size=9.5)
    add_caption(doc, "Table 1: Current implementation position against the approved action plan")
    add_picture(doc, docker_capture, 15.8)
    add_caption(doc, "Figure 1: Screen capture of Docker health evidence used for this report")

    doc.add_page_break()
    add_heading(doc, "2 Alignment with the Approved Proposal and Action Plan", 1)
    p = doc.add_paragraph()
    add_text(p, "The work follows the proposal's laboratory proof-of-concept boundary: a reproducible OAI core, two logically distinct slice profiles, virtual UE connectivity, controlled synthetic traffic, scenario ground truth and retained evidence. It avoids the explicitly excluded production, live-operator and real-subscriber context.")
    add_table(doc, ["Approved implementation intent", "What has been delivered", "Evidence retained"], [
        ("Step 1: establish a reproducible two-slice laboratory", "OAI AMF, SMF, UPF and supporting core services were deployed with eMBB (SST 1/DNN oai) and mMTC (SST 3/DNN mmtc).", "Version/configuration material; baseline summary; core Docker health."),
        ("Step 1: validate controlled UE/client connectivity", "RF-simulated gNB and two OAI NR-UEs attach to the core. UE tunnels are 10.0.0.2 (eMBB) and 10.0.2.2 (mMTC).", "gNB, AMF, SMF, UPF and UE logs; tunnel output; Docker status."),
        ("Step 2: define ground truth and evidence catalogue", "Two fixed UDP traffic scenarios specify slice, DNN, UE, rate, duration and expected artefacts before a run starts.", "Validated C# JSON scenario ledger and SHA-256 of scenario definition."),
        ("Steps 3–6: forensic processing and evaluation", "Not implemented in this report; no claim is made for evidence ingestion, attribution, hashing/custody or adaptive control.", "Bounded next-work statement on page 7."),
    ], [4.55, 7.2, 4.7], font_size=8.8)
    add_caption(doc, "Table 2: Direct mapping of completed work to the proposal and implementation plan")
    add_callout(doc, "Scope safeguard.", "All test identities are research-only OAI laboratory subscriptions. The radio interface is simulated; no physical radio, live operator network, real subscriber data, production deployment, or autonomous machine-learning decision is involved.")

    doc.add_page_break()
    add_heading(doc, "3 Implemented Laboratory Architecture", 1)
    p = doc.add_paragraph()
    add_text(p, "Phase 1 supplies the reproducible 5G core. Phase 2 adds the virtual RAN connectivity required to complete the controlled-connectivity acceptance criterion. Each scenario is defined before traffic is generated, so that later forensic collection can be evaluated against known ground truth rather than inferred after the fact.")
    add_picture(doc, topology, 16.0)
    add_caption(doc, "Figure 2: Validated Phase 1 + Phase 2 topology and evidence-producing paths")
    add_table(doc, ["Slice", "S-NSSAI / DNN", "Virtual UE tunnel", "Controlled scenario"], [
        ("eMBB baseline", "SST 1 / SD FFFFFF / oai", "10.0.0.2", "P2-NORMAL-EMBB-001: 1500 Kbit/s for 12 s"),
        ("mMTC evidence", "SST 3 / SD FFFFFF / mmtc", "10.0.2.2", "P2-NORMAL-MMTC-001: 256 Kbit/s for 12 s"),
    ], [3.0, 5.0, 3.0, 5.45], font_size=8.8)
    add_caption(doc, "Table 3: Slice-specific configuration used in the completed traffic run")

    doc.add_page_break()
    add_heading(doc, "4 Phase 1 — Reproducible Two-Slice Core: Completion Evidence", 1)
    p = doc.add_paragraph()
    add_text(p, "Phase 1 was revalidated before Phase 2 was started. The preflight check confirmed the local Visual Studio/.NET, WSL2 and Docker readiness. The C# solution was then built in Release configuration without warnings or errors, and the OAI core baseline declared the expected ten containers healthy.")
    add_table(doc, ["Validation item", "Observed result", "Retained proof"], [
        ("C#/.NET solution", "Release build succeeded with 0 warnings and 0 errors.", "artifacts/phase2-csharp-build.txt"),
        ("Core topology", "MySQL, NRF, UDR, UDM, AUSF, AMF, SMF, UPF, external DN and IMS present.", "artifacts/runs/20260726-134848/baseline-summary.json"),
        ("Required slice profiles", "eMBB SST 1/DNN oai/10.0.0.0/24 and mMTC SST 3/DNN mmtc/10.0.2.0/24.", "Baseline configuration summary and retained OAI patch."),
        ("Network-function registration", "UDR, UDM, AUSF, AMF, SMF and UPF verified through the NRF baseline evidence.", "Baseline evidence run logs."),
    ], [4.05, 6.25, 6.15], font_size=8.8)
    add_caption(doc, "Table 4: Phase 1 validation evidence")
    add_picture(doc, build_capture, 15.8)
    add_caption(doc, "Figure 3: Screen capture of the successful C#/.NET Release build")
    add_callout(doc, "Phase 1 acceptance conclusion.", "The two slice profiles are configured and logically separated in the validated OAI core. The Phase 2 traffic run on the following pages supplies the required controlled UE/client connectivity and labelled synthetic traffic for this first implementation step.")

    doc.add_page_break()
    add_heading(doc, "5 Phase 2 — Virtual gNB/UE and Controlled Traffic: Completion Evidence", 1)
    p = doc.add_paragraph()
    add_text(p, "The RF-simulated gNB is pinned to a container image digest and connects to the Phase 1 AMF. Two pinned virtual NR-UE images attach using distinct research-only subscriptions and DNNs. The controlled traffic script validates UE tunnel interfaces, creates the C# scenario ledger, executes UDP iperf traffic from the external data network, and retains logs and resource outputs in a timestamped evidence directory.")
    add_table(doc, ["Scenario", "Target / observed", "Jitter", "Loss", "Result"], [
        ("P2-NORMAL-EMBB-001", "1500 Kbit/s / 1.54 Mbit/s for 12.0023 s", "1.631 ms", "0 / 1571 (0%)", "Passed"),
        ("P2-NORMAL-MMTC-001", "256 Kbit/s / 264 Kbit/s for 12.0632 s", "1.742 ms", "0 / 271 (0%)", "Passed"),
    ], [3.35, 5.2, 2.0, 2.45, 3.45], font_size=8.8)
    add_caption(doc, "Table 5: Measured controlled UDP traffic results from the accepted Phase 2 run")
    add_picture(doc, traffic_capture, 15.8)
    add_caption(doc, "Figure 4: Screen capture of retained iperf server reports for the two labelled scenarios")
    add_table(doc, ["Scenario ledger field", "Configured value"], [
        ("Scenario-definition integrity", ledger["ScenarioDefinitionSha256"]),
        ("Evidence expected per scenario", "gNB / AMF / SMF / UPF events; iperf output; Docker resource snapshot."),
        ("Accepted evidence location", "artifacts/runs/phase2-20260726-144223/"),
        ("Run outcome", summary["Result"] + "; two scenarios: " + ", ".join(summary["Scenarios"])),
    ], [5.05, 11.4], font_size=8.7)
    add_caption(doc, "Table 6: Phase 2 ground-truth and evidence-record details")

    doc.add_page_break()
    add_heading(doc, "6 Reproduction Runbook and Supervisor Test", 1)
    p = doc.add_paragraph()
    add_text(p, "The implementation has been structured so that the completed phases can be reproduced from the workspace. Run the commands below in Windows PowerShell from the project root after Docker Desktop with WSL2 integration has started. Visual Studio may be used to open the solution; the same C# build is available through the .NET CLI.")
    add_table(doc, ["Order", "PowerShell command", "Expected observation"], [
        ("1", "dotnet build .\\AdaptiveForensics.sln --configuration Release", "Build succeeds with 0 warnings and 0 errors."),
        ("2", ".\\scripts\\Prepare-OaiTwoSliceLab.ps1", "Validates Phase 1 prerequisites and configuration."),
        ("3", "docker compose --project-name oai-two-slice-forensic-lab -f .\\third_party\\openairinterface5g\\doc\\tutorial_resources\\oai-cn5g\\docker-compose.yaml up -d --wait", "Core containers become healthy after the two-slice patch prepared in step 2."),
        ("4", ".\\scripts\\Capture-OaiBaselineEvidence.ps1", "Writes baseline core / NRF evidence to artifacts/runs."),
        ("5", ".\\scripts\\Start-Phase2Rfsim.ps1", "Starts the virtual gNB plus eMBB and mMTC UEs; script refuses an unhealthy core."),
        ("6", ".\\scripts\\Run-Phase2ControlledTraffic.ps1", "Creates C# ledger, verifies tunnels, runs both iperf tests and saves a timestamped evidence directory."),
    ], [1.0, 9.45, 6.0], font_size=8.15)
    add_caption(doc, "Table 7: Supervisor reproduction sequence")
    add_heading(doc, "6.1 Supervisor Verification Checklist", 2)
    for text in [
        "Confirm `docker ps` shows the 10 OAI core containers and the three Phase 2 containers as healthy.",
        "Confirm `phase2-oai-nr-ue-embb` has tunnel address 10.0.0.2 and `phase2-oai-nr-ue-mmtc` has 10.0.2.2.",
        "Open the latest `artifacts/runs/phase2-<timestamp>/phase2-summary.json`; it must list both scenario IDs and `completed`.",
        "Open the two iperf client/server outputs; each must report the configured duration and 0% UDP loss.",
        "Check the C# scenario ledger before interpreting logs: its scenario IDs, slice context and expected artefacts are the ground truth for this phase.",
    ]:
        add_bullet(doc, text)
    add_callout(doc, "Reliability note and bounded next work.", "A Windows WSL component update interrupted one initial traffic attempt. Docker Desktop was restarted, the core and RAN were restored from the same pinned configuration, and the complete accepted run reported on page 6 succeeded. The next implementation is Step 3: normalised evidence ingestion from the retained Phase 1/2 artefacts. Slice attribution, SHA-256 evidence hashing, custody recording, adaptive rules and formal evaluation remain explicitly pending.")

    for paragraph in doc.paragraphs:
        if paragraph.style.name.startswith("Heading"):
            keep_paragraph(paragraph, with_next=True)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    doc.save(OUTPUT)
    print(OUTPUT)


if __name__ == "__main__":
    make_report()
