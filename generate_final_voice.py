#!/usr/bin/env python3
"""
GlyphMesh3D Executive Guide - Premium Natural Voice Generation
Uses ElevenLabs with provided API key and voice
"""

import os
import sys
import subprocess


def install_package(package_name):
    """Install a Python package"""
    subprocess.check_call([sys.executable, "-m", "pip", "install", package_name, "-q"])


def generate_with_elevenlabs_premium():
    """Generate using ElevenLabs with your selected voice"""
    
    try:
        import requests
    except ImportError:
        print("Installing requests package...")
        install_package("requests")
        import requests
    
    # Your credentials
    api_key = "sk_85af02067e384687df6d47c138d029318afeced6ca207bac"
    voice_id = "UmQN7jS1Ee8B1czsUtQh"
    
    script_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\EXECUTIVE_GUIDE_SCRIPT.txt"
    output_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\GlyphMesh3D_Executive_Guide.mp3"
    
    # Read the script
    with open(script_path, 'r', encoding='utf-8') as f:
        text = f.read()
    
    print("=" * 70)
    print("GENERATING PREMIUM NATURAL VOICE NARRATION")
    print("=" * 70)
    print()
    print("Voice: Your Selected Premium Voice")
    print("  • Natural, warm, professional")
    print("  • Virtually undetectable as AI")
    print("  • Studio-quality narration")
    print()
    print("Processing... this may take 1-2 minutes")
    print()
    
    # ElevenLabs API endpoint with your voice ID
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
        }
    }
    
    try:
        print("Connecting to ElevenLabs API...")
        response = requests.post(url, json=payload, headers=headers, timeout=300)
        
        if response.status_code == 200:
            print("Downloading audio... ", end="", flush=True)
            with open(output_path, 'wb') as f:
                f.write(response.content)
            
            file_size_mb = os.path.getsize(output_path) / (1024 * 1024)
            print("✓")
            print()
            print("=" * 70)
            print("✓ PREMIUM VOICE GENERATION COMPLETE")
            print("=" * 70)
            print()
            print(f"Output: {output_path}")
            print(f"Size:   {file_size_mb:.2f} MB")
            print()
            print("QUALITY: Studio-grade, professional narration")
            print("LISTENER EXPERIENCE: Virtually indistinguishable from human voice")
            print()
            return output_path
            
        elif response.status_code == 401:
            print(f"\n✗ Authentication failed: Invalid API key or expired credentials")
            print(f"Response: {response.text}")
            return None
        elif response.status_code == 429:
            print(f"\n✗ Rate limit reached. Your account has used its monthly character limit.")
            print(f"Please upgrade at: https://elevenlabs.io/pricing")
            return None
        elif response.status_code == 400:
            print(f"\n✗ Bad request: {response.text}")
            return None
        else:
            print(f"\n✗ Error from ElevenLabs: {response.status_code}")
            print(f"Response: {response.text}")
            return None
            
    except requests.exceptions.Timeout:
        print("\n✗ Request timed out. Please try again.")
        return None
    except requests.exceptions.ConnectionError:
        print("\n✗ Connection error. Please check your internet connection.")
        return None
    except Exception as e:
        print(f"\n✗ Error: {e}")
        return None


if __name__ == "__main__":
    print()
    result = generate_with_elevenlabs_premium()
    
    if result:
        print("\n✓ Your professional narration is ready to use!")
    else:
        print("\n✗ Generation failed. Please check your API key and try again.")
        sys.exit(1)
