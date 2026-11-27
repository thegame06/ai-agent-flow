import express from 'express';
import axios from 'axios';
import QRCode from 'qrcode';
import pkg from 'whatsapp-web.js';

const { Client, LocalAuth } = pkg;
const app = express();
app.use(express.json({ limit: '1mb' }));

const PORT = process.env.PORT || 3401;
const AGENTFLOW_BASE_URL = process.env.AGENTFLOW_BASE_URL || 'http://localhost:5000';
const TENANT_ID = process.env.TENANT_ID || 'default';
const API_KEY = process.env.BRIDGE_API_KEY || '';
// Comma-separated phone numbers (digits, any format). When set, only messages from these numbers
// are forwarded to AgentFlow and only these numbers can be messaged. Use it whenever a personal
// WhatsApp is linked for testing, so the assistant never talks to the owner's other contacts.
const ALLOWED_NUMBERS = (process.env.ALLOWED_NUMBERS || '')
  .split(',')
  .map((n) => n.replace(/\D/g, ''))
  .filter(Boolean);

const lastDigits = (value) => String(value || '').replace(/\D/g, '').slice(-10);
const isAllowed = (number) =>
  ALLOWED_NUMBERS.length === 0 || ALLOWED_NUMBERS.some((n) => lastDigits(n) === lastDigits(number));

const sessions = new Map();
const sendRate = new Map(); // key: channelId|to -> timestamps(ms)

process.on('uncaughtException', (err) => {
  console.error('[bridge] uncaughtException:', err?.stack || err?.message || err);
});

process.on('unhandledRejection', (reason) => {
  console.error('[bridge] unhandledRejection:', reason?.stack || reason?.message || reason);
});

async function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

async function postToAgentFlowWithRetry(url, payload, attempts = 3) {
  let lastErr;
  for (let i = 1; i <= attempts; i++) {
    try {
      return await axios.post(url, payload, { timeout: 15000 });
    } catch (err) {
      lastErr = err;
      if (i < attempts) await sleep(400 * i);
    }
  }
  throw lastErr;
}

function allowSend(channelId, to, maxPerMinute = 20) {
  const key = `${channelId}|${to}`;
  const now = Date.now();
  const windowStart = now - 60_000;
  const arr = (sendRate.get(key) || []).filter((t) => t >= windowStart);
  if (arr.length >= maxPerMinute) return false;
  arr.push(now);
  sendRate.set(key, arr);
  return true;
}

function auth(req, res, next) {
  if (!API_KEY) return next();
  const token = req.headers.authorization?.replace('Bearer ', '');
  if (token !== API_KEY) return res.status(401).json({ error: 'unauthorized' });
  next();
}

// destroy() can hang on Windows and leave Chrome holding the session folder, which then makes
// the next start time out. Give it a few seconds, then kill the browser process outright.
async function shutdownClient(client) {
  if (!client) return;
  const browser = client.pupBrowser;
  try {
    await Promise.race([client.destroy(), sleep(5000)]);
  } catch {}
  try {
    browser?.process()?.kill('SIGKILL');
  } catch {}
}

function ensureState(channelId) {
  if (!sessions.has(channelId)) {
    sessions.set(channelId, {
      channelId,
      connected: false,
      status: 'idle',
      qrCode: null,
      client: null,
      lastSeenAt: null,
      lastForwardAt: null,
      lastError: null,
      startedAt: null,
    });
  }
  return sessions.get(channelId);
}

