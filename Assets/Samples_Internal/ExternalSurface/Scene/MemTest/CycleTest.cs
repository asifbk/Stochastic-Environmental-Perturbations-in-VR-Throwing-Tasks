using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VIVE.OpenXR.CompositionLayer;

public class CycleTest : MonoBehaviour
{
	public float cyclePlayPeriod = 3f;
	public float cycleWaitPeriod = 1f;


	public ExternalSurfaceDemo_Manager externalSurfaceDemo_Manager;
	public CompositionLayer compositionLayer;

	private bool isLoaded = true;
	private float counter = 0f;

	// Update is called once per frame
	void Update()
	{
		counter += Time.deltaTime;

		if (isLoaded)
		{
			if (counter >= cyclePlayPeriod)
			{
				counter = 0;

				externalSurfaceDemo_Manager.gameObject.SetActive(false);
                compositionLayer.gameObject.SetActive(false);
				isLoaded = false;
			}

			
		}
		else
		{
			if (counter >= cycleWaitPeriod)
			{
				counter = 0;

                compositionLayer.gameObject.SetActive(true);
				externalSurfaceDemo_Manager.gameObject.SetActive(true);
				isLoaded = true;
			}

			
		}
	}
}
