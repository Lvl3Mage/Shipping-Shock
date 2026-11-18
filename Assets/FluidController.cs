using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;

public class FluidController : MonoBehaviour
{

	public static FluidController instance {get; private set;}

	[SerializeField] MeshRenderer renderer;
	[SerializeField] Material debugMat;
	[SerializeField] ComputeShader compute;
	[SerializeField] float mouseDragStrength = 20;
	[SerializeField] Camera camera;
	[Header("Fluid simulation settings")]
	[SerializeField] float halfBleedoverSpeed = 150f;
	[SerializeField] float blurSpeed = 0.02f;
	[SerializeField] float decaySpeed = 0.01f;


	[Header("World scale settings")]
	[SerializeField] Vector2 worldSize;
	[SerializeField] float pixelsPerWorldUnit = 1;

	[Header("Power settings")]
	SliderController powerSlider;
	[SerializeField] float maxPower;
	[SerializeField] float powerRegainSpeed;
	float power;

	Vector2Int GetSimulationResolution(){ // calculates the resolution as the product between the worldSize and pixels per world unit, then rounds the result up
		Vector2 unroundedResolution = worldSize*pixelsPerWorldUnit;
		return new Vector2Int((int)Mathf.Ceil(unroundedResolution.x), (int)Mathf.Ceil(unroundedResolution.y));
	}


	RenderTexture velocityMap;
	RenderTexture accelerationMap;
	
	int kernel_CalculateAcceleration, kernel_AddAcceleration, kernel_ApplyAcceleration;
	void Awake()
	{
		if(instance!=null){
			Debug.LogError("Another instance of FluidController is already defined!");
			Destroy(this);
			return;
		}
		FluidController.instance=this;


		//Getting kernels
		kernel_CalculateAcceleration = compute.FindKernel("FluidCalculateAcceleration");
		kernel_AddAcceleration = compute.FindKernel("FluidAddAcceleration");
		kernel_ApplyAcceleration = compute.FindKernel("FluidApplyAcceleration");

		//Texture creation
		velocityMap = CreateFluidMap();
		accelerationMap = CreateFluidMap();

		//Setting parameters
		compute.SetFloat("inv_halfBleedoverSpeed", 1/halfBleedoverSpeed);
		compute.SetFloat("blurSpeed", blurSpeed);
		compute.SetFloat("decaySpeed", decaySpeed);

		//Setting Textures
		compute.SetTexture(kernel_CalculateAcceleration, "velocityMap", velocityMap);
		compute.SetTexture(kernel_ApplyAcceleration, "velocityMap", velocityMap);

		compute.SetTexture(kernel_CalculateAcceleration, "accelerationMap", accelerationMap);
		compute.SetTexture(kernel_AddAcceleration, "accelerationMap", accelerationMap);
		compute.SetTexture(kernel_ApplyAcceleration, "accelerationMap", accelerationMap);

		//variable initialization
		pastMousePos = GetMousePos();
		Material matClones = Instantiate(debugMat);
		AssignTexture(debugMat,velocityMap);
		AssignMat(renderer, debugMat);
		velocityDataBuffer = new NativeArray<Color>(velocityMap.height*velocityMap.width, Allocator.Persistent,NativeArrayOptions.UninitializedMemory);

	}
	void Start(){

		powerSlider = GameObject.FindGameObjectWithTag("Power").GetComponent<SliderController>();
		powerSlider.SetRange(0,maxPower);
	}
	void AssignMat(MeshRenderer mr, Material mat){
		Material[] mats = mr.materials;
		mats[0] = mat;
		mr.materials = mats;
	}
	void AssignTexture(Material mat, RenderTexture texture){
		mat.SetTexture("_MainTex", texture);
	}
	RenderTexture CreateFluidMap(){
		Vector2Int resolution = GetSimulationResolution();
		RenderTexture r = new RenderTexture(resolution.x, resolution.y, 16, RenderTextureFormat.ARGBFloat);
		r.enableRandomWrite = true;
		// r.filterMode = FilterMode.Point;
		return r;
	}
	Vector2 pastMousePos;
	void FixedUpdate()
	{
		if(GameManager.instance.gamePaused) return;

		Vector2 mousePos = GetMousePos();
		Vector2 mouseDelta = mousePos - pastMousePos;

		compute.SetFloat("timeScale", Time.fixedDeltaTime*50*1f);

		if(Input.GetMouseButton(0)){
			ApplyMouseDrag(mousePos,mouseDelta);
		}
		else{
			//recharge power
			power += powerRegainSpeed*Time.deltaTime;
			power = Mathf.Clamp(power,0,maxPower);
		}
		powerSlider.SetValue(power);

		InvokeKernelParelalized(kernel_CalculateAcceleration);
		InvokeKernelFull(kernel_ApplyAcceleration);


		pastMousePos = GetMousePos();
		RequestVelocityData();
		
	}
	void ApplyMouseDrag(Vector2 mousePos, Vector2 mouseDelta){
		if(Time.deltaTime > 0.1f){return;}
		if(power <= 0){return;}
		InvokeCursorAcceleration(mousePos,mouseDelta);

		power -= mouseDelta.magnitude;
		power = Mathf.Clamp(power,0,maxPower);

	}
	void RequestVelocityData(){
		// if(velocityDataBuffer.IsCreated){
		// 	velocityDataBuffer.Dispose();

		// }
		AsyncGPUReadback.RequestIntoNativeArray(ref velocityDataBuffer, velocityMap, 0/*mip index*/, 0/*x*/, velocityMap.width/*width*/,0/*y*/, velocityMap.height/*height*/,0/*z*/,1/*depth*/,TextureFormat.RGBAFloat,OnVelocityRequestComplete);
	}
	NativeArray<Color> velocityDataBuffer;
	Color[] velocityData;
	void OnVelocityRequestComplete(AsyncGPUReadbackRequest completedRequest){
		velocityData = velocityDataBuffer.ToArray();
		if(this == null){
			Debug.Log("Disposed of velocity buffer");
			velocityDataBuffer.Dispose();
		}
		//Nothing here for now	
	}


