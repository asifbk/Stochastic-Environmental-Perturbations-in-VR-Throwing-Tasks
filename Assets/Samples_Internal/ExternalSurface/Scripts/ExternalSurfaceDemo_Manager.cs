using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using VIVE.OpenXR.CompositionLayer;
//using Wave.Native;

public class ExternalSurfaceDemo_Manager : MonoBehaviour
{
	private const string LOG_TAG = "ExternalSurfaceDemo_Manager";

	public CompositionLayer compositionLayer;

	public string drmLicenseURL;
	public string videoPath;

	public Text playbackPositionText, videoDurationText, playButtonText;
	public Slider videoSeekSlider;

	private VideoSliderState sliderState = VideoSliderState.Normal;
	private bool isPlaying = false;
	private bool isPlayingBeforeSuspend = false;

	private ExoPlayerUnity playerInstance = null;
	private IntPtr currentSurfaceHandle;

	private float timer = 0.0f;

	private void OnEnable()
	{
		if (isPlayingBeforeSuspend)
		{
			Play();
		}
	}

	private void OnDisable()
	{
		if (isPlaying)
		{
			isPlayingBeforeSuspend = true;
			Pause();
		}
	}

	private void OnDestroy()
	{
		playerInstance.Stop();
		playerInstance = null;
	}

	private void Restart()
	{
		playerInstance.Stop();
		playerInstance.Release();
		playerInstance = null;
	}

	GameObject FindInActiveObjectByTag(string tag)
	{

		Transform[] objs = Resources.FindObjectsOfTypeAll<Transform>() as Transform[];
		for (int i = 0; i < objs.Length; i++)
		{
			if (objs[i].hideFlags == HideFlags.None)
			{
				if (objs[i].CompareTag(tag))
				{
					return objs[i].gameObject;
				}
			}
		}
		return null;
	}

	private void Start()
	{
		timer = 0.0f;
	}

	private void Update()
	{
		if (compositionLayer.GetExternalSurfaceObj() == IntPtr.Zero)
		{
			return;
		}

		if (playerInstance == null)
		{
			currentSurfaceHandle = compositionLayer.GetExternalSurfaceObj();
			if (currentSurfaceHandle != IntPtr.Zero)
			{
				playerInstance = new ExoPlayerUnity();

                // TODO : compositionLayer.OnDestroyCompositorLayerDelegate += playerInstance.ClearSurface;

				PrepareVideo();
			}
		}
		else
		{
			if (currentSurfaceHandle != compositionLayer.GetExternalSurfaceObj()) //Surface handle changed
			{
				playerInstance.SetSurface(compositionLayer.GetExternalSurfaceObj());
				currentSurfaceHandle = compositionLayer.GetExternalSurfaceObj();

				if (isPlaying)
				{
					Play();
				}
			}

			if (isPlaying)
			{
				long currentPlaybackPosition = playerInstance.GetCurrentPlaybackPosition();
				long currentVideoDuration = playerInstance.GetVideoDuration();



				playbackPositionText.text = TimeStampConversion(currentPlaybackPosition);
				videoDurationText.text = TimeStampConversion(currentVideoDuration);

				if (sliderState == VideoSliderState.Normal)
				{
					videoSeekSlider.value = (float)currentPlaybackPosition / (float)currentVideoDuration;
					//Debug.Log("sliderValue: " + videoSeekSlider.value);
				}
			}
			else
			{
				playbackPositionText.text = "00:00:00";
				videoDurationText.text = "00:00:00";
				videoSeekSlider.value = 0f;

				timer += Time.deltaTime;
				if (timer >= 1.0f)
				{
					Debug.Log("Start Play()");
					Play();
					timer = 0.0f;
				}
			}
		}
	}

	private bool IsLocalVideo(string movieName)
	{
		return !movieName.Contains("://");
	}

	public void PrepareVideo()
	{
		if (playerInstance == null) return;

		if (!IsLocalVideo(videoPath))
		{
			playerInstance.Prepare(videoPath, drmLicenseURL, compositionLayer.GetExternalSurfaceObj());
		}
		else
		{
			playerInstance.Prepare(Application.streamingAssetsPath + "/" + videoPath, null, compositionLayer.GetExternalSurfaceObj());
		}

		//Play();
		playerInstance.SetLoopMode(true);
		timer = 0.0f;
	}

	void OnApplicationPause(bool isPaused)
	{
		Debug.Log("OnApplicationPause: " + isPaused);
		if (isPaused)
		{
			if (isPlaying)
			{
				isPlayingBeforeSuspend = true;
			}
			Pause();
		}
		else
		{
			if (isPlayingBeforeSuspend)
			{
				Play();
			}
		}
	}

	string TimeStampConversion(long milliseconds) //converts video timestamp from ms to mins : secs string 
	{
		//Log.d(LOG_TAG, "TimeStampConversion: " + milliseconds + " ms");
		
		TimeSpan t = TimeSpan.FromMilliseconds(milliseconds);
		
		
		return string.Format("{0:D2}:{1:D2}:{2:D2}",
								t.Hours,
								t.Minutes,
								t.Seconds);
	}

	public void PlayButtonOnClick()
	{
		if (isPlaying)
		{
			isPlayingBeforeSuspend = false;
			Pause();
		}
		else
		{
			Play();
		}
	}

	public void ChangeButtonOnClick()
	{
		if (isPlaying)
		{
			isPlayingBeforeSuspend = false;
			Pause();
		}
		Restart();
	}

	private void Play()
	{
		if (playerInstance == null) return;
		playButtonText.text = "Pause";
		playerInstance.Play();
		isPlaying = true;
	}

	private void Pause()
	{
		if (playerInstance == null) return;
		playButtonText.text = "Play";
		playerInstance.Pause();
		isPlaying = false;
	}

	enum VideoSliderState
	{
		Scrubbing,
		Normal
	}

	public void VideoSliderOnBeginDrag()
	{
		sliderState = VideoSliderState.Scrubbing;
	}

	public void VideoSliderOnEndDrag()
	{
		sliderState = VideoSliderState.Normal;

		if (playerInstance == null) return;

		playerInstance.SeekTo((long)(videoSeekSlider.value * playerInstance.GetVideoDuration()));
	}
}
