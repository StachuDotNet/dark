#!/bin/bash

# reMarkable Darklang EPUB Upload Script
TOKEN="eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6IlF6QTBPVVZDUXpORk1FRTRSRVkzTWtWQlJrSTNNemRDTjBZME5EVTRSall5T0VWRE9FRTBRZyJ9.eyJodHRwczovL2F1dGgucmVtYXJrYWJsZS5jb20vdGVjdG9uaWMiOiJldSIsImh0dHBzOi8vYXV0aC5yZW1hcmthYmxlLmNvbS9jcmVhdGVkX2F0IjoiMjAxOS0xMi0xOVQxODoxNTozNC4wNjBaIiwiaHR0cHM6Ly9hdXRoLnJlbWFya2FibGUuY29tL2VtYWlsIjoic3RhY2h1a29yaWNrQGdtYWlsLmNvbSIsImh0dHBzOi8vYXV0aC5yZW1hcmthYmxlLmNvbS9lbWFpbF92ZXJpZmllZCI6dHJ1ZSwiaHR0cHM6Ly9hdXRoLnJlbWFya2FibGUuY29tL3N1YnNjcmlwdGlvbiI6ImNvbm5lY3QiLCJodHRwczovL2F1dGgucmVtYXJrYWJsZS5jb20vbGFzdF9sb2dpbiI6IjIwMjUtMDktMTVUMTg6MzA6MjUuNDc4WiIsImlzcyI6Imh0dHBzOi8vYXV0aC5yZW1hcmthYmxlLmNvbS8iLCJzdWIiOiJnb29nbGUtb2F1dGgyfDExMzQ0Nzc1MzgzODM5MDk5NDY4NSIsImF1ZCI6WyJodHRwczovL3dlYi5jbG91ZC5yZW1hcmthYmxlLmNvbSIsImh0dHBzOi8vcmVtYXJrYWJsZS5ldS5hdXRoMC5jb20vdXNlcmluZm8iXSwiaWF0IjoxNzU4MTE5NjUyLCJleHAiOjE3NTgyMDYwNTIsInNjb3BlIjoib3BlbmlkIHByb2ZpbGUgZW1haWwiLCJhenAiOiJXODhuRDVQaVRxYTVYOUJhQjI5cm1pbGxlMFc4MDJmSyIsInBlcm1pc3Npb25zIjpbXX0.YFTQShZ89Dkacz7gQH0xHTAlvnSYXXxElmpmgv0S8DGLWvlhkrORa8K1vVWfIfleJX0m0MhRIy2WVZMCSjOWp1XjMEpm8JRE2Uiri-AZIDqTDsuh3pF2xNiXW09AQ9mL6No2P-1eeTrZb6ZQjVtFDHit0OMJbo61Z4todNyuMs1rLN5yGQHqQigEbVCJ5skJGUqSqy6NbwkKs0wi3VyeouDY9R1z07jt-W_EglWOBzjT9bKG7v5Pun0IP34sM567SpO8r_V8OeHLfByaVXg12ioBW5hxArVaGidoUtRporE7vnPry5VCVgp54sKFoSB5GxeGpwsobzzsuyE9g_zZgA"

UPLOAD_URL="https://web.eu.tectonic.remarkable.com/doc/v2/files"

# List of Darklang EPUBs in reading order
epubs=(
    "01-Minimal-Ops-Design.epub"
    "02-VS-Code-Bridge-Analysis.epub"
    "03-Virtual-File-URL-Design.epub"
    "04-Structured-Types-Schema.epub"
    "05-Code-Design-Summary.epub"
    "06-VS-Code-Design-Summary.epub"
    "07-VS-Code-Features-Summary.epub"
    "08-CLI-Support-Summary.epub"
    "09-Complete-Ops-Analysis.epub"
    "10-DB-Schema-Analysis.epub"
    "11-Package-Search-Scenarios.epub"
)

# Function to create base64 encoded metadata
create_metadata() {
    local filename="$1"
    local basename=$(basename "$filename" .epub)
    local json="{\"parent\":\"\",\"file_name\":\"$basename\"}"
    echo -n "$json" | base64 -w 0
}

# Function to upload a single EPUB
upload_epub() {
    local filepath="$1"
    local filename=$(basename "$filepath")
    local file_size=$(stat -c%s "$filepath")
    local metadata=$(create_metadata "$filename")

    echo "[$((++count))/11] Uploading: $filename ($file_size bytes)"

    # Upload the EPUB
    response=$(curl "$UPLOAD_URL" \
        -X 'POST' \
        -H 'accept: */*' \
        -H 'accept-language: en-US,en;q=0.9' \
        -H "authorization: Bearer $TOKEN" \
        -H "content-length: $file_size" \
        -H 'content-type: application/epub+zip' \
        -H 'origin: https://my.remarkable.com' \
        -H 'priority: u=1, i' \
        -H "rm-meta: $metadata" \
        -H 'rm-source: WebLibrary' \
        -H 'sec-ch-ua: "Not;A=Brand";v="99", "Google Chrome";v="139", "Chromium";v="139"' \
        -H 'sec-ch-ua-mobile: ?0' \
        -H 'sec-ch-ua-platform: "Linux"' \
        -H 'sec-fetch-dest: empty' \
        -H 'sec-fetch-mode: cors' \
        -H 'sec-fetch-site: same-site' \
        -H 'user-agent: Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36' \
        --data-binary "@$filepath" \
        --write-out "HTTP_CODE:%{http_code}" \
        --silent)

    # Check response
    http_code=$(echo "$response" | grep -o 'HTTP_CODE:[0-9]*' | cut -d: -f2)

    if [ "$http_code" = "200" ] || [ "$http_code" = "201" ]; then
        echo "  ✓ $filename uploaded successfully"
        ((success_count++))
    else
        echo "  ✗ $filename upload failed (HTTP $http_code)"
        ((failed_count++))
    fi

    # Be nice to the server
    sleep 2
}

echo "🚀 UPLOADING DARKLANG DESIGN DOCS TO REMARKABLE CLOUD"
echo "===================================================="
echo ""



count=0
success_count=0
failed_count=0

echo "Found $(ls -1 *.epub 2>/dev/null | wc -l) EPUB files to upload"
echo ""

# Confirm before proceeding
read -p "🤔 This will upload 11 Darklang design EPUBs to your reMarkable account. Continue? (y/N): " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "Upload cancelled."
    exit 1
fi

echo ""
echo "Starting uploads..."
echo ""

# Upload each EPUB
for epub in "${epubs[@]}"; do
    if [ -f "$epub" ]; then
        upload_epub "$epub"
    else
        echo "  ⚠ $epub not found, skipping"
        ((failed_count++))
    fi
done

echo ""
echo "📊 UPLOAD SUMMARY"
echo "=================="
echo "✅ Successful uploads: $success_count"
echo "❌ Failed uploads: $failed_count"
echo "📚 Total processed: $((success_count + failed_count))"

if [ $success_count -gt 0 ]; then
    echo ""
    echo "🎉 Check your reMarkable library - your Darklang design docs should appear shortly!"
    echo "📱 They'll sync to your reMarkable automatically."
    echo ""
    echo "📖 Recommended reading order:"
    echo "   Phase 1: Core Concepts (01-04)"
    echo "   Phase 2: Implementation (05-08)"
    echo "   Phase 3: Validation (09-11)"
fi

if [ $failed_count -gt 0 ]; then
    echo ""
    echo "⚠️ Some uploads failed. You may need to:"
    echo "   - Check your internet connection"
    echo "   - Verify your token is still valid"
    echo "   - Try uploading failed files manually"
fi