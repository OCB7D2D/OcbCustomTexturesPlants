using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
// Boilerplate only, real objects will be
// instantiated via assembly-csharp.dll
//[RequireComponent(typeof(Texture))]

[ExecuteInEditMode]
public class PlantPipeline : MonoBehaviour
{

	[Tooltip("Set by script when changes are pending to be generated")]
	public bool ChangesPending = false;

	[Tooltip("Just for info how long generation took in ms")]
	public double TimeUsedMilliseconds = 0;

	[Header("Input - Must be UNCOMPRESSED")]

	public Texture2D UncompressInput;

	[Header("Albedo color manipulation")]

	[Range(0, 32)]
	[Tooltip("Pixels to sample for medians")] 
	public int MedianFilter = 5;

	[Range(0f, 2f)]
	public float RedFactor = 1f;

	[Range(0f, 2f)]
	public float GreenFactor = 1f;

	[Range(0f, 2f)]
	public float BlueFactor = 1f;

	[Range(0f, 2f)]
	public float Brightness = 1f;

	[Range(0f, 2f)]
	public float Lightness = 1f;

	[Range(0, 16)]
	public int BlurRadial = 3;

	[Range(0, 16)]
	public int BlurIterations = 1;

	[Header("Specular generation")]

	[Range(0f, 32f)]
	public float SpecularFactor = 0.75f;

	[Range(0f, 32f)]
	public float SpecularPower = 1.25f;

	[Range(0f, 1f)]
	public float MinLerpSpecular = 0.02f;

	[Range(0f, 2f)]
	public float MaxLerpSpecular = 0.85f;

	[Range(0f, 1f)]
	public float MinClampSpecular = 0.05f;

	[Range(0f, 2f)]
	public float MaxClampSpecular = 0.45f;

	[Header("Translucency generation")]

	[Range(0f, 32f)]
	public float TranslucencyFactor = 0.75f;

	[Range(0f, 32f)]
	public float TranslucencyPower = 1.25f;

	[Range(0f, 8f)]
	public float MinLerpTranslucency = 0.02f;

	[Range(0f, 16f)]
	public float MaxLerpTranslucency = 0.65f;

	[Range(0f, 8f)]
	public float MinClampTranslucency = 0.05f;

	[Range(0f, 16f)]
	public float MaxClampTranslucency = 0.45f;

	[Header("Normal map generation")]

	[Range(0f, 8f)]
	public float NormalStrength = 1.25f;

	[Range(0f, 2f)]
	public float HopFactor = 1f;

	[Header("Output paths")]

	[Tooltip("Path where to store generated maps (Path/name)")]
	public string OutputPath = "ExportPath";
	public string OutputName = "TextureName";

	[Header("Experimental Features")]

	public bool InvertSpecRed = false;
	public bool InvertSpecGreen = false;
	public bool InvertSpecBlue = false;

	// Internal value when update is requested
	float requestUpdate = float.MaxValue;

	private void OnEnable()
	{
		ChangesPending = false;
		// RequestUpdate(0);
	}

	void RequestUpdate(float delay = 0f)
    {
		if (enabled == false) return;
		requestUpdate = Time.realtimeSinceStartup + delay;
		if (ChangesPending == true) return;
		EditorApplication.update += Waiter;
		ChangesPending = true;
	}

	private void Waiter()
	{
		if (requestUpdate < Time.realtimeSinceStartup)
		{
			requestUpdate = float.MaxValue;
			EditorApplication.update -= Waiter;
			ChangesPending = false;
			CreateTexture();
		}
	}

	void OnValidate()
	{
		RequestUpdate(2.5f);
	}

	private void CreateTexture()
	{
		var t = Time.realtimeSinceStartup;
		CreateAlbedo(UncompressInput);
		var median = FilterMedian(UncompressInput, MedianFilter);
		CreateAOST(median);
		CreateNormal(median);
		TimeUsedMilliseconds = (Time.realtimeSinceStartup - t) * 1000;
	}

	string AlbedoPath { get => $"{OutputPath}/{OutputName}.albedo.png"; }
	string NormalPath { get => $"{OutputPath}/{OutputName}.normal.png"; }
	string AostPath { get => $"{OutputPath}/{OutputName}.aost.png"; }

	void CreateAlbedo(Texture2D texture)
	{
		if (string.IsNullOrEmpty(AlbedoPath)) return;
		// var map = CreateNormalmap(texture, NormalStrength, false);
		Texture2D blurred = new Texture2D(texture.width, texture.height);
		Color32[] pixels = texture.GetPixels32();
		blurred.SetPixels32(pixels); // Copy texture
		for (int i = 0; i < BlurIterations && BlurRadial > 0f; i += 1)
			ParallelGaussianBlur.GaussianBlur(ref blurred, BlurRadial);
		Color32[] blurs = blurred.GetPixels32();
		for (int x = 0; x < pixels.Length; x += 1)
		{
			// if (pixels[x].a == byte.MaxValue) continue;
			float a = pixels[x].a / byte.MaxValue;
			float b = 1f - pixels[x].a / byte.MaxValue;
			pixels[x].r = ByteRange(ApplyBrithness(Brightness, Lightness, pixels[x].r * a * RedFactor + blurs[x].r * b * RedFactor));
			pixels[x].g = ByteRange(ApplyBrithness(Brightness, Lightness, pixels[x].g * a * GreenFactor + blurs[x].g * b * GreenFactor));
			pixels[x].b = ByteRange(ApplyBrithness(Brightness, Lightness, pixels[x].b * a * BlueFactor + blurs[x].b * b * BlueFactor));
		}
		texture = blurred;
		texture.SetPixels32(pixels);
		texture.Apply();
		Debug.Log("Writing " + AlbedoPath);
		UpdateAsset(texture, AlbedoPath);
	}

