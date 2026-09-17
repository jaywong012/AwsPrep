#!/usr/bin/env python3
"""
Extract a reference question bank from an exam-dump PDF into the JSON shape the API
seeds from (server/AwsCertPrep.Api/Data/ReferenceBank/<CODE>.json).

    pdftotext -layout -enc UTF-8 CLF-C02.pdf clf.txt
    python tools/extract-reference-bank.py clf.txt server/AwsCertPrep.Api/Data/ReferenceBank/CLF-C02.json

The output is a faithful extraction: stem, options, answer key, explanation and any
reference URLs. Domain, difficulty and service tags are derived at load time by
ReferenceBank.cs, so re-running this script never discards hand classification.
"""
import json
import re
import sys

QSTART = re.compile(r"^\s*Question:\s*(\d+)\b")
OPT = re.compile(r"^\s*([A-E])[.)]\s*(.+)$")
ANS = re.compile(r"^\s*Answer:\s*([A-E, ]+?)\s*$")
EXPL = re.compile(r"^\s*Explanation:\s*(.*)$")
URL = re.compile(r"https?://\S+")

# Vendor watermark, banner and footer text that must never leak into an item.
NOISE = re.compile(
    r"CertyIQ|Premium exam material|Get certification quickly|Everything you need to prepare"
    r"|First attempt guaranteed|Download Full Version|Total:\s*\d+\s*Questions"
    r"|Link:\s*https?://\S*|But Wait|premium exam material|Lifetime free updates",
    re.I,
)

# Everything from the closing marketing page onwards belongs to no question.
FOOTER = re.compile(r"\n\s*Thank you\s*\n")

# Boilerplate the dump prepends to explanations ("Answer B:", "BAWS Transit Gateway...").
EXPL_PREFIXES = [
    re.compile(r"^(?:the\s+)?correct\s+answer\s+is\s*[:\-]?\s*[A-E]\s*[:.\-)]?\s*", re.I),
    re.compile(r"^answer\s*(?:is)?\s*[:\-]?\s*[A-E]\s*[:.\-)]?\s*", re.I),
    re.compile(r"^[A-E]\s+is\s+correct\s*[:.,\-]?\s*", re.I),
    re.compile(r"^[A-E]\s*[.:)]\s+"),
    # "BAWS Transit Gateway ..." - the answer letter glued onto the first word. The
    # lookahead lists real following words so "AWS Secrets Manager" keeps its A.
    re.compile(r"^[A-E](?=(?:AWS|Amazon|The|This|Correct|Answer|Use)\b)"),
]

ALLOWED_REF_HOSTS = ("aws.amazon.com", "docs.aws.amazon.com", "repost.aws")


def strip_noise(text):
    footer = FOOTER.search(text)
    if footer:
        text = text[: footer.start()]
    # Substituted inline rather than dropping the whole line: the page watermark shares a
    # line with the "Question: N" marker that delimits every item.
    return NOISE.sub(" ", text)


def collapse(text):
    return rejoin_wrapped(re.sub(r"\s+", " ", text).strip())


def rejoin_wrapped(text):
    """Repairs a hyphenated word the PDF broke across a column wrap ("on- premises").

    Only a lowercase letter on each side of "- " is joined, so a dash used as punctuation
    ("Trusted Advisor - the plan decides") and a capitalised name are both left alone.
    """
    return re.sub(r"(?<=[a-z])- (?=[a-z])", "-", text)


def clean_explanation(text):
    text = URL.sub(" ", text)
    text = re.sub(r"\bReferences?\s*:?\s*", " ", text, flags=re.I)
    text = collapse(text)
    for _ in range(3):
        for pattern in EXPL_PREFIXES:
            text = pattern.sub("", text, count=1).lstrip()
    return collapse(text)


