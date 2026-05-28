#!/usr/bin/env python3
"""Validate CvWpfclient qfm print forms for master-mente print additions."""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def validate_qfm(path: Path) -> list[str]:
    errors: list[str] = []
    try:
        raw = path.read_bytes()
    except OSError as ex:
        return [f"{path}: 読み込みに失敗しました: {ex}"]

    try:
        text = raw.decode("cp932")
    except UnicodeDecodeError as ex:
        return [f"{path}: Shift_JIS(cp932) として読み込めません: {ex}"]

    if text.encode("cp932") != raw:
        errors.append(f"{path}: Shift_JIS(cp932) のラウンドトリップが一致しません")

    head = text[:300]
    if not re.search(r'encoding\s*=\s*["\']shift[_-]jis["\']', head, re.IGNORECASE):
        errors.append(f"{path}: XML宣言の encoding が SHIFT_JIS/shift_jis ではありません")

    xml_text = re.sub(r"^\s*<\?xml[^>]*\?>", "", text, count=1)
    try:
        root = ET.fromstring(xml_text)
    except ET.ParseError as ex:
        errors.append(f"{path}: XML構文エラー: {ex}")
        return errors

    if root.tag != "printstream":
        errors.append(f"{path}: ルート要素が printstream ではありません")

    data_path = root.find("./datadesc/file/path")
    if data_path is None:
        errors.append(f"{path}: datadesc/file/path がありません")
    else:
        if data_path.attrib.get("datatype") != "csv":
            errors.append(f"{path}: path datatype が csv ではありません")
        if data_path.attrib.get("target") != "data.txt":
            errors.append(f"{path}: path target が data.txt ではありません")

    page = root.find("./page")
    if page is None:
        errors.append(f"{path}: page がありません")
        return errors

    if page.attrib.get("orientation") != "portrait":
        errors.append(f"{path}: page orientation が portrait ではありません")

    position = page.find("./position")
    if position is None:
        errors.append(f"{path}: page/position がありません")
    else:
        expected = {"x": "8", "y": "8", "width": "156", "height": "272"}
        for key, value in expected.items():
            if position.attrib.get(key) != value:
                errors.append(f"{path}: A4縦基本 position {key}={value} ではありません")

    items = root.findall("./datadesc/datarecord/item")
    if not items:
        errors.append(f"{path}: datarecord/item がありません")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate Shift_JIS A4 portrait qfm files.")
    parser.add_argument("files", nargs="+", type=Path)
    args = parser.parse_args()

    all_errors: list[str] = []
    for path in args.files:
        errors = validate_qfm(path)
        if errors:
            all_errors.extend(errors)
        else:
            print(f"OK: {path}")

    if all_errors:
        for error in all_errors:
            print(error, file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
