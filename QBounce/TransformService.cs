using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using NativeActor = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using NativeKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;

namespace QBounce;

public enum ActorRejection { None, WrongThread, EmptySlot, InvalidActor, UnsupportedKind, NoDraw, NotLoaded, UnsupportedDraw, UnsupportedModel, Attached }

public sealed unsafe class TransformService(IObjectTable objects, IFramework framework) : IActorAccess
{
    public int Capacity => objects.Length;

    public bool TryGet(int index, out ActorSnapshot snapshot)
        => TryGet(index, out snapshot, out _);

    public bool TryGet(int index, out ActorSnapshot snapshot, out ActorRejection rejection)
    {
        snapshot = default;
        rejection = ActorRejection.WrongThread;
        if (!framework.IsInFrameworkUpdateThread) return false;
        // Resolve the CURRENT slot; no cached native pointer is dereferenced first.
        var address = objects.GetObjectAddress(index);
        rejection = ActorRejection.EmptySlot;
        if (address == 0) return false;
        var actor = (NativeActor*)address;
        rejection = ActorRejection.InvalidActor;
        if (actor->ObjectIndex != index || actor->VirtualTable == null) return false;
        ActorGroup group;
        switch (actor->ObjectKind)
        {
            case NativeKind.Pc: group = index == 0 ? ActorGroup.Self : ActorGroup.Player; break;
            case NativeKind.EventNpc:
            case NativeKind.Retainer:
            case NativeKind.Companion: group = ActorGroup.Npc; break;
            case NativeKind.BattleNpc: group = ActorGroup.Monster; break;
            default: rejection = ActorRejection.UnsupportedKind; return false;
        }
        var draw = actor->DrawObject;
        rejection = ActorRejection.NoDraw;
        if (draw == null || draw->VirtualTable == null) return false;
        rejection = ActorRejection.NotLoaded;
        if (draw->LoadState != 3) return false;
        rejection = ActorRejection.UnsupportedDraw;
        if (draw->GetObjectType() != ObjectType.CharacterBase) return false;
        var model = (CharacterBase*)draw;
        rejection = ActorRejection.UnsupportedModel;
        if (model->Skeleton == null || model->GetModelType() is not
            (CharacterBase.ModelType.Human or CharacterBase.ModelType.DemiHuman or CharacterBase.ModelType.Monster)) return false;
        // Ordinary characters have a Scene.Object world container as parent. A non-null parent
        // alone does NOT mean a rider/attachment. Only admit null or that ordinary container.
        rejection = ActorRejection.Attached;
        if (draw->ParentObject != null && (draw->ParentObject->VirtualTable == null ||
            draw->ParentObject->GetObjectType() != ObjectType.Object)) return false;
        var character = (Character*)actor;
        var id = new ActorIdentity(index, address, actor->EntityId, actor->BaseId, actor->LayoutId, actor->OwnerId,
            (byte)actor->ObjectKind, (nint)actor->VirtualTable, (nint)draw, (nint)draw->VirtualTable,
            (nint)model->Skeleton, character->ModelContainer.ModelCharaId,
            (nint)draw->ParentObject, draw->ParentObject == null ? 0 : (nint)draw->ParentObject->VirtualTable);
        var key = ((ulong)actor->EntityId << 32) ^ actor->BaseId ^ ((ulong)(uint)index << 16) ^ actor->LayoutId;
        snapshot = new(id, group, key, (nint)actor->VirtualTable->Update);
        rejection = ActorRejection.None;
        return true;
    }

    internal object InspectLocal()
    {
        var accepted = TryGet(0, out _, out var rejection);
        var address = framework.IsInFrameworkUpdateThread ? objects.GetObjectAddress(0) : 0;
        var actor = (NativeActor*)address;
        var draw = actor == null ? null : actor->DrawObject;
        return new
        {
            Accepted = accepted, Rejection = rejection.ToString(), FrameworkThread = framework.IsInFrameworkUpdateThread,
            SlotPresent = actor != null, ObjectIndex = actor == null ? -1 : actor->ObjectIndex,
            Kind = actor == null ? "none" : actor->ObjectKind.ToString(), DrawPresent = draw != null,
            LoadState = draw == null ? -1 : draw->LoadState, ParentPresent = draw != null && draw->ParentObject != null,
            ParentType = draw == null || draw->ParentObject == null ? "none" : draw->ParentObject->GetObjectType().ToString(),
            ParentIsSelf = draw != null && draw->ParentObject == (FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object*)draw,
        };
    }

    private bool Validate(in ActorSnapshot expected, out DrawObject* draw)
    {
        draw = null;
        if (!TryGet(expected.Identity.Index, out var current) || current.Identity != expected.Identity) return false;
        draw = (DrawObject*)current.Identity.Draw;
        return true;
    }

    public bool TryReadScale(in ActorSnapshot snapshot, out Vector3 scale)
    {
        scale = default;
        if (!Validate(snapshot, out var draw)) return false;
        scale = new Vector3(draw->Scale.X, draw->Scale.Y, draw->Scale.Z);
        return true;
    }

    public bool TryWriteScale(in ActorSnapshot snapshot, Vector3 scale)
        => TryWriteTransform(snapshot, scale, null);

    public bool TryReadRotation(in ActorSnapshot snapshot, out Quaternion rotation)
    {
        rotation = default;
        if (!Validate(snapshot, out var draw)) return false;
        rotation = draw->Rotation;
        return OwnedRotation.Valid(rotation);
    }

    public bool TryWriteRotation(in ActorSnapshot snapshot, Quaternion rotation)
    {
        if (!TryReadScale(snapshot, out var scale)) return false;
        return TryWriteTransform(snapshot, scale, rotation);
    }

    public bool TryWriteTransform(in ActorSnapshot snapshot, Vector3 scale, Quaternion? rotation)
    {
        if (!BounceEngine.ValidScale(scale) || (rotation.HasValue && !OwnedRotation.Valid(rotation.Value)) || !Validate(snapshot, out var draw)) return false;
        if (draw->Scale.X == scale.X && draw->Scale.Y == scale.Y && draw->Scale.Z == scale.Z &&
            (rotation == null || (Quaternion)draw->Rotation == rotation.Value)) return true;
        // World Y is up. Only the render root's scale changes: position and gameplay data stay intact.
        // Equipment/hair models use this CharacterBase skeleton; attached weapons follow its transforms.
        draw->Scale.X = scale.X;
        draw->Scale.Y = scale.Y;
        draw->Scale.Z = scale.Z;
        if (rotation.HasValue) draw->Rotation = rotation.Value;
        draw->NotifyTransformChanged();
        return true;
    }
}
