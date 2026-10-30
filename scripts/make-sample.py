"""Generates samples/sample-product-sheet.pdf, a fictitious two-page product sheet.

The fund, the publisher and every figure are invented. Page 2 ends with a
block "Additional information from your bank" whose text sits exactly where the
backend places the editable field by default (FIELD_PAGE 2, FIELD_X 44,
FIELD_Y 792, FIELD_WIDTH 519, FIELD_HEIGHT 34, in points from the top-left
corner of the page), so the field covers it.

Usage (from the repository root, in a virtual environment outside the repo):
    python3 -m venv /tmp/hsm-sample-venv
    /tmp/hsm-sample-venv/bin/pip install reportlab
    /tmp/hsm-sample-venv/bin/python scripts/make-sample.py
"""

from pathlib import Path

from reportlab.lib.pagesizes import A4
from reportlab.pdfbase.pdfmetrics import stringWidth
from reportlab.pdfgen import canvas

OUT = Path(__file__).resolve().parent.parent / "samples" / "sample-product-sheet.pdf"
W, H = A4
LEFT, RIGHT = 44.65, 562.44
NAVY = (0.11, 0.23, 0.40)
GREY = (0.40, 0.42, 0.46)

FUND = "Example Global Equity Fund"
PUBLISHER = "Example Research Ltd"


def top(y):
    """Converts a distance from the top of the page to reportlab's bottom-left origin."""
    return H - y


def wrap(text, font, size, width):
    lines, line = [], ""
    for word in text.split():
        candidate = f"{line} {word}".strip()
        if stringWidth(candidate, font, size) > width:
            lines.append(line)
            line = word
        else:
            line = candidate
    if line:
        lines.append(line)
    return lines


def paragraph(c, text, y, size=9, font="Helvetica", lead=None, width=RIGHT - LEFT):
    lead = lead or size * 1.35
    c.setFont(font, size)
    for line in wrap(text, font, size, width):
        c.drawString(LEFT, top(y), line)
        y += lead
    return y + size * 0.6


def heading(c, text, y):
    c.setFillColorRGB(*NAVY)
    c.setFont("Helvetica-Bold", 11)
    c.drawString(LEFT, top(y), text)
    c.setFillColorRGB(0, 0, 0)
    return y + 16


def header(c, page):
    c.setFillColorRGB(*NAVY)
    c.rect(0, top(62), W, 62, stroke=0, fill=1)
    c.setFillColorRGB(1, 1, 1)
    c.setFont("Helvetica-Bold", 15)
    c.drawString(LEFT, top(30), "Product information sheet")
    c.setFont("Helvetica", 9)
    c.drawString(LEFT, top(47), f"{FUND}  |  Share class A (accumulating)  |  As of 30 September 2026")
    c.drawRightString(RIGHT, top(30), PUBLISHER)
    c.setFillColorRGB(*GREY)
    c.setFont("Helvetica", 7.5)
    c.drawRightString(RIGHT, 7, f"Page {page} of 2")
    c.drawString(LEFT, 7, "Sample document generated for a demo. All names and figures are fictitious.")
    c.setFillColorRGB(0, 0, 0)


def table(c, rows, y, col=200):
    c.setStrokeColorRGB(0.85, 0.86, 0.88)
    for label, value in rows:
        c.setFont("Helvetica-Bold", 8.5)
        c.drawString(LEFT, top(y), label)
        c.setFont("Helvetica", 8.5)
        c.drawString(LEFT + col, top(y), value)
        c.line(LEFT, top(y + 4), RIGHT, top(y + 4))
        y += 15
    return y + 8


