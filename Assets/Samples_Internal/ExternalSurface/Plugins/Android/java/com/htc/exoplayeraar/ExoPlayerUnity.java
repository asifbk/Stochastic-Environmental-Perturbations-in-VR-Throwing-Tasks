package com.htc.exoplayeraar;

import android.app.Activity;
import android.content.Context;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.view.Surface;

import androidx.core.math.MathUtils;

import com.google.android.exoplayer2.C;
//import com.google.android.exoplayer2.MediaItem;
import com.google.android.exoplayer2.Format;
import com.google.android.exoplayer2.PlaybackParameters;
import com.google.android.exoplayer2.Player;
import com.google.android.exoplayer2.SimpleExoPlayer;
import com.google.android.exoplayer2.drm.DefaultDrmSessionManager;
import com.google.android.exoplayer2.drm.DrmSessionManager;
import com.google.android.exoplayer2.drm.FrameworkMediaDrm;
import com.google.android.exoplayer2.drm.HttpMediaDrmCallback;
import com.google.android.exoplayer2.source.MediaSource;
import com.google.android.exoplayer2.source.ProgressiveMediaSource;
import com.google.android.exoplayer2.source.dash.DashMediaSource;
import com.google.android.exoplayer2.source.hls.HlsMediaSource;
import com.google.android.exoplayer2.source.smoothstreaming.SsMediaSource;
import com.google.android.exoplayer2.upstream.DataSource;
import com.google.android.exoplayer2.upstream.DefaultDataSourceFactory;
//import com.google.android.exoplayer2.upstream.DefaultHttpDataSource;
import com.google.android.exoplayer2.upstream.DefaultHttpDataSourceFactory;
import com.google.android.exoplayer2.upstream.HttpDataSource;
import com.google.android.exoplayer2.util.Assertions;
import com.google.android.exoplayer2.util.Util;

import java.util.UUID;

public class ExoPlayerUnity {

    private static final String TAG = ExoPlayerUnity.class.getSimpleName();

    private final Activity activity;
    private final Context context;
    private final String userAgent;
    private final DefaultHttpDataSourceFactory httpDataSourceFactory;
    private final DefaultDataSourceFactory mediaDataSourceFactory;

    private static ExoPlayerUnity instance = null;
    private Surface externalAndroidSurface;
    private SimpleExoPlayer simpleExoPlayer;
    private Handler mainLooperHandler = null;

    //Playback Info
    private volatile int currentPlaybackState = Player.STATE_IDLE;
    private volatile long videoDuration = 0;
    private volatile int videoWidth = 0, videoHeight = 0;
    private volatile long lastPlaybackPosition = 0;
    private volatile long lastPlaybackUpdateTime = 0;

    public static ExoPlayerUnity getInstance(Activity activity)
    {
        if (instance == null)
        {
            instance = new ExoPlayerUnity(activity);
        }

        return instance;
    }

    public static void removeInstance()
    {
        if (instance != null)
        {
            if (instance.simpleExoPlayer != null)
            {
                instance.release();
            }
        }

        instance = null;
    }

    public ExoPlayerUnity(Activity activity)
    {
        this.activity = activity;
        this.context = activity.getApplicationContext();

        userAgent = Util.getUserAgent(context, "ExoPlayerUnity");
        httpDataSourceFactory = new DefaultHttpDataSourceFactory(userAgent);
        mediaDataSourceFactory = new DefaultDataSourceFactory(context, null, httpDataSourceFactory);
    }