function attachClientHandlers(channelId, state, client) {
  client.on('qr', async (qr) => {
    console.log(`[bridge] ${channelId} qr generated`);
    state.qrCode = await QRCode.toDataURL(qr);
    state.connected = false;
    state.status = 'qr_pending';
    state.lastError = null;
  });

  client.on('authenticated', () => console.log(`[bridge] ${channelId} authenticated`));
  client.on('loading_screen', (pct) => console.log(`[bridge] ${channelId} loading ${pct}%`));
  client.on('ready', () => {
    console.log(`[bridge] ${channelId} ready (linked)`);
    state.connected = true;
    state.qrCode = null;
    state.status = 'connected';
    state.lastSeenAt = new Date().toISOString();
    state.lastError = null;
  });

  client.on('auth_failure', (msg) => {
    console.log(`[bridge] ${channelId} auth_failure: ${msg}`);
    state.connected = false;
    state.status = 'auth_failed';
    state.lastError = msg || 'auth_failure';
  });

  client.on('disconnected', (reason) => {
    console.log(`[bridge] ${channelId} disconnected: ${reason}`);
    state.connected = false;
    state.status = 'disconnected';
    state.lastError = reason || 'disconnected';

    // Auto-recover session for demo robustness
    setTimeout(async () => {
      try {
        if (state.status === 'disconnected') {
          await reconnectSession(channelId);
        }
      } catch (err) {
        state.status = 'error';
        state.lastError = err.message;
      }
    }, 1500);
  });

  client.on('message', (msg) => forwardIncoming(channelId, state, client, msg));
}

// Resolves the sender's real phone number. Newer WhatsApp accounts arrive as "<id>@lid" (a linked
// id, not a phone); AgentFlow needs the phone to match the reply to the customer and the campaign.
async function resolveSender(client, msg) {
  if (/@lid$/.test(msg.from)) {
    try {
      const [match] = await client.getContactLidAndPhone([msg.from]);
      if (match?.pn) return match.pn.includes('@') ? match.pn : `${match.pn}@c.us`;
    } catch (err) {
      console.log(`[bridge] could not resolve ${msg.from}: ${err.message}`);
    }
  }
  try {
    const contact = await msg.getContact();
    if (contact?.number) return `${contact.number}@c.us`;
  } catch {}
  return msg.from;
}

async function forwardIncoming(channelId, state, client, msg) {
  state.lastSeenAt = new Date().toISOString();
  // Never forward groups, status updates, broadcasts or channels: only 1:1 chats.
  if (msg.fromMe || !msg.from || /@(g\.us|broadcast|newsletter)$/.test(msg.from)) return false;
  try {
    const from = await resolveSender(client, msg);
    if (!isAllowed(from)) {
      console.log(`[bridge] ${channelId} dropped message from ${from} (not in ALLOWED_NUMBERS)`);
      return false;
    }

    const payload = {
      id: msg.id._serialized,
      from,
      content: msg.body,
      pushName: msg._data?.notifyName || null,
      timestamp: msg.timestamp,
    };

    await postToAgentFlowWithRetry(
      `${AGENTFLOW_BASE_URL}/api/v1/tenants/${TENANT_ID}/webhooks/whatsapp/qr`,
      payload,
      3
    );

    console.log(`[bridge] ${channelId} forwarded message from ${from}`);
    state.lastForwardAt = new Date().toISOString();
    state.lastError = null;
    return true;
  } catch (err) {
    state.lastError = err.message;
    // keep bridge alive; only log failure
    console.error('Failed forwarding message to AgentFlow:', err.message);
    return false;
  }
}

async function startSession(channelId) {
  const state = ensureState(channelId);
  if (state.client) return state;

  state.status = 'starting';
  state.startedAt = new Date().toISOString();
  state.lastError = null;

  const client = new Client({
    authStrategy: new LocalAuth({ clientId: channelId }),
    puppeteer: { headless: true, args: ['--no-sandbox', '--disable-setuid-sandbox'] },
  });

  attachClientHandlers(channelId, state, client);
  state.client = client;
  await client.initialize();
  return state;
}

async function reconnectSession(channelId) {
  const state = ensureState(channelId);
  state.status = 'reconnecting';
  state.lastError = null;

  if (state.client) {
    await shutdownClient(state.client);
    state.client = null;
    state.connected = false;
    state.qrCode = null;
  }

  await startSession(channelId);
  return state;
}

app.use(auth);

app.post('/session/start', async (req, res) => {
  const { channelId } = req.body || {};
  if (!channelId) return res.status(400).json({ error: 'channelId required' });

  try {
    const state = await startSession(channelId);
    return res.json({ ok: true, message: 'session started', status: state.status });
  } catch (err) {
    const state = ensureState(channelId);
    state.status = 'error';
    state.lastError = err.message;
    return res.status(500).json({ ok: false, error: err.message });
  }
});

