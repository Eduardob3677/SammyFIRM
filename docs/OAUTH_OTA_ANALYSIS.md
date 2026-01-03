# OAuth OTA Analysis - Samsung Firmware Download System

## Overview

This document provides a comprehensive analysis of the OAuth-based authentication system used by Samsung for Over-The-Air (OTA) firmware updates. The analysis is based on decompiled APK files from the Samsung firmware ecosystem, particularly the FotaAgent (Firmware Over-The-Air Agent).

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [OAuth 1.0 Implementation](#oauth-10-implementation)
3. [Key Components](#key-components)
4. [Authentication Flow](#authentication-flow)
5. [Code Analysis](#code-analysis)
6. [Integration Guide](#integration-guide)
7. [Security Considerations](#security-considerations)

## Architecture Overview

The Samsung OTA system uses a multi-layered architecture:

```
┌─────────────────────────────────────────────────┐
│         Samsung FOTA Server Infrastructure      │
│  (neofussvr.sslcs.cdngc.net / ospserver.net)   │
└─────────────────────────────────────────────────┘
                       ▲
                       │ OAuth 1.0 + Custom Auth
                       │
┌─────────────────────────────────────────────────┐
│              FotaAgent (Client)                 │
│  ┌──────────────────────────────────────────┐  │
│  │  RequestPropertiesForOsp$WithAuth        │  │
│  │  - OAuth 1.0 signature generation        │  │
│  │  - HmacSHA1 signing                      │  │
│  │  - Timestamp synchronization             │  │
│  └──────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────┐  │
│  │  AuthFramework                           │  │
│  │  - Samsung Account integration           │  │
│  │  - Device authentication                 │  │
│  └──────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
```

## OAuth 1.0 Implementation

### Overview

Samsung's FOTA system implements **OAuth 1.0** (not OAuth 2.0) with HmacSHA1 signature method. This is a classic three-legged OAuth implementation adapted for device-to-server authentication.

### Key OAuth Parameters

Based on the decompiled `FotaAgent` code, the following OAuth 1.0 parameters are used:

| Parameter | Value | Description |
|-----------|-------|-------------|
| `oauth_consumer_key` | Device/App specific | Consumer key identifying the client |
| `oauth_nonce` | Random 10-char token | One-time random value |
| `oauth_signature_method` | `HmacSHA1` | Signature algorithm |
| `oauth_timestamp` | Unix timestamp | Request timestamp (synced with server) |
| `oauth_version` | `1.0` | OAuth protocol version |
| `oauth_signature` | Base64-encoded HMAC | Request signature |

### OAuth Flow

```
1. Client generates oauth_nonce (10 random characters)
2. Client retrieves current timestamp (synchronized with server)
3. Client constructs parameter map with all OAuth fields
4. Client creates signature base string
5. Client signs with HmacSHA1 using consumer secret
6. Client adds Authorization header with OAuth parameters
7. Server validates signature and timestamp
8. Server responds with firmware information or download
```

## Key Components

### 1. FotaAgent APK
**Package:** `com.wsomacp`  
**Location:** `/system/priv-app/FotaAgent/FotaAgent.apk`

**Key Classes:**
- `com.idm.fotaagent.restapi.request.RequestPropertiesForOsp$WithAuth`
  - Implements OAuth 1.0 signature generation
  - Manages request properties for OSP (Open Service Platform)
  - Handles timestamp synchronization

**Permissions:**
- `android.permission.READ_PRIVILEGED_PHONE_STATE`
- `android.permission.RECEIVE_SMS` / `RECEIVE_WAP_PUSH`
- `android.permission.INTERNET`
- `com.sec.android.fotaclient.permission.FOTA`

### 2. AuthFramework APK
**Package:** `com.samsung.android.authfw`  
**Location:** `/system/priv-app/AuthFramework/AuthFramework.apk`

**Purpose:**
- Provides Samsung Account authentication
- Manages device credentials
- Handles biometric and secure authentication

### 3. SamsungAccount APK
**Package:** `com.osp.app.signin`  
**Location:** `/system/priv-app/SamsungAccount/SamsungAccount.apk`

**OAuth References:**
- `com.samsung.android.samsungaccount.authentication.sso.SsoActivity`
- `com.samsung.android.samsungaccount.authentication.sso.SsoConstants`
- `com.samsung.android.samsungaccount.authentication.server.common.request.AuthRequest`

### 4. AppUpdateCenter APK
**Package:** `com.samsung.android.app.updatecenter`  
**Location:** `/system/priv-app/AppUpdateCenter/AppUpdateCenter.apk`

**Purpose:**
- Manages application updates
- Coordinates with FOTA for system updates
- Handles update scheduling and notifications

## Authentication Flow

### Complete OTA Download Flow with OAuth

```
Device (FotaAgent) → Samsung FOTA Server → Auth Server

1. Request Nonce (NF_DownloadGenerateNonce.do)
   Server returns encrypted nonce
   
2. Decrypt nonce with AES-CBC
   Device decrypts using static keys
   
3. Generate OAuth parameters
   - oauth_nonce (10 random chars)
   - oauth_timestamp
   - oauth_consumer_key
   
4. Create signature base string
   Format: METHOD&URL&PARAMS
   
5. Sign with HmacSHA1
   Using consumer secret
   
6. Request firmware info (NF_DownloadBinaryInform.do)
   Headers:
   - Authorization: FUS nonce="...", signature="..."
   - OAuth 1.0 Authorization header
   
7. Server validates OAuth signature
   - Verify signature matches
   - Check timestamp freshness
   
8. Server returns firmware metadata (XML)
   - BINARY_NAME
   - BINARY_BYTE_SIZE
   - LOGIC_VALUE_FACTORY
   - MODEL_PATH
   
9. Initialize download (NF_DownloadBinaryInitForMass.do)
   Server confirms initialization
   
10. Download binary (NF_DownloadBinaryForMass.do)
    Streaming download with real-time decryption
    
11. Decrypt firmware (AES-256)
    Verify integrity and extract
```

### Detailed OAuth Signature Generation

From `RequestPropertiesForOsp$WithAuth.smali`:

```java
// Pseudo-code extracted from smali
class WithAuth {
    private static final String AUTH_KEY_KEY = "oauth_consumer_key";
    private static final String AUTH_KEY_METHOD = "oauth_signature_method";
    private static final String AUTH_KEY_NONCE = "oauth_nonce";
    private static final String AUTH_KEY_SIGNATURE = "oauth_signature";
    private static final String AUTH_KEY_TIMESTAMP = "oauth_timestamp";
    private static final String AUTH_KEY_VERSION = "oauth_version";
    
    private static final String AUTH_VALUE_OAUTH_SIGNATURE_METHOD = "HmacSHA1";
    private static final String AUTH_VALUE_OAUTH_VERSION = "1.0";
    
    private String generateAuth(String consumerKey, String url, 
                                 String method, String consumerSecret, 
                                 String params, long timestamp) {
        Map<String, String> oauthParams = new HashMap<>();
        
        // Add OAuth parameters
        oauthParams.put("oauth_consumer_key", consumerKey);
        oauthParams.put("oauth_nonce", generateRandomToken(10));
        oauthParams.put("oauth_signature_method", "HmacSHA1");
        oauthParams.put("oauth_timestamp", String.valueOf(timestamp));
        oauthParams.put("oauth_version", "1.0");
        
        // Create signature base string
        String signatureBaseString = createSignatureBaseString(
            method, url, oauthParams, params
        );
        
        // Generate signature
        byte[] signature = computeHmacSha1(
            signatureBaseString, 
            consumerSecret
        );
        
        // Add signature to params
        oauthParams.put("oauth_signature", 
            Base64.encode(signature));
        
        // Build Authorization header
        return buildAuthorizationHeader(oauthParams);
    }
    
    private byte[] computeHmacSha1(String data, String key) {
        SecretKeySpec keySpec = new SecretKeySpec(
            key.getBytes(UTF_8), "HmacSHA1"
        );
        Mac mac = Mac.getInstance("HmacSHA1");
        mac.init(keySpec);
        return mac.doFinal(data.getBytes(UTF_8));
    }
    
    private String generateRandomToken(int length) {
        // Generates random alphanumeric token
        // Used for oauth_nonce
    }
    
    private String urlEncodeWithOAuthSpec(String value) {
        // URL encoding per OAuth 1.0 spec (RFC 5849)
        // Encodes all characters except unreserved:
        // A-Z, a-z, 0-9, hyphen, period, underscore, tilde
    }
    
    private String normalizeUrlWithOAuthSpec(String url) {
        // Normalizes URL per OAuth spec:
        // 1. Lowercase scheme and host
        // 2. Remove default ports (80 for HTTP, 443 for HTTPS)
        // 3. Remove fragment
    }
}
```

## Code Analysis

### OAuth Signature Base String Format

According to OAuth 1.0 specification (RFC 5849), the signature base string is constructed as:

```
HTTP_METHOD + "&" + 
percent_encode(normalized_url) + "&" + 
percent_encode(sorted_parameter_string)
```

**Example:**
```
POST&https%3A%2F%2Fneofussvr.sslcs.cdngc.net%2FNF_DownloadBinaryInform.do&
oauth_consumer_key%3DABC123%26
oauth_nonce%3DxYz9876543%26
oauth_signature_method%3DHmacSHA1%26
oauth_timestamp%3D1640000000%26
oauth_version%3D1.0
```

### FUS (Firmware Update Service) Authentication

In addition to OAuth, Samsung uses a custom "FUS" authentication layer:

```java
// From Utils/Auth.cs in SamFirm.NET
Authorization: FUS nonce="<encrypted_nonce>", 
                   signature="<aes_encrypted_nonce>", 
                   nc="", type="", realm="", newauth="1"
```

This dual authentication provides:
1. **OAuth 1.0**: Standard industry authentication
2. **FUS Custom**: Samsung-specific device validation

### Nonce Encryption/Decryption

From `SamFirm/Utils/Auth.cs`:

```csharp
private const string NONCE_KEY = "vicopx7dqu06emacgpnpy8j8zwhduwlh";
private const string AUTH_KEY = "9u7qab84rpc16gvk";

public static string DecryptNonce(string nonce) {
    return Crypto.DecryptStringFromBytes(
        Convert.FromBase64String(nonce), 
        Encoding.ASCII.GetBytes(NONCE_KEY)
    );
}

public static string GetAuthorization(string decryptedNonce) {
    StringBuilder key = new StringBuilder();
    for (int i = 0; i < 16; i++) {
        int nonceChar = decryptedNonce[i];
        key.Append(NONCE_KEY[nonceChar % 16]);
    }
    key.Append(AUTH_KEY);
    
    return Convert.ToBase64String(
        Crypto.EncryptStringToBytes(
            decryptedNonce, 
            Encoding.ASCII.GetBytes(key.ToString())
        )
    );
}
```

**Encryption Details:**
- Algorithm: AES-256-CBC
- Key: Derived from nonce and static keys
- IV: First 16 bytes of key

## Integration Guide

### Prerequisites

1. **apktool** - For APK decompilation and analysis
2. **Device credentials** - Valid IMEI, model, region
3. **Consumer keys** - OAuth consumer key/secret (device-specific)

### Step 1: Install Dependencies

```bash
sudo apt install apktool -y
```

### Step 2: Download and Extract FOTA Dependencies

```bash
wget "https://github.com/Eduardob3677/UN1CA-firmware-dm2q/releases/download/firmware-dm2q-20251227-151319/fota-agent-dependencies-0195d3da907eb981cea007ff5da5c87687847329.zip" -O fota-deps.zip

unzip fota-deps.zip
```

### Step 3: Decompile Key APKs

```bash
apktool d system/system/priv-app/FotaAgent/FotaAgent.apk -o FotaAgent
apktool d system/system/priv-app/AuthFramework/AuthFramework.apk -o AuthFramework
apktool d system/system/priv-app/SamsungAccount/SamsungAccount.apk -o SamsungAccount
```

### Step 4: Analyze OAuth Implementation

Key files to examine:
- `FotaAgent/smali/com/idm/fotaagent/restapi/request/RequestPropertiesForOsp$WithAuth.smali`
- `SamsungAccount/smali_classes2/com/samsung/android/samsungaccount/authentication/sso/SsoActivity.smali`

### Step 5: Implement OAuth Client

Use the existing `SamFirm.NET` codebase as a reference, adding OAuth 1.0 authentication:

```csharp
// Example integration in C#
public static async Task<string> DownloadWithOAuth(
    string model, string region, string imei)
{
    // 1. Generate nonce
    FUSClient.GenerateNonce();
    
    // 2. Get firmware version
    string version = await GetLatestVersion(region, model, false);
    
    // 3. Create OAuth signature
    var oauthParams = new OAuthParameters {
        ConsumerKey = GetConsumerKey(model),
        Nonce = GenerateNonce(10),
        Timestamp = GetServerTimestamp(),
        SignatureMethod = "HmacSHA1",
        Version = "1.0"
    };
    
    string signature = GenerateOAuthSignature(
        oauthParams, 
        "POST",
        "https://neofussvr.sslcs.cdngc.net/NF_DownloadBinaryInform.do",
        GetConsumerSecret(model)
    );
    
    oauthParams.Signature = signature;
    
    // 4. Make authenticated request
    var request = CreateRequest(url);
    request.Headers.Add("Authorization", 
        BuildOAuthHeader(oauthParams));
    
    // 5. Download firmware
    var response = await request.GetResponseAsync();
    // ... process response
}
```

## Security Considerations

### 1. OAuth Consumer Credentials

- **Consumer keys are device-specific** - Each device model may have unique credentials
- **Consumer secrets must be protected** - Store securely, never hardcode in public repos
- **Key rotation** - Samsung may periodically rotate OAuth credentials

### 2. Timestamp Synchronization

- **Server time validation** - Requests must have timestamps within acceptable window (typically ±5 minutes)
- **Time sync required** - Device must sync with NTP or server time before requests
- **Replay attack prevention** - Nonces prevent request replay

### 3. HTTPS/TLS Requirements

- All OAuth requests MUST use HTTPS
- Certificate pinning recommended for production
- TLS 1.2+ required

### 4. Nonce Generation

- **Cryptographically secure random** - Use proper CSPRNG
- **Sufficient entropy** - 10+ characters from large character set
- **Never reuse** - Each request must have unique nonce

### 5. Signature Validation

Server validates:
1. OAuth signature matches computed signature
2. Timestamp is within acceptable window
3. Nonce hasn't been used before (replay protection)
4. Consumer key is valid for the device/model
5. All required OAuth parameters present

## Analyzed APKs Summary

The following APKs were decompiled and analyzed:

### Core OTA/FOTA
- ✅ **FotaAgent** - OAuth 1.0 implementation found
- ✅ **AuthFramework** - Authentication framework
- ✅ **AppUpdateCenter** - App update management

### Samsung Account & Authentication
- ✅ **SamsungAccount** - OAuth SSO implementation
- ✅ **SamsungPass** - Biometric authentication

### MDM (Mobile Device Management)
- ✅ **MDMApp** - Device management
- ✅ **UniversalMDMClient** - Universal MDM client

### Knox Security Suite
- ✅ **KnoxCore** - Core security framework
- ✅ **KnoxMposAgent** - Mobile POS agent
- ✅ **KnoxPushManager** - Push notification manager
- ✅ **KnoxNetworkFilter** - Network filtering
- ✅ **KnoxZtFramework** - Zero Trust framework
- ✅ **knoxvpnproxyhandler** - VPN proxy

### Samsung Agents
- ✅ **SCPMAgent** - Configuration & Policy Manager
- ✅ **KLMSAgent** - Knox License Management
- ✅ **DiagMonAgent95** - Diagnostic Monitoring
- ✅ **DeviceQualityAgent36** - Device Quality
- ✅ **EnhancedAttestationAgent** - Device attestation

### Other Components
- ✅ **SPPPushClient** - Samsung Push Protocol
- ✅ **MCFDeviceSync** - Multi-Connect Framework
- ✅ **SmartSwitchAssistant** - Data transfer
- ✅ **GalaxyResourceUpdater** - Resource updates

## References

### Specifications
- [RFC 5849 - OAuth 1.0 Protocol](https://tools.ietf.org/html/rfc5849)
- [OAuth Core 1.0 Revision A](https://oauth.net/core/1.0a/)

### Samsung Resources
- Samsung FOTA Server: `neofussvr.sslcs.cdngc.net`
- Samsung OSP Server: `fota-cloud-dn.ospserver.net`

### Related Projects
- [SamFirm.NET](https://github.com/jesec/samfirm.net) - Reference implementation
- Current repository - Enhanced OAuth analysis

## Contributing

When contributing OAuth analysis:
1. Document all findings in this file
2. Include decompiled code snippets (smali or pseudo-code)
3. Verify against actual firmware download tests
4. Update security considerations as needed

## License

This analysis is for educational and research purposes. Respect Samsung's terms of service and intellectual property.

---

**Last Updated:** 2025-12-29  
**Analyzed Package:** fota-agent-dependencies-0195d3da907eb981cea007ff5da5c87687847329.zip  
**Source:** UN1CA-firmware-dm2q repository
