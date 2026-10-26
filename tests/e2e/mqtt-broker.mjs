// A tiny MQTT 3.1.1 broker for the end-to-end tests (item 23): it takes FanPilot's connection (any one, or only with
// the user and password given), acknowledges what needs it and keeps every message and connection it sees. Enough to
// check what FanPilot sends to Home Assistant; nothing is forwarded to anyone. The protocol side is checked against a
// real broker in the C# tests (MqttTests).
import net from 'node:net';

export class TinyBroker {
  constructor({ user, password } = {}) {
    this.user = user;
    this.password = password;
    this.messages = [];
    this.connects = [];
    this.sockets = new Set();
  }

  async start() {
    this.server = net.createServer((socket) => this.serve(socket));
    await new Promise((resolve) => this.server.listen(0, '127.0.0.1', resolve));
    this.port = this.server.address().port;
    return this;
  }

  serve(socket) {
    this.sockets.add(socket);
    socket.on('close', () => this.sockets.delete(socket));
    socket.on('error', () => {});
    let buffer = Buffer.alloc(0);
    socket.on('data', (chunk) => {
      buffer = Buffer.concat([buffer, chunk]);
      for (;;) {
        // Fixed header: the type and its flags, then the remaining length in 7-bit groups.
        let length = 0, multiplier = 1, at = 1, byte;
        do {
          if (at >= buffer.length) return;
          byte = buffer[at++];
          length += (byte & 127) * multiplier;
          multiplier *= 128;
        } while (byte & 128);
        if (buffer.length < at + length) return;
        const type = buffer[0] >> 4, flags = buffer[0] & 15;
        const body = buffer.subarray(at, at + length);
        buffer = buffer.subarray(at + length);
        this.packet(socket, type, flags, body);
      }
    });
  }

  packet(socket, type, flags, body) {
    let at = 0;
    const text = () => {
      const n = body.readUInt16BE(at);
      const s = body.subarray(at + 2, at + 2 + n).toString('utf8');
      at += 2 + n;
      return s;
    };
    if (type === 1) { // CONNECT
      text(); // "MQTT"
      at += 1; // protocol level
      const connectFlags = body[at++];
      at += 2; // keep alive
      const c = { clientId: text() };
      if (connectFlags & 4) Object.assign(c, { willTopic: text(), willMessage: text(), willRetain: !!(connectFlags & 32) });
      if (connectFlags & 128) c.user = text();
      if (connectFlags & 64) c.password = text();
      this.connects.push(c);
      const ok = this.user === undefined || (c.user === this.user && c.password === this.password);
      socket.write(Buffer.from([0x20, 2, 0, ok ? 0 : 4])); // CONNACK: accepted, or bad user name or password
      if (!ok) socket.end();
    } else if (type === 3) { // PUBLISH
      const qos = (flags >> 1) & 3;
      const topic = text();
      if (qos > 0) {
        const id = body.readUInt16BE(at);
        at += 2;
        socket.write(Buffer.from([0x40, 2, id >> 8, id & 255])); // PUBACK
      }
      this.messages.push({ topic, payload: body.subarray(at).toString('utf8'), retain: !!(flags & 1) });
    } else if (type === 12) {
      socket.write(Buffer.from([0xd0, 0])); // PINGRESP
    } else if (type === 14) {
      socket.end(); // DISCONNECT
    }
  }

  // The last message on a topic (a RegExp or the exact name).
  last(topic) {
    return this.messages.filter((m) => (topic instanceof RegExp ? topic.test(m.topic) : m.topic === topic)).at(-1);
  }

  async stop() {
    for (const s of this.sockets) s.destroy();
    await new Promise((resolve) => this.server.close(resolve));
  }
}