def page_one(c):
    header(c, 1)
    y = 92
    y = heading(c, "1. Product overview", y)
    y = paragraph(c, f"The {FUND} invests in shares of large and medium-sized companies worldwide. The fund aims "
                     "to grow capital over at least five years. It is actively managed and does not track an index. "
                     "Income is reinvested.", y)
    y = table(c, [
        ("Product type", "Investment fund (UCITS), equity"),
        ("Identifier", "XS0000000000 (example)"),
        ("Currency", "EUR"),
        ("Launch date", "15 March 2019"),
        ("Fund size", "EUR 412 million"),
        ("Management company", "Example Asset Management S.A."),
        ("Recommended holding period", "5 years"),
    ], y)
    y = heading(c, "2. Risk indicator", y)
    y = paragraph(c, "The summary risk indicator ranges from 1 (lower risk) to 7 (higher risk). This fund is "
                     "classified in category 4: medium risk. The value of the investment can go down as well as up.", y)
    x = LEFT
    for i in range(1, 8):
        c.setFillColorRGB(*(NAVY if i == 4 else (0.90, 0.91, 0.93)))
        c.rect(x, top(y + 18), 40, 18, stroke=0, fill=1)
        c.setFillColorRGB(*((1, 1, 1) if i == 4 else (0.2, 0.2, 0.2)))
        c.setFont("Helvetica-Bold", 9)
        c.drawCentredString(x + 20, top(y + 12), str(i))
        x += 44
    c.setFillColorRGB(0, 0, 0)
    y += 34
    y = heading(c, "3. Past performance", y)
    y = table(c, [("2021", "+18.4 %"), ("2022", "-12.1 %"), ("2023", "+15.7 %"), ("2024", "+9.8 %"), ("2025", "+6.2 %")],
              y, col=120)
    y = paragraph(c, "Past performance is not a reliable indicator of future results. Figures are shown after "
                     "ongoing costs and in euro.", y)
    y = heading(c, "4. Investment strategy", y)
    paragraph(c, "The portfolio holds 60 to 90 companies across sectors and regions. The fund manager selects "
                 "companies with stable cash flows, sound balance sheets and reasonable valuations. Currency risk "
                 "is not hedged. Derivatives are used only to reduce risk.", y)


def page_two(c):
    header(c, 2)
    y = 92
    y = heading(c, "5. Costs", y)
    y = table(c, [
        ("Entry charge", "up to 5.00 % of the amount invested"),
        ("Exit charge", "none"),
        ("Ongoing costs", "1.45 % a year"),
        ("Transaction costs", "0.12 % a year"),
        ("Performance fee", "none"),
    ], y)
    y = paragraph(c, "The costs reduce the return of the investment. Your bank may charge additional fees for buying, "
                     "selling and holding the fund; they are shown at the end of this sheet.", y)
    y = heading(c, "6. Other information", y)
    y = paragraph(c, "Legal notice: this document is for information only. It is neither an offer nor a "
                     "recommendation to buy or sell a financial instrument. An investment decision should be based "
                     "on the prospectus, the key information document and the latest annual report, which are "
                     "available free of charge from the management company. If you have questions about how this "
                     "product works or about its risks, please contact your bank.", y, size=8)
    y = paragraph(c, f"{PUBLISHER} is not responsible for the additional information at the end of this "
                     "document, which is provided by your bank.", y, size=8)
    y = heading(c, "Publisher", y)
    paragraph(c, f"{PUBLISHER}, 1 Example Square, Example City. Registered in the example register under "
                 "number 000000. Contact: research@example.com.", y, size=8)

    # Block replaced by the editable field: heading above, three lines inside the field area.
    c.setStrokeColorRGB(0.80, 0.81, 0.84)
    c.line(LEFT, top(770), RIGHT, top(770))
    c.setFont("Helvetica-Bold", 9)
    c.drawCentredString((LEFT + RIGHT) / 2, top(783), "Additional information from your bank")
    text = ("For each buy or sell order we charge a fee of 1% of the order value, with a minimum of EUR 20 per "
            "order. Third-party costs that depend on the trading venue are added. While you hold the fund, we "
            "charge a custody fee as listed in our price list, available in every branch and on our website. "
            "Your adviser can tell you the total costs of your order before you place it.")
    c.setFont("Helvetica", 8)
    baseline = 799.0
    for line in wrap(text, "Helvetica", 8, RIGHT - LEFT):
        c.drawString(LEFT, top(baseline), line)
        baseline += 10.1


def main():
    OUT.parent.mkdir(parents=True, exist_ok=True)
    c = canvas.Canvas(str(OUT), pagesize=A4, invariant=1)
    c.setTitle(f"{FUND} - Product information sheet")
    c.setAuthor(PUBLISHER)
    c.setCreator("make-sample.py")
    page_one(c)
    c.showPage()
    page_two(c)
    c.showPage()
    c.save()
    print(f"Sample written: {OUT.relative_to(OUT.parent.parent)} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
