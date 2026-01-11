#!/usr/bin/env python3
"""
GlyphMesh3D Executive Guide - Professional AI Voice Generation
Uses ElevenLabs API for high-quality natural voice synthesis
"""

import os
import sys
import subprocess
import requests


def install_package(package_name):
    """Install a Python package"""
    subprocess.check_call([sys.executable, "-m", "pip", "install", package_name])


def generate_with_elevenlabs(api_key=None):
    """Generate audio using ElevenLabs Professional AI Voice"""
    
    if not api_key:
        # Check environment variable
        api_key = os.getenv("ELEVENLABS_API_KEY")
    
    if not api_key:
        print("ElevenLabs API Key not found.")
        print("Options:")
        print("1. Set ELEVENLABS_API_KEY environment variable")
        print("2. Get a free API key at: https://elevenlabs.io/sign-up")
        print("   (Free tier includes 10,000 characters/month)")
        print()
        return None
    
    script_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\EXECUTIVE_GUIDE_SCRIPT.txt"
    output_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\GlyphMesh3D_Executive_Guide.mp3"
    
    # Read the script
    with open(script_path, 'r', encoding='utf-8') as f:
        text = f.read()
    
    print("Generating professional AI voice using ElevenLabs...")
    print("Voice: Onyx (Male - Deep, authoritative, professional)")
    print()
    
    # ElevenLabs API endpoint
    url = "https://api.elevenlabs.io/v1/text-to-speech/onyx"
    
    headers = {
        "xi-api-key": api_key,
        "Content-Type": "application/json"
    }
    
    payload = {
        "text": text,
        "model_id": "eleven_monolingual_v1",
        "voice_settings": {
            "stability": 0.5,
            "similarity_boost": 0.75
        }
    }
    
    try:
        response = requests.post(url, json=payload, headers=headers)
        
        if response.status_code == 200:
            with open(output_path, 'wb') as f:
                f.write(response.content)
            
            file_size_mb = os.path.getsize(output_path) / (1024 * 1024)
            print(f"✓ Professional audio generated successfully!")
            print(f"  Location: {output_path}")
            print(f"  File size: {file_size_mb:.2f} MB")
            return output_path
        else:
            print(f"Error from ElevenLabs: {response.status_code}")
            print(f"Message: {response.text}")
            return None
            
    except Exception as e:
        print(f"Error: {e}")
        return None


def generate_with_azure_neural():
    """Generate audio using Microsoft Azure Neural Voices (high quality)"""
    
    try:
        import edge_tts
    except ImportError:
        print("Installing required package: edge-tts...")
        install_package("edge-tts")
        import edge_tts
    
    import asyncio
    
    script_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\EXECUTIVE_GUIDE_SCRIPT.txt"
    output_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\GlyphMesh3D_Executive_Guide.mp3"
    
    # Read the script
    with open(script_path, 'r', encoding='utf-8') as f:
        text = f.read()
    
    print("Generating professional audio using Azure Neural Voices...")
    print("Voice: Guy (Male - Natural, professional)")
    print()
    
    async def generate():
        # Guy64 is a high-quality male voice
        communicate = edge_tts.Communicate(
            text=text, 
            voice="en-US-GuyNeural",
            rate="-8%"  # Slightly slower for authority
        )
        await communicate.save(output_path)
        return output_path
    
    try:
        asyncio.run(generate())
        file_size_mb = os.path.getsize(output_path) / (1024 * 1024)
        print(f"✓ Professional audio generated successfully!")
        print(f"  Location: {output_path}")
        print(f"  File size: {file_size_mb:.2f} MB")
        return output_path
    except Exception as e:
        print(f"Error: {e}")
        return None


if __name__ == "__main__":
    print("GlyphMesh3D Executive Guide - Professional AI Voice Generator")
    print("=" * 60)
    print()
    
    api_key = os.getenv("ELEVENLABS_API_KEY")
    
    if api_key:
        # Try ElevenLabs first (best quality)
        result = generate_with_elevenlabs(api_key)
        if result:
            print()
            print("✓ Done! Your professional-quality narrated guide is ready.")
            sys.exit(0)
    
    # Fallback to Azure Neural Voices (still professional quality, free)
    print("Using Azure Neural Voices for professional quality...")
    print()
    result = generate_with_azure_neural()
    
    if result:
        print()
        print("✓ Done! Your professional-quality narrated guide is ready.")
        print()
        print("To upgrade to ElevenLabs (even higher quality):")
        print("  1. Sign up free at: https://elevenlabs.io")
        print("  2. Get your API key from dashboard")
        print("  3. Run: $env:ELEVENLABS_API_KEY = 'your-api-key'")
        print("  4. Re-run this script for premium voice quality")
    else:
        print()
        print("✗ Generation failed. Please ensure internet connection.")
        sys.exit(1)
