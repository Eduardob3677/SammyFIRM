# SamFirm.NET

A streaming downloader, decryptor and extractor of Samsung firmware.

## Getting started

### Run

1. Download the executable from [Release](https://github.com/jesec/samfirm.net/releases).
1. Run it with `--region` , `--model` and `--i` arguments.
1. Region, Model and IMEI need to be valid for the target device, otherwise FUS will respond with Err 408
1. If you dont have the IMEI of a certain device you want, usually googling <model> "imei swappa" will bring up valid ones

#### Optional: Selective Component Download

To save disk space, you can download only specific firmware components using these options:

- `--ap` - Download only AP (Application Processor) file
- `--bl` - Download only BL (Bootloader) file
- `--cp` - Download only CP (Modem/Radio) file
- `--csc` - Download only CSC (Consumer Software Customization) file
- `--home-csc` - Download only HOME_CSC file

You can combine multiple options. If no component options are specified, all components will be downloaded.

**Note:** 
- TAR archives (`.tar.md5` files) are extracted directly from the encrypted ZIP stream without writing to disk first, significantly improving extraction speed (can reduce extraction time by 50-70%)
- All temporary files including encrypted firmware files (`.enc4`) and download control files are automatically cleaned up after extraction
- Uses optimized 4MB buffer size for faster extraction of large files

Windows users may choose the smaller but not-self-contained variant if [.NET runtime](https://dotnet.microsoft.com/download/dotnet/5.0/runtime) is present.

### Build

1. Get [Visual Studio 2019](https://visualstudio.microsoft.com/vs/)
1. Open the repository with VS 2019
1. Install dependencies as prompted
1. Build solution

## Example

### Download all components (default behavior)

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

### Download only specific components (to save disk space)

Download only AP file:
```
> ./SamFirm -m SM-S916B -r EUX -i <valid imei> --ap
```

Download only BL and CSC files:
```
> ./SamFirm -m SM-S916B -r EUX -i <valid imei> --bl --csc
```

Download AP, BL, and CP files:
```
> ./SamFirm -m SM-S938B -r EUX -i <valid imei> --ap --bl --cp
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
