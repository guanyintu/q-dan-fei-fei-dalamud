using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Xunit;
using NativeActor = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using NativeKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;

namespace QBounce.Tests;

// Real TransformService against owned unmanaged fixtures and the installed ClientStructs DLL.
// No game process or game addresses are touched; this cannot prove rendered appearance.
public unsafe sealed class NativeTransformTests : IDisposable
{
    private readonly Character* character = (Character*)NativeMemory.AllocZeroed((nuint)sizeof(Character));
    private readonly CharacterBase* model = (CharacterBase*)NativeMemory.AllocZeroed((nuint)sizeof(CharacterBase));
    private readonly nint* actorVtable = (nint*)NativeMemory.AllocZeroed(100, (nuint)sizeof(nint));
    private readonly nint* drawVtable = (nint*)NativeMemory.AllocZeroed(100, (nuint)sizeof(nint));
    private NativeActor* Actor => (NativeActor*)character;
    private DrawObject* Draw => (DrawObject*)model;
    private nint liveAddress;
    private bool mainThread = true;
    private readonly TransformService service;
    private static int notifications;

    public NativeTransformTests()
    {
        *(nint*)Actor = (nint)actorVtable;
        *(nint*)Draw = (nint)drawVtable;
        drawVtable[2] = (nint)(delegate* unmanaged<DrawObject*, ObjectType>)&GetDrawType;
        drawVtable[7] = (nint)(delegate* unmanaged<DrawObject*, byte, void>)&Notify;
        drawVtable[50] = (nint)(delegate* unmanaged<CharacterBase*, CharacterBase.ModelType>)&GetModelType;
        actorVtable[36] = 12345;
        Actor->ObjectKind = NativeKind.EventNpc;
        Actor->ObjectIndex = 0;
        Actor->EntityId = 123;
        Actor->DrawObject = Draw;
        Actor->Scale = 1.25f;
        Actor->HitboxRadius = 2;
        Actor->Position.Y = 12;
        Draw->LoadState = 3;
        Draw->Scale.X = Draw->Scale.Y = Draw->Scale.Z = 1;
        Draw->Position.Y = 12;
        model->Skeleton = (FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton*)9876;
        liveAddress = (nint)Actor;
        var objects = Proxy.Create<IObjectTable>((method, _) => method.Name switch
        {
            "get_Length" => 1,
            "GetObjectAddress" => liveAddress,
            _ => throw new NotSupportedException(method.Name),
        });
        var framework = Proxy.Create<IFramework>((method, _) => method.Name switch
        {
            "get_IsInFrameworkUpdateThread" => mainThread,
            _ => throw new NotSupportedException(method.Name),
        });
        service = new TransformService(objects, framework);
    }

    [UnmanagedCallersOnly] private static ObjectType GetDrawType(DrawObject* _) => ObjectType.CharacterBase;
    [UnmanagedCallersOnly] private static CharacterBase.ModelType GetModelType(CharacterBase* _) => CharacterBase.ModelType.Human;
    [UnmanagedCallersOnly] private static CharacterBase.ModelType GetUnsupportedModel(CharacterBase* _) => CharacterBase.ModelType.Weapon;
    [UnmanagedCallersOnly] private static ObjectType GetUnsupportedDraw(DrawObject* _) => ObjectType.BgObject;
    [UnmanagedCallersOnly] private static ObjectType GetSceneContainer(FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object* _) => ObjectType.Object;
    [UnmanagedCallersOnly] private static void Notify(DrawObject* _, byte __) => notifications++;

    [Fact]
    public void OrdinaryWorldSceneParentDoesNotMakeCharacterAnAttachedActor()
    {
        var parent = (FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object*)NativeMemory.AllocZeroed(0x80);
        var parentVtable = (nint*)NativeMemory.AllocZeroed(8, (nuint)sizeof(nint));
        try
        {
            *(nint*)parent = (nint)parentVtable;
            parentVtable[2] = (nint)(delegate* unmanaged<FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object*, ObjectType>)&GetSceneContainer;
            Draw->ParentObject = parent;
            Assert.True(service.TryGet(0, out var actor));
            Assert.True(service.TryWriteScale(actor, new Vector3(.8f, 1.4f, .8f)));
        }
        finally { Draw->ParentObject = null; NativeMemory.Free(parent); NativeMemory.Free(parentVtable); }
    }

