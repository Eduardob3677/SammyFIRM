# EXML File Decryption

## Overview
EXML files are AES-encrypted XML files used by Samsung's FOTA Agent to store device configuration and firmware update definitions.

## File Locations
EXML files are located in the FotaAgent.apk at:
```
assets/profile/mformtest2020/
  - mformtest2020_DDF_APPLICATION.exml
  - mformtest2020_DDF_DEVDETAIL.exml
  - mformtest2020_DDF_DEVINF.exml
  - mformtest2020_DDF_FUMO.exml
  - mformtest2020_DDF_SYNCML_DM.exml

assets/profile/x6g1q14r75/
  - x6g1q14r75_DDF_DEVINF.exml
  - x6g1q14r75_DDF_SYNCML_DM.exml
  - x6g1q14r75_DDF_DEVDETAIL.exml
  - x6g1q14r75_DDF_FUMO.exml
  - x6g1q14r75_DDF_SYNCML_DM_CHN.exml
```

## File Format
- **DDF_SYNCML_DM**: SyncML Device Management definitions
- **DDF_FUMO**: Firmware Update Management Object definitions  
- **DDF_DEVDETAIL**: Device detail configurations
- **DDF_DEVINF**: Device information definitions
- **DDF_APPLICATION**: Application-specific configurations

## Encryption Details

### Algorithm
- **Cipher**: AES (Advanced Encryption Standard)
- **Mode**: CBC (Cipher Block Chaining) - inferred from AES implementation
- **Key Storage**: Native library `libdprw.so`

### Decryption Process (from Smali analysis)

```java
// From DDFManager.smali decrypt method:
1. Read EXML file as InputStream
2. Convert InputStream to byte array (IDMFileSystemAdapter.idmGetByteFromInputStream)
3. Decrypt bytes using IDMSecurityAESCryptImpl.decrypt()
4. Return decrypted content as new ByteArrayInputStream

// From IDMSecurityAESCryptImpl.smali:
1. Get encryption key from NativeUtils.getKey() (stored in libdprw.so)
2. Call parent class idmGetCryptionResult() with mode=2 (decrypt)
3. Return decrypted byte array
```

### Key Retrieval
The encryption key is obtained from a native method:
```java
// From smali code:
com.samsung.android.fotaagent.common.util.NativeUtils.getKey()
```

This method is implemented in `lib/arm64-v8a/libdprw.so` as:
```c
Java_com_samsung_android_fotaagent_common_util_NativeUtils_getKey
```

### Native Library Functions
Found in `libdprw.so`:
- `Java_com_samsung_android_fotaagent_common_util_NativeUtils_getKey` - Returns AES encryption key
- `Java_com_samsung_android_fotaagent_common_util_NativeUtils_getRegiKey` - Returns registration key
- `Java_com_samsung_android_fotaagent_common_util_NativeUtils_getTimeKey` - Returns time-based key
- `dp_getkey` - Internal key retrieval function

## Hexdump Example
First 20 bytes of `mformtest2020_DDF_FUMO.exml`:
```
00000000  e7 0d 6e 3d 4a c5 49 5e  82 bf aa 80 3c ee 94 e2  |..n=J.I^....<...|
00000010  31 e7 8c 48 42 3f d7 b4  98 06 d0 3e a9 2a ec 96  |1..HB?.....>.*..|
```

The file is completely binary encrypted - no plaintext headers.

## Why EXML Files Are Encrypted
1. **Security**: Prevents unauthorized modification of device management policies
2. **DRM**: Protects proprietary Samsung device configurations
3. **Integrity**: Ensures firmware update definitions haven't been tampered with
4. **Profile Protection**: Keeps test server profiles and configurations secret

## Decryption Limitations
- The AES key is hardcoded in the native library (libdprw.so)
- Extracting the key requires reverse engineering the ARM64 binary
- The key may be obfuscated or generated dynamically within the native code
- Different device models or firmware versions may use different keys

## Practical Application
For the SamFirm.NET project:
- EXML decryption is **not required** for firmware downloads
- The test server logic in Samsung's FOTA agent is independent of EXML files
- EXML files contain device management configurations, not download server URLs
- Server URLs are hardcoded in the smali code as constants

## OMA-DM Standards
These EXML files implement parts of the OMA Device Management (OMA-DM) standard:
- **SyncML DM**: Synchronization Markup Language for Device Management
- **FUMO**: Firmware Update Management Object (OMA-DM specification)
- **DevInfo**: Device Information Management Object
- **DevDetail**: Device Detail Management Object

These standards define how mobile devices communicate with management servers for:
- Remote configuration
- Firmware updates (FOTA)
- Application management
- Security policy enforcement

## References
- OMA Device Management: https://www.openmobilealliance.org/
- FotaAgent package: com.wsomacp
- Native library: libdprw.so (ARM64)
