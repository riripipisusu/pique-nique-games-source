# Son du lancer de roulette, tire de freesound_community-roulette_casino_evianaif-14446.mp3 (Pixabay) :
#   0-17 s roulement (voix a 17.0-17.6 s), 20.0-21.7 s chute dans la case, voix a 22-24 s.
# On garde le lancer, on raccourcit le roulement a 4.4 s (moment ou la bille tombe dans l'animation), puis la chute.
#   python roulette_sfx.py <source.mp3> <sortie.wav>
import subprocess, sys, wave, numpy as np
SR = 48000
raw = subprocess.run(["ffmpeg", "-v", "quiet", "-i", sys.argv[1], "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"], capture_output=True).stdout
x = np.frombuffer(raw, np.float32).astype(np.float64)
seg = lambda a, b: x[int(a * SR):int(b * SR)].copy()

def xfade(a, b, dur):
    n = int(dur * SR); r = np.linspace(0, 1, n)
    return np.concatenate([a[:-n], a[-n:] * np.sqrt(1 - r) + b[:n] * np.sqrt(r), b[n:]])

start = seg(0.15, 3.4)                  # lancer + roulement rapide
slow = seg(12.9, 14.6)                  # roulement lent de la fin, avant la voix
roll = xfade(start, slow, 0.35)[:int(4.4 * SR)]
roll[-int(0.15 * SR):] *= np.linspace(1, 0.4, int(0.15 * SR))
drop = seg(19.97, 21.75)
drop[-int(0.2 * SR):] *= np.linspace(1, 0, int(0.2 * SR))
out = np.concatenate([roll, drop])
out[:int(0.01 * SR)] *= np.linspace(0, 1, int(0.01 * SR))
out = out / np.abs(out).max() * 0.9
with wave.open(sys.argv[2], "wb") as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes((out * 32767).astype(np.int16).tobytes())
print(f"{len(out) / SR:.2f} s, chute a {len(roll) / SR:.2f} s")
