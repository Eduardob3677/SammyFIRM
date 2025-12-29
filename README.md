# SamFirm.NET

A streaming downloader, decryptor and extractor of Samsung firmware.

## 🆕 OAuth OTA Analysis

This repository now includes comprehensive analysis of Samsung's OAuth-based OTA (Over-The-Air) firmware update system. See:

- **[OAuth OTA Analysis Documentation](docs/OAUTH_OTA_ANALYSIS.md)** - Complete OAuth 1.0 implementation analysis
- **[JAR Extraction Scripts](scripts/)** - Tools to extract and analyze Samsung framework JARs

### Quick Start - OAuth Analysis

```bash
# Extract and analyze all framework JARs
./scripts/extract_jars.sh

# View OAuth analysis documentation
cat docs/OAUTH_OTA_ANALYSIS.md
```

## Getting started

### Run

1. Download the executable from [Release](https://github.com/jesec/samfirm.net/releases).
1. Run it with `--region` , `--model` and `--i` arguments.
1. Add `--test` to pull firmware info from Samsung's test server (`version.test.xml`) and decrypt the MD5 entries into usable firmware versions for beta builds.
1. Region, Model and IMEI need to be valid for the target device, otherwise FUS will respond with Err 408
1. If you dont have the IMEI of a certain device you want, usually googling <model> "imei swappa" will bring up valid ones

Windows users may choose the smaller but not-self-contained variant if [.NET runtime](https://dotnet.microsoft.com/download/dotnet/5.0/runtime) is present.

### Build

1. Get [Visual Studio 2019](https://visualstudio.microsoft.com/vs/)
1. Open the repository with VS 2019
1. Install dependencies as prompted
1. Build solution

## Example

```
> ./SamFirm -m SM-F916N -r KOO -i <valid imei>

  Model: SM-F916N
  Region: KOO

  Latest version:
    PDA: F916NTBU1ATJC
    CSC: F916NOKT1ATJC
    MODEM: F916NKSU1ATJ7

  OS: Q(Android 10)
  Filename: SM-F916N_10_20201028094404_saezf08xjk_fac.zip.enc4
  Size: 5669940496 bytes
  Logic Value: 611oq0u820f7uv34
  Description:
    • SIM Tray 제거시 가이드 팝업 적용
    • 충전 동작 관련 안정화 코드 적용
    • 단말 동작 관련 안정화 코드 적용
    • 단말 보안 관련 안정화 코드 적용

    https://doc.samsungmobile.com/SM-F916N/KOO/doc.html

/mnt/c/Users/jc/source/repos/SamFirm.NET/SamFirm/dist/linux-x64/SM-F916N_KOO
BL_F916NTBU1ATJC_CL19952515_QB35429635_REV00_user_low_ship_MULTI_CERT.tar.md5
```

## License

```
Copyright (C) 2020 Jesse Chan <jc@linux.com>

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
```
