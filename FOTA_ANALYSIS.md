# FOTA Agent Analysis and Findings

## Overview
This document details the analysis of Samsung's FOTA (Firmware Over-The-Air) agent and the SamFirm.NET download implementation.

## FOTA Agent Architecture

### Key Components Analyzed
1. **FotaAgent.apk** (com.wsomacp) - Main OTA update application
2. **OmaCP.apk** - OMA Client Provisioning
3. **Native Libraries** - libdprw.so (contains AES encryption keys)

### Server URLs
- **Version Server**: `https://fota-cloud-dn.ospserver.net/firmware/`
  - Production: `version.xml`
  - Test: `version.test.xml`
- **Authentication Server**: `https://neofussvr.sslcs.cdngc.net/`
- **Download Server**: `http://cloud-neofussvr.samsungmobile.com/`

### EXML Files (Encrypted XML)
Located in `assets/profile/` directories:
- `mformtest2020/` - Test profile with encrypted device definition files
- `x6g1q14r75/` - Another profile with DDF (Device Description Framework) files

EXML files are AES-encrypted and contain:
- DDF_SYNCML_DM.exml - SyncML Device Management definitions
- DDF_FUMO.exml - Firmware Update Management Object definitions
- DDF_DEVDETAIL.exml - Device detail definitions
- DDF_DEVINF.exml - Device info definitions
- DDF_APPLICATION.exml - Application definitions

The encryption key is obtained from native method `NativeUtils.getKey()` in libdprw.so.

## Current SamFirm.NET Implementation

### Test Server Support
The current implementation includes:
1. `--test` flag to use test server
2. URL templates for both test and regular servers
3. MD5 hash-based version decryption for test firmware

### Version Detection Algorithm
For test servers, the code:
1. Fetches `version.test.xml` which contains MD5 hashes
2. Fetches regular `version.xml` to get base version info
3. Brute-forces version strings by trying variations
4. Computes MD5 hash of each candidate
5. Matches against the MD5 hashes from test XML

## Issues Identified

### 1. Version String Format
**Issue**: When there's no modem (CP) part, the version string might have incorrect format.

**Current behavior**: Creates `PDA/CSC/` (with trailing slash but empty modem)
**Expected format**: Could be `PDA/CSC` or `PDA/CSC/PDA` (using PDA as modem fallback)

### 2. Test Server Access
**Analysis**: The FOTA agent uses the same base URL for both test and production servers, only changing the XML filename (`version.xml` vs `version.test.xml`).

**Current implementation**: Correctly uses HTTPS for test server and HTTP for production.

## Recommendations

1. **Version Format Fix**: Adjust version string construction when CP part is missing
2. **Enhanced Logging**: Add more detailed logging for test server operations
3. **Error Handling**: Improve error messages when test firmware cannot be resolved
4. **Documentation**: Document the test server usage and MD5 decryption process

## FOTA Agent Code Locations

### Version polling logic
- `smali/com/idm/fotaagent/database/sqlite/database/polling/PollingInfo.smali`
- `smali/com/idm/fotaagent/database/room/data/repository/PollingRepository.smali`

### Encryption/Decryption
- `smali/com/idm/fotaagent/enabler/security/IDMSecurityAESCryptImpl.smali`
- `smali/com/idm/fotaagent/tool/ddf/DDFManager.smali`
- `lib/arm64-v8a/libdprw.so` (native encryption key storage)

### Server Constants
- Base URL: `https://fota-cloud-dn.ospserver.net/firmware/`
- Version files: `version.xml` (production), `version.test.xml` (test)
