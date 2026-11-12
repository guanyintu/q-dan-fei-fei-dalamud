using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using NativeActor = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace QBounce;

// Native Update is virtual: discover the current entry through validated actors instead of hardcoded signatures.
internal sealed unsafe class ActorUpdateHooks(IFramework framework, IGameInteropProvider interop,
    ActorManager actors, Action<Exception> fault) : IDisposable
{
    private readonly Dictionary<nint, Hook<NativeActor.Delegates.Update>> hooks = [];
    public bool Active { get; set; }
    public int Count => hooks.Count;

    public void Ensure(ActorSnapshot actor)
    {
        if (hooks.ContainsKey(actor.UpdateFunction)) return;
        if (actor.UpdateFunction == 0 || hooks.Count >= 16) throw new InvalidOperationException("角色更新入口异常，已暂停。");
        Hook<NativeActor.Delegates.Update>? hook = null;
        hook = interop.HookFromAddress<NativeActor.Delegates.Update>(actor.UpdateFunction, native =>
        {
            var index = Active && native != null && framework.IsInFrameworkUpdateThread ? native->ObjectIndex : -1;
            // Always forward exactly once. Never swallow an exception from the original game function.
            hook!.Original(native);
            if (!Active || index < 0 || !framework.IsInFrameworkUpdateThread) return;
            // Original may have destroyed/redrawn native. Re-resolve its slot before touching it again.
            try { actors.AfterNativeUpdate(index, (nint)native); }
            catch (Exception ex)
            {
                Active = false;
                fault(ex);
            }
        });
        try
        {
            hooks.Add(actor.UpdateFunction, hook);
            hook.Enable();
        }
        catch
        {
            hooks.Remove(actor.UpdateFunction);
            hook.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Active = false;
        foreach (var hook in hooks.Values) hook.Dispose();
        hooks.Clear();
    }
}
