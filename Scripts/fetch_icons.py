#!/usr/bin/env python3
"""
Подготовка оригинальных иконок предметов для DS1 Companion.

Приложение ищет PNG в Assets/Icons/{icon_key}.png, затем {id}.png (поля из Data/items_ds1.json).
Иконки игры не распространяются вместе с приложением — этот скрипт помогает разложить
ВАШИ собственные файлы (распакованные из вашей копии игры) или скачать их по шаблону URL.

Режимы:
  1) Из распакованной папки (рекомендуется):
       python Scripts/fetch_icons.py --from-dir D:/dsr_unpacked/icons
     Сопоставление: {icon_key}.*, {id}.*, slug(name_en).*, а также CSV-карта --map (item_id,file).
     Файлы .dds/.tga/.webp/.jpg конвертируются в PNG, если установлен Pillow (pip install pillow).

  2) Скачивание по шаблону URL (источник выбираете вы и отвечаете за права на изображения):
       python Scripts/fetch_icons.py --url-template "https://example.org/icons/{name_en_slug}.png"
     Подстановки: {id} {icon_key} {name_en} {name_en_slug}

  2б) Оригинальные иконки со страниц вики Dark Souls (Fextralife), запускать на своём ПК:
       python Scripts/fetch_icons.py --wiki
     Для каждого предмета открывается страница вики по name_en, берётся картинка предмета
     (og:image или первая картинка инфобокса). Неудачные предметы перечисляются в конце —
     для них можно задать название страницы вручную: --wiki-titles titles.csv (item_id,Название страницы).

  3) Прописать icon_url в items_ds1.json, чтобы приложение само докачивало иконки в фоне:
       python Scripts/fetch_icons.py --write-icon-urls "https://example.org/icons/{name_en_slug}.png"

  Дополнительно: --list-missing — показать предметы без иконки; --size 128 — размер при конвертации.
Только стандартная библиотека Python 3.9+; Pillow — опционально.
"""
from __future__ import annotations

import argparse
import csv
import json
import re
import shutil
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ITEMS = ROOT / "Data" / "items_ds1.json"
DEFAULT_OUT = ROOT / "src" / "DS1Companion.UI" / "Assets" / "Icons"
PNG_MAGIC = b"\x89PNG"
CONVERTIBLE = {".dds", ".tga", ".webp", ".jpg", ".jpeg", ".bmp"}
MAX_BYTES = 2 * 1024 * 1024


def slug(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", text.lower().replace("'", "")).strip("_")


def safe_name(text: str) -> str:
    return re.sub(r"[^A-Za-z0-9_\-]", "_", text).strip("._") or "unnamed"


def load_items() -> list[dict]:
    with ITEMS.open(encoding="utf-8") as f:
        return json.load(f)


def target_name(item: dict) -> str:
    return safe_name(item.get("icon_key") or item["id"]) + ".png"


def has_icon(item: dict, out: Path) -> bool:
    return any((out / (safe_name(k) + ".png")).exists() for k in filter(None, (item.get("icon_key"), item["id"])))


def substitutions(item: dict) -> dict:
    return {
        "id": item["id"],
        "icon_key": item.get("icon_key") or item["id"],
        "name_en": urllib.parse.quote(item["name_en"]),
        "name_en_slug": slug(item["name_en"]),
    }


def save_image(src: Path, dst: Path, size: int) -> bool:
    if src.suffix.lower() == ".png":
        shutil.copyfile(src, dst)
        return True
    if src.suffix.lower() not in CONVERTIBLE:
        return False
    try:
        from PIL import Image  # type: ignore
    except ImportError:
        print(f"  ! {src.name}: нужен Pillow для конвертации ({src.suffix}) — pip install pillow")
        return False
    with Image.open(src) as img:
        img = img.convert("RGBA")
        img.thumbnail((size, size))
        img.save(dst, "PNG")
    return True


def from_dir(source: Path, out: Path, mapping: Path | None, size: int) -> int:
    files = {}
    for p in source.rglob("*"):
        if p.is_file():
            files.setdefault(p.stem.lower(), p)
    explicit = {}
    if mapping:
        with mapping.open(encoding="utf-8") as f:
            for row in csv.reader(f):
                if len(row) >= 2 and not row[0].startswith("#"):
                    explicit[row[0].strip()] = row[1].strip()
    copied = 0
    for item in load_items():
        candidates = [explicit.get(item["id"]), item.get("icon_key"), item["id"], slug(item["name_en"]), item["name_en"]]
        for c in filter(None, candidates):
            src = (source / c) if (source / c).is_file() else files.get(Path(c).stem.lower())
            if src and save_image(src, out / target_name(item), size):
                copied += 1
                break
    return copied


def download(template: str, out: Path, delay: float, overwrite: bool) -> int:
    ok = 0
    for item in load_items():
        dst = out / target_name(item)
        if dst.exists() and not overwrite:
            continue
        url = template.format(**substitutions(item))
        if urllib.parse.urlparse(url).scheme not in ("http", "https"):
            print(f"  ! {item['id']}: неподдерживаемый URL {url}")
            continue
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "DS1Companion-icon-fetch/1.0"})
            with urllib.request.urlopen(req, timeout=15) as resp:
                data = resp.read(MAX_BYTES + 1)
            if len(data) > MAX_BYTES or not data.startswith(PNG_MAGIC):
                print(f"  - {item['id']}: не PNG или слишком большой файл")
                continue
            dst.write_bytes(data)
            ok += 1
            print(f"  + {item['id']}")
        except Exception as exc:  # сеть/404 — просто пропускаем
            print(f"  - {item['id']}: {exc}")
        time.sleep(delay)
    return ok


