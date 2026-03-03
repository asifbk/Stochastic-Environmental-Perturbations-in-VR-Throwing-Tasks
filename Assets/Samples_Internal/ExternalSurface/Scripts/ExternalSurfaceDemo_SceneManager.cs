using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExternalSurfaceDemo_SceneManager : MonoBehaviour
{
	public GameObject sceneSelectionPanel;
	public GameObject returnToSelectionScenePanel;

	private string currentLoadedScene;

	private float timer = 0.0f;
	private float pre_timer = 0.0f;

	public string LocalVideoScenePath = "Assets/LocalTests/ExternalSurface/Scene/ExternalSurfaceVideo_Local.unity";
	public string NonDRMStreamingScenePath = "Assets/LocalTests/ExternalSurface/Scene/ExternalSurfaceVideo_NoDRMStreaming.unity";
	public string DRMStreamingScenePath = "Assets/LocalTests/ExternalSurface/Scene/ExternalSurfaceVideo_DRMStreaming.unity";

	private void Start()
	{
		timer = 0.0f;
	}

	private void Update()
	{
		timer += Time.deltaTime;

		if (pre_timer < 10.0f && timer >= 10.0f)
		{
			Debug.Log("timer : " + timer + " LaunchDRMStreamScene");
			LaunchDRMStreamScene();
			pre_timer = timer;
		}

		if (pre_timer < 100.0f && timer >= 100.0f)
		{
			Debug.Log("timer : " + timer + " ReturnToSelectionScene");
			ReturnToSelectionScene();
			pre_timer = timer;
		}

		if (pre_timer < 110.0f && timer >= 110.0f)
		{
			Debug.Log("timer : " + timer + " LaunchNonDRMStreamScene");
			LaunchNonDRMStreamScene();
			pre_timer = timer;
		}

		if (pre_timer < 200.0f && timer >= 200.0f)
		{
			Debug.Log("timer : " + timer + " ReturnToSelectionScene");
			ReturnToSelectionScene();
			pre_timer = timer;
		}

		if (pre_timer < 210.0f && timer >= 210.0f)
		{
			Debug.Log("timer : " + timer + " LaunchNonDRMStreamScene");
			LaunchNonDRMStreamScene();
			pre_timer = timer;
		}

		if (pre_timer < 300.0f && timer >= 300.0f)
		{
			Debug.Log("timer : " + timer + " ReturnToSelectionScene");
			ReturnToSelectionScene();
			pre_timer = timer;
		}

		if (pre_timer < 310.0f && timer >= 310.0f)
		{
			Debug.Log("timer : " + timer + " LaunchDRMStreamScene");
			LaunchDRMStreamScene();
			pre_timer = timer;
		}

		if (timer >= 400.0f)
		{
			Debug.Log("timer : " + timer + " ReturnToSelectionScene");
			ReturnToSelectionScene();
			timer = 0.0f;
			pre_timer = 0.0f;
		}
	}

	public void LaunchLocalVideoScene()
	{
		UnityEngine.SceneManagement.SceneManager.LoadScene(LocalVideoScenePath, UnityEngine.SceneManagement.LoadSceneMode.Additive);
		currentLoadedScene = LocalVideoScenePath;
		sceneSelectionPanel.SetActive(false);
		returnToSelectionScenePanel.SetActive(true);
	}

	public void LaunchNonDRMStreamScene()
	{
		UnityEngine.SceneManagement.SceneManager.LoadScene(NonDRMStreamingScenePath, UnityEngine.SceneManagement.LoadSceneMode.Additive);
		currentLoadedScene = NonDRMStreamingScenePath;
		sceneSelectionPanel.SetActive(false);
		returnToSelectionScenePanel.SetActive(true);
	}

	public void LaunchDRMStreamScene()
	{
		UnityEngine.SceneManagement.SceneManager.LoadScene(DRMStreamingScenePath, UnityEngine.SceneManagement.LoadSceneMode.Additive);
		currentLoadedScene = DRMStreamingScenePath;
		sceneSelectionPanel.SetActive(false);
		returnToSelectionScenePanel.SetActive(true);
	}

	public void ReturnToSelectionScene()
	{
		UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(currentLoadedScene);

		currentLoadedScene = "";

		sceneSelectionPanel.SetActive(true);
		returnToSelectionScenePanel.SetActive(false);
	}
}