    [Fact]
    public void ActualThreeAxisWriteNotifiesTransformAndLeavesGameplayAndAnchorUnchanged()
    {
        Assert.Equal((nint)0x70, Marshal.OffsetOf<FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object>("Scale"));
        Assert.Equal((nint)0x100, Marshal.OffsetOf<NativeActor>("DrawObject"));
        Assert.Equal((nint)0x8C, Marshal.OffsetOf<NativeActor>("ObjectIndex"));
        Assert.True(service.TryGet(0, out var snapshot));
        var previous = notifications;
        var value = new Vector3(.8f, 1.4f, .8f);
        Assert.True(service.TryWriteScale(snapshot, value));
        Assert.True(service.TryReadScale(snapshot, out var actual)); Assert.Equal(value, actual);
        Assert.Equal(previous + 1, notifications); Assert.True(Draw->IsTransformChanged);
        Assert.Equal(1.25f, Actor->Scale); Assert.Equal(2, Actor->HitboxRadius);
        Assert.Equal(12, Actor->Position.Y); Assert.Equal(12, Draw->Position.Y);
    }

    [Fact]
    public void SlotInvalidatedBeforeWriteDoesNotDereferenceCachedActor()
    {
        Assert.True(service.TryGet(0, out var snapshot));
        liveAddress = 0;
        Assert.False(service.TryWriteScale(snapshot, new Vector3(2)));
        Assert.Equal(1, Draw->Scale.Y);
    }

    [Fact]
    public void ReusedActorOrNewSkeletonOrNewDrawCannotBeRestoredThroughOldSnapshot()
    {
        Assert.True(service.TryGet(0, out var snapshot));
        Actor->EntityId++; Assert.False(service.TryWriteScale(snapshot, new Vector3(2)));
        Actor->EntityId--; model->Skeleton++;
        Assert.False(service.TryWriteScale(snapshot, new Vector3(2)));
        model->Skeleton--; Actor->DrawObject = null;
        Assert.False(service.TryWriteScale(snapshot, new Vector3(2)));
    }

    [Fact]
    public void UnknownModelsUnloadedModelsAndAttachedActorsAreSkipped()
    {
        Draw->LoadState = 2; Assert.False(service.TryGet(0, out _)); Draw->LoadState = 3;
        drawVtable[50] = (nint)(delegate* unmanaged<CharacterBase*, CharacterBase.ModelType>)&GetUnsupportedModel;
        Assert.False(service.TryGet(0, out _));
        drawVtable[50] = (nint)(delegate* unmanaged<CharacterBase*, CharacterBase.ModelType>)&GetModelType;
        drawVtable[2] = (nint)(delegate* unmanaged<DrawObject*, ObjectType>)&GetUnsupportedDraw;
        Assert.False(service.TryGet(0, out _));
        drawVtable[2] = (nint)(delegate* unmanaged<DrawObject*, ObjectType>)&GetDrawType;
        Draw->ParentObject = (FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object*)model;
        Assert.False(service.TryGet(0, out _));
    }

    [Fact]
    public void BackgroundThreadAndInvalidMultipliersNeverWrite()
    {
        Assert.True(service.TryGet(0, out var snapshot));
        Assert.False(service.TryWriteScale(snapshot, new Vector3(float.NaN)));
        Assert.False(service.TryWriteScale(snapshot, new Vector3(float.PositiveInfinity)));
        Assert.False(service.TryWriteScale(snapshot, new Vector3(-1)));
        mainThread = false;
        Assert.False(service.TryGet(0, out _)); Assert.False(service.TryWriteScale(snapshot, new Vector3(2)));
    }

    [Theory]
    [InlineData(NativeKind.Pc, ActorGroup.Self)]
    [InlineData(NativeKind.EventNpc, ActorGroup.Npc)]
    [InlineData(NativeKind.BattleNpc, ActorGroup.Monster)]
    [InlineData(NativeKind.Retainer, ActorGroup.Npc)]
    public void ActualObjectKindsMapToScope(NativeKind kind, ActorGroup group)
    {
        Actor->ObjectKind = kind;
        Assert.True(service.TryGet(0, out var snapshot)); Assert.Equal(group, snapshot.Group);
    }

    public void Dispose()
    {
        NativeMemory.Free(character); NativeMemory.Free(model); NativeMemory.Free(actorVtable); NativeMemory.Free(drawVtable);
    }
}

public class Proxy : DispatchProxy
{
    private Func<MethodInfo, object?[]?, object?> handler = null!;
    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var value = Create<T, Proxy>(); ((Proxy)(object)value).handler = handler; return value;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => handler(targetMethod!, args);
}
