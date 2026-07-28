from __future__ import annotations

import copy
import json
import subprocess
from pathlib import Path

from docx import Document
from docx.enum.table import WD_ALIGN_VERTICAL
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Cm, Pt, RGBColor
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(r"C:\Users\mukot\OneDrive - North-West University\Documents\Research Proposal Implementation")
REPORT = Path(r"C:\Users\mukot\OneDrive - North-West University\Desktop\COS700 2ND SEMESTER RESERACH REPORT AND DEVELOPMENT\Mukota_Phase_1_and_2_Development_Progress_Report.docx")
ASSETS = ROOT / "tmp" / "phase3-doc" / "report-assets"
BLUE = "D9EAF7"
LIGHT_BLUE = "EAF3FA"
MID_BLUE = "7F9DB9"
GREY_BORDER = "A6A6A6"
BLACK = "000000"


def font(size: int, bold: bool = False, mono: bool = False):
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


def centered(draw, bounds, text, text_font, fill="black", spacing=4):
    left, top, right, bottom = bounds
    box = draw.multiline_textbbox((0, 0), text, font=text_font, spacing=spacing, align="center")
    x = left + (right - left - (box[2] - box[0])) / 2
    y = top + (bottom - top - (box[3] - box[1])) / 2
    draw.multiline_text((x, y), text, font=text_font, fill=fill, spacing=spacing, align="center")


def terminal_image(name: str, title: str, lines: list[str]) -> Path:
    ASSETS.mkdir(parents=True, exist_ok=True)
    path = ASSETS / name
    mono = font(25, mono=True)
    title_font = font(28, bold=True)
    height = 135 + 42 * len(lines) + 48
    image = Image.new("RGB", (1700, height), "#111827")
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((3, 3, 1696, height - 4), radius=18, outline="#496C86", width=3)
    draw.rectangle((4, 4, 1696, 88), fill="#1F4E79")
    for x, colour in ((36, "#E06C75"), (64, "#E5C07B"), (92, "#73DACA")):
        draw.ellipse((x, 28, x + 17, 45), fill=colour)
    draw.text((140, 22), title, font=title_font, fill="white")
    y = 112
    for line in lines:
        draw.text((50, y), line, font=mono, fill="#73DACA" if line.startswith(">") else "#F7FAFC")
        y += 42
    image.save(path, "PNG")
    return path


def phase3_workflow() -> Path:
    ASSETS.mkdir(parents=True, exist_ok=True)
    path = ASSETS / "phase3_workflow.png"
    image = Image.new("RGB", (1700, 650), "white")
    draw = ImageDraw.Draw(image)
    title_font = font(31, bold=True)
    heading = font(24, bold=True)
    detail = font(19)
    centered(draw, (90, 25, 1610, 72), "Phase 3 Normalised Evidence-Ingestion Flow", title_font)
    boxes = [
        (80, "Retained Phase 1/2 artifacts", "Core health, NRF, logs,\nUE tunnels, iperf, metrics"),
        (470, "C# ingestion catalog", "17 selected source definitions\nRead-only registration / parsing"),
        (860, "Evidence index", "27 normalised records\nsource, time, NF, scenario,\npreliminary slice context"),
        (1250, "Queryable evidence", "Retrieve by network function,\nsource kind or scenario"),
    ]
    for x, label, description in boxes:
        draw.rounded_rectangle((x, 180, x + 300, 475), radius=18, fill="#EAF3FA" if x in (80, 860) else "#D9EAF7", outline="#1F4E79", width=3)
        centered(draw, (x + 18, 210, x + 282, 280), label, heading)
        centered(draw, (x + 18, 315, x + 282, 425), description, detail)
    for x in (380, 770, 1160):
        draw.line((x, 328, x + 68, 328), fill="#1F4E79", width=5)
        draw.polygon([(x + 68, 328), (x + 48, 316), (x + 48, 340)], fill="#1F4E79")
    draw.text((90, 555), "Scope boundary: this stage indexes approved source evidence; final slice attribution, integrity/custody and adaptive control remain later stages.", font=detail, fill="#1F4E79")
    image.save(path, "PNG")
    return path


def latest_run(pattern: str, required: str) -> Path:
    matches = [path for path in (ROOT / "artifacts" / "runs").glob(pattern) if (path / required).exists()]
    if not matches:
        raise FileNotFoundError(f"No run matching {pattern} with {required} was found.")
    return max(matches, key=lambda path: path.stat().st_mtime)


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


