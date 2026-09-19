"""One-off inspection of icon links served by the supported sites."""
from concurrent.futures import ThreadPoolExecutor
from html.parser import HTMLParser
from urllib.request import Request, urlopen


SITES = {
    "chatgpt": "https://chatgpt.com/",
    "claude": "https://claude.ai/",
    "gemini": "https://gemini.google.com/",
    "deepseek": "https://chat.deepseek.com/",
    "doubao": "https://www.doubao.com/chat/",
    "qwen": "https://www.qianwen.com/",
}


class Icons(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []

    def handle_starttag(self, tag, attrs):
        if tag != "link":
            return
        attrs = dict(attrs)
        if "icon" in attrs.get("rel", ""):
            self.links.append((attrs.get("rel"), attrs.get("href"), attrs.get("type")))


def inspect(item):
    site, url = item
    try:
        request = Request(url, headers={"User-Agent": "Mozilla/5.0 QiyeIconInspection/1.0"})
        with urlopen(request, timeout=12) as response:
            data = response.read(2_000_000)
            parser = Icons()
            parser.feed(data.decode("utf-8", errors="replace"))
            return site, response.status, response.url, parser.links[:12]
    except Exception as exc:
        return site, str(exc), url, []


with ThreadPoolExecutor(max_workers=6) as executor:
    for result in executor.map(inspect, SITES.items()):
        print(result)
