#!/usr/bin/env python3
"""
GlyphMesh3D Executive Guide - Text to Speech Generator
Using gtts (Google Text-to-Speech) - simpler and more reliable
"""

import os
import sys
import subprocess


def install_package(package_name):
    """Install a Python package"""
    subprocess.check_call([sys.executable, "-m", "pip", "install", package_name])


def generate_audio_with_gtts():
    """Generate audio using Google Text-to-Speech"""
    
    try:
        from gtts import gTTS
    except ImportError:
        print("Installing required package: gtts...")
        install_package("gtts")
        from gtts import gTTS
    
    script_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\EXECUTIVE_GUIDE_SCRIPT.txt"
    output_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\GlyphMesh3D_Executive_Guide.mp3"
    
    # Read the script
    with open(script_path, 'r', encoding='utf-8') as f:
        text = f.read()
    
    print("Generating high-quality audio from text...")
    
    # Create gTTS object with slow speech for authority
    tts = gTTS(text=text, lang='en', slow=False, tld='com')
    
    # Save to file
    tts.save(output_path)
    
    print(f"✓ Audio generated successfully: {output_path}")
    
    # Check file size
    file_size = os.path.getsize(output_path)
    file_size_mb = file_size / (1024 * 1024)
    print(f"  File size: {file_size_mb:.2f} MB")
    
    return output_path


if __name__ == "__main__":
    print("GlyphMesh3D Executive Guide - Audio Generator")
    print("=" * 50)
    print()
    
    print("Generating audio using Google Text-to-Speech...")
    print("(This requires internet connection)")
    print("(First run may take 1-2 minutes...)")
    print()
    
    try:
        generate_audio_with_gtts()
        print()
        print("✓ Done! Your narrated guide is ready to listen.")
        print()
        print("Location: c:\\Users\\Ruined Laptop\\Documents\\GlyphMesh3D\\GlyphMesh3D_Executive_Guide.mp3")
    except Exception as e:
        print(f"Error: {e}")
        print()
        print("Troubleshooting:")
        print("- Make sure you have an internet connection")
        print("- Try reinstalling gtts: pip install gtts --upgrade")
        sys.exit(1)
