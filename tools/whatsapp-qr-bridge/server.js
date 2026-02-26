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

const sessions = new Map();
const sendRate = new Map(); // key: channelId|to -> timestamps(ms)

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

app.use(auth);

app.post('/session/start', async (req, res) => {
  const { channelId } = req.body || {};
  if (!channelId) return res.status(400).json({ error: 'channelId required' });

  if (sessions.has(channelId)) {
    return res.json({ ok: true, message: 'already started' });
  }

  const state = {
    connected: false,
    qrCode: null,
    client: null,
    lastSeenAt: null,
    lastForwardAt: null,
    lastError: null
  };
  const client = new Client({
    authStrategy: new LocalAuth({ clientId: channelId }),
    puppeteer: { headless: true, args: ['--no-sandbox', '--disable-setuid-sandbox'] }
  });

  client.on('qr', async (qr) => {
    state.qrCode = await QRCode.toDataURL(qr);
    state.connected = false;
  });

  client.on('ready', () => {
    state.connected = true;
    state.qrCode = null;
    state.lastSeenAt = new Date().toISOString();
    state.lastError = null;
  });

  client.on('message', async (msg) => {
    state.lastSeenAt = new Date().toISOString();
    try {
      const payload = {
        id: msg.id._serialized,
        from: msg.from,
        content: msg.body,
        pushName: msg._data?.notifyName || null,
        timestamp: msg.timestamp
      };

      await postToAgentFlowWithRetry(
        `${AGENTFLOW_BASE_URL}/api/v1/tenants/${TENANT_ID}/webhooks/whatsapp/qr`,
        payload,
        3
      );

      state.lastForwardAt = new Date().toISOString();
      state.lastError = null;
    } catch (err) {
      state.lastError = err.message;
      console.error('Failed forwarding message to AgentFlow:', err.message);
    }
  });

  state.client = client;
  sessions.set(channelId, state);
  await client.initialize();

  res.json({ ok: true, message: 'session started' });
});

app.get('/session/qr', (req, res) => {
  const channelId = req.query.channelId;
  const state = sessions.get(channelId);
  if (!state) return res.status(404).json({ error: 'session not found' });
  res.json({ connected: state.connected, qrCode: state.qrCode });
});

app.get('/session/status', (req, res) => {
  const channelId = req.query.channelId;
  const state = sessions.get(channelId);
  if (!state) return res.status(404).json({ error: 'session not found' });
  res.json({
    connected: state.connected,
    lastSeenAt: state.lastSeenAt,
    lastForwardAt: state.lastForwardAt,
    lastError: state.lastError
  });
});

app.get('/health', (_req, res) => {
  const stats = Array.from(sessions.entries()).map(([channelId, s]) => ({
    channelId,
    connected: s.connected,
    hasQr: !!s.qrCode,
    lastSeenAt: s.lastSeenAt,
    lastForwardAt: s.lastForwardAt,
    lastError: s.lastError
  }));

  res.json({
    ok: true,
    sessions: stats.length,
    stats
  });
});

app.post('/messages/send', async (req, res) => {
  const { channelId, to, content } = req.body || {};
  const state = sessions.get(channelId);
  if (!state || !state.client) return res.status(404).json({ error: 'session not found' });
  if (!state.connected) return res.status(400).json({ error: 'session not connected' });
  if (!allowSend(channelId, to, 20)) return res.status(429).json({ error: 'rate limit exceeded' });

  try {
    const chatId = to.includes('@') ? to : `${to}@c.us`;
    const message = await state.client.sendMessage(chatId, content);
    state.lastSeenAt = new Date().toISOString();
    state.lastError = null;
    res.json({ ok: true, messageId: message.id._serialized });
  } catch (err) {
    state.lastError = err.message;
    res.status(500).json({ error: err.message });
  }
});

app.post('/session/disconnect', async (req, res) => {
  const { channelId } = req.body || {};
  const state = sessions.get(channelId);
  if (!state) return res.json({ ok: true, message: 'already disconnected' });

  try {
    await state.client.destroy();
  } catch {}

  sessions.delete(channelId);
  res.json({ ok: true });
});

app.listen(PORT, () => {
  console.log(`AgentFlow WhatsApp QR Bridge running on port ${PORT}`);
});
