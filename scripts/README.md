# Scripts Directory

This directory contains utility scripts for analyzing Samsung FOTA (Firmware Over-The-Air) dependencies.

## Available Scripts

### extract_jars.sh

**Purpose:** Extracts and decompiles all JAR files from the Samsung framework for analysis.

**Features:**
- Downloads FOTA dependencies automatically
- Extracts all framework JAR files using apktool
- Falls back to unzip if apktool fails
- Generates extraction report
- Searches for authentication-related code
- Color-coded output for easy reading

**Usage:**
```bash
./scripts/extract_jars.sh
```

**Requirements:**
- apktool (installed automatically if missing)
- wget
- unzip

**Output:**
- Extracted JARs: `/tmp/fota-analysis/jars/`
- Extraction report: `/tmp/fota-analysis/jars/EXTRACTION_REPORT.md`
- Auth search results: `/tmp/fota-analysis/jars/AUTH_SEARCH_RESULTS.txt`

**Environment Variables:**
- `WORK_DIR` - Working directory (default: `/tmp/fota-analysis`)

**Example:**
```bash
# Use custom work directory
WORK_DIR=/path/to/custom/dir ./scripts/extract_jars.sh
```

### What Gets Extracted

The script extracts approximately 45 JAR files from the Samsung framework, including:

#### Core Framework
- `framework.jar` - Main Android framework
- `services.jar` - System services
- `ext.jar` - Framework extensions

#### Telephony & IMS
- `telephony-common.jar` - Telephony APIs
- `telephony-ext.jar` - Extended telephony features
- `ims-common.jar` - IMS (IP Multimedia Subsystem)
- `imsmanager.jar` - IMS manager

#### Samsung-specific
- `com.samsung.android.semtelephonesdk.framework-v1.jar` - SEM telephone SDK
- `com.samsung.device.jar` - Device-specific APIs
- `samsungkeystoreutils.jar` - Samsung keystore utilities
- `service-samsung-payment.jar` - Samsung Pay services
- `service-samsung-blockchain.jar` - Blockchain services

#### Security & Authentication
- `com.sec.android.pmssdk.framework-v1.jar` - PMS (Package Manager Service) SDK
- `com.sec.android.sdhmssdk.framework-v1.jar` - SDHMS (Samsung Device Health Manager Service)
- `esecomm.jar` - eSE (embedded Secure Element) communication

#### Network & Connectivity
- `EpdgManager.jar` - ePDG (evolved Packet Data Gateway) manager
- `semwifi-service.jar` - SEM WiFi service
- `com.samsung.android.nfc.adapter.jar` - NFC adapter

## Analysis Workflow

1. **Extract JARs:**
   ```bash
   ./scripts/extract_jars.sh
   ```

2. **Search for OAuth code:**
   ```bash
   grep -r "oauth" /tmp/fota-analysis/jars/
   ```

3. **Find authentication classes:**
   ```bash
   find /tmp/fota-analysis/jars -name "*Auth*.smali"
   ```

4. **Analyze specific JAR:**
   ```bash
   cd /tmp/fota-analysis/jars/framework
   grep -r "\.method.*auth" .
   ```

5. **Review reports:**
   ```bash
   cat /tmp/fota-analysis/jars/EXTRACTION_REPORT.md
   cat /tmp/fota-analysis/jars/AUTH_SEARCH_RESULTS.txt
   ```

## Understanding Smali Code

The extracted JARs contain `.smali` files, which are human-readable representations of Dalvik bytecode.

### Common Smali Patterns

**Method definition:**
```smali
.method public myMethod(Ljava/lang/String;)V
    # Method body
.end method
```

**Field definition:**
```smali
.field private static final MY_CONSTANT:Ljava/lang/String; = "value"
```

**Method invocation:**
```smali
invoke-virtual {v0, v1}, Lcom/example/Class;->method(Ljava/lang/String;)V
```

### Useful Smali Search Patterns

```bash
# Find all method definitions
grep -r "\.method" /tmp/fota-analysis/jars/

# Find OAuth references
grep -ri "oauth" /tmp/fota-analysis/jars/

# Find crypto/encryption usage
grep -r "Cipher\|encrypt\|decrypt" /tmp/fota-analysis/jars/

# Find network calls
grep -r "HttpURLConnection\|HttpClient" /tmp/fota-analysis/jars/

# Find signature generation
grep -r "HmacSHA\|signature" /tmp/fota-analysis/jars/
```

## Troubleshooting

### apktool fails on some JARs
Some JARs may not be decompilable with apktool. The script automatically falls back to unzip, which extracts the raw contents.

### Out of disk space
The extracted JARs can take several GB of space. Ensure you have at least 5GB free in `/tmp/`.

### Extraction is slow
Extracting 45+ JAR files can take 5-10 minutes. Be patient or run the script in the background:
```bash
nohup ./scripts/extract_jars.sh > extraction.log 2>&1 &
```

## Security Note

These scripts are for educational and research purposes only. The extracted code belongs to Samsung and should be used in compliance with their terms of service and applicable laws.

## Related Documentation

- [OAuth OTA Analysis](../docs/OAUTH_OTA_ANALYSIS.md) - Comprehensive OAuth analysis
- [SamFirm README](../README.md) - Main project documentation

## Contributing

When adding new scripts:
1. Follow the existing structure and naming conventions
2. Include proper error handling
3. Add color-coded output for clarity
4. Generate reports for analysis results
5. Update this README with usage instructions

---

**Last Updated:** 2025-12-29
