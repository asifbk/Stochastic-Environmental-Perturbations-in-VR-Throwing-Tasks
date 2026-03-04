using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SG.Util
{
	/// <summary> Uses an SG_HapticGlove's IMU output to rotate a transform relative to another one. </summary>
	public class SG_IMUTracking : MonoBehaviour
	{
		// PlayerPrefs keys for persisting both the rotation offset and the calibration quaternion.
		private const string PrefKeyOffsetX  = "SG_IMU_OffsetX";
		private const string PrefKeyOffsetY  = "SG_IMU_OffsetY";
		private const string PrefKeyOffsetZ  = "SG_IMU_OffsetZ";
		private const string PrefKeyCalibrX  = "SG_IMU_CalibrX";
		private const string PrefKeyCalibrY  = "SG_IMU_CalibrY";
		private const string PrefKeyCalibrZ  = "SG_IMU_CalibrZ";
		private const string PrefKeyCalibrW  = "SG_IMU_CalibrW";
		private const string PrefKeyCalibrSet = "SG_IMU_CalibrSet";

		/// <summary> The SG_HapticGlove from which we can collect the IMU rotation. </summary>
		public SG_HapticGlove imuSource;

		/// <summary> The rotation is calibrated to - and moves relative to - this object. If left unassgined, you won't be able to calibrate. </summary>
		public Transform relativeTo;

		/// <summary> Calibrate automatically the first time we connect to a glove. </summary>
		protected bool firstCalibr = true;

		/// <summary> Used for calibration. </summary>
		protected Quaternion qCalibr = Quaternion.identity;

		public KeyCode calibrateIMUKey = KeyCode.None;

		/// <summary>
		/// An additional rotation (in degrees) that will always be applied on top of
		/// the computed IMU rotation. Default is -90 degrees on the Y axis.
		/// You can change this in the inspector.
		/// If <see cref="rotationOffsetIsWorldSpace"/> is true, the offset is applied
		/// in world-space (pre-multiplied). Otherwise it is applied in local/object
		/// space (post-multiplied) which is the default.
		/// The value is automatically saved to and loaded from PlayerPrefs so it
		/// persists between play sessions — no more manual inspector adjustments.
		/// </summary>
		public Vector3 rotationOffsetEuler = new Vector3(0f, -90f, 0f);

		/// <summary> When true the rotation offset is applied in world-space: offset * baseRotation. </summary>
		public bool rotationOffsetIsWorldSpace = true;

		private void Awake()
		{
			LoadFromPrefs();
		}

		/// <summary> Saves rotationOffsetEuler AND qCalibr to PlayerPrefs so both survive sessions. </summary>
		public void SaveToPrefs()
		{
			PlayerPrefs.SetFloat(PrefKeyOffsetX,  rotationOffsetEuler.x);
			PlayerPrefs.SetFloat(PrefKeyOffsetY,  rotationOffsetEuler.y);
			PlayerPrefs.SetFloat(PrefKeyOffsetZ,  rotationOffsetEuler.z);
			PlayerPrefs.SetFloat(PrefKeyCalibrX,  qCalibr.x);
			PlayerPrefs.SetFloat(PrefKeyCalibrY,  qCalibr.y);
			PlayerPrefs.SetFloat(PrefKeyCalibrZ,  qCalibr.z);
			PlayerPrefs.SetFloat(PrefKeyCalibrW,  qCalibr.w);
			PlayerPrefs.SetInt(PrefKeyCalibrSet, 1);
			PlayerPrefs.Save();
			Debug.Log($"[SG_IMUTracking] Saved — offset: {rotationOffsetEuler}, calibr: {qCalibr}");
		}

		/// <summary> Loads rotationOffsetEuler and qCalibr from PlayerPrefs if saved values exist. </summary>
		public void LoadFromPrefs()
		{
			if (PlayerPrefs.HasKey(PrefKeyOffsetX))
			{
				rotationOffsetEuler = new Vector3(
					PlayerPrefs.GetFloat(PrefKeyOffsetX),
					PlayerPrefs.GetFloat(PrefKeyOffsetY),
					PlayerPrefs.GetFloat(PrefKeyOffsetZ)
				);
			}

			// If a previous calibration was saved, restore it and skip auto-calibration on first connect.
			if (PlayerPrefs.GetInt(PrefKeyCalibrSet, 0) == 1)
			{
				qCalibr = new Quaternion(
					PlayerPrefs.GetFloat(PrefKeyCalibrX),
					PlayerPrefs.GetFloat(PrefKeyCalibrY),
					PlayerPrefs.GetFloat(PrefKeyCalibrZ),
					PlayerPrefs.GetFloat(PrefKeyCalibrW)
				);
				firstCalibr = false; // Skip auto-recalibration — use the saved one.
				Debug.Log($"[SG_IMUTracking] Restored calibration quaternion: {qCalibr}");
			}
		}

		/// <summary> Saves the current rotationOffsetEuler to PlayerPrefs. </summary>
		public void SaveOffsetToPrefs()
		{
			SaveToPrefs();
		}

		/// <summary> Loads rotationOffsetEuler from PlayerPrefs if a saved value exists. </summary>
		public void LoadOffsetFromPrefs()
		{
			LoadFromPrefs();
		}

		/// <summary> Calibrate the IMU to the relativeTo Transform and persist both the offset and calibration quaternion. </summary>
		public void CalibrateIMU()
        {
			this.firstCalibr = false;
			Quaternion currIMU;
			if (this.relativeTo != null && this.imuSource != null && this.imuSource.GetIMURotation(out currIMU))
            {
				qCalibr = this.relativeTo.rotation * Quaternion.Inverse(currIMU);
            }

			SaveToPrefs();
		}

		/// <summary> Updates the IMU rotation every frame. </summary>
		public void UpdateRotation()
		{
			Quaternion currIMU;
			if (this.imuSource != null && this.imuSource.GetIMURotation(out currIMU))
            {
				if (this.firstCalibr)
                {
					this.CalibrateIMU();
                }

				Quaternion baseRotation = (this.relativeTo != null) ? (this.qCalibr * currIMU) : currIMU;
				Quaternion offset = Quaternion.Euler(this.rotationOffsetEuler);

				if (this.rotationOffsetIsWorldSpace)
				{
					this.transform.rotation = offset * baseRotation;
				}
				else
				{
					this.transform.rotation = baseRotation * offset;
				}
			}
		}

		/// <summary> Returns true if the calibration key was pressed this frame, supporting both Input Systems. </summary>
		private bool IsCalibrKeyPressed()
		{
#if ENABLE_INPUT_SYSTEM
			if (calibrateIMUKey == KeyCode.None) return false;
			var keyboard = Keyboard.current;
			if (keyboard == null) return false;
			// Map legacy KeyCode to the new Input System key.
			if (System.Enum.TryParse(calibrateIMUKey.ToString(), true, out Key newKey))
				return keyboard[newKey].wasPressedThisFrame;
			return false;
#else
			return calibrateIMUKey != KeyCode.None && Input.GetKeyDown(calibrateIMUKey);
#endif
		}

		void Update()
		{
			if (IsCalibrKeyPressed())
				CalibrateIMU();

			UpdateRotation();
		}
	}
}