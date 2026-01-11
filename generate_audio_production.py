#!/usr/bin/env python3
import os
import sys
import subprocess

try:
    import requests
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "requests", "-q"])
    import requests

api_key = "sk_85af02067e384687df6d47c138d029318afeced6ca207bac"
voice_id = "3nDq4c7a9Pk3q5rxbMJH"

script_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\EXECUTIVE_GUIDE_SCRIPT_CLEAN.txt"
output_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\GlyphMesh3D_Executive_Guide.mp3"

with open(script_path, 'r', encoding='utf-8') as f:
    text = f.read()

url = f"https://api.elevenlabs.io/v1/text-to-speech/{voice_id}"

headers = {
    "xi-api-key": api_key,
    "Content-Type": "application/json"
}

payload = {
    "text": text,
    "model_id": "eleven_turbo_v2_5",
    "voice_settings": {
        "stability": 0.45,
        "similarity_boost": 0.85
    },
    "speed": 1.0
}

response = requests.post(url, json=payload, headers=headers, timeout=300)

if response.status_code == 200:
    with open(output_path, 'wb') as f:
        f.write(response.content)
    sys.exit(0)
else:
    sys.exit(1)