    public void setSurface(Surface surface)
    {
        this.externalAndroidSurface = surface;
        Log.d(TAG, "setSurface: " + this.externalAndroidSurface.toString());
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (simpleExoPlayer != null) {
                    simpleExoPlayer.setVideoSurface(externalAndroidSurface);
                }
            }
        });
    }

    public void clearSurface()
    {
        this.externalAndroidSurface = null;
        Log.d(TAG, "clearSurface()");
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (simpleExoPlayer != null) {
                    simpleExoPlayer.setVideoSurface(null);
                }
            }
        });
    }

    public void prepare(String videoPath, String drmLicenseUrl, Surface surface)
    {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {

                Uri videoUri = Uri.parse(videoPath);
                //videoUri = Uri.parse("https://storage.googleapis.com/wvmedia/cenc/hevc/tears/tears_sd.mpd");

                //Convert local file uri
                if (videoPath.startsWith( "jar:file:" )) {
                    if (videoPath.contains(".apk")) { // APK
                        videoUri = new Uri.Builder().scheme( "asset" ).path( videoPath.substring( videoPath.indexOf( "/assets/" ) + "/assets/".length() ) ).build();
                    }
    //                    else if (filePath.contains(".obb")) { // OBB
    //                        String obbPath = filePath.substring(11, filePath.indexOf(".obb") + 4);
    //
    //                        StorageManager sm = (StorageManager)context.getSystemService(Context.STORAGE_SERVICE);
    //                        if (!sm.isObbMounted(obbPath))
    //                        {
    //                            sm.mountObb(obbPath, null, new OnObbStateChangeListener() {
    //                                @Override
    //                                public void onObbStateChange(String path, int state) {
    //                                    super.onObbStateChange(path, state);
    //                                }
    //                            });
    //                        }
    //
    //                        uri = new Uri.Builder().scheme( "file" ).path( sm.getMountedObbPath(obbPath) + filePath.substring(filePath.indexOf(".obb") + 5) ).build();
    //                    }
                }
                Log.d(TAG, "initializePlayer: videoUri = " + videoUri);

                DrmSessionManager drmSessionManager;

                //final String drmVideoId = "edef8ba9-79d6-4ace-a3c8-27dcd51d21ed";

                if (drmLicenseUrl != null && drmLicenseUrl.length() > 0/*&& intent.hasExtra(DRM_SCHEME_EXTRA)*/) {
                    Log.d(TAG, "prepare with DRM ");
                    String drmScheme =  "widevine";
                    //String drmLicenseUrl = "https://proxy.uat.widevine.com/proxy?provider=widevine_test";
                    UUID drmSchemeUuid = Assertions.checkNotNull(Util.getDrmUuid(drmScheme));



                    //HttpDataSource.Factory licenseDataSourceFactory = httpDataSourceFactory;
                    HttpMediaDrmCallback drmCallback =
                            new HttpMediaDrmCallback(drmLicenseUrl, httpDataSourceFactory);
                    drmSessionManager =
                            new DefaultDrmSessionManager.Builder()
                                    .setUuidAndExoMediaDrmProvider(drmSchemeUuid, FrameworkMediaDrm.DEFAULT_PROVIDER)
                                    .build(drmCallback);
                }
                else
                {
                    Log.d(TAG, "dummyDRM");
                    drmSessionManager = DrmSessionManager.getDummyDrmSessionManager();
                }

                //Guess content type
                MediaSource mediaSource;
                @C.ContentType int type = Util.inferContentType(videoUri, null);
                switch (type) {
                    case C.TYPE_DASH:
                        mediaSource = new DashMediaSource.Factory(mediaDataSourceFactory)
                                .setDrmSessionManager(drmSessionManager)
                                .createMediaSource(videoUri);
                        break;
                    case C.TYPE_SS:
                        mediaSource = new SsMediaSource.Factory(mediaDataSourceFactory)
                                .setDrmSessionManager(drmSessionManager)
                                .createMediaSource(videoUri);
                        break;
                    case C.TYPE_HLS:
                        mediaSource = new HlsMediaSource.Factory(mediaDataSourceFactory)
                                .setDrmSessionManager(drmSessionManager)
                                .createMediaSource(videoUri);
                        break;
                    case C.TYPE_OTHER:
                        mediaSource = new ProgressiveMediaSource.Factory(mediaDataSourceFactory)
                                .setDrmSessionManager(drmSessionManager)
                                .createMediaSource(videoUri);
                        break;
                    default: {
                        throw new IllegalStateException("Unsupported type: " + type);
                    }
                }

                Log.d(TAG, "ContentType: " + type);

                //Create Simple ExoPlayer
                if (simpleExoPlayer != null)
                {
                    simpleExoPlayer.release();
                }

                simpleExoPlayer = new SimpleExoPlayer.Builder(context.getApplicationContext()).build();

                simpleExoPlayer.addListener(new Player.EventListener() {
                    @Override
                    public void onPlayerStateChanged(boolean playWhenReady, int playbackState) {

                        currentPlaybackState = playbackState;

                        refreshPlaybackInfo();
                    }

                    @Override
                    public void onPositionDiscontinuity(int reason) {
                        refreshPlaybackInfo();
                    }

                    @Override
                    public void onPlaybackParametersChanged(PlaybackParameters playbackParameters) {
                        refreshPlaybackInfo();
                    }
                });

                Log.d(TAG, "prepare()");
                setSurface(surface);
                simpleExoPlayer.prepare(mediaSource);
                simpleExoPlayer.setPlayWhenReady(false);
            }
        });
    }

    public void play()
    {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (simpleExoPlayer != null) {
                    Log.d(TAG, "play()");
                    simpleExoPlayer.setPlayWhenReady(true);
                }
            }
        });
    }

    public void pause()
    {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (simpleExoPlayer != null) {
                    Log.d(TAG, "pause()");
                    simpleExoPlayer.setPlayWhenReady(false);
                }
            }
        });
    }

    public void stop()
    {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (simpleExoPlayer != null) {
                    Log.d(TAG, "stop()");
                    simpleExoPlayer.stop();
                }
            }
        });
    }

    public void release()
    {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (simpleExoPlayer != null) {
                    Log.d(TAG, "release()");
                    simpleExoPlayer.release();
                    simpleExoPlayer = null;
                }
            }
        });
    }

    public void setLoopMode(boolean enableLooping)
    {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (simpleExoPlayer != null) {
                    Log.d(TAG, "setLoopMode(): " + enableLooping);
                    if (enableLooping)
                    {
                        simpleExoPlayer.setRepeatMode( Player.REPEAT_MODE_ONE );
                    }
                    else
                    {
                        simpleExoPlayer.setRepeatMode( Player.REPEAT_MODE_OFF );
                    }
                }
            }
        });
    }

    public void seekTo(long timeStamp)
    {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (simpleExoPlayer != null) {

                    long clampedTS = timeStamp;

                    if (clampedTS > videoDuration)
                    {
                        clampedTS = videoDuration;
                    }
                    else if (clampedTS < 0)
                    {
                        clampedTS = 0;
                    }

                    Log.d(TAG, "seekTo(): " + clampedTS);

                    simpleExoPlayer.seekTo(clampedTS);
                }
            }
        });
    }

    private void refreshPlaybackInfo()
    {
        Log.d(TAG, "refreshPlaybackInfo()");

        videoDuration = simpleExoPlayer.getDuration();
        lastPlaybackPosition = simpleExoPlayer.getCurrentPosition();
        lastPlaybackUpdateTime = System.currentTimeMillis();

        Format videoFormat = simpleExoPlayer.getVideoFormat();
        if (videoFormat != null)
        {
            videoWidth = videoFormat.width;
            videoHeight = videoFormat.height;
        }
        else
        {
            videoWidth = 0;
            videoHeight = 0;
        }
    }

    public int getVideoWidth()
    {
        //Log.d(TAG, "getVideoWidth(): " + videoWidth);
        return  videoWidth;
    }

    public int getVideoHeight()
    {
        //Log.d(TAG, "getVideoHeight(): " + videoHeight);
        return  videoHeight;
    }

    public long getVideoDuration()
    {
        //Log.d(TAG, "getVideoDuration(): " + videoDuration);
        return  Math.max(0, videoDuration);
    }

    public long getCurrentPlaybackPosition()
    {
        long currentPlaybackPosition = Math.max(0, Math.min(videoDuration, lastPlaybackPosition + (long)((System.currentTimeMillis() - lastPlaybackUpdateTime))));
        //Log.d(TAG, "getCurrentPlaybackPosition(): " + currentPlaybackPosition);
        return currentPlaybackPosition;
    }

    public long getLastPlaybackUpdateTime()
    {
        //Log.d(TAG, "getLastPlaybackUpdateTime(): " + lastPlaybackUpdateTime);
        return lastPlaybackUpdateTime;
    }
}
