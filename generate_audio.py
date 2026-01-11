#!/usr/bin/env python3
"""
GlyphMesh3D Executive Guide - Text to Speech Generator
Generates an MP3 audio file from the narration script
Uses edge-tts for high-quality natural voice synthesis
"""

import os
import sys
import subprocess
import asyncio


def install_package(package_name):
    """Install a Python package"""
    subprocess.check_call([sys.executable, "-m", "pip", "install", package_name])


def generate_audio_with_edge_tts():
    """Generate audio using edge-tts (Microsoft Azure high-quality voices)"""
    
    try:
        import edge_tts
    except ImportError:
        print("Installing required package: edge-tts...")
        install_package("edge-tts")
        import edge_tts
    
    script_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\EXECUTIVE_GUIDE_SCRIPT.txt"
    output_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\GlyphMesh3D_Executive_Guide.mp3"
    
    # Read the script
    with open(script_path, 'r', encoding='utf-8') as f:
        text = f.read()
    
    # Use a strong male voice
    # en-US-ArthurNeural = Strong, confident male voice (great for executive presentations)
    voice = "en-US-ArthurNeural"
    rate = "-10%"  # Slightly slower for authority
    
    async def main():
        communicate = edge_tts.Communicate(text=text, voice=voice, rate=rate)
        await communicate.save(output_path)
        print(f"✓ Audio generated successfully: {output_path}")
        return output_path
    
    # Run the async function
    asyncio.run(main())
    return output_path


def generate_audio_offline():
    """Fallback: Generate audio using pyttsx3 (offline, no API key needed)"""
    
    try:
        import pyttsx3
    except ImportError:
        print("Installing required package: pyttsx3")
        install_package("pyttsx3")
        import pyttsx3
    
    script_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\EXECUTIVE_GUIDE_SCRIPT.txt"
    output_path = r"c:\Users\Ruined Laptop\Documents\GlyphMesh3D\GlyphMesh3D_Executive_Guide.mp3"
    
    # Read the script
    with open(script_path, 'r', encoding='utf-8') as f:
        text = f.read()
    
    # Initialize the engine
    engine = pyttsx3.init()
    
    # Set properties for a strong male voice
    engine.setProperty('rate', 140)  # Speed (words per minute)
    engine.setProperty('volume', 0.9)  # Volume (0.0 to 1.0)
    
    # List available voices and select male voice
    voices = engine.getProperty('voices')
    print(f"Available voices: {len(voices)}")
    
    # Try to find and use a male voice (index 0 is usually male on Windows)
    if len(voices) > 0:
        male_voice_id = voices[0].id  # Default to first voice (typically male)
        for voice in voices:
            if 'male' in str(voice.name).lower():
                male_voice_id = voice.id
                break
        engine.setProperty('voice', male_voice_id)
        print(f"Using voice: {engine.getProperty('voice')}")
    
    # Save to file
    print("Converting text to speech...")
    engine.save_to_file(text, output_path)
    engine.runAndWait()
    
    print(f"✓ Audio generated successfully: {output_path}")
    return output_path


if __name__ == "__main__":
    print("GlyphMesh3D Executive Guide - Audio Generator")
    print("=" * 50)
    print()
    
    print("Generating high-quality audio using Microsoft Azure Neural voices...")
    print("(This may take 30-60 seconds on first run...)")
    print()
    
    try:
        generate_audio_with_edge_tts()
        print()
        print("✓ Done! Your narrated guide is ready.")
        print()
        print("Voice: en-US-ArthurNeural (Strong, confident male voice)")
    except Exception as e:
        print(f"Edge-TTS failed: {e}")
        print()
        print("Falling back to offline text-to-speech...")
        try:
            generate_audio_offline()
            print()
            print("✓ Done! Your narrated guide is ready.")
        except Exception as e2:
            print(f"Error: {e2}")
            print()
            print("Troubleshooting:")
            print("- Make sure you have internet connection for edge-tts")
            print("- Or ensure pyttsx3 is installed: pip install pyttsx3")
