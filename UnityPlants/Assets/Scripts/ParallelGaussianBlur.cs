using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

public class ParallelGaussianBlur
{

    // The MIT License
    // Copyright © 2020 Roger Cabo Ashauer
    // Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute,     // sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:     // The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.     // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,     // FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,     // WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
    // https://de.wikipedia.org/wiki/MIT-Lizenz

    // This solution is based on Fast image convolutions by Wojciech Jarosz.
    // http://elynxsdk.free.fr/ext-docs/Blur/Fast_box_blur.pdf
    // And Ivan Kutskir
    // http://blog.ivank.net/fastest-gaussian-blur.html
    // And Mike Demyl
    // https://github.com/mdymel  // https://github.com/mdymel/superfastblur

    private static readonly ParallelOptions _pOptions = new ParallelOptions
    {
        MaxDegreeOfParallelism = 8
    };

    public static void GaussianBlur(ref Texture2D tex2D, int Radial = 1)
    {

        int _Width = tex2D.width;
        int _Height = tex2D.height;
        int _PixelCount = _Width * _Height;

        NativeArray<Color32> _RawTextureData = new NativeArray<Color32>();

        _RawTextureData = tex2D.GetRawTextureData<Color32>();
        _Width = tex2D.width;
        _Height = tex2D.height;
        _PixelCount = tex2D.width * tex2D.height;

        _Width = tex2D.width;
        _Height = tex2D.height;

        int[] m_red = new int[_Width * _Height];
        int[] m_green = new int[_Width * _Height];
        int[] m_blue = new int[_Width * _Height];
        int[] m_alpha = new int[_Width * _Height];

        Parallel.For(0, _Width * _Height, _pOptions, i =>
        {
            m_red[i] = _RawTextureData[i].r;
            m_green[i] = _RawTextureData[i].g;
            m_blue[i] = _RawTextureData[i].b;
            m_alpha[i] = _RawTextureData[i].a;
        });

        var newAlpha = new int[_Width * _Height];
        var newRed = new int[_Width * _Height];
        var newGreen = new int[_Width * _Height];
        var newBlue = new int[_Width * _Height];

        Parallel.Invoke(
            () => gaussBlur_4(m_alpha, newAlpha, Radial, _Width, _Height),
            () => gaussBlur_4(m_red, newRed, Radial, _Width, _Height),
            () => gaussBlur_4(m_green, newGreen, Radial, _Width, _Height),
            () => gaussBlur_4(m_blue, newBlue, Radial, _Width, _Height));

        Parallel.For(0, _Width * _Height, _pOptions, i =>
        {
            // int[i] = (int)((uint)(newRed[i] << 24) | (uint)(newGreen[i] << 16) | (uint)(newBlue[i] << 8) | (uint)newAlpha[i]);

            if (newAlpha[i] > 255) newAlpha[i] = 255;
            if (newRed[i] > 255) newRed[i] = 255;
            if (newGreen[i] > 255) newGreen[i] = 255;
            if (newBlue[i] > 255) newBlue[i] = 255;

            if (newAlpha[i] < 0) newAlpha[i] = 0;
            if (newRed[i] < 0) newRed[i] = 0;
            if (newGreen[i] < 0) newGreen[i] = 0;
            if (newBlue[i] < 0) newBlue[i] = 0;

            _RawTextureData[i] = new Color32((byte)newRed[i], (byte)newGreen[i], (byte)newBlue[i], (byte)newAlpha[i]);
        });

        tex2D.Apply();
    }

    private static int[] boxesForGauss(int sigma, int n)
    {
        double wIdeal = System.Math.Sqrt((12 * sigma * sigma / n) + 1);
        int wl = (int)System.Math.Floor(wIdeal);
        if (wl % 2 == 0) wl--;
        int wu = wl + 2;

        double mIdeal = (double)(12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4 * wl - 4);
        double m = System.Math.Round(mIdeal);

        var sizes = new List<int>();
        for (var i = 0; i < n; i++) sizes.Add(i < m ? wl : wu);
        return sizes.ToArray();
    }

    private static void gaussBlur_4(int[] colorChannel, int[] destChannel, int r, int _Width, int _Height)
    {
        int[] bxs = boxesForGauss(r, 3);
        boxBlur_4(colorChannel, destChannel, _Width, _Height, (bxs[0] - 1) / 2);
        boxBlur_4(destChannel, colorChannel, _Width, _Height, (bxs[1] - 1) / 2);
        boxBlur_4(colorChannel, destChannel, _Width, _Height, (bxs[2] - 1) / 2);
    }

    private static void boxBlur_4(int[] colorChannel, int[] destChannel, int w, int h, int r)
    {
        for (var i = 0; i < colorChannel.Length; i++) destChannel[i] = colorChannel[i];
        boxBlurH_4(destChannel, colorChannel, w, h, r);
        boxBlurT_4(colorChannel, destChannel, w, h, r);
    }

    private static void boxBlurH_4(int[] colorChannel, int[] dest, int w, int h, int radial)
    {
        var iar = (double)1 / (radial + radial + 1);
        Parallel.For(0, h, _pOptions, i =>
        {
            var ti = i * w;
            var li = ti;
            var ri = ti + radial;
            var fv = colorChannel[ti];
            var lv = colorChannel[ti + w - 1];
            var val = (radial + 1) * fv;
            for (var j = 0; j < radial; j++) val += colorChannel[ti + j];
            for (var j = 0; j <= radial; j++)
            {
                val += colorChannel[ri++] - fv;
                dest[ti++] = (int)System.Math.Round(val * iar);
            }
            for (var j = radial + 1; j < w - radial; j++)
            {
                val += colorChannel[ri++] - dest[li++];
                dest[ti++] = (int)System.Math.Round(val * iar);
            }
            for (var j = w - radial; j < w; j++)
            {
                val += lv - colorChannel[li++];
                dest[ti++] = (int)System.Math.Round(val * iar);
            }
        });
    }

    private static void boxBlurT_4(int[] colorChannel, int[] dest, int w, int h, int r)
    {
        var iar = (double)1 / (r + r + 1);
        Parallel.For(0, w, _pOptions, i =>
        {
            var ti = i;
            var li = ti;
            var ri = ti + r * w;
            var fv = colorChannel[ti];
            var lv = colorChannel[ti + w * (h - 1)];
            var val = (r + 1) * fv;
            for (var j = 0; j < r; j++) val += colorChannel[ti + j * w];
            for (var j = 0; j <= r; j++)
            {
                val += colorChannel[ri] - fv;
                dest[ti] = (int)System.Math.Round(val * iar);
                ri += w;
                ti += w;
            }
            for (var j = r + 1; j < h - r; j++)
            {
                val += colorChannel[ri] - colorChannel[li];
                dest[ti] = (int)System.Math.Round(val * iar);
                li += w;
                ri += w;
                ti += w;
            }
            for (var j = h - r; j < h; j++)
            {
                val += lv - colorChannel[li];
                dest[ti] = (int)System.Math.Round(val * iar);
                li += w;
                ti += w;
            }
        });
    }
}
