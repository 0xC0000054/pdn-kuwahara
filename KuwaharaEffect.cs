////////////////////////////////////////////////////////////////////////////////
//
// This file is part of pdn-kuwahara, a Kuwahara noise reduction Effect
// plugin for Paint.NET.
//
// Copyright (c) 2017 Nicholas Hayes
//
// This file is licensed under the MIT License.
// See LICENSE.txt for complete licensing and attribution information.
//
////////////////////////////////////////////////////////////////////////////////

// Adapted from the Public Domain licensed
// Kuwahara Filter for ImageJ
// https://imagej.nih.gov/ij/plugins/kuwahara.html

// The Kuwahara Filter is a noise-reduction filter that preserves edges.
//
//  a  a  ab   b  b
//	a  a  ab   b  b
//	ac ac abcd bd bd
//	c  c  cd   d  d
//	c  c  cd   d  d
//
//	In the case of a 5x5 sampling window, the mean brightness and the
//	variance of each of the four 3x3 regions (a, b, c, d), are calculated
//	and the value of the center pixel (abcd) is set to the mean value
//	of the region that with the smallest variance.
//
//	Description based on the one at:
//	http://www.incx.nec.co.jp/imap-vision/library/wouter/kuwahara.html

using Kuwahara.Properties;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Kuwahara
{
    [PluginSupportInfo(typeof(PluginSupportInfo))]
    public sealed class KuwaharaEffect : PropertyBasedEffect
    {
        private bool useRgbChannels;
        private int kernelSize;
        private int kernelOffset;
        private float[,] redMean;
        private float[,] redVariance;
        private float[,] greenMean;
        private float[,] greenVariance;
        private float[,] blueMean;
        private float[,] blueVariance;
        private float[,] intensityMean;
        private float[,] intensityVariance;

        private static readonly int[] IntensityLookupTable = PopulateIntensityLookupTable();

        private static int[] PopulateIntensityLookupTable()
        {
            // Create a lookup table to map the highest RGB color channel value to its HSV value.
            int[] table = new int[256];

            for (int i = 0; i < table.Length; i++)
            {
                double value = i / 255.0;

                table[i] = (int)DoubleUtil.Clamp(value * 100.0, 0.0, 100.0);
            }

            return table;
        }

        internal static string StaticName
        {
            get
            {
                return "Kuwahara";
            }
        }

        internal static Image StaticImage
        {
            get
            {
                return new Bitmap(typeof(KuwaharaEffect), "icon.png");
            }
        }

        public KuwaharaEffect() : base(StaticName, StaticImage, SubmenuNames.Noise, EffectFlags.Configurable)
        {
        }

        protected override PropertyCollection OnCreatePropertyCollection()
        {
            List<Property> props = new List<Property>
            {
                new Int32Property(PropertyNames.Radius, 7, 3, 199),
                new BooleanProperty(PropertyNames.UseRgbChannels, true)
            };

            return new PropertyCollection(props);
        }

        protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
        {
            ControlInfo info = CreateDefaultConfigUI(props);

            info.SetPropertyControlValue(PropertyNames.Radius, ControlInfoPropertyNames.DisplayName, Resources.RadiusDisplayName);
            info.SetPropertyControlValue(PropertyNames.UseRgbChannels, ControlInfoPropertyNames.DisplayName, string.Empty);
            info.SetPropertyControlValue(PropertyNames.UseRgbChannels, ControlInfoPropertyNames.Description, Resources.UseRGBChannelsDescription);

            return info;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            int radius = newToken.GetProperty<Int32Property>(PropertyNames.Radius).Value;
            this.useRgbChannels = Token.GetProperty<BooleanProperty>(PropertyNames.UseRgbChannels).Value;

            // The sliding window size is normally an odd number,
            // but as the user can enter an even number handle that case.
            if ((radius & 1) == 1)
            {
                this.kernelSize = (radius + 1) / 2;
                this.kernelOffset = (radius - 1) / 2;
            }
            else
            {
                this.kernelSize = radius / 2;
                this.kernelOffset = (radius - 2) / 2;
            }

            Surface src = srcArgs.Surface;

            int arrayWidth = src.Width + this.kernelOffset;
            int arrayHeight = src.Height + this.kernelOffset;

            if (this.useRgbChannels)
            {
                this.redMean = new float[arrayWidth, arrayHeight];
                this.redVariance = new float[arrayWidth, arrayHeight];
                this.greenMean = new float[arrayWidth, arrayHeight];
                this.greenVariance = new float[arrayWidth, arrayHeight];
                this.blueMean = new float[arrayWidth, arrayHeight];
                this.blueVariance = new float[arrayWidth, arrayHeight];
            }
            else
            {
                this.intensityMean = new float[arrayWidth, arrayHeight];
                this.intensityVariance = new float[arrayWidth, arrayHeight];
            }

            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
        }

        protected override unsafe void OnRender(Rectangle[] renderRects, int startIndex, int length)
        {
            Surface src = SrcArgs.Surface;
            Surface dest = DstArgs.Surface;

            for (int i = startIndex; i < length; i++)
            {
                Rectangle rect = renderRects[i];

                if (this.useRgbChannels)
                {
                    RenderRGB(src, dest, rect);
                }
                else
                {
                    RenderIntensity(src, dest, rect);
                }
            }
        }

        private unsafe void RenderRGB(Surface src, Surface dest, Rectangle rect)
        {
            CalculateMeanAndVarianceRGB(src, rect);

            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                if (IsCancelRequested)
                {
                    return;
                }

                ColorBgra* p = dest.GetPointAddressUnchecked(rect.Left, y);

                for (int x = rect.Left; x < rect.Right; x++)
                {
                    int xbase = x;
                    int ybase = y;
                    int xbase2 = 0;
                    int ybase2 = 0;

                    float minR = float.MaxValue;
                    float minG = float.MaxValue;
                    float minB = float.MaxValue;

                    float varR = this.redVariance[xbase, ybase];
                    float varG = this.greenVariance[xbase, ybase];
                    float varB = this.blueVariance[xbase, ybase];

                    if (varR < minR)
                    {
                        minR = varR;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }
                    if (varG < minG)
                    {
                        minG = varG;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }
                    if (varB < minB)
                    {
                        minB = varB;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }

                    xbase = x + this.kernelOffset;
                    varR = this.redVariance[xbase, ybase];
                    varG = this.greenVariance[xbase, ybase];
                    varB = this.blueVariance[xbase, ybase];

                    if (varR < minR)
                    {
                        minR = varR;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }
                    if (varG < minG)
                    {
                        minG = varG;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }
                    if (varB < minB)
                    {
                        minB = varB;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }

                    ybase = y + this.kernelOffset;
                    varR = this.redVariance[xbase, ybase];
                    varG = this.greenVariance[xbase, ybase];
                    varB = this.blueVariance[xbase, ybase];

                    if (varR < minR)
                    {
                        minR = varR;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }
                    if (varG < minG)
                    {
                        minG = varG;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }
                    if (varB < minB)
                    {
                        minB = varB;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }

                    xbase = x;
                    varR = this.redVariance[xbase, ybase];
                    varG = this.greenVariance[xbase, ybase];
                    varB = this.blueVariance[xbase, ybase];

                    if (varR < minR)
                    {
                        minR = varR;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }
                    if (varG < minG)
                    {
                        minG = varG;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }
                    if (varB < minB)
                    {
                        minB = varB;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }

                    p->R = (byte)FloatUtil.Clamp(this.redMean[xbase2, ybase2] + 0.5f, 0, 255);
                    p->G = (byte)FloatUtil.Clamp(this.greenMean[xbase2, ybase2] + 0.5f, 0, 255);
                    p->B = (byte)FloatUtil.Clamp(this.blueMean[xbase2, ybase2] + 0.5f, 0, 255);

                    p++;
                }
            }
        }

        private unsafe void CalculateMeanAndVarianceRGB(Surface src, Rectangle roi)
        {
            int top = roi.Top;
            int bottom = roi.Bottom;
            int left = roi.Left;
            int right = roi.Right;

            for (int y = top - this.kernelOffset; y < bottom; y++)
            {
                if (IsCancelRequested)
                {
                    return;
                }

                for (int x = left - this.kernelOffset; x < right; x++)
                {
                    float accumulatedR = 0;
                    float accumulatedRSquared = 0;
                    float accumulatedG = 0;
                    float accumulatedGSquared = 0;
                    float accumulatedB = 0;
                    float accumulatedBSquared = 0;
                    float pixelCount = 0;

                    int blockTop = y;
                    if (blockTop < 0)
                    {
                        blockTop = 0;
                    }
                    int blockBottom = y + this.kernelSize;
                    if (blockBottom >= src.Height)
                    {
                        blockBottom = src.Height;
                    }
                    int blockLeft = x;
                    if (blockLeft < 0)
                    {
                        blockLeft = 0;
                    }
                    int blockRight = x + this.kernelSize;
                    if (blockRight >= src.Width)
                    {
                        blockRight = src.Width;
                    }

                    for (int y2 = blockTop; y2 < blockBottom; y2++)
                    {
                        ColorBgra* p = src.GetPointAddressUnchecked(blockLeft, y2);

                        for (int x2 = blockLeft; x2 < blockRight; x2++)
                        {
                            accumulatedR += p->R;
                            accumulatedRSquared += p->R * p->R;

                            accumulatedG += p->G;
                            accumulatedGSquared += p->G * p->G;

                            accumulatedB += p->B;
                            accumulatedBSquared += p->B * p->B;

                            pixelCount++;
                            p++;
                        }
                    }

                    int xIndex = x + this.kernelOffset;
                    int yIndex = y + this.kernelOffset;

                    this.redMean[xIndex, yIndex] = accumulatedR / pixelCount;
                    this.redVariance[xIndex, yIndex] = accumulatedRSquared - accumulatedR * accumulatedR / pixelCount;
                    this.greenMean[xIndex, yIndex] = accumulatedG / pixelCount;
                    this.greenVariance[xIndex, yIndex] = accumulatedGSquared - accumulatedG * accumulatedG / pixelCount;
                    this.blueMean[xIndex, yIndex] = accumulatedB / pixelCount;
                    this.blueVariance[xIndex, yIndex] = accumulatedBSquared - accumulatedB * accumulatedB / pixelCount;
                }
            }
        }

        private unsafe void RenderIntensity(Surface src, Surface dest, Rectangle rect)
        {
            CalculateMeanAndVarianceIntensity(src, rect);

            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                if (IsCancelRequested)
                {
                    return;
                }

                ColorBgra* srcPtr = src.GetPointAddressUnchecked(rect.Left, y);
                ColorBgra* dstPtr = dest.GetPointAddressUnchecked(rect.Left, y);

                for (int x = rect.Left; x < rect.Right; x++)
                {
                    int xbase = x;
                    int ybase = y;
                    int xbase2 = 0;
                    int ybase2 = 0;

                    float min = float.MaxValue;

                    float var = intensityVariance[xbase, ybase];

                    if (var < min)
                    {
                        min = var;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }

                    xbase = x + this.kernelOffset;
                    var = this.intensityVariance[xbase, ybase];

                    if (var < min)
                    {
                        min = var;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }

                    ybase = y + this.kernelOffset;
                    var = this.intensityVariance[xbase, ybase];

                    if (var < min)
                    {
                        min = var;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }

                    xbase = x;
                    var = this.intensityVariance[xbase, ybase];

                    if (var < min)
                    {
                        min = var;
                        xbase2 = xbase;
                        ybase2 = ybase;
                    }

                    HsvColor hsv = HsvColor.FromColor(srcPtr->ToColor());

                    hsv.Value = (int)FloatUtil.Clamp(this.intensityMean[xbase2, ybase2] + 0.5f, 0, 100);

                    RgbColor rgb = hsv.ToRgb();

                    dstPtr->R = (byte)rgb.Red;
                    dstPtr->G = (byte)rgb.Green;
                    dstPtr->B = (byte)rgb.Blue;

                    srcPtr++;
                    dstPtr++;
                }
            }
        }

        private unsafe void CalculateMeanAndVarianceIntensity(Surface src, Rectangle roi)
        {
            int top = roi.Top;
            int bottom = roi.Bottom;
            int left = roi.Left;
            int right = roi.Right;

            for (int y = top - this.kernelOffset; y < bottom; y++)
            {
                if (IsCancelRequested)
                {
                    return;
                }

                for (int x = left - this.kernelOffset; x < right; x++)
                {
                    float accumulatedIntensity = 0;
                    float accumulatedIntensitySquared = 0;
                    float pixelCount = 0;

                    int blockTop = y;
                    if (blockTop < 0)
                    {
                        blockTop = 0;
                    }
                    int blockBottom = y + this.kernelSize;
                    if (blockBottom >= src.Height)
                    {
                        blockBottom = src.Height;
                    }
                    int blockLeft = x;
                    if (blockLeft < 0)
                    {
                        blockLeft = 0;
                    }
                    int blockRight = x + this.kernelSize;
                    if (blockRight >= src.Width)
                    {
                        blockRight = src.Width;
                    }

                    for (int y2 = blockTop; y2 < blockBottom; y2++)
                    {
                        ColorBgra* p = src.GetPointAddressUnchecked(blockLeft, y2);

                        for (int x2 = blockLeft; x2 < blockRight; x2++)
                        {
                            // Map the highest RGB channel to its HSV intensity.

                            int index = Math.Max(p->R, Math.Max(p->G, p->B));

                            int intensity = IntensityLookupTable[index];

                            accumulatedIntensity += intensity;
                            accumulatedIntensitySquared += intensity * intensity;

                            pixelCount++;
                            p++;
                        }
                    }

                    int xIndex = x + this.kernelOffset;
                    int yIndex = y + this.kernelOffset;

                    this.intensityMean[xIndex, yIndex] = accumulatedIntensity / pixelCount;
                    this.intensityVariance[xIndex, yIndex] = accumulatedIntensitySquared - accumulatedIntensity * accumulatedIntensity / pixelCount;
                }
            }
        }

        public enum PropertyNames
        {
            Radius,
            UseRgbChannels
        }
    }
}
