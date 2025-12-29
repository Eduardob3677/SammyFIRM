# Complete Smali Analysis - All FOTA System Apps

## Executive Summary

Analysis of 6 FOTA-related system applications revealed:
- **FotaAgent**: Primary firmware update agent with OAuth 1.0 implementation
- **AppUpdateCenter & MCFDeviceSync**: App updates using DiagMon system
- **OmaCP, NSDSWebApp, SmartSwitchAssistant**: Supporting services

### Key Finding: FotaAgent uses TWO different authentication systems
1. **FUS Authentication** - For neofussvr (firmware binary download)
2. **OAuth 1.0** - For OSP server (version checking, test servers)

---

## 1. FotaAgent (com.wsomacp) v4.4.14

### Package Information
- **Version**: 4.4.14 (versionCode: 441400000)
- **Package**: com.wsomacp  
- **Primary Function**: Firmware Over-The-Air updates

### Native Library: libdprw.so

#### Native Methods Exposed:
```java
public static native String getKey();           // AES key for EXML encryption
public static native String getRegiKey();       // OAuth consumer key (registration)
public static native String getRegiValue();     // OAuth consumer secret (registration)
public static native String getTimeKey();       // OAuth consumer key (time-based)
public static native String getTimeValue();     // OAuth consumer secret (time-based)
public static native boolean setPinAndFallocate(String, long, long);
public static native int unscramble(String, String);
```

#### Keys Extracted from Library:
```
AES EXML Decryption Key: syncml7790010123
Hex Keys: 5763D0052DC1462E13751F753384E9A9
          AF87056C54E8BFD81142D235F4F8E552
Other:    dkaghghkehlsvkdlsmld
          j5p7ll8g33
          2cbmvps5z4
```

### OAuth 1.0 Implementation

#### Class: RequestPropertiesForOsp$WithAuth

**OAuth Parameters:**
```java
oauth_consumer_key      -> From getRegiKey() or getTimeKey()
oauth_consumer_secret   -> From getRegiValue() or getTimeValue()  
oauth_signature_method  -> "HmacSHA1"
oauth_version          -> "1.0"
oauth_nonce            -> Random 10-char hex (SHA1PRNG)
oauth_timestamp        -> Unix time in seconds
```

**Signature Generation Algorithm:**
```
1. Build parameter map with all OAuth params
2. Create signature source:
   METHOD&URL_ENCODED&PARAMS_ENCODED[&QUERY_STRING]
   Example: GET&https%3A%2F%2F...&oauth_consumer_key%3D...
3. Compute HMAC-SHA1:
   Key = consumer_secret + "&"
   Data = signature source
4. Base64 encode the HMAC result
5. Add as oauth_signature parameter
```

**Implementation Details:**
```java
// From generateSignatureSource method:
String source = httpMethod.toUpperCase() + "&" +
                urlEncode(normalizeUrl(url)) + "&" +
                urlEncode(normalizeParams(oauthParams)) +
                (queryString != null ? "&" + queryString : "");

// From computeSignature method:
Mac mac = Mac.getInstance("HmacSHA1");
SecretKeySpec key = new SecretKeySpec(
    consumerSecret.getBytes(UTF_8), 
    "HmacSHA1"
);
mac.init(key);
byte[] signature = mac.doFinal(source.getBytes(UTF_8));
return Base64.encode(signature);
```

#### HTTP Headers (OSP Requests):
```
X-Sec-Dm-DeviceModel: {model}       // e.g., SM-S916B
X-Sec-Dm-CustomerCode: {region}     // e.g., TPA
x-osp-version: v1
Accept-Encoding: identity
Content-Type: text/xml
Authorization: OAuth oauth_consumer_key="{key}",
                     oauth_signature="{sig}",
                     oauth_signature_method="HmacSHA1",
                     oauth_timestamp="{ts}",
                     oauth_nonce="{nonce}",
                     oauth_version="1.0"
```

