[Русский](protocol.md) | **English**

# Handsfreely protocol, version 1

The channel between the PC program and the phone app. Transport: Bluetooth RFCOMM. The PC listens on the service UUID `e7530220-2b8a-460d-925c-0267c9b9b467`, the phone on `1c3afcb6-f84e-400c-9554-6c9dd39596a3`; either side may connect (the phone while there is no link, the PC when its program starts). The handshake does not depend on that: the phone always starts it. If one PC-phone pair ends up with two connections, both sides keep the most recently established one. Call audio does not travel over this channel: the standard HFP profile, driven by Windows, carries it. SMS bypass the channel too: the PC reads and sends them itself over the standard Bluetooth MAP profile, so the phone app needs no SMS permissions (Google Play Protect blocks sideloaded apps that ask for them).

## Cryptography

- Curve P-256 (secp256r1), ECDH, public keys uncompressed: `0x04 || X || Y`, 65 bytes.
- HKDF-SHA256, AES-256-GCM with a 16-byte tag.
- The PC and the phone each own a long-term key pair (`static`). For every connection both sides create one-time pairs (`eph`).

## Pairing QR code

The PC shows the string:

```
handsfreely:1?a=<MAC>&k=<key>&t=<token>&n=<name>
```

| Field | Meaning |
|---|---|
| `a` | the PC's Bluetooth MAC address, 12 hex digits without separators |
| `k` | the PC's long-term public key, base64url without `=` |
| `t` | one-time token, 16 random bytes, base64url without `=`; lives 5 minutes, burned after a successful pairing |
| `n` | the PC's name, URL-encoded |

## Frames

Every message in both directions: a big-endian `u32` length, then the body. A length above 8 MiB drops the connection.

## Handshake

```
M1 phone -> PC: "HF" 0x01 | mode (1) | phoneStatic (65) | phoneEph (65)
M2 PC -> phone: "HF" 0x01 | status (1) | pcEph (65, only when status = 0)
```

`mode`: `1` - pairing from the QR code, `2` - reconnecting.
`status`: `0` - go on, `1` - unknown phone (pair again), `2` - the PC has no pairing window open, `3` - unsupported version. With a non-zero status the PC closes the connection.

The PC answers `1` when `mode = 2` and `phoneStatic` is not in its trusted list.

Keys:

```
h    = SHA256("Handsfreely/1" || pcStatic || M1 || M2)
ikm  = DH(phoneEph, pcEph) || DH(phoneEph, pcStatic) || DH(phoneStatic, pcEph) [|| token when mode = 1]
okm  = HKDF-SHA256(ikm, salt = h, info = "Handsfreely/1 keys", 64 bytes)
kUp   = okm[0..32]   phone -> PC
kDown = okm[32..64]  PC -> phone
```

`DH` is the raw ECDH shared secret (the X coordinate, 32 bytes). The derivation proves both sides: only the owner of the PC key can compute `DH(phoneEph, pcStatic)`, only the owner of the phone key can compute `DH(phoneStatic, pcEph)`. The phone knows the PC key from the QR code, so the PC cannot be impersonated. The token in `ikm` proves the phone saw the QR code.

From here on every frame is encrypted: frame body = `AES-256-GCM(key, nonce, plaintext)` with the tag at the end, `nonce = 00 00 00 00 || big-endian u64 counter`. Each direction has its own counter, starting at 0 and growing by 1 per frame. A frame that fails to decrypt drops the connection.

The phone's first encrypted frame is `hello`, the PC answers `welcome`. If the PC cannot decrypt `hello` (wrong token or a foreign key), it closes the connection silently. After `welcome`, with `mode = 1`, the PC adds `phoneStatic` to its trusted list.

## Messages

The frame plaintext is UTF-8 JSON:

```json
{"t": "type", "id": 7, "d": { ... }}
```

- Request: `t` and `id` (an integer, unique per sender), data in `d`.
- Response: `{"t": "result", "id": 7, "d": {...}}` or `{"t": "error", "id": 7, "d": {"code": "...", "message": "..."}}`.
- Event: `t` and `d` without `id`.

Error codes: `unknown_type`, `bad_request`, `permission` (the phone lacks a permission), `unsupported`, `failed`.

### Application handshake

| Type | Sender | `d` |
|---|---|---|
| `hello` | phone | `name`, `model`, `appVersion`, `sdk` |
| `welcome` | PC | `name`, `appVersion` |

### Requests PC -> phone

| Type | Request `d` | Response `d` |
|---|---|---|
| `ping` | - | - |
| `device.info` | - | `name`, `model`, `battery`, `charging`, `signal` (0..4), `operator`, `callControl` (`full` or `basic`), `permissions` (array of granted: `contacts`, `calllog`, `phone`) |
| `contacts.list` | - | `contacts`: `[{id, name, starred, hasPhoto, phones: [{number, type}]}]` |
| `contacts.photo` | `id` | `data` (JPEG, base64) or `null` |
| `calllog.list` | `limit`, `before` (ms, optional) | `calls`: `[{id, number, name, type, date, duration}]` |
| `call.dial` | `number` | - |
| `call.answer` | - | - |
| `call.reject` | - | - |
| `call.end` | - | - |
| `call.hold` | `on` | - |
| `call.mute` | `on` | - |
| `call.dtmf` | `digit` (`0-9`, `*`, `#`) | - |
| `call.audio` | `target` (`pc` or `phone`) | - |

Number `type`: `mobile`, `home`, `work`, `other`. Call log `type`: `in`, `out`, `missed`, `rejected`, `blocked`, `voicemail`. All times are Unix milliseconds.

`call.hold`, `call.mute`, `call.dtmf`, `call.audio` work with `callControl = full` (the phone granted the app the call companion role). With `basic` the phone answers `unsupported`, and the PC tries the same through Windows.

### Events phone -> PC

| Type | `d` |
|---|---|
| `call.state` | `calls`: `[{id, state, number, name, direction, since}]` - the full list of current calls; an empty array when there are none |
| `device.status` | `battery`, `charging`, `signal`, `operator` |
| `contacts.changed` | - |
| `calllog.changed` | - |

`state`: `ringing`, `dialing`, `active`, `holding`, `disconnected`. `direction`: `in`, `out`.

The receiver skips an unknown event type and answers an unknown request with `unknown_type`.
