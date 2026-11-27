"""End-to-end check of the collections happy path, against a running API.

Portfolio -> campaign by delinquency stage -> personalized messages -> customer replies ->
outcomes (promise to pay, opt-out) -> collections promise -> next cut-off resolves the promise.
Also: workflow-driven campaign that records its own outcome, operator override, opt-out skip.

Usage:
    python -I scripts/e2e/collections-campaign-e2e.py --config src/AgentFlow.Api/appsettings.Development.json

Messages go to an "Api" channel wired to a local receiver, so nothing reaches real phones.
Credentials are read from the config file and never printed. Test data uses the "E2E-" prefix.
"""
import argparse
import json
import sys
import threading
import time
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

RECEIVED: list[dict] = []
FAILURES: list[str] = []


class Receiver(BaseHTTPRequestHandler):
    def _read_body(self) -> bytes:
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            data = b""
            while True:
                size = int(self.rfile.readline().strip() or b"0", 16)
                if size == 0:
                    self.rfile.readline()
                    return data
                data += self.rfile.read(size)
                self.rfile.readline()
        return self.rfile.read(int(self.headers.get("Content-Length", "0") or 0))

    def do_POST(self):  # noqa: N802
        try:
            RECEIVED.append(json.loads(self._read_body() or b"{}"))
        except ValueError:
            pass
        self.send_response(200)
        self.end_headers()
        self.wfile.write(b"{}")

    def log_message(self, *_):
        pass


class Api:
    def __init__(self, base: str):
        self.base = base
        self.token = None

    def call(self, method: str, path: str, body=None):
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, method=method)
        req.add_header("Content-Type", "application/json")
        if self.token:
            req.add_header("Authorization", "Bearer " + self.token)
        try:
            with urllib.request.urlopen(req, timeout=120) as resp:
                text = resp.read().decode("utf-8-sig") or "null"
                return resp.status, json.loads(text)
        except urllib.error.HTTPError as err:
            text = err.read().decode()
            try:
                return err.code, json.loads(text)
            except ValueError:
                return err.code, text[:300]


