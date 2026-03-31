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

原版 Unity 插件包含了完整的 **实体状态机（聚集、Tentative、Confirmed、Lost）、EMA 坐标平滑**以及**视觉上的插值移动（Lerp）和淡入淡出**。为了在 Unreal 中完美复刻，我们通过 `Tick`（每帧更新）来接管平滑和实体生命周期逻辑，而不只是在收到网络包时瞬间移动。

### DasVisualizerActor.h (头文件)
```cpp
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Networking.h"
#include "Sockets.h"
#include "Common/UdpSocketReceiver.h"
#include "Containers/Queue.h"
#include "DasVisualizerActor.generated.h"

class USplineComponent;

// --- 数据结构 ---
USTRUCT()
struct FFootstepEvent
{
	GENERATED_BODY()
	float Timestamp = 0.0f;
	int32 ChannelIndex = 0;
	float Confidence = 0.0f;
};

UENUM()
enum class ETargetState : uint8
{
	Tentative,
	Confirmed,
	Lost,
	Removed
};

USTRUCT()
struct FTrackedTarget
{
	GENERATED_BODY()
	int32 ID = 0;
	ETargetState State = ETargetState::Tentative;
	
	float ChannelPosition = 0.0f; // EMA 平滑后的逻辑通道位置
	float LastEventTime = 0.0f;
	
	// 表现层
	UPROPERTY(Transient)
	AActor* VisualActor = nullptr; // 对应的游戏内模型实体
	
	float ActualDisplayDistance = 0.0f; // 当前实际模型在哪
};

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
	virtual void Tick(float DeltaTime) override;

	// --- 组件 ---
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "DAS")
	USceneComponent* Root;

	// 光纤路径
	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "DAS")
	USplineComponent* FiberPath;

	// --- 参数设置 ---
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "DAS|Network")
	int32 UdpListenPort = 9000;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "DAS|Tracking")
	float ChannelDistanceMultiplier = 200.0f; // 每个通道占多少厘米

	// 小人的可生成类 (在蓝图里指定一个做好的 BP_Person)
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "DAS|Visuals")
	TSubclassOf<AActor> PersonClassToSpawn;

	// 平滑速度
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "DAS|Visuals")
	float PositionSmoothSpeed = 5.0f;
	
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "DAS|Visuals")
	float TrackingPositionAlpha = 0.35f;

private:
	FSocket* ListenSocket;
	FUdpSocketReceiver* UdpReceiver;

	// 线程安全的事件队列
	TQueue<FFootstepEvent, EQueueMode::Spsc> IncomingEvents;
	
	// 活跃目标列表
	UPROPERTY(Transient)
	TArray<FTrackedTarget> ActiveTargets;
	int32 NextTargetId = 1;

	// 系统时间
	float CurrentStreamTime = 0.0f;

	void OnUdpDataReceived(const FArrayReaderPtr& ArrayReaderPtr, const FIPv4Endpoint& EndPt);
	
	// 逻辑处理
	void ProcessEvents();
	void UpdateVisuals(float DeltaTime);
};
```

