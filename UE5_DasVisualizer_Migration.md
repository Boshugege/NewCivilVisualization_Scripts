# DAS Visualizer - 从 Unity 到 Unreal Engine 5 移植指南

将 Unity 的 `DasVisualizer.cs` 移植到 Unreal Engine (UE) 中，核心逻辑是：**通过 UDP 接收 JSON 数据流，解析出包含 `packet_type` 和 `channel_index`（通道索引）的事件，然后在一条由控制点组成的线（路径）上，根据通道索引计算出实际位置，并在该位置显示/更新一个模型。**

在 Unreal 中，最稳定的网络和解析实现是 **C++**（用于 UDP 和 JSON），而路径和可视化最适合用 **Blueprint (蓝图) + Spline Component (样条线组件)** 来实现。

## 核心概念对应关系：
*   **网络和解析**: Unity `UdpClient` & `JsonUtility` ➡️ UE `FUdpSocketReceiver` & `FJsonSerializer`
*   **光纤路径**: Unity `LineRenderer` (坐标点数组) ➡️ UE `USplineComponent` (样条曲线，自带距离计算，极其方便)
*   **模型显示**: Unity `Instantiate(Prefab)` ➡️ UE `SpawnActor` 或直接移动一个提前放好的假人/方块 `UStaticMeshComponent`。

---

## 第一步：开启模块依赖

在你的 Unreal 工程中，找到 `你的项目名.Build.cs` 文件，确保包含网络和 JSON 模块：
```csharp
PublicDependencyModuleNames.AddRange(new string[] { 
    "Core", "CoreUObject", "Engine", "InputCore", 
    "Networking", "Sockets", "Json", "JsonUtilities" // 需要这四个
});
```

---

## 第二步：创建 C++ 数据接收与可视化组件

在引擎中创建一个继承自 `Actor` 的 C++ 类，命名为 `DasVisualizerActor`。

### DasVisualizerActor.h (头文件)
```cpp
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Networking.h"
#include "Sockets.h"
#include "Common/UdpSocketReceiver.h"
#include "DasVisualizerActor.generated.h"

class USplineComponent;
class UStaticMeshComponent;

UCLASS()
class YOURPROJECT_API ADasVisualizerActor : public AActor
{
	GENERATED_BODY()
	
public:	
	ADasVisualizerActor();

protected:
	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;

public:	
	// --- 组件 ---
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "DAS")
	USceneComponent* Root;

	// 用于模拟沿着光纤布置的路径
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "DAS")
	USplineComponent* FiberPath;

	// 替代小人的占位模型（比如一个方块或胶囊体）
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "DAS")
	UStaticMeshComponent* PlaceholderMesh;

	// --- 参数设置 ---
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "DAS|Network")
	int32 UdpListenPort;

	// 每个 Channel 对应在这个线条上多少距离（Unreal 单位默认是厘米）
	// 假如两米一个采样点，这里填 200
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "DAS|Tracking")
	float ChannelDistanceMultiplier;

private:
	FSocket* ListenSocket;
	FUdpSocketReceiver* UdpReceiver;

	// UDP接收回调
	void OnUdpDataReceived(const FArrayReaderPtr& ArrayReaderPtr, const FIPv4Endpoint& EndPt);
};
```

