"""Refresh bundled website favicons from the providers' own asset hosts."""
from hashlib import sha256
from pathlib import Path
from urllib.request import Request, urlopen
from time import sleep


ASSETS = Path(__file__).resolve().parents[1] / "src" / "Qiye.App" / "Assets" / "Sites"
SOURCES = {
    "chatgpt.ico": "https://cdn.oaistatic.com/assets/favicon-eex17e9e.ico",
    "claude.ico": "https://claude.ai/favicon.ico",
    "gemini.png": "https://www.gstatic.com/lamda/images/gemini_sparkle_4g_512_lt_f94943af3be039176192d.png",
    "deepseek.png": "https://fe-static.deepseek.com/chat/icon-180.png",
    "doubao.png": "https://lf-flow-web-cdn.doubao.com/obj/flow-doubao/favicon/new-doubao/128x128.png",
    "qwen.png": "https://img.alicdn.com/imgextra/i2/O1CN01taBbMS1CfyJoOt0lB_!!6000000000109-2-tps-80-80.png",
    "zhipu.png": "https://chatglm.cn/favicon.ico",
}

ASSETS.mkdir(parents=True, exist_ok=True)
for name, url in SOURCES.items():
    request = Request(url, headers={"User-Agent": "Mozilla/5.0"})
    for attempt in range(3):
        try:
            with urlopen(request, timeout=15) as response:
                content_type = response.headers.get("Content-Type", "").lower()
                data = response.read(1_000_001)
            break
        except Exception:
            if attempt == 2:
                raise
            sleep(attempt + 1)
    if len(data) > 1_000_000:
        raise ValueError(f"{name}: asset unexpectedly large")
    if name.endswith(".png") and (not data.startswith(b"\x89PNG\r\n\x1a\n") or "image/png" not in content_type):
        raise ValueError(f"{name}: expected PNG, got {content_type}")
    if name.endswith(".ico") and not data.startswith(b"\x00\x00\x01\x00"):
        raise ValueError(f"{name}: expected ICO, got {content_type}")
    (ASSETS / name).write_bytes(data)
    print(f"{name}: {len(data)} bytes sha256={sha256(data).hexdigest()} source={url}")
