#!/usr/bin/env python3
from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path


def _line_col(text: str, token: str) -> str:
	index = text.find(token)
	if index < 0:
		return ""
	line = text.count("\n", 0, index) + 1
	col = index - text.rfind("\n", 0, index)
	return f" at line {line}, column {col}"


def validate(path: Path) -> int:
	errors: list[str] = []
	warnings: list[str] = []

	try:
		raw = path.read_bytes()
	except OSError as exc:
		print(f"{path}: ERROR: cannot read file: {exc}")
		return 1

	if b'encoding="SHIFT_JIS"' not in raw[:160] and b"encoding='SHIFT_JIS'" not in raw[:160]:
		errors.append('XML declaration must include encoding="SHIFT_JIS"')

	try:
		text = raw.decode("shift_jis")
	except UnicodeDecodeError as exc:
		print(f"{path}: ERROR: file is not valid Shift_JIS: {exc}")
		return 1

	try:
		root = ET.fromstring(text)
	except ET.ParseError as exc:
		print(f"{path}: ERROR: XML parse failed: {exc}")
		return 1

	if root.tag != "printstream":
		errors.append(f"root element must be printstream, got {root.tag!r}")
	if root.get("version") != "3.0":
		warnings.append('expected printstream version="3.0"')

	data_path = root.find("./datadesc/file/path")
	if data_path is None:
		errors.append("missing datadesc/file/path")
	else:
		if data_path.get("datatype") != "csv":
			errors.append('datadesc/file/path must use datatype="csv"')
		if data_path.get("target") != "data.txt":
			errors.append('datadesc/file/path must use target="data.txt"')

	items = [item.get("id") for item in root.findall("./datadesc/datarecord/item")]
	item_ids = [item_id for item_id in items if item_id]
	if not item_ids:
		errors.append("missing datadesc/datarecord/item definitions")

	duplicate_items = [item_id for item_id, count in Counter(item_ids).items() if count > 1]
	if duplicate_items:
		errors.append(f"duplicate data item ids: {', '.join(sorted(duplicate_items))}")

	element_ids = [element.get("id") for element in root.iter() if element.get("id")]
	duplicate_element_ids = [element_id for element_id, count in Counter(element_ids).items() if count > 1]
	if duplicate_element_ids:
		errors.append(f"duplicate element ids: {', '.join(sorted(duplicate_element_ids))}")

	item_refs = [data.get("datasrc") for data in root.iter("data") if data.get("calctype") == "item"]
	item_refs = [item_ref for item_ref in item_refs if item_ref]
	missing_refs = sorted(set(item_refs) - set(item_ids))
	if missing_refs:
		for item_ref in missing_refs:
			errors.append(f"datasrc {item_ref!r} has no matching datarecord item{_line_col(text, f'datasrc=\"{item_ref}\"')}")

	unused_items = sorted(set(item_ids) - set(item_refs))
	if unused_items:
		warnings.append(f"unused datarecord items: {', '.join(unused_items)}")

	pages = root.findall("./page")
	if not pages:
		errors.append("missing page")
	for page in pages:
		if page.get("compatibility") != "3.0.0":
			warnings.append(f"page {page.get('id', '<unknown>')} expected compatibility=\"3.0.0\"")

	for message in warnings:
		print(f"{path}: WARN: {message}")
	for message in errors:
		print(f"{path}: ERROR: {message}")

	if errors:
		return 1

	print(f"{path}: OK ({len(item_ids)} items, {len(item_refs)} item references, {len(pages)} page(s))")
	return 0


def main(argv: list[str]) -> int:
	if len(argv) < 2:
		print("Usage: validate_qfm.py <file.qfm> [more.qfm ...]", file=sys.stderr)
		return 2

	status = 0
	for name in argv[1:]:
		status = max(status, validate(Path(name)))
	return status


if __name__ == "__main__":
	raise SystemExit(main(sys.argv))