def check(condition: bool, label: str, detail: str = "") -> None:
    print(f"  [{'PASS' if condition else 'FAIL'}] {label}" + (f" — {detail}" if detail and not condition else ""))
    if not condition:
        FAILURES.append(label)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    parser.add_argument("--api", default="http://localhost:5000")
    parser.add_argument("--tenant", default="tenant-1")
    parser.add_argument("--sink-port", type=int, default=3602)
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
    c = f"{t}/collections"
    print(f"[e2e] run {run}")
    api.call("POST", f"/api/v1/extensions/tenants/{args.tenant}/install", {"extensionId": "collections", "enableAfterInstall": True})

    _, channel = api.call("POST", f"{t}/channels", {
        "name": f"e2e-collections-{run}", "type": "Api",
        "config": {"WebhookCallbackUrl": f"http://localhost:{args.sink_port}/hook"}})
    channel_id = channel["id"]
    api.call("POST", f"{t}/channels/{channel_id}/activate")

    phone = {k: f"+52155{run}{n}" for k, n in (("A", "01"), ("B", "02"), ("C", "03"))}
    cust = {k: f"E2E-{run}-{k}" for k in "ABC"}
    today = datetime.now(timezone.utc).date()

    def portfolio(cutoff, a1_overdue, a2_overdue):
        return {"snapshotDate": f"{cutoff}T00:00:00Z", "dryRun": False, "rows": [
            {"customerId": cust["A"], "fullName": "Ana E2E", "phone": phone["A"], "loanId": f"{cust['A']}-L1", "totalBalance": 3000, "overduePrincipal": a1_overdue, "daysPastDue": 20 if a1_overdue else 0, "product": "Personal"},
            {"customerId": cust["A"], "fullName": "Ana E2E", "phone": phone["A"], "loanId": f"{cust['A']}-L2", "totalBalance": 5000, "overduePrincipal": a2_overdue, "daysPastDue": 45 if a2_overdue else 0, "product": "Nomina"},
            {"customerId": cust["B"], "fullName": "Beto E2E", "phone": phone["B"], "loanId": f"{cust['B']}-L1", "totalBalance": 9000, "overduePrincipal": 900, "daysPastDue": 95, "product": "Personal"},
            {"customerId": cust["C"], "fullName": "Caro E2E", "phone": phone["C"], "loanId": f"{cust['C']}-L1", "totalBalance": 1000, "daysPastDue": 0, "product": "Personal"},
        ]}

    print("1. Portfolio")
    status, imp = api.call("POST", f"{c}/accounts/batch", portfolio(today, 300, 200))
    check(status == 200 and imp["loansCreated"] == 4 and imp["customers"] == 3, "3 customers / 4 loans imported", str(imp)[:200])
    _, loans = api.call("GET", f"{c}/loans?query=E2E-{run}&sort=overdue")
    check(loans["total"] == 4 and loans["items"][0]["loanExternalId"] == f"{cust['B']}-L1", "loan list filters and sorts by overdue")
    _, staged = api.call("GET", f"{c}/loans?query=E2E-{run}&stage=Dpd31To60")
    check(staged["total"] == 1, "loan list filters by stage")

    print("2. Campaign over the portfolio (direct message, personalized)")
    template = "Hola {{primer_nombre}}, tu saldo vencido es ${{total_vencido}} ({{dias_atraso}} dias, {{num_prestamos}} prestamos)."
    filter_json = json.dumps({"source": "collections", "customerIds": [cust["A"], cust["B"], cust["C"]], "minDaysPastDue": 1})
    _, preview = api.call("POST", f"{t}/campaign-segments/preview", {"filterJson": filter_json})
    check(preview["estimatedCount"] == 2, "audience: one contact per delinquent customer (current customer excluded)", str(preview.get("estimatedCount")))
    _, camp = api.call("POST", f"{t}/campaigns", {
        "name": f"E2E cobranza {run}", "campaignType": 1, "channel": "api", "channelAction": 0, "executionMode": 1,
        "triggerType": 1, "messageDraft": template, "audienceFilterJson": filter_json})
    api.call("POST", f"{t}/campaigns/{camp['id']}/publish")
    RECEIVED.clear()
    _, run1 = api.call("POST", f"{t}/campaigns/{camp['id']}/run-now")
    counters = json.loads(run1["countersJson"])
    check(counters.get("Sent") == 2, "both customers sent", str(counters))
    time.sleep(1)
    texts = [str(m.get("content") or m.get("text") or m) for m in RECEIVED]
    ana_msg = next((x for x in texts if "Ana" in x), "")
    check("$500.00" in ana_msg and "45 dias" in ana_msg and "2 prestamos" in ana_msg and "{{" not in ana_msg,
          "message rendered with the customer's own loan data", ana_msg[:160])

    print("3. Replies are attributed and classified")
    api.call("POST", f"{t}/channels/{channel_id}/messages", {"from": phone["A"], "content": "Te pago 500 el viernes"})
    api.call("POST", f"{t}/channels/{channel_id}/messages", {"from": phone["B"], "content": "Ya no me escriban por favor"})
    _, res = api.call("GET", f"{t}/campaigns/{camp['id']}/results")
    outcomes = {o["code"]: o["count"] for o in res["outcomes"]}
    check(outcomes.get("promise_to_pay") == 1 and outcomes.get("opt_out") == 1, "outcomes: 1 promise to pay, 1 opt-out", str(outcomes))
    check(res["replied"] == 2 and res["promises"] == 1 and res["promisedAmount"] == 500, "funnel: 2 replied, 1 promise of $500", str({k: res[k] for k in ("replied", "promises", "promisedAmount")}))

    print("4. The promise lands in Cobranza")
    _, promises = api.call("GET", f"{c}/promises?status=Pending")
    ana_promise = next((p for p in promises["items"] if p.get("customerName") == "Ana E2E"), None)
    check(ana_promise is not None and ana_promise["amount"] == 500 and ana_promise["overdueAtCreation"] == 500, "pending promise for Ana with $500 overdue baseline", str(ana_promise)[:200])
    party_a = ana_promise["partyId"] if ana_promise else None
    _, cust_detail = api.call("GET", f"{c}/customers/{party_a}")
    check(any(x["outcome"] == "promise_to_pay" for x in cust_detail.get("contacts", [])), "customer file shows the campaign contact and its result")

    print("5. Operator override and opt-out on the next run")
    ana_contact = next(x for x in res["contactsList"] if x["recipient"] == phone["A"])
    status, overridden = api.call("POST", f"{t}/campaign-contacts/{ana_contact['id']}/outcome", {"outcome": "needs_human", "notes": "pidio hablar con asesor"})
    check(status == 200 and overridden["outcomeSource"] == "human", "operator can set the outcome")
    api.call("POST", f"{t}/channels/{channel_id}/messages", {"from": phone["A"], "content": "no gracias"})
    _, res = api.call("GET", f"{t}/campaigns/{camp['id']}/results")
    ana_contact = next(x for x in res["contactsList"] if x["recipient"] == phone["A"])
    check(ana_contact["outcome"] == "needs_human" and ana_contact["lastReply"] == "no gracias", "a later reply does not overwrite the operator's outcome")
    _, run2 = api.call("POST", f"{t}/campaigns/{camp['id']}/run-now")
    counters2 = json.loads(run2["countersJson"])
    check(counters2.get("Skipped") == 1 and counters2.get("Sent") == 1, "customer who opted out is skipped next time", str(counters2))

    print("6. Workflow-driven campaign records its own outcome")
    wf_id = f"e2e-collect-wf-{run}"
    definition = {"activities": [
        {"id": "send", "type": "channel.send", "config": {"channelId": channel_id, "to": "{{payload.recipient}}", "content": "WF: {{payload.message}}"}, "next": "outcome"},
        {"id": "outcome", "type": "campaign.record_outcome", "config": {"outcome": "callback_requested", "notes": "seguimiento automatico"}}]}
    api.call("PUT", f"{t}/studio/workflows/{wf_id}", {"name": wf_id, "triggerEventName": f"e2e.collect.{run}", "runtimeKind": "Text", "definitionJson": json.dumps(definition)})
    status, body = api.call("POST", f"{t}/studio/workflows/{wf_id}/publish")
    check(status == 200, "workflow with campaign.record_outcome publishes", str(body)[:200])
    _, wcamp = api.call("POST", f"{t}/campaigns", {
        "name": f"E2E cobranza wf {run}", "campaignType": 1, "channel": "api", "channelAction": 2, "executionMode": 0,
        "triggerType": 1, "workflowDefinitionId": wf_id, "messageDraft": "Hola {{primer_nombre}}, debes ${{total_vencido}}",
        "audienceFilterJson": json.dumps({"source": "collections", "customerIds": [cust["C"], cust["B"]], "includeChargedOff": True, "minDaysPastDue": 90})})
    RECEIVED.clear()
    _, wrun = api.call("POST", f"{t}/campaigns/{wcamp['id']}/run-now")
    time.sleep(2)
    _, wres = api.call("GET", f"{t}/campaigns/{wcamp['id']}/results")
    check(json.loads(wrun["countersJson"]).get("Skipped") == 1, "workflow campaign also skips the opted-out customer", wrun["countersJson"])
    # Beto opted out, so use a fresh customer for the workflow path.
    api.call("POST", f"{c}/accounts/batch", {"snapshotDate": f"{today}T00:00:00Z", "rows": [
        {"customerId": f"E2E-{run}-D", "fullName": "Dora E2E", "phone": f"+52155{run}04", "loanId": f"E2E-{run}-D-L1", "totalBalance": 2000, "overduePrincipal": 700, "daysPastDue": 120}]})
    api.call("PUT", f"{t}/campaigns/{wcamp['id']}", {"name": wcamp["name"], "audienceFilterJson": json.dumps({"source": "collections", "customerIds": [f"E2E-{run}-D"], "minDaysPastDue": 90})})
    RECEIVED.clear()
    api.call("POST", f"{t}/campaigns/{wcamp['id']}/run-now")
    time.sleep(2)
    _, wres = api.call("GET", f"{t}/campaigns/{wcamp['id']}/results")
    dora = next((x for x in wres["contactsList"] if x["recipient"].endswith(f"{run}04")), {})
    wf_texts = [str(m.get("content") or m) for m in RECEIVED]
    check(any("WF: Hola Dora, debes $700.00" in x for x in wf_texts), "workflow sent the rendered campaign message", str(wf_texts)[:200])
    check(dora.get("outcome") == "callback_requested" and dora.get("outcomeSource") == "workflow", "workflow recorded the outcome", str({k: dora.get(k) for k in ("outcome", "outcomeSource", "status")}))

    print("7. Next cut-off resolves the promise")
    api.call("POST", f"{c}/accounts/batch", portfolio(today + timedelta(days=1), 300, 0))
    _, pending = api.call("GET", f"{c}/promises?status=Pending")
    check(any(p.get("customerName") == "Ana E2E" for p in pending["items"]), "partial payment ($200 of $500) keeps the promise pending")
    api.call("POST", f"{c}/accounts/batch", portfolio(today + timedelta(days=2), 0, 0))
    _, kept = api.call("GET", f"{c}/promises?status=Kept")
    check(any(p.get("customerName") == "Ana E2E" for p in kept["items"]), "full payment on the next cut-off -> promise kept")

    print("8. Follow-ups: no response, promise reminder, hand-off, payments, daily cap")
    fu = {k: f"E2E-{run}-{k}" for k in ("E", "F", "G")}
    fphone = {"E": f"+52155{run}05", "F": f"+52155{run}06", "G": f"+52155{run}07"}
    api.call("POST", f"{c}/accounts/batch", {"snapshotDate": f"{today}T00:00:00Z", "rows": [
        {"customerId": fu[k], "fullName": f"{k}ena E2E", "phone": fphone[k], "loanId": f"{fu[k]}-L1",
         "totalBalance": 2000, "overduePrincipal": 300, "daysPastDue": 15} for k in fu]})
    policy = {"enabled": True, "maxFollowUps": 3, "rules": [
        {"id": "sin-respuesta", "trigger": "no_response", "afterHours": 0, "action": "message",
         "message": "Seguimos pendientes {{primer_nombre}}: {{total_vencido}}"},
        {"id": "recordatorio-promesa", "trigger": "promise_due", "beforeHours": 24 * 30, "action": "message",
         "message": "Recuerda tu pago de {{monto_promesa}} el {{fecha_promesa}}"},
        {"id": "asesor", "trigger": "outcome:needs_human", "action": "handoff"}]}
    _, fcamp = api.call("POST", f"{t}/campaigns", {
        "name": f"E2E seguimiento {run}", "campaignType": 1, "channel": "api", "channelAction": 0, "executionMode": 1,
        "triggerType": 1, "messageDraft": "Hola {{primer_nombre}}, debes {{total_vencido}}",
        "audienceFilterJson": json.dumps({"source": "collections", "customerIds": list(fu.values())}),
        "followupPolicyJson": json.dumps(policy), "timezone": "America/Mexico_City"})
    api.call("POST", f"{t}/campaigns/{fcamp['id']}/publish")
    _, frun = api.call("POST", f"{t}/campaigns/{fcamp['id']}/run-now")
    check(json.loads(frun["countersJson"]).get("Sent") == 3, "follow-up campaign sent to 3 customers", frun["countersJson"])
    api.call("POST", f"{t}/channels/{channel_id}/messages", {"from": fphone["F"], "content": "te pago 300 el viernes"})
    api.call("POST", f"{t}/channels/{channel_id}/messages", {"from": fphone["G"], "content": "quiero hablar con un asesor"})

    RECEIVED.clear()
    _, summary = api.call("POST", f"{t}/campaigns/{fcamp['id']}/follow-ups/run")
    check((summary.get("sent"), summary.get("handedOff")) == (2, 1), "follow-ups: 2 messages + 1 hand-off", str(summary))
    time.sleep(1)
    texts = [str(m.get("content") or m) for m in RECEIVED]
    check(any("Seguimos pendientes Eena" in x and "300.00" in x for x in texts), "no-response follow-up quotes today's debt", str(texts)[:200])
    check(any("Recuerda tu pago de 300.00" in x for x in texts), "promise reminder quotes the promised amount", str(texts)[:200])
    _, again = api.call("POST", f"{t}/campaigns/{fcamp['id']}/follow-ups/run")
    check(again.get("sent") == 0 and again.get("handedOff") == 0, "each follow-up runs once per contact", str(again))

    _, fres = api.call("GET", f"{t}/campaigns/{fcamp['id']}/results")
    g = next(x for x in fres["contactsList"] if x["recipient"] == fphone["G"])
    check(fres["needsAttention"] == 1 and g["needsAttention"] and fres["followUpsSent"] == 2, "results show 1 to attend and 2 follow-ups",
          str({k: fres[k] for k in ("needsAttention", "followUpsSent")}))
    status, _ = api.call("POST", f"{t}/campaign-contacts/{g['id']}/attention/resolve")
    _, fres = api.call("GET", f"{t}/campaigns/{fcamp['id']}/results")
    check(status == 200 and fres["needsAttention"] == 0, "operator marks the contact as attended")

    _, pay = api.call("POST", f"{c}/payments", {"payments": [{"loanId": f"{fu['F']}-L1", "amount": 300, "reference": f"E2E-PAY-{run}"}]})
    _, dup = api.call("POST", f"{c}/payments", {"payments": [{"loanId": f"{fu['F']}-L1", "amount": 300, "reference": f"E2E-PAY-{run}"}]})
    check(pay.get("applied") == 1 and pay.get("promisesResolved") == 1 and dup.get("duplicates") == 1,
          "payment applied, promise closed the same day, repeated reference ignored", str({"pay": pay.get("applied"), "resolved": pay.get("promisesResolved"), "dup": dup.get("duplicates")}))
    _, floans = api.call("GET", f"{c}/loans?query={fu['F']}")
    floan = floans["items"][0]
    check(floan["overdueBalance"] == 0 and floan["stage"] == "Current", "loan is current after the payment", str({k: floan[k] for k in ("overdueBalance", "stage")}))

    _, capcamp = api.call("POST", f"{t}/campaigns", {
        "name": f"E2E limite {run}", "campaignType": 1, "channel": "api", "channelAction": 0, "executionMode": 1, "triggerType": 1,
        "messageDraft": "Hola {{primer_nombre}}", "throttleJson": json.dumps({"maxPerRecipientPerDay": 2}),
        "audienceFilterJson": json.dumps({"source": "collections", "customerIds": [fu["E"]]})})
    _, caprun = api.call("POST", f"{t}/campaigns/{capcamp['id']}/run-now")
    check(json.loads(caprun["countersJson"]).get("Skipped") == 1, "daily cap: a customer messaged twice today is skipped", caprun["countersJson"])

    api.call("POST", f"{t}/channels/{channel_id}/deactivate")
    api.call("DELETE", f"{t}/channels/{channel_id}")
    print(f"\n{'ALL PASSED' if not FAILURES else f'{len(FAILURES)} FAILED'} (run {run})")
    return 1 if FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
