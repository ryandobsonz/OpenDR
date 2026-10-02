#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OpenRA.Graphics;
using OpenRA.Mods.Dr.FileFormats;
using OpenRA.Primitives;

namespace OpenRA.Mods.Dr.Graphics
{
	public enum DrShellVideoSound { None, Effects, Video }

	/// <summary>
	/// A Smacker video playing in the shell: each frame is enlarged by whole factors with nearest neighbour,
	/// as the still art is, so it meets that art without a change in sharpness. The movies, a quarter of the
	/// screen, are only filtered: enlarged by whole factors their dithering shows as blocks. Time runs from
	/// when the sound starts; a clip that loops starts again at its end, and one that does not holds its
	/// last frame.
	/// </summary>
	public sealed class DrShellVideo : IDisposable
	{
		readonly SmackerVideo video;
		readonly int scale;
		readonly bool loop;
		readonly bool[] mask;
		readonly Sheet sheet;
		readonly byte[] buffer;
		readonly Stopwatch clock = new();
		readonly Task<byte[]> audio;
		readonly DrShellVideoSound sound;

		bool audioStarted;
		bool soundStopped;
		bool skipped;

		/// <summary>The current frame, drawn at the video's size times its pixel scale in shell pixels.</summary>
		public Sprite Sprite { get; }

		/// <summary>Its size in the 640x480 shell.</summary>
		public int2 Size { get; }

		/// <summary>True once a clip that does not loop has shown its last frame for its full time.</summary>
		public bool Finished => skipped || Ended;

		bool Ended => !loop && Elapsed >= video.FrameCount / video.FramesPerSecond;

		/// <summary>Opens a video from the mod's file system, or returns null if it is not there or is not one.</summary>
		public static DrShellVideo Open(string path, int upscale, bool loop = false, DrShellVideoSound sound = DrShellVideoSound.None, int pixelScale = 1, bool circle = false)
		{
			if (!Game.ModData.DefaultFileSystem.TryOpen(path, out var stream))
				return null;

			try
			{
				// Long videos read their audio apart, on another thread, from a second copy of the stream.
				Stream audioStream = null;
				if (sound != DrShellVideoSound.None && stream.Length > 4 << 20)
					Game.ModData.DefaultFileSystem.TryOpen(path, out audioStream);

				return new DrShellVideo(new SmackerVideo(stream), audioStream, upscale, loop, sound, pixelScale, circle);
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Could not play {path}: {e.Message}");
				stream.Dispose();
				return null;
			}
		}

		DrShellVideo(SmackerVideo video, Stream audioStream, int upscale, bool loop, DrShellVideoSound sound, int pixelScale, bool circle)
		{
			this.video = video;
			this.loop = loop;
			this.sound = sound;

			// The movies are footage, not pixel art: they are drawn from their own pixels, filtered.
			scale = pixelScale > 1 ? 1 : Math.Max(1, upscale);
			Size = new int2(video.Width * pixelScale, video.Height * pixelScale);

			var w = video.Width * scale;
			var h = video.Height * scale;
			sheet = DrShellArt.NewSheet(w, h);
			buffer = new byte[4 * sheet.Size.Width * sheet.Size.Height];
			sheet.GetTexture().ScaleFilter = TextureScaleFilter.Linear;
			Sprite = new Sprite(sheet, new Rectangle(0, 0, w, h), TextureChannel.RGBA, (float)pixelScale / scale);

			if (circle)
			{
				// The key's clips are square; only its disc is drawn, so the art around it shows.
				mask = new bool[video.Width * video.Height];
				var r = Math.Min(video.Width, video.Height) / 2f;
				for (var y = 0; y < video.Height; y++)
				{
					for (var x = 0; x < video.Width; x++)
					{
						var dx = x + 0.5f - video.Width / 2f;
						var dy = y + 0.5f - video.Height / 2f;
						mask[y * video.Width + x] = dx * dx + dy * dy <= r * r;
					}
				}
			}

			if (sound != DrShellVideoSound.None && video.HasAudio && !Game.Sound.DummyEngine)
			{
				if (audioStream != null)
					audio = Task.Run(() =>
					{
						using var v = new SmackerVideo(audioStream);
						return v.AudioData;
					});
				else
					audio = Task.FromResult(video.AudioData);
			}

			Upload();
		}

		float Elapsed => (float)clock.Elapsed.TotalSeconds;

		/// <summary>Starts the clock; a video with sound starts once its sound is ready.</summary>
		public void Play()
		{
			if (audio == null)
				clock.Start();
		}

		/// <summary>Brings the frame up to the time, decoding those between.</summary>
		public void Update()
		{
			if (skipped)
				return;

			if (audio != null && !audioStarted && audio.IsCompleted)
			{
				audioStarted = true;
				if (audio.IsCompletedSuccessfully && audio.Result is { Length: > 0 } data)
				{
					// The engine starts a video's sound at the effects volume.
					Game.Sound.PlayVideo(data, video.AudioChannels, video.SampleBits, video.SampleRate);
					if (sound == DrShellVideoSound.Video)
						Game.Sound.VideoVolume = Game.Sound.VideoVolume;
				}

				clock.Start();
			}

			var frame = (int)(Elapsed * video.FramesPerSecond);
			if (frame >= video.FrameCount)
			{
				if (loop)
				{
					clock.Restart();
					frame = 0;
				}
				else
					frame = video.FrameCount - 1;
			}

			if (frame == video.CurrentFrameIndex)
				return;

			video.SeekTo(frame);
			Upload();
		}

		/// <summary>Jumps to the last frame, as a skipped clip ends.</summary>
		public void End()
		{
			if (skipped)
				return;

			skipped = true;
			StopSound();
			video.SeekTo(video.FrameCount - 1);
			Upload();
		}

		void Upload()
		{
			var w = video.Width;
			var h = video.Height;
			var stride = sheet.Size.Width;
			var pixels = video.Pixels;
			var palette = video.Palette;
			Span<uint> colours = stackalloc uint[256];
			for (var i = 0; i < 256; i++)
				colours[i] = 0xFF000000u | ((uint)palette[i].R << 16) | ((uint)palette[i].G << 8) | palette[i].B;

			var target = MemoryMarshal.Cast<byte, uint>(buffer.AsSpan());
			var k = scale;
			for (var y = 0; y < h; y++)
			{
				var row = target.Slice(y * k * stride, w * k);
				var s = y * w;
				for (var x = 0; x < w; x++)
				{
					var c = mask == null || mask[s + x] ? colours[pixels[s + x]] : 0u;
					row.Slice(x * k, k).Fill(c);
				}

				for (var dy = 1; dy < k; dy++)
					row.CopyTo(target.Slice((y * k + dy) * stride, w * k));
			}

			sheet.GetTexture().SetData(buffer, sheet.Size.Width, sheet.Size.Height);
		}

		/// <summary>
		/// Stops its sound when the clip is cut short. The engine plays one video sound at a time and stops
		/// whichever is playing, so a clip that has ended leaves the sound alone: it may be the next clip's.
		/// </summary>
		void StopSound()
		{
			if (audioStarted && audio?.IsCompletedSuccessfully == true && !soundStopped && !Ended)
			{
				soundStopped = true;
				Game.Sound.StopVideo();
			}
		}

		public void Dispose()
		{
			StopSound();
			sheet.Dispose();
			video.Dispose();
		}
	}
}
