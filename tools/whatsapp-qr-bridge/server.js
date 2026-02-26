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

  const state = { connected: false, qrCode: null, client: null };
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
  });

  client.on('message', async (msg) => {
    try {
      const payload = {
        id: msg.id._serialized,
        from: msg.from,
        content: msg.body,
        pushName: msg._data?.notifyName || null,
        timestamp: msg.timestamp
      };

      await axios.post(
        `${AGENTFLOW_BASE_URL}/api/v1/tenants/${TENANT_ID}/webhooks/whatsapp/qr`,
        payload,
        { timeout: 15000 }
      );
    } catch (err) {
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
  res.json({ connected: state.connected });
});

app.post('/messages/send', async (req, res) => {
  const { channelId, to, content } = req.body || {};
  const state = sessions.get(channelId);
  if (!state || !state.client) return res.status(404).json({ error: 'session not found' });
  if (!state.connected) return res.status(400).json({ error: 'session not connected' });

  try {
    const chatId = to.includes('@') ? to : `${to}@c.us`;
    const message = await state.client.sendMessage(chatId, content);
    res.json({ ok: true, messageId: message.id._serialized });
  } catch (err) {
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
