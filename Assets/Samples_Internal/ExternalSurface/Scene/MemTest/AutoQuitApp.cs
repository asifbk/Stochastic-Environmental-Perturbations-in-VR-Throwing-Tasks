using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VIVE.OpenXR.CompositionLayer;

public class AutoQuitApp : MonoBehaviour
{
	public float autoQuitThreshold = 5f;

	public ExternalSurfaceDemo_Manager externalSurfaceDemo_Manager;
	public CompositionLayer compositionLayer;

	private float counter = 0f;

    // Update is called once per frame
    void Update()
    {
		counter += Time.deltaTime;

		if (counter > autoQuitThreshold)
		{
			externalSurfaceDemo_Manager.gameObject.SetActive(false);
            compositionLayer.gameObject.SetActive(false);

			Application.Quit();
		}
    }
}
