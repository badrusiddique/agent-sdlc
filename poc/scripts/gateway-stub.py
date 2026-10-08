#!/usr/bin/env python3
"""Minimal OpenAI-shaped chat stub for local AI Gateway client smoke tests."""
from __future__ import annotations

import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt: str, *args) -> None:  # quieter
        pass

    def do_POST(self) -> None:  # noqa: N802
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length)
        key = self.headers.get("Ocp-Apim-Subscription-Key", "")
        model = self.headers.get("X-AIGW-Model", "openai")
        if not key:
            self.send_response(401)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(b'{"statusCode":401,"message":"missing subscription key"}')
            return

        try:
            body = json.loads(raw.decode() or "{}")
        except json.JSONDecodeError:
            body = {}

        messages = body.get("messages") or []
        last = messages[-1]["content"] if messages else ""
        text = f"gateway-stub ok model={model} echo={last[:80]}"

        payload = {
            "id": "chatcmpl-stub",
            "object": "chat.completion",
            "choices": [
                {
                    "index": 0,
                    "message": {"role": "assistant", "content": text},
                    "finish_reason": "stop",
                }
            ],
        }
        data = json.dumps(payload).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


def main() -> None:
    host, port = "127.0.0.1", 43190
    httpd = ThreadingHTTPServer((host, port), Handler)
    print(f"gateway-stub listening on http://{host}:{port}/ai/chat/completions", flush=True)
    httpd.serve_forever()


if __name__ == "__main__":
    main()
