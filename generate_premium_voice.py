#!/usr/bin/env python3
"""
GlyphMesh3D Executive Guide - Premium Natural Voice Generation
Uses ElevenLabs for virtually undetectable, professionally natural AI voice
"""

import os
import sys
import subprocess
import json


def get_elevenlabs_key():
    """Get ElevenLabs API key"""
    # Use provided API key
    api_key = "sk_85af02067e384687df6d47c138d029318afeced6ca207bac"
    return api_key


def install_package(package_name):
    """Install a Python package"""
    subprocess.check_call([sys.executable, "-m", "pip", "install", package_name, "-q"])


def generate_with_elevenlabs_premium(api_key):
    """Generate using ElevenLabs Premium voices - virtually indistinguishable from human"""
    
    try:
        import requests
    except ImportError:
        install_package("requests")
        import requests
    
    script_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\EXECUTIVE_GUIDE_SCRIPT.txt"
    output_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\GlyphMesh3D_Executive_Guide.mp3"
    
    # Read the script
    with open(script_path, 'r', encoding='utf-8') as f:
        text = f.read()
    
    print("\n" + "=" * 70)
    print("GENERATING PREMIUM NATURAL VOICE NARRATION")
    print("=" * 70)
    print()
    print("Voice: Custom Professional Voice")
    print("  • Natural, warm, authoritative")
    print("  • Virtually undetectable as AI")
    print("  • Professional presentation quality")
    print()
    print("Processing... this may take 1-2 minutes")
    print()
    
    # ElevenLabs API endpoint
    # Using your selected voice: UmQN7jS1Ee8B1czsUtQh
    voice_id = "UmQN7jS1Ee8B1czsUtQh"
    url = f"https://api.elevenlabs.io/v1/text-to-speech/{voice_id}"
    
    headers = {
        "xi-api-key": api_key,
        "Content-Type": "application/json"
    }
    
    payload = {
        "text": text,
        "model_id": "eleven_monolingual_v1",
        "voice_settings": {
            "stability": 0.45,      # Lower = more natural variation
            "similarity_boost": 0.85 # Higher = more consistent voice
        }
    }
    
    try:
        print("Connecting to ElevenLabs API...")
        response = requests.post(url, json=payload, headers=headers, timeout=120)
        
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
            print(f"\n✗ Invalid API key. Please check your ElevenLabs API key.")
            return None
        elif response.status_code == 429:
            print(f"\n✗ Rate limit reached. Please wait a moment and try again.")
            return None
        else:
            print(f"\n✗ Error from ElevenLabs: {response.status_code}")
            print(f"Message: {response.text}")
            return None
            
    except requests.exceptions.Timeout:
        print("\n✗ Request timed out. Please try again.")
        return None
    except Exception as e:
        print(f"\n✗ Error: {e}")
        return None


if __name__ == "__main__":
    print()
    api_key = get_elevenlabs_key()
    
    if not api_key:
        sys.exit(1)
    
    result = generate_with_elevenlabs_premium(api_key)
    
    if result:
        print("\nYour professional narration is ready to use!")
    else:
        print("\nGeneration failed. Please check your API key and try again.")
        sys.exit(1)