    private byte ByteRange(float v)
    {
		byte rv = (byte)(v + 0.5f);
		if (rv <= byte.MinValue) return byte.MinValue;
		if (rv >= byte.MaxValue) return byte.MaxValue;
		return rv;
    }

    private float ApplyBrithness(float brightness, float lightness, float value)
    {
		value = Mathf.Pow((value + 0.5f) / 255f, brightness);
		value = 1f - Mathf.Pow((1f - value), lightness);
		return value * 255f + 0.5f;
	}

	void CreateAOST(Texture2D median)
	{
		if (string.IsNullOrEmpty(AostPath)) return;
		var aost = CreateAOSTMap(median, 1f,
			SpecularFactor, SpecularPower,
			TranslucencyFactor, TranslucencyPower);
		Debug.Log("Writing " + AostPath);
		UpdateAsset(aost, AostPath);
	}

    private void UpdateAsset(Texture2D texture, string path)
    {
		Debug.Log("Write " + Application.dataPath + "/" + path);
		System.IO.File.WriteAllBytes(
			Application.dataPath + "/" + path,
			texture.EncodeToPNG());

		path = "Assets/" + path; //.Replace(".png", "");

		// if (AssetDatabase.LoadAssetAtPath(path, typeof(Texture2D)) is Texture2D exists)
        // {
		// 	Debug.Log("Set asset to dirty");
		// 	EditorUtility.SetDirty(exists);
		// 	EditorUtility.SetDirty(texture);
		// }
		AssetDatabase.Refresh();
		//	exists.SetPixels(aost.GetPixels());
		//	exists.Apply();
		//	
		//}
		//else
        //{
		//	AssetDatabase.CreateAsset(aost, "Assets/test");
		//}
	}

	void CreateNormal(Texture2D median)
	{
		if (string.IsNullOrEmpty(NormalPath)) return;
		var map = CreateNormalmap(median, NormalStrength, false, HopFactor);
		Debug.Log("Writing " + NormalPath);
		UpdateAsset(map, NormalPath);
	}

	public Texture2D CreateAOSTMap(Texture2D t, float AO = 1f,
		float SpecularFactor = 1f, float SpecularPower = 4f,
		float TranslucencyFactor = 1f, float TranslucencyPower = 2f)
	{

		Color[] pixels = t.GetPixels();

		// Create new texture to hold Specular (R), Occlusion (G), Translucency/Smoothness (B)
		Texture2D specular = new Texture2D(t.width, t.height, TextureFormat.RGB24, false, false);

		for (int y = 0; y < t.height; y++)
		{
			for (int x = 0; x < t.width; x++)
			{
				Color px = pixels[x + y * t.width];
				// Use own formula to create specular on flowers
				float spc = px.r * 0.45f + px.b * 0.35f + px.g * 0.2f;
				spc = Mathf.Pow(spc * SpecularFactor, SpecularPower);
				spc = Mathf.Lerp(MinLerpSpecular, MaxLerpSpecular, spc);
				spc = Mathf.Clamp(spc, MinClampSpecular, MaxClampSpecular);
				// Use own formula to create translucency on flowers
				float tr = Mathf.Max(px.r, px.b) * 0.25f + px.g * 0.75f;
				tr = Mathf.Pow(tr * TranslucencyFactor, TranslucencyPower);
				tr = Mathf.Lerp(MinLerpTranslucency, MaxLerpTranslucency, tr);
				tr = Mathf.Clamp(tr, MinClampTranslucency, MaxClampTranslucency);
				pixels[x + y * t.width] = new Color(
					InvertSpecRed ? 1f - spc : spc,
					InvertSpecGreen ? 1f - AO : AO,
					InvertSpecBlue ? 1f - tr : tr, 1);
			}
		}

		specular.SetPixels(pixels);
		specular.Apply();
		return specular;
	}

	public static Texture2D CreateNormalmap(Texture2D t, float normalStrength,
		bool compressed = false, float factor = 1f)
	{
		Color[] pixels = new Color[t.width * t.height];
		Texture2D texNormal = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false, false);
		Vector3 vScale = new Vector3(0.3333f, 0.3333f, 0.3333f);