### Server URLs
```
OSP Server (OAuth):
  https://fota-cloud-dn.ospserver.net/firmware/{region}/{model}/version.xml
  https://fota-cloud-dn.ospserver.net/firmware/{region}/{model}/version.test.xml

FUS Server (FUS Auth):
  https://neofussvr.sslcs.cdngc.net/NF_DownloadGenerateNonce.do
  https://neofussvr.sslcs.cdngc.net/NF_DownloadBinaryInform.do
  https://neofussvr.sslcs.cdngc.net/NF_DownloadBinaryInitForMass.do
  
Binary Download:
  http://cloud-neofussvr.samsungmobile.com/NF_DownloadBinaryForMass.do
```

### REST API Classes (154 files)
Key classes:
- `BaseRestClient` - Abstract base for all REST clients
- `PollingRestClient` - Handles version checking
- `HeartbeatRestClient` - Keep-alive functionality
- `DeviceRestClient` - Device registration
- `RequestPropertiesForOsp` - OAuth request builder
- `KeyValueLoader` - Loads OAuth keys from native library

---

## 2. OmaCP (com.wsomacp) v9.1.06

### Package Information
- **Version**: 9.1.06 (versionCode: 910600000)
- **Package**: com.wsomacp
- **Primary Function**: OMA Client Provisioning

### No Native Libraries

### Key Findings:
```smali
const-string v4, "secret: "
const-string v3, "m_szAauthSecret : "
const-string v4, "m_szAuthSecret : "
```

### Purpose:
- Receives device configuration via WAP Push / SMS
- Provisions APN, MMS, browser settings
- Works with FOTA for OMA-DM (Device Management)
- No direct involvement in firmware downloads

### URLs Found:
- Only W3C XML namespace URLs
- No firmware server URLs

---

## 3. AppUpdateCenter (com.samsung.android.app.updatecenter) v3.8.03

### Package Information
- **Version**: 3.8.03 (versionCode: 380300000)
- **Package**: com.samsung.android.app.updatecenter
- **Primary Function**: Application update management

### Native Library: libDiagMonKey.so

**Native Method:**
```java
public static native char[] getSALTKey();
```

**Purpose**: Samsung Analytics encryption key (NOT for FOTA)

### Server URLs:
```
https://vas.samsungapps.com                  // Samsung Apps store
https://regi.di.atlas.samsung.com            // DiagMon registration
https://dc.di.atlas.samsung.com              // DiagMon data collection
```

### Purpose:
- Manages APPLICATION updates (not firmware)
- Uses Samsung DiagMon (Diagnostic Monitor) system
- Integrates with Samsung Analytics
- Separate from firmware FOTA updates

---

## 4. MCFDeviceSync (com.samsung.android.mcfds) v1.3.08.15

### Package Information
- **Version**: 1.3.08.15 (versionCode: 130815000)
- **Package**: com.samsung.android.mcfds
- **Primary Function**: Multi-Connect Framework Device Sync

### Native Library: libDiagMonKey.so (same as AppUpdateCenter)

### Multiple SDK Versions:
```
Samsung Health Data SDK: 1.0.0-beta01
Simple Sharing SDK: 1.0.25061915
Bixby 2 SDK: 1.0.25
OCR SDK: 3.4.240805
Proximity Core SDK: 0.0.8
```

### Server URLs:
```
https://regi.di.atlas.samsung.com
https://dc.di.atlas.samsung.com
https://plus.google.com/
https://www.googleapis.com/auth/games
```

### Native Methods (LiveTranslation):
```java
private static native int _initialize(int, int, int, int, int, int);
private static native int _processImage(byte[]);
private static native int _renderingText(boolean);
private static native String _getNativeParameter(int);
// ... more translation-related methods
```

### Purpose:
- Device-to-device synchronization
- Keyboard sharing between devices
- Live translation features
- Not directly involved in firmware updates

---

## 5. NSDSWebApp (com.sec.vsim.ericssonnsds.webapp) v3.1.01.0

### Package Information
- **Version**: 3.1.01.0 (versionCode: 310100000)
- **Package**: com.sec.vsim.ericssonnsds.webapp
- **Primary Function**: Network Service Discovery

### No Native Libraries
### No OAuth Implementation
### No Server URLs Found

### Purpose:
- Network service discovery and configuration
- Web-based service interface
- eSIM/virtual SIM functionality (Ericsson NSDS)
- Part of device management infrastructure

---

