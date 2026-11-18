using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CurrentAffectedObject : MonoBehaviour
{
	Rigidbody2D rigidbody;
	FluidController controller;
	[SerializeField] float inertia;

	const float sampleRadius = 0.5f;
	const int sampleCount = 9;
	const float streamCenterPull = 2;
	Vector2[] sampleOffsets;
	void Start()
	{
		rigidbody = GetComponent<Rigidbody2D>();
		controller = FluidController.instance;
		sampleOffsets = GenerateSampleOffsets();
	}

	void Update()
	{
		Vector2 centralSample = controller.GetVelocityAtPosition(transform.position);
		float maxVelocitySqr = centralSample.sqrMagnitude;
		Vector2 maxVelocityOffset = Vector2.zero;
		bool surpassedCentralSample = false;
		foreach(Vector2 offset in sampleOffsets){
			// Debug.DrawLine(transform.position, (Vector2)transform.position + offset*sampleRadius, Color.green);
			float curVelocitySqr = controller.GetVelocityAtPosition((Vector2)transform.position + offset*sampleRadius).sqrMagnitude;
			if(maxVelocitySqr < curVelocitySqr){
				maxVelocitySqr = curVelocitySqr;
				maxVelocityOffset = offset;
				surpassedCentralSample = true;
			}
		}
		Vector2 targetVelocity = centralSample;

		if(surpassedCentralSample){
			targetVelocity += maxVelocityOffset*(Mathf.Sqrt(maxVelocitySqr) - Mathf.Sqrt(centralSample.sqrMagnitude))*streamCenterPull;
		}
		rigidbody.velocity = Vector2.Lerp(rigidbody.velocity, targetVelocity, 1 - Mathf.Pow(inertia, Time.deltaTime));
	}
	Vector2[] GenerateSampleOffsets(){
		Vector2[] sampleOffsets = new Vector2[sampleCount];
		float angleOffset = 2*Mathf.PI/sampleCount;
		for(int i = 0; i < sampleCount; i++){
			sampleOffsets[i] = new Vector2(Mathf.Cos(i*angleOffset), Mathf.Sin(i*angleOffset));
		}
		return sampleOffsets;
	}
}
