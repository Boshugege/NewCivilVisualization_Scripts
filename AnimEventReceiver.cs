using UnityEngine;

/// <summary>
/// 空的 AnimationEvent 接收器。
/// 挂载到带 Animator 的 GameObject 上，用于消化 StarterAssets 动画中的
/// OnFootstep / OnLand 事件，避免 "has no receiver" 警告。
/// </summary>
public class AnimEventReceiver : MonoBehaviour
{
    public void OnFootstep(AnimationEvent animationEvent)
    {
        // 静默消化脚步事件（不播放音效）
    }

    public void OnLand(AnimationEvent animationEvent)
    {
        // 静默消化着陆事件
    }
}