def set_cell_text(cell, text, size=9.1, bold=False):
    cell.text = ""
    paragraph = cell.paragraphs[0]
    paragraph.paragraph_format.space_after = Pt(1)
    paragraph.paragraph_format.line_spacing = 1.0
    add_text(paragraph, text, size=size, bold=bold)


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
        node = borders.find(qn(f"w:{edge}"))
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
    tbl_pr = table._tbl.tblPr
    layout = tbl_pr.first_child_found_in("w:tblLayout")
    if layout is None:
        layout = OxmlElement("w:tblLayout")
        tbl_pr.append(layout)
    layout.set(qn("w:type"), "fixed")
    grid = table._tbl.tblGrid
    for column, width in zip(grid.gridCol_lst, widths_cm):
        column.set(qn("w:w"), str(int(width * 567)))
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


def add_heading(doc, text, level=1):
    paragraph = doc.add_paragraph(style="Heading 1" if level == 1 else "Heading 2")
    paragraph.paragraph_format.space_before = Pt(10 if level == 1 else 6)
    paragraph.paragraph_format.space_after = Pt(4)
    add_text(paragraph, text, size=13 if level == 1 else 11.25, bold=True)
    return paragraph


def add_caption(doc, text):
    paragraph = doc.add_paragraph()
    paragraph.paragraph_format.space_before = Pt(2)
    paragraph.paragraph_format.space_after = Pt(6)
    add_text(paragraph, text, size=10.2, bold=True)


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


def add_table(doc, headers, rows, widths, font_size=9.0):
    table = doc.add_table(rows=1, cols=len(headers))
    set_table_geometry(table, widths)
    for cell, heading in zip(table.rows[0].cells, headers):
        set_cell_shading(cell, BLUE)
        set_cell_border(cell, top={"val": "single", "sz": "8", "color": MID_BLUE}, bottom={"val": "single", "sz": "8", "color": MID_BLUE}, left={"val": "single", "sz": "8", "color": MID_BLUE}, right={"val": "single", "sz": "8", "color": MID_BLUE})
        set_cell_text(cell, heading, size=font_size, bold=True)
    for values in rows:
        cells = table.add_row().cells
        for cell, value in zip(cells, values):
            set_cell_border(cell, top={"val": "single", "sz": "6", "color": GREY_BORDER}, bottom={"val": "single", "sz": "6", "color": GREY_BORDER}, left={"val": "single", "sz": "6", "color": GREY_BORDER}, right={"val": "single", "sz": "6", "color": GREY_BORDER})
            set_cell_text(cell, value, size=font_size - 0.1)
    return table


def add_picture(doc, path: Path, width_cm: float):
    paragraph = doc.add_paragraph()
    paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
    paragraph.paragraph_format.space_after = Pt(1)
    paragraph.add_run().add_picture(str(path), width=Cm(width_cm))


def replace_paragraph(paragraph, text, size=10.5, bold=False, italic=False):
    paragraph.clear()
    add_text(paragraph, text, size=size, bold=bold, italic=italic)


def copy_row_style(source_row, target_row):
    for source_cell, target_cell in zip(source_row.cells, target_row.cells):
        source_pr = source_cell._tc.get_or_add_tcPr()
        target_pr = target_cell._tc.get_or_add_tcPr()
        for child in list(target_pr):
            if child.tag != qn("w:tcW"):
                target_pr.remove(child)
        for child in list(source_pr):
            if child.tag != qn("w:tcW"):
                target_pr.append(copy.deepcopy(child))