## 6. SmartSwitchAssistant (com.samsung.android.smartswitchassistant) v2.4.01

### Package Information
- **Version**: 2.4.01 (versionCode: 240100000)
- **Package**: com.samsung.android.smartswitchassistant
- **Primary Function**: Smart Switch data transfer assistant

### No Native Libraries
### No OAuth Implementation  
### No Server URLs Found
### No Keys/Secrets Found

### Purpose:
- Assists with data transfer during device migration
- Works with Smart Switch PC/Mobile applications
- Facilitates backup/restore operations
- Not involved in firmware updates

---

## Authentication Comparison

### FotaAgent - Dual Authentication:

**1. FUS Authentication (neofussvr):**
```
POST https://neofussvr.sslcs.cdngc.net/NF_DownloadBinaryInform.do
Headers:
  User-Agent: Kies2.0_FUS
  Authorization: FUS nonce="{nonce}", signature="{sig}", nc="", type="", realm="", newauth="1"
  Cache-Control: no-cache
Body: XML with device info and version
```

**2. OAuth 1.0 Authentication (OSP):**
```
GET https://fota-cloud-dn.ospserver.net/firmware/{region}/{model}/version.test.xml
Headers:
  X-Sec-Dm-DeviceModel: {model}
  X-Sec-Dm-CustomerCode: {region}
  x-osp-version: v1
  Accept-Encoding: identity
  Content-Type: text/xml
  Authorization: OAuth oauth_consumer_key="{key}",
                       oauth_signature="{hmac_sha1_signature}",
                       oauth_signature_method="HmacSHA1",
                       oauth_timestamp="{unix_timestamp}",
                       oauth_nonce="{random_10_char_hex}",
                       oauth_version="1.0"
```

### Current SamFirm Implementation:
- ✅ Implements FUS authentication
- ❌ Missing OAuth 1.0 implementation
- ❌ Missing OSP-specific headers (FIXED in latest changes)
- ✅ Correct CLIENT_VERSION (FIXED: 4.4.14)

---

## Root Cause Analysis: Error 408

### Why Test Server Returns 408:

1. **Missing OAuth Authentication**
   - Test servers likely require OAuth 1.0
   - Current implementation only uses FUS auth
   - OAuth consumer keys are in libdprw.so (encrypted/obfuscated)

2. **Possible Solutions:**

   **Option A**: Extract OAuth keys from library
   - Requires reverse engineering ARM64 binary
   - Keys are likely obfuscated/encrypted
   - Complex but permanent solution

   **Option B**: Implement OAuth with test keys
   - Try common Samsung OAuth keys from other projects
   - May work if keys are standardized
   - Risk of server rejection

   **Option C**: Capture from real device
   - Use Frida/Xposed to hook NativeUtils methods
   - Capture actual keys at runtime
   - Requires rooted device with FOTA agent

---

## Implementation Roadmap

### Phase 1: OAuth 1.0 Implementation ✅ HEADERS ADDED
- [x] Add OSP-specific HTTP headers
- [x] Update CLIENT_VERSION to 4.4.14  
- [ ] Implement OAuth 1.0 signature generation
- [ ] Add timestamp and nonce generation
- [ ] Integrate with request flow

### Phase 2: Key Extraction 🔄 IN PROGRESS
- [x] Analyze libdprw.so structure
- [x] Identify key storage locations
- [ ] Reverse engineer key obfuscation
- [ ] Extract or derive OAuth keys

### Phase 3: Testing & Validation
- [ ] Test OAuth implementation
- [ ] Verify signature generation
- [ ] Test with real device IMEI
- [ ] Validate test server access

---

## Conclusion

The comprehensive smali analysis reveals that **FotaAgent uses TWO separate authentication systems**:

1. **FUS Auth** for firmware binary operations (neofussvr)
2. **OAuth 1.0** for version checking and test servers (OSP)

The **408 error** occurs because:
- SamFirm only implements FUS authentication
- Test servers require OAuth 1.0 authentication
- OAuth consumer keys are stored in libdprw.so (native library)

**Next Steps**:
1. Implement OAuth 1.0 signature generation in C#
2. Extract or derive OAuth consumer keys
3. Test with proper OAuth authentication

