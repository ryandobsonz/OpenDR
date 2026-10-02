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
using System.Globalization;
using System.IO;
using OpenRA.FileFormats;
using OpenRA.Graphics;
using OpenRA.Mods.Dr.FileFormats;

namespace OpenRA.Mods.Dr.UtilityCommands
{
	/// <summary>Writes a Smacker video's frames as a PNG contact sheet, and its audio as a WAV, to check the decoder.</summary>
	class DumpSmackerCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--dump-smacker";
		bool IUtilityCommand.ValidateArguments(string[] args) => args.Length >= 3;

		[Desc("FILE.SMK", "OUTPUT", "[COLUMNS]", "[SCALE]", "[EVERY]",
			"Writes OUTPUT.png, every EVERY-th frame in COLUMNS columns at 1/SCALE size, and OUTPUT.wav if it has audio.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			var columns = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 8;
			var scale = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 1;
			var every = args.Length > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 1;

			using var video = new SmackerVideo(File.OpenRead(args[1]));
			Console.WriteLine($"{video.Width}x{video.Height}, {video.FrameCount} frames at {video.FramesPerSecond:0.##} fps; " +
				(video.HasAudio ? $"audio {video.AudioChannels} channels, {video.SampleBits} bits, {video.SampleRate} Hz" : "no audio"));

			var shown = (video.FrameCount + every - 1) / every;
			var w = video.Width / scale;
			var h = video.Height / scale;
			var rows = (shown + columns - 1) / columns;
			var sheet = new byte[4 * w * columns * h * rows];
			var stride = w * columns;
			for (var f = 0; f < video.FrameCount; f++)
			{
				if (f > 0)
					video.AdvanceFrame();

				if (f % every != 0)
					continue;

				var cell = f / every;
				var x0 = cell % columns * w;
				var y0 = cell / columns * h;
				for (var y = 0; y < h; y++)
				{
					for (var x = 0; x < w; x++)
					{
						var c = video.Palette[video.Pixels[y * scale * video.Width + x * scale]];
						var o = 4 * ((y0 + y) * stride + x0 + x);
						sheet[o] = c.R;
						sheet[o + 1] = c.G;
						sheet[o + 2] = c.B;
						sheet[o + 3] = 255;
					}
				}
			}

			new Png(sheet, SpriteFrameType.Rgba32, stride, h * rows).Save(args[2] + ".png");

			if (video.HasAudio)
			{
				var audio = video.AudioData;
				using var wav = new BinaryWriter(File.Create(args[2] + ".wav"));
				var blockAlign = video.AudioChannels * video.SampleBits / 8;
				wav.Write("RIFF"u8.ToArray());
				wav.Write(36 + audio.Length);
				wav.Write("WAVEfmt "u8.ToArray());
				wav.Write(16);
				wav.Write((short)1);
				wav.Write((short)video.AudioChannels);
				wav.Write(video.SampleRate);
				wav.Write(video.SampleRate * blockAlign);
				wav.Write((short)blockAlign);
				wav.Write((short)video.SampleBits);
				wav.Write("data"u8.ToArray());
				wav.Write(audio.Length);
				wav.Write(audio);
				Console.WriteLine($"Audio: {audio.Length / blockAlign / (float)video.SampleRate:0.00} s");
			}
		}
	}
}
