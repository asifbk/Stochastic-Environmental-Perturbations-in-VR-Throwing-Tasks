using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExoPlayerUnity
{
	private IntPtr currActivity;
	private IntPtr exoPlayerUnityJavaClass;
	private IntPtr exoPlayerUnityJavaInstance;

	private IntPtr getInstanceStaticMethodId;

	public ExoPlayerUnity()
	{
		//Get current activity
		try
		{
			IntPtr unityPlayerClass = AndroidJNI.FindClass("com/unity3d/player/UnityPlayer");
			IntPtr currentActivityField = AndroidJNI.GetStaticFieldID(unityPlayerClass, "currentActivity", "Landroid/app/Activity;");
			IntPtr activity = AndroidJNI.GetStaticObjectField(unityPlayerClass, currentActivityField);

			currActivity = AndroidJNI.NewGlobalRef(activity);

			AndroidJNI.DeleteLocalRef(activity);
			AndroidJNI.DeleteLocalRef(unityPlayerClass);
		}
		catch (Exception e)
		{
			Debug.LogException(e);
			currActivity = System.IntPtr.Zero;

			return;
		}

		//Get ExoPlayerUnity Java Instance
		try
		{
			IntPtr exoPlayerUnityClassLocal = AndroidJNI.FindClass("com/htc/exoplayeraar/ExoPlayerUnity");

			if (exoPlayerUnityClassLocal != IntPtr.Zero)
			{
				Debug.Log("exoPlayerUnity found");
				exoPlayerUnityJavaClass = AndroidJNI.NewGlobalRef(exoPlayerUnityClassLocal);
				AndroidJNI.DeleteLocalRef(exoPlayerUnityClassLocal);
			}
			else
			{
				Debug.LogError("exoPlayerUnity not found");
			}
			
		}
		catch (Exception e)
		{
			Debug.LogException(e);
			exoPlayerUnityJavaClass = System.IntPtr.Zero;

			return;
		}

		//Get instance of exoPlayerUnityJavaClass
		if (getInstanceStaticMethodId == IntPtr.Zero)
		{
			getInstanceStaticMethodId = AndroidJNI.GetStaticMethodID(exoPlayerUnityJavaClass, "getInstance", "(Landroid/app/Activity;)Lcom/htc/exoplayeraar/ExoPlayerUnity;");
		}

		jvalue[] getInstanceParams = new jvalue[1];
		getInstanceParams[0].l = currActivity;

		IntPtr exoPlayerUnityJavaInstanceLocal = AndroidJNI.CallStaticObjectMethod(exoPlayerUnityJavaClass, getInstanceStaticMethodId, getInstanceParams);

		exoPlayerUnityJavaInstance = AndroidJNI.NewGlobalRef(exoPlayerUnityJavaInstanceLocal);
		AndroidJNI.DeleteLocalRef(exoPlayerUnityJavaInstanceLocal);
	}

	private IntPtr removeInstanceStaticMethodId;
	~ExoPlayerUnity()
	{
		if (removeInstanceStaticMethodId == IntPtr.Zero)
		{
			removeInstanceStaticMethodId = AndroidJNI.GetStaticMethodID(exoPlayerUnityJavaClass, "removeInstance", "()V");
		}

		AndroidJNI.CallStaticVoidMethod(exoPlayerUnityJavaClass, removeInstanceStaticMethodId, new jvalue[0]);

		AndroidJNI.DeleteGlobalRef(exoPlayerUnityJavaInstance);
		AndroidJNI.DeleteGlobalRef(exoPlayerUnityJavaClass);
		AndroidJNI.DeleteGlobalRef(currActivity);
	}

	private IntPtr setSurfaceMethodId;
	public void SetSurface(IntPtr externalSurfacePtr)
	{
		if (setSurfaceMethodId == IntPtr.Zero)
		{
			setSurfaceMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "setSurface", "(Landroid/view/Surface;)V");
		}

		jvalue[] setSurfaceParams = new jvalue[1];
		setSurfaceParams[0].l = externalSurfacePtr;

		AndroidJNI.CallVoidMethod(exoPlayerUnityJavaInstance, setSurfaceMethodId, setSurfaceParams);
	}

	private IntPtr clearSurfaceMethodId;
	public void ClearSurface()
	{
		if (clearSurfaceMethodId == IntPtr.Zero)
		{
			clearSurfaceMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "clearSurface", "()V");
		}

		AndroidJNI.CallVoidMethod(exoPlayerUnityJavaInstance, clearSurfaceMethodId, new jvalue[0]);
	}

	private IntPtr prepareMethodId;
	public void Prepare(string videoPath, string drmLicenseUrl, IntPtr externalSurfacePtr)
	{
		if (prepareMethodId == IntPtr.Zero)
		{
			prepareMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "prepare", "(Ljava/lang/String;Ljava/lang/String;Landroid/view/Surface;)V");
		}

		jvalue[] prepareParams = new jvalue[3];
		IntPtr videoPathJavaString = AndroidJNI.NewStringUTF(videoPath);
		IntPtr drmLicenseUrlJavaString = AndroidJNI.NewStringUTF(drmLicenseUrl);
		prepareParams[0].l = videoPathJavaString;
		prepareParams[1].l = drmLicenseUrlJavaString;
		prepareParams[2].l = externalSurfacePtr;

		AndroidJNI.CallVoidMethod(exoPlayerUnityJavaInstance, prepareMethodId, prepareParams);

		AndroidJNI.DeleteLocalRef(videoPathJavaString);
		AndroidJNI.DeleteLocalRef(drmLicenseUrlJavaString);
	}

	private IntPtr playMethodId;
	public void Play()
	{
		if (playMethodId == IntPtr.Zero)
		{
			playMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "play", "()V");
		}

		AndroidJNI.CallVoidMethod(exoPlayerUnityJavaInstance, playMethodId, new jvalue[0]);
	}

	private IntPtr pauseMethodId;
	public void Pause()
	{
		if (pauseMethodId == IntPtr.Zero)
		{
			pauseMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "pause", "()V");
		}

		AndroidJNI.CallVoidMethod(exoPlayerUnityJavaInstance, pauseMethodId, new jvalue[0]);
	}

	private IntPtr stopMethodId;
	public void Stop()
	{
		if (stopMethodId == IntPtr.Zero)
		{
			stopMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "stop", "()V");
		}

		AndroidJNI.CallVoidMethod(exoPlayerUnityJavaInstance, stopMethodId, new jvalue[0]);
	}

	private IntPtr releaseMethodId;
	public void Release()
	{
		if (releaseMethodId == IntPtr.Zero)
		{
			releaseMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "release", "()V");
		}

		AndroidJNI.CallVoidMethod(exoPlayerUnityJavaInstance, releaseMethodId, new jvalue[0]);
	}

	private IntPtr setLoopModeMethodId;
	public void SetLoopMode(bool enableLooping)
	{
		if (setLoopModeMethodId == IntPtr.Zero)
		{
			setLoopModeMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "setLoopMode", "(Z)V");
		}

		jvalue[] setLoopModeParams = new jvalue[1];
		setLoopModeParams[0].z = enableLooping;

		AndroidJNI.CallVoidMethod(exoPlayerUnityJavaInstance, setLoopModeMethodId, setLoopModeParams);
	}

	private IntPtr seekToModeMethodId;
	public void SeekTo(long timestamp)
	{
		if (seekToModeMethodId == IntPtr.Zero)
		{
			seekToModeMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "seekTo", "(J)V");
		}

		jvalue[] setLoopModeParams = new jvalue[1];
		setLoopModeParams[0].j = timestamp;

		AndroidJNI.CallVoidMethod(exoPlayerUnityJavaInstance, seekToModeMethodId, setLoopModeParams);
	}

	private IntPtr getVideoWidthMethodId;
	public int GetVideoWidth()
	{
		if (getVideoWidthMethodId == IntPtr.Zero)
		{
			getVideoWidthMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "getVideoWidth", "()I");
		}

		return AndroidJNI.CallIntMethod(exoPlayerUnityJavaInstance, getVideoWidthMethodId, new jvalue[0]);
	}

	private IntPtr getVideoHeightMethodId;
	public int GetVideoHeight()
	{
		if (getVideoHeightMethodId == IntPtr.Zero)
		{
			getVideoHeightMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "getVideoHeight", "()I");
		}

		return AndroidJNI.CallIntMethod(exoPlayerUnityJavaInstance, getVideoHeightMethodId, new jvalue[0]);
	}

	private IntPtr getVideoDurationMethodId;
	public long GetVideoDuration()
	{
		if (getVideoDurationMethodId == IntPtr.Zero)
		{
			getVideoDurationMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "getVideoDuration", "()J");
		}

		return AndroidJNI.CallLongMethod(exoPlayerUnityJavaInstance, getVideoDurationMethodId, new jvalue[0]);
	}

	private IntPtr getCurrentPlaybackPositionMethodId;
	public long GetCurrentPlaybackPosition()
	{
		if (getCurrentPlaybackPositionMethodId == IntPtr.Zero)
		{
			getCurrentPlaybackPositionMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "getCurrentPlaybackPosition", "()J");
		}

		return AndroidJNI.CallLongMethod(exoPlayerUnityJavaInstance, getCurrentPlaybackPositionMethodId, new jvalue[0]);
	}

	private IntPtr getLastPlaybackUpdateTimeMethodId;
	public long GetLastPlaybackUpdateTime()
	{
		if (getLastPlaybackUpdateTimeMethodId == IntPtr.Zero)
		{
			getLastPlaybackUpdateTimeMethodId = AndroidJNI.GetMethodID(exoPlayerUnityJavaClass, "getLastPlaybackUpdateTime", "()J");
		}

		return AndroidJNI.CallLongMethod(exoPlayerUnityJavaInstance, getLastPlaybackUpdateTimeMethodId, new jvalue[0]);
	}
}