### DasVisualizerActor.cpp (源文件)
```cpp
#include "DasVisualizerActor.h"
#include "Components/SplineComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Async/Async.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

// 注意：将 YOURPROJECT_API 宏替换为你自己的项目宏名

ADasVisualizerActor::ADasVisualizerActor()
{
	PrimaryActorTick.bCanEverTick = false;

	Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	RootComponent = Root;

	FiberPath = CreateDefaultSubobject<USplineComponent>(TEXT("FiberPath"));
	FiberPath->SetupAttachment(RootComponent);

	PlaceholderMesh = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("PlaceholderMesh"));
	PlaceholderMesh->SetupAttachment(RootComponent);
	// 默认隐藏，收到数据再点亮
	PlaceholderMesh->SetVisibility(false); 
    
	UdpListenPort = 9000;
	ChannelDistanceMultiplier = 200.0f;
}

void ADasVisualizerActor::BeginPlay()
{
	Super::BeginPlay();

	// 1. 初始化并监听 UDP 端口
	FIPv4Address Addr = FIPv4Address::Any;
	FIPv4Endpoint Endpoint(Addr, UdpListenPort);

	ListenSocket = FUdpSocketBuilder(TEXT("DasUdpSocket"))
		.AsNonBlocking()
		.AsReusable()
		.BoundToEndpoint(Endpoint)
		.WithReceiveBufferSize(2 * 1024 * 1024)
		.Build();

	if (ListenSocket)
	{
		FTimespan ThreadWaitTime = FTimespan::FromMilliseconds(100);
		UdpReceiver = new FUdpSocketReceiver(ListenSocket, ThreadWaitTime, TEXT("UdpReceiverThread"));
		UdpReceiver->OnDataReceived().BindUObject(this, &ADasVisualizerActor::OnUdpDataReceived);
		UdpReceiver->Start();
		UE_LOG(LogTemp, Warning, TEXT("DAS UDP Listening on Port: %d"), UdpListenPort);
	}
}

void ADasVisualizerActor::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	Super::EndPlay(EndPlayReason);
	
	if (UdpReceiver)
	{
		UdpReceiver->Stop();
		delete UdpReceiver;
		UdpReceiver = nullptr;
	}
	if (ListenSocket)
	{
		ListenSocket->Close();
		ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM)->DestroySocket(ListenSocket);
		ListenSocket = nullptr;
	}
}

void ADasVisualizerActor::OnUdpDataReceived(const FArrayReaderPtr& ArrayReaderPtr, const FIPv4Endpoint& EndPt)
{
	// 2. 将收到的字节流转换为字符串 (UTF-8)
	FString PayloadString = FString(ANSI_TO_TCHAR(reinterpret_cast<const char*>(ArrayReaderPtr->GetData())));

	// 处理了多行 JSON 粘包，按 \n 分割
	TArray<FString> JsonLines;
	PayloadString.ParseIntoArray(JsonLines, TEXT("\n"), true);

	for (FString& Line : JsonLines)
	{
		TSharedPtr<FJsonObject> JsonObject;
		TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Line);

		if (FJsonSerializer::Deserialize(Reader, JsonObject) && JsonObject.IsValid())
		{
			FString PacketType;
			int32 ChannelIndex = 0;
			
			// 3. 读取 packet_type 和 channel_index
			if (JsonObject->TryGetStringField(TEXT("packet_type"), PacketType) && 
				PacketType == TEXT("event") &&
				JsonObject->TryGetNumberField(TEXT("channel_index"), ChannelIndex))
			{
				float TargetDistance = ChannelIndex * ChannelDistanceMultiplier;

				// 4. 返回主线程更新渲染层（重要：不能在网络线程直接更新组件位置）
				AsyncTask(ENamedThreads::GameThread, [this, TargetDistance]()
				{
					if (FiberPath && PlaceholderMesh)
					{
						// 如果之前是隐藏的，现在显示出来
						PlaceholderMesh->SetVisibility(true);

						// 约束距离，防止超界
						float ClampedDist = FMath::Clamp(TargetDistance, 0.0f, FiberPath->GetSplineLength());
						
						// 根据计算出的距离，在 Spline(样条线) 上获取对应的三维世界坐标
						FVector NewLocation = FiberPath->GetLocationAtDistanceAlongSpline(ClampedDist, ESplineCoordinateSpace::World);
						
						// 加上一个 Z 轴偏移让物体悬浮在路径上方
						NewLocation.Z += 100.0f; 
						
						PlaceholderMesh->SetWorldLocation(NewLocation);
					}
				});
			}
		}
	}
}
```

---

## 第三步：在 Unreal 编辑器里使用它

1. **编译 C++ 代码**：关闭 UE 编辑器，在你的 IDE（如 Visual Studio 或 Rider）中点击编译并重新打开工程。
2. **创建蓝图**：在 UE 内容浏览器中右键 -> `Blueprint Class` -> 搜索展开所有类，输入 `DasVisualizerActor` 并继承它，命名为 `BP_DasVisualizer`。
3. **分配占位模型**：
   * 双击打开 `BP_DasVisualizer`。
   * 在左侧 Components 面板点击 `PlaceholderMesh`。
   * 在右侧 Details (细节) 面板找到 `Static Mesh`，选择一个内置的 `Cube` (方块) 或 `Cylinder` (圆柱体)。
4. **绘制管线路径 (如原 C# 中的 Polyline)**：
   * 将做好的 `BP_DasVisualizer` 拖进场景 (Level) 中。
   * 点击场景中它的 `FiberPath` (Spline组件)。
   * 你会在场景里看到一条包含两个端点的线段。你可以在视口里**选中端点，按住 `Alt` 键并拖动**来挤出新的线条节点，随意画出你在现实对应的 DAS 监测线走势（它可以是弯曲的也可以是折线）。
5. **调节参数**：
   * 确保 `Udp Listen Port` 为 Python 端发送数据的端口（如 9000）。
   * 根据你的阵列物理间距计算 `ChannelDistanceMultiplier`，比如如果你的 DAQ 是 2米/通道，Unreal中填 `200.0` (单位厘米)。
6. **运行测试 (Play)**：
   * 点击 Play。Python 脚本只要往对应端口发送 UDP 包类似：`{"packet_type":"event", "timestamp":123.0, "channel_index":15, "confidence":0.9}`。方块就会“瞬间移动”到路径上通道 `15` 对应的世界坐标处！

## 进阶与后续优化
这套逻辑目前提供了一个全局光标闪动的基础模型功能。当你确保网络通信与坐标样条线（Spline）正确运作后，您可以进一步将其扩展为多重跟踪：
通过管理一个 `TMap<int32, AActor*>` (或目标管理列表) 来动态 `SpawnActor` 和 `Destroy` 不同的角色实例，以此复刻类似 `DasVisualizer.cs` 原版中的人群跟踪生命周期。