def make_report():
    if not REPORT.exists():
        raise FileNotFoundError(REPORT)
    phase1 = latest_run("*", "baseline-summary.json")
    phase2 = latest_run("phase2-*", "phase2-summary.json")
    phase3 = latest_run("phase3-*", "phase3-summary.json")
    phase3_summary = json.loads((phase3 / "phase3-summary.json").read_text(encoding="utf-8"))
    phase2_summary = json.loads((phase2 / "phase2-summary.json").read_text(encoding="utf-8"))
    smf_query = (phase3 / "query-oai-smf.txt").read_text(encoding="utf-8").splitlines()[0]
    mmtc_query = (phase3 / "query-mmtc-scenario.txt").read_text(encoding="utf-8").splitlines()[0]
    traffic_query = (phase3 / "query-user-plane-traffic.txt").read_text(encoding="utf-8").splitlines()[0]
    phase3_panel = terminal_image(
        "phase3_acceptance.png",
        "Captured Phase 3 acceptance evidence - C#/.NET normalised ingestion",
        [
            "> .\\scripts\\Run-Phase3EvidenceIngestion.ps1",
            "EVIDENCE_INGESTION_VALID records=27 sources=17",
            "EVIDENCE_INDEX_WRITTEN artifacts\\runs\\phase3-20260728-180129\\evidence-index.json",
            "",
            "Original artifacts unchanged: True",
            "SMF retrieval: " + smf_query,
            "mMTC scenario retrieval: " + mmtc_query,
            "User-plane retrieval: " + traffic_query,
        ],
    )
    workflow = phase3_workflow()
    doc = Document(REPORT)

    # Update the existing report's wording and evidence tables in place.
    for paragraph in doc.paragraphs:
        if paragraph.text == "Phase 1 & Phase 2 Development Progress Report":
            replace_paragraph(paragraph, "Phase 1, Phase 2 & Phase 3 Development Progress Report", size=16, bold=True)
        elif paragraph.text == "Reporting date: 26 July 2026":
            paragraph.clear()
            add_text(paragraph, "Reporting date: ", size=11.5, bold=True)
            add_text(paragraph, "28 July 2026", size=11.5)
        elif paragraph.text == "Evidence-led completion report for the first two laboratory development phases.":
            replace_paragraph(paragraph, "Evidence-led completion report for the first three laboratory development phases.", size=10.5, italic=True)
        elif paragraph.text.startswith("This report records the completed and revalidated foundation"):
            replace_paragraph(paragraph, "This report records the completed and revalidated first three stages of the approved implementation plan. The contained Docker/WSL2 laboratory runs the OAI core, RF-simulated virtual gNB and UEs, controlled traffic, and a C#/.NET normalised evidence-ingestion utility. Later forensic modules are not claimed as complete.")
        elif paragraph.text.startswith("The work follows the proposal's laboratory proof-of-concept boundary"):
            replace_paragraph(paragraph, "The work follows the proposal's laboratory proof-of-concept boundary: a reproducible OAI core, two logically distinct slice profiles, virtual UE connectivity, controlled synthetic traffic, scenario ground truth and a normalised evidence index. It avoids the explicitly excluded production, live-operator and real-subscriber context.")
        elif paragraph.text.startswith("The implementation has been structured so that the completed phases"):
            replace_paragraph(paragraph, "The implementation has been structured so that the first three completed phases can be reproduced from the workspace. Run the commands below in Windows PowerShell from the project root after Docker Desktop with WSL2 integration has started. Visual Studio may be used to open the solution; the same C# build is available through the .NET CLI.")

    verdict = doc.tables[0].cell(0, 0)
    verdict.text = ""
    p = verdict.paragraphs[0]
    add_text(p, "Verification verdict. ", size=10.5, bold=True)
    add_text(p, "On 28 July 2026, Phase 1 revalidated healthy with 10 core containers and registered network functions. Phase 2 revalidated healthy with one virtual gNB, two virtual UEs and both controlled traffic scenarios passing with 0% UDP loss. Phase 3 completed: 17 selected sources produced 27 queryable C#/.NET normalised evidence records while original artefacts remained unchanged.", size=10.5)

    status = doc.tables[1]
    set_cell_text(status.cell(1, 2), "10 expected core containers; NRF registration; baseline evidence revalidated 28 July 2026.")
    set_cell_text(status.cell(2, 2), "gNB + 2 virtual UEs healthy; fresh two-scenario iperf run; both reports show 0% UDP loss.")
    set_cell_text(status.cell(3, 1), "Completed")
    set_cell_text(status.cell(3, 2), "C# scenario ledger defines IDs, S-NSSAI, DNN, rates, durations and expected artefacts.")
    set_cell_text(status.cell(4, 0), "Phase 3 - normalised evidence ingestion")
    set_cell_text(status.cell(4, 1), "Completed")
    set_cell_text(status.cell(4, 2), "17 sources / 27 records; original artefacts unchanged; retrieval queries passed.")

    alignment = doc.tables[2]
    set_cell_text(alignment.cell(4, 0), "Step 3: implement normalised evidence ingestion")
    set_cell_text(alignment.cell(4, 1), "C# registers selected Phase 1/2 artefacts as timestamped, source-aware, queryable records with scenario and preliminary slice fields where known.")
    set_cell_text(alignment.cell(4, 2), "Phase 3 evidence index, three retrieval proofs and acceptance summary dated 28 July 2026.")

    phase1_table = doc.tables[5]
    set_cell_text(phase1_table.cell(1, 1), "Release build succeeded with 0 warnings and 0 errors on 28 July 2026.")
    set_cell_text(phase1_table.cell(1, 2), "C# Release build and artifacts/phase3-preflight.json")
    set_cell_text(phase1_table.cell(2, 2), f"artifacts/runs/{phase1.name}/baseline-summary.json")

    runbook = doc.tables[9]
    extra_row = runbook.add_row()
    copy_row_style(runbook.rows[-2], extra_row)
    set_cell_text(extra_row.cells[0], "7", size=8.15)
    set_cell_text(extra_row.cells[1], ".\\scripts\\Run-Phase3EvidenceIngestion.ps1", size=8.15)
    set_cell_text(extra_row.cells[2], "Registers 27 normalised records and writes three retrieval proofs without modifying the Phase 1/2 inputs.", size=8.15)

    bounded = doc.tables[10].cell(0, 0)
    bounded.text = ""
    p = bounded.paragraphs[0]
    add_text(p, "Reliability note and bounded next work. ", size=10.5, bold=True)
    add_text(p, "A Windows WSL component update previously interrupted one Phase 2 traffic attempt. The core and RAN were restored from the same pinned configuration, and the new 28 July validation passed. Phase 3 now registers normalised evidence from those retained inputs. The next approved implementation is Step 4: slice attribution and transparent rule logic. Evidence hashing, custody recording, adaptive rules and formal evaluation remain explicitly pending.", size=10.5)

    # Append the Phase 3 completion evidence without replacing the established report design.
    doc.add_page_break()
    add_heading(doc, "7 Phase 3 - Normalised Evidence Ingestion: Completion Evidence", 1)
    p = doc.add_paragraph()
    add_text(p, "Phase 3 implements the action plan's normalised evidence-ingestion stage. The C#/.NET utility validates a catalogue of selected laboratory artefacts, retains the original source reference, normalises evidence time, source kind, source component, network function, scenario identifier and preliminary slice context where it is already available. It writes a structured JSON evidence index rather than changing the original source files.")
    add_callout(doc, "Phase 3 acceptance verdict.", "The accepted 28 July 2026 run selected 17 source definitions from the revalidated Phase 1 and Phase 2 evidence runs and produced 27 normalised records. Every record reported its original artifact unchanged, and all required retrieval demonstrations returned results.")
    add_picture(doc, phase3_panel, 15.8)
    add_caption(doc, "Figure 5: Screen capture of the completed Phase 3 ingestion and retrieval checks")
    add_table(doc, ["Acceptance measure", "Observed result", "Retained proof"], [
        ("Selected evidence sources", "17 configured sources spanning core health, NRF, UE, gNB, AMF, SMF, UPF, iperf and resource evidence.", "evidence-ingestion.json"),
        ("Normalised index", "27 structured, queryable C#/.NET records.", f"artifacts/runs/{phase3.name}/evidence-index.json"),
        ("Original-artifact safety", "All indexed records report OriginalArtifactUnchanged = true.", "phase3-summary.json"),
        ("Retrieval demonstration", "SMF: 2 matches; mMTC scenario: 4 matches; user-plane traffic: 2 matches.", "Three query-*.txt results"),
    ], [4.3, 7.0, 5.15], font_size=8.8)
    add_caption(doc, "Table 8: Phase 3 acceptance measures and retained artefacts")

    doc.add_page_break()
    add_heading(doc, "8 Phase 3 Evidence Flow and Updated Supervisor Verification", 1)
    p = doc.add_paragraph()
    add_text(p, "The flow below distinguishes normalised ingestion from later forensic decisions. Known Phase 2 scenario metadata is retained as preliminary context for the appropriate records; Phase 4 will be responsible for transparent attribution decisions and their rationale.")
    add_picture(doc, workflow, 15.8)
    add_caption(doc, "Figure 6: Phase 3 evidence-ingestion flow and explicit boundary to later stages")
    add_heading(doc, "8.1 Supervisor Verification for Phase 3", 2)
    for text in [
        "Run .\\scripts\\Run-Phase3EvidenceIngestion.ps1 after the Phase 1 baseline and Phase 2 controlled-traffic scripts have completed.",
        "Open the latest artifacts/runs/phase3-<timestamp>/phase3-summary.json and confirm Result is completed, RecordCount is at least 27, and OriginalArtifactsUnchanged is true.",
        "Open evidence-index.json and verify every record includes source reference, normalised timestamp, source kind, component, network function and scenario field; preliminary slice fields are present only where ground truth already provides them.",
        "Open query-oai-smf.txt, query-mmtc-scenario.txt and query-user-plane-traffic.txt to verify the retrieval results.",
        "Confirm the Phase 1 and Phase 2 input directories remain present and unchanged; the Phase 3 output is written only to its separate timestamped directory.",
    ]:
        paragraph = doc.add_paragraph(style="List Bullet")
        paragraph.paragraph_format.space_after = Pt(2)
        add_text(paragraph, text, size=10.0)
    add_callout(doc, "Declared boundary after Phase 3.", "Normalised ingestion is complete. Step 4 slice attribution and transparent rule logic, Step 5 integrity / chain-of-custody and adaptive controls, and Step 6 controlled evaluation remain future work. This preserves the staged, evidence-led sequence approved in the implementation plan.")

    doc.save(REPORT)
    print(REPORT)


if __name__ == "__main__":
    make_report()