WIKI_BASE = "https://darksouls.wiki.fextralife.com"
WIKI_IMG = re.compile(r'(?:src|data-src)="((?:https?://darksouls\.wiki\.fextralife\.com)?/file/Dark-Souls/[^"?#]+\.(?:png|jpe?g|webp))"', re.I)
OG_IMG = re.compile(r'<meta[^>]+property="og:image"[^>]+content="([^"]+)"', re.I)
SKIP_IMG = re.compile(r"logo|banner|icon_s|souls_wiki|fextralife|favicon|_map|map_", re.I)


def wiki_page_url(title: str) -> str:
    return f"{WIKI_BASE}/{urllib.parse.quote(title.replace(' ', '+'), safe="+()'")}"


def extract_wiki_image(html: str) -> str | None:
    """Картинка предмета со страницы вики: og:image, затем первая картинка в инфобоксе, затем первая подходящая."""
    def absolute(u: str) -> str:
        return u if u.startswith("http") else WIKI_BASE + u
    m = OG_IMG.search(html)
    if m and "/file/Dark-Souls/" in m.group(1) and not SKIP_IMG.search(m.group(1).rsplit("/", 1)[-1]):
        return absolute(m.group(1))
    start = max(html.find("infobox"), 0)
    for chunk in (html[start:], html):
        for m in WIKI_IMG.finditer(chunk):
            if not SKIP_IMG.search(m.group(1).rsplit("/", 1)[-1]):
                return absolute(m.group(1))
    return None


def http_get(url: str, limit: int) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": "DS1Companion-icon-fetch/1.0 (personal use)"})
    with urllib.request.urlopen(req, timeout=20) as resp:
        return resp.read(limit + 1)


def save_downloaded(data: bytes, dst: Path, size: int) -> None:
    """Сохраняет картинку как PNG (с Pillow — конвертирует и уменьшает; без него — как есть, WPF читает по содержимому)."""
    try:
        from PIL import Image  # type: ignore
        import io
        with Image.open(io.BytesIO(data)) as img:
            img = img.convert("RGBA")
            img.thumbnail((size, size))
            img.save(dst, "PNG")
    except ImportError:
        dst.write_bytes(data)


def fetch_wiki(out: Path, titles_csv: Path | None, delay: float, overwrite: bool, size: int, only: set[str]) -> int:
    titles = {}
    if titles_csv:
        with titles_csv.open(encoding="utf-8") as f:
            titles = {r[0].strip(): r[1].strip() for r in csv.reader(f) if len(r) >= 2 and not r[0].startswith("#")}
    ok, failed = 0, []
    for item in load_items():
        if only and item["id"] not in only:
            continue
        dst = out / target_name(item)
        if dst.exists() and not overwrite:
            continue
        title = titles.get(item["id"], item["name_en"])
        try:
            html = http_get(wiki_page_url(title), 3_000_000).decode("utf-8", "replace")
            img = extract_wiki_image(html)
            if not img:
                failed.append((item["id"], title, "картинка не найдена на странице"))
                continue
            data = http_get(img, MAX_BYTES)
            if len(data) > MAX_BYTES:
                failed.append((item["id"], title, "слишком большой файл"))
                continue
            save_downloaded(data, dst, size)
            ok += 1
            print(f"  + {item['id']:<28} {img}")
        except Exception as exc:
            failed.append((item["id"], title, str(exc)))
        time.sleep(delay)
    if failed:
        print(f"\nНе получилось для {len(failed)} предметов (задайте название страницы в --wiki-titles):")
        for iid, title, why in failed:
            print(f"  - {iid} ({title}): {why}")
    return ok


def write_icon_urls(template: str) -> None:
    items = load_items()
    for item in items:
        item["icon_url"] = template.format(**substitutions(item))
    with ITEMS.open("w", encoding="utf-8") as f:
        json.dump(items, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print(f"icon_url прописан для {len(items)} предметов в {ITEMS}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--from-dir", type=Path)
    ap.add_argument("--map", type=Path, help="CSV: item_id,имя_файла")
    ap.add_argument("--url-template")
    ap.add_argument("--write-icon-urls", metavar="TEMPLATE")
    ap.add_argument("--wiki", action="store_true", help="скачать оригинальные иконки со страниц вики Dark Souls")
    ap.add_argument("--wiki-titles", type=Path, help="CSV: item_id,Название страницы вики")
    ap.add_argument("--only", nargs="*", default=[], help="ограничиться этими id предметов")
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--size", type=int, default=128)
    ap.add_argument("--delay", type=float, default=0.3, help="пауза между запросами, сек")
    ap.add_argument("--overwrite", action="store_true")
    ap.add_argument("--list-missing", action="store_true")
    args = ap.parse_args()

    args.out.mkdir(parents=True, exist_ok=True)
    if args.write_icon_urls:
        write_icon_urls(args.write_icon_urls)
    if args.from_dir:
        print(f"Скопировано иконок: {from_dir(args.from_dir, args.out, args.map, args.size)}")
    if args.wiki:
        n = fetch_wiki(args.out, args.wiki_titles, max(args.delay, 1.0), args.overwrite, args.size, set(args.only))
        print(f"Скачано иконок с вики: {n}")
    if args.url_template:
        print(f"Скачано иконок: {download(args.url_template, args.out, args.delay, args.overwrite)}")
    if args.list_missing or not (args.from_dir or args.url_template or args.write_icon_urls or args.wiki):
        missing = [i["id"] for i in load_items() if not has_icon(i, args.out)]
        print(f"Без иконки: {len(missing)} из {len(load_items())}")
        for m in missing:
            print("  ", m)
    return 0


if __name__ == "__main__":
    sys.exit(main())