		// TODO: would be faster using pixel array, instead of getpixel
		for (int y = 0; y < t.height; y++)
		{
			for (int x = 0; x < t.width; x++)
			{
				Color tc = t.GetPixel(x - 1, y - 1);
				Vector3 cSampleNegXNegY = new Vector3(tc.r, tc.g, tc.g);
				tc = t.GetPixel(x, y - 1);
				Vector3 cSampleZerXNegY = new Vector3(tc.r, tc.g, tc.g);
				tc = t.GetPixel(x + 1, y - 1);
				Vector3 cSamplePosXNegY = new Vector3(tc.r, tc.g, tc.g);
				tc = t.GetPixel(x - 1, y);
				Vector3 cSampleNegXZerY = new Vector3(tc.r, tc.g, tc.g);
				tc = t.GetPixel(x + 1, y);
				Vector3 cSamplePosXZerY = new Vector3(tc.r, tc.g, tc.g);
				tc = t.GetPixel(x - 1, y + 1);
				Vector3 cSampleNegXPosY = new Vector3(tc.r, tc.g, tc.g);
				tc = t.GetPixel(x, y + 1);
				Vector3 cSampleZerXPosY = new Vector3(tc.r, tc.g, tc.g);
				tc = t.GetPixel(x + 1, y + 1);
				Vector3 cSamplePosXPosY = new Vector3(tc.r, tc.g, tc.g);
				float fSampleNegXNegY = Vector3.Dot(cSampleNegXNegY, vScale);
				float fSampleZerXNegY = Vector3.Dot(cSampleZerXNegY, vScale);
				float fSamplePosXNegY = Vector3.Dot(cSamplePosXNegY, vScale);
				float fSampleNegXZerY = Vector3.Dot(cSampleNegXZerY, vScale);
				float fSamplePosXZerY = Vector3.Dot(cSamplePosXZerY, vScale);
				float fSampleNegXPosY = Vector3.Dot(cSampleNegXPosY, vScale);
				float fSampleZerXPosY = Vector3.Dot(cSampleZerXPosY, vScale);
				float fSamplePosXPosY = Vector3.Dot(cSamplePosXPosY, vScale);
				float edgeX = (fSampleNegXNegY - fSamplePosXNegY) * 0.25f + (fSampleNegXZerY - fSamplePosXZerY) * 0.5f + (fSampleNegXPosY - fSamplePosXPosY) * 0.25f;
				float edgeY = (fSampleNegXNegY - fSampleNegXPosY) * 0.25f + (fSampleZerXNegY - fSampleZerXPosY) * 0.5f + (fSamplePosXNegY - fSamplePosXPosY) * 0.25f;
				Vector2 vEdge = new Vector2(edgeX, edgeY) * normalStrength;
				Vector3 norm = new Vector3(vEdge.x, vEdge.y, 1.0f).normalized;

				if (compressed)
				{
					var r = norm.x * 0.5f + 0.5f;
					var g = norm.y * 0.5f + 0.5f;
					g += factor;
					r = Mathf.Clamp01(r);
					g = Mathf.Clamp01(g);
					pixels[x + y * t.width] = new Color(1f, g, g, r);
				}
				else
				{
					pixels[x + y * t.width].r = norm.x * 0.5f + 0.5f;
					pixels[x + y * t.width].g = norm.y * 0.5f + 0.5f;
					pixels[x + y * t.width].b = norm.z * 0.5f + 0.5f;
					pixels[x + y * t.width].r *= factor;
					pixels[x + y * t.width].g *= factor;
					pixels[x + y * t.width].b *= factor;
					pixels[x + y * t.width].a = 1f;
				}
			} // for x
		} // for y

		texNormal.SetPixels(pixels);
		texNormal.Apply();

		return texNormal;
	} // CreateNormalmap

	public static Texture2D FilterMedian(Texture2D t, int filterSize)
	{
		Color[] pixels = new Color[t.width * t.height];
		Texture2D texFiltered = new Texture2D(t.width, t.height, TextureFormat.RGB24, false, false);
		int tIndex = 0;
		int medianMin = -(filterSize / 2);
		int medianMax = (filterSize / 2);
		List<float> r = new List<float>();
		List<float> g = new List<float>();
		List<float> b = new List<float>();
		for (int x = 0; x < t.width; ++x)
		{
			for (int y = 0; y < t.height; ++y)
			{
				r.Clear();
				g.Clear();
				b.Clear();
				for (int x2 = medianMin; x2 < medianMax; ++x2)
				{
					int tx = x + x2;
					if (tx >= 0 && tx < t.width) // TODO: should wrap around? use modulus..
					{
						for (int y2 = medianMin; y2 < medianMax; ++y2)
						{
							int ty = y + y2;
							if (ty >= 0 && ty < t.height)
							{
								Color c = t.GetPixel(tx, ty);
								r.Add(c.r * c.a);
								g.Add(c.g * c.a);
								b.Add(c.b * c.a);
							}
						}
					}
				}
				r.Sort();
				g.Sort();
				b.Sort();
				pixels[x + y * t.width] = new Color(r[r.Count / 2], g[g.Count / 2], b[b.Count / 2]);
				tIndex++;
			}
		}
		texFiltered.SetPixels(pixels);
		texFiltered.Apply();
		return texFiltered;
	} // filtersMedian()

}