def clean_references(urls):
    out = []
    for url in urls:
        url = url.rstrip(".,;)#")
        if not any(h in url for h in ALLOWED_REF_HOSTS):
            continue          # third-party blogs are not authoritative for an answer key
        if url.endswith("-"):
            continue          # truncated by the PDF line wrap
        # The layout engine glues the next word onto the URL ("/inspector/Software").
        # Only a trailing capitalised segment with no file extension is a glued word;
        # "/UserGuide/EBSEncryption.html" is a genuine docs path.
        url = re.sub(r"/[A-Z][A-Za-z]*$", "/", url)
        if url not in out:
            out.append(url)
    return out


def normalise_stem(stem):
    stem = collapse(stem)
    # The dump writes "(Choose two.)"; the official AWS wording is "(Select TWO.)".
    stem = re.sub(r"\(\s*choose\s+two\.?\s*\)", "(Select TWO.)", stem, flags=re.I)
    stem = re.sub(r"\(\s*choose\s+three\.?\s*\)", "(Select THREE.)", stem, flags=re.I)
    return stem


def split_blocks(lines):
    blocks, current = [], None
    for line in lines:
        match = QSTART.match(line)
        if match:
            if current:
                blocks.append(current)
            current = {"number": int(match.group(1)), "lines": []}
            continue
        if current is not None:
            current["lines"].append(line)
    if current:
        blocks.append(current)
    return blocks


def parse_block(block):
    stem, options, correct, explanation = [], [], [], []
    phase = "stem"

    for line in block["lines"]:
        if ANS.match(line):
            # "Answer: C", "Answer: AE" and "Answer: A, E" all occur.
            correct = sorted(set(re.sub(r"[,\s]+", "", ANS.match(line).group(1))))
            phase = "answer"
            continue
        if EXPL.match(line):
            phase = "explanation"
            tail = EXPL.match(line).group(1)
            if tail.strip():
                explanation.append(tail)
            continue
        if phase == "explanation":
            explanation.append(line)
            continue
        if phase == "answer":
            continue

        match = OPT.match(line)
        if match and stem:
            options.append([match.group(1), match.group(2)])
            phase = "options"
            continue
        if phase == "options" and line.strip():
            options[-1][1] += " " + line.strip()     # option wrapped across lines
            continue
        if phase == "stem":
            stem.append(line)

    return {
        "number": block["number"],
        "stem": normalise_stem(" ".join(stem)),
        "options": [{"label": label, "text": collapse(text)} for label, text in options],
        "correct": correct,
        "explanation": clean_explanation(" ".join(explanation)),
        "references": clean_references(URL.findall(" ".join(explanation))),
    }


def validate(item):
    labels = [o["label"] for o in item["options"]]
    problems = []
    if len(item["options"]) < 4:
        problems.append(f"{len(item['options'])} options")
    if labels != sorted(labels) or len(set(labels)) != len(labels):
        problems.append("duplicate or unordered labels")
    if not item["correct"]:
        problems.append("no answer key")
    if any(c not in labels for c in item["correct"]):
        problems.append("answer key not among the options")
    if len(item["correct"]) == len(item["options"]):
        problems.append("every option keyed correct")
    if len(item["stem"].split()) < 5:
        problems.append("stem too short")
    if not item["explanation"]:
        problems.append("no explanation")
    return problems


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)

    source, destination = sys.argv[1], sys.argv[2]
    text = strip_noise(open(source, encoding="utf-8", errors="replace").read())
    items = [parse_block(b) for b in split_blocks(text.split("\n"))]

    kept, dropped = [], []
    for item in items:
        problems = validate(item)
        (dropped if problems else kept).append((item, problems))

    json.dump([i for i, _ in kept], open(destination, "w", encoding="utf-8"),
              indent=1, ensure_ascii=False)

    print(f"{len(kept)} items written to {destination}")
    for item, problems in dropped:
        print(f"  dropped #{item['number']}: {', '.join(problems)}")


if __name__ == "__main__":
    main()
