"""HTTP client dùng chung cho B12, chỉ dùng thư viện chuẩn Python."""
import json
import os
import urllib.error
import urllib.request


class Api:
    def __init__(self, base=None):
        self.base = (base or os.environ.get("ASX_API_BASE", "http://localhost:5080")).rstrip("/")
        self.token = os.environ.get("ASX_TOKEN", "")

    def call(self, method, path, body=None, headers=None, raw=False):
        data = json.dumps(body).encode("utf-8") if body is not None else None
        request = urllib.request.Request(self.base + path, data=data, method=method)
        request.add_header("Accept", "application/json")
        if data is not None:
            request.add_header("Content-Type", "application/json")
        if self.token:
            request.add_header("Authorization", "Bearer " + self.token)
        for key, value in (headers or {}).items():
            request.add_header(key, value)
        try:
            response = urllib.request.urlopen(request, timeout=120)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            text = response.read().decode("utf-8")
            result = text if raw else (json.loads(text) if text.strip() else None)
            return response.status, result, dict(response.headers)

    def expect(self, method, path, status, body=None, headers=None, raw=False):
        actual, result, response_headers = self.call(method, path, body, headers, raw)
        if actual != status:
            raise AssertionError("%s %s: HTTP %s, expected %s: %s" % (method, path, actual, status, result))
        return result, response_headers