### DasVisualizerActor.cpp (源文件)
```cpp
#include "DasVisualizerActor.h"
#include "Components/SplineComponent.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Async/Async.h"

// 注意：将 YOURPROJECT_API 宏替换为你自己的项目宏名

ADasVisualizerActor::ADasVisualizerActor()
{
	PrimaryActorTick.bCanEverTick = true; // ★ 开启 Tick 用于平滑和处理

	Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	RootComponent = Root;

	FiberPath = CreateDefaultSubobject<USplineComponent>(TEXT("FiberPath"));
	FiberPath->SetupAttachment(RootComponent);
}

void ADasVisualizerActor::BeginPlay()
{
	Super::BeginPlay();

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
		FTimespan ThreadWaitTime = FTimespan::FromMilliseconds(10);
		UdpReceiver = new FUdpSocketReceiver(ListenSocket, ThreadWaitTime, TEXT("UdpReceiverThread"));
		UdpReceiver->OnDataReceived().BindUObject(this, &ADasVisualizerActor::OnUdpDataReceived);
		UdpReceiver->Start();
        UE_LOG(LogTemp, Warning, TEXT("DAS 跟踪版 UDP 已启动，端口: %d"), UdpListenPort);
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
	TArray<uint8> ReceivedData = *ArrayReaderPtr;
	ReceivedData.Add(0); 
	FString PayloadString = FString(UTF8_TO_TCHAR(ReceivedData.GetData()));

	TArray<FString> JsonLines;
	PayloadString.ParseIntoArray(JsonLines, TEXT("\n"), true);

	for (FString& Line : JsonLines)
	{
		TSharedPtr<FJsonObject> JsonObject;
		TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Line);

		if (FJsonSerializer::Deserialize(Reader, JsonObject) && JsonObject.IsValid())
		{
			FString PacketType;
			if (JsonObject->TryGetStringField(TEXT("packet_type"), PacketType) && PacketType == TEXT("event"))
			{
				FFootstepEvent NewEvent;
				JsonObject->TryGetNumberField(TEXT("timestamp"), NewEvent.Timestamp);
				JsonObject->TryGetNumberField(TEXT("channel_index"), NewEvent.ChannelIndex);
				JsonObject->TryGetNumberField(TEXT("confidence"), NewEvent.Confidence);
				
				// 将解析好的数据塞进队列，交给主线程 Tick 处理
				IncomingEvents.Enqueue(NewEvent);
			}
		}
	}
}

void ADasVisualizerActor::Tick(float DeltaTime)
{
	Super::Tick(DeltaTime);
	
	CurrentStreamTime += DeltaTime; // 简单的时间推移模拟

	ProcessEvents();
	UpdateVisuals(DeltaTime);
}

void ADasVisualizerActor::ProcessEvents()
{
	FFootstepEvent EventData;
	// 每次将队列里的数据抽干处理
	while (IncomingEvents.Dequeue(EventData))
	{
		bool bAssociated = false;

		// 1. 尝试将新事件关联到存活的目标上
		for (FTrackedTarget& Target : ActiveTargets)
		{
			float Dist = FMath::Abs(Target.ChannelPosition - EventData.ChannelIndex);
			// 距离较近则认作同一个人继续走动 (对应 Unity 里的 maxAssociationDistance)
			if (Dist < 15.0f) 
			{
				// 用 EMA 平滑更新该人的虚拟通道坐标
				Target.ChannelPosition = FMath::Lerp(Target.ChannelPosition, (float)EventData.ChannelIndex, TrackingPositionAlpha);
				Target.LastEventTime = CurrentStreamTime;
				Target.State = ETargetState::Confirmed;
				bAssociated = true;
				break;
			}
		}

		// 2. 如果是一个全新很远地方的脚步（触发新角色）
		if (!bAssociated)
		{
			FTrackedTarget NewTarget;
			NewTarget.ID = NextTargetId++;
			NewTarget.ChannelPosition = EventData.ChannelIndex;
			NewTarget.LastEventTime = CurrentStreamTime;
			NewTarget.State = ETargetState::Tentative;
			
			// 把其实际显示坐标就初始化在出生点
			NewTarget.ActualDisplayDistance = NewTarget.ChannelPosition * ChannelDistanceMultiplier;

			// 生成指定的模型 (类似 Instantiate)，要在蓝图配置 PersonClassToSpawn
			if (PersonClassToSpawn)
			{
				FActorSpawnParameters SpawnParams;
				NewTarget.VisualActor = GetWorld()->SpawnActor<AActor>(PersonClassToSpawn, FVector::ZeroVector, FRotator::ZeroRotator, SpawnParams);
			}
			
			ActiveTargets.Add(NewTarget);
		}
	}
}

void ADasVisualizerActor::UpdateVisuals(float DeltaTime)
{
	for (int32 i = ActiveTargets.Num() - 1; i >= 0; i--)
	{
		FTrackedTarget& Target = ActiveTargets[i];

		// 生命周期管理：3秒没信号判定为丢失，5秒彻底清除模型 (对应 lostTimeoutSeconds 和 removeTimeoutSeconds)
		float TimeSinceLastEvent = CurrentStreamTime - Target.LastEventTime;
		if (TimeSinceLastEvent > 3.0f && Target.State == ETargetState::Confirmed)
		{
			Target.State = ETargetState::Lost;
		}
		if (TimeSinceLastEvent > 5.0f)
		{
			if (Target.VisualActor)
			{
				Target.VisualActor->Destroy();
			}
			ActiveTargets.RemoveAt(i);
			continue;
		}

		// 执行类似原版的 Visual 平滑
		if (Target.VisualActor && FiberPath)
		{
			// 本阵列对应的目标绝对距离
			float TargetRealDistance = Target.ChannelPosition * ChannelDistanceMultiplier;
			float MaxSplineLen = FiberPath->GetSplineLength();
			TargetRealDistance = FMath::Clamp(TargetRealDistance, 0.0f, MaxSplineLen);

			// ★ 像原版中那样，利用 FInterpTo 随着位置平滑地 Lerp(插值)，而不是瞬移！★
			Target.ActualDisplayDistance = FMath::FInterpTo(Target.ActualDisplayDistance, TargetRealDistance, DeltaTime, PositionSmoothSpeed);

			FVector NewLocation = FiberPath->GetLocationAtDistanceAlongSpline(Target.ActualDisplayDistance, ESplineCoordinateSpace::World);
			
			// 可以让模型稍微高于地面
			NewLocation.Z += 100.0f;
            
			FRotator NewRotation = FiberPath->GetRotationAtDistanceAlongSpline(Target.ActualDisplayDistance, ESplineCoordinateSpace::World);
			
			Target.VisualActor->SetActorLocationAndRotation(NewLocation, NewRotation);
		}
	}
}
```

---

## 第三步：在 Unreal 编辑器里的新变化

1. 你需要先去新建一个普通的 **Blueprint Class (类型为 Actor)**，比如叫 `BP_PersonTarget`。
2. 双击打开 `BP_PersonTarget`，在里面加一个 `Static Mesh` 组件（设定为方块或胶囊体），再加你想展示的粒子特效或材质。
3. 打开我们在主场景中放的 `BP_DasVisualizer`，在右侧细节面板找到新增的 **Person Class To Spawn** 这个属性。把它设置为你刚刚创建的 `BP_PersonTarget`。
4. **效果**：现在它不再是简单挪动一个方块，而是完整的模拟了 C# 中的逻辑：
   - 如果收到数据，它会自动**Spawn(生成)** 出一个你配置的小方块放在对应位置。
   - 如果继续连续收到数据，小方块会**以平滑插值（Lerp）的方式向着目标位置滑动**。
   - 如果好几秒都没有收到某个人的新脚步，那个小方块会自己进入 Lost 状态并在 5 秒后被 **Destroy(销毁)**！如果有两处远距离的脚步，它会生成两个方块！
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