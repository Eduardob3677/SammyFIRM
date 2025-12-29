# Samsung Staging Server Findings from EXML Analysis

## Key Discovery: Staging Server

From x6g1q14r75 EXML profile (decrypted):
```
Server: https://stg-fota-cloud-dvce-apis.samsungdms.net
Purpose: Regist Staging server URL
```

From FotaAgent smali code:
```java
STAGING_HOST = "stg-fota-cloud.samsungdms.net"
```

## Server Architecture

### Production Servers:
1. **OSP Server** (Version checking):
   - `https://fota-cloud-dn.ospserver.net/firmware/`
   - Uses: version.xml

2. **FUS Server** (Binary download):
   - `https://neofussvr.sslcs.cdngc.net/`
   - Uses: NF_DownloadBinaryInform.do, NF_DownloadBinaryInitForMass.do

### Staging/Test Servers:
1. **Staging OSP** (found in EXML):
   - `https://stg-fota-cloud-dvce-apis.samsungdms.net`
   - Purpose: Registration and staging tests

2. **Staging Cloud**:
   - `stg-fota-cloud.samsungdms.net`
   - Used in Hidden Admin commands

## Current vs Correct Implementation

### Current (Incorrect for Test):
```
Test URL: https://fota-cloud-dn.ospserver.net/firmware/{region}/{model}/version.test.xml
FUS URL: https://neofussvr.sslcs.cdngc.net/NF_DownloadBinaryInform.do
```

### Should Be (For Staging/Test):
```
Test URL: https://stg-fota-cloud.samsungdms.net/firmware/{region}/{model}/version.xml
OR
Test URL: https://stg-fota-cloud-dvce-apis.samsungdms.net/...
```

## EXML Profile Information

### x6g1q14r75 Profile:
- ServerID: x6g1q14r75
- Client: fotaagent
- Staging URL: stg-fota-cloud-dvce-apis.samsungdms.net

### mformtest2020 Profile:
- ServerID: mformtest2020
- Server: https://iotnucleon.iot.nokia.com/oma/iop
- Password: mform

## Hypothesis for Error 408

The error 408 may be occurring because:
1. We're using the wrong server (`ospserver.net` instead of `samsungdms.net`)
2. The `version.test.xml` endpoint may not be the correct one for staging
3. Staging server may require different authentication or registration

## Recommended Changes

1. Add staging server option
2. Use `stg-fota-cloud.samsungdms.net` for test firmware
3. Update URL construction for staging environment
4. May need device registration API call first

## Additional URLs Found

In smali code:
- XPath for version: `versioninfo/firmware/version/upgrade/value`
- Admin hidden commands reference staging server
- Staging host used for URI construction with HTTPS

