#!/usr/bin/env python3
"""End-to-end check: campaigns really deliver messages through a channel.

Runs a local HTTP receiver, wires an API channel to it, and verifies:
  1. A Message campaign (no workflow) sends one message per contact through the channel.
  2. A WorkflowStart campaign runs ITS OWN workflow (a decoy listening to the same event must
     not run), the designer-format definition executes, and {{payload.contact.recipient}}
     resolves to each contact.
  3. Every contact is delivered (several contacts per campaign; the connect inbox used to
     accept only one).

Usage (against a running local stack):
  python scripts/e2e/campaign-delivery-e2e.py \
      --config src/AgentFlow.Api/appsettings.Development.json \
      [--api http://localhost:5000] [--tenant tenant-1] [--sink-port 3601]

Signs in with the dev admin from the config file (never printed). Exits 1 on any failure.
Test data is prefixed "e2e-"; the test channel is deleted at the end and the workflows listen
to a unique event name, so they never capture real campaign events.
"""
import argparse
import json
import sys
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, HTTPServer

RECEIVED: list[dict] = []


class Receiver(BaseHTTPRequestHandler):
    def _read_body(self) -> bytes:
        # .NET PostAsJsonAsync streams the body with chunked transfer encoding (no Content-Length).
        if "chunked" in (self.headers.get("Transfer-Encoding") or "").lower():
            chunks = []
            while True:
                size = int(self.rfile.readline().strip().split(b";")[0] or b"0", 16)
                if size == 0:
                    self.rfile.readline()  # trailing CRLF after the last chunk
                    break
                chunks.append(self.rfile.read(size))
                self.rfile.readline()  # CRLF after each chunk
            return b"".join(chunks)
        return self.rfile.read(int(self.headers.get("Content-Length", 0)))

    def do_POST(self):  # noqa: N802 (http.server API)
        raw = self._read_body()
        try:
            RECEIVED.append(json.loads(raw))
        except ValueError:
            RECEIVED.append({"raw": raw.decode("utf-8", "replace")})
        self.send_response(200)
        self.end_headers()
        self.wfile.write(b"{}")

    def log_message(self, *_):
        pass


class Api:
    def __init__(self, base: str):
        self.base = base
        self.token: str | None = None

    def call(self, method: str, path: str, body=None):
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, method=method)
        req.add_header("Content-Type", "application/json")
        if self.token:
            req.add_header("Authorization", "Bearer " + self.token)
        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                return resp.status, json.loads(resp.read().decode() or "null")
        except urllib.error.HTTPError as err:
            raw = err.read().decode()
            try:
                return err.code, json.loads(raw)
            except ValueError:
                return err.code, raw[:300]


FAILURES: list[str] = []


