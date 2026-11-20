<!-- ─────────────────────────────────────────────────────────────────────────────
     XrayProxyWorker · README (English) · @mmd-yousefiasl
     Persian version: README.fa.md
     ───────────────────────────────────────────────────────────────────────────── -->

<img width="100%" src="https://capsule-render.vercel.app/api?type=waving&color=0:512BD4,1:8E7CF3&height=200&section=header&text=XRAY%20PROXY%20WORKER&fontSize=40&fontColor=ffffff&desc=Find%20Working%20V2Ray%20Nodes%20%C2%B7%20Batch%20Benchmarking%20%C2%B7%20.NET%2010&descSize=16&descAlignY=72&animation=fadeIn" alt="XrayProxyWorker — batch benchmarking for public V2Ray/Xray configs" />

<div align="center">

<b>English</b> | <a href="README.fa.md">فارسی</a>

<a href="https://github.com/mmd-yousefiasl/XrayProxyWorker">
  <img src="https://readme-typing-svg.demolab.com?font=Fira+Code&weight=600&size=18&pause=1200&color=A855F7&center=true&vCenter=true&width=640&height=44&lines=Thousands+of+public+proxy+configs+go+in+%E2%80%94+the+working+ones+come+out;vless+%C2%B7+vmess+%C2%B7+trojan+%C2%B7+shadowsocks+%E2%80%94+parsed%2C+deduped%2C+benchmarked;A+real+Xray-core+engine+per+batch+%C2%B7+one+SOCKS+port+per+config;Reachability+sweep+at+100+concurrency+%C2%B7+speed+test+on+survivors;Only+configs+whose+data+actually+flows+survive" alt="Thousands of public proxy configs go in — the working ones come out" />
</a>

<br>

<img src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 10" />&nbsp;
<img src="https://img.shields.io/badge/C%23-512BD4?style=flat-square" alt="C#" />&nbsp;
<img src="https://img.shields.io/badge/Xray--Core-bundled-24292F?style=flat-square" alt="Xray-core bundled" />&nbsp;
<img src="https://img.shields.io/badge/Protocols-vless%C2%B7vmess%C2%B7trojan%C2%B7ss-8E7CF3?style=flat-square" alt="vless, vmess, trojan, shadowsocks" />&nbsp;
<img src="https://img.shields.io/badge/Windows%20%7C%20Linux-engines%20included-2EA043?style=flat-square" alt="Windows and Linux engines included" />

</div>

<img width="100%" src="https://capsule-render.vercel.app/api?type=rect&color=0:512BD4,1:8E7CF3&height=2&width=1000" alt="" />

<div align="center">
  <img src="https://img.shields.io/badge/OVERVIEW-2F1C6B?style=for-the-badge" alt="Overview" />
</div>

Public proxy subscription lists are full of dead links. XrayProxyWorker is a benchmark rig that finds out which ones are actually alive — and how fast they are. It pulls V2Ray/Xray subscription sources (raw or base64-encoded), parses **vless / vmess / trojan / shadowsocks** links, deduplicates them, then exercises every candidate inside a **real Xray-core engine**: one local SOCKS inbound per config, a full HTTPS request pushed through the tunnel, and a measured verdict. Survivors can go through a 2 MB download speed test and are ranked by throughput.

