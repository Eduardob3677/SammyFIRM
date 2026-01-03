#!/bin/bash
# Script to extract JAR files from Samsung FOTA dependencies using apktool
# This script decompiles all framework JAR files for analysis

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
FOTA_DEPS_URL="https://github.com/Eduardob3677/UN1CA-firmware-dm2q/releases/download/firmware-dm2q-20251227-151319/fota-agent-dependencies-0195d3da907eb981cea007ff5da5c87687847329.zip"
WORK_DIR="${WORK_DIR:-/tmp/fota-analysis}"
JARS_DIR="$WORK_DIR/jars"
FRAMEWORK_DIR=""

# Function to print colored messages
print_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

# Function to check if apktool is installed
check_apktool() {
    if ! command -v apktool &> /dev/null; then
        print_error "apktool is not installed!"
        print_info "Installing apktool..."
        sudo apt-get update -qq
        sudo apt-get install -y apktool
        print_success "apktool installed successfully"
    else
        print_success "apktool is already installed ($(apktool --version | head -1))"
    fi
}

# Function to download FOTA dependencies if not present
download_fota_deps() {
    if [ ! -f "/tmp/fota-deps.zip" ]; then
        print_info "Downloading FOTA dependencies..."
        wget -q --show-progress "$FOTA_DEPS_URL" -O /tmp/fota-deps.zip
        print_success "Download complete"
    else
        print_info "FOTA dependencies already downloaded"
    fi
}

# Function to extract FOTA dependencies
extract_fota_deps() {
    if [ ! -d "/tmp/system" ]; then
        print_info "Extracting FOTA dependencies..."
        cd /tmp
        unzip -q fota-deps.zip
        print_success "Extraction complete"
    else
        print_info "FOTA dependencies already extracted"
    fi
}

# Function to find framework directory
find_framework_dir() {
    FRAMEWORK_DIR=$(find /tmp -type d -path "*/system/system/framework" 2>/dev/null | head -1)
    if [ -z "$FRAMEWORK_DIR" ]; then
        print_error "Framework directory not found!"
        exit 1
    fi
    print_success "Framework directory found: $FRAMEWORK_DIR"
}

# Function to extract a single JAR file
extract_jar() {
    local jar_path="$1"
    local jar_name=$(basename "$jar_path" .jar)
    local output_dir="$JARS_DIR/$jar_name"
    
    if [ -d "$output_dir" ]; then
        print_warning "Skipping $jar_name (already extracted)"
        return 0
    fi
    
    print_info "Extracting $jar_name..."
    
    # Try to extract with apktool
    if apktool d "$jar_path" -o "$output_dir" -f &>/dev/null; then
        print_success "✓ $jar_name extracted successfully"
        return 0
    else
        # If apktool fails, try unzip as fallback
        print_warning "apktool failed for $jar_name, trying unzip..."
        mkdir -p "$output_dir"
        if unzip -q "$jar_path" -d "$output_dir" 2>/dev/null; then
            print_success "✓ $jar_name extracted with unzip"
            return 0
        else
            print_error "✗ Failed to extract $jar_name"
            rm -rf "$output_dir"
            return 1
        fi
    fi
}