	void InvokeCursorAcceleration(Vector2 pos, Vector2 delta){
		var (sizeX, sizeY, sizeZ) = GetKernelGroupSize(kernel_AddAcceleration);
		pos = WorldToTextureCoordinates(pos);
		compute.SetInts("appliedPoint", new int[]{(int)pos.x - (int)sizeX/2, (int)pos.y - (int)sizeY/2});
		compute.SetFloats("appliedVelocity", new float[]{delta.x*mouseDragStrength,delta.y*mouseDragStrength});
		compute.Dispatch(kernel_AddAcceleration, 1, 1, 1);
	}
	Vector2 WorldToTextureCoordinates(Vector2 worldCoords){

		worldCoords = new Vector2(worldCoords.x/worldSize.x, worldCoords.y/worldSize.y);

		//transforming coordinates from [-0.5,0.5] to [0,1];
		worldCoords += Vector2.one * 0.5f;


		Vector2Int resolution = GetSimulationResolution();
		worldCoords = Vector2.Scale(worldCoords,(Vector2)resolution);
		return worldCoords;
	}
	Vector2 TextureToWorldCoordinates(Vector2 textureCoords){

		Vector2Int resolution = GetSimulationResolution();
		textureCoords = new Vector2(textureCoords.x/resolution.x, textureCoords.y/resolution.y);

		//transforming coordinates from [0,1] to [-0.5,0.5];
		textureCoords -= Vector2.one * 0.5f;


		textureCoords = Vector2.Scale(textureCoords, worldSize);
		return textureCoords;
	}

	void InvokeKernelParelalized(int kernel){
		Vector2Int resolution = GetSimulationResolution();
		var (fuidX,fluidY,fluidZ) = GetKernelGroupSize(kernel);

		Vector2Int individualKernelSize = new Vector2Int((int)Mathf.Ceil((resolution.x/(float)fuidX) / 3f), (int)Mathf.Ceil((resolution.y / (float)fluidY) / 3f));
		for(int i = 0; i <= 2; i++){
			for(int j = 0; j <= 2; j++){
				compute.SetInts("blockOffset", new int[]{i, j});
				compute.Dispatch(kernel, individualKernelSize.x, individualKernelSize.y, 1);
			}
		}
	}
	void InvokeKernelFull(int kernel){
		Vector2Int resolution = GetSimulationResolution();
		var (fuidX,fluidY,fluidZ) = GetKernelGroupSize(kernel);
		compute.Dispatch(kernel, (int)Mathf.Ceil(resolution.x/(float)fuidX), (int)Mathf.Ceil(resolution.y/(float)fluidY), 1);
	}


	Vector2 GetMousePos(){
		Vector3 worldPosition = camera.ScreenToWorldPoint(Input.mousePosition);
		return worldPosition;
	}
	(int,int,int) GetKernelGroupSize(int kernelID){
		uint x, y, z;
		compute.GetKernelThreadGroupSizes(kernelID, out x, out y, out z);
		return ((int)x,(int)y,(int)z);
	}
	void OnDrawGizmos(){
		Gizmos.color = Color.blue;
		Vector2 size = worldSize/2;
		Gizmos.DrawLine(new Vector2(size.x,size.y),new Vector2(size.x,-size.y));
		Gizmos.DrawLine(new Vector2(size.x,-size.y),new Vector2(-size.x,-size.y));
		Gizmos.DrawLine(new Vector2(-size.x,-size.y),new Vector2(-size.x,size.y));
		Gizmos.DrawLine(new Vector2(-size.x,size.y),new Vector2(size.x,size.y));
	}
	public Vector2 GetVelocityAtPosition(Vector2 worldPos){
		if(velocityData == null){return Vector2.zero;}
		Vector2 localPos = WorldToTextureCoordinates(worldPos);
		Vector2Int closestPos = new Vector2Int((int)Mathf.Round(localPos.x),(int)Mathf.Round(localPos.y));
		Vector2Int resolution = GetSimulationResolution();
		closestPos.x = Mathf.Clamp(closestPos.x, 0, resolution.x-1);
		closestPos.y = Mathf.Clamp(closestPos.y, 0, resolution.y-1);
		// Debug.Log(velocityData.IsCreated);
		Color color = velocityData[closestPos.x + closestPos.y*resolution.x];
		return new Vector2(color.r, color.g);
	}
}