app.post('/session/reconnect', async (req, res) => {
  const { channelId } = req.body || {};
  if (!channelId) return res.status(400).json({ error: 'channelId required' });

  try {
    const state = await reconnectSession(channelId);
    return res.json({ ok: true, message: 'session reconnect triggered', status: state.status });
  } catch (err) {
    const state = ensureState(channelId);
    state.status = 'error';
    state.lastError = err.message;
    return res.status(500).json({ ok: false, error: err.message });
  }
});

app.get('/session/qr', (req, res) => {
  const channelId = req.query.channelId;
  const state = sessions.get(channelId);
  if (!state) return res.status(404).json({ error: 'session not found' });
  res.json({ connected: state.connected, status: state.status, qrCode: state.qrCode });
});

app.get('/session/status', (req, res) => {
  const channelId = req.query.channelId;
  const state = sessions.get(channelId);
  if (!state) return res.status(404).json({ error: 'session not found' });
  res.json({
    connected: state.connected,
    status: state.status,
    hasQr: !!state.qrCode,
    lastSeenAt: state.lastSeenAt,
    lastForwardAt: state.lastForwardAt,
    lastError: state.lastError,
    startedAt: state.startedAt,
  });
});

app.get('/health', (_req, res) => {
  const stats = Array.from(sessions.entries()).map(([channelId, s]) => ({
    channelId,
    connected: s.connected,
    status: s.status,
    hasQr: !!s.qrCode,
    lastSeenAt: s.lastSeenAt,
    lastForwardAt: s.lastForwardAt,
    lastError: s.lastError,
  }));

  res.json({
    ok: true,
    sessions: stats.length,
    stats,
  });
});

app.post('/messages/send', async (req, res) => {
  const { channelId, to, content } = req.body || {};
  const state = sessions.get(channelId);
  if (!state || !state.client) return res.status(404).json({ error: 'session not found' });
  if (!state.connected) return res.status(400).json({ error: 'session not connected' });
  if (!allowSend(channelId, to, 20)) return res.status(429).json({ error: 'rate limit exceeded' });

  if (!isAllowed(to)) return res.status(403).json({ error: 'number not in ALLOWED_NUMBERS' });

  try {
    let chatId = to;
    if (!to.includes('@')) {
      // "+52 55 1234 5678" -> the WhatsApp id WhatsApp actually uses for that number (e.g. 521...).
      const digits = to.replace(/\D/g, '');
      const numberId = await state.client.getNumberId(digits);
      if (!numberId) return res.status(404).json({ error: `number ${to} is not on WhatsApp` });
      chatId = numberId._serialized;
    }
    const message = await state.client.sendMessage(chatId, content);
    state.lastSeenAt = new Date().toISOString();
    state.lastError = null;
    res.json({ ok: true, messageId: message.id._serialized });
  } catch (err) {
    state.lastError = err.message;
    state.status = 'error';
    res.status(500).json({ error: err.message });
  }
});

// Re-forwards recent incoming 1:1 messages (e.g. ones dropped while the bridge was misconfigured).
// Safe to repeat: AgentFlow ignores a message id it already received.
app.post('/messages/replay', async (req, res) => {
  const { channelId, minutes = 60 } = req.body || {};
  const state = sessions.get(channelId);
  if (!state?.client || !state.connected) return res.status(400).json({ error: 'session not connected' });
  const since = Date.now() / 1000 - Number(minutes) * 60;
  let forwarded = 0;
  const chats = await state.client.getChats();
  for (const chat of chats.filter((c) => !c.isGroup && c.timestamp >= since)) {
    const messages = await chat.fetchMessages({ limit: 20 });
    for (const msg of messages.filter((m) => !m.fromMe && m.timestamp >= since)) {
      if (await forwardIncoming(channelId, state, state.client, msg)) forwarded++;
    }
  }
  res.json({ ok: true, forwarded });
});

app.post('/session/disconnect', async (req, res) => {
  const { channelId } = req.body || {};
  const state = sessions.get(channelId);
  if (!state) return res.json({ ok: true, message: 'already disconnected' });

  await shutdownClient(state.client);

  sessions.delete(channelId);
  res.json({ ok: true });
});

app.listen(PORT, () => {
  console.log(`AgentFlow WhatsApp QR Bridge running on port ${PORT}`);
});