# Function to extract all JAR files
extract_all_jars() {
    print_info "Creating output directory: $JARS_DIR"
    mkdir -p "$JARS_DIR"
    
    # Find all JAR files
    local jar_files=($(find "$FRAMEWORK_DIR" -name "*.jar" | sort))
    local total_jars=${#jar_files[@]}
    local success_count=0
    local fail_count=0
    
    print_info "Found $total_jars JAR files to extract"
    echo ""
    
    # Extract each JAR file
    for jar_file in "${jar_files[@]}"; do
        if extract_jar "$jar_file"; then
            ((success_count++))
        else
            ((fail_count++))
        fi
    done
    
    echo ""
    print_success "Extraction complete!"
    print_info "Successfully extracted: $success_count/$total_jars"
    if [ $fail_count -gt 0 ]; then
        print_warning "Failed to extract: $fail_count/$total_jars"
    fi
}

# Function to generate analysis report
generate_report() {
    local report_file="$JARS_DIR/EXTRACTION_REPORT.md"
    
    print_info "Generating extraction report..."
    
    cat > "$report_file" << EOF
# JAR Files Extraction Report

## Overview

This report documents the extraction of Samsung framework JAR files from the FOTA dependencies package.

## Extraction Details

**Date:** $(date)
**Total JAR Files:** $(find "$JARS_DIR" -maxdepth 1 -type d 2>/dev/null | tail -n +2 | wc -l) directories
**Source:** UN1CA-firmware-dm2q fota-agent-dependencies

## Extracted JAR Files

EOF

    # List all extracted directories
    for dir in "$JARS_DIR"/*/; do
        if [ -d "$dir" ]; then
            local jar_name=$(basename "$dir")
            local file_count=$(find "$dir" -type f 2>/dev/null | wc -l)
            echo "### $jar_name" >> "$report_file"
            echo "- **Files extracted:** $file_count" >> "$report_file"
            
            # Check for specific file types
            local smali_count=$(find "$dir" -name "*.smali" 2>/dev/null | wc -l)
            local xml_count=$(find "$dir" -name "*.xml" 2>/dev/null | wc -l)
            local class_count=$(find "$dir" -name "*.class" 2>/dev/null | wc -l)
            
            if [ $smali_count -gt 0 ]; then
                echo "- **Smali files:** $smali_count" >> "$report_file"
            fi
            if [ $xml_count -gt 0 ]; then
                echo "- **XML files:** $xml_count" >> "$report_file"
            fi
            if [ $class_count -gt 0 ]; then
                echo "- **Class files:** $class_count" >> "$report_file"
            fi
            
            echo "" >> "$report_file"
        fi
    done
    
    cat >> "$report_file" << 'EOF'

## Usage

These extracted JAR files can be analyzed to understand:
- Samsung framework APIs
- Authentication mechanisms
- Network communication protocols
- Security implementations
- Device management interfaces

## Tools for Analysis

1. **Text search:** `grep -r "pattern" .`
2. **Find specific classes:** `find . -name "*ClassName*.smali"`
3. **Count methods:** `grep -r "\.method" . | wc -l`
4. **Find OAuth refs:** `grep -ri "oauth" .`

## Notes

- Some JAR files may contain only compiled classes without resources
- Smali files represent decompiled DEX bytecode
- XML files contain Android resources and manifests
- Failed extractions are logged above

---
Generated by extract_jars.sh
EOF

    print_success "Report generated: $report_file"
}

# Function to search for OAuth/Auth related code
search_auth_code() {
    print_info "Searching for authentication-related code in JARs..."
    
    local search_results="$JARS_DIR/AUTH_SEARCH_RESULTS.txt"
    
    echo "=== Authentication Code Search Results ===" > "$search_results"
    echo "Generated: $(date)" >> "$search_results"
    echo "" >> "$search_results"
    
    # Search for OAuth
    echo "## OAuth References:" >> "$search_results"
    find "$JARS_DIR" -name "*.smali" -exec grep -l "oauth\|OAuth" {} \; 2>/dev/null | \
        sed "s|$JARS_DIR/||" >> "$search_results"
    echo "" >> "$search_results"
    
    # Search for authentication
    echo "## Authentication Classes:" >> "$search_results"
    find "$JARS_DIR" -name "*Auth*.smali" -o -name "*auth*.smali" 2>/dev/null | \
        sed "s|$JARS_DIR/||" | head -50 >> "$search_results"
    echo "" >> "$search_results"
    
    # Search for security
    echo "## Security-related Files:" >> "$search_results"
    find "$JARS_DIR" -name "*Security*.smali" -o -name "*Crypto*.smali" 2>/dev/null | \
        sed "s|$JARS_DIR/||" | head -50 >> "$search_results"
    
    print_success "Search results saved: $search_results"
}

# Main execution
main() {
    print_info "Samsung FOTA JAR Extraction Tool"
    print_info "================================="
    echo ""
    
    # Step 1: Check apktool
    check_apktool
    echo ""
    
    # Step 2: Download FOTA dependencies
    download_fota_deps
    echo ""
    
    # Step 3: Extract FOTA dependencies
    extract_fota_deps
    echo ""
    
    # Step 4: Find framework directory
    find_framework_dir
    echo ""
    
    # Step 5: Extract all JAR files
    extract_all_jars
    echo ""
    
    # Step 6: Generate report
    generate_report
    echo ""
    
    # Step 7: Search for authentication code
    search_auth_code
    echo ""
    
    print_success "All operations completed!"
    print_info "Extracted JARs location: $JARS_DIR"
    print_info "Report: $JARS_DIR/EXTRACTION_REPORT.md"
    print_info "Auth search: $JARS_DIR/AUTH_SEARCH_RESULTS.txt"
}

# Run main function
main "$@"
