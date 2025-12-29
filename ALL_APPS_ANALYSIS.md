# Complete FOTA System Apps Analysis

## Summary of All Apps

### 1. FotaAgent (com.wsomacp) - v4.4.14
**Primary FOTA update manager**

#### Native Library: libdprw.so
- AES encryption key for EXML files
- OAuth consumer keys (getRegiKey, getTimeKey)
- OAuth consumer secrets (getRegiValue, getTimeValue)

#### Key Findings:
- Uses OAuth 1.0 with HmacSHA1 for OSP requests
- Base URL: `https://fota-cloud-dn.ospserver.net/firmware/`
- Endpoints: `version.xml` (production), `version.test.xml` (test)
- HTTP Headers required:
  - `X-Sec-Dm-DeviceModel` - Device model
  - `X-Sec-Dm-CustomerCode` - Region code
  - `x-osp-version` - "v1"
  - `Accept-Encoding` - "identity"
  - `Content-Type` - "text/xml"

#### Classes:
- `RequestPropertiesForOsp$WithAuth` - OAuth authentication
- `KeyValueLoader` - Loads keys from native library
- `PollingRestClient` - Handles version checking
- `BaseRestClient` - Base HTTP client

### 2. AppUpdateCenter (com.samsung.android.app.updatecenter) - v3.8.03
**App update management**

#### Native Library: libDiagMonKey.so
- Contains `getSALTKey()` method

#### Server URLs Found:
- `https://vas.samsungapps.com`
- `https://regi.di.atlas.samsung.com`
- `https://dc.di.atlas.samsung.com`

#### Purpose:
- Manages application updates (not firmware)
- Uses Samsung's DiagMon (Diagnostic Monitor) system
- Separate from FOTA firmware updates

### 3. MCFDeviceSync (Multi-Connect Framework) - v1.3.08.15
**Device synchronization**

#### Native Library: libDiagMonKey.so (same as AppUpdateCenter)

#### Server URLs Found:
- `https://regi.di.atlas.samsung.com`
- `https://dc.di.atlas.samsung.com`

#### Purpose:
- Device-to-device synchronization
- Keyboard sharing functionality
- Uses DiagMon system

### 4. SmartSwitchAssistant - v2.4.01
**Data transfer assistant**

#### No native libraries
#### No OAuth implementation found

#### Purpose:
- Assists with data transfer during device migration
- Works with Smart Switch application
- Not directly involved in FOTA updates

### 5. OmaCP (OMA Client Provisioning) - v9.1.06
**Device configuration provisioning**

#### No native libraries
#### No OAuth implementation found

#### Purpose:
- Handles OMA-CP (Open Mobile Alliance Client Provisioning)
- Receives configuration via WAP Push and SMS
- Configures APN, MMS, browser settings
- Works alongside FOTA for device management

### 6. NSDSWebApp - v3.1.01.0
**Network Service Discovery**

#### No native libraries
#### No OAuth implementation found

#### Purpose:
- Network service discovery and configuration
- Web-based service interface
- Part of device management infrastructure

## Key Extraction Results

### From libdprw.so (FotaAgent):
```
AES EXML Key: syncml7790010123 (16 bytes, generated via Mealy Machine)
Hex Keys found:
  - 5763D0052DC1462E13751F753384E9A9
  - AF87056C54E8BFD81142D235F4F8E552
  - dkaghghkehlsvkdlsmld
  - j5p7ll8g33
  - 2cbmvps5z4
```

### From libDiagMonKey.so (AppUpdateCenter, MCFDeviceSync):
```
Native method: getSALTKey()
Purpose: Provides key for Samsung Analytics
Not related to FOTA firmware updates
```

## Authentication Architecture

### FotaAgent - Two Systems:

1. **FUS (Firmware Update Service)** - For neofussvr.sslcs.cdngc.net
   ```
   Authorization: FUS nonce="{nonce}", signature="{signature}", ...
   ```

2. **OAuth 1.0** - For OSP (fota-cloud-dn.ospserver.net)
   ```
   Authorization: OAuth oauth_consumer_key="{key}", 
                        oauth_signature="{signature}",
                        oauth_signature_method="HmacSHA1",
                        oauth_timestamp="{timestamp}",
                        oauth_nonce="{nonce}",
                        oauth_version="1.0"
   ```

### Current SamFirm Implementation:
- Uses FUS authentication only
- Missing OAuth 1.0 implementation
- This may be why test server returns 408

## Architecture Diagram

```
FOTA Update Flow:
┌─────────────────┐
│  FotaAgent      │
│  (com.wsomacp)  │
└────────┬────────┘
         │
         ├─────► OSP Server (OAuth 1.0)
         │       https://fota-cloud-dn.ospserver.net/
         │       - version.xml / version.test.xml
         │
         └─────► FUS Server (FUS Auth)
                 https://neofussvr.sslcs.cdngc.net/
                 - DownloadBinaryInform
                 - DownloadBinaryInitForMass
                 - DownloadGenerateNonce

Supporting Apps:
┌──────────────────┐    ┌──────────────────┐
│  AppUpdateCenter │    │  MCFDeviceSync   │
│  (DiagMon)       │    │  (Sync)          │
└──────────────────┘    └──────────────────┘

┌──────────────────┐    ┌──────────────────┐
│  OmaCP           │    │  SmartSwitch     │
│  (Provisioning)  │    │  (Migration)     │
└──────────────────┘    └──────────────────┘
```

## Recommendations

### To Fix 408 Error:

1. **Implement OAuth 1.0 Authentication** for OSP requests
   - Extract or reverse-engineer OAuth keys from libdprw.so
   - Implement OAuth signature generation (HmacSHA1)
   - Use proper timestamp and nonce generation

2. **Verify Request Format**
   - Ensure all required headers are present
   - Verify OAuth signature is correctly calculated
   - Check timestamp format (Unix epoch in seconds)

3. **Alternative Approaches**:
   - Try extracting actual OAuth keys from running device
   - Use frida/xposed to hook native methods
   - Analyze network traffic from real FOTA agent

## Files Modified

### SamFirm.NET Changes:
1. `FUSClient.cs` - Added OSP headers
2. `FUSMsg.cs` - Updated CLIENT_VERSION to 4.4.14
3. `Program.cs` - Added model/region configuration

### Still Needed:
1. OAuth 1.0 implementation
2. Native key extraction or hardcoding
3. Proper signature generation

## Conclusion

The 408 error is likely due to missing OAuth 1.0 authentication. The FotaAgent uses two different authentication systems:
- FUS auth for neofussvr (binary download)
- OAuth 1.0 for OSP (version checking, test servers)

Current SamFirm only implements FUS auth. Test servers may specifically require OAuth authentication which we haven't implemented yet.
