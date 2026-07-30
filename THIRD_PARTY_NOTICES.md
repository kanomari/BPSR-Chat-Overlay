# Third-Party Notices

BPSR Chat Overlay includes or derives from the third-party software listed
below. BPSR Chat Overlay itself is distributed under the MIT License in
`LICENSE`.

The version, license expression, repository, and source commit for NuGet
packages are taken from the package metadata used by this build. Unless stated
otherwise, the packaged third-party binaries are used without modification.

## Source-derived component

### BPSR-ZDPS

- Project: https://github.com/Blue-Protocol-Source/BPSR-ZDPS
- License: MIT
- Copyright: Copyright (c) 2025 Blue-Protocol-Source
- Modifications: Portions of the packet capture, TCP reassembly, and network
  protocol handling code were adapted for BPSR Chat Overlay.

The original copyright notice is retained above. The MIT license terms are
included in `licenses/MIT-Third-Party.txt`.

## NuGet packages

| Component | Version | License | Copyright / authors |
| --- | ---: | --- | --- |
| [Google.Protobuf](https://github.com/protocolbuffers/protobuf/tree/35cd01f9fe9afbeea38cc7b979a3b6bfcde82c03) | 3.35.1 | BSD-3-Clause | Copyright 2008 Google Inc. All rights reserved. |
| [Microsoft.Extensions.ObjectPool](https://github.com/dotnet/dotnet/tree/f7d90799ce4ef09a0bb257852a57248d2a8fb8dd) | 10.0.10 | MIT | © Microsoft Corporation. All rights reserved. |
| [PacketDotNet](https://github.com/dotpcap/packetnet/tree/690707ce56d6e9c266daf6236c4f76ac5035334c) | 1.4.8 | MPL-2.0 | Chris Morgan and PacketDotNet contributors |
| [Serilog](https://github.com/serilog/serilog/tree/497f80fda4f9e8f98b9c13ba34b1f0530f8c4449) | 4.4.0 | Apache-2.0 | Copyright © Serilog Contributors |
| [Serilog.Sinks.File](https://github.com/serilog/serilog-sinks-file/tree/23c732a8658a0df2a5434fe69b0011800b14f0da) | 7.0.0 | Apache-2.0 | Serilog Contributors |
| [SharpPcap](https://github.com/chmorgan/sharppcap/tree/bfedf297e7410ffcf44e05d14f7fbef304f20895) | 6.3.1 | MIT | Tamir Gal, Chris Morgan and others |
| [ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp/tree/2cd0c019693bc786a5fe5c3be94e107b24e7267e) | 0.8.8 | MIT | Copyright Oleg Stepanischev 2026 |
| [System.Memory](https://github.com/dotnet/maintenance-packages/tree/f62ca0009b038cab4725a720f386623a969d73ad) | 4.6.3 | MIT | © Microsoft Corporation. All rights reserved. |
| [System.Runtime.CompilerServices.Unsafe](https://github.com/dotnet/runtime/tree/4822e3c3aa77eb82b2fb33c9321f923cf11ddde6) | 6.0.0 | MIT | © Microsoft Corporation. All rights reserved. |
| [System.Text.Encoding.CodePages](https://github.com/dotnet/runtime/tree/e36e4d1a8f8dfb08d7e3a6041459c9791d732c01) | 9.0.5 | MIT | © Microsoft Corporation. All rights reserved. |

Corresponding license information:

- Google.Protobuf: `licenses/Google.Protobuf-BSD-3-Clause.txt`
- MIT-licensed components: `licenses/MIT-Third-Party.txt`
- Serilog and Serilog.Sinks.File: `licenses/Apache-2.0.txt`
- PacketDotNet: `licenses/PacketDotNet-MPL-2.0.txt`

## PacketDotNet source availability

This distribution contains PacketDotNet 1.4.8 in executable form under the
Mozilla Public License 2.0. The corresponding Source Code Form is available at:

https://github.com/dotpcap/packetnet/tree/690707ce56d6e9c266daf6236c4f76ac5035334c

BPSR Chat Overlay does not modify PacketDotNet. The MPL-2.0 license and source
availability information are provided in
`licenses/PacketDotNet-MPL-2.0.txt`.

## .NET runtime

The self-contained Windows distribution includes Microsoft .NET runtime
components. During publishing, the official license and third-party notice
files from the exact resolved runtime packs are copied to `licenses/dotnet`.
Those runtime-pack notices cover the framework binaries included in the
self-contained distribution.

## Npcap

Npcap is required at runtime but is not included or redistributed with BPSR
Chat Overlay. Users obtain it separately from its official site:

https://npcap.com/