def check(condition: bool, label: str, detail: str = "") -> None:
    print(("  PASS " if condition else "  FAIL ") + label + (f"  ({detail})" if detail and not condition else ""))
    if not condition:
        FAILURES.append(label)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    parser.add_argument("--api", default="http://localhost:5000")
    parser.add_argument("--tenant", default="tenant-1")
    parser.add_argument("--sink-port", type=int, default=3601)
    args = parser.parse_args()

    server = HTTPServer(("127.0.0.1", args.sink_port), Receiver)
    threading.Thread(target=server.serve_forever, daemon=True).start()

    with open(args.config, encoding="utf-8-sig") as fh:
        admin = json.load(fh)["Auth"]["Admin"]
    api = Api(args.api)
    status, auth = api.call("POST", "/api/auth/sign-in", {"email": admin["Email"], "password": admin["Password"]})
    if status != 200:
        print(f"sign-in failed: HTTP {status}")
        return 1
    api.token = auth["accessToken"]

    run = str(int(time.time()))[-7:]
    t = f"/api/v1/tenants/{args.tenant}"
    event = f"e2e.campaign.{run}"
    print(f"[e2e] run {run}")

    # Channel wired to the local receiver.
    _, channel = api.call("POST", f"{t}/channels", {
        "name": f"e2e-api-{run}", "type": "Api",
        "config": {"WebhookCallbackUrl": f"http://localhost:{args.sink_port}/hook"}})
    channel_id = channel["id"]
    api.call("POST", f"{t}/channels/{channel_id}/activate")

    # Two contacts (campaign audiences come from commerce parties).
    recipients = [f"e2e{run}a", f"e2e{run}b"]
    for r in recipients:
        api.call("POST", f"{t}/commerce/crm/resolve-party", {
            "channel": "api", "identifier": r, "displayName": f"E2E {r}", "kind": "customer"})

    def publish_workflow(wf_id: str, content: str) -> int:
        definition = {"activities": [{"id": "send", "type": "channel.send", "config": {
            "channelId": channel_id, "to": "{{payload.contact.recipient}}", "content": content}}]}
        api.call("PUT", f"{t}/studio/workflows/{wf_id}", {
            "name": wf_id, "triggerEventName": event, "runtimeKind": "Text",
            "definitionJson": json.dumps(definition)})
        status, body = api.call("POST", f"{t}/studio/workflows/{wf_id}/publish")
        if status >= 300:
            print(f"  publish {wf_id} -> {status} {body}")
        return status

    wf_id, decoy_id = f"e2e-wf-{run}", f"e2e-decoy-{run}"
    check(publish_workflow(wf_id, f"WF {run}") == 200, "designer-format workflow with channel.send publishes")
    publish_workflow(decoy_id, f"DECOY {run}")

    def run_campaign(name: str, body: dict):
        _, camp = api.call("POST", f"{t}/campaigns", {"name": name, "audienceFilterJson": json.dumps({"query": f"e2e{run}"}), **body})
        api.call("POST", f"{t}/campaigns/{camp['id']}/publish")
        _, result = api.call("POST", f"{t}/campaigns/{camp['id']}/run-now")
        _, contacts = api.call("GET", f"{t}/campaign-runs/{result['id']}/contacts")
        return contacts if isinstance(contacts, list) else []

    # 1. Direct message campaign (ChannelAction.Message = 0), channel addressed by id.
    message_contacts = run_campaign(f"e2e-message-{run}", {"channelAction": 0, "channel": channel_id, "messageDraft": f"MSG {run}"})
    # 2. Workflow campaign (ChannelAction.WorkflowStart = 2) pointing at its own workflow.
    workflow_contacts = run_campaign(f"e2e-workflow-{run}", {"channelAction": 2, "channel": "api", "workflowDefinitionId": wf_id})

    time.sleep(8)  # let the workflow worker process the queued executions

    msg = [m for m in RECEIVED if m.get("content") == f"MSG {run}"]
    wf = [m for m in RECEIVED if m.get("content") == f"WF {run}"]
    decoy = [m for m in RECEIVED if m.get("content") == f"DECOY {run}"]

    print("[e2e] message campaign")
    check(len(message_contacts) == 2, "audience has 2 contacts", str(len(message_contacts)))
    check(all(c.get("status") == 1 for c in message_contacts), "every contact marked Sent", str([c.get("status") for c in message_contacts]))
    check(len(msg) == 2, "receiver got 2 messages", str(len(msg)))

    print("[e2e] workflow campaign")
    check(all(c.get("status") == 6 for c in workflow_contacts), "every contact started the workflow", str([c.get("status") for c in workflow_contacts]))
    check(len(wf) == 2, "receiver got 2 workflow messages", str(len(wf)))
    check(not decoy, "decoy workflow did not run", str(len(decoy)))
    _, execs = api.call("GET", f"{t}/studio/workflows/executions?limit=20")
    mine = [e for e in execs if e.get("workflowDefinitionId") == wf_id] if isinstance(execs, list) else []
    check(len(mine) == 2 and all(e.get("status") == 2 for e in mine), "workflow executions completed",
          str([(e.get("status"), e.get("error")) for e in mine]))

    print("[e2e] recipients")
    check(sorted(m.get("to") for m in msg) == sorted(recipients), "message campaign reached each contact", str([m.get("to") for m in msg]))
    check(sorted(m.get("to") for m in wf) == sorted(recipients), "{{payload.contact.recipient}} resolved per contact", str([m.get("to") for m in wf]))
    api.call("POST", f"{t}/channels/{channel_id}/deactivate")
    api.call("DELETE", f"{t}/channels/{channel_id}")
    server.shutdown()

    print(f"[e2e] {'OK' if not FAILURES else 'FAILED: ' + ', '.join(FAILURES)}")
    return 0 if not FAILURES else 1


if __name__ == "__main__":
    sys.exit(main())