Everything runs locally with the engines and geo data shipped inside `Core/` — no Xray installation, no external services. Configs that pass are tagged `#telegram@connect_iran`, ready for publishing to the [@connect_iran](https://t.me/connect_iran) channel through the built-in Telegram publisher.

<div align="center">
  <img src="https://img.shields.io/badge/HOW_IT_WORKS-2F1C6B?style=for-the-badge" alt="How It Works" />
</div>

```
appsettings.json ── subscription sources (any URL, raw or base64)
        │
        ▼
V2RayConfigService
        fetch · auto-detect base64 blobs · parse
        vless / vmess / trojan / shadowsocks
        structural validation · dedupe · cap at MaxConfigs
        │
        ▼
XrayConfigGenerator ── one master config · SOCKS inbound per proxy
        │                port pool from PortPoolStart (20000)
        │                tcp / ws / grpc / httpupgrade / kcp / splithttp
        ▼
XrayProcessManager
        validate with the real engine · launch bundled Xray
        wait until the first inbound actually ACCEPTS connections
        │
        ▼
Phase 1 ── REACHABILITY · concurrency 100
        HTTPS GET through each inbound to speed.cloudflare.com
        a 64 KB body must actually arrive · TTFB measured
        live progress bar, refreshed 4×/second
        │
        ▼
Phase 2 ── SPEED TEST · concurrency 8 · survivors only
        best-latency first · 2 MB download per config
        Mbps measured · downloads never share bandwidth
        │
        ▼
WORKING CONFIGS ── sorted · tagged #telegram@connect_iran
        printed to the console · publishable to @connect_iran
```

**Two benchmark modes** — both shipped, both driven by the same services:

| Mode | Method | Xray engine | Reachability | Speed test | Result order |
|------|--------|-------------|--------------|------------|--------------|
| **Ping** (console default) | `FindReachableProxiesAsync` | one per 500-config batch | 64 KB Cloudflare probe | — | by latency |
| **Fast** | `FindAllFastProxiesAsync` | one master config for all | same probe | 2 MB download → Mbps | by Mbps, then latency |

In ping mode, each batch spins up its own short-lived engine: when the process dies, its listening sockets are released and the same port pool is reused for the next batch — system load stays flat no matter how many configs come in.

<div align="center">
  <img src="https://img.shields.io/badge/FEATURES-2F1C6B?style=for-the-badge" alt="Features" />
</div>

- **Four protocols, every mainstream transport** — vless (incl. XTLS Reality), vmess, trojan and shadowsocks, over tcp, websocket, gRPC, httpupgrade, kcp and splithttp, with tls and reality security handled
- **A verdict that means data flowed** — reachability is not a TCP ping or a header check: a full HTTPS response body must arrive through the tunnel before a config is called alive
- **Dual benchmark modes** — a quick ping sweep or a full speed-ranked run, driven by the same engine plumbing
- **Live progress bars** — `[####......] Reachability 3200/5000 | Ok: 812 | Failed: 2388 | 01:24`, redrawn 4×/second with ok/failed counts and elapsed time
- **Smart deduplication** — the same node is never tested twice: links are normalized across sources and remarks into a single proxy key before testing starts
- **Structural validation first** — GUID user ids for vless/vmess, credential length checks for trojan/ss — malformed links never reach the engine
- **Telegram publishing built in** — verified configs are HTML-encoded into a one-click code block and posted to @connect_iran through a Cloudflare Worker relay, with 3 retries and 10-second timeouts
- **Everything tunable without recompiling** — sources, caps, batch size, port pool, concurrency, timeouts and payload size all live in `appsettings.json`
- **Interactive console, CI-friendly** — press `1` to start, `Esc` to quit; with a closed stdin (piped output, automation) the runner does one pass and exits instead of spinning on prompts

<div align="center">
  <img src="https://img.shields.io/badge/ARCHITECTURE-2F1C6B?style=for-the-badge" alt="Architecture" />
</div>

A single .NET Worker-SDK project on the generic host, with every capability behind an interface:

```
XrayProxyWorker.slnx
└── XrayProxyWorker/
    ├── Core/       # bundled Xray engines (Windows + Linux) · geoip.dat · geosite.dat
    ├── Dtos/       # V2RayConfig, GeneratedProxy, results, BenchmarkOptions
    ├── Tools/      # subscription parsing & normalization extensions
    ├── Services/   # interface-first implementations
    └── Workers/    # ConsoleRunner — the interactive test loop
```

| Component | Responsibility |
|-----------|----------------|
| **V2RayConfigService** | fetches sources, auto-decodes base64 bodies, parses links, dedupes by normalized key |
| **ProxyConfigValidator** | structural checks — protocol, address, port range, credential shape — before anything reaches the engine |
| **XrayConfigGenerator** | builds the master config: one SOCKS inbound per proxy on the port pool, outbounds for every protocol and transport |
| **XrayProcessManager** | validates configs with `xray -test`, launches engines, polls for true readiness, kills orphaned processes, tears down batches |
| **SocketsLatencyTester** | raw-socket reachability probes and download speed tests through each inbound |
| **XrayBenchmarkService** | orchestrates the two phases with concurrency caps and live progress bars |
| **TelegramPublisher** | formats verified configs as an HTML-safe code block and posts them to @connect_iran via a Cloudflare Worker relay |
| **ConsoleRunner** | the interactive loop — press 1, sweep, print working configs |

Services are registered in DI with typed `HttpClient` factories (`V2RayConfigService`, `TelegramPublisher`) and resolved through `IOptions<BenchmarkOptions>`.

<div align="center">
  <img src="https://img.shields.io/badge/TECH_STACK-2F1C6B?style=for-the-badge" alt="Tech Stack" />
</div>

| Category | Technologies |
|----------|--------------|
| **Runtime / Language** | .NET 10 (Worker SDK), C# |
| **Proxy engine** | Xray-core — Windows and Linux binaries bundled with geoip/geosite data |
| **Networking** | raw sockets, typed `HttpClient`, SOCKS proxying, Cloudflare speed endpoint as the measurement target |
| **Concurrency** | `SemaphoreSlim`-capped async worker pools (100 reachability / 8 speed test) |
| **Serialization** | Protobuf + gRPC tooling |
| **Host** | Generic Host, dependency injection, `IOptions` pattern |
| **Publishing** | Telegram channel @connect_iran via a Cloudflare Worker relay |

<div align="center">
  <img src="https://img.shields.io/badge/KEY_ENGINEERING_DECISIONS-2F1C6B?style=for-the-badge" alt="Key Engineering Decisions" />
</div>

**A working verdict means data flowed.** Some transports answer headers fine and then hang on the body — the reachability probe knows that. It sends a real HTTPS GET through the tunnel to Cloudflare's speed endpoint and refuses to accept a config until a 64 KB body actually arrives, measuring time-to-first-byte on the way.

**Batches keep load flat.** Five hundred configs per engine; when the process dies, its listening sockets are released and the port pool is reused. Memory and CPU stay flat whether the run has 500 configs or 5,000.

**Readiness by probing, not sleeping.** `WaitUntilReadyAsync` polls the first SOCKS inbound until it genuinely accepts connections (20-second budget), instead of guessing with a fixed `sleep`.

**Orphan hygiene.** Leftover Xray processes are swept at startup, every batch kills the full process tree and waits for exit before the next batch rebinds the same ports, and temp configs are deleted in `finally` blocks — a crashed run leaves nothing behind.

**Speed tests never share bandwidth.** Reachability runs hot at concurrency 100 on purpose; speed tests deliberately drop to 8 so parallel downloads can't distort each other's Mbps, and survivors are tested best-latency first.

**Base64 with a sanity gate.** Subscription bodies are URL-safe normalized (padding, `-`/`_` → `+`/`/`), decoded once, and rejected unless the result contains `://` — so random text can never be mistaken for configs.

**Graceful everywhere.** `Ctrl+C` cancels through a linked token source, engines are killed and cleaned in `finally`, and a closed stdin flips the runner into one-shot mode — piped and automated runs complete instead of spinning on console prompts.

<div align="center">
  <img src="https://img.shields.io/badge/GETTING_STARTED-2F1C6B?style=for-the-badge" alt="Getting Started" />
</div>

**Prerequisites**

- .NET 10 SDK
- That's it — the Xray engines (Windows + Linux) and geo data ship inside `Core/`

**Run it**

```
git clone https://github.com/mmd-yousefiasl/XrayProxyWorker.git
cd XrayProxyWorker
dotnet run --project XrayProxyWorker
```

Then: `Press 1 to start testing (Esc to quit)...` — the sweep runs batch by batch with live progress bars and prints the working configs when done.

**Configure**

Everything lives in the `Benchmark` section of `appsettings.json`:

| Setting | Default | What it does |
|---------|---------|--------------|
| `Sources` | 7 public lists | subscription URLs to fetch (raw or base64) |
| `MaxConfigs` | 5000 | cap on parsed configs per run |
| `BatchSize` | 500 | configs per Xray engine (ping mode) |
| `PortPoolStart` | 20000 | first SOCKS inbound port |
| `ReachabilityConcurrency` | 100 | parallel reachability probes |
| `SpeedTestConcurrency` | 8 | parallel speed tests |
| `ConnectTimeoutSeconds` | 5 | per-probe timeout |
| `SpeedTestTimeoutSeconds` | 12 | speed-test timeout |
| `DownloadBytes` | 2097152 | payload per speed test (2 MB) |
| `XrayStartupTimeoutSeconds` | 20 | engine readiness budget |

<div align="center">
  <img src="https://img.shields.io/badge/CONTACT-2F1C6B?style=for-the-badge" alt="Contact" />
</div>

<p align="center">
  <b>Mohammad Yousefi</b> — Backend Developer (.NET / ASP.NET Core)
</p>

<div align="center">

<a href="mailto:mmdyousefi95@gmail.com">
  <img src="https://img.shields.io/badge/Email-mmdyousefi95%40gmail.com-512BD4?style=for-the-badge&logo=gmail&logoColor=white" alt="Email" />
</a>
<a href="https://github.com/mmd-yousefiasl">
  <img src="https://img.shields.io/badge/GitHub-%40mmd--yousefiasl-512BD4?style=for-the-badge&logo=github&logoColor=white" alt="GitHub" />
</a>
<a href="https://t.me/mmd_u_a">
  <img src="https://img.shields.io/badge/Telegram-%40mmd__u__a-512BD4?style=for-the-badge&logo=telegram&logoColor=white" alt="Telegram" />
</a>

</div>

<br>

<img width="100%" src="https://capsule-render.vercel.app/api?type=waving&color=0:512BD4,1:8E7CF3&height=140&section=footer" alt="" />
